using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Logging;
using ZeroHourStudio.Infrastructure.Services;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZeroHourStudio.Infrastructure.DependencyResolution;

/// <summary>
/// محرك حل التبعيات الذكي
/// يقوم بتحليل ملفات INI و BIG ليكتشف جميع التبعيات المطلوبة لوحدة
/// </summary>
public class SmartDependencyResolver : IDependencyResolver
{
    private readonly IBigFileReader _bigFileReader;
    private HashSet<string> _visitedNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _unitIniCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _objectIniCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _iniContentCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visitedBlocks = new(StringComparer.OrdinalIgnoreCase);
    private string? _archiveIndexRoot;
    private readonly ConcurrentDictionary<string, ArchiveLocation> _archiveIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Archives.BigArchiveManager> _archiveManagerCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// الحد الأقصى لعمق التحليل التكراري (4 بدلاً من 8 لمنع الانفجار)
    /// 0: Root Object INI
    /// 1: Weapons, Armor
    /// 2: Projectiles, FXLists
    /// 3: Audio, ParticleSystems (الحد الآمن)
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 4;

    /// <summary>
    /// الحد الأقصى الآمن لعدد التبعيات في الرسم البياني الواحد
    /// 80: حماية من الانفجار بسبب DeepSageChainTraversal أو Regex واسع
    /// يحافظ فقط على 80 عقدة الأكثر حرجة (تم فرزها حسب النوع والعمق)
    /// </summary>
    private const int MaxTotalDependencies = 80;

    /// <summary>
    /// تحذير: الحد الأقصى لعدد التبعيات قبل إصدار تنبيه
    /// </summary>
    private const int DependencyWarningThreshold = 60;

    /// <summary>
    /// SAGE definition index for deep recursive dependency chain traversal.
    /// Set this before calling ResolveDependenciesAsync for deep analysis.
    /// </summary>
    public SageDefinitionIndex? SageIndex { get; set; }

    // ═══ Static Compiled Regex Patterns (تجنب إعادة البناء في كل استدعاء) ═══
    private static readonly Regex s_filePattern = new(
        @"^\s*([A-Za-z0-9_]*ModelName[A-Za-z0-9_]*|Model|ModelNames|Animation|IdleAnimation|[A-Za-z0-9_]*Texture[A-Za-z0-9_]*|Texture|TextureName|ParticleName|MoveHintName|W3D|DDS|Audio|Sound|Sounds|Music|Filename|DeathFX|FXList|Weapon|Armor|TrackMarks|Dust|DirtSpray|PowerslideSpray)\s*=?\s+([^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_extensionPattern = new(
        @"([A-Za-z0-9_\-./\\]+\.(w3d|dds|tga|wav|mp3|wma|ini|wnd|map|str|bik|htm|html))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_inheritPattern = new(
        @"^\s*InheritFrom\s*=\s*([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_objectPattern = new(
        @"^\s*(Armor|DeathWeapon|WeaponName|CrushingWeaponName|ContinuousWeaponDamaged|ContinuousWeaponPristine|ContinuousWeaponReallyDamaged|DetonationWeapon|GeometryBasedDamageWeapon|HowitzerWeaponTemplate|OccupantDamageWeaponTemplate|ShockwaveWeaponTemplate|WeaponTemplate|ReactionWeaponDamaged|ReactionWeaponPristine|ReactionWeaponReallyDamaged|Projectile|FXList|Locomotor|DeathFX|ExplosionList|CommandSet|CommandButton|Sound|Voice|EngineSound|AmbientSound|ProjectileObject|ProjectileTemplate|ProjectileDetonationFX|ProjectileDetonationOCL|FireFX|FireOCL|MuzzleFlash|TracerFX|BeamFX|LaserName|FireSound|WeaponSound|StartSound|StopSound|LoopSound|ProjectileExhaust|VeterancyProjectileExhaust|ProjectileStream|ProjectileStreamName|ProjectileTrail|ProjectileTrailName|TrailFX|ContrailFX|SmokeTrailFX|ParticleSystem|ParticleSystemName|ParticleSystemID|SlaveSystem|AttachParticle|ObjectNames|ObjectName|Object|CrateObject|CrateData|ReferenceObject|FinalRubbleObject|SpecialObject|UnitName|BuildVariations|CarriageTemplateName|PayloadTemplateName|DamageFX|SpecialPower|SpecialPowerTemplate|Science|RequiredScience|ScienceRequired|KillerScience|PickupScience|GrantScience|Upgrade|RequiredUpgrade|PrerequisiteUpgrade|TriggeredBy|UpgradeObject|UpgradeOCL|UpgradeToGrant|UpgradeToRemove|UpgradeRequired|NeedsUpgrade|RemovesUpgrades|UpgradeCameo|UpgradeCameo1|UpgradeCameo2|UpgradeCameo3|UpgradeCameo4|UpgradeCameo5|UpgradeCameo6|UpgradeCameo7|UpgradeCameo8|ButtonImage|SelectPortrait|PortraitImage|Image|IntroMovie|PortraitMovieLeftName|PortraitMovieRightName|CreationList|Dust|DirtSpray|PowerslideSpray|TrackMarks|WeaponMuzzleFlash|CursorName|InvalidCursorName|RadiusCursorType|LevelGainAnimationName|GetHealedAnimationName|CrashFXTemplateName|BridgeParticle|StumpName|BaseDefenseStructure[A-Za-z0-9_]*)\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_oclPattern = new(
        @"^\s*[A-Za-z0-9_]*OCL\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_oclKeyPattern = new(
        @"^\s*OCL[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_oclAnyKeyPattern = new(
        @"^\s*[A-Za-z0-9_]*OCL[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_oclTokenPattern = new(
        @"OCL:([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_fxPattern = new(
        @"^\s*[A-Za-z0-9_]*FX\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_fxPrefixPattern = new(
        @"^\s*FX[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_fxTokenPattern = new(
        @"FX:([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_stagedFxPattern = new(
        @"^\s*FX\s*=?\s+(?:INITIAL|FINAL|MIDPOINT|PRIMARY|SECONDARY|TERTIARY)\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_fxListKeyPattern = new(
        @"^\s*[A-Za-z0-9_]*FXList[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_soundPattern = new(
        @"^\s*[A-Za-z0-9_]*(Sound(?!s)|Voice)[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_musicPattern = new(
        @"^\s*[A-Za-z0-9_]*Music[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_imagePattern = new(
        @"^\s*[A-Za-z0-9_]*(Image|Portrait)[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_imageLoosePattern = new(
        @"^\s*([A-Za-z0-9_]*(Image|Portrait|Button|Logo|Arrow|Marker|Cameo)[A-Za-z0-9_]*)\s+([A-Za-z0-9_\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_imageTokenPattern = new(
        @"\bIMAGE:\s*([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_objectNamePattern = new(
        @"^\s*[A-Za-z0-9_]*ObjectName[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_textureNamePattern = new(
        @"^\s*Texture\s*=?\s+([A-Za-z0-9_\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_textureKeyPattern = new(
        @"^\s*[A-Za-z0-9_]*Texture[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_particleSystemPattern = new(
        @"^\s*[A-Za-z0-9_]*ParticleSystem\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_laserNameKeyPattern = new(
        @"^\s*[A-Za-z0-9_]*LaserName[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_particleSystemKeyPattern = new(
        @"^\s*[A-Za-z0-9_]*Particle[A-Za-z0-9_]*System[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_particleSystemTokenPattern = new(
        @"PSys:([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_particleSysKeyPattern = new(
        @"^\s*[A-Za-z0-9_]*ParticleSys[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_particleSysBonePattern = new(
        @"^\s*ParticleSysBone\s*=?\s+[A-Za-z0-9_]+\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_particleSysBoneKeyPattern = new(
        @"^\s*[A-Za-z0-9_]*ParticleSysBone[A-Za-z0-9_]*\s*=?\s+[A-Za-z0-9_]+\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_stagedWeaponPattern = new(
        @"^\s*Weapon\s*=?\s+(?:INITIAL|FINAL|MIDPOINT|PRIMARY|SECONDARY|TERTIARY)?\s*([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_damageFxPattern = new(
        @"^\s*(MajorFX|MinorFX|VeterancyMajorFX|VeterancyMinorFX)\s*=?\s+[A-Za-z0-9_]+\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_stagedOclPattern = new(
        @"^\s*OCL\s*=?\s+(?:INITIAL|FINAL|MIDPOINT|PRIMARY|SECONDARY|TERTIARY)\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_structurePattern = new(
        @"^\s*Structure\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_unitPattern = new(
        @"^\s*Unit\s+([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    // ExtractFileReferences patterns
    private static readonly Regex s_extractFilePattern = new(
        @"([A-Za-z0-9_]*ModelName[A-Za-z0-9_]*|Model|ModelNames|Animation|IdleAnimation|[A-Za-z0-9_]*Texture[A-Za-z0-9_]*|Texture|TextureName|ParticleName|MoveHintName|W3D|DDS|Audio|Sound|Sounds|Music|Filename)\s*=\s*([^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_extractExtensionPattern = new(
        @"([A-Za-z0-9_\-./\\]+\.(w3d|dds|tga|wav|mp3|wma|ini|wnd|map|str|bik|htm|html))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_winNamePattern = new(
        @"^\s*WinName\s*=\s*([^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex s_commandRefPattern = new(
        @"=\s*(Command_[A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_fxBlockNamePattern = new(
        @"^Name\s*=\s*([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex s_sagePlaceholderPattern = new(
        @"\{[A-Za-z_]+\}[/\\]?",
        RegexOptions.Compiled);

    private static readonly Regex s_multiSlashPattern = new(
        @"[\\\/]+",
        RegexOptions.Compiled);

    private static readonly HashSet<string> s_baseArchives = new(StringComparer.OrdinalIgnoreCase)
    {
        "INIZH.big", "INI.big", "Textures.big", "W3D.big", "Audio.big", "Music.big", "PatchZH.big",
        "Shaders.big", "Terrain.big", "Window.big", "English.big", "SpeechEnglish.big",
        "AudioChinese.big", "AudioEnglish.big", "Generals.big", "C&C Generals Zero Hour.big",
        "Speech.big"
    };

    /// <summary>
    /// قائمة سوداء للملفات والكائنات المستبعدة من التحليل
    /// هذه الملفات غير ضرورية للنقل: تترجمات، واجهات المستخدم، الخرائط، إلخ.
    /// </summary>
    private static readonly HashSet<string> s_excludedFilePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        // Files that should not be ported
        "campaign", "mission", "map", "playertemplate", "controlbarscheme",
        "window", "ingameui", "credits", "language", "help",

        // UI-specific (window definitions, not needed for game logic)
        "wnd", ".wnd",

        // Localization/translation files
        "csf", ".csf", "csfstringfile",

        // Map and mission data
        "str", ".str", "scn", ".scn"
    };

