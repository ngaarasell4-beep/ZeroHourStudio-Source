namespace ZeroHourStudio.Application.Interfaces;

/// <summary>
/// واجهة لقراءة ملفات الأرشيف الكبيرة (Big Files)
/// </summary>
public interface IBigFileReader
{
    /// <summary>
    /// فتح ملف أرشيف اللعبة وقراءة محتوياته
    /// </summary>
    /// <param name="filePath">مسار ملف الأرشيف</param>
    /// <returns>قائمة بأسماء الملفات داخل الأرشيف</returns>
    Task<IEnumerable<string>> ReadAsync(string filePath);

    /// <summary>
    /// استخراج ملف معين من الأرشيف
    /// </summary>
    /// <param name="filePath">مسار ملف الأرشيف</param>
    /// <param name="fileName">اسم الملف المراد استخراجه</param>
    /// <param name="outputPath">المسار المراد حفظ الملف فيه</param>
    Task ExtractAsync(string filePath, string fileName, string outputPath);

    /// <summary>
    /// التحقق من وجود ملف معين داخل الأرشيف
    /// </summary>
    /// <param name="filePath">مسار ملف الأرشيف</param>
    /// <param name="fileName">اسم الملف المراد التحقق منه</param>
    /// <returns>true إذا كان الملف موجوداً</returns>
    Task<bool> FileExistsAsync(string filePath, string fileName);
}
