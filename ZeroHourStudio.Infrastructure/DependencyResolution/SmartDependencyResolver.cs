using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Logging;
using ZeroHourStudio.Infrastructure.Services;
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
    private readonly Dictionary<string, DependencyNode> _cachedNodes = new();
    private HashSet<string> _visitedNodes = new();
    private readonly Dictionary<string, string> _unitIniCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _objectIniCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _iniContentCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visitedBlocks = new(StringComparer.OrdinalIgnoreCase);
    private string? _archiveIndexRoot;
    private readonly Dictionary<string, ArchiveLocation> _archiveIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Archives.BigArchiveManager> _archiveManagerCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// SAGE definition index for deep recursive dependency chain traversal.
    /// Set this before calling ResolveDependenciesAsync for deep analysis.
    /// </summary>
    public SageDefinitionIndex? SageIndex { get; set; }

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
        // 🔧 Clear caches at the beginning of each analysis to prevent memory bloat
        _visitedNodes.Clear();
        _cachedNodes.Clear();
        _visitedBlocks.Clear();
        
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
            }
            else
            {
                // Fallback: legacy regex-based resolution (limited)
                // Note: caches already cleared at method start
                await ParseIniDependenciesAsync(rootNode, sourceModPath, graph);
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
            catch
            {
                // تجاهل الملفات التي لا يمكن قراءتها
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
            catch
            {
                // تجاهل الأرشيفات التي لا يمكن قراءتها
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
            System.Diagnostics.Debug.WriteLine($"[Resolver] INI content for {parentNode.Name}: {iniContent.Length} chars");

            // استخراج جميع المراجع للملفات
            var filePattern = new Regex(@"^\s*([A-Za-z0-9_]*ModelName[A-Za-z0-9_]*|Model|ModelNames|Animation|IdleAnimation|[A-Za-z0-9_]*Texture[A-Za-z0-9_]*|Texture|TextureName|ParticleName|MoveHintName|W3D|DDS|Audio|Sound|Sounds|Music|Filename|DeathFX|FXList|Weapon|Armor|TrackMarks|Dust|DirtSpray|PowerslideSpray)\s*=?\s+([^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            var extensionPattern = new Regex(@"([A-Za-z0-9_\-./\\]+\.(w3d|dds|tga|wav|mp3|wma|ini|wnd|map|str|bik|htm|html))", RegexOptions.IgnoreCase);

            var keyedMatches = filePattern.Matches(iniContent).Cast<Match>()
                .Select(m => (Key: m.Groups[1].Value, Value: m.Groups[2].Value))
                .ToList();

            var extensionMatches = extensionPattern.Matches(iniContent).Cast<Match>()
                .Select(m => (Key: string.Empty, Value: m.Groups[1].Value))
                .ToList();

            var matches = keyedMatches.Concat(extensionMatches)
                .DistinctBy(m => m.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[Resolver] Keyed={keyedMatches.Count}, Extension={extensionMatches.Count}, Total={matches.Count}");

            var nextDepth = parentNode.Depth + 1;

            foreach (var matchValue in matches)
            {
                var filePath = matchValue.Value.Trim();
                if (string.IsNullOrWhiteSpace(filePath))
                    continue;

                // تنظيف المسار
                filePath = CleanFilePath(filePath);

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

                // البحث عن الملف
                var fullPath = FindPriorityFile(sourceModPath, filePath);
                var isFound = fullPath != null && File.Exists(fullPath);

                if (!isFound && _archiveIndex.TryGetValue(Path.GetFileName(filePath), out var archiveLocation))
                {
                    fullPath = $"{archiveLocation.ArchivePath}::{archiveLocation.EntryPath}";
                    isFound = true;
                }
                var dependencyNode = new DependencyNode
                {
                    Name = Path.GetFileName(filePath),
                    Type = DetermineDependencyType(filePath),
                    FullPath = fullPath,
                    Status = isFound ? AssetStatus.Found : AssetStatus.Missing,
                    Depth = nextDepth
                };

                // تجنب التكرار
                if (!graph.AllNodes.Any(n => n.Name == dependencyNode.Name))
                {
                    graph.AllNodes.Add(dependencyNode);
                    parentNode.Dependencies.Add(dependencyNode);

                    // المتابعة العميقة (Deep Traversal) لملفات INI المتسلسلة
                    if (dependencyNode.Status == AssetStatus.Found && 
                        dependencyNode.Type == DependencyType.ObjectINI &&
                        nextDepth < 5) // تجنب الحلقات غير المنتهية
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

                var iniNode = new DependencyNode
                {
                    Name = reference.ObjectName,
                    Type = reference.Type,
                    FullPath = iniFilePath,
                    Status = isFound ? AssetStatus.Found : AssetStatus.Missing,
                    Depth = nextDepth
                };

                if (!graph.AllNodes.Any(n => n.Name == iniNode.Name && n.Type == iniNode.Type))
                {
                    graph.AllNodes.Add(iniNode);
                    parentNode.Dependencies.Add(iniNode);
                }

                // Skip recursive regex parsing when SageIndex handles deep traversal
                if (SageIndex == null && isFound && ShouldParseDefinitionBlock(reference.Type))
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

    private IEnumerable<IniObjectReference> ExtractIniObjectReferences(string iniContent)
    {
        var refs = new List<IniObjectReference>();
        var objectPattern = new Regex(@"^\s*(Armor|DeathWeapon|WeaponName|CrushingWeaponName|ContinuousWeaponDamaged|ContinuousWeaponPristine|ContinuousWeaponReallyDamaged|DetonationWeapon|GeometryBasedDamageWeapon|HowitzerWeaponTemplate|OccupantDamageWeaponTemplate|ShockwaveWeaponTemplate|WeaponTemplate|ReactionWeaponDamaged|ReactionWeaponPristine|ReactionWeaponReallyDamaged|Projectile|FXList|Locomotor|DeathFX|ExplosionList|CommandSet|CommandButton|Sound|Voice|EngineSound|AmbientSound|ProjectileObject|ProjectileTemplate|ProjectileDetonationFX|ProjectileDetonationOCL|FireFX|FireOCL|MuzzleFlash|TracerFX|BeamFX|LaserName|FireSound|WeaponSound|StartSound|StopSound|LoopSound|ProjectileExhaust|VeterancyProjectileExhaust|ProjectileStream|ProjectileStreamName|ProjectileTrail|ProjectileTrailName|TrailFX|ContrailFX|SmokeTrailFX|ParticleSystem|ParticleSystemName|ParticleSystemID|SlaveSystem|AttachParticle|ObjectNames|ObjectName|Object|CrateObject|CrateData|ReferenceObject|FinalRubbleObject|SpecialObject|UnitName|BuildVariations|CarriageTemplateName|PayloadTemplateName|DamageFX|SpecialPower|SpecialPowerTemplate|Science|RequiredScience|ScienceRequired|KillerScience|PickupScience|GrantScience|Upgrade|RequiredUpgrade|PrerequisiteUpgrade|TriggeredBy|UpgradeObject|UpgradeOCL|UpgradeToGrant|UpgradeToRemove|UpgradeRequired|NeedsUpgrade|RemovesUpgrades|UpgradeCameo|UpgradeCameo1|UpgradeCameo2|UpgradeCameo3|UpgradeCameo4|UpgradeCameo5|UpgradeCameo6|UpgradeCameo7|UpgradeCameo8|ButtonImage|SelectPortrait|PortraitImage|Image|IntroMovie|PortraitMovieLeftName|PortraitMovieRightName|CreationList|Dust|DirtSpray|PowerslideSpray|TrackMarks|WeaponMuzzleFlash|CursorName|InvalidCursorName|RadiusCursorType|LevelGainAnimationName|GetHealedAnimationName|CrashFXTemplateName|BridgeParticle|StumpName|BaseDefenseStructure[A-Za-z0-9_]*)\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var oclPattern = new Regex(@"^\s*[A-Za-z0-9_]*OCL\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var oclKeyPattern = new Regex(@"^\s*OCL[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var oclAnyKeyPattern = new Regex(@"^\s*[A-Za-z0-9_]*OCL[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var oclTokenPattern = new Regex(@"OCL:([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var fxPattern = new Regex(@"^\s*[A-Za-z0-9_]*FX\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var fxPrefixPattern = new Regex(@"^\s*FX[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var fxTokenPattern = new Regex(@"FX:([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var stagedFxPattern = new Regex(@"^\s*FX\s*=?\s+(?:INITIAL|FINAL|MIDPOINT|PRIMARY|SECONDARY|TERTIARY)\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var fxListKeyPattern = new Regex(@"^\s*[A-Za-z0-9_]*FXList[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var soundPattern = new Regex(@"^\s*[A-Za-z0-9_]*(Sound(?!s)|Voice)[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var musicPattern = new Regex(@"^\s*[A-Za-z0-9_]*Music[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var imagePattern = new Regex(@"^\s*[A-Za-z0-9_]*(Image|Portrait)[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_\.]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var imageLoosePattern = new Regex(@"^\s*([A-Za-z0-9_]*(Image|Portrait|Button|Logo|Arrow|Marker|Cameo)[A-Za-z0-9_]*)\s+([A-Za-z0-9_\.]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var imageTokenPattern = new Regex(@"\bIMAGE:\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var objectNamePattern = new Regex(@"^\s*[A-Za-z0-9_]*ObjectName[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var textureNamePattern = new Regex(@"^\s*Texture\s*=?\s+([A-Za-z0-9_\.]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var textureKeyPattern = new Regex(@"^\s*[A-Za-z0-9_]*Texture[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_\.]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var particleSystemPattern = new Regex(@"^\s*[A-Za-z0-9_]*ParticleSystem\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var laserNameKeyPattern = new Regex(@"^\s*[A-Za-z0-9_]*LaserName[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var particleSystemKeyPattern = new Regex(@"^\s*[A-Za-z0-9_]*Particle[A-Za-z0-9_]*System[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var particleSystemTokenPattern = new Regex(@"PSys:([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var particleSysKeyPattern = new Regex(@"^\s*[A-Za-z0-9_]*ParticleSys[A-Za-z0-9_]*\s*=?\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var particleSysBonePattern = new Regex(@"^\s*ParticleSysBone\s*=?\s+[A-Za-z0-9_]+\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var particleSysBoneKeyPattern = new Regex(@"^\s*[A-Za-z0-9_]*ParticleSysBone[A-Za-z0-9_]*\s*=?\s+[A-Za-z0-9_]+\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var stagedWeaponPattern = new Regex(@"^\s*Weapon\s*=?\s+(?:INITIAL|FINAL|MIDPOINT|PRIMARY|SECONDARY|TERTIARY)?\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var damageFxPattern = new Regex(@"^\s*(MajorFX|MinorFX|VeterancyMajorFX|VeterancyMinorFX)\s*=?\s+[A-Za-z0-9_]+\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var stagedOclPattern = new Regex(@"^\s*OCL\s*=?\s+(?:INITIAL|FINAL|MIDPOINT|PRIMARY|SECONDARY|TERTIARY)\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var structurePattern = new Regex(@"^\s*Structure\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var unitPattern = new Regex(@"^\s*Unit\s+([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        foreach (Match match in objectPattern.Matches(iniContent))
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

        foreach (Match match in oclPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                if (!token.StartsWith("OCL", StringComparison.OrdinalIgnoreCase))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "objectcreationlist.ini"));
            }
        }

        foreach (Match match in oclKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                if (!token.StartsWith("OCL", StringComparison.OrdinalIgnoreCase))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "objectcreationlist.ini"));
            }
        }

        foreach (Match match in oclAnyKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                if (!token.StartsWith("OCL", StringComparison.OrdinalIgnoreCase))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "objectcreationlist.ini"));
            }
        }

        foreach (Match match in oclTokenPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.Custom, "objectcreationlist.ini"));
            }
        }

        foreach (Match match in fxPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in fxPrefixPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in fxListKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in stagedFxPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in fxTokenPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in damageFxPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[2].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in soundPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[2].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.Audio, "soundeffects.ini"));
            }
        }

        foreach (Match match in musicPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.Audio, "music.ini"));
            }
        }

        foreach (Match match in imagePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[2].Value))
            {
                if (Path.HasExtension(token))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "mappedimages.ini"));
            }
        }

        foreach (Match match in imageLoosePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[3].Value))
            {
                if (Path.HasExtension(token))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "mappedimages.ini"));
            }
        }

        foreach (Match match in imageTokenPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.Custom, "mappedimages.ini"));
            }
        }

        foreach (Match match in objectNamePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.ObjectINI, "object.ini"));
            }
        }

        foreach (Match match in textureNamePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                if (Path.HasExtension(token))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "mappedimages.ini"));
            }
        }

        foreach (Match match in textureKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                if (Path.HasExtension(token))
                    continue;

                refs.Add(new IniObjectReference(token, DependencyType.Custom, "mappedimages.ini"));
            }
        }

        foreach (Match match in particleSystemPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in laserNameKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.FXList, "fxlist.ini"));
            }
        }

        foreach (Match match in particleSystemKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in particleSystemTokenPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in particleSysKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in particleSysBonePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in particleSysBoneKeyPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.VisualEffect, "particlesystem.ini"));
            }
        }

        foreach (Match match in stagedWeaponPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.Weapon, "weapon.ini"));
            }
        }

        foreach (Match match in stagedOclPattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.Custom, "objectcreationlist.ini"));
            }
        }

        foreach (Match match in structurePattern.Matches(iniContent))
        {
            foreach (var token in ExpandNames(match.Groups[1].Value))
            {
                refs.Add(new IniObjectReference(token, DependencyType.ObjectINI, "object.ini"));
            }
        }

        foreach (Match match in unitPattern.Matches(iniContent))
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
            "projectiledetonationocl" => (DependencyType.Custom, "objectcreationlist.ini"),
            "fireocl" => (DependencyType.Custom, "objectcreationlist.ini"),
            "ocl" => (DependencyType.Custom, "objectcreationlist.ini"),
            "creationlist" => (DependencyType.Custom, "objectcreationlist.ini"),
            "locomotor" => (DependencyType.Custom, "locomotor.ini"),
            "commandset" => (DependencyType.Custom, "commandset.ini"),
            "commandbutton" => (DependencyType.Custom, "commandbutton.ini"),
            "sound" => (DependencyType.Audio, "soundeffects.ini"),
            "voice" => (DependencyType.Audio, "voice.ini"),
            "enginesound" => (DependencyType.Audio, "soundeffects.ini"),
            "ambientsound" => (DependencyType.Audio, "soundeffects.ini"),
            "firesound" => (DependencyType.Audio, "soundeffects.ini"),
            "weaponsound" => (DependencyType.Audio, "soundeffects.ini"),
            "startsound" => (DependencyType.Audio, "soundeffects.ini"),
            "stopsound" => (DependencyType.Audio, "soundeffects.ini"),
            "loopsound" => (DependencyType.Audio, "soundeffects.ini"),
            "projectileexhaust" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "veterancyprojectileexhaust" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "projectilestream" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "projectilestreamname" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "projectiletrail" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "projectiletrailname" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "trailfx" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "contrailfx" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "smoketrailfx" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "particlesystem" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "particlesystemname" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "particlesystemid" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "slavesystem" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "objectnames" => (DependencyType.ObjectINI, "object.ini"),
            "objectname" => (DependencyType.ObjectINI, "object.ini"),
            "object" => (DependencyType.ObjectINI, "object.ini"),
            "crateobject" => (DependencyType.ObjectINI, "object.ini"),
            "unitname" => (DependencyType.ObjectINI, "object.ini"),
            "buildvariations" => (DependencyType.ObjectINI, "object.ini"),
            "damagefx" => (DependencyType.VisualEffect, "damagefx.ini"),
            "specialpower" => (DependencyType.Custom, "specialpower.ini"),
            "specialpowertemplate" => (DependencyType.Custom, "specialpower.ini"),
            "science" => (DependencyType.Custom, "science.ini"),
            "requiredscience" => (DependencyType.Custom, "science.ini"),
            "sciencerequired" => (DependencyType.Custom, "science.ini"),
            "killerscience" => (DependencyType.Custom, "science.ini"),
            "pickupscience" => (DependencyType.Custom, "science.ini"),
            "grantscience" => (DependencyType.Custom, "science.ini"),
            "upgrade" => (DependencyType.Custom, "upgrade.ini"),
            "requiredupgrade" => (DependencyType.Custom, "upgrade.ini"),
            "prerequisiteupgrade" => (DependencyType.Custom, "upgrade.ini"),
            "triggeredby" => (DependencyType.Custom, "upgrade.ini"),
            "upgradetogrant" => (DependencyType.Custom, "upgrade.ini"),
            "upgradetoremove" => (DependencyType.Custom, "upgrade.ini"),
            "upgraderequired" => (DependencyType.Custom, "upgrade.ini"),
            "needsupgrade" => (DependencyType.Custom, "upgrade.ini"),
            "removesupgrades" => (DependencyType.Custom, "upgrade.ini"),
            "upgradeobject" => (DependencyType.Custom, "objectcreationlist.ini"),
            "upgradeocl" => (DependencyType.Custom, "objectcreationlist.ini"),
            "cratedata" => (DependencyType.Custom, "crate.ini"),
            "stumpname" => (DependencyType.ObjectINI, "object.ini"),
            "crashfxtemplatename" => (DependencyType.ObjectINI, "object.ini"),
            "bridgeparticle" => (DependencyType.VisualEffect, "particlesystem.ini"),
            "attachparticle" => (DependencyType.VisualEffect, "particlesystem.ini"),
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
            var resolved = FindPriorityFile(sourceModPath, Path.Combine("INI", candidate));
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            resolved = FindPriorityFile(sourceModPath, Path.Combine("INI", "Default", candidate));
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            if (_archiveIndex.TryGetValue(candidate, out var archiveLocation))
                return $"{archiveLocation.ArchivePath}::{archiveLocation.EntryPath}";
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
        var pattern = new Regex(@"=\s*(Command_[A-Za-z0-9_]+)", RegexOptions.IgnoreCase);

        foreach (var line in blockLines)
        {
            var match = pattern.Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            refs.Add(new IniObjectReference(name, DependencyType.Custom, "commandbutton.ini"));
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

            var match = Regex.Match(trimmed, @"^Name\s*=\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase);
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
                refs.Add(new IniObjectReference(name, DependencyType.VisualEffect, "particlesystem.ini"));
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
        var filePattern = new Regex(@"([A-Za-z0-9_]*ModelName[A-Za-z0-9_]*|Model|ModelNames|Animation|IdleAnimation|[A-Za-z0-9_]*Texture[A-Za-z0-9_]*|Texture|TextureName|ParticleName|MoveHintName|W3D|DDS|Audio|Sound|Sounds|Music|Filename)\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
        var extensionPattern = new Regex(@"([A-Za-z0-9_\-./\\]+\.(w3d|dds|tga|wav|mp3|wma|ini|wnd|map|str|bik|htm|html))", RegexOptions.IgnoreCase);
        var winNamePattern = new Regex(@"^\s*WinName\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        var keyedMatches = filePattern.Matches(iniContent).Cast<Match>()
            .Select(m => (Key: m.Groups[1].Value, Value: m.Groups[2].Value))
            .ToList();

        var winNameMatches = winNamePattern.Matches(iniContent).Cast<Match>()
            .Select(m => (Key: "WinName", Value: ExtractWinNameFile(m.Groups[1].Value)))
            .Where(m => !string.IsNullOrWhiteSpace(m.Value))
            .ToList();

        var extensionMatches = extensionPattern.Matches(iniContent).Cast<Match>()
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

                var dependencyNode = new DependencyNode
                {
                    Name = Path.GetFileName(filePath),
                    Type = DetermineDependencyType(filePath),
                    FullPath = fullPath,
                    Status = isFound ? AssetStatus.Found : AssetStatus.Missing,
                    Depth = nextDepth
                };

                if (!graph.AllNodes.Any(n => n.Name == dependencyNode.Name))
                {
                    graph.AllNodes.Add(dependencyNode);
                    parentNode.Dependencies.Add(dependencyNode);

                    var extension = Path.GetExtension(filePath);
                    var shouldParseContent = dependencyNode.Type == DependencyType.ObjectINI
                        || extension.Equals(".wnd", StringComparison.OrdinalIgnoreCase);

                    if (dependencyNode.Status == AssetStatus.Found && shouldParseContent && nextDepth < 5)
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
            var iniFilePath = ResolveIniObjectFile(reference.IniFileName, sourceModPath, reference.ObjectName, reference.Type);
            var isFound = iniFilePath != null && (IsArchiveReference(iniFilePath) || File.Exists(iniFilePath));

            var iniNode = new DependencyNode
            {
                Name = reference.ObjectName,
                Type = reference.Type,
                FullPath = iniFilePath,
                Status = isFound ? AssetStatus.Found : AssetStatus.Missing,
                Depth = nextDepth
            };

            if (!graph.AllNodes.Any(n => n.Name == iniNode.Name && n.Type == iniNode.Type))
            {
                graph.AllNodes.Add(iniNode);
                parentNode.Dependencies.Add(iniNode);
            }

            if (isFound && ShouldParseDefinitionBlock(reference.Type))
            {
                await ParseDefinitionBlockAsync(iniNode, sourceModPath, graph);
            }
        }
    }

    private string? FindDefinitionIniPath(DependencyType type, string? iniFileName, string objectName, string sourceModPath)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        var keywords = GetBlockKeywords(type, iniFileName, null);
        if (keywords.Count == 0)
            return null;

        var cacheKey = $"{string.Join("|", keywords)}:{objectName}";
        if (_objectIniCache.TryGetValue(cacheKey, out var cachedPath))
            return cachedPath;

        var iniRoot = Path.Combine(sourceModPath, "Data", "INI");
        if (!Directory.Exists(iniRoot))
            return null;

        foreach (var file in Directory.GetFiles(iniRoot, "*.ini", SearchOption.AllDirectories))
        {
            try
            {
                var lines = File.ReadLines(file);
                if (keywords.Any(keyword => lines.Any(l => l.TrimStart().StartsWith($"{keyword} {objectName}", StringComparison.OrdinalIgnoreCase))))
                {
                    _objectIniCache[cacheKey] = file;
                    return file;
                }
            }
            catch
            {
                // تجاهل الملفات التي لا يمكن قراءتها
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

        // البحث المرن (في أي مكان في المجلد)
        try
        {
            var foundFiles = Directory.GetFiles(sourceModPath, Path.GetFileName(fileName), SearchOption.AllDirectories);
            return foundFiles.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> ReadIniContentAsync(string path)
    {
        if (!IsArchiveReference(path))
        {
            return await File.ReadAllTextAsync(path);
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
        try
        {
            var data = await manager.ExtractFileAsync(entryPath);
            return System.Text.Encoding.GetEncoding(1252).GetString(data);
        }
        catch
        {
            throw;
        }
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
        filePath = Regex.Replace(filePath, @"\{[A-Za-z_]+\}[/\\]?", "");
        
        // السماح فقط بأحرف آمنة
        filePath = Regex.Replace(filePath, @"[\\\/]+", "/");
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
    /// Deep recursive SAGE chain traversal.
    /// Follows: Object → Weapon → Projectile → FXList → ParticleSystem → Textures → Audio
    /// Uses the SageDefinitionIndex for fast lookup of any named SAGE block.
    /// </summary>
    private void DeepSageChainTraversal(DependencyNode parentNode, UnitDependencyGraph graph, string sourceModPath)
    {
        if (SageIndex == null) return;

        // Collect all [Custom] and unresolved reference names from existing nodes
        var unresolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.AllNodes.ToList())
        {
            // Extract reference names from node names (strip prefixes like "[Custom]")
            var name = node.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            unresolvedNames.Add(name);
        }

        // Also extract all references from the unit's INI content directly
        var unitDef = SageIndex.Lookup(graph.UnitName);
        if (unitDef != null)
        {
            var unitRefs = SageDefinitionIndex.ExtractReferences(unitDef);
            foreach (var r in unitRefs)
                unresolvedNames.Add(r.Name);
        }

        // Recursively resolve each reference
        foreach (var name in unresolvedNames.ToList())
        {
            RecursiveResolve(name, parentNode, graph, sourceModPath, 1, 8);
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
            node = new DependencyNode
            {
                Name = name,
                Type = MapBlockTypeToDependencyType(def.BlockType),
                FullPath = def.SourceFile,
                Status = AssetStatus.Found,
                Depth = depth
            };
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

    private void AddAudioCandidates(string baseName, DependencyNode parent, UnitDependencyGraph graph,
        string sourceModPath, int depth)
    {
        var candidates = new[] { baseName + ".wav", baseName + ".mp3" };
        foreach (var c in candidates)
        {
            var refObj = new SageReference(SageRefType.Audio, c, "AudioEvent");
            AddFileNode(refObj, parent, graph, sourceModPath, depth);
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

        var fileNode = new DependencyNode
        {
            Name = fileName,
            Type = depType,
            FullPath = fullPath,
            Status = status,
            Depth = depth
        };
        parent.Dependencies.Add(fileNode);
        graph.AllNodes.Add(fileNode);
    }

    private static DependencyType MapBlockTypeToDependencyType(string blockType)
    {
        return blockType.ToUpperInvariant() switch
        {
            "WEAPON" => DependencyType.Weapon,
            "PROJECTILE" => DependencyType.Projectile,
            "FXLIST" => DependencyType.VisualEffect,
            "OBJECTCREATIONLIST" => DependencyType.Custom,
            "PARTICLESYSTEM" => DependencyType.VisualEffect,
            "ARMOR" => DependencyType.Custom,
            "LOCOMOTOR" => DependencyType.Custom,
            "OBJECT" or "OBJECTRESKIN" => DependencyType.ObjectINI,
            "COMMANDBUTTON" => DependencyType.Custom,
            "COMMANDSET" => DependencyType.Custom,
            "AUDIOEVENT" => DependencyType.Audio,
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

    private sealed record ArchiveLocation(string ArchivePath, string EntryPath, long Size, bool IsHighPriority);
}
