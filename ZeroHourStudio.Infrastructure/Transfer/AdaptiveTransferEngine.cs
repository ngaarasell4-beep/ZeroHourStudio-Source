using System.Diagnostics;
using System.Text;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Infrastructure.Archives;
using ZeroHourStudio.Infrastructure.Services;

namespace ZeroHourStudio.Infrastructure.Transfer;

/// <summary>
/// طلب نقل وحدة
/// </summary>
public class AdaptiveTransferRequest
{
    public string UnitName { get; set; } = string.Empty;
    public string SourceModPath { get; set; } = string.Empty;
    public string TargetModPath { get; set; } = string.Empty;
    public string TargetFaction { get; set; } = string.Empty;
    public string SourceFaction { get; set; } = string.Empty;
    public UnitDependencyGraph? DependencyGraph { get; set; }
    public Dictionary<string, string>? UnitData { get; set; }
    public Dictionary<string, string>? RenameMap { get; set; }
}

/// <summary>
/// مرحلة النقل الحالية
/// </summary>
public class AdaptiveTransferProgress
{
    public string Stage { get; set; } = string.Empty;
    public int Percentage { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public int FilesProcessed { get; set; }
    public int TotalFiles { get; set; }
}

/// <summary>
/// نتيجة التحقق بعد النقل
/// </summary>
public class TransferValidation
{
    public bool Success { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public int MissingReferences { get; set; }
    public string Summary => Success
        ? $"✅ التحقق ناجح ({Warnings.Count} تحذير)"
        : $"⚠ التحقق فشل: {Errors.Count} خطأ، {Warnings.Count} تحذير";
}

/// <summary>
/// نتيجة النقل التكيفي الكاملة
/// </summary>
public class AdaptiveTransferResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public TargetGameProfile? TargetProfile { get; set; }
    public List<TransferConflict> Conflicts { get; set; } = new();
    public BatchMergeResult? IniMergeResult { get; set; }
    public int BinaryFilesCopied { get; set; }
    public TransferValidation? Validation { get; set; }
    public TimeSpan Duration { get; set; }
    public TransferJournalEntry? JournalEntry { get; set; }

    public string Summary => Success
        ? $"✅ تم النقل بنجاح — {IniMergeResult?.AddedCount ?? 0} بلوك مدمج، {BinaryFilesCopied} ملف ثنائي ({Duration.TotalSeconds:F1}ث)"
        : $"✗ فشل النقل: {Message}";
}

/// <summary>
/// محرك النقل التكيفي — الجيل الجديد من نظام النقل
/// يجمع بين: تحليل الهدف + فحص التعارضات + دمج INI ذكي + نسخ ثنائي + تحقق
/// </summary>
public class AdaptiveTransferEngine
{
    private readonly SageIniMerger _iniMerger = new();
    private readonly GameTargetAnalyzer _targetAnalyzer = new();

