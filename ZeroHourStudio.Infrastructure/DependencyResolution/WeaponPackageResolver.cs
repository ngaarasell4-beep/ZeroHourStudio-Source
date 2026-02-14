using System.Text.RegularExpressions;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Filtering;
using ZeroHourStudio.Infrastructure.Logging;
using ZeroHourStudio.Infrastructure.Services;

namespace ZeroHourStudio.Infrastructure.DependencyResolution;

/// <summary>
/// محرك حل حزم الأسلحة.
/// يبدأ من Object → WeaponSet → Weapon → Projectile → FX → Audio.
/// يرفض أي سلاح ناقص. يمنع انفجار التبعيات.
/// </summary>
public sealed class WeaponPackageResolver
{
    private readonly SageDefinitionIndex _sageIndex;
    private readonly IBigFileReader _bigFileReader;
    private readonly Dictionary<string, ArchiveLocation> _archiveIndex;
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);

    // ═══ Regex patterns for extracting weapon chain values ═══
    // SAGE WeaponSet: "Weapon = PRIMARY WeaponName" or "Weapon PRIMARY WeaponName" or "Weapon = PRIMARY   WeaponName"
    private static readonly Regex WeaponSlotPattern = new(
        @"^\s*Weapon\s*=\s*(PRIMARY|SECONDARY|TERTIARY)\s+(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // Alternative pattern for "Weapon = PRIMARY    WeaponName" with variable spacing
    private static readonly Regex WeaponSlotAltPattern = new(
        @"^\s*Weapon\s+((?:PRIMARY|SECONDARY|TERTIARY))\s+(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // SAGE Weapon: "ProjectileObject = Name" or "ProjectileTemplateName = Name"
    private static readonly Regex ProjectilePattern = new(
        @"^\s*(?:ProjectileObject|ProjectileTemplate|ProjectileTemplateName)\s*=\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // DamageType is a simple value like ARMOR_PIERCING
    private static readonly Regex DamageTypePattern = new(
        @"^\s*DamageType\s*=\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // FX: FireFX, MuzzleFlash, FXList from Weapon block
    private static readonly Regex FireFxPattern = new(
        @"^\s*(?:FireFX|MuzzleFlash|FXList)\s*=\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // Detonation FX from Projectile block
    private static readonly Regex DetonationFxPattern = new(
        @"^\s*(?:DetonationFX|ProjectileDetonationFX|ExplosionFX)\s*=\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // Audio: FireSound, WeaponSound from Weapon block
    private static readonly Regex FireSoundPattern = new(
        @"^\s*(?:FireSound|WeaponSound)\s*=\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // FXList Sound section: Name = AudioEventName
    private static readonly Regex FxSoundPattern = new(
        @"^\s*Name\s*=\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // CommandButton: Object = UnitName
    private static readonly Regex CommandButtonObjectPattern = new(
        @"^\s*Object\s*=\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // ButtonImage from CommandButton
    private static readonly Regex ButtonImagePattern = new(
        @"^\s*ButtonImage\s*=\s*(\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public WeaponPackageResolver(
        SageDefinitionIndex sageIndex,
        IBigFileReader bigFileReader,
        Dictionary<string, ArchiveLocation> archiveIndex)
    {
        _sageIndex = sageIndex;
        _bigFileReader = bigFileReader;
        _archiveIndex = archiveIndex;
    }

    /// <summary>
    /// تحليل أسلحة وحدة قتالية وبناء حزم نقل مكتملة.
    /// </summary>
    public WeaponTransferManifest ResolveWeapons(string unitName, string sourceModPath)
    {
        var manifest = new WeaponTransferManifest { UnitName = unitName };
        _visited.Clear();

        // ═══ 1. البحث عن Object block في SageIndex ═══
        var unitDef = _sageIndex.GetMergedDefinition(unitName);
        if (unitDef == null)
        {
            BlackBoxRecorder.Record("WEAPON_RESOLVER", "UNIT_NOT_FOUND", $"Unit={unitName}");
            return manifest;
        }

        var unitContent = unitDef.RawContent;

        // استخراج KindOf
        var kindOfMatch = Regex.Match(unitContent, @"^\s*KindOf\s*=\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (kindOfMatch.Success)
            manifest.UnitKindOf = kindOfMatch.Groups[1].Value.Trim();

        // ═══ 2. استخراج تبعيات الوحدة الأساسية (نموذج، أصوات، درع، محرك) ═══
        ExtractUnitBaseDependencies(unitDef, manifest, sourceModPath);

        // ═══ 3. استخراج أسماء الأسلحة من WeaponSet ═══
        var weaponSlots = ExtractWeaponSlots(unitContent);

        // ═══ 4. بناء حزمة لكل سلاح ═══
        foreach (var (slot, weaponName) in weaponSlots)
        {
            if (weaponName.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                continue;

            var package = BuildWeaponPackage(unitName, slot, weaponName, sourceModPath);
            if (package.IsComplete && package.DependencyCount <= DependencyLimits.MAX_DEPENDENCIES)
                manifest.AcceptedWeapons.Add(package);
            else
                manifest.RejectedWeapons.Add(package);
        }

        // ═══ 5. تحقق من الحد الإجمالي ═══
        if (manifest.TotalDependencies > DependencyLimits.MAX_DEPENDENCIES)
        {
            BlackBoxRecorder.Record("WEAPON_RESOLVER", "MAX_DEPS_EXCEEDED", $"Unit={unitName}, Total={manifest.TotalDependencies}");
            manifest.RejectedWeapons.AddRange(manifest.AcceptedWeapons);
            manifest.AcceptedWeapons.Clear();
        }

        BlackBoxRecorder.Record("WEAPON_RESOLVER", "RESOLVE_COMPLETE",
            $"Unit={unitName}, Accepted={manifest.AcceptedWeapons.Count}, Rejected={manifest.RejectedWeapons.Count}, UnitDeps={manifest.UnitDependencies.Count}");

        return manifest;
    }

    /// <summary>
    /// استخراج تبعيات الوحدة الأساسية: Model, Textures, Locomotor, Armor, Voice, CommandSet
    /// يغطي كل فروع شجرة SAGE بشكل شامل
    /// </summary>
    private void ExtractUnitBaseDependencies(SageDefinition unitDef, WeaponTransferManifest manifest, string sourceModPath)
    {
        var content = unitDef.RawContent;
        var depsAdded = 0;
        var unitName = unitDef.Name;

        // ═══ A. نماذج الوحدة 3D (W3D) - من Draw modules ═══
        var modelPatterns = new[]
        {
            @"^\s*Model\s*=\s*(\S+)",
            @"^\s*ModelName\s*=\s*(\S+)",
            @"^\s*DefaultModel\s*=\s*(\S+)",
            @"^\s*W3DModel\s*=\s*(\S+)",
            @"^\s*ConditionState.*\n.*Model\s*=\s*(\S+)",
            @"^\s*Turret\s*\n.*Model\s*=\s*(\S+)",
            @"^\s*TurretArt\s*\n.*Model\s*=\s*(\S+)"
        };
        foreach (var pattern in modelPatterns)
        {
            foreach (Match m in Regex.Matches(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var modelName = m.Groups[m.Groups.Count - 1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(modelName))
                {
                    // Add .w3d extension if missing
                    if (!modelName.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase))
                        modelName += ".w3d";
                    AddUnitDependency(manifest, modelName, DependencyType.Model3D, sourceModPath);
                    depsAdded++;
                }
            }
        }

        // ═══ B. أصوات الوحدة (Voice, SFX, Engine) ═══
        var soundPatterns = new Dictionary<string, string>
        {
            [@"^\s*VoiceSelect\s*=\s*(\S+)"] = "VoiceSelect",
            [@"^\s*VoiceMove\s*=\s*(\S+)"] = "VoiceMove",
            [@"^\s*VoiceAttack\s*=\s*(\S+)"] = "VoiceAttack",
            [@"^\s*VoiceCreate\s*=\s*(\S+)"] = "VoiceCreate",
            [@"^\s*VoiceFear\s*=\s*(\S+)"] = "VoiceFear",
            [@"^\s*VoiceFearCliff\s*=\s*(\S+)"] = "VoiceFearCliff",
            [@"^\s*VoiceFearUnderFire\s*=\s*(\S+)"] = "VoiceFearUnderFire",
            [@"^\s*VoiceGarrison\s*=\s*(\S+)"] = "VoiceGarrison",
            [@"^\s*VoiceSurrender\s*=\s*(\S+)"] = "VoiceSurrender",
            [@"^\s*VoiceEnter\s*=\s*(\S+)"] = "VoiceEnter",
            [@"^\s*VoiceEnterHostile\s*=\s*(\S+)"] = "VoiceEnterHostile",
            [@"^\s*VoiceEject\s*=\s*(\S+)"] = "VoiceEject",
            [@"^\s*VoiceEvacuate\s*=\s*(\S+)"] = "VoiceEvacuate",
            [@"^\s*VoiceGuard\s*=\s*(\S+)"] = "VoiceGuard",
            [@"^\s*VoiceHoldPosition\s*=\s*(\S+)"] = "VoiceHoldPosition",
            [@"^\s*VoiceDie\s*=\s*(\S+)"] = "VoiceDie",
            [@"^\s*VoiceCrushed\s*=\s*(\S+)"] = "VoiceCrushed",
            [@"^\s*VoiceAmbushed\s*=\s*(\S+)"] = "VoiceAmbushed",
            [@"^\s*VoiceBuildResponse\s*=\s*(\S+)"] = "VoiceBuildResponse",
            [@"^\s*VoiceTaskComplete\s*=\s*(\S+)"] = "VoiceTaskComplete",
            [@"^\s*VoiceDeploy\s*=\s*(\S+)"] = "VoiceDeploy",
            [@"^\s*VoiceUndeploy\s*=\s*(\S+)"] = "VoiceUndeploy",
            [@"^\s*SoundMoveStart\s*=\s*(\S+)"] = "SoundMoveStart",
            [@"^\s*SoundMoveLoop\s*=\s*(\S+)"] = "SoundMoveLoop",
            [@"^\s*SoundAmbient\s*=\s*(\S+)"] = "SoundAmbient",
            [@"^\s*SoundAmbientDamaged\s*=\s*(\S+)"] = "SoundAmbientDamaged",
            [@"^\s*SoundAmbientReallyDamaged\s*=\s*(\S+)"] = "SoundAmbientReallyDamaged",
            [@"^\s*SoundAmbientRubble\s*=\s*(\S+)"] = "SoundAmbientRubble",
            [@"^\s*SoundImpact\s*=\s*(\S+)"] = "SoundImpact",
            [@"^\s*SoundMelee\s*=\s*(\S+)"] = "SoundMelee",
            [@"^\s*SoundStealthOn\s*=\s*(\S+)"] = "SoundStealthOn",
            [@"^\s*SoundStealthOff\s*=\s*(\S+)"] = "SoundStealthOff",
            [@"^\s*SoundNoBuild\s*=\s*(\S+)"] = "SoundNoBuild",
            [@"^\s*SoundCreated\s*=\s*(\S+)"] = "SoundCreated",
            [@"^\s*SoundOnDamaged\s*=\s*(\S+)"] = "SoundOnDamaged",
            [@"^\s*SoundOnReallyDamaged\s*=\s*(\S+)"] = "SoundOnReallyDamaged",
            [@"^\s*SoundUnitEnter\s*=\s*(\S+)"] = "SoundUnitEnter",
            [@"^\s*SoundUnitExit\s*=\s*(\S+)"] = "SoundUnitExit",
            [@"^\s*SoundPromotedVeteran\s*=\s*(\S+)"] = "SoundPromotedVeteran",
            [@"^\s*SoundPromotedElite\s*=\s*(\S+)"] = "SoundPromotedElite",
            [@"^\s*SoundPromotedHero\s*=\s*(\S+)"] = "SoundPromotedHero",
            [@"^\s*SoundWakeUp\s*=\s*(\S+)"] = "SoundWakeUp",
            [@"^\s*SoundEnter\s*=\s*(\S+)"] = "SoundEnter",
            [@"^\s*SoundExit\s*=\s*(\S+)"] = "SoundExit"
        };
        foreach (var kvp in soundPatterns)
        {
            foreach (Match m in Regex.Matches(content, kvp.Key, RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var soundName = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(soundName))
                {
                    // Add .wav extension
                    var soundFile = soundName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) 
                        ? soundName 
                        : soundName + ".wav";
                    AddUnitDependency(manifest, soundFile, DependencyType.Audio, sourceModPath);
                    depsAdded++;
                }
            }
        }

        // ═══ C. Locomotor (محرك الحركة) ═══
        var locoPatterns = new[]
        {
            @"^\s*Locomotor\s*=\s*\S+\s+(\S+)",
            @"^\s*Locomotor\s*=\s*(\S+)",
            @"^\s*LocomotorSet\s*\n.*Locomotor\s*=\s*(\S+)"
        };
        foreach (var pattern in locoPatterns)
        {
            var locoMatch = Regex.Match(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (locoMatch.Success)
            {
                var locoName = locoMatch.Groups[1].Value.Trim();
                manifest.LocomotorName = locoName;
                AddUnitDependency(manifest, locoName, DependencyType.Custom, sourceModPath, "locomotor.ini");
                depsAdded++;
                break;
            }
        }

        // ═══ D. Armor (الدرع) ═══
        var armorPatterns = new[]
        {
            @"^\s*Armor\s*=\s*(\S+)",
            @"^\s*ArmorSet\s*\n.*Armor\s*=\s*(\S+)",
            @"^\s*ArmorSet\s*\n.*Conditions\s*=\s*\S+.*\n.*Armor\s*=\s*(\S+)"
        };
        foreach (var pattern in armorPatterns)
        {
            var armorMatch = Regex.Match(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (armorMatch.Success)
            {
                var armorName = armorMatch.Groups[1].Value.Trim();
                manifest.ArmorName = armorName;
                AddUnitDependency(manifest, armorName, DependencyType.Armor, sourceModPath, "armor.ini");
                depsAdded++;
                break;
            }
        }

        // ═══ E. CommandSet (للزر في قائمة البناء) ═══
        var cmdSetPatterns = new[]
        {
            @"^\s*CommandSet\s*=\s*(\S+)",
            @"^\s*BuildVariations\s*=\s*(\S+)"
        };
        foreach (var pattern in cmdSetPatterns)
        {
            var cmdSetMatch = Regex.Match(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (cmdSetMatch.Success)
            {
                var cmdSetName = cmdSetMatch.Groups[1].Value.Trim();
                manifest.CommandSetName = cmdSetName;
                AddUnitDependency(manifest, cmdSetName, DependencyType.Custom, sourceModPath, "commandset.ini");
                depsAdded++;
                break;
            }
        }

        // ═══ F. ButtonImage و SelectPortrait (الأيقونات) ═══
        var imagePatterns = new Dictionary<string, Action<string>>
        {
            [@"^\s*ButtonImage\s*=\s*(\S+)"] = (name) => { manifest.ButtonImageName = name; },
            [@"^\s*SelectPortrait\s*=\s*(\S+)"] = (name) => { },
            [@"^\s*PortraitImage\s*=\s*(\S+)"] = (name) => { }
        };
        foreach (var kvp in imagePatterns)
        {
            foreach (Match m in Regex.Matches(content, kvp.Key, RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var imageName = m.Groups[1].Value.Trim();
                kvp.Value(imageName);
                // MappedImage reference
                AddUnitDependency(manifest, imageName, DependencyType.Texture, sourceModPath, "mappedimages.ini");
                depsAdded++;
            }
        }

        // ═══ G. FXList, OCL, ParticleSystems ═══
        var fxPatterns = new[]
        {
            @"^\s*FXList\s*=\s*(\S+)",
            @"^\s*FireOCL\s*=\s*(\S+)",
            @"^\s*ProjectileOCL\s*=\s*(\S+)",
            @"^\s*DamageOCL\s*=\s*(\S+)",
            @"^\s*DeathOCL\s*=\s*(\S+)",
            @"^\s*SpawnOCL\s*=\s*(\S+)",
            @"^\s*CreateObject\s*=\s*(\S+)",
            @"^\s*ParticleSysBone\s*=\s*\S+\s+(\S+)",
            @"^\s*ReallyDamagedParticleSystem1\s*=\s*(\S+)",
            @"^\s*ReallyDamagedParticleSystem2\s*=\s*(\S+)",
            @"^\s*ReallyDamagedParticleSystem3\s*=\s*(\S+)",
            @"^\s*DamagedParticleSystem\s*=\s*(\S+)",
            @"^\s*DestructionParticleSystem\s*=\s*(\S+)",
            @"^\s*RailFiringSystem\s*=\s*(\S+)",
            @"^\s*LaserFiringSystem\s*=\s*(\S+)",
            @"^\s*TreadDebrisLeft\s*=\s*(\S+)",
            @"^\s*TreadDebrisRight\s*=\s*(\S+)"
        };
        foreach (var pattern in fxPatterns)
        {
            foreach (Match m in Regex.Matches(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var fxName = m.Groups[m.Groups.Count - 1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(fxName))
                {
                    AddUnitDependency(manifest, fxName, DependencyType.FXList, sourceModPath, "fxlist.ini");
                    depsAdded++;
                }
            }
        }

        // ═══ H. Upgrades ═══
        var upgradePatterns = new[]
        {
            @"^\s*Upgrade\s*=\s*(\S+)",
            @"^\s*UpgradeToGrant\s*=\s*(\S+)",
            @"^\s*UpgradeToRemove\s*=\s*(\S+)",
            @"^\s*PrerequisiteUpgrade\s*=\s*(\S+)",
            @"^\s*TriggeredBy\s*=\s*(\S+)"
        };
        foreach (var pattern in upgradePatterns)
        {
            foreach (Match m in Regex.Matches(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var upgradeName = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(upgradeName))
                {
                    AddUnitDependency(manifest, upgradeName, DependencyType.Custom, sourceModPath, "upgrade.ini");
                    depsAdded++;
                }
            }
        }

        // ═══ I. Science ═══
        var sciencePatterns = new[]
        {
            @"^\s*Science\s*=\s*(\S+)",
            @"^\s*PrerequisiteSciences\s*=\s*(\S+)",
            @"^\s*GrantScience\s*=\s*(\S+)"
        };
        foreach (var pattern in sciencePatterns)
        {
            foreach (Match m in Regex.Matches(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                var scienceName = m.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(scienceName))
                {
                    AddUnitDependency(manifest, scienceName, DependencyType.Custom, sourceModPath, "science.ini");
                    depsAdded++;
                }
            }
        }

        // ═══ J. إضافة كل التبعيات من تعريف الوحدة (باستخدام SageDefinitionIndex) ═══
        var refs = SageDefinitionIndex.ExtractReferences(unitDef);
        var fileRefCount = 0;
        foreach (var sageRef in refs)
        {
            if (HasFileExtension(sageRef.Name))
            {
                var depType = MapRefType(sageRef.Type);
                AddUnitDependency(manifest, sageRef.Name, depType, sourceModPath);
                depsAdded++;
                fileRefCount++;
            }
        }
    }

    private void AddUnitDependency(WeaponTransferManifest manifest, string name, DependencyType type, string sourceModPath, string? iniFile = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var fullPath = FindFile(name, type, sourceModPath);
        if (fullPath == null && iniFile != null)
        {
            fullPath = $"{{INI}}/{iniFile}::{name}";
        }

        manifest.UnitDependencies.Add(new DependencyNode
        {
            Name = name,
            Type = type,
            FullPath = fullPath,
            Status = fullPath != null ? AssetStatus.Found : AssetStatus.Missing,
            Depth = 0
        });
    }

    /// <summary>
    /// استخراج أسماء الأسلحة من WeaponSet block (PRIMARY/SECONDARY/TERTIARY فقط)
    /// SAGE format: "Weapon = PRIMARY WeaponName" or "Weapon PRIMARY WeaponName"
    /// </summary>
    private static List<(string Slot, string WeaponName)> ExtractWeaponSlots(string objectContent)
    {
        var slots = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Try primary regex first
        var matches1 = WeaponSlotPattern.Matches(objectContent);
        foreach (Match m in matches1)
        {
            var slot = m.Groups[1].Value.Trim().ToUpperInvariant();
            var name = CleanWeaponName(m.Groups[2].Value.Trim());
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(slot))
                slots.Add((slot, name));
        }

        // Try alternative pattern
        var matches2 = WeaponSlotAltPattern.Matches(objectContent);
        foreach (Match m in matches2)
        {
            var slot = m.Groups[1].Value.Trim().ToUpperInvariant();
            var name = CleanWeaponName(m.Groups[2].Value.Trim());
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(slot))
                slots.Add((slot, name));
        }

        // Fallback: line-by-line parsing for edge cases
        if (slots.Count == 0)
        {
            var weaponLines = objectContent.Split('\n').Where(l => l.Trim().StartsWith("Weapon", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var rawLine in weaponLines)
            {
                var line = rawLine.Trim();

                var parts = line.Split(new[] { ' ', '\t', '=' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var slotIdx = -1;
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var p = parts[i].ToUpperInvariant();
                        if (p == "PRIMARY" || p == "SECONDARY" || p == "TERTIARY")
                        {
                            slotIdx = i;
                            break;
                        }
                    }

                    if (slotIdx >= 0 && slotIdx + 1 < parts.Length)
                    {
                        var slot = parts[slotIdx].ToUpperInvariant();
                        var name = CleanWeaponName(parts[slotIdx + 1]);
                        if (!string.IsNullOrWhiteSpace(name) && seen.Add(slot))
                            slots.Add((slot, name));
                    }
                }
            }
        }

        return slots;
    }

    private static string CleanWeaponName(string name)
    {
        var ci = name.IndexOf(';');
        if (ci >= 0) name = name.Substring(0, ci).Trim();
        ci = name.IndexOf("//", StringComparison.Ordinal);
        if (ci >= 0) name = name.Substring(0, ci).Trim();
        ci = name.IndexOf(' ');
        if (ci >= 0) name = name.Substring(0, ci).Trim();
        return name;
    }

    /// <summary>
    /// بناء حزمة سلاح كاملة مع تتبع شامل لكل التبعيات:
    /// Object → Weapon → Projectile → Armor → Locomotor → Model → Textures → Audio → FX
    /// </summary>
    private WeaponPackage BuildWeaponPackage(string unitName, string slot, string weaponName, string sourceModPath)
    {
        var package = new WeaponPackage
        {
            OwnerUnit = unitName,
            Slot = slot
        };

        // ═══ A. Weapon (إلزامي) ═══
        var weaponDef = _sageIndex.GetMergedDefinition(weaponName);
        if (weaponDef == null)
        {
            package.Weapon = new WeaponElement { Name = weaponName, Found = false };
            BlackBoxRecorder.Record("WEAPON_RESOLVER", "WEAPON_NOT_FOUND", $"Weapon={weaponName}, Unit={unitName}");
            return package;
        }

        package.Weapon = new WeaponElement
        {
            Name = weaponName,
            SourceFile = weaponDef.SourceFile,
            Found = true
        };

        // استخراج كل تبعيات السلاح (Model, Texture, Audio, FX)
        AddDefinitionDeps(weaponDef, package, sourceModPath, 1);

        // ═══ B. Projectile ═══
        var projectileName = ExtractFirstMatch(weaponDef.RawContent, ProjectilePattern);

        if (!string.IsNullOrWhiteSpace(projectileName))
        {
            var projDef = _sageIndex.GetMergedDefinition(projectileName);
            package.Projectile = new WeaponElement
            {
                Name = projectileName,
                SourceFile = projDef?.SourceFile ?? string.Empty,
                Found = projDef != null
            };

            if (projDef != null)
            {
                AddDefinitionDeps(projDef, package, sourceModPath, 2);

                // استخراج DetonationFX من Projectile
                var detFx = ExtractFirstMatch(projDef.RawContent, DetonationFxPattern);
                if (!string.IsNullOrWhiteSpace(detFx))
                    ResolveFxChain(detFx, package, sourceModPath, 3);
            }
        }
        else
        {
            package.Projectile = new WeaponElement { Name = "(none)", Found = false };
        }

        // ═══ C. DamageType ═══
        var damageType = ExtractFirstMatch(weaponDef.RawContent, DamageTypePattern);
        package.DamageType = new WeaponElement
        {
            Name = damageType ?? "(none)",
            SourceFile = "inline",
            Found = !string.IsNullOrWhiteSpace(damageType)
        };

        // ═══ D. FireFX من Weapon ═══
        var fireFx = ExtractFirstMatch(weaponDef.RawContent, FireFxPattern);
        if (!string.IsNullOrWhiteSpace(fireFx))
        {
            package.FX = new WeaponElement
            {
                Name = fireFx,
                SourceFile = _sageIndex.Lookup(fireFx)?.SourceFile ?? string.Empty,
                Found = _sageIndex.Contains(fireFx)
            };
            ResolveFxChain(fireFx, package, sourceModPath, 2);
        }
        else
        {
            package.FX = new WeaponElement { Name = "(none)", Found = false };
        }

        // ═══ E. FireSound من Weapon ═══
        var fireSound = ExtractFirstMatch(weaponDef.RawContent, FireSoundPattern);
        if (!string.IsNullOrWhiteSpace(fireSound))
        {
            package.Audio = new WeaponElement
            {
                Name = fireSound,
                SourceFile = _sageIndex.Lookup(fireSound)?.SourceFile ?? string.Empty,
                Found = _sageIndex.Contains(fireSound)
            };
            // إضافة ملف الصوت إذا وجد
            AddAudioFileDep(fireSound, package, sourceModPath);
        }
        else
        {
            // محاولة استخراج صوت من FXList
            var fxName = fireFx ?? package.FX?.Name;
            if (!string.IsNullOrWhiteSpace(fxName))
            {
                var fxAudio = TryExtractAudioFromFx(fxName);
                if (!string.IsNullOrWhiteSpace(fxAudio))
                {
                    package.Audio = new WeaponElement
                    {
                        Name = fxAudio,
                        SourceFile = _sageIndex.Lookup(fxAudio)?.SourceFile ?? string.Empty,
                        Found = _sageIndex.Contains(fxAudio)
                    };
                    AddAudioFileDep(fxAudio, package, sourceModPath);
                }
                else
                {
                    package.Audio = new WeaponElement { Name = "(none)", Found = false };
                }
            }
            else
            {
                package.Audio = new WeaponElement { Name = "(none)", Found = false };
            }
        }

        // ═══ F. Icon (اختياري) ═══
        package.Icon = ResolveWeaponIcon(weaponName, unitName);

        return package;
    }

    private void AddAudioFileDep(string audioName, WeaponPackage package, string sourceModPath)
    {
        var candidates = new[] { audioName + ".wav", audioName + ".mp3" };
        foreach (var fileName in candidates)
        {
            if (_archiveIndex.TryGetValue(fileName, out var loc))
            {
                package.AllDependencies.Add(new DependencyNode
                {
                    Name = fileName,
                    Type = DependencyType.Audio,
                    FullPath = $"{loc.ArchivePath}::{loc.EntryPath}",
                    Status = AssetStatus.Found,
                    Depth = 3
                });
            }
            else
            {
                var testPath = Path.Combine(sourceModPath, "Data", "Audio", "Sounds", fileName);
                if (File.Exists(testPath))
                {
                    package.AllDependencies.Add(new DependencyNode
                    {
                        Name = fileName,
                        Type = DependencyType.Audio,
                        FullPath = testPath,
                        Status = AssetStatus.Found,
                        Depth = 3
                    });
                }
            }
        }
    }

    private string? TryExtractAudioFromFx(string? fxName)
    {
        if (string.IsNullOrWhiteSpace(fxName)) return null;
        var fxDef = _sageIndex.GetMergedDefinition(fxName);
        if (fxDef == null) return null;
        return ExtractFxAudio(fxDef.RawContent);
    }

    /// <summary>
    /// إضافة تبعيات ملفات (W3D, DDS, WAV) من تعريف SAGE
    /// </summary>
    private void AddDefinitionDeps(SageDefinition def, WeaponPackage package, string sourceModPath, int depth)
    {
        if (depth > DependencyLimits.MAX_DEPTH) return;
        if (package.AllDependencies.Count >= DependencyLimits.MAX_DEPENDENCIES) return;

        var refs = SageDefinitionIndex.ExtractReferences(def);
        foreach (var sageRef in refs)
        {
            if (package.AllDependencies.Count >= DependencyLimits.MAX_DEPENDENCIES) break;
            if (_visited.Contains(sageRef.Name)) continue;
            _visited.Add(sageRef.Name);

            if (HasFileExtension(sageRef.Name))
            {
                var depType = MapRefType(sageRef.Type);
                var fullPath = FindFile(sageRef.Name, depType, sourceModPath);
                package.AllDependencies.Add(new DependencyNode
                {
                    Name = sageRef.Name,
                    Type = depType,
                    FullPath = fullPath,
                    Status = fullPath != null ? AssetStatus.Found : AssetStatus.Missing,
                    Depth = depth
                });
            }
            else if (depth < DependencyLimits.MAX_DEPTH)
            {
                var childDef = _sageIndex.GetMergedDefinition(sageRef.Name);
                if (childDef != null)
                {
                    var depType = MapBlockType(childDef.BlockType);
                    package.AllDependencies.Add(new DependencyNode
                    {
                        Name = sageRef.Name,
                        Type = depType,
                        FullPath = childDef.SourceFile,
                        Status = AssetStatus.Found,
                        Depth = depth
                    });
                    AddDefinitionDeps(childDef, package, sourceModPath, depth + 1);
                }
            }
        }
    }

    /// <summary>
    /// تتبع سلسلة FXList → ParticleSystem → Audio
    /// </summary>
    private void ResolveFxChain(string fxName, WeaponPackage package, string sourceModPath, int depth)
    {
        if (depth > DependencyLimits.MAX_DEPTH) return;
        if (_visited.Contains(fxName)) return;
        _visited.Add(fxName);

        var fxDef = _sageIndex.GetMergedDefinition(fxName);
        if (fxDef == null) return;

        AddDefinitionDeps(fxDef, package, sourceModPath, depth);
    }

    /// <summary>
    /// استخراج صوت من FXList block (Sound section → Name = ...)
    /// </summary>
    private static string? ExtractFxAudio(string fxContent)
    {
        var inSound = false;
        foreach (var rawLine in fxContent.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Equals("Sound", StringComparison.OrdinalIgnoreCase))
            {
                inSound = true;
                continue;
            }
            if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                inSound = false;
                continue;
            }
            if (inSound)
            {
                var m = FxSoundPattern.Match(line);
                if (m.Success) return m.Groups[1].Value.Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// أيقونة السلاح: فقط إذا CommandButton + ButtonImage + MappedImage حقيقي
    /// </summary>
    private WeaponIconInfo? ResolveWeaponIcon(string weaponName, string unitName)
    {
        // Search all CommandButton definitions for one that references this weapon or unit
        var allDefs = _sageIndex.GetAllDefinitions();
        foreach (var def in allDefs)
        {
            if (!def.BlockType.Equals("CommandButton", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = def.RawContent;

            // Check if this CommandButton references our unit
            var objMatch = CommandButtonObjectPattern.Match(content);
            if (!objMatch.Success) continue;

            var refName = objMatch.Groups[1].Value.Trim();
            if (!refName.Equals(weaponName, StringComparison.OrdinalIgnoreCase) &&
                !refName.Equals(unitName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Extract ButtonImage
            var imgMatch = ButtonImagePattern.Match(content);
            if (!imgMatch.Success) continue;

            var buttonImage = imgMatch.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(buttonImage)) continue;

            // Verify MappedImage exists
            var mappedDef = _sageIndex.Lookup(buttonImage);
            if (mappedDef == null || !mappedDef.BlockType.Equals("MappedImage", StringComparison.OrdinalIgnoreCase))
                continue;

            return new WeaponIconInfo
            {
                CommandButtonName = def.Name,
                ButtonImageName = buttonImage,
                MappedImageName = buttonImage,
                IsValid = true
            };
        }

        return null;
    }

    // ═══ Helpers ═══

    private static string? ExtractFirstMatch(string content, Regex pattern)
    {
        var m = pattern.Match(content);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private string? FindFile(string fileName, DependencyType depType, string sourceModPath)
    {
        if (_archiveIndex.TryGetValue(fileName, out var loc))
            return $"{loc.ArchivePath}::{loc.EntryPath}";

        var searchDirs = depType switch
        {
            DependencyType.Model3D => new[] { "Art\\W3D", "Art\\w3d" },
            DependencyType.Texture => new[] { "Art\\Textures", "Art\\textures" },
            DependencyType.Audio => new[] { "Data\\Audio", "data\\audio" },
            _ => Array.Empty<string>()
        };

        foreach (var dir in searchDirs)
        {
            var testPath = Path.Combine(sourceModPath, dir, fileName);
            if (File.Exists(testPath)) return testPath;
        }

        return null;
    }

    private static bool HasFileExtension(string name) =>
        name.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);

    private static DependencyType MapRefType(SageRefType type) => type switch
    {
        SageRefType.Model => DependencyType.Model3D,
        SageRefType.Texture => DependencyType.Texture,
        SageRefType.Audio => DependencyType.Audio,
        SageRefType.FXList => DependencyType.FXList,
        SageRefType.Weapon => DependencyType.Weapon,
        _ => DependencyType.Custom
    };

    private static DependencyType MapBlockType(string blockType) => blockType.ToUpperInvariant() switch
    {
        "WEAPON" => DependencyType.Weapon,
        "PROJECTILE" => DependencyType.Projectile,
        "FXLIST" => DependencyType.FXList,
        "PARTICLESYSTEM" => DependencyType.VisualEffect,
        "AUDIOEVENT" => DependencyType.Audio,
        "ARMOR" => DependencyType.Armor,
        _ => DependencyType.Custom
    };

    public sealed record ArchiveLocation(string ArchivePath, string EntryPath, long Size, bool IsHighPriority);
}
