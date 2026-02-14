using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Infrastructure.Transfer;

/// <summary>
/// واجهة لخدمة النقل الذكي
/// تقوم بنقل الوحدات وتبعياتها بعد حلها بذكاء
/// </summary>
public interface ISmartTransferService
{
    /// <summary>
    /// نقل وحدة وجميع تبعياتها بذكاء
    /// </summary>
    /// <param name="graph">رسم بياني للتبعيات</param>
    /// <param name="sourcePath">مسار المصدر</param>
    /// <param name="destinationPath">مسار الهدف</param>
    /// <param name="progress">callback لتحديث التقدم</param>
    Task<SmartTransferResult> TransferAsync(
        UnitDependencyGraph graph,
        string sourcePath,
        string destinationPath,
        IProgress<TransferProgress>? progress = null);
}

/// <summary>
/// نتيجة عملية النقل الذكي
/// </summary>
public class SmartTransferResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TransferredFilesCount { get; set; }
    public long TransferredBytesCount { get; set; }
    public int RecoveredFilesCount { get; set; }
    public List<string> FailedFiles { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// تحديث تقدم عملية النقل
/// </summary>
public class TransferProgress
{
    public int CurrentFileIndex { get; set; }
    public int TotalFiles { get; set; }
    public string CurrentFileName { get; set; } = string.Empty;
    public long TransferredBytes { get; set; }
    public long TotalBytes { get; set; }
    public double PercentageComplete => TotalBytes > 0 ? (TransferredBytes * 100.0) / TotalBytes : 0;
}