    /// <summary>
    /// تنفيذ نقل تكيفي كامل
    /// </summary>
    public async Task<AdaptiveTransferResult> TransferAsync(
        AdaptiveTransferRequest request,
        TargetGameProfile? cachedProfile = null,
        IProgress<AdaptiveTransferProgress>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        var result = new AdaptiveTransferResult();
        var archiveManagers = new Dictionary<string, BigArchiveManager>(StringComparer.OrdinalIgnoreCase);

        // === بدء التسجيل ===
        var journal = new TransferJournal(request.TargetModPath);
        var journalEntry = journal.BeginTransfer(
            request.UnitName, request.SourceFaction,
            request.TargetFaction, request.SourceModPath, request.TargetModPath);

        try
        {
            // =============================================
            // المرحلة 1: تحليل الهدف
            // =============================================
            Report(progress, "تحليل المود الهدف...", 0);

            var targetProfile = cachedProfile
                ?? await _targetAnalyzer.AnalyzeAsync(request.TargetModPath);
            result.TargetProfile = targetProfile;

            Debug.WriteLine($"[AdaptiveTransfer] Target: {targetProfile.Summary}");

            // =============================================
            // المرحلة 2: تصنيف الملفات
            // =============================================
            Report(progress, "تصنيف الملفات...", 10);

            var graph = request.DependencyGraph;
            if (graph == null)
            {
                result.Success = false;
                result.Message = "لا يوجد رسم بياني للتبعيات";
                return result;
            }

            var foundNodes = graph.AllNodes
                .Where(n => n.Status == AssetStatus.Found && n.FullPath != null)
                .ToList();

            // تصنيف: ملفات INI مشتركة vs ملفات ثنائية vs ملفات INI خاصة
            var sharedIniNodes = new List<DependencyNode>();
            var privateIniNodes = new List<DependencyNode>();
            var binaryNodes = new List<DependencyNode>();

            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in foundNodes.OrderBy(n => n.Depth))
            {
                var relPath = GetRelativePath(node.FullPath!, request.SourceModPath);
                var targetPath = Path.Combine(request.TargetModPath, relPath);

                if (!seenTargets.Add(targetPath))
                    continue; // تخطي المكررات

                var ext = Path.GetExtension(node.Name).ToLowerInvariant();
                var fileName = Path.GetFileName(targetPath);

                if (ext == ".ini")
                {
                    if (IsSharedIniFile(fileName) && File.Exists(targetPath))
                        sharedIniNodes.Add(node);
                    else
                        privateIniNodes.Add(node);
                }
                else
                {
                    binaryNodes.Add(node);
                }
            }

            Debug.WriteLine($"[AdaptiveTransfer] Files: {sharedIniNodes.Count} shared INI, {privateIniNodes.Count} private INI, {binaryNodes.Count} binary");

            // =============================================
            // المرحلة 3: فحص التعارضات
            // =============================================
            Report(progress, "فحص التعارضات...", 20);

            // تحليل بلوكات INI المصدر لفحص التعارضات
            var sourceSections = new List<IniSection>();
            foreach (var node in sharedIniNodes)
            {
                try
                {
                    var content = await ReadSourceContent(node.FullPath!, request.SourceModPath, archiveManagers);
                    var parsed = _iniMerger.ParseContent(content);
                    sourceSections.AddRange(parsed.Sections);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AdaptiveTransfer] Parse error for {node.Name}: {ex.Message}");
                }
            }

            result.Conflicts = _targetAnalyzer.CheckConflicts(sourceSections, targetProfile);
            Debug.WriteLine($"[AdaptiveTransfer] Conflicts: {result.Conflicts.Count}");

            // =============================================
            // المرحلة 4: دمج ذكي لملفات INI المشتركة
            // =============================================
            Report(progress, "دمج ملفات INI...", 30);

            var allMergeResults = new BatchMergeResult { Success = true };
            int iniProcessed = 0;

            foreach (var node in sharedIniNodes)
            {
                iniProcessed++;
                Report(progress, $"دمج: {node.Name}", 30 + (iniProcessed * 30 / Math.Max(sharedIniNodes.Count, 1)));

                try
                {
                    var relPath = GetRelativePath(node.FullPath!, request.SourceModPath);
                    var targetPath = Path.Combine(request.TargetModPath, relPath);

                    // نسخة احتياطية
                    if (File.Exists(targetPath))
                        journal.RecordIniModification(journalEntry, targetPath, $"دمج ذكي: {node.Name}");

                    // استخراج محتوى المصدر
                    var sourceContent = await ReadSourceContent(node.FullPath!, request.SourceModPath, archiveManagers);

                    // تحليل ودمج
                    var targetIni = _iniMerger.Parse(targetPath);
                    var sourceIni = _iniMerger.ParseContent(sourceContent);

                    var mergeResult = _iniMerger.MergeBatch(targetIni, sourceIni.Sections, MergeStrategy.Smart);

                    if (mergeResult.AddedCount > 0 || mergeResult.ReplacedCount > 0)
                    {
                        _iniMerger.Write(targetIni, targetPath);
                        Debug.WriteLine($"[AdaptiveTransfer] Merged '{node.Name}': {mergeResult.Summary}");
                    }

                    // تجميع النتائج
                    allMergeResults.AddedCount += mergeResult.AddedCount;
                    allMergeResults.SkippedCount += mergeResult.SkippedCount;
                    allMergeResults.RenamedCount += mergeResult.RenamedCount;
                    allMergeResults.ReplacedCount += mergeResult.ReplacedCount;
                    allMergeResults.MergedCount += mergeResult.MergedCount;
                    allMergeResults.ErrorCount += mergeResult.ErrorCount;
                    allMergeResults.TotalSections += mergeResult.TotalSections;
                }
                catch (Exception ex)
                {
                    allMergeResults.ErrorCount++;
                    allMergeResults.Errors.Add($"{node.Name}: {ex.Message}");
                    Debug.WriteLine($"[AdaptiveTransfer] Merge error '{node.Name}': {ex.Message}");
                }
            }

