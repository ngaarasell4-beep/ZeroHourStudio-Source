using System.IO;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.ConflictResolution;
using ZeroHourStudio.Infrastructure.DependencyResolution;
using ZeroHourStudio.Infrastructure.Localization;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.Transfer;

namespace ZeroHourStudio.UI.WPF.Services;

/// <summary>
/// نتيجة عملية النقل الكاملة v3.0
/// </summary>
public class PipelineResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public ConflictReport? ConflictReport { get; set; }
    public SmartTransferResult? TransferResult { get; set; }
    public List<CsfEntry> GeneratedCsfEntries { get; set; } = new();
    public Dictionary<string, string> AppliedRenames { get; set; } = new();
    public int TotalFilesTransferred { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// خدمة النقل المتكاملة v3.0 - الخط الكامل:
/// تحليل التبعيات → كشف التعارضات → حل التعارضات → نقل الملفات → حقن CommandSet → توليد CSF
/// </summary>
public class TransferPipelineService : ZeroHourStudio.Infrastructure.Transfer.IBatchPipeline
{
    private readonly SmartDependencyResolver _dependencyResolver;
    private readonly SmartTransferService _transferService;
    private readonly ConflictDetectionService _conflictDetection;
    private readonly SmartRenamingService _renamingService;
    private readonly VirtualFileSystem _virtualFs;
    private readonly CsfLocalizationService _csfService;
    private readonly CommandSetPatchService _commandSetPatch;
    private readonly SageDefinitionIndex _sageIndex;

    public TransferPipelineService(
        SmartDependencyResolver dependencyResolver,
        SmartTransferService transferService,
        ConflictDetectionService conflictDetection,
        SmartRenamingService renamingService,
        VirtualFileSystem virtualFs,
        CsfLocalizationService csfService,
        CommandSetPatchService commandSetPatch,
        SageDefinitionIndex sageIndex)
    {
        _dependencyResolver = dependencyResolver;
        _transferService = transferService;
        _conflictDetection = conflictDetection;
        _renamingService = renamingService;
        _virtualFs = virtualFs;
        _csfService = csfService;
        _commandSetPatch = commandSetPatch;
        _sageIndex = sageIndex;
    }

    /// <summary>
    /// المرحلة 1: تحليل التبعيات
    /// </summary>
    public async Task<UnitDependencyGraph> AnalyzeDependenciesAsync(
        SageUnit unit,
        string sourceModPath,
        string? unitIniPath,
        Dictionary<string, string>? unitData)
    {
        _dependencyResolver.SageIndex = _sageIndex;
        var graph = await _dependencyResolver.ResolveDependenciesAsync(
            unit.TechnicalName, sourceModPath, unitIniPath, unitData);
        await _dependencyResolver.ValidateDependenciesAsync(graph, sourceModPath);
        return graph;
    }

    /// <summary>
    /// المرحلة 2: كشف التعارضات
    /// </summary>
    public async Task<ConflictReport> DetectConflictsAsync(
        UnitDependencyGraph graph,
        string targetModPath)
    {
        return await _conflictDetection.DetectConflictsAsync(graph, targetModPath);
    }

    /// <summary>
    /// المرحلة 3: تنفيذ النقل الكامل مع حل التعارضات
    /// </summary>
    public async Task<PipelineResult> ExecuteTransferAsync(
        SageUnit unit,
        UnitDependencyGraph graph,
        string sourceModPath,
        string targetModPath,
        string targetFaction,
        Dictionary<string, string>? renameMap,
        Dictionary<string, string>? unitData,
        IProgress<TransferProgress>? progress = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new PipelineResult();

        // === بدء التسجيل في السجل ===
        var journal = new TransferJournal(targetModPath);
        var journalEntry = journal.BeginTransfer(
            unit.TechnicalName, unit.Side,
            targetFaction, sourceModPath, targetModPath);

        try
        {
            // تأكد من هيكل المجلدات
            _virtualFs.EnsureDirectoryStructure(targetModPath);

            // نقل الملفات
            var transferResult = await _transferService.TransferAsync(
                graph, sourceModPath, targetModPath, progress);
            result.TransferResult = transferResult;
            result.TotalFilesTransferred = transferResult.TransferredFilesCount;

            // === تسجيل الملفات المنسوخة من graph ===
            foreach (var node in graph.AllNodes.Where(n =>
                n.Status == Application.Models.AssetStatus.Found && n.FullPath != null))
            {
                var relativePath = node.FullPath!.Contains("::")
                    ? node.FullPath.Split("::")[1].Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar)
                    : Path.GetRelativePath(sourceModPath, node.FullPath);
                journal.RecordFileCopy(journalEntry,
                    node.FullPath,
                    Path.Combine(targetModPath, relativePath));
            }

            if (!transferResult.Success)
            {
                result.Success = false;
                result.Message = $"فشل النقل: {transferResult.Message}";
                return result;
            }

            // تطبيق إعادة التسمية إن وجدت
            if (renameMap != null && renameMap.Count > 0)
            {
                result.AppliedRenames = renameMap;
                journalEntry.AppliedRenames = renameMap;
                var iniFiles = Directory.EnumerateFiles(targetModPath, "*.ini", SearchOption.AllDirectories);
                foreach (var iniFile in iniFiles)
                {
                    journal.RecordIniModification(journalEntry, iniFile, $"تطبيق إعادة التسمية: {renameMap.Count} تسمية");
                    await _renamingService.ProcessFileAsync(iniFile, renameMap);
                }
            }

            // حقن CommandSet
            if (unitData != null)
            {
                await _commandSetPatch.EnsureCommandSetAsync(unit, unitData, targetModPath, targetFaction);
                var cmdSetPath = Path.Combine(targetModPath, "Data", "INI", "CommandSet.ini");
                if (File.Exists(cmdSetPath))
                    journal.RecordIniModification(journalEntry, cmdSetPath, "حقن CommandSet");
            }

            // توليد مدخلات CSF
            var displayName = unit.TechnicalName;
            result.GeneratedCsfEntries = _csfService.GenerateEntriesForUnit(
                unit.TechnicalName, displayName);

            // محاولة دمج CSF في ملف موجود
            var csfPath = Path.Combine(targetModPath, "Data", "generals.csf");
            if (File.Exists(csfPath))
            {
                journal.RecordIniModification(journalEntry, csfPath, "دمج CSF");
                await _csfService.MergeEntriesAsync(csfPath, result.GeneratedCsfEntries);
            }

            sw.Stop();
            result.Success = true;
            result.Duration = sw.Elapsed;
            result.Message = $"تم نقل {result.TotalFilesTransferred} ملف بنجاح ({sw.Elapsed.TotalSeconds:F1}ث)";

            // === حفظ السجل ===
            await journal.SaveEntryAsync(journalEntry);
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Success = false;
            result.Duration = sw.Elapsed;
            result.Message = $"خطأ: {ex.Message}";
        }

        return result;
    }

    // === IBatchPipeline Adapter ===

    async Task<UnitDependencyGraph> Infrastructure.Transfer.IBatchPipeline.AnalyzeDependenciesAsync(
        SageUnit unit, string sourceModPath, string? unitIniPath, Dictionary<string, string>? unitData)
    {
        return await AnalyzeDependenciesAsync(unit, sourceModPath, unitIniPath, unitData);
    }

    async Task<ConflictReport> Infrastructure.Transfer.IBatchPipeline.DetectConflictsAsync(
        UnitDependencyGraph graph, string targetModPath)
    {
        return await DetectConflictsAsync(graph, targetModPath);
    }

    async Task<Infrastructure.Transfer.BatchPipelineResult> Infrastructure.Transfer.IBatchPipeline.ExecuteTransferAsync(
        SageUnit unit, UnitDependencyGraph graph, string sourceModPath,
        string targetModPath, string targetFaction,
        Dictionary<string, string>? renameMap, Dictionary<string, string>? unitData)
    {
        var pipelineResult = await ExecuteTransferAsync(
            unit, graph, sourceModPath, targetModPath, targetFaction, renameMap, unitData);

        return new Infrastructure.Transfer.BatchPipelineResult
        {
            Success = pipelineResult.Success,
            Message = pipelineResult.Message,
            TotalFilesTransferred = pipelineResult.TotalFilesTransferred
        };
    }
}
