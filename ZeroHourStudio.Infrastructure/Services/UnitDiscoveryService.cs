using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.Archives;
using ZeroHourStudio.Infrastructure.Logging;

namespace ZeroHourStudio.Infrastructure.Services;

public class UnitDiscoveryResult
{
    public List<SageUnit> Units { get; } = new();
    public Dictionary<string, Dictionary<string, string>> UnitDataByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> UnitSourceIniPath { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int TotalObjectsScanned { get; set; }
    public List<string> Warnings { get; } = new();
}

public class DiscoveryProgress
{
    public int FilesProcessed { get; set; }
    public int TotalFiles { get; set; }
    public int UnitsFound { get; set; }
    public string CurrentFile { get; set; } = string.Empty;

    public int Percentage => TotalFiles == 0 ? 0 : (int)Math.Round(FilesProcessed * 100.0 / TotalFiles);
}

/// <summary>
/// خدمة اكتشاف الوحدات من ملفات INI
/// </summary>
public class UnitDiscoveryService
{
    private static readonly Regex ObjectHeaderRegex = new(@"^\s*Object(?:Reskin)?\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const int FileSystemPriority = 100;
    private const int ArchivePriority = 50;
    private const int HighPriorityBonus = 200;

    public async Task<UnitDiscoveryResult> DiscoverUnitsAsync(
        string sourceModPath,
        IProgress<DiscoveryProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(sourceModPath))
            throw new ArgumentNullException(nameof(sourceModPath));

        var logger = new SimpleLogger("discovery.log");
        logger.LogInfo($"بدء الاكتشاف في: {sourceModPath}");
        var discoverySw = System.Diagnostics.Stopwatch.StartNew();
        BlackBoxRecorder.Record("DISCOVERY", "START", $"Path={sourceModPath}");

        var result = new UnitDiscoveryResult();
        var iniRoot = Path.Combine(sourceModPath, "Data", "INI");

        var iniFiles = Directory.Exists(iniRoot)
            ? Directory.GetFiles(iniRoot, "*.ini", SearchOption.AllDirectories)
            : Array.Empty<string>();

        if (iniFiles.Length == 0)
        {
            result.Warnings.Add($"تعذر العثور على ملفات INI مفكوكة داخل: {iniRoot}");
            logger.LogWarning($"تعذر العثور على ملفات INI مفكوكة داخل: {iniRoot}");
        }
        var bigFiles = Directory.GetFiles(sourceModPath, "*.big", SearchOption.AllDirectories);
        logger.LogInfo($"عدد ملفات INI: {iniFiles.Length} | عدد أرشيفات BIG: {bigFiles.Length}");
        var unitCandidates = new ConcurrentDictionary<string, UnitCandidate>(StringComparer.OrdinalIgnoreCase);

        var bigEntriesByArchive = await CollectIniEntriesFromArchivesAsync(bigFiles, result.Warnings);
        var totalFiles = iniFiles.Length + bigEntriesByArchive.Sum(kvp => kvp.Value.Count);
        var progressState = new DiscoveryProgress { TotalFiles = totalFiles };
        var progressLock = new object();

        await Parallel.ForEachAsync(iniFiles, async (file, ct) =>
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(file, ct);
                var isHighPriority = Path.GetFileName(file).StartsWith("!!", StringComparison.OrdinalIgnoreCase);
                ProcessIniObjects(lines, file, false, unitCandidates, result, isHighPriority);

                lock (progressLock)
                {
                    progressState.FilesProcessed++;
                    progressState.CurrentFile = file;
                    progressState.UnitsFound = unitCandidates.Count;
                    progress?.Report(progressState);
                }
            }
            catch (Exception ex)
            {
                lock (progressLock)
                {
                    result.Warnings.Add($"تعذر قراءة {file}: {ex.Message}");
                }
            }
        });