            result.IniMergeResult = allMergeResults;

            // =============================================
            // المرحلة 5: نسخ ملفات INI الخاصة
            // =============================================
            Report(progress, "نسخ ملفات INI الخاصة...", 60);

            foreach (var node in privateIniNodes)
            {
                try
                {
                    var relPath = GetRelativePath(node.FullPath!, request.SourceModPath);
                    var targetPath = Path.Combine(request.TargetModPath, relPath);

                    EnsureDirectory(targetPath);
                    await CopyNodeToTarget(node, targetPath, request.SourceModPath, archiveManagers);
                    journal.RecordFileCopy(journalEntry, node.FullPath!, targetPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AdaptiveTransfer] Copy error '{node.Name}': {ex.Message}");
                    allMergeResults.Errors.Add($"نسخ {node.Name}: {ex.Message}");
                }
            }

            // =============================================
            // المرحلة 6: نسخ الملفات الثنائية (W3D, TGA, إلخ)
            // =============================================
            Report(progress, "نسخ الملفات الثنائية...", 70);

            int binaryProcessed = 0;
            foreach (var node in binaryNodes)
            {
                binaryProcessed++;
                Report(progress, $"نسخ: {node.Name}", 70 + (binaryProcessed * 20 / Math.Max(binaryNodes.Count, 1)));

                try
                {
                    var relPath = GetRelativePath(node.FullPath!, request.SourceModPath);
                    var targetPath = Path.Combine(request.TargetModPath, relPath);

                    EnsureDirectory(targetPath);
                    await CopyNodeToTarget(node, targetPath, request.SourceModPath, archiveManagers);
                    journal.RecordFileCopy(journalEntry, node.FullPath!, targetPath);
                    result.BinaryFilesCopied++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AdaptiveTransfer] Binary copy error '{node.Name}': {ex.Message}");
                    allMergeResults.Errors.Add($"نسخ ثنائي {node.Name}: {ex.Message}");
                }
            }

            // =============================================
            // المرحلة 7: التحقق من النتيجة
            // =============================================
            Report(progress, "التحقق من النتيجة...", 90);

            result.Validation = ValidateTransfer(request, targetProfile);

            // =============================================
            // حفظ السجل
            // =============================================
            await journal.SaveEntryAsync(journalEntry);
            result.JournalEntry = journalEntry;

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Success = allMergeResults.ErrorCount == 0;
            result.Message = result.Success
                ? $"تم النقل التكيفي بنجاح ({sw.Elapsed.TotalSeconds:F1}ث)"
                : $"اكتمل مع {allMergeResults.ErrorCount} خطأ";

            Report(progress, "اكتمل", 100);
            Debug.WriteLine($"[AdaptiveTransfer] Complete: {result.Summary}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Success = false;
            result.Duration = sw.Elapsed;
            result.Message = $"خطأ: {ex.Message}";
            Debug.WriteLine($"[AdaptiveTransfer] FATAL: {ex.Message}");
        }
        finally
        {
            foreach (var mgr in archiveManagers.Values)
                mgr.Dispose();
        }

