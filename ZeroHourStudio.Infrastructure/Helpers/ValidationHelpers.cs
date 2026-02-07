namespace ZeroHourStudio.Infrastructure.Helpers;

/// <summary>
/// مساعدات التحقق والتدقيق
/// </summary>
public static class ValidationHelpers
{
    /// <summary>
    /// التحقق من صحة اسم الوحدة التقني
    /// </summary>
    public static bool IsValidUnitName(string unitName)
    {
        if (string.IsNullOrWhiteSpace(unitName))
            return false;

        // لا يجب أن يحتوي على مسافات أو أحرف خاصة
        return System.Text.RegularExpressions.Regex.IsMatch(
            unitName,
            @"^[a-zA-Z0-9_]+$");
    }

    /// <summary>
    /// التحقق من صحة مسار الأرشيف
    /// </summary>
    public static bool IsValidArchivePath(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            return false;

        return File.Exists(archivePath) && 
               (archivePath.EndsWith(".big", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// التحقق من صحة ملف INI
    /// </summary>
    public static bool IsValidIniFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            // محاولة قراءة الملف للتأكد من أنه نص صحيح
            var firstLine = File.ReadLines(filePath).FirstOrDefault();
            return !string.IsNullOrEmpty(firstLine);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// التحقق من أن القيمة تمثل رقماً صحيحاً
    /// </summary>
    public static bool TryParseInt(string? value, out int result)
    {
        return int.TryParse(value, out result);
    }

    /// <summary>
    /// التحقق من أن القيمة تمثل رقماً عشرياً
    /// </summary>
    public static bool TryParseFloat(string? value, out float result)
    {
        return float.TryParse(value, out result);
    }

    /// <summary>
    /// التحقق من أن القيمة تمثل قيمة منطقية
    /// </summary>
    public static bool TryParseBoolean(string? value, out bool result)
    {
        result = false;

        if (string.IsNullOrEmpty(value))
            return false;

        var normalized = value.ToLowerInvariant().Trim();
        
        if (normalized == "true" || normalized == "yes" || normalized == "1")
        {
            result = true;
            return true;
        }

        if (normalized == "false" || normalized == "no" || normalized == "0")
        {
            result = false;
            return true;
        }

        return false;
    }
}
