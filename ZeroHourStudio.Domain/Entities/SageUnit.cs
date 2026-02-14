namespace ZeroHourStudio.Domain.Entities;

/// <summary>
/// يمثل وحدة اللعبة (Unit) مع خصائصها الأساسية
/// </summary>
public class SageUnit
{
    /// <summary>
    /// الاسم التقني للوحدة
    /// </summary>
    public string TechnicalName { get; set; } = string.Empty;

    /// <summary>
    /// جانب الوحدة (Infantry, Vehicle, Aircraft, Building, etc.)
    /// </summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>
    /// تكلفة بناء الوحدة
    /// </summary>
    public int BuildCost { get; set; }

    /// <summary>
    /// اسم ملف النموذج ثلاثي الأبعاد
    /// </summary>
    public string ModelW3D { get; set; } = string.Empty;

    /// <summary>
    /// اسم صورة الأيقونة (ButtonImage من CommandButton)
    /// </summary>
    public string ButtonImage { get; set; } = string.Empty;
}
