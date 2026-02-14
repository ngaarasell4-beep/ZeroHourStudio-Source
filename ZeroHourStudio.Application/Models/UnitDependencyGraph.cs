namespace ZeroHourStudio.Application.Models;

/// <summary>
/// يمثل رسم بياني كامل للتبعات الخاصة بوحدة معينة
/// </summary>
public class UnitDependencyGraph
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
    /// العقدة الجذر (ملف INI للوحدة)
    /// </summary>
    public DependencyNode? RootNode { get; set; }

    /// <summary>
    /// جميع العقد في الرسم البياني
    /// </summary>
    public List<DependencyNode> AllNodes { get; set; } = new();

    /// <summary>
    /// عمق الرسم البياني (أعلى مستوى عمق)
    /// </summary>
    public int MaxDepth { get; set; } = 0;

    /// <summary>
    /// إجمالي حجم جميع الملفات بالبايتات
    /// </summary>
    public long TotalSizeInBytes { get; set; } = 0;

    /// <summary>
    /// عدد العقد المفقودة
    /// </summary>
    public int MissingCount { get; set; } = 0;

    /// <summary>
    /// عدد العقد الموجودة
    /// </summary>
    public int FoundCount { get; set; } = 0;

    /// <summary>
    /// حالة الاكتمال الإجمالية
    /// </summary>
    public CompletionStatus Status { get; set; } = CompletionStatus.Unknown;

    /// <summary>
    /// تاريخ إنشاء الرسم البياني
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// ملاحظات إضافية
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// الحصول على جميع المسارات المفقودة
    /// </summary>
    public IEnumerable<DependencyNode> GetMissingDependencies()
    {
        return AllNodes.Where(n => n.Status == AssetStatus.Missing);
    }

    /// <summary>
    /// الحصول على جميع الممتلكات غير المتحققة
    /// </summary>
    public IEnumerable<DependencyNode> GetUnverifiedDependencies()
    {
        return AllNodes.Where(n => n.Status == AssetStatus.NotVerified);
    }

    /// <summary>
    /// النسبة المئوية للاكتمال
    /// </summary>
    public double GetCompletionPercentage()
    {
        if (AllNodes.Count == 0) return 0;
        return (double)FoundCount / AllNodes.Count * 100;
    }

    public override string ToString() => $"Graph({UnitName}) - {AllNodes.Count} nodes, {GetCompletionPercentage():F1}% complete";
}

/// <summary>
/// حالة اكتمال الوحدة
/// </summary>
public enum CompletionStatus
{
    /// <summary>لم يتم التحديد بعد</summary>
    Unknown,

    /// <summary>الوحدة مكتملة تماماً</summary>
    Complete,

    /// <summary>الوحدة ناقصة بعض الملفات غير الحرجة</summary>
    Partial,

    /// <summary>الوحدة ناقصة ملفات حرجة (مثل النموذج)</summary>
    Incomplete,

    /// <summary>الوحدة لم تتمكن من التحقق من حالتها</summary>
    CannotVerify
}
