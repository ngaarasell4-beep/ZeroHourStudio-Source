using System.Text;
using System.Text.RegularExpressions;
using ZeroHourStudio.Domain.Models;

namespace ZeroHourStudio.Infrastructure.Services;

/// <summary>
/// نتيجة معاينة التحويل
/// </summary>
public class ConversionPreview
{
    public string UnitName { get; set; } = string.Empty;
    public string ConvertedName { get; set; } = string.Empty;
    public List<ConversionChange> Changes { get; set; } = new();
    public int TotalChanges => Changes.Count;
}

/// <summary>
/// تغيير واحد في التحويل
/// </summary>
public class ConversionChange
{
    public string Field { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
}

/// <summary>
/// خدمة تحويل الوحدات بين الفصائل
/// </summary>
public class FactionAdapterService
{
    /// <summary>
    /// معاينة التحويل بدون تطبيقه
    /// </summary>
    public ConversionPreview PreviewConversion(string unitContent, string unitName, FactionConversionRules rules)
    {
        var preview = new ConversionPreview
        {
            UnitName = unitName,
            ConvertedName = ConvertName(unitName, rules)
        };

        if (preview.ConvertedName != unitName)
        {
            preview.Changes.Add(new ConversionChange
            {
                Field = "Name",
                OldValue = unitName,
                NewValue = preview.ConvertedName,
                ChangeType = "إعادة تسمية"
            });
        }

        // Scan for convertible fields
        var lines = unitContent.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = trimmed[..eqIdx].Trim();
            var value = trimmed[(eqIdx + 1)..].Trim();

            // Voice conversion
            if (rules.ConvertVoices && key.Contains("Voice", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var mapping in rules.VoiceMapping)
                {
                    if (value.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        preview.Changes.Add(new ConversionChange
                        {
                            Field = key,
                            OldValue = value,
                            NewValue = value.Replace(mapping.Key, mapping.Value, StringComparison.OrdinalIgnoreCase),
                            ChangeType = "صوت"
                        });
                        break;
                    }
                }
            }

            // Color conversion
            if (rules.ConvertColors &&
                (key.Contains("Color", StringComparison.OrdinalIgnoreCase) ||
                 key.Contains("Tint", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var mapping in rules.ColorMapping)
                {
                    if (value.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        preview.Changes.Add(new ConversionChange
                        {
                            Field = key,
                            OldValue = mapping.Key,
                            NewValue = mapping.Value,
                            ChangeType = "لون"
                        });
                        break;
                    }
                }
            }

            // Prefix conversion in references
            if (rules.RenamePrefixes)
            {
                foreach (var prefix in FactionConversionRules.FactionPrefixes)
                {
                    if (prefix.Key.Equals(rules.SourceFaction, StringComparison.OrdinalIgnoreCase) &&
                        value.Contains(prefix.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        var targetPrefix = FactionConversionRules.FactionPrefixes
                            .FirstOrDefault(p => p.Key.Equals(rules.TargetFaction, StringComparison.OrdinalIgnoreCase)).Value;

                        if (targetPrefix != null && targetPrefix != prefix.Value)
                        {
                            preview.Changes.Add(new ConversionChange
                            {
                                Field = key,
                                OldValue = prefix.Value,
                                NewValue = targetPrefix,
                                ChangeType = "بادئة"
                            });
                        }
                        break;
                    }
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[FactionAdapter] Preview: {preview.TotalChanges} changes for '{unitName}'");
        return preview;
    }

    /// <summary>
    /// تطبيق التحويل على محتوى الوحدة
    /// </summary>
    public string ConvertUnitToFaction(string unitContent, FactionConversionRules rules)
    {
        var result = unitContent;

        // Apply voice mappings
        if (rules.ConvertVoices)
        {
            foreach (var mapping in rules.VoiceMapping)
            {
                result = result.Replace(mapping.Key, mapping.Value, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Apply color mappings
        if (rules.ConvertColors)
        {
            foreach (var mapping in rules.ColorMapping)
            {
                result = result.Replace(mapping.Key, mapping.Value, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Apply prefix renaming
        if (rules.RenamePrefixes)
        {
            var sourcePrefix = FactionConversionRules.FactionPrefixes
                .FirstOrDefault(p => p.Key.Equals(rules.SourceFaction, StringComparison.OrdinalIgnoreCase)).Value;
            var targetPrefix = FactionConversionRules.FactionPrefixes
                .FirstOrDefault(p => p.Key.Equals(rules.TargetFaction, StringComparison.OrdinalIgnoreCase)).Value;

            if (sourcePrefix != null && targetPrefix != null && sourcePrefix != targetPrefix)
            {
                result = result.Replace(sourcePrefix, targetPrefix, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    private string ConvertName(string unitName, FactionConversionRules rules)
    {
        var sourcePrefix = FactionConversionRules.FactionPrefixes
            .FirstOrDefault(p => p.Key.Equals(rules.SourceFaction, StringComparison.OrdinalIgnoreCase)).Value;
        var targetPrefix = FactionConversionRules.FactionPrefixes
            .FirstOrDefault(p => p.Key.Equals(rules.TargetFaction, StringComparison.OrdinalIgnoreCase)).Value;

        if (sourcePrefix != null && targetPrefix != null &&
            unitName.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return targetPrefix + unitName[sourcePrefix.Length..];
        }

        return unitName;
    }
}
