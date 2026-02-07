using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Parsers;

namespace ZeroHourStudio.Infrastructure.Implementations;

/// <summary>
/// تنفيذ IIniParser باستخدام SAGE_IniParser
/// </summary>
public class IniParser : IIniParser
{
    private SAGE_IniParser? _parser;

    /// <summary>
    /// تحليل ملف INI بالكامل
    /// </summary>
    public async Task<Dictionary<string, Dictionary<string, string>>> ParseAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        _parser = new SAGE_IniParser();
        return await _parser.ParseAsync(filePath);
    }

    /// <summary>
    /// الحصول على قيمة محددة
    /// </summary>
    public async Task<string?> GetValueAsync(string filePath, string section, string key)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (string.IsNullOrWhiteSpace(section))
            throw new ArgumentNullException(nameof(section));

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentNullException(nameof(key));

        return await Task.Run(() =>
        {
            var parser = new SAGE_IniParser();
            parser.ParseAsync(filePath).GetAwaiter().GetResult();
            return parser.GetValue(section, key);
        });
    }

    /// <summary>
    /// الحصول على جميع المفاتيح في قسم
    /// </summary>
    public async Task<IEnumerable<string>> GetKeysAsync(string filePath, string section)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (string.IsNullOrWhiteSpace(section))
            throw new ArgumentNullException(nameof(section));

        return await Task.Run(() =>
        {
            var parser = new SAGE_IniParser();
            parser.ParseAsync(filePath).GetAwaiter().GetResult();
            return parser.GetKeys(section);
        });
    }

    /// <summary>
    /// الحصول على جميع الأقسام
    /// </summary>
    public async Task<IEnumerable<string>> GetSectionsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        return await Task.Run(() =>
        {
            var parser = new SAGE_IniParser();
            parser.ParseAsync(filePath).GetAwaiter().GetResult();
            return parser.GetSections();
        });
    }

    /// <summary>
    /// استخراج كائن كامل بناءً على اسمه التقني
    /// </summary>
    public string? ExtractObject(string technicalName)
    {
        if (_parser == null)
            throw new InvalidOperationException("يجب استدعاء ParseAsync أولاً");

        return _parser.ExtractObject(technicalName);
    }

    /// <summary>
    /// الحصول على جميع الكائنات المستخرجة
    /// </summary>
    public Dictionary<string, string> GetFullObjects()
    {
        if (_parser == null)
            throw new InvalidOperationException("يجب استدعاء ParseAsync أولاً");

        return _parser.GetFullObjects();
    }
}