        return result;
    }

    // =========================================
    // === أدوات مساعدة ===
    // =========================================

    private static readonly HashSet<string> _sharedIniFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Weapon.ini", "Armor.ini", "FXList.ini", "DamageFX.ini",
        "ObjectCreationList.ini", "Locomotor.ini", "CommandSet.ini",
        "CommandButton.ini", "SpecialPower.ini", "Upgrade.ini",
        "Science.ini", "ExperienceLevel.ini", "Crate.ini",
        "SoundEffects.ini", "Object.ini",
    };

    private static bool IsSharedIniFile(string fileName)
        => _sharedIniFiles.Contains(fileName);

    private static void Report(IProgress<AdaptiveTransferProgress>? progress, string stage, int pct)
    {
        progress?.Report(new AdaptiveTransferProgress { Stage = stage, Percentage = pct });
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static string GetRelativePath(string fullPath, string sourcePath)
    {
        if (fullPath.Contains("::", StringComparison.Ordinal))
        {
            var parts = fullPath.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1
                ? parts[1].Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar)
                : Path.GetFileName(fullPath);
        }

        try
        {
            return Path.GetRelativePath(sourcePath, fullPath);
        }
        catch
        {
            return Path.GetFileName(fullPath);
        }
    }

    /// <summary>
    /// قراءة محتوى ملف مصدر (من ملف عادي أو أرشيف BIG)
    /// </summary>
    private static async Task<string> ReadSourceContent(
        string sourcePath,
        string modPath,
        Dictionary<string, BigArchiveManager> archiveManagers)
    {
        if (sourcePath.Contains("::", StringComparison.Ordinal))
        {
            var parts = sourcePath.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new InvalidOperationException($"مرجع أرشيف غير صالح: {sourcePath}");

            if (!archiveManagers.TryGetValue(parts[0], out var mgr))
            {
                mgr = new BigArchiveManager(parts[0]);
                await mgr.LoadAsync();
                archiveManagers[parts[0]] = mgr;
            }

            var data = await mgr.ExtractFileAsync(parts[1]);
            return Encoding.GetEncoding(1252).GetString(data);
        }

        return await File.ReadAllTextAsync(sourcePath, Encoding.GetEncoding(1252));
    }

    /// <summary>
    /// نسخ ملف من المصدر إلى الهدف (يدعم الأرشيفات)
    /// </summary>
    private static async Task CopyNodeToTarget(
        DependencyNode node,
        string targetPath,
        string sourceModPath,
        Dictionary<string, BigArchiveManager> archiveManagers)
    {
        var sourcePath = node.FullPath!;

        if (sourcePath.Contains("::", StringComparison.Ordinal))
        {
            var parts = sourcePath.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new InvalidOperationException($"مرجع أرشيف غير صالح: {sourcePath}");

            if (!archiveManagers.TryGetValue(parts[0], out var mgr))
            {
                mgr = new BigArchiveManager(parts[0]);
                await mgr.LoadAsync();
                archiveManagers[parts[0]] = mgr;
            }

            var data = await mgr.ExtractFileAsync(parts[1]);
            await File.WriteAllBytesAsync(targetPath, data);
        }
        else
        {
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
            await source.CopyToAsync(target);
        }
    }

    /// <summary>
    /// التحقق من سلامة النقل
    /// </summary>
    private TransferValidation ValidateTransfer(
        AdaptiveTransferRequest request,
        TargetGameProfile targetProfile)
    {
        var validation = new TransferValidation { Success = true };

        try
        {
            // فحص وجود ملف Object INI للوحدة
            var objectDirs = new[]
            {
                Path.Combine(request.TargetModPath, "Data", "INI", "Object"),
                Path.Combine(request.TargetModPath, "Data", "INI"),
            };

            bool unitFound = false;
            foreach (var dir in objectDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*.ini", SearchOption.AllDirectories))
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        if (content.Contains($"Object {request.UnitName}", StringComparison.OrdinalIgnoreCase))
                        {
                            unitFound = true;
                            break;
                        }
                    }
                    catch { }
                }
                if (unitFound) break;
            }

            if (!unitFound)
            {
                validation.Warnings.Add($"تعريف الوحدة '{request.UnitName}' لم يُعثر عليه في ملفات الهدف");
            }

            // فحص ملفات W3D
            var artDir = Path.Combine(request.TargetModPath, "Art", "W3D");
            if (request.UnitData != null && request.UnitData.TryGetValue("Model", out var modelName))
            {
                if (!string.IsNullOrWhiteSpace(modelName))
                {
                    var w3dPath = Path.Combine(artDir, modelName);
                    if (!File.Exists(w3dPath) && !File.Exists(w3dPath + ".W3D"))
                    {
                        validation.Warnings.Add($"نموذج W3D '{modelName}' غير موجود في الهدف");
                        validation.MissingReferences++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            validation.Errors.Add($"خطأ في التحقق: {ex.Message}");
            validation.Success = false;
        }

        return validation;
    }
}
