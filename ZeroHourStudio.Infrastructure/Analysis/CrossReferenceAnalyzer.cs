using System.Text;
using System.Text.RegularExpressions;
using ZeroHourStudio.Infrastructure.Archives;

namespace ZeroHourStudio.Infrastructure.Analysis;

/// <summary>
/// عنصر مرجع تقاطعي
/// </summary>
public class CrossReference
{
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public List<string> UsedByUnits { get; set; } = new();
    public int UsageCount => UsedByUnits.Count;
    public bool IsShared => UsageCount > 1;
}

/// <summary>
/// نتيجة تحليل المراجع التقاطعية
/// </summary>
public class CrossReferenceReport
{
    public List<CrossReference> WeaponReferences { get; set; } = new();
    public List<CrossReference> ArmorReferences { get; set; } = new();
    public List<CrossReference> FxReferences { get; set; } = new();
    public List<CrossReference> ModelReferences { get; set; } = new();
    public List<CrossReference> TextureReferences { get; set; } = new();

    public int TotalResources => WeaponReferences.Count + ArmorReferences.Count + FxReferences.Count
                                 + ModelReferences.Count + TextureReferences.Count;
    public int SharedResources => WeaponReferences.Count(r => r.IsShared) + ArmorReferences.Count(r => r.IsShared)
                                  + FxReferences.Count(r => r.IsShared) + ModelReferences.Count(r => r.IsShared)
                                  + TextureReferences.Count(r => r.IsShared);
}

