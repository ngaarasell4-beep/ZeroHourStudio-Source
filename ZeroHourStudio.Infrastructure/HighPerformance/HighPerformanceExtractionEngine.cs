using System.Buffers;
using System.IO.MemoryMappedFiles;
using System.Text;
using ZeroHourStudio.Application.Interfaces;

namespace ZeroHourStudio.Infrastructure.HighPerformance;

/// <summary>
/// „Õ—ﬂ «” Œ—«Ã ⁄«·Ì «·√œ«¡ (High-Performance Extraction Engine)
/// - Ì” Œœ„ Span<T> Ê Memory<T> ··ﬁ—«¡… »œÊ‰ ‰”Œ (Zero-Copy)
/// - Ìœ⁄„ „⁄«·Ã… «·„·›«  «·÷Œ„… (BIG Ê INI) »ﬂ›«¡…
/// - Ì” Œœ„ Memory-Mapped Files ··√œ«¡ «·√„À·
/// </summary>
public class HighPerformanceExtractionEngine : IDisposable
{
    private readonly Dictionary<string, MemoryPool<byte>> _memoryPoolCache;
    private readonly Dictionary<string, MemoryMappedFile?> _mmfCache;
    private bool _disposed;

    public HighPerformanceExtractionEngine()
    {
        _memoryPoolCache = new(StringComparer.OrdinalIgnoreCase);
        _mmfCache = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// «” Œ—«Ã „Õ ÊÏ „·› INI »ﬂ›«¡… ⁄«·Ì… »«” Œœ«„ Span<T>
    /// </summary>
    public async Task<Dictionary<string, Dictionary<string, string>>> ExtractIniContentAsync(string filePath)
    {
        ThrowIfDisposed();

        return await Task.Run(() => ExtractIniContentInternal(filePath));
    }

    private Dictionary<string, Dictionary<string, string>> ExtractIniContentInternal(string filePath)
    {
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var mmf = MemoryMappedFile.CreateFromFile(fileStream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);

        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        long fileSize = fileStream.Length;

        // ﬁ—«¡… «·„Õ ÊÏ »«·ﬂ«„· ›Ì »«›— „Õ·Ì
        byte[] buffer = ArrayPool<byte>.Shared.Rent((int)fileSize);
        try
        {
            int bytesRead = 0;
            for (int i = 0; i < fileSize; i++)
            {
                buffer[i] = accessor.ReadByte(i);
                bytesRead++;
            }

            //  ÕÊÌ· «·»«Ì «  ≈·Ï ‰’ UTF-8 »ﬂ›«¡…
            Span<byte> contentSpan = buffer.AsSpan(0, bytesRead);
            string content = Encoding.UTF8.GetString(contentSpan);

            //  Õ·Ì· «·„Õ ÊÏ
            ParseIniContent(content, result);

            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///  Õ·Ì· „Õ ÊÏ INI »«” Œœ«„ Span<char> ··√œ«¡ «·⁄«·Ì
    /// </summary>
    private void ParseIniContent(string content, Dictionary<string, Dictionary<string, string>> result)
    {
        ReadOnlySpan<char> contentSpan = content.AsSpan();
        string? currentSection = null;
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            ReadOnlySpan<char> trimmedLine = line.AsSpan().Trim();

            //  Ã«Â· «·√”ÿ— «·›«—€… Ê«· ⁄·Ìﬁ« 
            if (trimmedLine.IsEmpty || trimmedLine.StartsWith(";") || trimmedLine.StartsWith("//"))
                continue;

            // „⁄«·Ã… «·√ﬁ”«„ [Section]
            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                currentSection = trimmedLine.Slice(1, trimmedLine.Length - 2).ToString();
                if (!result.ContainsKey(currentSection))
                {
                    result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                continue;
            }

            // „⁄«·Ã… Key = Value
            if (currentSection != null)
            {
                int equalsIndex = trimmedLine.IndexOf('=');
                if (equalsIndex > 0)
                {
                    string key = trimmedLine.Slice(0, equalsIndex).Trim().ToString();
                    string value = trimmedLine.Slice(equalsIndex + 1).Trim().ToString();

                    result[currentSection][key] = value;
                }
            }
        }
    }

    /// <summary>
    /// «” Œ—«Ã ﬁ”„ „⁄Ì‰ „‰ „·› BIG »«” Œœ«„ Memory<T>
    /// </summary>
    public async Task<Memory<byte>> ExtractBigSectionAsync(string filePath, long offset, int length)
    {
        ThrowIfDisposed();

        return await Task.Run(() =>
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var mmf = MemoryMappedFile.CreateFromFile(fileStream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);

            byte[] buffer = new byte[length];
            using var accessor = mmf.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);

            for (int i = 0; i < length; i++)
            {
                buffer[i] = accessor.ReadByte(i);
            }

            return new Memory<byte>(buffer);
        });
    }

