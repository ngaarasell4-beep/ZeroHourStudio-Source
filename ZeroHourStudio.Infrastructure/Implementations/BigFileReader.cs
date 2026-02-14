using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Archives;

namespace ZeroHourStudio.Infrastructure.Implementations;

/// <summary>
/// تنفيذ IBigFileReader باستخدام BigArchiveManager
/// </summary>
public class BigFileReader : IBigFileReader
{
    private BigArchiveManager? _archiveManager;
    private readonly string _archivePath;

    public BigFileReader(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentNullException(nameof(archivePath));

        _archivePath = archivePath;
    }

    /// <summary>
    /// فتح وقراءة محتويات أرشيف BIG
    /// </summary>
    public async Task<IEnumerable<string>> ReadAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        using var manager = new BigArchiveManager(filePath);
        await manager.LoadAsync();
        return manager.GetFileList();
    }

    /// <summary>
    /// استخراج ملف معين من الأرشيف
    /// </summary>
    public async Task ExtractAsync(string filePath, string fileName, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentNullException(nameof(fileName));

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentNullException(nameof(outputPath));

        using var manager = new BigArchiveManager(filePath);
        await manager.LoadAsync();

        var fileData = await manager.ExtractFileAsync(fileName);

        // التأكد من وجود المجلد
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(outputPath, fileData);
    }

    /// <summary>
    /// التحقق من وجود ملف في الأرشيف
    /// </summary>
    public async Task<bool> FileExistsAsync(string filePath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentNullException(nameof(fileName));

        using var manager = new BigArchiveManager(filePath);
        await manager.LoadAsync();
        return manager.FileExists(fileName);
    }

    /// <summary>
    /// تحميل الأرشيف الحالي
    /// </summary>
    public async Task InitializeAsync()
    {
        _archiveManager = new BigArchiveManager(_archivePath);
        await _archiveManager.LoadAsync();
    }

    /// <summary>
    /// الحصول على معلومات ملف محدد
    /// </summary>
    public ArchiveEntry? GetFileInfo(string fileName)
    {
        if (_archiveManager == null)
            throw new InvalidOperationException("يجب استدعاء InitializeAsync أولاً");

        return _archiveManager.GetFileInfo(fileName);
    }

    public void Dispose()
    {
        _archiveManager?.Dispose();
    }
}

// Extension method لـ BigArchiveManager.Load (بدون Async)
internal static class BigArchiveManagerExtensions
{
    public static void Load(this BigArchiveManager manager)
    {
        manager.LoadAsync().GetAwaiter().GetResult();
    }
}
