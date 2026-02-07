namespace ZeroHourStudio.Application.Models;

/// <summary>
/// يمثل نتيجة التحقق من سلامة الوحدة
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// معرف الوحدة المتحقق منها
    /// </summary>
    public string UnitId { get; set; } = string.Empty;

    /// <summary>
    /// هل الوحدة صحيحة (True) أم بها مشاكل (False)
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// الأخطاء الموجودة
    /// </summary>
    public List<ValidationError> Errors { get; set; } = new();

    /// <summary>
    /// التحذيرات
    /// </summary>
    public List<ValidationWarning> Warnings { get; set; } = new();

    /// <summary>
    /// معلومات إضافية عن التحقق
    /// </summary>
    public Dictionary<string, object> AdditionalInfo { get; set; } = new();

    /// <summary>
    /// تاريخ التحقق
    /// </summary>
    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// حساب النسبة المئوية للنجاح
    /// </summary>
    public double GetSuccessPercentage()
    {
        int totalIssues = Errors.Count + Warnings.Count;
        if (totalIssues == 0) return 100;

        int criticalCount = Errors.Count(e => e.Severity == ErrorSeverity.Critical);
        int warningCount = Warnings.Count;

        double score = 100 - (criticalCount * 20) - (warningCount * 5);
        return Math.Max(0, score);
    }

    public override string ToString()
    {
        return $"Validation({UnitId}): {(IsValid ? "Valid" : "Invalid")} - {Errors.Count} errors, {Warnings.Count} warnings";
    }
}

/// <summary>
/// يمثل خطأ التحقق
/// </summary>
public class ValidationError
{
    /// <summary>
    /// رمز الخطأ
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// رسالة الخطأ
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// خطورة الخطأ
    /// </summary>
    public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;

    /// <summary>
    /// الملف المتعلق بالخطأ
    /// </summary>
    public string? RelatedFile { get; set; }

    /// <summary>
    /// رقم السطر (إن أمكن)
    /// </summary>
    public int? LineNumber { get; set; }

    public override string ToString() => $"[{Severity}] {Code}: {Message}";
}

/// <summary>
/// يمثل تحذير التحقق
/// </summary>
public class ValidationWarning
{
    /// <summary>
    /// رمز التحذير
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// رسالة التحذير
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// الملف المتعلق بالتحذير
    /// </summary>
    public string? RelatedFile { get; set; }

    public override string ToString() => $"[WARNING] {Code}: {Message}";
}

/// <summary>
/// درجات خطورة الأخطاء
/// </summary>
public enum ErrorSeverity
{
    /// <summary>خطأ طفيف، لا يؤثر على الاستخدام</summary>
    Info,

    /// <summary>خطأ متوسط، قد يسبب مشاكل</summary>
    Warning,

    /// <summary>خطأ حرج، يمنع الاستخدام</summary>
    Error,

    /// <summary>خطأ حرج جداً، من الممكن أن يسبب أعطال</summary>
    Critical
}
