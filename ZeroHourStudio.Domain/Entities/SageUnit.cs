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
    /// مجموعة الأسلحة المرتبطة بالوحدة
    /// </summary>
    public string WeaponSet { get; set; } = string.Empty;

    /// <summary>
    /// اسم ملف النموذج ثلاثي الأبعاد
    /// </summary>
    public string ModelW3D { get; set; } = string.Empty;
}
