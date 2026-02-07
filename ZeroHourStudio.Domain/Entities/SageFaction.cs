namespace ZeroHourStudio.Domain.Entities;

/// <summary>
/// يمثل الجيش/الحضارة في اللعبة
/// </summary>
public class SageFaction
{
    /// <summary>
    /// الاسم الداخلي للجيش (يستخدم في الملفات)
    /// </summary>
    public string InternalName { get; set; } = string.Empty;

    /// <summary>
    /// الاسم المعروض للاعب
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// مسار ملف CommandSet الخاص بالجيش
    /// </summary>
    public string CommandSetPath { get; set; } = string.Empty;
}
