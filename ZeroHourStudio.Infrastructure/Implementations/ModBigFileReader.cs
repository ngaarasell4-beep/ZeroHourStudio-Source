using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Archives;

namespace ZeroHourStudio.Infrastructure.Implementations;

/// <summary>
/// قارئ BIG متعدد للأرشيفات ضمن مسار مود محدد
/// </summary>
public class ModBigFileReader : IBigFileReader, IDisposable
{
    private const string HighPriorityPrefix = "!!";
    private string _rootPath;
    private readonly Dictionary<string, ArchiveLocation> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BigArchiveManager> _managers = new(StringComparer.OrdinalIgnoreCase);
    private bool _indexed;

    public ModBigFileReader(string rootPath)
    {
        _rootPath = rootPath ?? string.Empty;
    }

    public void SetRootPath(string rootPath)
    {
        _rootPath = rootPath ?? string.Empty;
        _indexed = false;
        _index.Clear();
    }

    public async Task<IEnumerable<string>> ReadAsync(string filePath)
    {
        await EnsureIndexAsync();
        return _index.Keys.ToList();
    }

    public async Task ExtractAsync(string filePath, string fileName, string outputPath)
    {
        await EnsureIndexAsync();

        if (!_index.TryGetValue(fileName, out var location))
        {
            // Try filename-only lookup
            var justName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(justName) || !_index.TryGetValue(justName, out location))
                throw new FileNotFoundException($"الملف غير موجود في الأرشيف: {fileName}");
        }

        if (!_managers.TryGetValue(location.ArchivePath, out var manager))
        {
            manager = new BigArchiveManager(location.ArchivePath);
            await manager.LoadAsync();
            _managers[location.ArchivePath] = manager;
        }

        var data = await manager.ExtractFileAsync(location.EntryPath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(outputPath, data);
    }

    public async Task<bool> FileExistsAsync(string filePath, string fileName)
    {
        await EnsureIndexAsync();
        if (_index.ContainsKey(fileName))
            return true;
        // Also try with just the filename portion (strip path)
        var justName = Path.GetFileName(fileName);
        return !string.IsNullOrEmpty(justName) && _index.ContainsKey(justName);
    }

    private async Task EnsureIndexAsync()
    {
        if (_indexed)
            return;

        _index.Clear();

        if (string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath))
        {
            _indexed = true;
            return;
        }

        // Sort archives: normal ones first, !! prefixed ones last (so they override)
        var archives = Directory.GetFiles(_rootPath, "*.big", SearchOption.AllDirectories)
            .OrderBy(a => Path.GetFileName(a).StartsWith(HighPriorityPrefix, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(a => Path.GetFileName(a), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var archive in archives)
        {
            try
            {
                using var manager = new BigArchiveManager(archive);
                await manager.LoadAsync();

                // Archive-level priority: if the BIG filename starts with !!, all entries inside get high priority
                var archiveFileName = Path.GetFileName(archive);
                var isArchiveHighPriority = archiveFileName.StartsWith(HighPriorityPrefix, StringComparison.OrdinalIgnoreCase);

                foreach (var entry in manager.GetFileList())
                {
                    var fileName = Path.GetFileName(entry);
                    var isHighPriority = isArchiveHighPriority
                        || fileName.StartsWith(HighPriorityPrefix, StringComparison.OrdinalIgnoreCase);

                    if (_index.TryGetValue(fileName, out var existing))
                    {
                        if (!existing.IsHighPriority && isHighPriority)
                        {
                            _index[fileName] = new ArchiveLocation(archive, entry, isHighPriority);
                        }
                        continue;
                    }

                    _index[fileName] = new ArchiveLocation(archive, entry, isHighPriority);
                }
            }
            catch (Exception ex)
            {
                // Skip unreadable archives gracefully, but log them
                System.Diagnostics.Debug.WriteLine($"[ModBigFileReader] Error indexing archive {archive}: {ex.Message}");
            }
        }

        _indexed = true;
    }

    /// <summary>
    /// عدد الملفات المفهرسة في أرشيفات BIG (للتحقق من نجاح الفهرسة)
    /// </summary>
    public int GetFileCount()
    {
        return _index.Count;
    }

    public void Dispose()
    {
        foreach (var manager in _managers.Values)
        {
            try { manager.Dispose(); } catch { /* ignore */ }
        }
        _managers.Clear();
        _index.Clear();
        _indexed = false;
    }

    private sealed record ArchiveLocation(string ArchivePath, string EntryPath, bool IsHighPriority);
}
