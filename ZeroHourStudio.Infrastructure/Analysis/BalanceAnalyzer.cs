using System.Text;
using System.Text.RegularExpressions;
using ZeroHourStudio.Infrastructure.Archives;

namespace ZeroHourStudio.Infrastructure.Analysis;

/// <summary>
/// تقييم خاصية التوازن
/// </summary>
public class BalanceRating
{
    public string Category { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Average { get; set; }
    public double Percentage => Average == 0 ? 0 : Math.Round(Value / Average * 100, 1);
    public string Verdict => Percentage switch
    {
        > 150 => "⚠ مبالغ فيه",
        > 120 => "🔼 فوق المتوسط",
        > 80 => "✅ متوازن",
        > 50 => "🔽 تحت المتوسط",
        _ => "⚠ ضعيف جداً"
    };
    public string VerdictColor => Percentage switch
    {
        > 150 => "#FF6666",
        > 120 => "#FFD700",
        > 80 => "#00CC66",
        > 50 => "#87CEEB",
        _ => "#FF6666"
    };
}

/// <summary>
/// تقرير التوازن الكامل لوحدة
/// </summary>
public class BalanceReport
{
    public string UnitName { get; set; } = string.Empty;
    public string UnitType { get; set; } = string.Empty;
    public List<BalanceRating> Ratings { get; set; } = new();
    public double OverallScore => Ratings.Count == 0 ? 0 : Math.Round(Ratings.Average(r => r.Percentage), 1);
    public string OverallVerdict => OverallScore switch
    {
        > 150 => "⚠ مبالغ فيه",
        > 120 => "فوق المتوسط",
        > 80 => "✅ متوازن",
        > 50 => "تحت المتوسط",
        _ => "⚠ ضعيف"
    };
    public int PeerCount { get; set; }
}

/// <summary>
/// محلل توازن الوحدات - يقارن إحصائيات الوحدة بأقرانها
/// </summary>
public class BalanceAnalyzer
{
    private static readonly string[] _numericFields = new[]
    {
        "BuildCost", "BuildTime", "MaxHealth", "Speed", "SightRange",
        "VisionRange", "ShroudClearingRange", "CrushableLevel",
        "ArmorSet", "CommandPoints"
    };

