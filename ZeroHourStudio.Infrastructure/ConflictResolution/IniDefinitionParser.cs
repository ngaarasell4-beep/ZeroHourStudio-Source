using System.Text;
using System.Text.RegularExpressions;

namespace ZeroHourStudio.Infrastructure.ConflictResolution;

/// <summary>
/// حقل واحد في تعريف INI
/// </summary>
public class IniField
{
    /// <summary>اسم المفتاح (مثل Armor, Weapon, CommandButton)</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>القيمة (مثل SMALL_ARMS 25%)</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>السطر الأصلي الكامل</summary>
    public string RawLine { get; set; } = string.Empty;

    /// <summary>تعليق على نفس السطر</summary>
    public string? Comment { get; set; }

    /// <summary>رقم السطر في الملف الأصلي</summary>
    public int LineNumber { get; set; }

    public override string ToString() => $"{Key} = {Value}";
}

/// <summary>
/// بلوك تعريف INI كامل (مثل Object AmericaTank أو Weapon AmericaTankGun)
/// </summary>
public class IniDefinitionBlock
{
    /// <summary>نوع التعريف (Object, Weapon, Armor...)</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>اسم التعريف</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>السطر الأول (الرأس)</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>جميع حقول التعريف</summary>
    public List<IniField> Fields { get; set; } = new();

    /// <summary>البلوكات الفرعية (مثل Body = ActiveBody داخل Object)</summary>
    public List<IniDefinitionBlock> SubBlocks { get; set; } = new();

    /// <summary>المحتوى الخام الكامل</summary>
    public string RawContent { get; set; } = string.Empty;

    /// <summary>الحصول على قيم حقل معين (قد يكون متعدد القيم)</summary>
    public List<IniField> GetFieldsByKey(string key)
        => Fields.Where(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>الحصول على أول قيمة لحقل</summary>
    public string? GetFirstValue(string key)
        => Fields.FirstOrDefault(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
}

/// <summary>
/// محلل بلوكات INI - يحول نص INI إلى بنية مفصلة من الحقول
/// </summary>
public class IniDefinitionParser
{
    // أنواع التعريفات المعروفة
    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Object", "Weapon", "FXList", "ObjectCreationList", "ParticleSystem",
        "Armor", "CommandButton", "CommandSet", "SpecialPower", "Upgrade",
        "Science", "Locomotor", "LocomotorSet", "WeaponSet", "ArmorSet",
        "ExperienceLevel", "ModifierList", "MultiplayerSettings"
    };

    /// <summary>
    /// تحليل محتوى INI واستخراج جميع بلوكات التعريفات
    /// </summary>
    public List<IniDefinitionBlock> ParseAll(string iniContent)
    {
        var blocks = new List<IniDefinitionBlock>();
        var lines = iniContent.Split('\n');

        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            // تجاهل التعليقات والأسطر الفارغة
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';'))
            {
                i++;
                continue;
            }

            // البحث عن رأس بلوك: "Type Name"
            var headerMatch = Regex.Match(trimmed, @"^(\w+)\s+(\S+)\s*$");
            if (headerMatch.Success && KnownTypes.Contains(headerMatch.Groups[1].Value))
            {
                var block = ParseBlock(lines, ref i, headerMatch.Groups[1].Value, headerMatch.Groups[2].Value, line);
                blocks.Add(block);
            }
            else
            {
                i++;
            }
        }

        return blocks;
    }

    /// <summary>
    /// تحليل بلوك واحد من تعريف INI حسب الاسم
    /// </summary>
    public IniDefinitionBlock? ParseDefinition(string iniContent, string definitionName)
    {
        var blocks = ParseAll(iniContent);
        return blocks.FirstOrDefault(b => b.Name.Equals(definitionName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// تحليل بلوك بدءاً من سطر الرأس
    /// </summary>
    private IniDefinitionBlock ParseBlock(string[] lines, ref int index, string type, string name, string headerLine)
    {
        var block = new IniDefinitionBlock
        {
            Type = type,
            Name = name,
            Header = headerLine
        };

        var rawBuilder = new StringBuilder();
        rawBuilder.AppendLine(headerLine);

        index++; // تجاوز سطر الرأس
        int depth = 0;

        while (index < lines.Length)
        {
            var line = lines[index].TrimEnd('\r');
            var trimmed = line.TrimStart();
            rawBuilder.AppendLine(line);

            // تحقق من End
            if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                {
                    index++;
                    break;
                }
                depth--;
                index++;
                continue;
            }

            // تحقق من بلوك فرعي (مثل Body = ActiveBody)
            var subBlockMatch = Regex.Match(trimmed, @"^(\w+)\s*=\s*(\w+)\s*$");
            if (subBlockMatch.Success && IsLikelySubBlock(subBlockMatch.Groups[1].Value))
            {
                depth++;
                index++;
                continue;
            }

            // حقل عادي: Key = Value
            var fieldMatch = Regex.Match(trimmed, @"^(\w+)\s*=\s*(.+?)(?:\s*;(.*))?$");
            if (fieldMatch.Success && depth == 0) // نخزن حقول المستوى الأعلى فقط
            {
                block.Fields.Add(new IniField
                {
                    Key = fieldMatch.Groups[1].Value,
                    Value = fieldMatch.Groups[2].Value.TrimEnd(),
                    RawLine = line,
                    Comment = fieldMatch.Groups[3].Success ? fieldMatch.Groups[3].Value.Trim() : null,
                    LineNumber = index
                });
            }
            else if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith(';') && depth == 0)
            {
                // سطر بدون = ، نضيفه كحقل خام
                block.Fields.Add(new IniField
                {
                    Key = "__RAW__",
                    Value = trimmed,
                    RawLine = line,
                    LineNumber = index
                });
            }

            index++;
        }

        block.RawContent = rawBuilder.ToString();
        return block;
    }

    /// <summary>
    /// هل هذا المفتاح يبدأ بلوك فرعي؟
    /// </summary>
    private static bool IsLikelySubBlock(string key) => key switch
    {
        "Body" or "Draw" or "Behavior" or "ClientBehavior" or "ClientUpdate" or
        "AIUpdate" or "SlowDeathBehavior" or "TransitionState" or "ConditionState" or
        "DefaultConditionState" or "AnimationState" or "IdleAnimation" or
        "ModelConditionState" or "WeaponSet" or "ArmorSet" or "LocomotorSet" or
        "UnitSpecificSounds" or "Prerequisite" or "VeterancyValues" or
        "Die" or "DeathTypes" or "FireWeaponUpdate" => true,
        _ => false
    };
}
