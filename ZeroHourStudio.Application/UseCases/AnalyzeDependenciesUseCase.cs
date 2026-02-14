using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using System.Diagnostics;

namespace ZeroHourStudio.Application.UseCases;

/// <summary>
/// Use Case: تحليل تبعيات الوحدة
/// المسؤوليات:
/// - طلب من Infrastructure لتحليل الوحدة
/// - تحويل النتائج إلى DTO للدرجات العليا
/// - معالجة الأخطاء
/// </summary>
public interface IAnalyzeUnitDependenciesUseCase
{
    /// <summary>
    /// تنفيذ حالة الاستخدام
    /// </summary>
    Task<AnalyzeDependenciesResponse> ExecuteAsync(AnalyzeDependenciesRequest request);
}

/// <summary>
/// تنفيذ حالة استخدام تحليل التبعيات
/// </summary>
public class AnalyzeUnitDependenciesUseCase : IAnalyzeUnitDependenciesUseCase
{
    private readonly IUnitDependencyAnalyzer _dependencyAnalyzer;
    private readonly IUnitCompletionValidator _validator;

    public AnalyzeUnitDependenciesUseCase(
        IUnitDependencyAnalyzer dependencyAnalyzer,
        IUnitCompletionValidator validator)
    {
        _dependencyAnalyzer = dependencyAnalyzer ?? throw new ArgumentNullException(nameof(dependencyAnalyzer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }


    public async Task<AnalyzeDependenciesResponse> ExecuteAsync(AnalyzeDependenciesRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new AnalyzeDependenciesResponse();

        try
        {
            // 1. إجراء تحليل التبعيات
            var graph = await _dependencyAnalyzer.AnalyzeDependenciesAsync(
                request.UnitId,
                request.UnitName,
                request.UnitData);

            // 2. التحقق من اكتمال الوحدة
            var validationResult = _validator.ValidateUnitCompletion(request.UnitId, graph);

            // 3. تقييم حالة الاكتمال والنسبة المئوية
            var status = _validator.EvaluateCompletionStatus(graph);
            var percentage = graph.GetCompletionPercentage();

            // 4. بناء الاستجابة
            response.Success = true;
            response.DependencyGraph = graph;
            response.ValidationResult = validationResult;
            response.CompletionStatus = status;
            response.CompletionPercentage = percentage;

            if (request.GenerateReport)
            {
                response.DetailedReport = _validator.GenerateDetailedReport(validationResult, graph);
            }
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.ErrorMessage = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTime = stopwatch.Elapsed;
        }

        return response;
    }
}

/// <summary>
/// طلب تحليل التبعيات
/// </summary>
public class AnalyzeDependenciesRequest
{
    /// <summary>
    /// معرف الوحدة
    /// </summary>
    public string UnitId { get; set; } = string.Empty;

    /// <summary>
    /// اسم الوحدة
    /// </summary>
    public string UnitName { get; set; } = string.Empty;

    /// <summary>
    /// بيانات الوحدة من ملف INI
    /// </summary>
    public Dictionary<string, string> UnitData { get; set; } = new();

    /// <summary>
    /// هل يتم تخزين النتيجة مؤقتاً
    /// </summary>
    public bool CacheResult { get; set; } = true;

    /// <summary>
    /// هل يتم إنشاء تقرير مفصل
    /// </summary>
    public bool GenerateReport { get; set; } = false;

    public override string ToString() => $"AnalyzeDependencies({UnitName})";
}

/// <summary>
/// استجابة تحليل التبعيات
/// </summary>
public class AnalyzeDependenciesResponse
{
    /// <summary>
    /// هل نجحت العملية
    /// </summary>
    public bool Success { get; set; } = false;

    /// <summary>
    /// الرسم البياني للتبعيات
    /// </summary>
    public UnitDependencyGraph? DependencyGraph { get; set; }

    /// <summary>
    /// نتائج التحقق
    /// </summary>
    public ValidationResult? ValidationResult { get; set; }

    /// <summary>
    /// حالة الاكتمال
    /// </summary>
    public CompletionStatus CompletionStatus { get; set; }

    /// <summary>
    /// النسبة المئوية للاكتمال
    /// </summary>
    public double CompletionPercentage { get; set; } = 0;

    /// <summary>
    /// التقرير المفصل (اختياري)
    /// </summary>
    public string? DetailedReport { get; set; }

    /// <summary>
    /// رسالة الخطأ (إن وجدت)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// الوقت المستغرق للتنفيذ
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }

    public override string ToString() =>
        $"Response: {(Success ? "Success" : "Failed")} - {CompletionStatus} ({CompletionPercentage:F1}%)";
}