    /// <summary>
    /// تحليل توازن وحدة مقارنة بأقرانها في المود
    /// </summary>
    public async Task<BalanceReport> AnalyzeUnit(string modPath, string unitName)
    {
        var report = new BalanceReport { UnitName = unitName };
        try
        {
            // Parse all units from loose files AND BIG archives
            var allUnits = await ParseAllUnitsFromModAsync(modPath);
            report.PeerCount = allUnits.Count;

            // Try exact name, then common prefixes
            Dictionary<string, string>? targetStats = null;
            if (allUnits.TryGetValue(unitName, out targetStats)) { }
            else if (allUnits.TryGetValue($"ZH_{unitName}", out targetStats)) { }
            else
            {
                // Try partial match
                var match = allUnits.Keys.FirstOrDefault(k =>
                    k.EndsWith(unitName, StringComparison.OrdinalIgnoreCase) ||
                    k.Contains(unitName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    targetStats = allUnits[match];
            }

            if (targetStats == null)
            {
                System.Diagnostics.Debug.WriteLine($"[BalanceAnalyzer] Unit '{unitName}' not found among {allUnits.Count} units");
                return report;
            }

            report.UnitType = targetStats.GetValueOrDefault("_type", "Object");

            // Calculate averages and ratings
            foreach (var field in _numericFields)
            {
                var values = allUnits.Values
                    .Select(u => u.TryGetValue(field, out var v) ? TryParseDouble(v) : (double?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                if (values.Count < 2) continue;

                var avg = values.Average();
                var unitVal = targetStats.TryGetValue(field, out var rawVal) ? TryParseDouble(rawVal) : null;

                if (unitVal.HasValue && avg > 0)
                {
                    report.Ratings.Add(new BalanceRating
                    {
                        Category = field,
                        Value = unitVal.Value,
                        Average = avg
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BalanceAnalyzer] ERROR: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// تجميع جميع الوحدات من ملفات INI المفكوكة + أرشيفات BIG
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, string>>> ParseAllUnitsFromModAsync(string modPath)
    {
        var units = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        // 1. Parse loose INI files
        var iniDirs = new[]
        {
            Path.Combine(modPath, "Data", "INI"),
            Path.Combine(modPath, "INI"),
        };

        foreach (var iniDir in iniDirs)
        {
            if (!Directory.Exists(iniDir)) continue;
            foreach (var file in Directory.GetFiles(iniDir, "*.ini", SearchOption.AllDirectories))
            {
                await Task.Yield();
                try
                {
                    var lines = await File.ReadAllLinesAsync(file);
                    ParseLinesIntoUnits(lines, units);
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
                            ParseLinesIntoUnits(lines, units);
                        }
                        catch { /* skip unreadable entries */ }
                    }
                }
                catch { /* skip unreadable archives */ }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[BalanceAnalyzer] Parsed {units.Count} units from {modPath}");
        return units;
    }

    // بلوكات فرعية بكلمة واحدة (بدون مسافة) — تفتح بلوك وتُغلق بـ End
    private static readonly HashSet<string> _singleWordSubBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "DefaultConditionState", "ConditionState", "TransitionState",
        "ModelConditionState", "AnimationState", "IdleAnimationState",
        "Prerequisites", "UnitSpecificSounds", "UnitSpecificFX",
    };

    private static void ParseLinesIntoUnits(string[] lines, Dictionary<string, Dictionary<string, string>> units)
    {
        string? currentUnit = null;
        Dictionary<string, string>? currentStats = null;
        int depth = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("//"))
                continue;

            // Object block header
            if (depth == 0 && trimmed.StartsWith("Object ", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("ObjectCreation", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("ObjectStatus", StringComparison.OrdinalIgnoreCase))
            {
                currentUnit = trimmed.Length > 7 ? trimmed[7..].Trim() : null;
                if (currentUnit != null)
                {
                    currentStats = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    currentStats["_type"] = "Object";
                    units[currentUnit] = currentStats;
                    depth = 1;
                }
                continue;
            }

            // Non-Object block at depth 0 (e.g. Weapon, Armor, FXList) — track depth only
            if (depth == 0 && !trimmed.Contains('=') && !trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                var fw = trimmed.Split(' ', '\t')[0];
                if (fw.Length > 1 && char.IsUpper(fw[0]))
                {
                    depth = 1;
                    continue;
                }
            }

            if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                depth--;
                if (depth <= 0)
                {
                    currentUnit = null;
                    currentStats = null;
                    depth = 0;
                }
                continue;
            }

            // Track sub-blocks inside any block
            if (depth > 0 && !trimmed.Contains('='))
            {
                // Single-word sub-block starters (e.g. DefaultConditionState, Prerequisites)
                if (!trimmed.Contains(' ') && _singleWordSubBlocks.Contains(trimmed))
                {
                    depth++;
                    continue;
                }

                // Multi-word sub-block: "Word Word" pattern (e.g. "ActiveBody ModuleTag_Body")
                if (trimmed.Contains(' ') && trimmed.Length > 2 && char.IsLetter(trimmed[0]))
                {
                    var firstWord2 = trimmed.Split(' ', '\t')[0];
                    if (firstWord2.Length > 1 && char.IsUpper(firstWord2[0]))
                    {
                        depth++;
                        continue;
                    }
                }
            }

            // Extract key=value pairs only at depth 1 (direct children of Object block)
            if (currentStats == null || depth != 1) continue;

            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = trimmed[..eqIdx].Trim();
            var value = trimmed[(eqIdx + 1)..].Trim();

            var commentIdx = value.IndexOf(';');
            if (commentIdx > 0) value = value[..commentIdx].Trim();

            if (!string.IsNullOrWhiteSpace(value))
                currentStats[key] = value;
        }
    }

    private static double? TryParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Regex.Match(value, @"[\d.]+");
        return match.Success && double.TryParse(match.Value, out var num) ? num : null;
    }
}
