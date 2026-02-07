using System.Collections.Concurrent;

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
/// تنفيذ بسيط للـ Logger
/// </summary>
public class SimpleLogger : ILogger
{
    private readonly ConcurrentBag<LogEntry> _logs;
    private readonly bool _consoleOutput;

    public SimpleLogger(bool consoleOutput = false)
    {
        _logs = new ConcurrentBag<LogEntry>();
        _consoleOutput = consoleOutput;
    }

    public void LogInfo(string message) => Log(LogLevel.Info, message);
    public void LogWarning(string message) => Log(LogLevel.Warning, message);
    public void LogError(string message, Exception? ex = null) => Log(LogLevel.Error, $"{message} {ex?.Message}");
    public void LogDebug(string message) => Log(LogLevel.Debug, message);

    private void Log(LogLevel level, string message)
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
