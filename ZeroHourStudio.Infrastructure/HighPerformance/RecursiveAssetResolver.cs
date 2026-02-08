using System.Collections.Concurrent;
using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Infrastructure.HighPerformance;

/// <summary>
/// äÙÇã Íá ÇáÊÈÚíÇÊ ÇáÚäŞæÏí (Recursive Asset Resolver)
/// - íÊÊÈÚ ÇáÊÈÚíÇÊ ÈÔßá ÚäŞæÏí (Recursive) ÍÊì ÂÎÑ ÊÈÚíÉ
/// - íÏÚã OCLs (Object Classes) æ Projectiles
/// - íãäÚ ÇáÍáŞÇÊ ÇáÏÇÆÑíÉ (Circular Dependencies)
/// - íÓÊÎÏã ãÚÇáÌÉ ãÊæÇÒíÉ (Parallel Processing) ááÃÏÇÁ ÇáÚÇáí
/// </summary>
public class RecursiveAssetResolver : IDisposable
{
    private readonly HighPerformanceExtractionEngine _extractionEngine;
    private readonly UTF16ArabicBinarySearchCache _searchCache;
    private readonly ConcurrentDictionary<string, DependencyNode> _resolvedAssets;
    private readonly ConcurrentDictionary<string, AssetResolutionStatus> _resolutionStatus;
    private readonly HashSet<string> _currentlyResolving; // áÊÊÈÚ ÇáÊÈÚíÇÊ ŞíÏ ÇáãÚÇáÌÉ
    private readonly object _lockObject = new();
    private bool _disposed;

    // ËÇÈÊÉ ÇáÍÏ ÇáÃŞÕì áÚãŞ ÇáÊÈÚíÇÊ
    private const int MAX_RECURSION_DEPTH = 100;
    private const int MAX_PARALLEL_TASKS = 8;

