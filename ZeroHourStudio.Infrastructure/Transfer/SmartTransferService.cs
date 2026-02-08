using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Archives;
using ZeroHourStudio.Infrastructure.Logging;
using ZeroHourStudio.Infrastructure.Implementations;
using System.Diagnostics;

namespace ZeroHourStudio.Infrastructure.Transfer;

/// <summary>
/// خدمة النقل الذكي
/// تدير عملية نقل الوحدات والملفات بكفاءة عالية
/// تدعم الاسترداد التلقائي والعمليات الذرية
/// </summary>
public class SmartTransferService : ISmartTransferService
{
    private const int BufferSize = 64 * 1024; // 64KB buffer
    private readonly IBigFileReader? _gameArchiveReader;

    public SmartTransferService()
    {
        _gameArchiveReader = null;
    }

    public SmartTransferService(IBigFileReader? gameArchiveReader)
    {
        _gameArchiveReader = gameArchiveReader;
    }

    /// <summary>
    /// نقل وحدة وجميع تبعياتها بذكاء مع دعم الاسترداد التلقائي
    /// </summary>
    public async Task<SmartTransferResult> TransferAsync(
        UnitDependencyGraph graph,
        string sourcePath,
        string destinationPath,
        IProgress<TransferProgress>? progress = null)
    {
        var startTime = DateTime.UtcNow;
        var transferSw = Stopwatch.StartNew();
        var result = new SmartTransferResult();
        var transferProgress = new TransferProgress();
        var createdFiles = new List<string>();
        var createdDirectories = new List<string>();
        var backups = new List<(string Original, string Backup)>();
        var archiveManagers = new Dictionary<string, BigArchiveManager>(StringComparer.OrdinalIgnoreCase);
        var logger = new SimpleLogger("transfer_errors.log");

        try
        {
            // 1. التحقق من المسارات
            if (!Directory.Exists(sourcePath))
            {
                result.Success = false;
                result.Message = $"مسار المصدر غير موجود: {sourcePath}";
                logger.LogError($"[Transfer] Source path missing: {sourcePath}");
                return result;
            }

            // 2. إنشاء مجلد الهدف إن لم يكن موجوداً
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
                logger.LogInfo($"[Transfer] Created target directory: {destinationPath}");
            }

            // 3. الحصول على قائمة الملفات للنقل
            var filesToTransfer = graph.AllNodes
                .Where(n => n.Status == AssetStatus.Found && n.FullPath != null)
                .OrderBy(n => n.Depth)
                .ToList();

            // 3b. Auto-recovery: try to find missing files in source BIG archives
            var missingNodes = graph.AllNodes
                .Where(n => n.Status == AssetStatus.Missing && !string.IsNullOrWhiteSpace(n.Name))
                .ToList();

            if (missingNodes.Count > 0 && _gameArchiveReader != null)
            {
                foreach (var missingNode in missingNodes)
                {
                    try
                    {
                        var exists = await _gameArchiveReader.FileExistsAsync(sourcePath, missingNode.Name);
                        if (exists)
                        {
                            missingNode.Status = AssetStatus.Found;
                            missingNode.FullPath = $"__ARCHIVE_RECOVERY__::{missingNode.Name}";
                            filesToTransfer.Add(missingNode);
                            result.RecoveredFilesCount++;
                        }
                    }
                    catch { }
                }
            }

            BlackBoxRecorder.RecordTransferStart(
                graph.UnitName ?? "unknown", sourcePath, destinationPath, filesToTransfer.Count);

            if (filesToTransfer.Count == 0)
            {
                result.Success = false;
                result.Message = "لا توجد ملفات للنقل";
                logger.LogWarning("[Transfer] No files to transfer (graph empty or missing)");
                BlackBoxRecorder.RecordTransferEnd(graph.UnitName ?? "unknown", false, 0, 0, 0, "No files");
                return result;
            }

            // 4. Pre-flight health check: verify all source files are accessible
            var healthCheckFailed = new List<string>();
            foreach (var node in filesToTransfer)
            {
                if (node.FullPath == null) continue;
                if (node.FullPath.Contains("::", StringComparison.Ordinal)) continue; // Archive refs checked during extraction
                if (!File.Exists(node.FullPath))
                    healthCheckFailed.Add(node.Name);
            }

            if (healthCheckFailed.Count > 0)
            {
                result.Success = false;
                result.Message = $"فحص السلامة فشل: {healthCheckFailed.Count} ملف غير موجود";
                result.FailedFiles.AddRange(healthCheckFailed);
                logger.LogError($"[Transfer] Health check failed: {string.Join(", ", healthCheckFailed)}");
                return result;
            }

            // 5. حساب الحجم الإجمالي
            transferProgress.TotalFiles = filesToTransfer.Count;
            transferProgress.TotalBytes = filesToTransfer.Sum(f => f.SizeInBytes ?? 0);

            // 6. بدء عملية النقل الذرية
            for (int i = 0; i < filesToTransfer.Count; i++)
            {
                var fileNode = filesToTransfer[i];
                transferProgress.CurrentFileIndex = i + 1;
                transferProgress.CurrentFileName = fileNode.Name;

                try
                {
                    var sourceFullPath = fileNode.FullPath ?? throw new InvalidOperationException($"ملف بدون مسار: {fileNode.Name}");
                    var relativePath = GetRelativePath(sourceFullPath, sourcePath);
                    var targetPath = Path.Combine(destinationPath, relativePath);
                    var tempTargetPath = targetPath + ".zhs_tmp";

                    // إنشاء المجلد الهدف إن لزم الأمر
                    var targetDirectory = Path.GetDirectoryName(targetPath);
                    if (targetDirectory != null && !Directory.Exists(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                        createdDirectories.Add(targetDirectory);
                    }

                    // حفظ نسخة احتياطية في حال كان الملف موجوداً
                    if (File.Exists(targetPath))
                    {
                        var backupPath = targetPath + $".bak_{Guid.NewGuid():N}";
                        File.Move(targetPath, backupPath);
                        backups.Add((targetPath, backupPath));
                    }

                    // نسخ الملف إلى مسار مؤقت ثم استبداله
                    if (sourceFullPath.StartsWith("__ARCHIVE_RECOVERY__::", StringComparison.Ordinal))
                    {
                        // Auto-recovery extraction from game archives
                        var recoveryName = sourceFullPath.Replace("__ARCHIVE_RECOVERY__::", "");
                        var recoveryTarget = Path.Combine(destinationPath, "Data", relativePath);
                        var recoveryDir = Path.GetDirectoryName(recoveryTarget);
                        if (recoveryDir != null && !Directory.Exists(recoveryDir))
                            Directory.CreateDirectory(recoveryDir);
                        if (_gameArchiveReader != null)
                            await _gameArchiveReader.ExtractAsync(sourcePath, recoveryName, tempTargetPath);
                    }
                    else if (sourceFullPath.Contains("::", StringComparison.Ordinal))
                    {
                        await ExtractFromArchiveAsync(sourceFullPath, tempTargetPath, archiveManagers, transferProgress, progress);
                    }
                    else
                    {
                        await CopyFileAsync(sourceFullPath, tempTargetPath, transferProgress, progress);
                    }

                    File.Move(tempTargetPath, targetPath, overwrite: true);

                    if (!backups.Any(b => b.Original.Equals(targetPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        createdFiles.Add(targetPath);
                    }

                    result.TransferredFilesCount++;
                    result.TransferredBytesCount += fileNode.SizeInBytes ?? 0;
                    BlackBoxRecorder.RecordTransferFile(fileNode.Name, true, fileNode.SizeInBytes ?? 0);
                }
                catch (Exception ex)
                {
                    var failMsg = $"{fileNode.Name}: {ex.Message}";
                    result.FailedFiles.Add(failMsg);
                    logger.LogError($"[Transfer] Failed file: {failMsg}", ex);

                    BlackBoxRecorder.RecordTransferFile(fileNode.Name, false, 0, ex.Message);
                    // Atomic: any failure triggers immediate rollback
                    Rollback(createdFiles, backups);
                    BlackBoxRecorder.RecordTransferRollback(fileNode.Name, createdFiles.Count);
                    result.Success = false;
                    result.Message = $"تم التراجع عن النقل بسبب فشل: {fileNode.Name}";
                    result.Duration = DateTime.UtcNow - startTime;
                    transferSw.Stop();
                    BlackBoxRecorder.RecordTransferEnd(graph.UnitName ?? "unknown", false, result.TransferredFilesCount, result.FailedFiles.Count, transferSw.ElapsedMilliseconds, result.Message);
                    return result;
                }
            }

            result.Success = true;
            result.Message = $"تم نقل {result.TransferredFilesCount} ملف بنجاح";
            if (result.RecoveredFilesCount > 0)
                result.Message += $" (استرداد {result.RecoveredFilesCount} ملف تلقائياً)";

            CleanupBackups(backups);
            result.Duration = DateTime.UtcNow - startTime;
            transferSw.Stop();
            BlackBoxRecorder.RecordTransferEnd(graph.UnitName ?? "unknown", true, result.TransferredFilesCount, result.FailedFiles.Count, transferSw.ElapsedMilliseconds, result.Message);

            logger.LogInfo($"[Transfer] Completed successfully in {result.Duration.TotalSeconds:F2} seconds");
            logger.LogInfo($"[Transfer] Transferred {result.TransferredFilesCount} files ({result.TransferredBytesCount} bytes)");
            if (result.RecoveredFilesCount > 0)
                logger.LogInfo($"[Transfer] Recovered {result.RecoveredFilesCount} files");
        }
        catch (Exception ex)
        {
            Rollback(createdFiles, backups);
            result.Success = false;
            result.Message = $"خطأ في عملية النقل (تم التراجع): {ex.Message}";
            logger.LogError("خطأ في عملية النقل", ex);
        }
        finally
        {
            foreach (var manager in archiveManagers.Values)
            {
                manager.Dispose();
            }
        }

        return result;
    }

    private static async Task ExtractFromArchiveAsync(
        string archiveReference,
        string destinationFile,
        Dictionary<string, BigArchiveManager> managers,
        TransferProgress progress,
        IProgress<TransferProgress>? progressReporter)
    {
        var parts = archiveReference.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new InvalidOperationException($"مرجع أرشيف غير صالح: {archiveReference}");

        var archivePath = parts[0];
        var entryPath = parts[1];

        if (!managers.TryGetValue(archivePath, out var manager))
        {
            manager = new BigArchiveManager(archivePath);
            await manager.LoadAsync();
            managers[archivePath] = manager;
        }

        var data = await manager.ExtractFileAsync(entryPath);
        await File.WriteAllBytesAsync(destinationFile, data);
        progress.TransferredBytes += data.Length;
        progressReporter?.Report(progress);
    }

    private static void Rollback(List<string> createdFiles, List<(string Original, string Backup)> backups)
    {
        foreach (var created in createdFiles)
        {
            try
            {
                if (File.Exists(created))
                    File.Delete(created);
            }
            catch { }
        }

        foreach (var (original, backup) in backups)
        {
            try
            {
                if (File.Exists(backup))
                {
                    if (File.Exists(original))
                        File.Delete(original);
                    File.Move(backup, original);
                }
            }
            catch { }
        }
    }

    private static void CleanupBackups(List<(string Original, string Backup)> backups)
    {
        foreach (var (_, backup) in backups)
        {
            try
            {
                if (File.Exists(backup))
                    File.Delete(backup);
            }
            catch { }
        }
    }

    /// <summary>
    /// نسخ ملف مع تحديث التقدم
    /// </summary>
    private async Task CopyFileAsync(
        string sourceFile,
        string destinationFile,
        TransferProgress progress,
        IProgress<TransferProgress>? progressReporter)
    {
        using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
        using (var destinationStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            byte[] buffer = new byte[BufferSize];
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await destinationStream.WriteAsync(buffer, 0, bytesRead);
                progress.TransferredBytes += bytesRead;
                progressReporter?.Report(progress);
            }

            try
            {
                File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
            }
            catch { }
        }
    }

    /// <summary>
    /// الحصول على المسار النسبي مع دعم مراجع الأرشيف (archive::entry)
    /// </summary>
    private string GetRelativePath(string fullPath, string basePath)
    {
        if (fullPath.Contains("::", StringComparison.Ordinal))
        {
            var parts = fullPath.Split(new[] { "::" }, 2, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                return parts[1].Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        return Path.GetFileName(fullPath);
    }
}
