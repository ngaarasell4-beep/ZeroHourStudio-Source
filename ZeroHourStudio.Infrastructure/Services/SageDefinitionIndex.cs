using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ZeroHourStudio.Infrastructure.Logging;

namespace ZeroHourStudio.Infrastructure.Services;

/// <summary>
/// Indexes all SAGE engine definition blocks (Weapon, FXList, ObjectCreationList, 
/// ParticleSystem, Armor, AudioEvent, Locomotor, etc.) from INI files and BIG archives.
/// Provides fast lookup by block name for dependency chain traversal.
/// </summary>
public class SageDefinitionIndex
{
    /// <summary>
    /// Maps definition name (e.g. "IskanderMissileWeapon") to its raw INI content and metadata.
    /// </summary>
    private readonly ConcurrentDictionary<string, SageDefinition> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SageDefinition> _mergedCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Known block type prefixes in SAGE INI files.
    /// </summary>
    private static readonly HashSet<string> BlockTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Weapon", "FXList", "ObjectCreationList", "ParticleSystem",
        "Armor", "Locomotor", "SpecialPower", "Upgrade",
        "CommandButton", "CommandSet", "Science",
        "Object", "ObjectReskin", "AudioEvent", "Projectile",
        "MappedImage", "PlayerTemplate", "ModifierList",
        "DamageFX", "DrawGroupInfo", "CrateData"
    };

    public int Count => _index.Count;

    /// <summary>
    /// Look up a SAGE definition by name.
    /// </summary>
    public SageDefinition? Lookup(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        _index.TryGetValue(name.Trim(), out var def);
        return def;
    }

    /// <summary>
    /// دمج الوراثة (InheritFrom): يعيد تعريفًا مدمجًا يجمع محتوى الأب ثم الابن.
    /// </summary>
    public SageDefinition? GetMergedDefinition(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (_mergedCache.TryGetValue(name.Trim(), out var cached)) return cached;

        var def = Lookup(name);
        if (def == null) return null;

        var parentName = ExtractInheritFrom(def.RawContent);
        if (string.IsNullOrWhiteSpace(parentName))
        {
            _mergedCache[name] = def;
            return def;
        }

        var parentDef = GetMergedDefinition(parentName);
        if (parentDef == null)
        {
            _mergedCache[name] = def;
            return def;
        }

        var merged = new SageDefinition
        {
            Name = def.Name,
            BlockType = def.BlockType,
            SourceFile = def.SourceFile,
            IsHighPriority = def.IsHighPriority,
            RawContent = parentDef.RawContent + "\n" + def.RawContent
        };

        _mergedCache[name] = merged;
        return merged;
    }

    /// <summary>
    /// Check if a definition exists.
    /// </summary>
    public bool Contains(string name) => !string.IsNullOrWhiteSpace(name) && _index.ContainsKey(name.Trim());

    /// <summary>
    /// Get all indexed definitions (for scanning CommandButtons, etc.)
    /// </summary>
    public IEnumerable<SageDefinition> GetAllDefinitions() => _index.Values;

    /// <summary>
    /// Build index from all INI files in the mod path (loose files + BIG archives).
    /// </summary>
    public async Task BuildIndexAsync(string modPath)
    {
        _index.Clear();
        _mergedCache.Clear();
        var buildSw = System.Diagnostics.Stopwatch.StartNew();
        BlackBoxRecorder.Record("SAGE_INDEX", "BUILD_START", $"Path={modPath}");

        // 1. Index loose INI files
        var iniDir = Path.Combine(modPath, "Data", "INI");
        if (Directory.Exists(iniDir))
        {
            var iniFiles = Directory.GetFiles(iniDir, "*.ini", SearchOption.AllDirectories);
            await Task.Run(() =>
            {
                Parallel.ForEach(iniFiles, file =>
                {
                    try
                    {
                        var content = File.ReadAllText(file, Encoding.GetEncoding(1252));
                        ParseBlocks(content, file, false);
                    }
                    catch { /* skip unreadable files */ }
                });
            });
        }

        // Also check INI directly under modPath
        var topIniDir = Path.Combine(modPath, "INI");
        if (Directory.Exists(topIniDir))
        {
            var iniFiles = Directory.GetFiles(topIniDir, "*.ini", SearchOption.AllDirectories);
            await Task.Run(() =>
            {
                Parallel.ForEach(iniFiles, file =>
                {
                    try
                    {
                        var content = File.ReadAllText(file, Encoding.GetEncoding(1252));
                        ParseBlocks(content, file, false);
                    }
                    catch { }
                });
            });
        }

        // 2. Index BIG archive INI entries
        var bigFiles = Directory.GetFiles(modPath, "*.big", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.Contains("!!") ? 1 : 0) // !! archives last (highest priority override)
            .ToList();

        foreach (var bigFile in bigFiles)
        {
            try
            {
                using var mgr = new Archives.BigArchiveManager(bigFile);
                await mgr.LoadAsync();
                var entries = mgr.GetFileList()
                    .Where(e => e.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var entry in entries)
                {
                    try
                    {
                        var data = await mgr.ExtractFileAsync(entry);
                        var content = Encoding.GetEncoding(1252).GetString(data);
                        var source = $"{bigFile}::{entry}";
                        bool isHighPriority = Path.GetFileName(bigFile).StartsWith("!!");
                        ParseBlocks(content, source, isHighPriority);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static string? ExtractInheritFrom(string rawContent)
    {
        var lines = rawContent.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith(";")) continue;
            if (line.StartsWith("//")) continue;

            var eqIdx = line.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = line.Substring(0, eqIdx).Trim();
                if (!key.Equals("InheritFrom", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line.Substring(eqIdx + 1).Trim();
                var cIdx = value.IndexOf(';');
                if (cIdx >= 0) value = value.Substring(0, cIdx).Trim();
                cIdx = value.IndexOf("//", StringComparison.Ordinal);
                if (cIdx >= 0) value = value.Substring(0, cIdx).Trim();
                return value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            }
            else
            {
                var sp = line.IndexOf(' ');
                if (sp <= 0) continue;
                var key = line.Substring(0, sp).Trim();
                if (!key.Equals("InheritFrom", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line.Substring(sp + 1).Trim();
                var cIdx = value.IndexOf(';');
                if (cIdx >= 0) value = value.Substring(0, cIdx).Trim();
                cIdx = value.IndexOf("//", StringComparison.Ordinal);
                if (cIdx >= 0) value = value.Substring(0, cIdx).Trim();
                return value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            }
        }

        return null;
    }

    /// <summary>
    /// Parse all named blocks from INI content.
    /// Blocks follow SAGE format: "BlockType BlockName" ... "End"
    /// </summary>
    private void ParseBlocks(string content, string sourceFile, bool isHighPriority)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i].Trim();

            // Skip comments and empty lines
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("//"))
            {
                i++;
                continue;
            }

            // Check for block header: "BlockType BlockName"
            var spaceIdx = line.IndexOf(' ');
            if (spaceIdx <= 0)
            {
                i++;
                continue;
            }

            var blockType = line.Substring(0, spaceIdx).Trim();
            if (!BlockTypes.Contains(blockType))
            {
                i++;
                continue;
            }

            // Extract block name (may have additional tokens like "ModuleTag_01")
            var rest = line.Substring(spaceIdx + 1).Trim();
            // Strip comments from the header line
            var commentIdx = rest.IndexOf(';');
            if (commentIdx >= 0) rest = rest.Substring(0, commentIdx).Trim();
            commentIdx = rest.IndexOf("//", StringComparison.Ordinal);
            if (commentIdx >= 0) rest = rest.Substring(0, commentIdx).Trim();

            var blockName = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(blockName))
            {
                i++;
                continue;
            }

            // Collect block content until matching "End"
            int depth = 0;
            int startLine = i;
            var blockLines = new List<string>();
            blockLines.Add(lines[i]);
            i++;

            while (i < lines.Length)
            {
                var cur = lines[i].Trim();
                blockLines.Add(lines[i]);

                if (cur.Equals("End", StringComparison.OrdinalIgnoreCase))
                {
                    if (depth == 0)
                    {
                        i++;
                        break;
                    }
                    depth--;
                }
                else
                {
                    // Check for nested block starters
                    var curSpace = cur.IndexOf(' ');
                    var curFirst = curSpace > 0 ? cur.Substring(0, curSpace).Trim() : cur;
                    var curEq = cur.IndexOf('=');
                    if (curEq > 0)
                        curFirst = cur.Substring(0, curEq).Trim();

                    if (IsNestedBlockStart(curFirst, cur))
                        depth++;
                }

                i++;
            }

            var rawContent = string.Join("\n", blockLines);

            var def = new SageDefinition
            {
                Name = blockName,
                BlockType = blockType,
                RawContent = rawContent,
                SourceFile = sourceFile,
                IsHighPriority = isHighPriority
            };

            // High priority or new entries win
            _index.AddOrUpdate(blockName, def, (_, existing) =>
                isHighPriority || !existing.IsHighPriority ? def : existing);
        }
    }

    private static readonly HashSet<string> NestedBlockStarters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sound", "ParticleSystem", "FXParticleSystemTemplate", "DynamicLOD",
        "CreateObject", "CreateDebris", "DeliverPayload",
        "Nugget", "DamageNugget", "MetaImpactNugget", "DOTDamageNugget",
        "AttributeModifierNugget", "SpawnAndFadeNugget", "FireLogicNugget",
        "WeaponOCLNugget", "LuaEventNugget", "DamageFieldNugget",
        "DefaultConditionState", "ConditionState", "TransitionState",
        "ModelConditionState", "AnimationState",
        "Body", "Draw", "Behavior", "WeaponSet", "ArmorSet",
        "LocomotorSet", "Prerequisites", "UnitSpecificSounds",
        "Turret", "FireWeaponUpdate", "AIUpdate",
        "Modifier", "Duration", "ReallyDamagedParticleSystem1",
        "TerrainSound", "Waypoint",
    };

    private static bool IsNestedBlockStart(string firstWord, string fullLine)
    {
        if (NestedBlockStarters.Contains(firstWord)) return true;
        // SAGE convention: nested blocks often end with known suffixes
        if (firstWord.EndsWith("Body", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("Draw", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("Behavior", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("Update", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("Die", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("Contain", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("Module", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("Collide", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("State", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("Sounds", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("Upgrade", StringComparison.OrdinalIgnoreCase) ||
            firstWord.EndsWith("Ability", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Extract all references from a definition block for dependency traversal.
    /// Returns (ReferenceType, ReferenceName) pairs.
    /// </summary>
    public static List<SageReference> ExtractReferences(SageDefinition def)
    {
        var refs = new List<SageReference>();
        var lines = def.RawContent.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("//")) continue;

            // Strip comments
            var commentIdx = line.IndexOf(';');
            if (commentIdx >= 0) line = line.Substring(0, commentIdx).Trim();
            commentIdx = line.IndexOf("//", StringComparison.Ordinal);
            if (commentIdx >= 0) line = line.Substring(0, commentIdx).Trim();
            if (line.Length == 0) continue;

            // Parse key = value or key value
            string key, value;
            var eqIdx = line.IndexOf('=');
            if (eqIdx > 0)
            {
                key = line.Substring(0, eqIdx).Trim();
                value = line.Substring(eqIdx + 1).Trim();
            }
            else
            {
                var spIdx = line.IndexOf(' ');
                if (spIdx <= 0) continue;
                key = line.Substring(0, spIdx).Trim();
                value = line.Substring(spIdx + 1).Trim();
            }

            // Match known reference types
            var refType = ClassifyReference(key, value);
            if (refType == SageRefType.None) continue;

            // Extract the actual reference name(s) from the value
            var names = ExtractNames(key, value, refType);
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    refs.Add(new SageReference(refType, name.Trim(), key));
            }
        }

        return refs;
    }

    private static SageRefType ClassifyReference(string key, string value)
    {
        var k = key.ToUpperInvariant();

        // Weapon references
        if (k == "WEAPON" || k.Contains("WEAPON") && !k.Contains("WEAPONFIRE") && !k.Contains("WEAPONMUZZLE") && !k.Contains("WEAPONRECOIL") && !k.Contains("WEAPONLAUNCH"))
            return SageRefType.Weapon;

        // Projectile
        if (k == "PROJECTILEOBJECT" || k == "PROJECTILE" || k == "PROJECTILETEMPLATE")
            return SageRefType.Object;

        // FX references (specific keys only)
        if (k == "FXLIST" || k.StartsWith("FX_") || k.EndsWith("FX") || k.EndsWith("FXLIST"))
            return SageRefType.FXList;

        // OCL references
        if (k.Contains("OCL") || k.Contains("CREATIONLIST"))
            return SageRefType.OCL;

        // Sound/Audio references (NOT "NAME" - too generic)
        if (k.Contains("SOUND") || k.Contains("VOICE") || k.Contains("AUDIO") || k.Contains("AUDIOEVENT") || k.Contains("SOUNDEVENT"))
            return SageRefType.Audio;

        // Model references (W3D files - specific keys only)
        if (k == "MODEL" || k == "MODELNAME" || k == "MODELNAMES")
            return SageRefType.Model;

        // Texture references (including particle textures)
        if (k == "TEXTURE" || k.Contains("TEXTURE") || k == "TRACKMARKS" || k.Contains("PARTICLETEXTURE"))
            return SageRefType.Texture;

        // Armor
        if (k == "ARMOR")
            return SageRefType.Armor;

        // Locomotor
        if (k == "LOCOMOTOR")
            return SageRefType.Locomotor;

        // Object references (including inheritance + PlayerTemplate buildables)
        if (k == "OBJECTNAMES" || k == "OBJECTNAME" || k == "OBJECT" || k == "UNITNAME" || k == "INHERITFROM" ||
            k == "BUILDABLEOBJECT" || k == "BUILDABLEOBJECTS")
            return SageRefType.Object;

        // CommandSet
        if (k == "COMMANDSET")
            return SageRefType.CommandSet;

        // Science/Upgrade
        if (k.Contains("UPGRADE") || k.Contains("TRIGGEREDBY"))
            return SageRefType.Upgrade;

        // ParticleSystem (from inside FXList blocks)
        if (k == "PARTICLESYSTEM" || k.Contains("PARTICLESYS"))
            return SageRefType.ParticleSystem;

        // Image references
        if (k == "BUTTONIMAGE" || k == "SELECTPORTRAIT" || k == "PORTRAITIMAGE")
            return SageRefType.Image;

        // Animation
        if (k == "ANIMATION" || k == "IDLEANIMATION")
            return SageRefType.Model;

        return SageRefType.None;
    }

    private static List<string> ExtractNames(string key, string value, SageRefType refType)
    {
        var names = new List<string>();
        var parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        switch (refType)
        {
            case SageRefType.Weapon:
                // "Weapon = PRIMARY IskanderMissileWeapon" or "Weapon = HumveeGun"
                // Skip slot keywords (PRIMARY/SECONDARY/TERTIARY)
                foreach (var p in parts)
                {
                    if (IsSlotKeyword(p)) continue;
                    if (IsCommonKeyword(p)) continue;
                    names.Add(p);
                }
                break;

            case SageRefType.FXList:
                // "FireFX = FX_IskanderLaunch" or "FX = INITIAL FX_Something"
                foreach (var p in parts)
                {
                    if (IsSlotKeyword(p) || IsStageKeyword(p)) continue;
                    if (IsCommonKeyword(p)) continue;
                    names.Add(p);
                }
                break;

            case SageRefType.OCL:
                // "FireOCL = OCL_SomeThing" or "OCL = INITIAL OCL_Something"
                foreach (var p in parts)
                {
                    if (IsStageKeyword(p) || IsSlotKeyword(p) || IsCommonKeyword(p)) continue;
                    names.Add(p);
                }
                break;

            case SageRefType.Locomotor:
                // "Locomotor = SET_NORMAL HumveeLocomotor"
                foreach (var p in parts)
                {
                    if (p.StartsWith("SET_", StringComparison.OrdinalIgnoreCase)) continue;
                    names.Add(p);
                }
                break;

            case SageRefType.Model:
                // "Model = AVCrusader" or "ModelNames = PMBarrel01_D1"
                foreach (var p in parts)
                {
                    if (IsCommonKeyword(p)) continue;
                    names.Add(p);
                }
                break;

            case SageRefType.Object:
                // ObjectNames can list multiple objects - capture all
                foreach (var p in parts)
                {
                    if (IsStageKeyword(p) || IsSlotKeyword(p) || IsCommonKeyword(p)) continue;
                    names.Add(p);
                }
                break;

            default:
                // Take the first meaningful token
                if (parts.Length > 0)
                {
                    foreach (var p in parts)
                    {
                        if (IsStageKeyword(p) || IsSlotKeyword(p)) continue;
                        names.Add(p);
                        break; // usually just one name
                    }
                }
                break;
        }

        return names;
    }

    private static bool IsSlotKeyword(string s) =>
        s.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("SECONDARY", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("TERTIARY", StringComparison.OrdinalIgnoreCase);

    private static bool IsStageKeyword(string s) =>
        s.Equals("INITIAL", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("FINAL", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("MIDPOINT", StringComparison.OrdinalIgnoreCase);

    private static bool IsCommonKeyword(string s) =>
        s.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("No", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("True", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("False", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("None", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("End", StringComparison.OrdinalIgnoreCase) ||
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
}

public class SageDefinition
{
    public string Name { get; set; } = "";
    public string BlockType { get; set; } = "";
    public string RawContent { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public bool IsHighPriority { get; set; }
}

public enum SageRefType
{
    None, Weapon, FXList, OCL, Audio, Model, Texture,
    Armor, Locomotor, Object, CommandSet, Upgrade,
    ParticleSystem, Image, Science
}

public record SageReference(SageRefType Type, string Name, string SourceKey);
