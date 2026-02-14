using System.Text;
using System.Text.RegularExpressions;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.Archives;
using ZeroHourStudio.Infrastructure.Localization;
using ZeroHourStudio.Infrastructure.Logging;

namespace ZeroHourStudio.Infrastructure.Services;

/// <summary>
/// خدمة اكتشاف الفصائل الحقيقية من محرك SAGE — بدون أي بيانات مزيفة.
/// المصادر بالترتيب:
/// 1. PlayerTemplate INI (المصدر الأساسي والأكثر دقة)
/// 2. Side= في Object definitions (fallback)
/// كل المصادر تدعم الملفات المفكوكة + أرشيفات BIG.
/// </summary>
public class FactionDiscoveryService
{
    private static readonly Regex PlayerTemplateHeaderRegex = new(
        @"^\s*PlayerTemplate\s+(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> ExcludedFactions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Civilian", "Observer", "Boss", "Neutral", ""
    };

    /// <summary>
    /// اكتشاف جميع الفصائل من المود — بدون fallback وهمي.
    /// يُعيد قائمة فارغة مع ErrorMessage إذا لم يُعثر على شيء.
    /// </summary>
    public async Task<FactionDiscoveryResult> DiscoverFactionsAsync(string modPath, CsfLocalizationService? csfService = null)
    {
        var result = new FactionDiscoveryResult();

        if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
        {
            result.ErrorMessage = $"المسار غير صالح أو غير موجود: {modPath}";
            BlackBoxRecorder.Record("FACTION_DISCOVERY", "INVALID_PATH", modPath ?? "(null)");
            return result;
        }

        BlackBoxRecorder.Record("FACTION_DISCOVERY", "START", $"Path={modPath}");

        // === Strategy 1: PlayerTemplate (الأكثر دقة) ===
        var ptFactions = await DiscoverFromPlayerTemplateAsync(modPath);
        if (ptFactions.Count > 0)
        {
            result.Factions = ptFactions;
            result.Source = FactionDiscoverySource.PlayerTemplate;
            result.Success = true;
            BlackBoxRecorder.Record("FACTION_DISCOVERY", "PLAYER_TEMPLATE", $"Found {ptFactions.Count} factions");
        }

        // === Strategy 2: Object Side= (fallback — يغطي ملفات بدون PlayerTemplate مفكوك) ===
        if (result.Factions.Count == 0)
        {
            var sideFactions = await DiscoverFromObjectSideAsync(modPath);
            if (sideFactions.Count > 0)
            {
                result.Factions = sideFactions;
                result.Source = FactionDiscoverySource.ObjectSide;
                result.Success = true;
                BlackBoxRecorder.Record("FACTION_DISCOVERY", "OBJECT_SIDE", $"Found {sideFactions.Count} factions");
            }
        }

        // === Resolve CSF display names ===
        if (result.Factions.Count > 0 && csfService != null)
        {
            await ResolveCsfDisplayNamesAsync(result.Factions, modPath, csfService);
        }

        // === No factions found — report the failure clearly ===
        if (result.Factions.Count == 0)
        {
            result.Success = false;
            result.ErrorMessage = "لم يتم العثور على أي فصائل. تحقق من أن المود يحتوي على PlayerTemplate.ini أو ملفات Object مع Side=";
            BlackBoxRecorder.Record("FACTION_DISCOVERY", "EMPTY", "No factions found from any source");
        }

        BlackBoxRecorder.Record("FACTION_DISCOVERY", "END",
            $"Factions={result.Factions.Count} Source={result.Source} Success={result.Success}");

        return result;
    }

    // ═══════════════════════════════════════════
    // Strategy 1: PlayerTemplate
    // ═══════════════════════════════════════════

