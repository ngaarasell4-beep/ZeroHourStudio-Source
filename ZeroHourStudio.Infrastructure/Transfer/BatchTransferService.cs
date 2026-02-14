using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.ConflictResolution;

namespace ZeroHourStudio.Infrastructure.Transfer;

/// <summary>
/// حالة نقل وحدة واحدة في عملية الدفعة
/// </summary>
public enum BatchUnitStatus
{
    Pending,
    Analyzing,
    Transferring,
    Succeeded,
    Failed,
    Skipped
}

/// <summary>
/// نتيجة نقل وحدة واحدة ضمن عملية الدفعة
/// </summary>
public class BatchUnitResult
{
    public string UnitName { get; set; } = string.Empty;
    public BatchUnitStatus Status { get; set; } = BatchUnitStatus.Pending;
    public string StatusMessage { get; set; } = string.Empty;
    public int ConflictsDetected { get; set; }
    public int ConflictsResolved { get; set; }
    public int FilesTransferred { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }

    public string StatusIcon => Status switch
    {
        BatchUnitStatus.Pending => "⏳",
        BatchUnitStatus.Analyzing => "🔍",
        BatchUnitStatus.Transferring => "📦",
        BatchUnitStatus.Succeeded => "✅",
        BatchUnitStatus.Failed => "❌",
        BatchUnitStatus.Skipped => "⏭",
        _ => "⏳"
    };
}

/// <summary>
/// تقرير عملية النقل الدفعية الشاملة
/// </summary>
public class BatchTransferReport
{
    public List<BatchUnitResult> UnitResults { get; set; } = new();
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime EndTime { get; set; }
    public TimeSpan TotalDuration => EndTime - StartTime;

    public int TotalUnits => UnitResults.Count;
    public int SucceededCount => UnitResults.Count(u => u.Status == BatchUnitStatus.Succeeded);
    public int FailedCount => UnitResults.Count(u => u.Status == BatchUnitStatus.Failed);
    public int SkippedCount => UnitResults.Count(u => u.Status == BatchUnitStatus.Skipped);
    public int TotalFilesTransferred => UnitResults.Sum(u => u.FilesTransferred);
    public int TotalConflicts => UnitResults.Sum(u => u.ConflictsDetected);

    public double SuccessRate => TotalUnits > 0 ? (SucceededCount * 100.0) / TotalUnits : 0;

    public string Summary =>
        $"✅ {SucceededCount} ناجح | ❌ {FailedCount} فاشل | ⏭ {SkippedCount} مُتجاوز | " +
        $"📄 {TotalFilesTransferred} ملف | ⏱ {TotalDuration.TotalSeconds:F1}ث";
}

/// <summary>
/// تقدم عملية النقل الدفعية
/// </summary>
public class BatchTransferProgress
{
    public int CurrentUnitIndex { get; set; }
    public int TotalUnits { get; set; }
    public string CurrentUnitName { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public double OverallPercentage => TotalUnits > 0 ? (CurrentUnitIndex * 100.0) / TotalUnits : 0;
}

/// <summary>
/// طلب نقل دفعي
/// </summary>
public class BatchTransferRequest
{
    public List<SageUnit> Units { get; set; } = new();
    public string SourceModPath { get; set; } = string.Empty;
    public string TargetModPath { get; set; } = string.Empty;
    public string TargetFaction { get; set; } = string.Empty;

    /// <summary>هل نتخطى الوحدات التي بها تعارضات حرجة</summary>
    public bool SkipCriticalConflicts { get; set; } = true;

    /// <summary>هل نطبق إعادة التسمية التلقائية</summary>
    public bool AutoRename { get; set; } = true;

    /// <summary>Callback لاسترجاع بيانات الوحدة</summary>
    public Func<string, Dictionary<string, string>?>? UnitDataProvider { get; set; }

    /// <summary>Callback لاسترجاع مسار INI</summary>
    public Func<string, string?>? UnitIniPathProvider { get; set; }
}

/// <summary>
/// واجهة مجردة لخط النقل - تُستخدم من BatchTransferService لتجنب التبعية الدائرية
/// </summary>
public interface IBatchPipeline
{
    Task<UnitDependencyGraph> AnalyzeDependenciesAsync(SageUnit unit, string sourceModPath, string? unitIniPath, Dictionary<string, string>? unitData);
    Task<ConflictReport> DetectConflictsAsync(UnitDependencyGraph graph, string targetModPath);
    Task<BatchPipelineResult> ExecuteTransferAsync(SageUnit unit, UnitDependencyGraph graph, string sourceModPath, string targetModPath, string targetFaction, Dictionary<string, string>? renameMap, Dictionary<string, string>? unitData);
}

public class BatchPipelineResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalFilesTransferred { get; set; }
}

