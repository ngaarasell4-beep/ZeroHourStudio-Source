using System.Text;
using System.Text.RegularExpressions;
using ZeroHourStudio.Infrastructure.Archives;
using ZeroHourStudio.Infrastructure.Transfer;

namespace ZeroHourStudio.Infrastructure.Services;

/// <summary>
/// نوع اللعبة الهدف
/// </summary>
public enum GameType
{
    Unknown,
    VanillaGenerals,
    VanillaZeroHour,
    ContraMod,
    ShockwaveMod,
    RiseOfTheRedsMod,
    ProjectXMod,
    CustomMod
}

/// <summary>
/// معلومات سلاح واحد
/// </summary>
public class WeaponInfo
{
    public string Name { get; set; } = string.Empty;
    public string? PrimaryDamage { get; set; }
    public string? PrimaryDamageRadius { get; set; }
    public string? AttackRange { get; set; }
    public string? RateOfFire { get; set; }
    public string? ProjectileObject { get; set; }
    public string? FireFX { get; set; }
    public string? SourceFilePath { get; set; }
}

/// <summary>
/// معلومات وحدة واحدة
/// </summary>
public class UnitInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Side { get; set; }
    public string? BuildCost { get; set; }
    public string? BuildTime { get; set; }
    public string? MaxHealth { get; set; }
    public string? KindOf { get; set; }
    public string? CommandSet { get; set; }
    public string? SourceFilePath { get; set; }
    public List<string> Weapons { get; set; } = new();
    public List<string> Armors { get; set; } = new();
}

/// <summary>
/// معلومات فصيل واحد
/// </summary>
public class FactionInfo2
{
    public string InternalName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Side { get; set; }
    public int UnitCount { get; set; }
}

/// <summary>
/// فهرس الأسلحة
/// </summary>
public class WeaponIndex
{
    public Dictionary<string, WeaponInfo> Weapons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Count => Weapons.Count;
}

/// <summary>
/// فهرس الوحدات
/// </summary>
public class UnitIndex
{
    public Dictionary<string, UnitInfo> Units { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Count => Units.Count;
}

/// <summary>
/// فهرس الفصائل
/// </summary>
public class FactionIndex
{
    public Dictionary<string, FactionInfo2> Factions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Count => Factions.Count;
}

/// <summary>
/// بنية مجلدات اللعبة
/// </summary>
public class GameStructure
{
    public string DataIniPath { get; set; } = string.Empty;
    public string ArtPath { get; set; } = string.Empty;
    public bool HasLooseIniFiles { get; set; }
    public List<string> BigArchives { get; set; } = new();
    public List<string> IniFiles { get; set; } = new();
    public long TotalSizeBytes { get; set; }
}

/// <summary>
/// ملف تعريف اللعبة الهدف الكامل
/// </summary>
public class TargetGameProfile
{
    public string GamePath { get; set; } = string.Empty;
    public GameType Type { get; set; } = GameType.Unknown;
    public string Version { get; set; } = string.Empty;
    public WeaponIndex WeaponIndex { get; set; } = new();
    public UnitIndex UnitIndex { get; set; } = new();
    public FactionIndex FactionIndex { get; set; } = new();
    public GameStructure Structure { get; set; } = new();
    public DateTime AnalyzedAt { get; set; }
    public TimeSpan AnalysisDuration { get; set; }

    public string Summary =>
        $"{Type} | {UnitIndex.Count} وحدة | {WeaponIndex.Count} سلاح | {FactionIndex.Count} فصيل | {Structure.BigArchives.Count} أرشيف";
}

/// <summary>
/// محلل اللعبة الهدف — يبني ملف تعريف شامل للمود الهدف
/// يكشف النوع، يبني فهارس الأسلحة/الوحدات/الفصائل، ويحلل البنية
/// </summary>
public class GameTargetAnalyzer
{
    private readonly SageIniMerger _parser = new();