    public RecursiveAssetResolver(
        HighPerformanceExtractionEngine extractionEngine,
        UTF16ArabicBinarySearchCache searchCache)
    {
        _extractionEngine = extractionEngine ?? throw new ArgumentNullException(nameof(extractionEngine));
        _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        _resolvedAssets = new ConcurrentDictionary<string, DependencyNode>(StringComparer.OrdinalIgnoreCase);
        _resolutionStatus = new ConcurrentDictionary<string, AssetResolutionStatus>(StringComparer.OrdinalIgnoreCase);
        _currentlyResolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Íá ÌãíÚ ÇáÊÈÚíÇÊ áÃÕá ãÚíä ÈÔßá ÚäŞæÏí
    /// </summary>
    public async Task<DependencyNode> ResolveAssetRecursivelyAsync(
        string assetPath,
        string baseDirectory,
        int depth = 0)
    {
        ThrowIfDisposed();

        // ÇáÊÍŞŞ ãä ÇáÍÏ ÇáÃŞÕì ááÚãŞ
        if (depth > MAX_RECURSION_DEPTH)
        {
            return CreateNodeWithError(assetPath, $"ÊÌÇæÒ ÇáÍÏ ÇáÃŞÕì ááÚãŞ ({MAX_RECURSION_DEPTH})");
        }

        // ÇáÊÍŞŞ ãä ÇáÊÎÒíä ÇáãÄŞÊ
        if (_resolvedAssets.TryGetValue(assetPath, out var cachedNode))
        {
            return cachedNode;
        }

        // ãäÚ ÇáÍáŞÇÊ ÇáÏÇÆÑíÉ
        lock (_lockObject)
        {
            if (_currentlyResolving.Contains(assetPath))
            {
                return CreateNodeWithError(assetPath, "ÍáŞÉ ÏÇÆÑíÉ ãßÊÔİÉ");
            }

            _currentlyResolving.Add(assetPath);
        }

        try
        {
            var node = new DependencyNode
            {
                Name = Path.GetFileName(assetPath),
                FullPath = assetPath,
                Depth = depth,
                Type = DetermineDependencyType(assetPath),
                Status = AssetStatus.Found
            };

            // ÇÓÊÎÑÇÌ ÇáÊÈÚíÇÊ ÇáãÈÇÔÑÉ
            var directDependencies = await ExtractDirectDependenciesAsync(assetPath, baseDirectory);

            // Íá ÇáÊÈÚíÇÊ ÈÔßá ãÊæÇÒí ááÃÏÇÁ ÇáÚÇáí
            if (directDependencies.Count > 0)
            {
                var resolveTasksAsync = directDependencies
                    .Take(MAX_PARALLEL_TASKS)
                    .Select(dep => ResolveAssetRecursivelyAsync(dep, baseDirectory, depth + 1))
                    .ToList();

                var resolvedDeps = await Task.WhenAll(resolveTasksAsync);
                node.Dependencies.AddRange(resolvedDeps);

                // ãÚÇáÌÉ ÇáÊÈÚíÇÊ ÇáÅÖÇİíÉ ÈÔßá ãÊÓáÓá ÅĞÇ ßÇäÊ ÃßËÑ ãä MAX_PARALLEL_TASKS
                if (directDependencies.Count > MAX_PARALLEL_TASKS)
                {
                    for (int i = MAX_PARALLEL_TASKS; i < directDependencies.Count; i++)
                    {
                        var depNode = await ResolveAssetRecursivelyAsync(
                            directDependencies[i],
                            baseDirectory,
                            depth + 1);
                        node.Dependencies.Add(depNode);
                    }
                }
            }

            // ÊÍÏíË ÍÇáÉ ÇáÃÕá
            node.Status = node.Dependencies.Count == 0 || 
                         node.Dependencies.All(d => d.Status == AssetStatus.Found)
                ? AssetStatus.Found
                : AssetStatus.NotVerified;

            // ÍİÙ İí ÇáßÇÔ
            _resolvedAssets.TryAdd(assetPath, node);
            _resolutionStatus.TryAdd(assetPath, new AssetResolutionStatus
            {
                AssetPath = assetPath,
                Status = node.Status,
                ResolutionTime = DateTime.UtcNow,
                DependencyCount = node.Dependencies.Count
            });

            return node;
        }
        finally
        {
            lock (_lockObject)
            {
                _currentlyResolving.Remove(assetPath);
            }
        }
    }

    /// <summary>
    /// ÇÓÊÎÑÇÌ ÇáÊÈÚíÇÊ ÇáãÈÇÔÑÉ áÃÕá ãÚíä
    /// íÊã ÇáÈÍË Úä:
    /// - OCLs (Object Classes)
    /// - Weapons (ÇáÃÓáÍÉ)
    /// - Projectiles (ÇáŞĞÇÆİ)
    /// - Models (ÇáãæÏíáÇÊ)
    /// - Textures (ÇáäÓíÌ)
    /// - Audio (ÇáÕæÊ)
    /// </summary>
    private async Task<List<string>> ExtractDirectDependenciesAsync(
        string assetPath,
        string baseDirectory)
    {
        var dependencies = new List<string>();

        try
        {
            // ŞÑÇÁÉ ãÍÊæì ÇáÃÕá
            string content = await _extractionEngine.ReadTextEfficientlyAsync(assetPath);

            // ÇáÈÍË Úä OCLs
            ExtractOCLReferences(content, dependencies, baseDirectory);

            // ÇáÈÍË Úä ÇáÃÓáÍÉ
            ExtractWeaponReferences(content, dependencies, baseDirectory);

            // ÇáÈÍË Úä ÇáŞĞÇÆİ
            ExtractProjectileReferences(content, dependencies, baseDirectory);

            // ÇáÈÍË Úä ÇáãæÏíáÇÊ æÇáäÓíÌ æÇáÕæÊ
            ExtractAssetReferences(content, dependencies, baseDirectory);

            return dependencies;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ÎØÃ İí ÇÓÊÎÑÇÌ ÇáÊÈÚíÇÊ ãä {assetPath}: {ex.Message}");
            return dependencies;
        }
    }

    /// <summary>
    /// ÇÓÊÎÑÇÌ ãÑÇÌÚ OCL (Object Class)
    /// ãËá: DefaultBehavior = OCLName
    /// </summary>
    private void ExtractOCLReferences(string content, List<string> dependencies, string baseDirectory)
    {
        // ÇáÈÍË Úä ÇáäãØ: DefaultBehavior = SomeOCL
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            if (line.Contains("DefaultBehavior", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Behavior", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('=');
                if (parts.Length == 2)
                {
                    string oclName = parts[1].Trim();
                    string oclPath = Path.Combine(baseDirectory, $"{oclName}.ini");

                    if (File.Exists(oclPath) && !dependencies.Contains(oclPath))
                    {
                        dependencies.Add(oclPath);
                    }
                }
            }
        }
    }

    /// <summary>
    /// ÇÓÊÎÑÇÌ ãÑÇÌÚ ÇáÃÓáÍÉ
    /// ãËá: Weapon = M14Rifle
    /// </summary>
    private void ExtractWeaponReferences(string content, List<string> dependencies, string baseDirectory)
    {
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            if (line.Contains("Weapon", StringComparison.OrdinalIgnoreCase) && !line.StartsWith(";"))
            {
                var parts = line.Split('=');
                if (parts.Length == 2)
                {
                    string weaponName = parts[1].Trim();
                    string weaponPath = Path.Combine(baseDirectory, $"{weaponName}.ini");

                    if (File.Exists(weaponPath) && !dependencies.Contains(weaponPath))
                    {
                        dependencies.Add(weaponPath);
                    }
                }
            }
        }
    }

