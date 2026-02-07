using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.AssetManagement;
using ZeroHourStudio.Infrastructure.Validation;

namespace ZeroHourStudio.Infrastructure.Services;

/// <summary>
/// خدمة موحدة لتحليل وتحقق من التبعيات الشاملة
/// تجمع بين:
/// - UnitDependencyAnalyzer
/// - AssetReferenceHunter
/// - UnitCompletionValidator
/// </summary>
public class ComprehensiveDependencyService : IDisposable
{
    private readonly UnitDependencyAnalyzer _dependencyAnalyzer;
    private readonly AssetReferenceHunter _assetHunter;
    private readonly UnitCompletionValidator _validator;
    private readonly Dictionary<string, UnitDependencyGraph> _cachedGraphs;

    public ComprehensiveDependencyService(
        UnitDependencyAnalyzer dependencyAnalyzer,
        AssetReferenceHunter assetHunter,
        UnitCompletionValidator validator)
    {
        _dependencyAnalyzer = dependencyAnalyzer ?? throw new ArgumentNullException(nameof(dependencyAnalyzer));
        _assetHunter = assetHunter ?? throw new ArgumentNullException(nameof(assetHunter));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _cachedGraphs = new Dictionary<string, UnitDependencyGraph>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// تحليل شامل لوحدة معينة
    /// 1. بناء رسم بياني للتبعيات
    /// 2. البحث عن الأصول المرتبطة
    /// 3. التحقق من الاكتمال والصحة
    /// </summary>
    public async Task<UnitAnalysisResult> AnalyzeUnitComprehensivelyAsync(
        string unitId,
        string unitName,
        Dictionary<string, string> unitData)
    {
        var result = new UnitAnalysisResult
        {
            UnitId = unitId,
            UnitName = unitName
        };

        try
        {
            // الخطوة 1: بناء رسم بياني التبعيات
            var dependencyGraph = await _dependencyAnalyzer.AnalyzeDependenciesAsync(
                unitId, unitName, unitData);

            result.DependencyGraph = dependencyGraph;

            // الخطوة 2: البحث عن الأصول لكل عقدة في الرسم البياني
            foreach (var node in dependencyGraph.AllNodes)
            {
                var assets = await _assetHunter.FindAssetsAsync(node.Name);
                
                if (assets.Count > 0)
                {
                    node.Status = AssetStatus.Found;
                    node.Dependencies.AddRange(assets);
                }
            }

            // الخطوة 3: التحقق من الاكتمال والصحة
            var validationResult = _validator.ValidateUnitCompletion(unitId, dependencyGraph);
            result.ValidationResult = validationResult;

            // الخطوة 4: تحديد حالة الاكتمال النهائية
            result.CompletionStatus = _validator.EvaluateCompletionStatus(dependencyGraph);

            // تخزين النتيجة (Caching)
            _cachedGraphs[unitId] = dependencyGraph;
        }
        catch (Exception ex)
        {
            result.HasErrors = true;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// تحليل مجموعة من الوحدات
    /// </summary>
    public async Task<List<UnitAnalysisResult>> AnalyzeMultipleUnitsAsync(
        Dictionary<string, (string name, Dictionary<string, string> data)> units)
    {
        var results = new List<UnitAnalysisResult>();

        foreach (var kvp in units)
        {
            var (unitName, unitData) = kvp.Value;
            var result = await AnalyzeUnitComprehensivelyAsync(kvp.Key, unitName, unitData);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// الحصول على رسم بياني مخزن مؤقتاً
    /// </summary>
    public UnitDependencyGraph? GetCachedGraph(string unitId)
    {
        return _cachedGraphs.TryGetValue(unitId, out var graph) ? graph : null;
    }

    /// <summary>
    /// إنشاء تقرير شامل
    /// </summary>
    public string GenerateComprehensiveReport(UnitAnalysisResult analysisResult)
    {
        var report = new System.Text.StringBuilder();

        report.AppendLine("╔═══════════════════════════════════════════════════════╗");
        report.AppendLine("║         تقرير تحليل الوحدة الشامل                    ║");
        report.AppendLine("╚═══════════════════════════════════════════════════════╝");
        report.AppendLine();

        report.AppendLine($"الوحدة: {analysisResult.UnitName} ({analysisResult.UnitId})");
        report.AppendLine($"الحالة: {analysisResult.CompletionStatus}");
        report.AppendLine();

        if (analysisResult.DependencyGraph != null)
        {
            report.AppendLine("📊 إحصائيات الرسم البياني:");
            report.AppendLine($"  - إجمالي العقد: {analysisResult.DependencyGraph.AllNodes.Count}");
            report.AppendLine($"  - الملفات الموجودة: {analysisResult.DependencyGraph.FoundCount}");
            report.AppendLine($"  - الملفات المفقودة: {analysisResult.DependencyGraph.MissingCount}");
            report.AppendLine($"  - العمق الأقصى: {analysisResult.DependencyGraph.MaxDepth}");
            report.AppendLine($"  - نسبة الاكتمال: {analysisResult.DependencyGraph.GetCompletionPercentage():F1}%");
            report.AppendLine();

            // تفاصيل الأصول
            var assetsByType = analysisResult.DependencyGraph.AllNodes
                .GroupBy(n => n.Type)
                .OrderBy(g => g.Key);

            report.AppendLine("📦 الأصول حسب النوع:");
            foreach (var group in assetsByType)
            {
                var foundCount = group.Count(n => n.Status == AssetStatus.Found);
                var totalCount = group.Count();
                report.AppendLine($"  {group.Key,20}: {foundCount,3}/{totalCount,3} ({(double)foundCount/totalCount*100:F1}%)");
            }
            report.AppendLine();
        }

        if (analysisResult.ValidationResult != null)
        {
            report.AppendLine("✓ نتائج التحقق:");
            report.AppendLine(analysisResult.ValidationResult.ToString());
            report.AppendLine();

            if (analysisResult.ValidationResult.Errors.Count > 0)
            {
                report.AppendLine("❌ الأخطاء:");
                foreach (var error in analysisResult.ValidationResult.Errors)
                {
                    report.AppendLine($"  [{error.Severity}] {error.Message}");
                }
                report.AppendLine();
            }

            if (analysisResult.ValidationResult.Warnings.Count > 0)
            {
                report.AppendLine("⚠️ التحذيرات:");
                foreach (var warning in analysisResult.ValidationResult.Warnings)
                {
                    report.AppendLine($"  {warning.Message}");
                }
            }
        }

        return report.ToString();
    }

    /// <summary>
    /// مسح ذاكرة التخزين المؤقت
    /// </summary>
    public void ClearCache()
    {
        _cachedGraphs.Clear();
    }

    public void Dispose()
    {
        _cachedGraphs.Clear();
    }
}

/// <summary>
/// نتيجة التحليل الشامل للوحدة
/// </summary>
public class UnitAnalysisResult
{
    public string UnitId { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public UnitDependencyGraph? DependencyGraph { get; set; }
    public ValidationResult? ValidationResult { get; set; }
    public CompletionStatus CompletionStatus { get; set; } = CompletionStatus.Unknown;
    public bool HasErrors { get; set; } = false;
    public string? ErrorMessage { get; set; }

    public bool IsComplete => CompletionStatus == CompletionStatus.Complete;
    public bool IsPartial => CompletionStatus == CompletionStatus.Partial;
    public bool IsIncomplete => CompletionStatus == CompletionStatus.Incomplete;

    public override string ToString() => 
        $"{UnitName} - {CompletionStatus} (Errors: {(ValidationResult?.Errors.Count ?? 0)})";
}