    /// <summary>
    /// كائنات مستبعدة من التحليل العميق
    /// </summary>
    private static readonly HashSet<string> s_excludedObjectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MappedImage", "Video", "MouseCursor", "RadiusCursor",
        "Animation", "ControlBar", "Window"
    };

    public SmartDependencyResolver(IBigFileReader bigFileReader)
    {
        _bigFileReader = bigFileReader ?? throw new ArgumentNullException(nameof(bigFileReader));
    }

    /// <summary>
    /// حل جميع التبعيات لوحدة معينة
    /// </summary>
    public async Task<UnitDependencyGraph> ResolveDependenciesAsync(string unitName, string sourceModPath)
    {
        return await ResolveDependenciesAsync(unitName, sourceModPath, null, null);
    }

    /// <summary>
    /// حل جميع التبعيات لوحدة معينة مع مسار INI معروف
    /// </summary>
    public async Task<UnitDependencyGraph> ResolveDependenciesAsync(
        string unitName,
        string sourceModPath,
        string? objectIniPath,
        Dictionary<string, string>? unitData = null)
    {
        // Clear per-analysis state
        _visitedNodes.Clear();
        _visitedBlocks.Clear();
        _iniContentCache.Clear();
        _unitIniCache.Clear();
        _objectIniCache.Clear();
        
        var graph = new UnitDependencyGraph
        {
            UnitId = unitName,
            UnitName = unitName,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await EnsureArchiveIndexAsync(sourceModPath);

            // 1. تحديد ملف INI للوحدة
            var iniPath = objectIniPath;
            if (string.IsNullOrWhiteSpace(iniPath))
            {
                iniPath = FindUnitIniPath(unitName, sourceModPath);
            }

            if (string.IsNullOrWhiteSpace(iniPath) || (!IsArchiveReference(iniPath) && !File.Exists(iniPath)))
            {
                graph.Status = CompletionStatus.CannotVerify;
                graph.Notes = $"لم يتم العثور على ملف INI للوحدة: {unitName}";
                return graph;
            }

            // 2. إنشاء عقدة جذر
            var rootNode = new DependencyNode
            {
                Name = Path.GetFileName(iniPath),
                Type = DependencyType.ObjectINI,
                FullPath = iniPath,
                Status = AssetStatus.Found,
                Depth = 0
            };

            graph.RootNode = rootNode;
            graph.AllNodes.Add(rootNode);

            // ═══ 3. WEAPON-CENTRIC RESOLUTION (New Design) ═══
            if (SageIndex != null)
            {
                var archiveIdx = _archiveIndex.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new WeaponPackageResolver.ArchiveLocation(
                        kvp.Value.ArchivePath, kvp.Value.EntryPath, kvp.Value.Size, kvp.Value.IsHighPriority),
                    StringComparer.OrdinalIgnoreCase);

                var wpResolver = new WeaponPackageResolver(SageIndex, _bigFileReader, archiveIdx);
                var manifest = await Task.Run(() => wpResolver.ResolveWeapons(unitName, sourceModPath));

                // Convert accepted weapon packages to dependency nodes
                foreach (var wp in manifest.AcceptedWeapons)
                {
                    var weaponNode = new DependencyNode
                    {
                        Name = $"[{wp.Slot}] {wp.Weapon.Name}",
                        Type = DependencyType.Weapon,
                        FullPath = wp.Weapon.SourceFile,
                        Status = AssetStatus.Found,
                        Depth = 1
                    };
                    rootNode.Dependencies.Add(weaponNode);
                    graph.AllNodes.Add(weaponNode);

                    foreach (var dep in wp.AllDependencies)
                    {
                        if (!graph.AllNodes.Any(n => n.Name == dep.Name))
                        {
                            graph.AllNodes.Add(dep);
                            weaponNode.Dependencies.Add(dep);
                        }
                    }
                }

                // Add rejected weapons as notes
                if (manifest.RejectedWeapons.Count > 0)
                {
                    var rejectInfo = string.Join("; ",
                        manifest.RejectedWeapons.Select(w => $"{w.Slot}:{w.Weapon.Name} → {w.RejectReason}"));
                    graph.Notes = $"Rejected weapons: {rejectInfo}";
                }

                // Store manifest for transfer validation
                _lastManifest = manifest;

                // ═══ 3b. Add unit base dependencies from manifest (models, audio, armor, etc.) ═══
                foreach (var dep in manifest.UnitDependencies)
                {
                    if (!graph.AllNodes.Any(n => n.Name == dep.Name))
                    {
                        graph.AllNodes.Add(dep);
                        rootNode.Dependencies.Add(dep);
                    }
                }

                // ═══ 3c. Run legacy INI parsing for file-level dependencies FIRST ═══
                // This must run BEFORE DeepSageChainTraversal to populate graph with initial nodes
                // Note: _visitedNodes already cleared at method start
                await ParseIniDependenciesAsync(rootNode, sourceModPath, graph);

                // ═══ 3d. Deep SAGE chain traversal for named references ═══
                // This will use the already-parsed nodes from step 3c
                // Note: _visitedBlocks already cleared at method start
                DeepSageChainTraversal(rootNode, graph, sourceModPath);

                // ✅ Phase 3: Check dependency graph limits to prevent bloat
                CheckAndEnforceDependencyLimits(graph);
            }
            else
            {
                // Fallback: legacy regex-based resolution (limited)
                // Note: caches already cleared at method start
                await ParseIniDependenciesAsync(rootNode, sourceModPath, graph);

                // ✅ Phase 3: Check dependency graph limits to prevent bloat
                CheckAndEnforceDependencyLimits(graph);
            }

            // 4. حساب الإحصائيات
            graph.FoundCount = graph.AllNodes.Count(n => n.Status == AssetStatus.Found);
            graph.MissingCount = graph.AllNodes.Count(n => n.Status == AssetStatus.Missing);
            graph.MaxDepth = graph.AllNodes.Count > 0 ? graph.AllNodes.Max(n => n.Depth) : 0;

            // 5. تحديد حالة الاكتمال
            if (graph.MissingCount == 0)
                graph.Status = CompletionStatus.Complete;
            else if (graph.FoundCount > graph.MissingCount)
                graph.Status = CompletionStatus.Partial;
            else
                graph.Status = CompletionStatus.Incomplete;

            return graph;
        }
        catch (Exception ex)
        {
            graph.Status = CompletionStatus.CannotVerify;
            graph.Notes = $"خطأ أثناء حل التبعيات: {ex.Message}";
            return graph;
        }
    }

    private WeaponTransferManifest? _lastManifest;

    /// <summary>
    /// Get the last resolved weapon manifest for pre-transfer validation
    /// </summary>
    public WeaponTransferManifest? LastManifest => _lastManifest;

    private string? FindUnitIniPath(string unitName, string sourceModPath)
    {
        if (_unitIniCache.TryGetValue(unitName, out var cachedPath))
        {
            return cachedPath;
        }

        var iniRoot = Path.Combine(sourceModPath, "Data", "INI");
        if (!Directory.Exists(iniRoot))
            return null;

        var candidate = Path.Combine(iniRoot, "Object", $"{unitName}.ini");
        if (File.Exists(candidate))
        {
            _unitIniCache[unitName] = candidate;
            return candidate;
        }

        foreach (var file in Directory.GetFiles(iniRoot, "*.ini", SearchOption.AllDirectories))
        {
            try
            {
                var lines = File.ReadLines(file);
                if (lines.Any(l => l.TrimStart().StartsWith($"Object {unitName}", StringComparison.OrdinalIgnoreCase)))
                {
                    _unitIniCache[unitName] = file;
                    return file;
                }
            }
            catch (Exception ex)
            {
                BlackBoxRecorder.Record("RESOLVER", "FILE_READ_ERROR", $"FindUnitIni: {file}, Error={ex.Message}");
            }
        }

        var archiveKey = $"{unitName}.ini";
        if (_archiveIndex.TryGetValue(archiveKey, out var archiveLocation))
        {
            var archivePath = $"{archiveLocation.ArchivePath}::{archiveLocation.EntryPath}";
            _unitIniCache[unitName] = archivePath;
            return archivePath;
        }

        return null;
    }

    private async Task EnsureArchiveIndexAsync(string sourceModPath)
    {
        if (string.Equals(_archiveIndexRoot, sourceModPath, StringComparison.OrdinalIgnoreCase) && _archiveIndex.Count > 0)
            return;

        _archiveIndexRoot = sourceModPath;
        _archiveIndex.Clear();

        var archives = Directory.Exists(sourceModPath)
            ? Directory.GetFiles(sourceModPath, "*.big", SearchOption.AllDirectories)
                .OrderBy(a => Path.GetFileName(a).StartsWith("!!", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(a => Path.GetFileName(a), StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        _archiveManagerCache.Clear();

        foreach (var archivePath in archives)
        {
            try
            {
                var manager = new Archives.BigArchiveManager(archivePath);
                await manager.LoadAsync();
                _archiveManagerCache[archivePath] = manager;

                // Archive-level priority: if the BIG filename starts with !!, all entries get high priority
                var archiveFileName = Path.GetFileName(archivePath);
                var isArchiveHighPriority = archiveFileName.StartsWith("!!", StringComparison.OrdinalIgnoreCase);

                foreach (var entry in manager.GetFileList())
                {
                    var key = Path.GetFileName(entry);
                    var isHighPriority = isArchiveHighPriority
                        || key.StartsWith("!!", StringComparison.OrdinalIgnoreCase);

                    if (_archiveIndex.TryGetValue(key, out var existing))
                    {
                        if (!existing.IsHighPriority && isHighPriority)
                        {
                            _archiveIndex[key] = new ArchiveLocation(archivePath, entry, manager.GetFileInfo(entry)?.Size ?? 0, isHighPriority);
                        }
                        continue;
                    }

                    _archiveIndex[key] = new ArchiveLocation(archivePath, entry, manager.GetFileInfo(entry)?.Size ?? 0, isHighPriority);
                }
            }
            catch (Exception ex)
            {
                BlackBoxRecorder.Record("RESOLVER", "ARCHIVE_LOAD_ERROR", $"Archive={archivePath}, Error={ex.Message}");
            }
        }
    }

    /// <summary>
    /// تحليل ملف INI واستخراج التبعيات
    /// </summary>
    private async Task ParseIniDependenciesAsync(DependencyNode parentNode, string sourceModPath, UnitDependencyGraph graph)
    {
        if (parentNode.FullPath == null || _visitedNodes.Contains(parentNode.FullPath))
            return;

        _visitedNodes.Add(parentNode.FullPath);

        try
        {
            var iniContent = await ReadIniContentCachedAsync(parentNode.FullPath);

            // ═══ InheritFrom: استخراج الوراثة من الأب ودمج تبعياته ═══
            await ResolveInheritFromAsync(iniContent, parentNode, sourceModPath, graph);

            // استخراج جميع المراجع للملفات
            var keyedMatches = s_filePattern.Matches(iniContent).Cast<Match>()
                .Select(m => (Key: m.Groups[1].Value, Value: m.Groups[2].Value))
                .ToList();

            var extensionMatches = s_extensionPattern.Matches(iniContent).Cast<Match>()
                .Select(m => (Key: string.Empty, Value: m.Groups[1].Value))
                .ToList();

            var matches = keyedMatches.Concat(extensionMatches)
                .DistinctBy(m => m.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var nextDepth = parentNode.Depth + 1;

            foreach (var matchValue in matches)
            {
                var filePath = matchValue.Value.Trim();
                if (string.IsNullOrWhiteSpace(filePath))
                    continue;

                // تنظيف المسار
                filePath = CleanFilePath(filePath);

                // ✅ فحص القائمة السوداء: تخطي الملفات المستبعدة
                if (IsExcludedFile(filePath))
                    continue;

                if (matchValue.Key.Contains("Animation", StringComparison.OrdinalIgnoreCase) && filePath.Contains('.', StringComparison.Ordinal))
                {
                    filePath = filePath.Split('.')[0];
                }

                if (string.IsNullOrEmpty(Path.GetExtension(filePath)))
                {
                    if (matchValue.Key.Contains("Texture", StringComparison.OrdinalIgnoreCase))
                        continue;

                    filePath = AppendDefaultExtension(matchValue.Key, filePath);
                }

                // البحث عن الملف — Archive-first (90%+ of assets live in BIG archives)
                string? fullPath = null;
                bool isFound = false;
                var fileNameOnly = Path.GetFileName(filePath);

                if (_archiveIndex.TryGetValue(fileNameOnly, out var archiveLocation))
                {
                    // ❌ FIX: Ignore Base Game Archives to prevent transferring original game assets
                    if (IsBaseGameArchive(archiveLocation.ArchivePath))
                    {
                        // It's a base game asset. We verify it exists but do NOT add it as a dependency node for transfer.
                        // Unless we want to traverse it for deep dependencies (e.g. INI), but usually base objects don't need porting.
                        // We will skip it to avoid bloat.
                        continue;
                    }

                    fullPath = $"{archiveLocation.ArchivePath}::{archiveLocation.EntryPath}";
                    isFound = true;
                }
                else
                {
                    fullPath = FindPriorityFile(sourceModPath, filePath);
                    isFound = fullPath != null && File.Exists(fullPath);
                }
                var dependencyNode = CreateNodeWithPaths(
                    Path.GetFileName(filePath),
                    DetermineDependencyType(filePath),
                    fullPath,
                    sourceModPath,
                    isFound ? AssetStatus.Found : AssetStatus.Missing,
                    nextDepth);

                // تجنب التكرار
                if (!graph.AllNodes.Any(n => n.Name == dependencyNode.Name))
                {
                    graph.AllNodes.Add(dependencyNode);
                    parentNode.Dependencies.Add(dependencyNode);

                    // المتابعة العميقة (Deep Traversal) لملفات INI المتسلسلة
                    if (dependencyNode.Status == AssetStatus.Found && 
                        dependencyNode.Type == DependencyType.ObjectINI &&
                        nextDepth < MaxRecursionDepth)
                    {
                        await ParseIniDependenciesAsync(dependencyNode, sourceModPath, graph);
                    }
                }
            }

            var iniReferences = ExtractIniObjectReferences(iniContent);
            foreach (var reference in iniReferences)
            {
                var iniFilePath = ResolveIniObjectFile(reference.IniFileName, sourceModPath, reference.ObjectName, reference.Type);
                var isFound = iniFilePath != null && (IsArchiveReference(iniFilePath) || File.Exists(iniFilePath));

                var iniNode = CreateNodeWithPaths(
                    reference.ObjectName,
                    reference.Type,
                    iniFilePath,
                    sourceModPath,
                    isFound ? AssetStatus.Found : AssetStatus.Missing,
                    nextDepth);

                if (!graph.AllNodes.Any(n => n.Name == iniNode.Name && n.Type == iniNode.Type))
                {
                    graph.AllNodes.Add(iniNode);
                    parentNode.Dependencies.Add(iniNode);
                }

                if (!isFound || nextDepth >= MaxRecursionDepth)
                    continue;

                // Deep OCL Chain: الكائنات المُنتجة من OCL → تحليل تبعياتها الكامل
                if (reference.Type == DependencyType.ObjectINI && IsSpawnedFromOcl(parentNode))
                {
                    await ParseIniDependenciesAsync(iniNode, sourceModPath, graph);
                }
                // Skip recursive regex parsing when SageIndex handles deep traversal
                else if (SageIndex == null && ShouldParseDefinitionBlock(reference.Type))
                {
                    await ParseDefinitionBlockAsync(iniNode, sourceModPath, graph);
                }
            }
        }
        catch (Exception ex)
        {
            var logger = new SimpleLogger("dependency_errors.log");
            logger.LogError($"تعذر تحليل INI: {parentNode.FullPath}", ex);
            logger.LogInfo($"Stack: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"خطأ في تحليل INI: {ex.Message}");
        }
    }

    /// <summary>
    /// استخراج InheritFrom من محتوى INI وتحليل تبعيات الأب بشكل تكراري
    /// SAGE syntax: InheritFrom = ParentObjectName
    /// </summary>
    private async Task ResolveInheritFromAsync(string iniContent, DependencyNode currentNode,
        string sourceModPath, UnitDependencyGraph graph)
    {
        var inheritMatches = s_inheritPattern.Matches(iniContent);
        if (inheritMatches.Count == 0)
            return;

        foreach (Match match in inheritMatches)
        {
            var parentName = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(parentName))
                continue;

            // تجنب الحلقات: تحقق مما إذا كان الأب قد تمت معالجته بالفعل
            var inheritVisitKey = $"__inherit__{parentName}";
            if (_visitedNodes.Contains(inheritVisitKey))
                continue;
            _visitedNodes.Add(inheritVisitKey);

            // البحث عن ملف INI الخاص بالأب
            string? parentIniPath = null;

            // أولاً: استخدام SageIndex إذا كان متاحاً (الأسرع)
            if (SageIndex != null)
            {
                var parentDef = SageIndex.Lookup(parentName);
                if (parentDef != null)
                {
                    parentIniPath = parentDef.SourceFile;
                }
            }

            // ثانياً: البحث في فهرس الأرشيف
            if (string.IsNullOrWhiteSpace(parentIniPath))
            {
                parentIniPath = FindUnitIniPath(parentName, sourceModPath);
            }

            // ثالثاً: البحث في ملفات INI المحلية عن كتلة Object ParentName
            if (string.IsNullOrWhiteSpace(parentIniPath))
            {
                parentIniPath = FindDefinitionIniPath(
                    DependencyType.ObjectINI, "object.ini", parentName, sourceModPath);
            }

            if (string.IsNullOrWhiteSpace(parentIniPath))
            {
                continue;
            }

            // إنشاء عقدة تبعية للأب
            var isFound = IsArchiveReference(parentIniPath) || File.Exists(parentIniPath);
            var parentNode = CreateNodeWithPaths(
                $"[Inherit] {parentName}",
                DependencyType.ObjectINI,
                parentIniPath,
                sourceModPath,
                isFound ? AssetStatus.Found : AssetStatus.Missing,
                currentNode.Depth + 1);

            if (!graph.AllNodes.Any(n => n.Name == parentNode.Name))
            {
                graph.AllNodes.Add(parentNode);
                currentNode.Dependencies.Add(parentNode);
            }

            // تحليل تبعيات الأب بشكل تكراري (يشمل InheritFrom المتسلسل)
            if (isFound && parentNode.Depth < MaxRecursionDepth)
            {
                await ParseIniDependenciesAsync(parentNode, sourceModPath, graph);
            }
        }
    }

    private IEnumerable<IniObjectReference> ExtractIniObjectReferences(string iniContent)
    {
        var refs = new List<IniObjectReference>();

        foreach (Match match in s_objectPattern.Matches(iniContent))
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value;
            var mapped = MapIniReference(key);
            if (mapped != null)
            {
                foreach (var token in ExpandNames(value))
                {
                    refs.Add(new IniObjectReference(token, mapped.Value.Type, mapped.Value.IniFileName));
                }
            }
        }

        foreach (Match match in s_oclPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                if (!token.StartsWith("OCL", StringComparison.OrdinalIgnoreCase))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.OCL, "objectcreationlist.ini"));
            }
        }

        foreach (Match match in s_oclKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                if (!token.StartsWith("OCL", StringComparison.OrdinalIgnoreCase))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.OCL, "objectcreationlist.ini"));
            }
        }

        foreach (Match match in s_oclAnyKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                if (!token.StartsWith("OCL", StringComparison.OrdinalIgnoreCase))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.OCL, "objectcreationlist.ini"));
            }
        }

        foreach (Match match in s_oclTokenPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.OCL, "objectcreationlist.ini"));
            }
        }

        foreach (Match match in s_fxPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in s_fxPrefixPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in s_fxListKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in s_stagedFxPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in s_fxTokenPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in s_damageFxPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[2].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in s_soundPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[2].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.Audio, "soundeffects.ini"));
            }
        }

        foreach (Match match in s_musicPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.Audio, "music.ini"));
            }
        }

        foreach (Match match in s_imagePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[2].Value))
            {
                if (Path.HasExtension(token))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "mappedimages.ini"));
            }
        }

        foreach (Match match in s_imageLoosePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[3].Value))
            {
                if (Path.HasExtension(token))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "mappedimages.ini"));
            }
        }

        foreach (Match match in s_imageTokenPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.Custom, "mappedimages.ini"));
            }
        }

        foreach (Match match in s_objectNamePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.ObjectINI, "object.ini"));
            }
        }

        foreach (Match match in s_textureNamePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                if (Path.HasExtension(token))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "mappedimages.ini"));
            }
        }

        foreach (Match match in s_textureKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                if (Path.HasExtension(token))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "mappedimages.ini"));
            }
        }

        foreach (Match match in s_particleSystemPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in s_laserNameKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in s_particleSystemKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in s_particleSystemTokenPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in s_particleSysKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in s_particleSysBonePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in s_particleSysBoneKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in s_stagedWeaponPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.Weapon, "weapon.ini"));
            }
        }

        foreach (Match match in s_stagedOclPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.OCL, "objectcreationlist.ini"));
            }
        }

        foreach (Match match in s_structurePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.ObjectINI, "object.ini"));
            }
        }

        foreach (Match match in s_unitPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.ObjectINI, "object.ini"));
            }
        }

        return refs;
    }

    private static (DependencyType Type, string IniFileName)? MapIniReference(string key)
    {
        if (key.StartsWith("BaseDefenseStructure", StringComparison.OrdinalIgnoreCase))
            return (DependencyType.ObjectINI, "object.ini");

        return key.ToLowerInvariant() switch
        {
            "armor" => (DependencyType.Armor, "armor.ini"),
            "weapon" => (DependencyType.Weapon, "weapon.ini"),
            "projectile" => (DependencyType.Projectile, "projectile.ini"),
            "projectileobject" => (DependencyType.Projectile, "projectile.ini"),
            "projectiletemplate" => (DependencyType.Projectile, "projectile.ini"),
            "deathweapon" => (DependencyType.Weapon, "weapon.ini"),
            "weaponname" => (DependencyType.Weapon, "weapon.ini"),
            "crushingweaponname" => (DependencyType.Weapon, "weapon.ini"),
            "continuousweapondamaged" => (DependencyType.Weapon, "weapon.ini"),
            "continuousweaponpristine" => (DependencyType.Weapon, "weapon.ini"),
            "continuousweaponreallydamaged" => (DependencyType.Weapon, "weapon.ini"),
            "detonationweapon" => (DependencyType.Weapon, "weapon.ini"),
            "geometrybaseddamageweapon" => (DependencyType.Weapon, "weapon.ini"),
            "howitzerweapontemplate" => (DependencyType.Weapon, "weapon.ini"),
            "occupantdamageweapontemplate" => (DependencyType.Weapon, "weapon.ini"),
            "shockwaveweapontemplate" => (DependencyType.Weapon, "weapon.ini"),
            "weapontemplate" => (DependencyType.Weapon, "weapon.ini"),
            "reactionweapondamaged" => (DependencyType.Weapon, "weapon.ini"),
            "reactionweaponpristine" => (DependencyType.Weapon, "weapon.ini"),
            "reactionweaponreallydamaged" => (DependencyType.Weapon, "weapon.ini"),
            "fxlist" => (DependencyType.FXList, "fxlist.ini"),
            "deathfx" => (DependencyType.FXList, "fxlist.ini"),
            "explosionlist" => (DependencyType.FXList, "fxlist.ini"),
            "projectiledetonationfx" => (DependencyType.FXList, "fxlist.ini"),
            "firefx" => (DependencyType.FXList, "fxlist.ini"),
            "muzzleflash" => (DependencyType.FXList, "fxlist.ini"),
            "tracerfx" => (DependencyType.FXList, "fxlist.ini"),
            "beamfx" => (DependencyType.FXList, "fxlist.ini"),
            "lasername" => (DependencyType.FXList, "fxlist.ini"),
            "projectiledetonationocl" => (DependencyType.OCL, "objectcreationlist.ini"),
            "fireocl" => (DependencyType.OCL, "objectcreationlist.ini"),
            "ocl" => (DependencyType.OCL, "objectcreationlist.ini"),
            "creationlist" => (DependencyType.OCL, "objectcreationlist.ini"),
            "locomotor" => (DependencyType.Locomotor, "locomotor.ini"),
            "commandset" => (DependencyType.CommandSet, "commandset.ini"),
            "commandbutton" => (DependencyType.CommandSet, "commandbutton.ini"),
            "sound" => (DependencyType.Audio, "soundeffects.ini"),
            "voice" => (DependencyType.Audio, "voice.ini"),
            "enginesound" => (DependencyType.Audio, "soundeffects.ini"),
            "ambientsound" => (DependencyType.Audio, "soundeffects.ini"),
            "firesound" => (DependencyType.Audio, "soundeffects.ini"),
            "weaponsound" => (DependencyType.Audio, "soundeffects.ini"),
            "startsound" => (DependencyType.Audio, "soundeffects.ini"),
            "stopsound" => (DependencyType.Audio, "soundeffects.ini"),
            "loopsound" => (DependencyType.Audio, "soundeffects.ini"),
            "projectileexhaust" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "veterancyprojectileexhaust" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "projectilestream" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "projectilestreamname" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "projectiletrail" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "projectiletrailname" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "trailfx" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "contrailfx" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "smoketrailfx" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "particlesystem" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "particlesystemname" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "particlesystemid" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "slavesystem" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "objectnames" => (DependencyType.ObjectINI, "object.ini"),
            "objectname" => (DependencyType.ObjectINI, "object.ini"),
            "object" => (DependencyType.ObjectINI, "object.ini"),
            "crateobject" => (DependencyType.ObjectINI, "object.ini"),
            "unitname" => (DependencyType.ObjectINI, "object.ini"),
            "buildvariations" => (DependencyType.ObjectINI, "object.ini"),
            "damagefx" => (DependencyType.FXList, "damagefx.ini"),
            "specialpower" => (DependencyType.Upgrade, "specialpower.ini"),
            "specialpowertemplate" => (DependencyType.Upgrade, "specialpower.ini"),
            "science" => (DependencyType.Upgrade, "science.ini"),
            "requiredscience" => (DependencyType.Upgrade, "science.ini"),
            "sciencerequired" => (DependencyType.Upgrade, "science.ini"),
            "killerscience" => (DependencyType.Upgrade, "science.ini"),
            "pickupscience" => (DependencyType.Upgrade, "science.ini"),
            "grantscience" => (DependencyType.Upgrade, "science.ini"),
            "upgrade" => (DependencyType.Upgrade, "upgrade.ini"),
            "requiredupgrade" => (DependencyType.Upgrade, "upgrade.ini"),
            "prerequisiteupgrade" => (DependencyType.Upgrade, "upgrade.ini"),
            "triggeredby" => (DependencyType.Upgrade, "upgrade.ini"),
            "upgradetogrant" => (DependencyType.Upgrade, "upgrade.ini"),
            "upgradetoremove" => (DependencyType.Upgrade, "upgrade.ini"),
            "upgraderequired" => (DependencyType.Upgrade, "upgrade.ini"),
            "needsupgrade" => (DependencyType.Upgrade, "upgrade.ini"),
            "removesupgrades" => (DependencyType.Upgrade, "upgrade.ini"),
            "upgradeobject" => (DependencyType.OCL, "objectcreationlist.ini"),
            "upgradeocl" => (DependencyType.OCL, "objectcreationlist.ini"),
            "cratedata" => (DependencyType.Custom, "crate.ini"),
            "stumpname" => (DependencyType.ObjectINI, "object.ini"),
            "crashfxtemplatename" => (DependencyType.ObjectINI, "object.ini"),
            "bridgeparticle" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "attachparticle" => (DependencyType.ParticleSystem, "particlesystem.ini"),
            "referenceobject" => (DependencyType.ObjectINI, "object.ini"),
            "finalrubbleobject" => (DependencyType.ObjectINI, "object.ini"),
            "specialobject" => (DependencyType.ObjectINI, "object.ini"),
            "carriagetemplatename" => (DependencyType.ObjectINI, "object.ini"),
            "payloadtemplatename" => (DependencyType.ObjectINI, "object.ini"),
            "buttonimage" => (DependencyType.Custom, "mappedimages.ini"),
            "selectportrait" => (DependencyType.Custom, "mappedimages.ini"),
            "portraitimage" => (DependencyType.Custom, "mappedimages.ini"),
            "image" => (DependencyType.Custom, "mappedimages.ini"),
            "levelgainanimationname" => (DependencyType.Custom, "animation2d.ini"),
            "gethealedanimationname" => (DependencyType.Custom, "animation2d.ini"),
            "intromovie" => (DependencyType.Custom, "video.ini"),
            "portraitmovieleftname" => (DependencyType.Custom, "video.ini"),
            "portraitmovierightname" => (DependencyType.Custom, "video.ini"),
            "cursorname" => (DependencyType.Custom, "mouse.ini"),
            "invalidcursorname" => (DependencyType.Custom, "mouse.ini"),
            "radiuscursortype" => (DependencyType.Custom, "ingameui.ini"),
            _ => null
        };
    }

    private static IEnumerable<string> ExpandNames(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value
            .Replace(',', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0 && !IsPlaceholderToken(token));
    }

    private static bool IsPlaceholderToken(string token)
    {
        return string.Equals(token, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "NONE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "NoSound", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "NOSOUND", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "NoImage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "NOIMAGE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "unimplemented", StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveIniObjectFile(string iniFileName, string sourceModPath, string objectName, DependencyType type)
    {
        foreach (var candidate in ExpandIniFileCandidates(iniFileName))
        {
            // Archive-first: O(1) lookup before disk I/O
            if (_archiveIndex.TryGetValue(candidate, out var archiveLocation))
                return $"{archiveLocation.ArchivePath}::{archiveLocation.EntryPath}";

            var resolved = FindPriorityFile(sourceModPath, Path.Combine("INI", candidate));
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            resolved = FindPriorityFile(sourceModPath, Path.Combine("INI", "Default", candidate));
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        var definitionPath = FindDefinitionIniPath(type, iniFileName, objectName, sourceModPath);
        if (!string.IsNullOrWhiteSpace(definitionPath))
            return definitionPath;

        return null;
    }

    private static IEnumerable<string> ExpandIniFileCandidates(string iniFileName)
    {
        if (string.IsNullOrWhiteSpace(iniFileName))
            return Array.Empty<string>();

        if (!iniFileName.Equals("audio.ini", StringComparison.OrdinalIgnoreCase))
            return new[] { iniFileName };

        return new[]
        {
            "soundeffects.ini",
            "voice.ini",
            "speech.ini",
            "miscaudio.ini",
            "music.ini",
            "audio.ini"
        };
    }

    private static bool ShouldParseDefinitionBlock(DependencyType type)
    {
        return type is DependencyType.Weapon
            or DependencyType.Armor
            or DependencyType.Projectile
            or DependencyType.FXList
            or DependencyType.Audio
            or DependencyType.VisualEffect
            or DependencyType.Custom;
    }

    private static IReadOnlyList<string> GetBlockKeywords(DependencyType type, string? iniFileName, string? fullPath)
    {
        return type switch
        {
            DependencyType.Weapon => new[] { "Weapon" },
            DependencyType.Armor => new[] { "Armor" },
            DependencyType.Projectile => new[] { "Projectile" },
            DependencyType.FXList => new[] { "FXList" },
            DependencyType.ObjectINI => new[] { "Object" },
            DependencyType.Audio => GetAudioKeywords(iniFileName),
            DependencyType.VisualEffect => GetVisualEffectKeywords(iniFileName),
            DependencyType.Custom => GetCustomKeywords(iniFileName, fullPath),
            _ => Array.Empty<string>()
        };
    }

    private static IReadOnlyList<string> GetAudioKeywords(string? iniFileName)
    {
        if (string.IsNullOrWhiteSpace(iniFileName))
            return new[] { "AudioEvent", "DialogEvent", "MusicTrack" };

        return iniFileName.ToLowerInvariant() switch
        {
            "music.ini" => new[] { "MusicTrack" },
            "speech.ini" => new[] { "DialogEvent" },
            _ => new[] { "AudioEvent", "DialogEvent" }
        };
    }

    private static IReadOnlyList<string> GetVisualEffectKeywords(string? iniFileName)
    {
        if (string.IsNullOrWhiteSpace(iniFileName))
            return Array.Empty<string>();

        return iniFileName.ToLowerInvariant() switch
        {
            "particlesystem.ini" => new[] { "ParticleSystem" },
            "damagefx.ini" => new[] { "DamageFX" },
            _ => Array.Empty<string>()
        };
    }

    private static IReadOnlyList<string> GetCustomKeywords(string? iniFileName, string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(iniFileName))
            return IsMappedImagesPath(fullPath) ? new[] { "MappedImage" } : Array.Empty<string>();

        if (iniFileName.Equals("mappedimages.ini", StringComparison.OrdinalIgnoreCase))
            return new[] { "MappedImage" };

        return iniFileName.ToLowerInvariant() switch
        {
            "objectcreationlist.ini" => new[] { "ObjectCreationList" },
            "specialpower.ini" => new[] { "SpecialPower" },
            "science.ini" => new[] { "Science" },
            "upgrade.ini" => new[] { "Upgrade" },
            "commandset.ini" => new[] { "CommandSet" },
            "commandbutton.ini" => new[] { "CommandButton" },
            "mouse.ini" => new[] { "MouseCursor" },
            "ingameui.ini" => new[] { "RadiusCursor" },
            "video.ini" => new[] { "Video" },
            "animation2d.ini" => new[] { "Animation" },
            _ => Array.Empty<string>()
        };
    }

    private async Task ParseDefinitionBlockAsync(DependencyNode iniNode, string sourceModPath, UnitDependencyGraph graph)
    {
        if (iniNode.FullPath == null)
            return;

        var iniFileName = GetIniFileNameFromPath(iniNode.FullPath);
        var keywords = GetBlockKeywords(iniNode.Type, iniFileName, iniNode.FullPath);
        if (keywords.Count == 0)
            return;

        var iniContent = await ReadIniContentCachedAsync(iniNode.FullPath);

        foreach (var keyword in keywords)
        {
            var visitKey = $"{iniNode.FullPath}::{keyword}::{iniNode.Name}";
            if (!_visitedBlocks.Add(visitKey))
                continue;

            List<string> blockLines;

            if (string.Equals(iniFileName, "ingameui.ini", StringComparison.OrdinalIgnoreCase) &&
                keyword.Equals("RadiusCursor", StringComparison.OrdinalIgnoreCase))
            {
                blockLines = new List<string>();
                foreach (var startToken in BuildInGameUiCursorTokens(iniNode.Name))
                {
                    blockLines = ExtractIniBlockByToken(iniContent, startToken).ToList();
                    if (blockLines.Count > 0)
                        break;
                }
            }
            else
            {
                blockLines = ExtractIniBlock(iniContent, keyword, iniNode.Name).ToList();
            }

            if (blockLines.Count == 0)
                continue;

            var blockContent = string.Join(Environment.NewLine, blockLines);

            var fileRefs = ExtractFileReferences(blockContent);
            await AddFileDependencies(iniNode, fileRefs, sourceModPath, graph);

            var iniRefs = ExtractIniObjectReferences(blockContent);
            await AddIniDependencies(iniNode, iniRefs, sourceModPath, graph);

            if (iniNode.Type == DependencyType.FXList)
            {
                var fxRefs = ExtractFxListBlockReferences(blockLines);
                await AddIniDependencies(iniNode, fxRefs, sourceModPath, graph);
            }

            if (iniNode.Type == DependencyType.Custom &&
                string.Equals(iniFileName, "commandset.ini", StringComparison.OrdinalIgnoreCase))
            {
                var commandRefs = ExtractCommandSetBlockReferences(blockLines);
                await AddIniDependencies(iniNode, commandRefs, sourceModPath, graph);
            }
        }
    }

    private static IEnumerable<IniObjectReference> ExtractCommandSetBlockReferences(IEnumerable<string> blockLines)
    {
        var refs = new List<IniObjectReference>();

        foreach (var line in blockLines)
        {
            var match = s_commandRefPattern.Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            refs.Add(new IniObjectReference(name, DependencyType.CommandSet, "commandbutton.ini"));
        }

        return refs;
    }

    private static IEnumerable<IniObjectReference> ExtractFxListBlockReferences(IEnumerable<string> blockLines)
    {
        var refs = new List<IniObjectReference>();
        var currentSection = string.Empty;

        foreach (var rawLine in blockLines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (trimmed.Equals("Sound", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "Sound";
                continue;
            }

            if (trimmed.Equals("ParticleSystem", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = "ParticleSystem";
                continue;
            }

            if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = string.Empty;
                continue;
            }

            if (!currentSection.Equals("Sound", StringComparison.OrdinalIgnoreCase) &&
                !currentSection.Equals("ParticleSystem", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = s_fxBlockNamePattern.Match(trimmed);
            if (!match.Success)
                continue;

            var name = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (currentSection.Equals("Sound", StringComparison.OrdinalIgnoreCase))
            {
                refs.Add(new IniObjectReference(name, DependencyType.Audio, "soundeffects.ini"));
            }
            else
            {
                refs.Add(new IniObjectReference(name, DependencyType.ParticleSystem, "particlesystem.ini"));
            }
        }

        return refs;
    }

    private static IEnumerable<string> ExtractIniBlock(string iniContent, string keyword, string objectName)
    {
        using var reader = new StringReader(iniContent);
        var startToken = $"{keyword} {objectName}";
        var inBlock = false;
        var depth = 0;
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.TrimStart();
            if (!inBlock && trimmed.StartsWith(startToken, StringComparison.OrdinalIgnoreCase))
            {
                inBlock = true;
                continue;
            }

            if (!inBlock)
                continue;

            if (trimmed.StartsWith("End", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                    yield break;

                depth--;
                continue;
            }

            if (IsIniBlockStart(trimmed))
                depth++;

            yield return line;
        }
    }

    private static IEnumerable<string> ExtractIniBlockByToken(string iniContent, string startToken)
    {
        using var reader = new StringReader(iniContent);
        var inBlock = false;
        var depth = 0;
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.TrimStart();
            if (!inBlock && trimmed.StartsWith(startToken, StringComparison.OrdinalIgnoreCase))
            {
                inBlock = true;
                continue;
            }

            if (!inBlock)
                continue;

            if (trimmed.StartsWith("End", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                    yield break;

                depth--;
                continue;
            }

            if (IsIniBlockStart(trimmed))
                depth++;

            yield return line;
        }
    }

    private static IEnumerable<string> BuildInGameUiCursorTokens(string cursorType)
    {
        if (string.IsNullOrWhiteSpace(cursorType))
            return Array.Empty<string>();

        var tokens = new List<string>
        {
            $"{cursorType}RadiusCursor"
        };

        if (cursorType.Contains('_', StringComparison.Ordinal))
        {
            var pascal = string.Concat(cursorType
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
            tokens.Add($"{pascal}RadiusCursor");
        }

        return tokens;
    }

    private static bool IsIniBlockStart(string trimmedLine)
    {
        if (string.IsNullOrWhiteSpace(trimmedLine))
            return false;

        if (trimmedLine.StartsWith(";", StringComparison.Ordinal))
            return false;

        return !trimmedLine.Contains('=');
    }

    private static List<(string Key, string Value)> ExtractFileReferences(string iniContent)
    {
        var keyedMatches = s_extractFilePattern.Matches(iniContent).Cast<Match>()
            .Select(m => (Key: m.Groups[1].Value, Value: m.Groups[2].Value))
            .ToList();

        var winNameMatches = s_winNamePattern.Matches(iniContent).Cast<Match>()
            .Select(m => (Key: "WinName", Value: ExtractWinNameFile(m.Groups[1].Value)))
            .Where(m => !string.IsNullOrWhiteSpace(m.Value))
            .ToList();

        var extensionMatches = s_extractExtensionPattern.Matches(iniContent).Cast<Match>()
            .Select(m => (Key: string.Empty, Value: m.Groups[1].Value))
            .ToList();

        return keyedMatches.Concat(extensionMatches).Concat(winNameMatches)
            .DistinctBy(m => m.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ExtractWinNameFile(string value)
    {
        var trimmed = value.Trim();
        var colonIndex = trimmed.IndexOf(':');
        return colonIndex > 0 ? trimmed[..colonIndex] : trimmed;
    }

    private async Task AddFileDependencies(DependencyNode parentNode, IEnumerable<(string Key, string Value)> matches, string sourceModPath, UnitDependencyGraph graph)
    {
        var nextDepth = parentNode.Depth + 1;

        foreach (var matchValue in matches)
        {
            foreach (var token in ExpandNames(matchValue.Value))
            {
                var filePath = token.Trim();
                if (string.IsNullOrWhiteSpace(filePath))
                    continue;

                filePath = CleanFilePath(filePath);

                if (string.IsNullOrEmpty(Path.GetExtension(filePath)))
                {
                    filePath = AppendDefaultExtension(matchValue.Key, filePath);
                }

                var fullPath = FindPriorityFile(sourceModPath, filePath);
                var isFound = fullPath != null && File.Exists(fullPath);

                if (!isFound && _archiveIndex.TryGetValue(Path.GetFileName(filePath), out var archiveLocation))
                {
                    fullPath = $"{archiveLocation.ArchivePath}::{archiveLocation.EntryPath}";
                    isFound = true;
                }

                var dependencyNode = CreateNodeWithPaths(
                    Path.GetFileName(filePath),
                    DetermineDependencyType(filePath),
                    fullPath,
                    sourceModPath,
                    isFound ? AssetStatus.Found : AssetStatus.Missing,
                    nextDepth);

                if (!graph.AllNodes.Any(n => n.Name == dependencyNode.Name))
                {
                    graph.AllNodes.Add(dependencyNode);
                    parentNode.Dependencies.Add(dependencyNode);

                    var extension = Path.GetExtension(filePath);
                    var shouldParseContent = dependencyNode.Type == DependencyType.ObjectINI
                        || extension.Equals(".wnd", StringComparison.OrdinalIgnoreCase);

                    if (dependencyNode.Status == AssetStatus.Found && shouldParseContent && nextDepth < MaxRecursionDepth)
                    {
                        await ParseIniDependenciesAsync(dependencyNode, sourceModPath, graph);
                    }
                }
            }
        }
    }

    private async Task AddIniDependencies(DependencyNode parentNode, IEnumerable<IniObjectReference> references, string sourceModPath, UnitDependencyGraph graph)
    {
        var nextDepth = parentNode.Depth + 1;

        foreach (var reference in references)
        {
            // ✅ تخطي أنواع الكائنات المستبعدة
            if (IsExcludedObjectType(reference.Type, reference.ObjectName))
                continue;

            var iniFilePath = ResolveIniObjectFile(reference.IniFileName, sourceModPath, reference.ObjectName, reference.Type);
            var isFound = iniFilePath != null && (IsArchiveReference(iniFilePath) || File.Exists(iniFilePath));

            var iniNode = CreateNodeWithPaths(
                    reference.ObjectName,
                    reference.Type,
                    iniFilePath,
                    sourceModPath,
                    isFound ? AssetStatus.Found : AssetStatus.Missing,
                    nextDepth);

            if (!graph.AllNodes.Any(n => n.Name == iniNode.Name && n.Type == iniNode.Type))
            {
                graph.AllNodes.Add(iniNode);
                parentNode.Dependencies.Add(iniNode);
            }

            if (!isFound || nextDepth >= MaxRecursionDepth)
                continue;

            // ═══ Deep OCL Chain: الكائنات المُنتجة من OCL تحتاج تحليل تبعياتها الكامل ═══
            // OCL → Object (debris/fire/smoke) → كل كائن منتج له موديلات وتكستشرات وFX خاصة به
            // بدون هذا التحليل ستظهر "مربعات وردية" (Missing Textures)
            if (reference.Type == DependencyType.ObjectINI && IsSpawnedFromOcl(parentNode))
            {
                await ParseIniDependenciesAsync(iniNode, sourceModPath, graph);
            }
            else if (ShouldParseDefinitionBlock(reference.Type))
            {
                await ParseDefinitionBlockAsync(iniNode, sourceModPath, graph);
            }
        }
    }

    /// <summary>
    /// هل هذا الكائن ناتج عن OCL (ObjectCreationList)؟
    /// يتحقق من نوع العقدة الأم أو من اسم ملف INI المصدر
    /// </summary>
    private static bool IsSpawnedFromOcl(DependencyNode parentNode)
    {
        // العقدة الأم من نوع Custom وملفها objectcreationlist.ini
        if (parentNode.Type == DependencyType.Custom &&
            parentNode.FullPath != null &&
            parentNode.FullPath.Contains("objectcreationlist", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // أو اسم العقدة يبدأ بـ OCL (الاسم المعياري لكتل الـ ObjectCreationList)
        if (parentNode.Name.StartsWith("OCL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private string? FindDefinitionIniPath(DependencyType type, string? iniFileName, string objectName, string sourceModPath)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        // SageIndex-first: instant O(1) lookup before expensive file scanning
        if (SageIndex != null)
        {
            var def = SageIndex.GetMergedDefinition(objectName);
            if (def != null)
            {
                var result = def.SourceFile;
                var keywords = GetBlockKeywords(type, iniFileName, null);
                if (keywords.Count > 0)
                {
                    var cacheKey = $"{string.Join("|", keywords)}:{objectName}";
                    _objectIniCache[cacheKey] = result;
                }
                return result;
            }
        }

        var kw = GetBlockKeywords(type, iniFileName, null);
        if (kw.Count == 0)
            return null;

        var cKey = $"{string.Join("|", kw)}:{objectName}";
        if (_objectIniCache.TryGetValue(cKey, out var cachedPath))
            return cachedPath;

        var iniRoot = Path.Combine(sourceModPath, "Data", "INI");
        if (!Directory.Exists(iniRoot))
            return null;

        foreach (var file in Directory.GetFiles(iniRoot, "*.ini", SearchOption.AllDirectories))
        {
            try
            {
                var lines = File.ReadLines(file);
                if (kw.Any(keyword => lines.Any(l => l.TrimStart().StartsWith($"{keyword} {objectName}", StringComparison.OrdinalIgnoreCase))))
                {
                    _objectIniCache[cKey] = file;
                    return file;
                }
            }
            catch (Exception ex)
            {
                BlackBoxRecorder.Record("RESOLVER", "FILE_READ_ERROR", $"FindObjectIni: {file}, Error={ex.Message}");
            }
        }

        return null;
    }

    private static bool IsMappedImagesPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return false;

        return fullPath.Contains("\\MappedImages\\", StringComparison.OrdinalIgnoreCase)
            || fullPath.Contains("/MappedImages/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetIniFileNameFromPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        if (!IsArchiveReference(fullPath))
            return Path.GetFileName(fullPath);

        var parts = fullPath.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? Path.GetFileName(parts[1]) : null;
    }

    private async Task<string> ReadIniContentCachedAsync(string path)
    {
        if (_iniContentCache.TryGetValue(path, out var cached))
            return cached;

        var content = await ReadIniContentAsync(path);
        _iniContentCache[path] = content;
        return content;
    }

    private string AppendDefaultExtension(string key, string filePath)
    {
        if (string.IsNullOrWhiteSpace(key))
            return filePath;

        var normalized = key.ToLowerInvariant();

        if (normalized.Contains("model", StringComparison.Ordinal))
            return filePath + ".w3d";

        if (normalized.Contains("movehint", StringComparison.Ordinal))
            return filePath + ".w3d";

        if (normalized.Contains("texture", StringComparison.Ordinal))
            return filePath + ".dds";

        if (normalized.Contains("sound", StringComparison.Ordinal) ||
            normalized.Contains("audio", StringComparison.Ordinal) ||
            normalized.Contains("music", StringComparison.Ordinal))
        {
            return filePath + ".wav";
        }

        return normalized switch
        {
            "model" or "w3d" => filePath + ".w3d",
            "texture" or "dds" => filePath + ".dds",
            "audio" or "sound" or "music" => filePath + ".wav",
            "filename" => filePath + ".bik",
            _ => filePath
        };
    }

    /// <summary>
    /// البحث عن الملف مع أولوية الملفات التي تبدأ بـ !!
    /// </summary>
    private string? FindPriorityFile(string sourceModPath, string fileName)
    {
        // توحيد المسار
        var normalizedName = fileName.Replace('\\', '/').TrimStart('/');

        // البحث أولاً عن نسخة الأولوية (تبدأ بـ !!)
        var priorityFileName = $"!!{Path.GetFileName(normalizedName)}";
        var priorityPath = Path.Combine(sourceModPath, "Data", priorityFileName);

        if (File.Exists(priorityPath))
            return priorityPath;

        // البحث عن المسار كما هو
        var directPath = Path.Combine(sourceModPath, normalizedName);
        if (File.Exists(directPath))
            return directPath;

        // البحث عن الملف العادي داخل Data
        var normalPath = Path.Combine(sourceModPath, "Data", normalizedName);
        if (File.Exists(normalPath))
            return normalPath;

        // Archive index checked by callers — no expensive recursive disk scan needed
        return null;
    }

    private async Task<string> ReadIniContentAsync(string path)
    {
        if (!IsArchiveReference(path))
        {
            var bytes = await File.ReadAllBytesAsync(path);
            return DecodeIniBytes(bytes);
        }

        var parts = path.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return string.Empty;

        var archivePath = parts[0];
        var entryPath = parts[1];

        // إذا كان المسار مجرد اسم أرشيف، حاول ربطه بالجذر أو بالأرشيف المفهرس
        if (!Path.IsPathRooted(archivePath))
        {
            var candidate = _archiveIndexRoot != null ? Path.Combine(_archiveIndexRoot, archivePath) : archivePath;
            if (File.Exists(candidate))
            {
                archivePath = candidate;
            }
            else if (_archiveIndex.TryGetValue(Path.GetFileName(archivePath), out var loc))
            {
                archivePath = loc.ArchivePath;
            }
        }
        else if (!File.Exists(archivePath) && _archiveIndex.TryGetValue(Path.GetFileName(archivePath), out var loc2))
        {
            archivePath = loc2.ArchivePath;
        }

        if (!_archiveManagerCache.TryGetValue(archivePath, out var manager))
        {
            manager = new Archives.BigArchiveManager(archivePath);
            await manager.LoadAsync();
            _archiveManagerCache[archivePath] = manager;
        }

        var data = await manager.ExtractFileAsync(entryPath);
        return DecodeIniBytes(data);
    }

    /// <summary>
    /// كشف الترميز تلقائياً مع دعم Windows-1256 (العربية)
    /// يفحص BOM أولاً، ثم UTF-8، ثم يميز بين 1256 و 1252 حسب توزيع البايتات
    /// </summary>
    internal static string DecodeIniBytes(byte[] data)
    {
        if (data.Length == 0)
            return string.Empty;

        // 1. BOM detection
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return System.Text.Encoding.UTF8.GetString(data, 3, data.Length - 3);

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(data, 2, data.Length - 2);

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);

        // 2. Try UTF-8 (valid UTF-8 multibyte sequences are rare in single-byte encodings)
        if (IsValidUtf8(data))
        {
            var utf8Text = System.Text.Encoding.UTF8.GetString(data);
            // Only accept UTF-8 if we actually found multibyte characters,
            // otherwise it's just ASCII which is valid in any encoding
            if (utf8Text.Any(c => c > 127))
                return utf8Text;
        }

        // 3. Distinguish Windows-1256 (Arabic) from Windows-1252 (Latin)
        // Bytes 0xC0-0xDB in Windows-1256 map to Arabic letters (ؠ-ۛ)
        // Same range in Windows-1252 maps to Latin letters (À-Û)
        // Heuristic: count bytes that fall in Arabic-specific ranges of 1256
        int arabicIndicatorCount = 0;
        int highByteCount = 0;

        foreach (var b in data)
        {
            if (b < 0x80) continue;
            highByteCount++;

            // Bytes that are Arabic letters in 1256 but NOT valid Latin in 1252:
            // 0x81, 0x8D, 0x8E, 0x90, 0x9D, 0x9E → undefined in 1252, Arabic in 1256
            // 0xC1-0xDA → Arabic letters ﺁ-ﻱ in 1256 (vs À-Ú in 1252)
            // 0xE0-0xEF → Arabic letters (ـ ف ق ك ل م ن ه و ى ي) in 1256
            // Key: 0x81,0x8D,0x8E,0x90,0x9D,0x9E are undefined in 1252 → strong Arabic signal
            if (b == 0x81 || b == 0x8D || b == 0x8E || b == 0x90 || b == 0x9D || b == 0x9E)
            {
                arabicIndicatorCount += 5; // Strong signal: these bytes are invalid in 1252
            }
            // Bytes 0xC7-0xDA are common Arabic letters in 1256 (Ç=ا, È=ب, É=ة, etc.)
            else if (b >= 0xC7 && b <= 0xDA)
            {
                arabicIndicatorCount++;
            }
            // 0xE0-0xEF also Arabic in 1256
            else if (b >= 0xE0 && b <= 0xEF)
            {
                arabicIndicatorCount++;
            }
        }

        // If >30% of high bytes look Arabic, or any strong Arabic indicators found, use 1256
        if (highByteCount > 0 && (arabicIndicatorCount * 100 / highByteCount) > 30)
        {
            return System.Text.Encoding.GetEncoding(1256).GetString(data);
        }

        // 4. Fallback: Windows-1252 (standard SAGE engine Latin encoding)
        return System.Text.Encoding.GetEncoding(1252).GetString(data);
    }

    /// <summary>
    /// فحص صلاحية تسلسلات UTF-8 متعددة البايت
    /// </summary>
    private static bool IsValidUtf8(byte[] data)
    {
        int i = 0;
        while (i < data.Length)
        {
            if (data[i] < 0x80)
            {
                i++;
                continue;
            }

            int expectedContinuation;
            if ((data[i] & 0xE0) == 0xC0) expectedContinuation = 1;
            else if ((data[i] & 0xF0) == 0xE0) expectedContinuation = 2;
            else if ((data[i] & 0xF8) == 0xF0) expectedContinuation = 3;
            else return false; // Invalid UTF-8 lead byte

            if (i + expectedContinuation >= data.Length) return false;

            for (int j = 1; j <= expectedContinuation; j++)
            {
                if ((data[i + j] & 0xC0) != 0x80) return false;
            }

            i += expectedContinuation + 1;
        }
        return true;
    }

    private static bool IsArchiveReference(string path)
    {
        return path.Contains("::", StringComparison.Ordinal);
    }

    private sealed record IniObjectReference(string ObjectName, DependencyType Type, string IniFileName);

    /// <summary>
    /// تحديد نوع التبعية بناءً على امتداد الملف
    /// </summary>
    private DependencyType DetermineDependencyType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        return extension switch
        {
            ".w3d" => DependencyType.Model3D,
            ".dds" => DependencyType.Texture,
            ".wav" or ".mp3" or ".wma" => DependencyType.Audio,
            ".ini" => DependencyType.ObjectINI,
            ".tga" or ".bmp" => DependencyType.Texture,
            _ => DependencyType.Custom
        };
    }

    /// <summary>
    /// تنظيف مسار الملف من الأحرف الخاصة
    /// </summary>
    private string CleanFilePath(string filePath)
    {
        // إزالة علامات الاقتباس
        filePath = filePath.Trim('"', '\'');
        // إزالة تعليقات INI (;...) و C++ (//...)
        var commentIdx = filePath.IndexOf(';');
        if (commentIdx >= 0)
            filePath = filePath.Substring(0, commentIdx);
        commentIdx = filePath.IndexOf("//", StringComparison.Ordinal);
        if (commentIdx >= 0)
            filePath = filePath.Substring(0, commentIdx);
        
        // 🔧 Fix: Remove SAGE placeholder paths like {MISSING}\ or {PLACEHOLDER}\
        filePath = s_sagePlaceholderPattern.Replace(filePath, "");

        filePath = s_multiSlashPattern.Replace(filePath, "/");
        // إزالة المسافات الزائدة
        return filePath.Trim();
    }

    /// <summary>
    /// التحقق من وجود جميع التبعيات
    /// </summary>
    public async Task<bool> ValidateDependenciesAsync(UnitDependencyGraph graph, string sourceModPath)
    {
        return await Task.Run(() =>
        {
            if (graph.AllNodes.Count == 0)
                return false;

            foreach (var node in graph.AllNodes)
            {
                if (node.FullPath == null)
                {
                    node.Status = AssetStatus.Missing;
                }
                else if (node.FullPath.Contains("::", StringComparison.Ordinal))
                {
                    node.Status = AssetStatus.Found;
                    var parts = node.FullPath.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && _archiveIndex.TryGetValue(Path.GetFileName(parts[1]), out var archiveLocation))
                    {
                        node.SizeInBytes = archiveLocation.Size;
                    }
                }
                else
                {
                    node.Status = AssetStatus.Found;
                    var fileInfo = new FileInfo(node.FullPath);
                    node.SizeInBytes = fileInfo.Length;
                    node.LastModified = fileInfo.LastWriteTimeUtc;
                }
            }

            graph.FoundCount = graph.AllNodes.Count(n => n.Status == AssetStatus.Found);
            graph.MissingCount = graph.AllNodes.Count(n => n.Status == AssetStatus.Missing);
            graph.TotalSizeInBytes = graph.AllNodes
                .Where(n => n.SizeInBytes.HasValue)
                .Sum(n => n.SizeInBytes!.Value);
            return graph.AllNodes.All(n => n.Status == AssetStatus.Found);
        });
    }

    /// <summary>
    /// الحصول على قائمة الملفات التي يجب نقلها
    /// </summary>
    public async Task<List<DependencyNode>> GetFilesToTransferAsync(UnitDependencyGraph graph)
    {
        return await Task.Run(() =>
        {
            var filesToTransfer = graph.AllNodes
                .Where(n => n.Status == AssetStatus.Found && n.FullPath != null)
                .OrderBy(n => n.Depth) // نقل الملفات الأب أولاً
                .ToList();

            return filesToTransfer;
        });
    }

    /// <summary>
    /// Deep recursive SAGE chain traversal with selective filtering.
    /// Only traverses critical dependency types to prevent bloat:
    /// - Weapon, WeaponSet, Projectile (combat-critical)
    /// - FXList, AudioEvent (effects and sounds)
    /// - Armor, Locomotor (unit properties)
    /// - CommandSet, CommandButton (UI)
    /// Uses the SageDefinitionIndex for fast lookup of specific named SAGE blocks.
    /// Max depth: 2 (strict limit to prevent Campaign/Mission/Map leaks)
    /// </summary>
    private void DeepSageChainTraversal(DependencyNode parentNode, UnitDependencyGraph graph, string sourceModPath)
    {
        if (SageIndex == null) return;

        // ✅ الحل: استخراج انتقائي فقط
        var unresolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ✅ فقط استخراج من Unit definition نفسها (بدون جميع العقد)
        var unitDef = SageIndex.Lookup(graph.UnitName);
        if (unitDef == null) return;

        // ✅ فلترة: فقط الأنواع المطلوبة + BLACK LIST للملفات الخطرة
        // تضمين Upgrade و Science حتى يعمل السلاح فور البناء (ترقيات السلاح/الوحدة تنتقل مع النقل)
        var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Weapon",
            "WeaponSet",
            "Projectile",
            "FXList",
            "AudioEvent",
            "Armor",
            "Locomotor",
            "CommandSet",
            "CommandButton",
            "Upgrade",
            "Science"
        };

        // BLACK LIST: تجنب العقد الخطرة تماماً
        var dangerousPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Campaign", "Mission", "Map", "PlayerTemplate", "ControlBarScheme",
            "InGameUI", "MainMenu", "Skirmish", "MultiPlayer", "Credits"
        };

        var unitRefs = SageDefinitionIndex.ExtractReferences(unitDef);
        foreach (var r in unitRefs)
        {
            // ✅ فحص BLACK LIST أولاً
            if (dangerousPatterns.Any(p => r.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            // ✅ تحقق من النوع المسموح
            var refDef = SageIndex.GetMergedDefinition(r.Name);
            if (refDef != null && allowedTypes.Contains(refDef.BlockType))
            {
                unresolvedNames.Add(r.Name);
            }
        }

        // ✅ حل فقط المراجع المفلترة بعمق منخفض جداً (2 فقط)
        foreach (var name in unresolvedNames.ToList())
        {
            // ✅ عمق 2 جداً: Level 1 = Weapon/Armor، Level 2 = Projectile/FX
            RecursiveResolve(name, parentNode, graph, sourceModPath, 1, 2);
        }
    }

    private void RecursiveResolve(string name, DependencyNode parent, UnitDependencyGraph graph,
        string sourceModPath, int depth, int maxDepth)
    {
        if (SageIndex == null || depth > maxDepth) return;
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_visitedBlocks.Contains(name)) return;
        _visitedBlocks.Add(name);

        var def = SageIndex.GetMergedDefinition(name);
        if (def == null) return;

        // Create or find node for this definition
        var existingNode = graph.AllNodes.FirstOrDefault(n =>
            n.Name != null && n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        DependencyNode node;
        if (existingNode != null)
        {
            node = existingNode;
            // Update status if it was missing but we found it in the index
            if (node.Status == AssetStatus.Missing)
            {
                node.Status = AssetStatus.Found;
                node.FullPath = def.SourceFile;
            }
        }
        else
        {
            node = CreateNodeWithPaths(
                name,
                MapBlockTypeToDependencyType(def.BlockType),
                def.SourceFile,
                sourceModPath,
                AssetStatus.Found,
                depth);
            parent.Dependencies.Add(node);
            graph.AllNodes.Add(node);
        }

        // Extract references from this definition and recursively resolve
        var refs = SageDefinitionIndex.ExtractReferences(def);
        foreach (var sageRef in refs)
        {
            if (_visitedBlocks.Contains(sageRef.Name)) continue;

            // Check if it's a file reference (has extension)
            if (HasFileExtension(sageRef.Name))
            {
                AddFileNode(sageRef, node, graph, sourceModPath, depth + 1);
            }
            else
            {
                // It's a named SAGE reference - recurse
                RecursiveResolve(sageRef.Name, node, graph, sourceModPath, depth + 1, maxDepth);
            }
        }
    }

    private void AddFileNode(SageReference sageRef, DependencyNode parent, UnitDependencyGraph graph,
        string sourceModPath, int depth)
    {
        var fileName = sageRef.Name.Trim();
        if (graph.AllNodes.Any(n => n.Name != null && n.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            return;

        var depType = sageRef.Type switch
        {
            SageRefType.Model => DependencyType.Model3D,
            SageRefType.Texture => DependencyType.Texture,
            SageRefType.Audio => DependencyType.Audio,
            _ => DependencyType.Custom
        };

        // Try to find the file
        string? fullPath = null;
        var status = AssetStatus.Missing;

        // Check archive index
        if (_archiveIndex.TryGetValue(fileName, out var archLoc))
        {
            fullPath = $"{archLoc.ArchivePath}::{archLoc.EntryPath}";
            status = AssetStatus.Found;
        }
        else
        {
            // Try common paths
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
                if (File.Exists(testPath))
                {
                    fullPath = testPath;
                    status = AssetStatus.Found;
                    break;
                }
            }
        }

        var fileNode = CreateNodeWithPaths(
            fileName,
            depType,
            fullPath,
            sourceModPath,
            status,
            depth);
        parent.Dependencies.Add(fileNode);
        graph.AllNodes.Add(fileNode);
    }

    private static DependencyType MapBlockTypeToDependencyType(string blockType)
    {
        return blockType.ToUpperInvariant() switch
        {
            "WEAPON" => DependencyType.Weapon,
            "PROJECTILE" => DependencyType.Projectile,
            "FXLIST" => DependencyType.FXList,
            "OBJECTCREATIONLIST" => DependencyType.OCL,
            "PARTICLESYSTEM" => DependencyType.ParticleSystem,
            "ARMOR" => DependencyType.Armor,
            "LOCOMOTOR" => DependencyType.Locomotor,
            "OBJECT" or "OBJECTRESKIN" => DependencyType.ObjectINI,
            "COMMANDBUTTON" or "COMMANDSET" => DependencyType.CommandSet,
            "AUDIOEVENT" => DependencyType.Audio,
            "UPGRADE" or "SPECIALPOWER" or "SCIENCE" => DependencyType.Upgrade,
            _ => DependencyType.Custom
        };
    }

    private static bool HasFileExtension(string name)
    {
        return name.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// إنشاء DependencyNode مع ملء المسارات المتخصصة تلقائياً
    /// </summary>
    private DependencyNode CreateNodeWithPaths(
        string name, 
        DependencyType type, 
        string? fullPath,
        string modPath,
        AssetStatus status = AssetStatus.NotVerified,
        int depth = 0)
    {
        var node = new DependencyNode
        {
            Name = name,
            Type = type,
            FullPath = fullPath,
            Status = status,
            Depth = depth
        };

        // ملء المسارات المتخصصة حسب نوع التبعية
        switch (type)
        {
            case DependencyType.Texture:
                // Textures عادة في Art\Textures\
                node.DdsFilePath = fullPath != null && fullPath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                    ? fullPath
                    : fullPath != null ? $"{fullPath}.dds" : null;
                
                // التحقق من وجود الملف
                if (node.DdsFilePath != null)
                {
                    var ddsFullPath = Path.Combine(modPath, node.DdsFilePath);
                    if (File.Exists(ddsFullPath))
                    {
                        node.Status = AssetStatus.Found;
                        node.SizeInBytes = new FileInfo(ddsFullPath).Length;
                    }
                    else if (status != AssetStatus.Found) // لا تُعدّل إذا كان Found من الأرشيف
                    {
                        node.Status = AssetStatus.Missing;
                    }
                }
                break;

            case DependencyType.Model3D:
                // Models عادة في Art\W3D\
                node.W3dFilePath = fullPath != null && fullPath.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase)
                    ? fullPath
                    : fullPath != null ? $"{fullPath}.w3d" : null;
                
                if (node.W3dFilePath != null)
                {
                    var w3dFullPath = Path.Combine(modPath, node.W3dFilePath);
                    if (File.Exists(w3dFullPath))
                    {
                        node.Status = AssetStatus.Found;
                        node.SizeInBytes = new FileInfo(w3dFullPath).Length;
                    }
                    else if (status != AssetStatus.Found)
                    {
                        node.Status = AssetStatus.Missing;
                    }
                }
                break;

            case DependencyType.Audio:
                // Audio files عادة في Data\Audio\
                node.AudioFilePath = fullPath;
                
                // Audio قد يكون wav أو mp3
                if (fullPath != null)
                {
                    var audioExtensions = new[] { ".wav", ".mp3", ".ogg" };
                    foreach (var ext in audioExtensions)
                    {
                        var audioPath = Path.Combine(modPath, fullPath + ext);
                        if (File.Exists(audioPath))
                        {
                            node.AudioFilePath = fullPath + ext;
                            node.Status = AssetStatus.Found;
                            node.SizeInBytes = new FileInfo(audioPath).Length;
                            break;
                        }
                    }
                }
                
                if (node.Status == AssetStatus.NotVerified && status != AssetStatus.Found)
                    node.Status = AssetStatus.Missing;
                break;

            case DependencyType.ObjectINI:
            case DependencyType.Weapon:
            case DependencyType.Armor:
            case DependencyType.Projectile:
                // INI files
                node.IniFilePath = fullPath != null && fullPath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                    ? fullPath
                    : fullPath != null ? $"{fullPath}.ini" : null;
                
                if (node.IniFilePath != null)
                {
                    var iniFullPath = Path.Combine(modPath, node.IniFilePath);
                    if (File.Exists(iniFullPath))
                    {
                        node.Status = AssetStatus.Found;
                        node.SizeInBytes = new FileInfo(iniFullPath).Length;
                    }
                    else if (status != AssetStatus.Found)
                    {
                        node.Status = AssetStatus.Missing;
                    }
                }
                break;

            default:
                // أنواع أخرى: استخدام FullPath فقط
                if (status == AssetStatus.NotVerified)
                    node.Status = AssetStatus.Unknown;
                break;
        }

        return node;
    }

    private static bool IsBaseGameArchive(string archivePath)
    {
        var name = Path.GetFileName(archivePath);
        return s_baseArchives.Contains(name) || name.StartsWith("Speech", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// تحقق من وجود الملف في القائمة السوداء
    /// </summary>
    private static bool IsExcludedFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var lowerPath = filePath.ToLowerInvariant();
        return s_excludedFilePatterns.Any(pattern => lowerPath.Contains(pattern));
    }

    /// <summary>
    /// تحقق من وجود نوع الكائن في القائمة السوداء
    /// </summary>
    private static bool IsExcludedObjectType(DependencyType type, string? typeName = null)
    {
        return type is DependencyType.Custom ||
               (typeName != null && s_excludedObjectTypes.Contains(typeName));
    }

    /// <summary>
    /// ✅ المرحلة 3: فحص وتطبيق حد أقصى آمن لعدد التبعيات
    /// هذا يحمي من الرسوم البيانية الضخمة التي قد تؤدي لمشاكل في الأداء والذاكرة
    /// عند تجاوز 80 عقدة، يتم القطع الحاد مع تحذير
    /// </summary>
    private void CheckAndEnforceDependencyLimits(UnitDependencyGraph graph)
    {
        if (graph.AllNodes.Count <= MaxTotalDependencies)
            return;

        var logger = new SimpleLogger("dependency_limits.log");
        var excessCount = graph.AllNodes.Count - MaxTotalDependencies;

        // تحذير خطير عند تجاوز الحد الأقصى
        logger.LogWarning(
            $"[{graph.UnitName}] ⚠️ CRITICAL: Dependency limit EXCEEDED!");
        logger.LogWarning(
            $"     Expected: {MaxTotalDependencies} nodes | Actual: {graph.AllNodes.Count} nodes");
        logger.LogWarning(
            $"     Excess: {excessCount} nodes will be REMOVED");
        logger.LogWarning(
            $"     This usually means: DeepSageChainTraversal found Campaign/Mission/Map files");
        logger.LogWarning(
            $"     Solution: Check FilterPatterns in DeepSageChainTraversal");

        // قائمة أولويات: احتفظ بالأنواع الحرجة أولاً
        var priorityOrder = new Dictionary<DependencyType, int>
        {
            { DependencyType.Weapon, 1 },                  // أولويات: Weapons أولاً
            { DependencyType.Projectile, 2},
            { DependencyType.Armor, 3 },
            { DependencyType.Locomotor, 4 },
            { DependencyType.FXList, 5 },
            { DependencyType.Audio, 6 },
            { DependencyType.CommandSet, 7 },
            { DependencyType.Model3D, 8 },
            { DependencyType.Texture, 9 },
            { DependencyType.ObjectINI, 10 },
            { DependencyType.Custom, 99 },               // آخر الأولويات
        };

        // ترتيب: الأنواع الحرجة أولاً، ثم حسب العمق (الآباء قبل الأطفال)، ثم الاسم
        var prioritized = graph.AllNodes
            .OrderBy(n => priorityOrder.TryGetValue(n.Type, out var priority) ? priority : 50)
            .ThenBy(n => n.Depth)
            .ThenBy(n => n.Name)
            .ToList();

        // احتفظ فقط بأول MaxTotalDependencies
        var keptNodes = prioritized.Take(MaxTotalDependencies).ToHashSet();
        var removedCount = graph.AllNodes.Count - keptNodes.Count;
        var removedNodes = graph.AllNodes.Except(keptNodes).ToList();

        // تسجيل الملفات المحذوفة (خاصة إذا كانت تحتوي على Campaign/Mission)
        var dangerousRemoved = removedNodes
            .Where(n => n.Name != null && (
                n.Name.Contains("Campaign", StringComparison.OrdinalIgnoreCase) ||
                n.Name.Contains("Mission", StringComparison.OrdinalIgnoreCase) ||
                n.Name.Contains("Map", StringComparison.OrdinalIgnoreCase) ||
                n.Name.Contains("PlayerTemplate", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (dangerousRemoved.Count > 0)
        {
            logger.LogWarning($"     ✂️ Removed {dangerousRemoved.Count} DANGEROUS nodes:");
            foreach (var node in dangerousRemoved.Take(10))
            {
                logger.LogWarning($"        - {node.Name} ({node.Type})");
            }
        }

        // تحديث الرسم البياني
        graph.AllNodes = keptNodes.ToList();

        // تحديث على مستوى الأب-الطفل (أزل الروابط للعقد المحذوفة)
        foreach (var node in graph.AllNodes.ToList())
        {
            node.Dependencies.RemoveAll(d => !keptNodes.Contains(d));
        }

        // تحديث الحالة
        if (graph.MissingCount > 0)
            graph.Status = CompletionStatus.Partial;

        var notes = $"⚠️ DEPENDENCY LIMIT ENFORCED: " +
                   $"Max {MaxTotalDependencies} nodes. " +
                   $"Removed {removedCount} nodes. " +
                   $"Final: {graph.AllNodes.Count} nodes.";

        if (!string.IsNullOrEmpty(graph.Notes))
            graph.Notes += $"; {notes}";
        else
            graph.Notes = notes;

        logger.LogInfo($"[{graph.UnitName}] Graph reduced to {graph.AllNodes.Count} nodes (removed {removedCount})");
        logger.LogInfo($"[{graph.UnitName}] Transfer should be SAFE now (no Campaign/Mission/Map files)");
    }

    private sealed record ArchiveLocation(string ArchivePath, string EntryPath, long Size, bool IsHighPriority);
}