        foreach (var kvp in bigEntriesByArchive)
        {
            var archivePath = kvp.Key;
            var entries = kvp.Value;

            try
            {
                using var manager = new BigArchiveManager(archivePath);
                await manager.LoadAsync();

                await Parallel.ForEachAsync(entries, async (entry, ct) =>
                {
                    try
                    {
                        var fileData = await manager.ExtractFileAsync(entry.EntryPath);
                        var lines = DecodeIniLines(fileData);
                        ProcessIniObjects(lines, $"{archivePath}::{entry.EntryPath}", true, unitCandidates, result, entry.IsHighPriority);

                        lock (progressLock)
                        {
                            progressState.FilesProcessed++;
                            progressState.CurrentFile = $"{Path.GetFileName(archivePath)}::{entry.EntryPath}";
                            progressState.UnitsFound = unitCandidates.Count;
                            progress?.Report(progressState);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (progressLock)
                        {
                            result.Warnings.Add($"تعذر قراءة {archivePath}::{entry.EntryPath}: {ex.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"تعذر فهرسة الأرشيف {archivePath}: {ex.Message}");
            }
        }

        foreach (var candidate in unitCandidates.Values.OrderBy(c => c.Unit.TechnicalName, StringComparer.OrdinalIgnoreCase))
        {
            result.Units.Add(candidate.Unit);
            result.UnitDataByName[candidate.Unit.TechnicalName] = candidate.Data;
            result.UnitSourceIniPath[candidate.Unit.TechnicalName] = candidate.SourcePath;
        }

        discoverySw.Stop();
        logger.LogInfo($"تم اكتشاف {result.Units.Count} وحدة بنجاح");
        BlackBoxRecorder.Record("DISCOVERY", "END", $"Units={result.Units.Count} Scanned={result.TotalObjectsScanned} Warnings={result.Warnings.Count} Elapsed={discoverySw.ElapsedMilliseconds}ms");
        foreach (var warning in result.Warnings)
        {
            logger.LogWarning(warning);
        }

        return result;
    }

    /// <summary>
    /// اكتشاف الفصائل المتاحة في مود (للمود الهدف - اختيار الفصيل عند النقل)
    /// </summary>
    public async Task<List<string>> DiscoverFactionsAsync(string modPath)
    {
        if (string.IsNullOrWhiteSpace(modPath) || !Directory.Exists(modPath))
            return new List<string>();

        try
        {
            var result = await DiscoverUnitsAsync(modPath);
            var factions = result.Units
                .Select(u => u.Side?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .Select(s => s!) // Fix CS8619
                .ToList();

            if (factions.Count == 0)
            {
                return new List<string> { "USA", "China", "GLA" };
            }

            return factions;
        }
        catch
        {
            return new List<string> { "USA", "China", "GLA" };
        }
    }

    private static SageUnit BuildUnit(string name, Dictionary<string, string> data)
    {
        var unit = new SageUnit
        {
            TechnicalName = name,
            Side = data.TryGetValue("Side", out var side) ? side : string.Empty,
            BuildCost = ParseInt(data, "BuildCost"),
            WeaponSet = data.TryGetValue("WeaponSet", out var weaponSet) ? weaponSet : string.Empty,
            ModelW3D = GetModelName(data),
            ButtonImage = GetButtonImage(data)
        };

        return unit;
    }

    private static string GetButtonImage(Dictionary<string, string> data)
    {
        if (data.TryGetValue("ButtonImage", out var img) && !string.IsNullOrWhiteSpace(img))
            return img.Trim();
        if (data.TryGetValue("SelectPortrait", out var portrait) && !string.IsNullOrWhiteSpace(portrait))
            return portrait.Trim();
        if (data.TryGetValue("PortraitImage", out var pImg) && !string.IsNullOrWhiteSpace(pImg))
            return pImg.Trim();
        return string.Empty;
    }

    private static int ParseInt(Dictionary<string, string> data, string key)
    {
        if (data.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return result;

        return 0;
    }

    private static string GetModelName(Dictionary<string, string> data)
    {
        if (data.TryGetValue("Model", out var model) && !string.IsNullOrWhiteSpace(model))
            return model;

        if (data.TryGetValue("ModelName", out var modelName) && !string.IsNullOrWhiteSpace(modelName))
            return modelName;

        return string.Empty;
    }

    private static readonly HashSet<string> AllowedEditorSorting = new(StringComparer.OrdinalIgnoreCase)
    {
        "VEHICLE", "INFANTRY", "AIRCRAFT", "NAVAL"
    };

    private static readonly System.Text.RegularExpressions.Regex ExcludedNamePattern = new(
        @"^(Bush|Tree|Rock|Crate|Fence|Prop|Debris|Sign|Light|Lamp|Pole|Barrel|Box|Civilian|Herd|" +
        @"Beacon|Marker|Flag|Smoke|Fire|Explosion|Projectile|Missile|Bullet|Bomb|Shell|Shrapnel|" +
        @"Parachute|Paradrop|CashBounty|Upgrade|Science|SkirmishCiv|Convoy|PortableStructure)" +
        @"|_Var\d+$|Prisoner|Mob$|Angry",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly HashSet<string> ExcludedEditorSorting = new(StringComparer.OrdinalIgnoreCase)
    {
        "SHRUBBERY", "DEBRIS", "SYSTEM", "OBSTACLE", "MISC",
        "STRUCTURE", "TECH_BUILDING", "SUPPLY",
    };

    private static readonly HashSet<string> AllowedKindOf = new(StringComparer.OrdinalIgnoreCase)
    {
        "INFANTRY", "VEHICLE", "AIRCRAFT"
    };

    private static readonly HashSet<string> RejectedKindOf = new(StringComparer.OrdinalIgnoreCase)
    {
        "BUILDING", "STRUCTURE", "PROP", "DECORATION", "OBJECTPRELOAD",
        "PROJECTILE", "CRATE", "DEBRIS", "SHRUBBERY", "CLEANUP_HAZARD",
        "IGNORED_IN_GUI"
    };

    private static bool IsSelectableUnit(Dictionary<string, string> data, string objectName)
    {
        // ═══ RULE 1: ممنوع أي Object يبدأ بـ CINE_ ═══
        if (objectName.StartsWith("CINE_", StringComparison.OrdinalIgnoreCase))
            return false;

        // ═══ RULE 2: يجب وجود KindOf ═══
        if (!data.TryGetValue("KindOf", out var kindOf) || string.IsNullOrWhiteSpace(kindOf))
            return false;

        var k = kindOf.ToUpperInvariant();

        // ═══ RULE 3: رفض فوري إذا أي KindOf محظور ═══
        foreach (var rejected in RejectedKindOf)
        {
            if (k.Contains(rejected))
                return false;
        }

        // ═══ RULE 4: قبول فقط إذا INFANTRY أو VEHICLE أو AIRCRAFT ═══
        var hasAllowed = false;
        foreach (var allowed in AllowedKindOf)
        {
            if (k.Contains(allowed))
            {
                hasAllowed = true;
                break;
            }
        }
        if (!hasAllowed) return false;

        // ═══ RULE 5: يجب وجود فصيل ═══
        if (!data.TryGetValue("Side", out var side) || string.IsNullOrWhiteSpace(side))
            return false;

        var s = side.Trim();
        if (s.Equals("Civilian", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Boss", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Observer", StringComparison.OrdinalIgnoreCase))
            return false;

        // ═══ RULE 6: Name-based exclusion (safety net) ═══
        if (ExcludedNamePattern.IsMatch(objectName))
            return false;

        // ═══ RULE 7: Exclude non-combat EditorSorting ═══
        if (data.TryGetValue("EditorSorting", out var editorSorting) &&
            ExcludedEditorSorting.Contains(editorSorting.Trim()))
            return false;

        return true;
    }

    private static IEnumerable<(string Name, Dictionary<string, string> Data, bool IsReskin)> ExtractObjects(string[] lines)
    {
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];
            var match = ObjectHeaderRegex.Match(line);
            if (!match.Success)
            {
                index++;
                continue;
            }

            var objectName = match.Groups[1].Value;
            if (objectName.Contains(' '))
            {
                objectName = objectName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            }
            var isReskin = line.TrimStart().StartsWith("ObjectReskin", StringComparison.OrdinalIgnoreCase);
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            index++;

            int nestedDepth = 0;

            while (index < lines.Length)
            {
                var current = lines[index].Trim();

                if (current.StartsWith(";", StringComparison.OrdinalIgnoreCase) ||
                    current.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
                    current.Length == 0)
                {
                    index++;
                    continue;
                }

                if (current.Equals("End", StringComparison.OrdinalIgnoreCase))
                {
                    if (nestedDepth <= 0)
                        break; // End of the outer Object block

                    nestedDepth--;
                    index++;
                    continue;
                }

                // Extract first word to check if it's a block starter
                var equalsIndex = current.IndexOf('=');
                string firstWord;
                if (equalsIndex > 0)
                    firstWord = current.Substring(0, equalsIndex).Trim();
                else
                {
                    var sp = current.IndexOf(' ');
                    firstWord = sp > 0 ? current.Substring(0, sp).Trim() : current;
                }

                // Block starters: "Draw = W3DTruckDraw", "Body ActiveBody", "Behavior = DestroyDie", etc.
                if (IsBlockStarter(firstWord))
                {
                    nestedDepth++;
                }
                else
                {
                    // Capture properties - critical Object-level keys at ANY depth (survives nestedDepth desync)
                    // Other properties only at depth 0
                    bool isCriticalKey = IsObjectLevelKey(firstWord);
                    if (nestedDepth == 0 || isCriticalKey)
                    {
                        if (equalsIndex > 0)
                        {
                            var key = current.Substring(0, equalsIndex).Trim();
                            if (nestedDepth == 0 || IsObjectLevelKey(key))
                            {
                                var rawValue = current.Substring(equalsIndex + 1).Trim();
                                var commentIdx = rawValue.IndexOf(';');
                                if (commentIdx >= 0)
                                    rawValue = rawValue.Substring(0, commentIdx).TrimEnd();
                                commentIdx = rawValue.IndexOf("//", StringComparison.Ordinal);
                                if (commentIdx >= 0)
                                    rawValue = rawValue.Substring(0, commentIdx).TrimEnd();
                                if (!data.ContainsKey(key)) // first occurrence wins (Object level)
                                    data[key] = rawValue.Trim('"');
                            }
                        }
                        else if (!current.StartsWith("End", StringComparison.OrdinalIgnoreCase))
                        {
                            var spaceIdx = current.IndexOf(' ');
                            if (spaceIdx > 0 && (nestedDepth == 0 || isCriticalKey))
                            {
                                var spaceVal = current.Substring(spaceIdx + 1).Trim();
                                var cIdx = spaceVal.IndexOf(';');
                                if (cIdx >= 0) spaceVal = spaceVal.Substring(0, cIdx).TrimEnd();
                                cIdx = spaceVal.IndexOf("//", StringComparison.Ordinal);
                                if (cIdx >= 0) spaceVal = spaceVal.Substring(0, cIdx).TrimEnd();
                                if (!data.ContainsKey(firstWord))
                                    data[firstWord] = spaceVal.Trim('"');
                            }
                        }
                    }
                }

                index++;
            }

            yield return (objectName, data, isReskin);
            index++;
        }
    }

    private static readonly string[] BlockSuffixes =
    {
        "Body", "Draw", "Behavior", "Update", "Die", "Contain", "Module",
        "Collide", "Upgrade", "State", "Ability", "Sounds"
    };

    private static readonly HashSet<string> ExactBlockStarters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Body", "Draw", "Behavior", "WeaponSet", "ArmorSet", "LocomotorSet",
        "DefaultConditionState", "ConditionState", "TransitionState",
        "ModelConditionState", "AnimationState", "ReplaceModule",
        "InheritableModule", "RemoveModule", "Prerequisites",
        "UnitSpecificSounds", "ClientUpdate", "ClientBehavior",
        "Turret", "AddModule", "ObjectStatusOfContained",
        "VeterancyLevels", "ExperienceLevels", "UnitSpecificFX",
        "Flammability", "ThreatBreakdown", "EvaEvents",
        "UnitSpecificExtensions", "FormationPreviewDecal",
        "PerUnitFX", "ProductionModifier", "WeaponBonusCondition",
        "AutoHealBehavior", "RebuildHoleExposeDie",
        "CreateObject", "CreateDebris", "DeliverPayload",
        "Sound", "ParticleSystem", "LightPulse", "FXParticleSystemTemplate",
        "DynamicLOD", "Nugget", "DamageNugget", "MetaImpactNugget",
    };

    private static readonly HashSet<string> ObjectLevelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Side", "EditorSorting", "BuildCost", "ButtonImage", "SelectPortrait",
        "PortraitImage", "CommandSet", "KindOf", "DisplayName", "BuildTime",
        "VisionRange", "ShroudClearingRange", "EnergyProduction", "EnergyBonus",
        "IsPrerequisite", "Scale", "CrushableLevel", "Crushable",
        "TransportSlotCount", "CommandPoints", "ExperienceValue",
        "ExperienceRequired", "CrusherLevel", "ArmorSet", "WeaponSet",
        "BuildVariations", "InheritFrom",
    };

    private static bool IsObjectLevelKey(string key) => ObjectLevelKeys.Contains(key);

    private static bool IsBlockStarter(string key)
    {
        if (ExactBlockStarters.Contains(key)) return true;
        foreach (var suffix in BlockSuffixes)
        {
            if (key.Length > suffix.Length && key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void ProcessIniObjects(
        string[] lines,
        string sourcePath,
        bool isFromArchive,
        ConcurrentDictionary<string, UnitCandidate> candidates,
        UnitDiscoveryResult result,
        bool isHighPriority = false)
    {
        foreach (var obj in ExtractObjects(lines))
        {
            result.TotalObjectsScanned++;

            if (!IsSelectableUnit(obj.Data, obj.Name))
                continue;

            var score = (isFromArchive ? ArchivePriority : FileSystemPriority) + (isHighPriority ? HighPriorityBonus : 0);
            var unit = BuildUnit(obj.Name, obj.Data);
            var candidate = new UnitCandidate(unit, obj.Data, sourcePath, score);

            candidates.AddOrUpdate(obj.Name, candidate, (_, existing) =>
                candidate.PriorityScore > existing.PriorityScore ? candidate : existing);
        }
    }

    private static string[] DecodeIniLines(byte[] data)
    {
        var content = Encoding.GetEncoding(1252).GetString(data);
        return content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
    }

    private static async Task<Dictionary<string, List<ArchiveIniEntry>>> CollectIniEntriesFromArchivesAsync(
        IEnumerable<string> archivePaths,
        List<string> warnings)
    {
        var entries = new Dictionary<string, List<ArchiveIniEntry>>(StringComparer.OrdinalIgnoreCase);

        // Sort archives: normal ones first, !! prefixed ones last (so they override)
        var sortedArchives = archivePaths
            .OrderBy(a => Path.GetFileName(a).StartsWith("!!", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(a => Path.GetFileName(a), StringComparer.OrdinalIgnoreCase);

        foreach (var archivePath in sortedArchives)
        {
            try
            {
                using var manager = new BigArchiveManager(archivePath);
                await manager.LoadAsync();

                // Archive-level priority: if the BIG filename starts with !!, all entries get high priority
                var archiveFileName = Path.GetFileName(archivePath);
                var isArchiveHighPriority = archiveFileName.StartsWith("!!", StringComparison.OrdinalIgnoreCase);

                foreach (var entry in manager.GetFileList())
                {
                    if (!IsIniArchiveEntry(entry))
                        continue;

                    if (!entries.TryGetValue(archivePath, out var list))
                    {
                        list = new List<ArchiveIniEntry>();
                        entries[archivePath] = list;
                    }

                    list.Add(new ArchiveIniEntry
                    {
                        ArchivePath = archivePath,
                        EntryPath = entry,
                        IsHighPriority = isArchiveHighPriority
                            || Path.GetFileName(entry).StartsWith("!!", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"تعذر فهرسة الأرشيف {archivePath}: {ex.Message}");
            }
        }

        return entries;
    }

    private static bool IsIniArchiveEntry(string entry)
    {
        if (!entry.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = entry.Replace('\\', '/');
        // Accept INI files under data/ini/, ini/, or top-level INI files
        // The ObjectHeaderRegex already filters for actual Object definitions
        return normalized.Contains("/data/ini/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/ini/", StringComparison.OrdinalIgnoreCase)
            || !normalized.Contains('/'); // top-level INI files in the archive
    }

    private sealed class UnitCandidate
    {
        public SageUnit Unit { get; }
        public Dictionary<string, string> Data { get; }
        public string SourcePath { get; }
        public int PriorityScore { get; }

        public UnitCandidate(SageUnit unit, Dictionary<string, string> data, string sourcePath, int priorityScore)
        {
            Unit = unit;
            Data = data;
            SourcePath = sourcePath;
            PriorityScore = priorityScore;
        }
    }

    private sealed class ArchiveIniEntry
    {
        public string ArchivePath { get; set; } = string.Empty;
        public string EntryPath { get; set; } = string.Empty;
        public bool IsHighPriority { get; set; }
    }
}