/// <summary>
/// خدمة النقل الدفعي - نقل عدة وحدات دفعة واحدة
/// </summary>
public class BatchTransferService
{
    private readonly IBatchPipeline _pipeline;
    private readonly ConflictIntelligenceEngine _intelligence = new();

    public BatchTransferService(IBatchPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <summary>
    /// تنفيذ عملية نقل دفعية
    /// </summary>
    public async Task<BatchTransferReport> ExecuteBatchAsync(
        BatchTransferRequest request,
        IProgress<BatchTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = new BatchTransferReport();

        for (int i = 0; i < request.Units.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var unit = request.Units[i];
            var unitResult = new BatchUnitResult { UnitName = unit.TechnicalName };
            report.UnitResults.Add(unitResult);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // === مرحلة التحليل ===
                unitResult.Status = BatchUnitStatus.Analyzing;
                progress?.Report(new BatchTransferProgress
                {
                    CurrentUnitIndex = i,
                    TotalUnits = request.Units.Count,
                    CurrentUnitName = unit.TechnicalName,
                    Phase = "تحليل"
                });

                var unitData = request.UnitDataProvider?.Invoke(unit.TechnicalName);
                var unitIniPath = request.UnitIniPathProvider?.Invoke(unit.TechnicalName);

                var graph = await _pipeline.AnalyzeDependenciesAsync(
                    unit, request.SourceModPath, unitIniPath, unitData);

                var conflicts = await _pipeline.DetectConflictsAsync(graph, request.TargetModPath);
                unitResult.ConflictsDetected = conflicts.Conflicts.Count;

                // تحليل ذكي للتعارضات
                if (conflicts.HasConflicts)
                {
                    var diagnoses = _intelligence.DiagnoseConflicts(conflicts, graph);
                    var criticalCount = diagnoses.Count(d => d.Severity == ConflictSeverity.Critical);

                    if (request.SkipCriticalConflicts && criticalCount > 0)
                    {
                        unitResult.Status = BatchUnitStatus.Skipped;
                        unitResult.StatusMessage = $"تم التجاوز - {criticalCount} تعارض حرج";
                        sw.Stop();
                        unitResult.Duration = sw.Elapsed;
                        continue;
                    }
                }

                // === مرحلة النقل ===
                unitResult.Status = BatchUnitStatus.Transferring;
                progress?.Report(new BatchTransferProgress
                {
                    CurrentUnitIndex = i,
                    TotalUnits = request.Units.Count,
                    CurrentUnitName = unit.TechnicalName,
                    Phase = "نقل"
                });

                // إعادة تسمية تلقائية
                Dictionary<string, string>? renameMap = null;
                if (request.AutoRename && conflicts.HasConflicts)
                {
                    renameMap = new Dictionary<string, string>();
                    foreach (var conflict in conflicts.Conflicts.Where(c => c.Kind == ConflictKind.Duplicate))
                    {
                        renameMap[conflict.DefinitionName] = $"ZH_{conflict.DefinitionName}";
                    }
                    unitResult.ConflictsResolved = renameMap.Count;
                }

                var pipelineResult = await _pipeline.ExecuteTransferAsync(
                    unit, graph, request.SourceModPath, request.TargetModPath,
                    request.TargetFaction, renameMap, unitData);

                if (pipelineResult.Success)
                {
                    unitResult.Status = BatchUnitStatus.Succeeded;
                    unitResult.FilesTransferred = pipelineResult.TotalFilesTransferred;
                    unitResult.StatusMessage = pipelineResult.Message;
                }
                else
                {
                    unitResult.Status = BatchUnitStatus.Failed;
                    unitResult.ErrorMessage = pipelineResult.Message;
                    unitResult.StatusMessage = $"فشل: {pipelineResult.Message}";
                }
            }
            catch (Exception ex)
            {
                unitResult.Status = BatchUnitStatus.Failed;
                unitResult.ErrorMessage = ex.Message;
                unitResult.StatusMessage = $"خطأ: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                unitResult.Duration = sw.Elapsed;
            }
        }

        report.EndTime = DateTime.UtcNow;
        return report;
    }
}