    private async Task<List<FactionInfo>> DiscoverFromPlayerTemplateAsync(string modPath)
    {
        var factions = new Dictionary<string, FactionInfo>(StringComparer.OrdinalIgnoreCase);

        // Loose files
        var ptPaths = new[]
        {
            Path.Combine(modPath, "Data", "INI", "PlayerTemplate.ini"),
            Path.Combine(modPath, "INI", "PlayerTemplate.ini"),
            Path.Combine(modPath, "PlayerTemplate.ini"),
        };

        foreach (var ptPath in ptPaths)
        {
            if (!File.Exists(ptPath)) continue;
            try
            {
                var content = await File.ReadAllTextAsync(ptPath);
                var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                ParsePlayerTemplateLines(lines, ptPath, factions);
            }
            catch (Exception ex)
            {
                BlackBoxRecorder.Record("FACTION_DISCOVERY", "PT_READ_ERROR", $"File={ptPath}, Error={ex.Message}");
            }
        }

        // BIG archives
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
                    var ptEntries = mgr.GetFileList()
                        .Where(e => e.EndsWith("PlayerTemplate.ini", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var entry in ptEntries)
                    {
                        try
                        {
                            var data = await mgr.ExtractFileAsync(entry);
                            var content = Encoding.GetEncoding(1252).GetString(data);
                            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                            ParsePlayerTemplateLines(lines, $"{Path.GetFileName(bigFile)}::{entry}", factions);
                        }
                        catch { /* skip */ }
                    }
                }
                catch { /* skip */ }
            }
        }

        return factions.Values
            .Where(f => f.IsPlayable)
            .OrderBy(f => f.InternalName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ParsePlayerTemplateLines(string[] lines, string sourceFile, Dictionary<string, FactionInfo> factions)
    {
        string? currentTemplate = null;
        string side = "";
        string displayNameLabel = "";
        bool isPlayable = true;
        int depth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("//"))
                continue;

            var ptMatch = PlayerTemplateHeaderRegex.Match(trimmed);
            if (ptMatch.Success && depth == 0)
            {
                currentTemplate = ptMatch.Groups[1].Value.Trim();
                side = "";
                displayNameLabel = "";
                isPlayable = true;
                depth = 1;
                continue;
            }

            if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 1 && currentTemplate != null)
                {
                    // Commit this PlayerTemplate
                    var factionName = !string.IsNullOrWhiteSpace(side) ? side : currentTemplate;

                    // Filter out non-playable factions
                    if (!ExcludedFactions.Contains(factionName) && isPlayable)
                    {
                        if (!factions.ContainsKey(factionName))
                        {
                            factions[factionName] = new FactionInfo
                            {
                                InternalName = factionName,
                                Side = side,
                                IsPlayable = isPlayable,
                                SourceFile = sourceFile
                            };
                        }
                    }

                    currentTemplate = null;
                }
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (currentTemplate == null || depth < 1) continue;

            // Parse key = value
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = trimmed[..eqIdx].Trim();
            var value = trimmed[(eqIdx + 1)..].Trim();
            // Strip comments
            var commentIdx = value.IndexOf(';');
            if (commentIdx > 0) value = value[..commentIdx].Trim();
            value = value.Trim('"');

            if (key.Equals("Side", StringComparison.OrdinalIgnoreCase))
                side = value;
            else if (key.Equals("DisplayName", StringComparison.OrdinalIgnoreCase))
                displayNameLabel = value;
            else if (key.Equals("IsObserver", StringComparison.OrdinalIgnoreCase) &&
                     value.Equals("Yes", StringComparison.OrdinalIgnoreCase))
                isPlayable = false;
            else if (key.Equals("PlayableSide", StringComparison.OrdinalIgnoreCase) &&
                     value.Equals("No", StringComparison.OrdinalIgnoreCase))
                isPlayable = false;
        }
    }

    // ═══════════════════════════════════════════
    // Strategy 2: Object Side=
    // ═══════════════════════════════════════════

    private async Task<List<FactionInfo>> DiscoverFromObjectSideAsync(string modPath)
    {
        var sideCountMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Loose INI files
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
                try
                {
                    var lines = await File.ReadAllLinesAsync(file);
                    CollectSidesFromLines(lines, sideCountMap);
                }
                catch { /* skip */ }
            }
        }

        // BIG archives
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
                            CollectSidesFromLines(lines, sideCountMap);
                        }
                        catch { /* skip */ }
                    }
                }
                catch { /* skip */ }
            }
        }

        return sideCountMap
            .Where(kvp => !ExcludedFactions.Contains(kvp.Key))
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new FactionInfo
            {
                InternalName = kvp.Key,
                Side = kvp.Key,
                UnitCount = kvp.Value,
                IsPlayable = true,
                SourceFile = "(Object Side=)"
            })
            .ToList();
    }

    private static void CollectSidesFromLines(string[] lines, Dictionary<string, int> sideMap)
    {
        bool inObject = false;
        int depth = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";")) continue;

            if (trimmed.StartsWith("Object ", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("ObjectCreation", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("ObjectStatus", StringComparison.OrdinalIgnoreCase))
            {
                inObject = true;
                depth = 1;
                continue;
            }

            if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                depth--;
                if (depth <= 0) { inObject = false; depth = 0; }
                continue;
            }

            if (!inObject) continue;

            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx <= 0) continue;

            var key = trimmed[..eqIdx].Trim();
            if (!key.Equals("Side", StringComparison.OrdinalIgnoreCase)) continue;

            var value = trimmed[(eqIdx + 1)..].Trim();
            var commentIdx = value.IndexOf(';');
            if (commentIdx > 0) value = value[..commentIdx].Trim();
            value = value.Trim('"');

            if (!string.IsNullOrWhiteSpace(value))
            {
                sideMap.TryGetValue(value, out var count);
                sideMap[value] = count + 1;
            }
        }
    }

    // ═══════════════════════════════════════════
    // CSF Display Name Resolution
    // ═══════════════════════════════════════════

    private async Task ResolveCsfDisplayNamesAsync(List<FactionInfo> factions, string modPath, CsfLocalizationService csfService)
    {
        try
        {
            // Find CSF file
            var csfPaths = new[]
            {
                Path.Combine(modPath, "Data", "generals.csf"),
                Path.Combine(modPath, "Data", "Generals.csf"),
                Path.Combine(modPath, "generals.csf"),
            };

            var csfPath = csfPaths.FirstOrDefault(File.Exists);
            if (csfPath == null) return;

            var entries = await csfService.ReadCsfAsync(csfPath);
            var csfMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Label) && !string.IsNullOrWhiteSpace(entry.EnglishText))
                    csfMap[entry.Label] = entry.EnglishText;
            }

            // Try to resolve display names using common CSF label patterns
            foreach (var faction in factions)
            {
                var labelCandidates = new[]
                {
                    $"SIDE:{faction.InternalName}",
                    $"PlayerTemplate:{faction.InternalName}",
                    $"FACTION:{faction.InternalName}",
                    $"GUI:{faction.InternalName}",
                };

                foreach (var label in labelCandidates)
                {
                    if (csfMap.TryGetValue(label, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
                    {
                        faction.DisplayName = displayName;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            BlackBoxRecorder.Record("FACTION_DISCOVERY", "CSF_ERROR", ex.Message);
        }
    }
}
