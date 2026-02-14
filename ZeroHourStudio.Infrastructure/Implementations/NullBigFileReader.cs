using ZeroHourStudio.Application.Interfaces;

namespace ZeroHourStudio.Infrastructure.Implementations;

/// <summary>
/// قارئ BIG فارغ يستخدم عندما لا تكون الأرشفة مطلوبة
/// </summary>
public class NullBigFileReader : IBigFileReader
{
    public Task<IEnumerable<string>> ReadAsync(string filePath)
    {
        return Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
    }

    public Task ExtractAsync(string filePath, string fileName, string outputPath)
    {
        throw new InvalidOperationException("قراءة ملفات BIG غير مهيأة في هذا السياق");
    }

    public Task<bool> FileExistsAsync(string filePath, string fileName)
    {
        return Task.FromResult(false);
    }
}