    /// <summary>
    /// «·»ÕÀ ⁄‰ ‰„ÿ „⁄Ì‰ ›Ì „·› ﬂ»Ì— »«” Œœ«„ Span<byte>
    /// </summary>
    public async Task<List<long>> FindPatternInBigFileAsync(string filePath, byte[] pattern, int bufferSize = 65536)
    {
        ThrowIfDisposed();

        return await Task.Run(() => FindPatternInternal(filePath, pattern, bufferSize));
    }

    private List<long> FindPatternInternal(string filePath, byte[] pattern, int bufferSize)
    {
        var matches = new List<long>();

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize + pattern.Length);

        try
        {
            long position = 0;
            int bytesRead = 0;
            int overlapSize = Math.Max(pattern.Length - 1, 0);

            while ((bytesRead = fileStream.Read(buffer, overlapSize, bufferSize)) > 0)
            {
                Span<byte> searchSpan = buffer.AsSpan(0, overlapSize + bytesRead);

                // «·»ÕÀ ⁄‰ «·‰„ÿ ›Ì Â–« «·Ã“¡
                int index = 0;
                while ((index = searchSpan.IndexOf(pattern)) >= 0)
                {
                    matches.Add(position + index);
                    searchSpan = searchSpan.Slice(index + 1);
                    position += index + 1;
                }

                // ‰”Œ «·Ã“¡ «·√ŒÌ— ··»«›— «· «·Ì
                if (bytesRead < bufferSize)
                    break;

                Array.Copy(buffer, bufferSize, buffer, 0, overlapSize);
                position += bufferSize;
            }

            return matches;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// ﬁ—«¡… «·‰’ »ﬂ›«¡… „‰ „·› „⁄ „⁄«·Ã… «· —„Ì“ (UTF-8, UTF-16)
    /// </summary>
    public async Task<string> ReadTextEfficientlyAsync(string filePath, Encoding? encoding = null)
    {
        ThrowIfDisposed();

        return await Task.Run(() =>
        {
            encoding ??= Encoding.UTF8;

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var mmf = MemoryMappedFile.CreateFromFile(fileStream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);

            long fileSize = fileStream.Length;
            byte[] buffer = ArrayPool<byte>.Shared.Rent((int)fileSize);

            try
            {
                using var accessor = mmf.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.Read);

                for (long i = 0; i < fileSize; i++)
                {
                    buffer[i] = accessor.ReadByte(i);
                }

                return encoding.GetString(buffer, 0, (int)fileSize);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        });
    }

    /// <summary>
    /// «” Œ—«Ã ﬂ«∆‰ INI ﬂ«„· »ﬂ›«¡… ⁄«·Ì…
    /// </summary>
    public async Task<string?> ExtractCompleteObjectAsync(string filePath, string objectName)
    {
        ThrowIfDisposed();

        return await Task.Run(() =>
        {
            var content = File.ReadAllText(filePath, Encoding.UTF8);
            return ExtractObjectInternal(content, objectName);
        });
    }

    private string? ExtractObjectInternal(string content, string objectName)
    {
        ReadOnlySpan<char> contentSpan = content.AsSpan();
        string searchPattern = $"Object {objectName}";

        int startIndex = content.IndexOf(searchPattern, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
            return null;

        // «·»ÕÀ ⁄‰ »œ«Ì… «·ﬂ«∆‰
        int braceStart = content.IndexOf('{', startIndex);
        if (braceStart < 0)
            return null;

        int braceCount = 1;
        int braceEnd = braceStart + 1;

        // «·»ÕÀ ⁄‰ ≈€·«ﬁ «·√ﬁÊ«”
        while (braceEnd < content.Length && braceCount > 0)
        {
            if (content[braceEnd] == '{')
                braceCount++;
            else if (content[braceEnd] == '}')
                braceCount--;

            braceEnd++;
        }

        return content.Substring(startIndex, braceEnd - startIndex);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var mmf in _mmfCache.Values)
        {
            mmf?.Dispose();
        }

        _mmfCache.Clear();

        foreach (var pool in _memoryPoolCache.Values)
        {
            pool?.Dispose();
        }

        _memoryPoolCache.Clear();

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HighPerformanceExtractionEngine));
    }
}
