using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Archives; // Left for DependencyType only if needed, otherwise clean up

namespace ZeroHourStudio.Infrastructure.AssetManagement;

/// <summary>
/// صائد المراجع الخارجية - يبحث عن الملفات الأساسية
/// يتولى البحث عن: .w3d (نماذج), .dds/.tga (نسيج), .wav/.mp3 (صوت)
/// </summary>
public class AssetReferenceHunter
{
    private readonly IBigFileReader? _archiveReader;
    private readonly string? _gameAssetsPath;

    /// <summary>
    /// الامتدادات التي يتم البحث عنها
    /// </summary>
    private static readonly Dictionary<string, DependencyType> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 3D Models
        { ".w3d", DependencyType.Model3D },

        // Textures
        { ".dds", DependencyType.Texture },
        { ".tga", DependencyType.Texture },

        // Audio
        { ".wav", DependencyType.Audio },
        { ".mp3", DependencyType.Audio },

        // Visual Effects
        { ".w3x", DependencyType.VisualEffect },
    };

    public AssetReferenceHunter(IBigFileReader? archiveReader = null, string? gameAssetsPath = null)
    {
        _archiveReader = archiveReader;
        _gameAssetsPath = gameAssetsPath;
    }

    /// <summary>
    /// البحث عن ملفات الأصول بناءً على الاسم المرجعي
    /// </summary>
    public async Task<List<DependencyNode>> FindAssetsAsync(string assetReference, string? preferredType = null)
    {
        if (string.IsNullOrWhiteSpace(assetReference))
            throw new ArgumentNullException(nameof(assetReference));

        var foundAssets = new List<DependencyNode>();

        // محاولة البحث عن الملفات بامتدادات مختلفة
        foreach (var extension in SupportedExtensions.Keys)
        {
            var fileName = $"{assetReference}{extension}";
            var node = await FindAssetAsync(fileName);

            if (node != null)
            {
                foundAssets.Add(node);
            }
        }

        return foundAssets;
    }

    /// <summary>
    /// البحث عن ملف محدد من الأصول
    /// </summary>
    public async Task<DependencyNode?> FindAssetAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var extension = Path.GetExtension(fileName);

        if (!SupportedExtensions.TryGetValue(extension, out var assetType))
            return null;

        var node = new DependencyNode
        {
            Name = fileName,
            Type = assetType,
            Status = AssetStatus.NotVerified
        };

        // البحث في الأرشيف أولاً
        if (_archiveReader != null)
        {
            bool existsInArchive = await _archiveReader.FileExistsAsync("", fileName);
            if (existsInArchive)
            {
                node.Status = AssetStatus.Found;
                node.LastModified = DateTime.UtcNow;
                // SizeInBytes not available in IBigFileReader directly without extraction or extension
                return node;
            }
        }

        // البحث في نظام الملفات
        if (!string.IsNullOrEmpty(_gameAssetsPath))
        {
            var fullPath = Path.Combine(_gameAssetsPath, fileName);
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                node.Status = AssetStatus.Found;
                node.FullPath = fullPath;
                node.SizeInBytes = fileInfo.Length;
                node.LastModified = fileInfo.LastWriteTimeUtc;
                return node;
            }
        }

        // إذا لم نجد الملف
        node.Status = AssetStatus.Missing;
        return node;
    }

    /// <summary>
    /// البحث عن جميع الملفات حسب نوع محدد
    /// </summary>
    public async Task<List<DependencyNode>> FindAssetsByTypeAsync(DependencyType assetType)
    {
        var extensionsToSearch = SupportedExtensions
            .Where(x => x.Value == assetType)
            .Select(x => x.Key)
            .ToList();

        var results = new List<DependencyNode>();

        if (_archiveReader != null)
        {
            var archiveFiles = await _archiveReader.ReadAsync("");
            var matchingFiles = archiveFiles
                .Where(f => extensionsToSearch.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var file in matchingFiles)
            {
                var node = new DependencyNode
                {
                    Name = file,
                    Type = assetType,
                    Status = AssetStatus.Found
                };

                results.Add(node);
            }
        }

        return results;
    }

    /// <summary>
    /// التحقق من وجود مورد معين في الفهرس
    /// </summary>
    public async Task<bool> IsAssetIndexedAsync(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName) || _archiveReader == null)
            return false;

        return await _archiveReader.FileExistsAsync("", assetName);
    }

    /// <summary>
    /// الحصول على إحصائيات الأصول
    /// </summary>
    public async Task<AssetStatistics> GetAssetStatisticsAsync()
    {
        var stats = new AssetStatistics();

        if (_archiveReader == null)
            return stats;

        var files = await _archiveReader.ReadAsync("");

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();

            if (extension == ".w3d")
                stats.Model3DCount++;
            else if (extension == ".dds" || extension == ".tga")
                stats.TextureCount++;
            else if (extension == ".wav" || extension == ".mp3")
                stats.AudioCount++;
            else if (extension == ".w3x")
                stats.VisualEffectCount++;

            // Size calculation removed as IBigFileReader does not verify size efficiently
        }

        return stats;
    }
}

/// <summary>
/// إحصائيات الأصول في الأرشيف
/// </summary>
public class AssetStatistics
{
    public int Model3DCount { get; set; } = 0;
    public int TextureCount { get; set; } = 0;
    public int AudioCount { get; set; } = 0;
    public int VisualEffectCount { get; set; } = 0;
    public long TotalSizeInBytes { get; set; } = 0;

    public int TotalAssetCount => Model3DCount + TextureCount + AudioCount + VisualEffectCount;

    public double GetTotalSizeInMB() => TotalSizeInBytes / (1024.0 * 1024.0);

    public override string ToString() =>
        $"Assets: {Model3DCount} models, {TextureCount} textures, {AudioCount} audio, {VisualEffectCount} effects - Total Count: {TotalAssetCount}";
}
