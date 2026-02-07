using System.Text.RegularExpressions;

namespace ZeroHourStudio.Infrastructure.Parsers;

/// <summary>
/// محلل INI متقدم للعبة SAGE
/// - غير حساس لحالة الأحرف (Case-Insensitive)
/// - يتجاهل التعليقات التي تبدأ بـ ;
/// - يستخدم ReadOnlySpan<char> للأداء العالي
/// </summary>
public class SAGE_IniParser
{
    private readonly Dictionary<string, Dictionary<string, string>> _data;
    private readonly Dictionary<string, string> _fullObjects; // لتخزين كود الكائنات الكامل

    public SAGE_IniParser()
    {
        _data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        _fullObjects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// تحليل ملف INI بأكمله
    /// </summary>
    public async Task<Dictionary<string, Dictionary<string, string>>> ParseAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"ملف INI غير موجود: {filePath}");

        return await Task.Run(() =>
        {
            var lines = File.ReadAllLines(filePath);
            ParseLines(lines);
            return new Dictionary<string, Dictionary<string, string>>(_data);
        });
    }

    /// <summary>
    /// تحليل نصوص برمجية INI
    /// </summary>
    private void ParseLines(string[] lines)
    {
        string? currentSection = null;

        for (int i = 0; i < lines.Length; i++)
        {
            ReadOnlySpan<char> line = lines[i].AsSpan().Trim();

            // تجاهل الأسطر الفارغة
            if (line.IsEmpty)
                continue;

            // تجاهل التعليقات
            if (line.StartsWith(";") || line.StartsWith("//"))
                continue;

            // داخل قسم (Section)
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                currentSection = line.Slice(1, line.Length - 2).ToString();
                if (!_data.ContainsKey(currentSection))
                {
                    _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                continue;
            }

            // معالجة كائن (Object)
            if (line.StartsWith("Object", StringComparison.OrdinalIgnoreCase))
            {
                HandleObjectDefinition(lines, ref i);
            }
            // معالجة مفتاح = قيمة
            else if (line.Contains('='))
            {
                ParseKeyValue(line, currentSection);
            }
        }
    }

    /// <summary>
    /// معالجة تعريف الكائن (Object ... End)
    /// </summary>
    private void HandleObjectDefinition(string[] lines, ref int currentIndex)
    {
        string? objectName = null;
        var objectContent = new List<string>();

        ReadOnlySpan<char> firstLine = lines[currentIndex].AsSpan().Trim();
        
        // استخراج اسم الكائن من "Object ObjectName"
        var match = Regex.Match(firstLine.ToString(), @"Object\s+(\w+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            objectName = match.Groups[1].Value;
        }

        objectContent.Add(lines[currentIndex]);
        currentIndex++;

        // جمع جميع الأسطر من Object إلى End
        while (currentIndex < lines.Length)
        {
            string currentLine = lines[currentIndex];
            objectContent.Add(currentLine);

            if (currentLine.Trim().Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            currentIndex++;
        }

        if (!string.IsNullOrEmpty(objectName))
        {
            // تخزين الكود الكامل للكائن
            _fullObjects[objectName] = string.Join(Environment.NewLine, objectContent);

            // إضافة معلومات أساسية إلى القاموس
            if (!_data.ContainsKey(objectName))
            {
                _data[objectName] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// تحليل سطر Key=Value
    /// </summary>
    private void ParseKeyValue(ReadOnlySpan<char> line, string? currentSection)
    {
        int equalsIndex = line.IndexOf('=');
        if (equalsIndex <= 0)
            return;

        var key = line.Slice(0, equalsIndex).Trim().ToString();
        var value = line.Slice(equalsIndex + 1).Trim().ToString();

        // إزالة علامات الاقتباس إذا كانت موجودة
        if (value.StartsWith("\"") && value.EndsWith("\""))
        {
            value = value.Substring(1, value.Length - 2);
        }

        if (!string.IsNullOrEmpty(currentSection))
        {
            _data[currentSection][key] = value;
        }
    }

    /// <summary>
    /// استخراج كود كائن كامل من اسمه التقني
    /// </summary>
    public string? ExtractObject(string technicalName)
    {
        if (string.IsNullOrWhiteSpace(technicalName))
            return null;

        return _fullObjects.TryGetValue(technicalName, out var objectCode) ? objectCode : null;
    }

    /// <summary>
    /// الحصول على قيمة محددة من القسم والمفتاح (غير حساس لحالة الأحرف)
    /// </summary>
    public string? GetValue(string section, string key)
    {
        if (_data.TryGetValue(section, out var sectionData))
        {
            return sectionData.TryGetValue(key, out var value) ? value : null;
        }

        return null;
    }

    /// <summary>
    /// الحصول على جميع المفاتيح في قسم معين
    /// </summary>
    public IEnumerable<string> GetKeys(string section)
    {
        return _data.TryGetValue(section, out var sectionData) ? sectionData.Keys : Enumerable.Empty<string>();
    }

    /// <summary>
    /// الحصول على جميع الأقسام
    /// </summary>
    public IEnumerable<string> GetSections() => _data.Keys;

    /// <summary>
    /// الحصول على البيانات المحللة
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> GetParsedData() => _data;

    /// <summary>
    /// الحصول على جميع الكائنات المستخرجة
    /// </summary>
    public Dictionary<string, string> GetFullObjects() => _fullObjects;
}
