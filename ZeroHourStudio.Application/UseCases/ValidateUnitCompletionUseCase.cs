using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;

namespace ZeroHourStudio.Application.UseCases;

/// <summary>
/// Use Case: التحقق من صحة الوحدة
/// المسؤوليات:
/// - طلب من Infrastructure للتحقق
/// - معالجة النتائج
/// - إرسال التقارير
/// </summary>
public interface IValidateUnitCompletionUseCase
{
    /// <summary>
    /// تنفيذ التحقق
    /// </summary>
    Task<ValidateUnitResponse> ExecuteAsync(ValidateUnitRequest request);
}

/// <summary>
/// تنفيذ حالة استخدام التحقق من اكتمال الوحدة
/// </summary>
public class ValidateUnitCompletionUseCase : IValidateUnitCompletionUseCase
{
    private readonly IUnitCompletionValidator _validator;

    public ValidateUnitCompletionUseCase(IUnitCompletionValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<ValidateUnitResponse> ExecuteAsync(ValidateUnitRequest request)
    {
        var response = new ValidateUnitResponse();

        try
        {
            // 1. إجراء التحقق باستخدام خدمة البنية التحتية
            var validationResult = _validator.ValidateUnitCompletion(
                request.UnitId,
                request.DependencyGraph,
                request.AdditionalChecks);

            // 2. تقييم حالة الاكتمال والنسبة المئوية
            var status = _validator.EvaluateCompletionStatus(request.DependencyGraph);
            var percentage = request.DependencyGraph.GetCompletionPercentage();

            // 3. بناء الاستجابة
            response.IsValid = validationResult.IsValid;
            response.ValidationResult = validationResult;
            response.CompletionStatus = status;
            response.CompletionPercentage = percentage;

            // 4. استخراج الملفات المفقودة والتحذيرات للواجهة
            response.MissingFiles = validationResult.Errors
                .Where(e => !string.IsNullOrEmpty(e.RelatedFile))
                .Select(e => e.RelatedFile!)
                .ToList();

            response.Warnings = validationResult.Warnings
                .Select(w => w.Message)
                .ToList();

            // 5. إضافة توصيات بناءً على النتائج
            AddRecommendations(response, validationResult);
        }
        catch (Exception ex)
        {
            response.IsValid = false;
            response.Warnings.Add($"فشل التحقق: {ex.Message}");
        }

        return await Task.FromResult(response);
    }

    private void AddRecommendations(ValidateUnitResponse response, ValidationResult result)
    {
        if (!response.IsValid)
        {
            response.Recommendations.Add("يجب معالجة الأخطاء الحرجة قبل محاولة نقل الوحدة.");
        }

        if (response.CompletionPercentage < 100)
        {
            response.Recommendations.Add("بعض الملفات غير الحرجة مفقودة، قد تظهر الوحدة بشكل غير مكتمل في اللعبة.");
        }

        if (result.Errors.Any(e => e.Code == "COMMANDSET_NOT_FOUND"))
        {
            response.Recommendations.Add("تأكد من وجود ملف CommandSet.ini الصحيح في مجلد اللعبة.");
        }
    }
}

/// <summary>
/// طلب التحقق من الوحدة
/// </summary>
public class ValidateUnitRequest
{
    /// <summary>
    /// معرف الوحدة
    /// </summary>
    public string UnitId { get; set; } = string.Empty;

    /// <summary>
    /// الرسم البياني للتبعيات
    /// </summary>
    public UnitDependencyGraph DependencyGraph { get; set; } = new();

    /// <summary>
    /// فحوصات إضافية
    /// </summary>
    public Dictionary<string, bool> AdditionalChecks { get; set; } = new();

    /// <summary>
    /// مستوى صرامة التحقق
    /// </summary>
    public ValidationSeverity ValidationSeverity { get; set; } = ValidationSeverity.Standard;

    public override string ToString() => $"ValidateUnit({UnitId})";
}

/// <summary>
/// استجابة التحقق من الوحدة
/// </summary>
public class ValidateUnitResponse
{
    /// <summary>
    /// هل الوحدة صحيحة
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// نتائج التحقق المفصلة
    /// </summary>
    public ValidationResult? ValidationResult { get; set; }

    /// <summary>
    /// حالة الاكتمال
    /// </summary>
    public CompletionStatus CompletionStatus { get; set; }

    /// <summary>
    /// قائمة الملفات المفقودة
    /// </summary>
    public List<string> MissingFiles { get; set; } = new();

    /// <summary>
    /// قائمة التحذيرات
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// النسبة المئوية للاكتمال
    /// </summary>
    public double CompletionPercentage { get; set; }

    /// <summary>
    /// التوصيات
    /// </summary>
    public List<string> Recommendations { get; set; } = new();

    public override string ToString() =>
        $"ValidationResponse: {(IsValid ? "Valid" : "Invalid")} - {CompletionPercentage:F1}% complete";
}

/// <summary>
/// مستويات صرامة التحقق
/// </summary>
public enum ValidationSeverity
{
    /// <summary>متساهل - تحذيرات فقط</summary>
    Lenient,

    /// <summary>قياسي - الأخطاء والتحذيرات</summary>
    Standard,

    /// <summary>صارم - أي خطأ صغير</summary>
    Strict
}