    private static readonly Regex ObjectHeaderRegex = new(
        @"^\s*Object\s+(\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WeaponHeaderRegex = new(
        @"^\s*Weapon\s+(\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlayerTemplateRegex = new(
        @"^\s*PlayerTemplate\s+(\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex KeyValueRegex = new(
        @"^\s*(\w+)\s*=\s*(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// تحليل شامل للمود الهدف
    /// </summary>
    public async Task<TargetGameProfile> AnalyzeAsync(string gamePath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var profile = new TargetGameProfile
        {
            GamePath = gamePath,
            AnalyzedAt = DateTime.UtcNow
        };

        try
        {
            // 1. كشف النوع
            profile.Type = DetectGameType(gamePath);

            // 2. تحليل البنية
            profile.Structure = AnalyzeStructure(gamePath);

            // 3. بناء الفهارس من ملفات INI المفكوكة
            await BuildIndexesFromLooseFiles(gamePath, profile);

            // 4. بناء الفهارس من أرشيفات BIG
            await BuildIndexesFromBigArchives(gamePath, profile);

            // 5. كشف الإصدار
            profile.Version = DetectVersion(gamePath, profile.Type);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameTargetAnalyzer] Error: {ex.Message}");
        }

        sw.Stop();
        profile.AnalysisDuration = sw.Elapsed;
        System.Diagnostics.Debug.WriteLine(
            $"[GameTargetAnalyzer] Analysis complete in {sw.ElapsedMilliseconds}ms: {profile.Summary}");

        return profile;
    }

    // =========================================
    // === كشف نوع اللعبة ===
    // =========================================

    private static GameType DetectGameType(string path)
    {
        // فحص ملفات تنفيذية
        if (File.Exists(Path.Combine(path, "generals.exe")))
        {
            if (File.Exists(Path.Combine(path, "game.dat")))
                return GameType.VanillaZeroHour;
            return GameType.VanillaGenerals;
        }

        // فحص أرشيفات مودات معروفة
        var bigFiles = Directory.Exists(path)
            ? Directory.GetFiles(path, "*.big", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToList()
            : new List<string?>();

        foreach (var bigName in bigFiles)
        {
            if (bigName == null) continue;
            var lower = bigName.ToLowerInvariant();

            if (lower.Contains("contra"))
                return GameType.ContraMod;
            if (lower.Contains("shockwave") || lower.Contains("swmod"))
                return GameType.ShockwaveMod;
            if (lower.Contains("rotr") || lower.Contains("riseofthereds"))
                return GameType.RiseOfTheRedsMod;
            if (lower.Contains("projectx"))
                return GameType.ProjectXMod;
        }

        // فحص مجلد Data\INI
        if (Directory.Exists(Path.Combine(path, "Data", "INI")))
            return GameType.CustomMod;

        return GameType.Unknown;
    }

    // =========================================
    // === تحليل البنية ===
    // =========================================

    private static GameStructure AnalyzeStructure(string gamePath)
    {
        var structure = new GameStructure();

        // مسار Data\INI
        var dataIniPath = Path.Combine(gamePath, "Data", "INI");
        structure.DataIniPath = dataIniPath;
        structure.HasLooseIniFiles = Directory.Exists(dataIniPath);

        // مسار Art
        structure.ArtPath = Path.Combine(gamePath, "Art");

        // أرشيفات BIG
        if (Directory.Exists(gamePath))
        {
            structure.BigArchives = Directory.GetFiles(gamePath, "*.big", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .ToList();
        }

        // ملفات INI المفكوكة
        if (structure.HasLooseIniFiles)
        {
            structure.IniFiles = Directory.GetFiles(dataIniPath, "*.ini", SearchOption.AllDirectories)
                .ToList();
            structure.TotalSizeBytes = structure.IniFiles.Sum(f => new FileInfo(f).Length);
        }

        return structure;
    }

    // =========================================
    // === بناء الفهارس من ملفات مفكوكة ===
    // =========================================

    private async Task BuildIndexesFromLooseFiles(string gamePath, TargetGameProfile profile)
    {
        var iniDirs = new[]
        {
            Path.Combine(gamePath, "Data", "INI"),
            Path.Combine(gamePath, "INI"),
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
                    ParseLinesIntoIndexes(lines, file, profile);
                }
                catch { /* skip unreadable */ }
            }
        }
    }

    // =========================================
    // === بناء الفهارس من أرشيفات BIG ===
    // =========================================

