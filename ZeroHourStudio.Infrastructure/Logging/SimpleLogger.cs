using System.Collections.Concurrent;
using System.Text;

namespace ZeroHourStudio.Infrastructure.Logging;

/// <summary>
/// نظام تسجيل الأخطاء والعمليات
/// </summary>
public interface ILogger
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? ex = null);
    void LogDebug(string message);
}

/// <summary>
/// تنفيذ بسيط للـ Logger مع دعم التسجيل في ملف
/// </summary>
public class SimpleLogger : ILogger
{
    private readonly ConcurrentBag<LogEntry> _logs;
    private readonly bool _consoleOutput;
    private readonly string? _filePath;
    private readonly object _fileLock = new();

    public SimpleLogger(bool consoleOutput = false)
    {
        _logs = new ConcurrentBag<LogEntry>();
        _consoleOutput = consoleOutput;
        _filePath = null;
    }

    /// <summary>
    /// إنشاء logger يكتب إلى ملف
    /// </summary>
    /// <param name="filePath">مسار ملف السجل (مثل: dependency_errors.log)</param>
    public SimpleLogger(string filePath)
    {
        _logs = new ConcurrentBag<LogEntry>();
        _consoleOutput = false;
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public void LogInfo(string message) => LogInternal(LogLevel.Info, message);
    public void LogWarning(string message) => LogInternal(LogLevel.Warning, message);
    public void LogError(string message, Exception? ex = null) => LogInternal(LogLevel.Error, ex != null ? $"{message} | {ex.Message}" : message);
    public void LogDebug(string message) => LogInternal(LogLevel.Debug, message);

    /// <summary>
    /// تسجيل رسالة بالمستوى المحدد
    /// </summary>
    public void Log(string message, LogLevel level = LogLevel.Info) => LogInternal(level, message);

    /// <summary>
    /// تسجيل منظّم بأعمدة: عملية | هدف | تفاصيل
    /// </summary>
    public void LogStructured(string operation, string target, string details = "")
        => LogInfo($"{operation,-25} | {target,-40} | {details}");

    private void LogInternal(LogLevel level, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message
        };

        _logs.Add(entry);

        if (_consoleOutput)
        {
            Console.WriteLine($"[{entry.Timestamp:HH:mm:ss}] [{level}] {message}");
        }

        if (!string.IsNullOrEmpty(_filePath))
        {
            try
            {
                lock (_fileLock)
                {
                    var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(_filePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // تجاهل أخطاء الكتابة في الملف
            }
        }
    }

    public IEnumerable<LogEntry> GetLogs() => _logs.OrderBy(x => x.Timestamp);
    public void ClearLogs() => _logs.Clear();
}

/// <summary>
/// تمثيل إدخال السجل
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// مستويات التسجيل
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}
