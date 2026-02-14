using System.IO.MemoryMappedFiles;
using System.Text;

namespace ZeroHourStudio.Infrastructure.Archives;

/// <summary>
/// مدير قراءة ملفات الأرشيف BIG بأداء عالي
/// يدعم نظام الأولوية (Mounting Priority) للملفات التي تبدأ بـ !!
/// </summary>
public class BigArchiveManager : IDisposable
{
    private const uint BIGF_SIGNATURE = 0x46474942; // "BIGF" as LE uint32
    private const uint BIG4_SIGNATURE = 0x34474942; // "BIG4" as LE uint32
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

        // قراءة التوقيع - دعم BIGF و BIG4
        uint signature = accessor.ReadUInt32(0);
        if (signature != BIGF_SIGNATURE && signature != BIG4_SIGNATURE)
            throw new InvalidOperationException($"توقيع ملف BIG غير صحيح: 0x{signature:X8} (المتوقع BIGF أو BIG4)");

        // حقول العدد والحجم مخزنة بترتيب Big-Endian في صيغة BIG
        // Bytes 4-7: حجم الأرشيف (LE)
        // Bytes 8-11: عدد الملفات (BE)
        // Bytes 12-15: حجم الفهرس (BE)
        uint fileCount = ReadBigEndianUInt32(accessor, 8);
        uint indexSize = ReadBigEndianUInt32(accessor, 12);

        long position = 16; // بعد الـ Header (16 بايت)

        for (uint i = 0; i < fileCount; i++)
        {
            var (entry, bytesConsumed) = ReadFileEntry(accessor, position);
            if (entry != null)
            {
                string key = entry.FileName;
                if (!_fileIndex.ContainsKey(key) || entry.FileName.StartsWith(HIGH_PRIORITY_PREFIX, StringComparison.OrdinalIgnoreCase))
                {
                    _fileIndex[key] = entry;
                }
            }

            if (bytesConsumed <= 0)
                break;
            position += bytesConsumed;
        }
    }

    /// <summary>
    /// قراءة قيمة uint32 بترتيب Big-Endian
    /// </summary>
    private static uint ReadBigEndianUInt32(MemoryMappedViewAccessor accessor, long position)
    {
        byte b0 = accessor.ReadByte(position);
        byte b1 = accessor.ReadByte(position + 1);
        byte b2 = accessor.ReadByte(position + 2);
        byte b3 = accessor.ReadByte(position + 3);
        return (uint)((b0 << 24) | (b1 << 16) | (b2 << 8) | b3);
    }

    /// <summary>
    /// قراءة إدخال ملف واحد من الأرشيف
    /// </summary>
    /// <returns>(الإدخال، عدد البايتات المُستهلكة) - عند الفشل يُرجع (null, 0) للإيقاف</returns>
    private static (ArchiveEntry? Entry, int BytesConsumed) ReadFileEntry(MemoryMappedViewAccessor accessor, long position)
    {
        try
        {
            // صيغة الإدخال في BIG: Offset (4 BE) + Size (4 BE) + Filename (null-terminated)
            uint offset = ReadBigEndianUInt32(accessor, position);
            uint size = ReadBigEndianUInt32(accessor, position + 4);

            var nameBuilder = new StringBuilder();
            long namePos = position + 8; // بعد offset (4) + size (4) = 8 بايت
            byte charByte;
            int nameLen = 0;
            const int maxNameLen = 512;

            while (nameLen < maxNameLen && (charByte = accessor.ReadByte(namePos)) != 0)
            {
                nameBuilder.Append((char)charByte);
                namePos++;
                nameLen++;
            }

            var bytesConsumed = 8 + nameLen + 1; // 8 بايت للحقول + طول الاسم + null terminator
            return (new ArchiveEntry
            {
                FileName = nameBuilder.ToString(),
                Offset = offset,
                Size = size,
                Timestamp = 0
            }, bytesConsumed);
        }
        catch
        {
            return (null, 0);
        }
    }

    /// <summary>
    /// استخراج ملف من الأرشيف
    /// </summary>
    public async Task<byte[]> ExtractFileAsync(string fileName)
    {
        ThrowIfDisposed();

        var entry = FindEntry(fileName);
        if (entry == null)
            throw new FileNotFoundException($"الملف غير موجود في الأرشيف: {fileName}");

        return await Task.Run(() =>
        {
            using var accessor = _mmf!.CreateViewAccessor(entry.Offset, (long)entry.Size, MemoryMappedFileAccess.Read);
            var buffer = new byte[entry.Size];
            accessor.ReadArray(0, buffer, 0, buffer.Length);
            return buffer;
        });
    }

    private ArchiveEntry? FindEntry(string fileName)
    {
        // Try exact match
        if (_fileIndex.TryGetValue(fileName, out var entry))
            return entry;
        // Normalize slashes and retry
        var normalized = fileName.Replace('\\', '/');
        if (_fileIndex.TryGetValue(normalized, out entry))
            return entry;
        // Try filename-only fallback
        var nameOnly = Path.GetFileName(fileName);
        if (_fileIndex.TryGetValue(nameOnly, out entry))
            return entry;
        return null;
    }

    /// <summary>
    /// التحقق من وجود ملف في الأرشيف
    /// </summary>
    public bool FileExists(string fileName)
    {
        ThrowIfDisposed();
        return FindEntry(fileName) != null;
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
        return FindEntry(fileName);
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
        _fileIndex?.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~BigArchiveManager()
    {
        try { Dispose(); } catch { }
    }
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
