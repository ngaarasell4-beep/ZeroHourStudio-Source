using System.Text;
using ZeroHourStudio.Infrastructure.Archives;

namespace ZeroHourStudio.Infrastructure.Validation;

/// <summary>
/// مشكلة في ملف INI
/// </summary>
public class IniIssue
{
    public string FileName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string IssueType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IniIssueSeverity Severity { get; set; }
}

public enum IniIssueSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>
/// تقرير فحص INI
/// </summary>
public class IniValidationReport
{
    public List<IniIssue> Issues { get; set; } = new();
    public int FilesScanned { get; set; }
    public int TotalErrors => Issues.Count(i => i.Severity == IniIssueSeverity.Error);
    public int TotalWarnings => Issues.Count(i => i.Severity == IniIssueSeverity.Warning);
    public bool IsClean => TotalErrors == 0;
}

/// <summary>
/// محلل صياغة ملفات INI - يكتشف:
/// 1. أقواس End المفقودة أو الزائدة
/// 2. أسماء تعريفات مكررة (Object, Weapon, etc.)
/// 3. أسطر غير صالحة داخل البلوكات
/// </summary>
public class IniSyntaxValidator
{
    // Top-level block types that open with "BlockType Name" and close with "End"
    private static readonly HashSet<string> BlockTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Objects
        "Object", "ObjectReskin", "ChildObject",
        // Weapons & Combat
        "Weapon", "Armor", "DamageFX", "FXList", "FXListAtBonePos",
        "ObjectCreationList", "ParticleSystem",
        // Locomotion
        "Locomotor", "LocomotorTemplate",
        // Commands
        "CommandButton", "CommandSet", "CommandMap",
        // Upgrades & Powers
        "SpecialPower", "Upgrade", "Science", "ExperienceLevel",
        // Players & Factions
        "PlayerTemplate", "Team", "SidesList",
        // Audio
        "AudioEvent", "DialogEvent", "MusicTrack", "SoundEffects",
        "MultisoundSoundBankNugget", "Multisound",
        // UI & Images
        "MappedImage", "Animation", "Animation2D", "AnimationSoundClientBehavior",
        "WindowTransition", "HeaderTemplate", "DrawGroupInfo",
        // AI
        "AIData", "SkirmishAIData", "AIBase", "AIPersonality",
        // Misc game data
        "CrateData", "ModifierList", "CrowdResponse",
        "Bridge", "Road", "Weather", "WaterSet", "WaterTransparency",
        "Fire", "FireEffect", "LargeGroupAudioMap", "EvaEvent",
        "Rank", "ChallengeGenerals", "StanceTemplate",
        "ControlBarScheme", "ControlBarResizer", "ShellMenuScheme",
        "ArmySummaryDescription", "Campaign", "Mission",
        "VideoEvent", "Credits", "GameData", "InGameUI",
        "Mouse", "MouseCursor", "FontDefaultSettings", "FontSubstitution",
        "HeaderTemplate", "WebpageURL", "Language",
        "StaticGameLOD", "DynamicGameLOD", "LODPreset", "BenchProfile",
        "Terrain", "LivingWorldRegionCampaign", "MetaMapData",
        "MultiplayerSettings", "MultiplayerColor", "OnlineChatColors",
        // Worldbuilder
        "LightPointLevel",
    };

    // Sub-block keywords that open a nested block inside a top-level block
    private static readonly HashSet<string> SubBlockStarters = new(StringComparer.OrdinalIgnoreCase)
    {
        // Module system
        "Body", "Draw", "Behavior", "ClientUpdate", "ClientBehavior",
        "Turret", "AddModule", "ReplaceModule", "RemoveModule", "InheritableModule",
        // Sets
        "WeaponSet", "ArmorSet", "LocomotorSet", "Prerequisites",
        // Draw states
        "DefaultConditionState", "ConditionState", "TransitionState",
        "ModelConditionState", "AnimationState", "IdleAnimationState",
        // FX internals
        "Nugget", "DamageNugget", "WeaponOCLNugget", "MetaImpactNugget",
        "FXNugget", "AttackNugget", "DOTNugget", "SlowDeathNugget",
        "SpawnAndFadeNugget", "ParalyzeNugget", "EmotionNugget", "AttributeModifierNugget",
        "CurseNugget", "FireLogicNugget", "DamageFieldNugget", "OpenGateNugget",
        "DamageContainedNugget", "StealMoneyNugget", "LuaEventNugget",
        "ProjectileNugget", "HordeTransportNugget", "GarrisonNugget",
        // Audio
        "UnitSpecificSounds", "UnitSpecificFX", "VoiceAttack", "VoiceMove",
        "VoiceSelect", "VoiceCreated", "SoundAmbient",
        // Other sub-blocks
        "FireWeaponNugget", "WeaponBonusSet", "ShadowInfo",
        "Flammability", "ThreatBreakdown", "AutoResolveInfo",
        "ProductionQueue", "BuildableItem", "Side",
        // UI sub-blocks
        "Phase", "Frame", "AnimationFrame",
    };

    // Known key = value prefixes that should NOT be treated as sub-block starters
    private static readonly HashSet<string> KnownKeyPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Prerequisite", "InheritFrom", "Side", "KindOf", "BuildCost", "BuildTime",
        "VisionRange", "MaxHealth", "Speed", "ArmorSet", "WeaponSet",
        "DisplayName", "EditorSorting", "IsTrainable", "Scale",
    };

    /// <summary>
    /// فحص جميع ملفات INI في المود (ملفات مفكوكة + أرشيفات BIG)
    /// </summary>
    public async Task<IniValidationReport> ValidateModAsync(string modPath)
    {
        var report = new IniValidationReport();
        var definitionNames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan loose INI files
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
                    var shortName = Path.GetRelativePath(modPath, file);
                    ValidateFile(lines, shortName, report, definitionNames);
                    report.FilesScanned++;
                }
                catch { /* skip unreadable */ }
            }
        }

        // 2. Scan BIG archives
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
                            var shortName = $"{Path.GetFileName(bigFile)}::{entry}";
                            ValidateFile(lines, shortName, report, definitionNames);
                            report.FilesScanned++;
                        }
                        catch { /* skip */ }
                    }
                }
                catch { /* skip */ }
            }
        }

        // 3. Report duplicate definitions
        foreach (var kvp in definitionNames)
        {
            if (kvp.Value.Count > 1)
            {
                report.Issues.Add(new IniIssue
                {
                    FileName = string.Join(", ", kvp.Value.Distinct()),
                    LineNumber = 0,
                    IssueType = "DUPLICATE_DEFINITION",
                    Message = $"التعريف '{kvp.Key}' مكرر في {kvp.Value.Count} موقع: {string.Join("، ", kvp.Value.Distinct().Take(3))}",
                    Severity = IniIssueSeverity.Warning
                });
            }
        }

        return report;
    }

    /// <summary>
    /// فحص ملف INI واحد
    /// </summary>
    public IniValidationReport ValidateContent(string[] lines, string fileName)
    {
        var report = new IniValidationReport();
        var names = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        ValidateFile(lines, fileName, report, names);
        report.FilesScanned = 1;

        foreach (var kvp in names.Where(k => k.Value.Count > 1))
        {
            report.Issues.Add(new IniIssue
            {
                FileName = fileName,
                IssueType = "DUPLICATE_DEFINITION",
                Message = $"التعريف '{kvp.Key}' مكرر {kvp.Value.Count} مرات في هذا الملف",
                Severity = IniIssueSeverity.Warning
            });
        }

        return report;
    }

    private void ValidateFile(
        string[] lines,
        string fileName,
        IniValidationReport report,
        Dictionary<string, List<string>> definitionNames)
    {
        int depth = 0;
        string? currentBlockType = null;
        string? currentBlockName = null;
        int blockStartLine = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var lineNum = i + 1;
            var trimmed = lines[i].Trim();

            // Skip comments, empty, and preprocessor directives
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("//") || trimmed.StartsWith("#"))
                continue;

            // Check for top-level block start
            if (depth == 0)
            {
                // Stray End at depth 0
                if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
                {
                    report.Issues.Add(new IniIssue
                    {
                        FileName = fileName,
                        LineNumber = lineNum,
                        IssueType = "EXTRA_END",
                        Message = $"'End' زائد بدون بلوك مفتوح (سطر {lineNum})",
                        Severity = IniIssueSeverity.Error
                    });
                    continue;
                }

                // Key=Value lines at top level (e.g. global settings) — skip
                if (trimmed.Contains('='))
                    continue;

                var firstWord = GetFirstWord(trimmed);
                var blockName = trimmed.Length > firstWord.Length
                    ? trimmed[(firstWord.Length)..].Trim()
                    : "";

                // Known block type OR heuristic: "Word Name" pattern (uppercase start, no '=')
                // SAGE INI has hundreds of block types — we accept any that looks like a block header
                bool isBlock = BlockTypes.Contains(firstWord);
                if (!isBlock && firstWord.Length > 1 && char.IsUpper(firstWord[0]) &&
                    !string.IsNullOrWhiteSpace(blockName) &&
                    !blockName.StartsWith(";") && !blockName.StartsWith("//"))
                {
                    isBlock = true;
                }

                if (isBlock)
                {
                    if (BlockTypes.Contains(firstWord) && !string.IsNullOrWhiteSpace(blockName))
                    {
                        // Track definition names for duplicate detection (known types only)
                        var fullKey = $"{firstWord}:{blockName}";
                        if (!definitionNames.TryGetValue(fullKey, out var locations))
                        {
                            locations = new List<string>();
                            definitionNames[fullKey] = locations;
                        }
                        locations.Add(fileName);
                    }

                    currentBlockType = firstWord;
                    currentBlockName = blockName;
                    blockStartLine = lineNum;
                    depth = 1;
                }
                continue;
            }

            // Inside a block
            if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                depth--;
                if (depth < 0)
                {
                    report.Issues.Add(new IniIssue
                    {
                        FileName = fileName,
                        LineNumber = lineNum,
                        IssueType = "EXTRA_END",
                        Message = $"'End' زائد في '{currentBlockType} {currentBlockName}' (سطر {lineNum})",
                        Severity = IniIssueSeverity.Error
                    });
                    depth = 0;
                }
                else if (depth == 0)
                {
                    currentBlockType = null;
                    currentBlockName = null;
                }
                continue;
            }

            // Check for sub-block starters
            var fw = GetFirstWord(trimmed);
            if (IsSubBlockStarter(fw, trimmed))
            {
                depth++;
            }
        }

        // File ended with unclosed block
        if (depth > 0)
        {
            report.Issues.Add(new IniIssue
            {
                FileName = fileName,
                LineNumber = blockStartLine,
                IssueType = "MISSING_END",
                Message = $"بلوك '{currentBlockType} {currentBlockName}' (سطر {blockStartLine}) غير مغلق - 'End' مفقود ({depth} مستوى مفتوح)",
                Severity = IniIssueSeverity.Error
            });
        }
    }

    private static string GetFirstWord(string line)
    {
        var spaceIdx = line.IndexOf(' ');
        var eqIdx = line.IndexOf('=');
        var tabIdx = line.IndexOf('\t');

        var endIdx = line.Length;
        if (spaceIdx > 0 && spaceIdx < endIdx) endIdx = spaceIdx;
        if (eqIdx > 0 && eqIdx < endIdx) endIdx = eqIdx;
        if (tabIdx > 0 && tabIdx < endIdx) endIdx = tabIdx;

        return line[..endIdx].Trim();
    }

    private static bool IsSubBlockStarter(string firstWord, string fullLine)
    {
        // Explicit known sub-blocks
        if (SubBlockStarters.Contains(firstWord)) return true;

        // If the line has '=', it's a key=value pair, not a sub-block
        if (fullLine.Contains('=')) return false;

        // Pattern: "SomethingBody", "SomethingDraw", etc. — SAGE module naming convention
        string[] suffixes = {
            "Body", "Draw", "Behavior", "Update", "Die", "Contain", "Module",
            "Collide", "Sounds", "State", "Create", "Nugget", "Helper",
            "Locomotor", "Special", "Power", "System", "Effect", "FX",
            "Info", "Set", "Data", "Override", "AIUpdate", "Upgrade",
        };
        foreach (var suffix in suffixes)
        {
            if (firstWord.Length > suffix.Length &&
                firstWord.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Top-level block types can also appear as sub-blocks (nested blocks in SAGE)
        if (BlockTypes.Contains(firstWord)) return true;

        // Heuristic: if the line is "Word Word" with no '=' and the first word starts with
        // an uppercase letter, it's very likely a sub-block opener in SAGE INI
        // Only apply inside blocks (caller ensures depth > 0)
        if (firstWord.Length > 2 && char.IsUpper(firstWord[0]))
        {
            var rest = fullLine.Length > firstWord.Length
                ? fullLine[firstWord.Length..].Trim()
                : "";

            // Must have a module tag or type name after the keyword
            if (!string.IsNullOrEmpty(rest) && !rest.StartsWith(";") && !rest.StartsWith("//"))
            {
                // Looks like "ModuleType ModuleTag_Name" — a sub-block
                var restWord = rest.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (restWord.Length >= 1 && restWord[0].Length > 1 && char.IsUpper(restWord[0][0]))
                    return true;
            }
        }

        return false;
    }
}
