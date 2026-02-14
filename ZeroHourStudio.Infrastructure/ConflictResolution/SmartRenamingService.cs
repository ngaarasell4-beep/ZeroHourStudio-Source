using System.Text.RegularExpressions;

namespace ZeroHourStudio.Infrastructure.ConflictResolution;

/// <summary>
/// خدمة إعادة التسمية الذكية - تطبيق خريطة إعادة التسمية على ملفات INI
/// </summary>
public class SmartRenamingService
{
    /// <summary>
    /// تطبيق إعادة التسمية على محتوى INI مع الحفاظ على سلامة المراجع
    /// </summary>
    public string ApplyRenames(string iniContent, Dictionary<string, string> renameMap)
    {
        if (string.IsNullOrEmpty(iniContent) || renameMap.Count == 0)
            return iniContent;

        var result = iniContent;

        // ترتيب حسب الطول لتجنب الاستبدال الجزئي
        foreach (var kvp in renameMap.OrderByDescending(k => k.Key.Length))
        {
            // استبدال في رؤوس البلوكات: "Object NewName" بدلاً من "Object OldName"
            result = Regex.Replace(
                result,
                @"(?<=\b(?:Object|Weapon|FXList|ObjectCreationList|ParticleSystem|Armor|CommandButton|CommandSet|SpecialPower|Upgrade|Science|Locomotor|LocomotorSet)\s+)" +
                Regex.Escape(kvp.Key) + @"(?=\s|$)",
                kvp.Value,
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // استبدال في المراجع: قيم المفاتيح
            result = Regex.Replace(
                result,
                @"(?<=\=\s*)" + Regex.Escape(kvp.Key) + @"(?=\s|$|;)",
                kvp.Value,
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            // استبدال داخل WeaponSet / WeaponSlot references
            result = Regex.Replace(
                result,
                @"(?<=Weapon\s*=\s*)" + Regex.Escape(kvp.Key) + @"(?=\s|$|;)",
                kvp.Value,
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// توليد اسم فريد لتجنب التعارض
    /// </summary>
    public string GenerateUniqueName(string baseName, HashSet<string> existingNames)
    {
        var candidate = baseName.StartsWith("ZH_", StringComparison.OrdinalIgnoreCase)
            ? baseName
            : "ZH_" + baseName;

        if (!existingNames.Contains(candidate))
            return candidate;

        for (int i = 2; i < 100; i++)
        {
            var numbered = $"{candidate}_v{i}";
            if (!existingNames.Contains(numbered))
                return numbered;
        }

        return $"{candidate}_{Guid.NewGuid():N8}";
    }

    /// <summary>
    /// تطبيق إعادة التسمية على ملف INI كامل
    /// </summary>
    public async Task<string> ProcessFileAsync(string filePath, Dictionary<string, string> renameMap)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var renamed = ApplyRenames(content, renameMap);
        if (renamed != content)
        {
            await File.WriteAllTextAsync(filePath, renamed);
        }
        return renamed;
    }
}