/// <summary>
/// محلل المراجع التقاطعية - يكتشف الروابط بين الوحدات والموارد المشتركة
/// </summary>
public class CrossReferenceAnalyzer
{
    // أنماط الحقول التي تشير إلى موارد
    private static readonly Dictionary<string, string> _resourcePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Weapon", "Weapon" },
        { "PrimaryWeapon", "Weapon" },
        { "SecondaryWeapon", "Weapon" },
        { "TertiaryWeapon", "Weapon" },
        { "ArmorSet", "Armor" },
        { "Armor", "Armor" },
        { "Body", "Armor" },
        { "FXList", "FX" },
        { "FireFX", "FX" },
        { "ProjectileFX", "FX" },
        { "OcclModel", "Model" },
        { "Model", "Model" },
        { "Draw", "Model" },
        { "Texture", "Texture" },
        { "MappedImage", "Texture" }
    };

    /// <summary>
    /// تحليل المراجع التقاطعية لجميع وحدات المود
    /// </summary>
    public async Task<CrossReferenceReport> AnalyzeModAsync(string modPath)
    {
        var report = new CrossReferenceReport();
        var weaponMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var armorMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var fxMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var modelMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var textureMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // 1. Parse loose INI files
            var iniDirs = new[]
            {
                Path.Combine(modPath, "Data", "INI"),
                Path.Combine(modPath, "INI"),
            };

            foreach (var iniDir in iniDirs)
            {
                if (!Directory.Exists(iniDir)) continue;
                var iniFiles = Directory.GetFiles(iniDir, "*.ini", SearchOption.AllDirectories);
                foreach (var file in iniFiles)
                {
                    await Task.Yield();
                    try
                    {
                        var lines = await File.ReadAllLinesAsync(file);
                        ParseLinesForReferences(lines, weaponMap, armorMap, fxMap, modelMap, textureMap);
                    }
                    catch { /* skip unreadable files */ }
                }
            }

            // 2. Parse INI files from BIG archives
            if (Directory.Exists(modPath))
            {
                var bigFiles = Directory.GetFiles(modPath, "*.big", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => Path.GetFileName(f).StartsWith("!!") ? 1 : 0)
                    .ToList();

                foreach (var bigFile in bigFiles)
                {
                    try
                    {
                        using var mgr = new BigArchiveManager(bigFile);
                        await mgr.LoadAsync();
                        var iniEntries = mgr.GetFileList()
                            .Where(e => e.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        foreach (var entry in iniEntries)
                        {
                            try
                            {
                                var data = await mgr.ExtractFileAsync(entry);
                                var content = Encoding.GetEncoding(1252).GetString(data);
                                var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                                ParseLinesForReferences(lines, weaponMap, armorMap, fxMap, modelMap, textureMap);
                            }
                            catch { /* skip unreadable entries */ }
                        }
                    }
                    catch { /* skip unreadable archives */ }
                }
            }

            // Convert maps to CrossReference lists
            report.WeaponReferences = weaponMap.Select(kv => new CrossReference
            { ResourceName = kv.Key, ResourceType = "Weapon", UsedByUnits = kv.Value }).OrderByDescending(r => r.UsageCount).ToList();

            report.ArmorReferences = armorMap.Select(kv => new CrossReference
            { ResourceName = kv.Key, ResourceType = "Armor", UsedByUnits = kv.Value }).OrderByDescending(r => r.UsageCount).ToList();

            report.FxReferences = fxMap.Select(kv => new CrossReference
            { ResourceName = kv.Key, ResourceType = "FX", UsedByUnits = kv.Value }).OrderByDescending(r => r.UsageCount).ToList();

            report.ModelReferences = modelMap.Select(kv => new CrossReference
            { ResourceName = kv.Key, ResourceType = "Model", UsedByUnits = kv.Value }).OrderByDescending(r => r.UsageCount).ToList();

            report.TextureReferences = textureMap.Select(kv => new CrossReference
            { ResourceName = kv.Key, ResourceType = "Texture", UsedByUnits = kv.Value }).OrderByDescending(r => r.UsageCount).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CrossRefAnalyzer] ERROR: {ex.Message}");
        }

        System.Diagnostics.Debug.WriteLine($"[CrossRefAnalyzer] Found {report.TotalResources} resources, {report.SharedResources} shared");
        return report;
    }

    private void ParseLinesForReferences(
        string[] lines,
        Dictionary<string, List<string>> weaponMap,
        Dictionary<string, List<string>> armorMap,
        Dictionary<string, List<string>> fxMap,
        Dictionary<string, List<string>> modelMap,
        Dictionary<string, List<string>> textureMap)
    {
        string? currentUnit = null;
        int depth = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";")) continue;

            if (trimmed.StartsWith("Object ", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("ObjectCreation", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("ObjectStatus", StringComparison.OrdinalIgnoreCase))
            {
                currentUnit = trimmed.Length > 7 ? trimmed[7..].Trim() : null;
                depth = currentUnit != null ? 1 : 0;
                continue;
            }

            if (currentUnit != null && !trimmed.StartsWith("End", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Contains(' ') && !trimmed.Contains('=') &&
                    (trimmed.StartsWith("Body", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("Draw", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("Behavior", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("Locomotor", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("ArmorSet", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("WeaponSet", StringComparison.OrdinalIgnoreCase)))
                {
                    depth++;
                }
            }

            if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                depth--;
                if (depth <= 0)
                {
                    currentUnit = null;
                    depth = 0;
                }
                continue;
            }

            if (currentUnit == null) continue;

            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = trimmed[..eqIdx].Trim();
            var value = trimmed[(eqIdx + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(value)) continue;

            var commentIdx = value.IndexOf(';');
            if (commentIdx > 0)
                value = value[..commentIdx].Trim();

            foreach (var pattern in _resourcePatterns)
            {
                if (key.Contains(pattern.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var map = pattern.Value switch
                    {
                        "Weapon" => weaponMap,
                        "Armor" => armorMap,
                        "FX" => fxMap,
                        "Model" => modelMap,
                        "Texture" => textureMap,
                        _ => null
                    };

                    if (map != null)
                    {
                        if (!map.TryGetValue(value, out var units))
                        {
                            units = new List<string>();
                            map[value] = units;
                        }
                        if (!units.Contains(currentUnit, StringComparer.OrdinalIgnoreCase))
                            units.Add(currentUnit);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// البحث عن جميع مستخدمي مورد معين
    /// </summary>
    public List<string> FindUsersOf(CrossReferenceReport report, string resourceName)
    {
        var allRefs = report.WeaponReferences
            .Concat(report.ArmorReferences)
            .Concat(report.FxReferences)
            .Concat(report.ModelReferences)
            .Concat(report.TextureReferences);

        var match = allRefs.FirstOrDefault(r => r.ResourceName.Equals(resourceName, StringComparison.OrdinalIgnoreCase));
        return match?.UsedByUnits ?? new List<string>();
    }
}
