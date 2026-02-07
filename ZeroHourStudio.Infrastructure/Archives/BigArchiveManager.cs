using System.IO.MemoryMappedFiles;
using System.Text;

namespace ZeroHourStudio.Infrastructure.Archives;

/// <summary>
/// مدير قراءة ملفات الأرشيف BIG بأداء عالي
/// يدعم نظام الأولوية (Mounting Priority) للملفات التي تبدأ بـ !!
/// </summary>
public class BigArchiveManager : IDisposable
{
    private const uint BIG_FILE_SIGNATURE = 0x12FD0000; // توقيع ملف BIG
    private const string HIGH_PRIORITY_PREFIX = "!!";

    private readonly string _archivePath;
    private MemoryMappedFile? _mmf;
    private FileStream? _fileStream;
    private readonly Dictionary<string, ArchiveEntry> _fileIndex;
    private bool _disposed;

    public BigArchiveManager(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentNullException(nameof(archivePath));

        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"ملف الأرشيف غير موجود: {archivePath}", archivePath);

        _archivePath = archivePath;
        _fileIndex = new Dictionary<string, ArchiveEntry>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// تحميل الأرشيف وفهرسة محتوياته
    /// </summary>
    public async Task LoadAsync()
    {
        ThrowIfDisposed();

        try
        {
            _fileStream = new FileStream(_archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _mmf = MemoryMappedFile.CreateFromFile(_fileStream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);

            await Task.Run(() => IndexArchiveContents());
        }
        catch (Exception ex)
        {
            Dispose();
            throw new InvalidOperationException($"فشل تحميل الأرشيف: {_archivePath}", ex);
        }
    }

    /// <summary>
    /// فهرسة محتويات الأرشيف
    /// </summary>
    private void IndexArchiveContents()
    {
        using var accessor = _mmf!.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        // قراءة التوقيع
        uint signature = accessor.ReadUInt32(0);
        if (signature != BIG_FILE_SIGNATURE)
            throw new InvalidOperationException("توقيع ملف BIG غير صحيح");

        // قراءة عدد الملفات
        uint fileCount = accessor.ReadUInt32(4);
        uint dataSize = accessor.ReadUInt32(8);

        long position = 12;

        for (uint i = 0; i < fileCount; i++)
        {
            var entry = ReadFileEntry(accessor, position);
            if (entry != null)
            {
                // نظام الأولوية: الملفات التي تبدأ بـ !! لها الأولوية
                string key = entry.FileName;
                if (!_fileIndex.ContainsKey(key) || entry.FileName.StartsWith(HIGH_PRIORITY_PREFIX))
                {
                    _fileIndex[key] = entry;
                }
            }

            position += 12 + (entry?.FileName.Length ?? 0) + 1;
        }
    }

    /// <summary>
    /// قراءة إدخال ملف واحد من الأرشيف
    /// </summary>
    private static ArchiveEntry? ReadFileEntry(MemoryMappedViewAccessor accessor, long position)
    {
        try
        {
            uint offset = accessor.ReadUInt32(position);
            uint size = accessor.ReadUInt32(position + 4);
            uint timestamp = accessor.ReadUInt32(position + 8);

            // قراءة اسم الملف (null-terminated string)
            var nameBuilder = new StringBuilder();
            long namePos = position + 12;
            byte charByte;

            while ((charByte = accessor.ReadByte(namePos)) != 0)
            {
                nameBuilder.Append((char)charByte);
                namePos++;
            }

            return new ArchiveEntry
            {
                FileName = nameBuilder.ToString(),
                Offset = offset,
                Size = size,
                Timestamp = timestamp
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// استخراج ملف من الأرشيف
    /// </summary>
    public async Task<byte[]> ExtractFileAsync(string fileName)
    {
        ThrowIfDisposed();

        if (!_fileIndex.TryGetValue(fileName, out var entry))
            throw new FileNotFoundException($"الملف غير موجود في الأرشيف: {fileName}");

        return await Task.Run(() =>
        {
            using var accessor = _mmf!.CreateViewAccessor(entry.Offset, (long)entry.Size, MemoryMappedFileAccess.Read);
            var buffer = new byte[entry.Size];
            accessor.ReadArray(0, buffer, 0, buffer.Length);
            return buffer;
        });
    }

    /// <summary>
    /// التحقق من وجود ملف في الأرشيف
    /// </summary>
    public bool FileExists(string fileName)
    {
        ThrowIfDisposed();
        return _fileIndex.ContainsKey(fileName);
    }

    /// <summary>
    /// الحصول على قائمة الملفات في الأرشيف
    /// </summary>
    public IEnumerable<string> GetFileList()
    {
        ThrowIfDisposed();
        return _fileIndex.Keys.ToList();
    }

    /// <summary>
    /// الحصول على معلومات الملف
    /// </summary>
    public ArchiveEntry? GetFileInfo(string fileName)
    {
        ThrowIfDisposed();
        return _fileIndex.TryGetValue(fileName, out var entry) ? entry : null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BigArchiveManager));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _mmf?.Dispose();
        _fileStream?.Dispose();
        _fileIndex.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~BigArchiveManager() => Dispose();
}

/// <summary>
/// يمثل إدخال ملف واحد في الأرشيف
/// </summary>
public class ArchiveEntry
{
    public string FileName { get; set; } = string.Empty;
    public uint Offset { get; set; }
    public uint Size { get; set; }
    public uint Timestamp { get; set; }
}
