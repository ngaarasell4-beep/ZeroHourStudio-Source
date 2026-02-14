using System.Text.RegularExpressions;
using ZeroHourStudio.Application.Interfaces;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;

namespace ZeroHourStudio.Infrastructure.Parsers;

/// <summary>
/// محلل INI متقدم للعبة SAGE
/// - غير حساس لحالة الأحرف (Case-Insensitive)
/// - يتجاهل التعليقات التي تبدأ بـ ;
/// - يستخدم ReadOnlySpan<char> للأداء العالي
/// </summary>
public class SAGE_IniParser : IIniParser
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
        if (currentSection == null) return;

        var equalsIndex = line.IndexOf('=');
        if (equalsIndex == -1) return;

        var key = line.Slice(0, equalsIndex).Trim().ToString();
        var value = line.Slice(equalsIndex + 1).Trim().ToString();

        _data[currentSection][key] = value;
    }

    /// <summary>
    /// الحصول على قيمة معينة من القسم والمفتاح
    /// </summary>
    public async Task<string?> GetValueAsync(string filePath, string section, string key)
    {
        var data = await ParseAsync(filePath);
        if (data.TryGetValue(section, out var sectionData))
        {
            return sectionData.GetValueOrDefault(key);
        }
        return null;
    }

    /// <summary>
    /// الحصول على جميع المفاتيح في قسم معين
    /// </summary>
    public async Task<IEnumerable<string>> GetKeysAsync(string filePath, string section)
    {
        var data = await ParseAsync(filePath);
        if (data.TryGetValue(section, out var sectionData))
        {
            return sectionData.Keys;
        }
        return Enumerable.Empty<string>();
    }

    /// <summary>
    /// الحصول على جميع الأقسام في الملف
    /// </summary>
    public async Task<IEnumerable<string>> GetSectionsAsync(string filePath)
    {
        var data = await ParseAsync(filePath);
        return data.Keys;
    }

    /// <summary>
    /// الحصول على قيمة مباشرة (sync version)
    /// </summary>
    public string GetValue(string section, string key)
    {
        if (_data.TryGetValue(section, out var sectionData))
        {
            return sectionData.GetValueOrDefault(key) ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>
    /// الحصول على جميع المفاتيح (sync version)
    /// </summary>
    public IEnumerable<string> GetKeys(string section)
    {
        if (_data.TryGetValue(section, out var sectionData))
        {
            return sectionData.Keys;
        }
        return Enumerable.Empty<string>();
    }

    /// <summary>
    /// الحصول على جميع الأقسام (sync version)
    /// </summary>
    public IEnumerable<string> GetSections()
    {
        return _data.Keys;
    }

    /// <summary>
    /// استخراج كائن كامل
    /// </summary>
    public string? ExtractObject(string objectName)
    {
        return _fullObjects.GetValueOrDefault(objectName);
    }

    /// <summary>
    /// الحصول على جميع الكائنات الكاملة
    /// </summary>
    public Dictionary<string, string> GetFullObjects()
    {
        return new Dictionary<string, string>(_fullObjects);
    }
}