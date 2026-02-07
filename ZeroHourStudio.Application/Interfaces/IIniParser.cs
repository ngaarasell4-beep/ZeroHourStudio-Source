namespace ZeroHourStudio.Application.Interfaces;

/// <summary>
/// واجهة لتحليل ملفات INI
/// </summary>
public interface IIniParser
{
    /// <summary>
    /// تحليل ملف INI وإرجاع محتوياته
    /// </summary>
    /// <param name="filePath">مسار ملف INI</param>
    /// <returns>قاموس متداخل يحتوي على الأقسام والمفاتيح والقيم</returns>
    Task<Dictionary<string, Dictionary<string, string>>> ParseAsync(string filePath);

    /// <summary>
    /// الحصول على قيمة معينة من القسم والمفتاح
    /// </summary>
    /// <param name="filePath">مسار ملف INI</param>
    /// <param name="section">اسم القسم</param>
    /// <param name="key">اسم المفتاح</param>
    /// <returns>القيمة أو null إذا لم تكن موجودة</returns>
    Task<string?> GetValueAsync(string filePath, string section, string key);

    /// <summary>
    /// الحصول على جميع المفاتيح في قسم معين
    /// </summary>
    /// <param name="filePath">مسار ملف INI</param>
    /// <param name="section">اسم القسم</param>
    /// <returns>قائمة بالمفاتيح</returns>
    Task<IEnumerable<string>> GetKeysAsync(string filePath, string section);

    /// <summary>
    /// الحصول على جميع الأقسام في الملف
    /// </summary>
    /// <param name="filePath">مسار ملف INI</param>
    /// <returns>قائمة بأسماء الأقسام</returns>
    Task<IEnumerable<string>> GetSectionsAsync(string filePath);
}