    /// <summary>
    /// ÇÓÊÎÑÇÌ ãÑÇÌÚ ÇáŞĞÇÆİ
    /// ãËá: Projectile = BulletProjectile
    /// </summary>
    private void ExtractProjectileReferences(string content, List<string> dependencies, string baseDirectory)
    {
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            if (line.Contains("Projectile", StringComparison.OrdinalIgnoreCase) && !line.StartsWith(";"))
            {
                var parts = line.Split('=');
                if (parts.Length == 2)
                {
                    string projectileName = parts[1].Trim();
                    string projectilePath = Path.Combine(baseDirectory, $"{projectileName}.ini");

                    if (File.Exists(projectilePath) && !dependencies.Contains(projectilePath))
                    {
                        dependencies.Add(projectilePath);
                    }
                }
            }
        }
    }

    /// <summary>
    /// ÇÓÊÎÑÇÌ ãÑÇÌÚ ÇáãæÏíáÇÊ æÇáäÓíÌ æÇáÕæÊ
    /// </summary>
    private void ExtractAssetReferences(string content, List<string> dependencies, string baseDirectory)
    {
        // ÇáÈÍË Úä ÃäãÇØ W3D æ TGA æ WAV
        var patterns = new[] { ".w3d", ".tga", ".jpg", ".dds", ".wav", ".mp3" };

        foreach (var pattern in patterns)
        {
            int index = 0;
            while ((index = content.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                // ÇáÈÍË Úä ÈÏÇíÉ ÇÓã Çáãáİ
                int startIndex = index;
                while (startIndex > 0 && !char.IsWhiteSpace(content[startIndex - 1]) && content[startIndex - 1] != '=')
                {
                    startIndex--;
                }

                string fileName = content.Substring(startIndex, index - startIndex + pattern.Length).Trim();

                // ÅÒÇáÉ ÇáÃÍÑİ ÇáÅÖÇİíÉ
                fileName = fileName.Trim('\"', '\'', ' ', '\t');

                string assetPath = Path.Combine(baseDirectory, fileName);
                if (File.Exists(assetPath) && !dependencies.Contains(assetPath))
                {
                    dependencies.Add(assetPath);
                }

                index = index + pattern.Length;
            }
        }
    }

    /// <summary>
    /// ÊÍÏíÏ äæÚ ÇáÊÈÚíÉ ÈäÇÁğ Úáì ÇãÊÏÇÏ Çáãáİ
    /// </summary>
    private DependencyType DetermineDependencyType(string assetPath)
    {
        var extension = Path.GetExtension(assetPath).ToLowerInvariant();

        return extension switch
        {
            ".ini" => DependencyType.ObjectINI,
            ".w3d" => DependencyType.Model3D,
            ".tga" or ".jpg" or ".dds" => DependencyType.Texture,
            ".wav" or ".mp3" => DependencyType.Audio,
            _ => DependencyType.ObjectINI
        };
    }

    /// <summary>
    /// ÅäÔÇÁ ÚŞÏÉ ÈÍÇáÉ ÎØÃ
    /// </summary>
    private DependencyNode CreateNodeWithError(string assetPath, string errorMessage)
    {
        return new DependencyNode
        {
            Name = Path.GetFileName(assetPath),
            FullPath = assetPath,
            Status = AssetStatus.Invalid,
            Type = DependencyType.ObjectINI
        };
    }

    /// <summary>
    /// ÇáÍÕæá Úáì ÔÌÑÉ ÇáÊÈÚíÇÊ ßÇãáÉ (Dependency Tree)
    /// </summary>
    public DependencyTreeReport GenerateDependencyTreeReport(DependencyNode rootNode)
    {
        ThrowIfDisposed();

        var report = new DependencyTreeReport
        {
            RootAsset = rootNode.Name,
            GeneratedAt = DateTime.UtcNow,
            TotalNodes = CountNodes(rootNode),
            MaxDepth = GetMaxDepth(rootNode)
        };

        TraverseTree(rootNode, report.AllNodes);

        return report;
    }

    /// <summary>
    /// ÚÏ ÚÏÏ ÇáÚŞÏ İí ÇáÔÌÑÉ
    /// </summary>
    private int CountNodes(DependencyNode node)
    {
        int count = 1;

        foreach (var dep in node.Dependencies)
        {
            count += CountNodes(dep);
        }

        return count;
    }

    /// <summary>
    /// ÇáÍÕæá Úáì ÃŞÕì ÚãŞ İí ÇáÔÌÑÉ
    /// </summary>
    private int GetMaxDepth(DependencyNode node)
    {
        if (node.Dependencies.Count == 0)
            return node.Depth;

        int maxChildDepth = 0;

        foreach (var dep in node.Dependencies)
        {
            int childDepth = GetMaxDepth(dep);
            if (childDepth > maxChildDepth)
                maxChildDepth = childDepth;
        }

        return maxChildDepth;
    }

    /// <summary>
    /// ÚÈæÑ ÇáÔÌÑÉ æÌãÚ ÌãíÚ ÇáÚŞÏ
    /// </summary>
    private void TraverseTree(DependencyNode node, List<DependencyNode> allNodes)
    {
        allNodes.Add(node);

        foreach (var dep in node.Dependencies)
        {
            TraverseTree(dep, allNodes);
        }
    }

    /// <summary>
    /// ãÓÍ ÇáßÇÔ
    /// </summary>
    public void ClearCache()
    {
        ThrowIfDisposed();

        _resolvedAssets.Clear();
        _resolutionStatus.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _resolvedAssets.Clear();
        _resolutionStatus.Clear();
        _currentlyResolving.Clear();

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RecursiveAssetResolver));
    }
}

/// <summary>
/// ÍÇáÉ ÏæÑÉ Íá ÇáÃÕá
/// </summary>
public class AssetResolutionStatus
{
    public string AssetPath { get; set; } = string.Empty;
    public AssetStatus Status { get; set; }
    public DateTime ResolutionTime { get; set; }
    public int DependencyCount { get; set; }
}

/// <summary>
/// ÊŞÑíÑ ÔÌÑÉ ÇáÊÈÚíÇÊ
/// </summary>
public class DependencyTreeReport
{
    public string RootAsset { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public int TotalNodes { get; set; }
    public int MaxDepth { get; set; }
    public List<DependencyNode> AllNodes { get; set; } = new();

    public override string ToString()
    {
        return $"ÊŞÑíÑ ÇáÊÈÚíÇÊ: {RootAsset} ({TotalNodes} ÚŞÏÉ¡ ÚãŞ ÃŞÕì: {MaxDepth})";
    }
}