    private async Task BuildIndexesFromBigArchives(string gamePath, TargetGameProfile profile)
    {
        if (!Directory.Exists(gamePath)) return;

        var bigFiles = Directory.GetFiles(gamePath, "*.big", SearchOption.TopDirectoryOnly)
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
                        ParseLinesIntoIndexes(lines, $"{bigFile}::{entry}", profile);
                    }
                    catch { /* skip */ }
                }
            }
            catch { /* skip unreadable archives */ }
        }
    }

    // =========================================
    // === تحليل الأسطر إلى فهارس ===
    // =========================================

    private void ParseLinesIntoIndexes(string[] lines, string sourceFile, TargetGameProfile profile)
    {
        string? currentBlockType = null;
        string? currentBlockName = null;
        int depth = 0;
        var currentProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentWeapons = new List<string>();
        var currentArmors = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("//"))
                continue;

            // بداية بلوك عند depth=0
            if (depth == 0)
            {
                if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
                    continue;

                var objMatch = ObjectHeaderRegex.Match(trimmed);
                if (objMatch.Success && !trimmed.StartsWith("ObjectCreation", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.StartsWith("ObjectStatus", StringComparison.OrdinalIgnoreCase))
                {
                    currentBlockType = "Object";
                    currentBlockName = objMatch.Groups[1].Value;
                    depth = 1;
                    currentProps.Clear();
                    currentWeapons.Clear();
                    currentArmors.Clear();
                    continue;
                }

                var wpnMatch = WeaponHeaderRegex.Match(trimmed);
                if (wpnMatch.Success)
                {
                    currentBlockType = "Weapon";
                    currentBlockName = wpnMatch.Groups[1].Value;
                    depth = 1;
                    currentProps.Clear();
                    continue;
                }

                var ptMatch = PlayerTemplateRegex.Match(trimmed);
                if (ptMatch.Success)
                {
                    currentBlockType = "PlayerTemplate";
                    currentBlockName = ptMatch.Groups[1].Value;
                    depth = 1;
                    currentProps.Clear();
                    continue;
                }

                // أي بلوك آخر — تتبع العمق فقط
                if (!trimmed.Contains('='))
                {
                    var fw = trimmed.Split(' ', '\t')[0];
                    if (fw.Length > 1 && char.IsUpper(fw[0]))
                    {
                        currentBlockType = null;
                        currentBlockName = null;
                        depth = 1;
                        continue;
                    }
                }
                continue;
            }

            // End
            if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                depth--;
                if (depth <= 0)
                {
                    // حفظ البلوك المكتمل في الفهرس المناسب
                    if (currentBlockType == "Object" && currentBlockName != null)
                    {
                        if (!profile.UnitIndex.Units.ContainsKey(currentBlockName))
                        {
                            profile.UnitIndex.Units[currentBlockName] = new UnitInfo
                            {
                                Name = currentBlockName,
                                Side = currentProps.GetValueOrDefault("Side"),
                                BuildCost = currentProps.GetValueOrDefault("BuildCost"),
                                BuildTime = currentProps.GetValueOrDefault("BuildTime"),
                                MaxHealth = currentProps.GetValueOrDefault("MaxHealth"),
                                KindOf = currentProps.GetValueOrDefault("KindOf"),
                                CommandSet = currentProps.GetValueOrDefault("CommandSet"),
                                SourceFilePath = sourceFile,
                                Weapons = new List<string>(currentWeapons),
                                Armors = new List<string>(currentArmors),
                            };
                        }
                    }
                    else if (currentBlockType == "Weapon" && currentBlockName != null)
                    {
                        if (!profile.WeaponIndex.Weapons.ContainsKey(currentBlockName))
                        {
                            profile.WeaponIndex.Weapons[currentBlockName] = new WeaponInfo
                            {
                                Name = currentBlockName,
                                PrimaryDamage = currentProps.GetValueOrDefault("PrimaryDamage"),
                                PrimaryDamageRadius = currentProps.GetValueOrDefault("PrimaryDamageRadius"),
                                AttackRange = currentProps.GetValueOrDefault("AttackRange"),
                                RateOfFire = currentProps.GetValueOrDefault("DelayBetweenShots"),
                                ProjectileObject = currentProps.GetValueOrDefault("ProjectileObject"),
                                FireFX = currentProps.GetValueOrDefault("FireFX"),
                                SourceFilePath = sourceFile,
                            };
                        }
                    }
                    else if (currentBlockType == "PlayerTemplate" && currentBlockName != null)
                    {
                        if (!profile.FactionIndex.Factions.ContainsKey(currentBlockName))
                        {
                            profile.FactionIndex.Factions[currentBlockName] = new FactionInfo2
                            {
                                InternalName = currentBlockName,
                                DisplayName = currentProps.GetValueOrDefault("DisplayName"),
                                Side = currentProps.GetValueOrDefault("Side"),
                            };
                        }
                    }

                    currentBlockType = null;
                    currentBlockName = null;
                    depth = 0;
                }
                continue;
            }

            // بلوكات فرعية
            if (!trimmed.Contains('='))
            {
                if (!trimmed.Contains(' '))
                {
                    // كلمة واحدة — بلوك فرعي معروف
                    var singleBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "DefaultConditionState", "ConditionState", "TransitionState",
                        "ModelConditionState", "AnimationState", "IdleAnimationState",
                        "Prerequisites", "UnitSpecificSounds", "UnitSpecificFX",
                    };
                    if (singleBlocks.Contains(trimmed))
                    {
                        depth++;
                        continue;
                    }
                }
                else
                {
                    var fw = trimmed.Split(' ', '\t')[0];
                    if (fw.Length > 1 && char.IsUpper(fw[0]))
                    {
                        depth++;

                        // استخراج أسماء الأسلحة والدروع من بلوكات فرعية
                        if (currentBlockType == "Object" && depth == 2)
                        {
                            if (fw.EndsWith("Weapon", StringComparison.OrdinalIgnoreCase) ||
                                fw.Equals("PrimaryWeapon", StringComparison.OrdinalIgnoreCase))
                            {
                                var wpnName = trimmed[(fw.Length)..].Trim().Split(' ', ';')[0];
                                if (!string.IsNullOrWhiteSpace(wpnName))
                                    currentWeapons.Add(wpnName);
                            }
                        }
                        continue;
                    }
                }
            }

            // key=value — حفظ فقط عند depth=1 (أبناء مباشرون)
            if (depth == 1 && currentBlockName != null)
            {
                var kvMatch = KeyValueRegex.Match(trimmed);
                if (kvMatch.Success)
                {
                    var key = kvMatch.Groups[1].Value;
                    var val = kvMatch.Groups[2].Value.Trim();
                    var commentIdx = val.IndexOf(';');
                    if (commentIdx > 0) val = val[..commentIdx].Trim();
                    currentProps[key] = val;

                    // استخراج أسماء الأسلحة من key=value
                    if (currentBlockType == "Object")
                    {
                        if (key.Equals("Weapon", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("PrimaryWeapon", StringComparison.OrdinalIgnoreCase) ||
                            key.Equals("SecondaryWeapon", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrWhiteSpace(val))
                                currentWeapons.Add(val);
                        }
                        else if (key.Equals("Armor", StringComparison.OrdinalIgnoreCase) ||
                                 key.Equals("ArmorSet", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrWhiteSpace(val))
                                currentArmors.Add(val);
                        }
                    }
                }
            }
        }
    }

    // =========================================
    // === كشف الإصدار ===
    // =========================================

    private static string DetectVersion(string gamePath, GameType type)
    {
        // فحص game.dat
        var gameDat = Path.Combine(gamePath, "game.dat");
        if (File.Exists(gameDat))
        {
            try
            {
                var info = new FileInfo(gameDat);
                return $"{type} ({info.Length / 1024}KB)";
            }
            catch { }
        }

        // فحص generals.exe
        var exe = Path.Combine(gamePath, "generals.exe");
        if (File.Exists(exe))
        {
            try
            {
                var ver = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrEmpty(ver.FileVersion))
                    return ver.FileVersion;
            }
            catch { }
        }

        return type.ToString();
    }

    // =========================================
    // === فحص التعارضات مع المود الهدف ===
    // =========================================

    /// <summary>
    /// فحص تعارضات الأسماء بين بلوكات المصدر وفهارس الهدف
    /// </summary>
    public List<TransferConflict> CheckConflicts(
        IEnumerable<IniSection> sourceSections,
        TargetGameProfile target)
    {
        var conflicts = new List<TransferConflict>();

        foreach (var section in sourceSections)
        {
            bool existsInTarget = false;
            string conflictType = "";

            switch (section.Type)
            {
                case SectionType.Weapon:
                    existsInTarget = target.WeaponIndex.Weapons.ContainsKey(section.Name);
                    conflictType = "سلاح";
                    break;
                case SectionType.Object:
                    existsInTarget = target.UnitIndex.Units.ContainsKey(section.Name);
                    conflictType = "وحدة";
                    break;
                default:
                    // فحص عام — نبحث في كل الفهارس
                    existsInTarget = target.WeaponIndex.Weapons.ContainsKey(section.Name) ||
                                     target.UnitIndex.Units.ContainsKey(section.Name);
                    conflictType = section.TypeName;
                    break;
            }

            if (existsInTarget)
            {
                conflicts.Add(new TransferConflict
                {
                    Name = section.Name,
                    Type = conflictType,
                    SuggestedRename = $"ZHS_{section.Name}",
                    IsIdentical = false, // يحتاج فحص أعمق
                });
            }
        }

        return conflicts;
    }
}

/// <summary>
/// تعارض نقل واحد
/// </summary>
public class TransferConflict
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SuggestedRename { get; set; } = string.Empty;
    public bool IsIdentical { get; set; }
    public string Message => IsIdentical
        ? $"{Type} '{Name}' موجود ومتطابق — سيتم تجاوزه"
        : $"{Type} '{Name}' موجود لكن مختلف — يُقترح: {SuggestedRename}";
}
