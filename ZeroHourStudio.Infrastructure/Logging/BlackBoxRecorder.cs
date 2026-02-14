using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ZeroHourStudio.Infrastructure.Logging;

/// <summary>
/// Full Black-Box Runtime Recorder
/// Records EVERY event from application start to shutdown.
/// Thread-safe, non-blocking, with circular buffer for hang detection.
/// </summary>
public static class BlackBoxRecorder
{
    private static readonly object _fileLock = new();
    private static string _logPath = string.Empty;
    private static bool _initialized;
    private static long _eventCounter;
    private static readonly Stopwatch _appStopwatch = new();

    // Circular buffer: last 20 events for hang detection
    private static readonly string[] _recentEvents = new string[20];
    private static int _recentIndex;
    private static readonly object _recentLock = new();

    // Dependency counter (real-time)
    private static long _dependencyCounter;
    private static long _dependencyFoundCounter;
    private static long _dependencyMissingCounter;

    // File I/O counters
    private static long _fileOpenAttempts;
    private static long _fileOpenSuccess;
    private static long _fileOpenFailed;
    private static long _totalBytesRead;

    // INI parse counters
    private static long _iniBlocksParsed;
    private static long _iniValuesIgnored;

    // Icon counters
    private static long _iconSearchAttempts;
    private static long _iconFound;
    private static long _iconNotFound;

    // Hang detector
    private static Timer? _hangDetectorTimer;
    private static long _lastHeartbeat;
    private static string _lastFile = "(none)";
    private static string _lastLogicalLine = "(none)";
    private static int _activeOperations; // >0 means busy, 0 means idle (user waiting)

    public static long DependencyCount => Interlocked.Read(ref _dependencyCounter);
    public static long DependencyFoundCount => Interlocked.Read(ref _dependencyFoundCounter);
    public static long DependencyMissingCount => Interlocked.Read(ref _dependencyMissingCounter);

    /// <summary>
    /// Initialize the recorder. Must be called once at app startup.
    /// </summary>
    public static void Initialize(string logFilePath)
    {
        if (_initialized) return;
        _logPath = logFilePath;
        _initialized = true;
        _appStopwatch.Start();

        try
        {
            var header = new StringBuilder();
            header.AppendLine("╔══════════════════════════════════════════════════════════════════╗");
            header.AppendLine("║       WEAPON BLACK-BOX RUNTIME RECORDER                        ║");
            header.AppendLine("║       ZeroHour Studio V2 - Full Runtime Trace                  ║");
            header.AppendLine($"║       Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}                         ║");
            header.AppendLine($"║       Machine: {Environment.MachineName,-40}     ║");
            header.AppendLine($"║       OS: {Environment.OSVersion,-45}║");
            header.AppendLine($"║       CLR: {Environment.Version,-44}║");
            header.AppendLine($"║       Processors: {Environment.ProcessorCount,-37}║");
            header.AppendLine("╚══════════════════════════════════════════════════════════════════╝");
            header.AppendLine();
            header.AppendLine("FORMAT: [Elapsed] [EventID] [Category] [Detail]");
            header.AppendLine(new string('─', 80));
            header.AppendLine();

            lock (_fileLock)
            {
                File.WriteAllText(_logPath, header.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BlackBox] Log write failed: {ex.Message}"); }

        // Start hang detector: checks every 5 seconds
        _lastHeartbeat = _appStopwatch.ElapsedMilliseconds;
        _hangDetectorTimer = new Timer(HangDetectorCallback, null, 5000, 5000);

        Record("SYSTEM", "INIT", "Black-Box Recorder initialized");
    }

    /// <summary>
    /// Shutdown the recorder. Call at app exit.
    /// </summary>
    public static void Shutdown()
    {
        _hangDetectorTimer?.Dispose();

        Record("SYSTEM", "SHUTDOWN", "Application shutting down");

        // Write final summary
        var summary = new StringBuilder();
        summary.AppendLine();
        summary.AppendLine(new string('═', 80));
        summary.AppendLine("FINAL SUMMARY");
        summary.AppendLine(new string('─', 80));
        summary.AppendLine($"  Total Events Recorded:    {Interlocked.Read(ref _eventCounter)}");
        summary.AppendLine($"  Total Runtime:            {_appStopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
        summary.AppendLine($"  File Open Attempts:       {Interlocked.Read(ref _fileOpenAttempts)}");
        summary.AppendLine($"  File Open Success:        {Interlocked.Read(ref _fileOpenSuccess)}");
        summary.AppendLine($"  File Open Failed:         {Interlocked.Read(ref _fileOpenFailed)}");
        summary.AppendLine($"  Total Bytes Read:         {Interlocked.Read(ref _totalBytesRead):N0}");
        summary.AppendLine($"  INI Blocks Parsed:        {Interlocked.Read(ref _iniBlocksParsed)}");
        summary.AppendLine($"  INI Values Ignored:       {Interlocked.Read(ref _iniValuesIgnored)}");
        summary.AppendLine($"  Dependencies Total:       {Interlocked.Read(ref _dependencyCounter)}");
        summary.AppendLine($"  Dependencies Found:       {Interlocked.Read(ref _dependencyFoundCounter)}");
        summary.AppendLine($"  Dependencies Missing:     {Interlocked.Read(ref _dependencyMissingCounter)}");
        summary.AppendLine($"  Icon Search Attempts:     {Interlocked.Read(ref _iconSearchAttempts)}");
        summary.AppendLine($"  Icons Found:              {Interlocked.Read(ref _iconFound)}");
        summary.AppendLine($"  Icons Not Found:          {Interlocked.Read(ref _iconNotFound)}");
        summary.AppendLine(new string('═', 80));

        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(_logPath, summary.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BlackBox] Log write failed: {ex.Message}"); }

        _appStopwatch.Stop();
        _initialized = false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CORE RECORDING
    // ═══════════════════════════════════════════════════════════════

    public static void Record(string category, string action, string detail)
    {
        if (!_initialized) return;

        var eventId = Interlocked.Increment(ref _eventCounter);
        var elapsed = _appStopwatch.Elapsed;
        var threadId = Environment.CurrentManagedThreadId;
        var line = $"[{elapsed:hh\\:mm\\:ss\\.fff}] #{eventId:D6} T{threadId:D2} [{category}] {action}: {detail}";

        // Update circular buffer
        lock (_recentLock)
        {
            _recentEvents[_recentIndex % 20] = line;
            _recentIndex++;
        }

        // Update heartbeat
        Interlocked.Exchange(ref _lastHeartbeat, _appStopwatch.ElapsedMilliseconds);

        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BlackBox] Log write failed: {ex.Message}"); }
    }

    // ═══════════════════════════════════════════════════════════════
    //  1) USER INTERACTION (Mouse / UI)
    // ═══════════════════════════════════════════════════════════════

    public static void RecordMouseClick(string button, double x, double y, string elementName, string elementType)
    {
        Record("UI_CLICK", button,
            $"Element={elementName} Type={elementType} Coords=({x:F0},{y:F0})");
    }

    public static void RecordUserSelection(string selectionType, string value)
    {
        Record("UI_SELECT", selectionType, value);
    }

    public static void RecordDialogOpen(string dialogType, string title)
    {
        Record("UI_DIALOG", "OPEN", $"Type={dialogType} Title={title}");
    }

    public static void RecordDialogResult(string dialogType, string result, string value)
    {
        Record("UI_DIALOG", "RESULT", $"Type={dialogType} Result={result} Value={value}");
    }

    // ═══════════════════════════════════════════════════════════════
    //  2) TIMING & PERFORMANCE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a disposable scope that records start/end time of any operation.
    /// Usage: using var scope = BlackBoxRecorder.TimeScope("CATEGORY", "operation name");
    /// </summary>
    public static TimedScope TimeScope(string category, string operationName,
        [CallerFilePath] string file = "", [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
    {
        return new TimedScope(category, operationName, file, member, line);
    }

    public static void RecordSlowOperation(string category, string operation, long elapsedMs)
    {
        if (elapsedMs > 50)
        {
            Record("SLOW_OP", category, $"{operation} took {elapsedMs}ms (>50ms threshold)");
        }
    }

    /// <summary>Call when a long operation starts (load/analyze/transfer). Enables hang detection.</summary>
    public static void BeginOperation(string name)
    {
        Interlocked.Increment(ref _activeOperations);
        Record("OPERATION", "BEGIN", name);
    }

    /// <summary>Call when a long operation ends. Disables hang detection when idle.</summary>
    public static void EndOperation(string name)
    {
        Interlocked.Decrement(ref _activeOperations);
        Record("OPERATION", "END", name);
    }

    // ═══════════════════════════════════════════════════════════════
    //  3) FILE I/O
    // ═══════════════════════════════════════════════════════════════

    public static void RecordFileOpen(string path, bool success, long sizeBytes = 0, string failReason = "")
    {
        Interlocked.Increment(ref _fileOpenAttempts);
        if (success)
        {
            Interlocked.Increment(ref _fileOpenSuccess);
            Record("FILE_IO", "OPEN_OK", $"Path={path} Size={sizeBytes:N0}B");
        }
        else
        {
            Interlocked.Increment(ref _fileOpenFailed);
            Record("FILE_IO", "OPEN_FAIL", $"Path={path} Reason={failReason}");
        }

        Interlocked.Exchange(ref _lastFile, path);
    }

    public static void RecordFileRead(string path, long bytesRead, bool complete)
    {
        Interlocked.Add(ref _totalBytesRead, bytesRead);
        Record("FILE_IO", "READ", $"Path={path} Bytes={bytesRead:N0} Complete={complete}");
    }

    public static void RecordArchiveExtract(string archivePath, string entryPath, bool success, long sizeBytes = 0, string failReason = "")
    {
        if (success)
            Record("FILE_IO", "ARCHIVE_EXTRACT_OK", $"Archive={Path.GetFileName(archivePath)} Entry={entryPath} Size={sizeBytes:N0}B");
        else
            Record("FILE_IO", "ARCHIVE_EXTRACT_FAIL", $"Archive={Path.GetFileName(archivePath)} Entry={entryPath} Reason={failReason}");
    }

    // ═══════════════════════════════════════════════════════════════
    //  4) INI PARSING
    // ═══════════════════════════════════════════════════════════════

    public static void RecordIniBlock(string fileName, string blockName, string blockType, string parsedAs)
    {
        Interlocked.Increment(ref _iniBlocksParsed);
        Record("INI_PARSE", "BLOCK", $"File={Path.GetFileName(fileName)} Block={blockName} Type={blockType} ParsedAs={parsedAs}");
    }

    public static void RecordIniValue(string fileName, string blockName, string key, string value, bool understood)
    {
        if (!understood)
        {
            Interlocked.Increment(ref _iniValuesIgnored);
            Record("INI_PARSE", "IGNORED_VALUE", $"File={Path.GetFileName(fileName)} Block={blockName} Key={key} Value={value}");
        }
    }

    public static void RecordIniParseStart(string fileName, int lineCount)
    {
        Record("INI_PARSE", "START", $"File={Path.GetFileName(fileName)} Lines={lineCount}");
    }

    public static void RecordIniParseEnd(string fileName, int blocksFound, long elapsedMs)
    {
        Record("INI_PARSE", "END", $"File={Path.GetFileName(fileName)} Blocks={blocksFound} Elapsed={elapsedMs}ms");
    }

    // ═══════════════════════════════════════════════════════════════
    //  5) DEPENDENCIES
    // ═══════════════════════════════════════════════════════════════

    public static void RecordDependencyCreated(string name, string type, string createdBy, string reason)
    {
        var count = Interlocked.Increment(ref _dependencyCounter);
        Record("DEPENDENCY", "CREATED",
            $"#{count} Name={name} Type={type} CreatedBy={createdBy} Reason={reason}");
    }

    public static void RecordDependencyFound(string name, string path)
    {
        Interlocked.Increment(ref _dependencyFoundCounter);
        Record("DEPENDENCY", "FOUND", $"Name={name} Path={path}");
    }

    public static void RecordDependencyMissing(string name, string searchedPaths)
    {
        Interlocked.Increment(ref _dependencyMissingCounter);
        Record("DEPENDENCY", "MISSING", $"Name={name} Searched={searchedPaths}");
    }

    public static void RecordDependencyDuplicate(string name, string type)
    {
        Record("DEPENDENCY", "DUPLICATE", $"Name={name} Type={type} (skipped)");
    }

    public static void RecordDependencyResolveStart(string unitName, string iniPath)
    {
        // Reset counters for this unit
        Interlocked.Exchange(ref _dependencyCounter, 0);
        Interlocked.Exchange(ref _dependencyFoundCounter, 0);
        Interlocked.Exchange(ref _dependencyMissingCounter, 0);
        Record("DEPENDENCY", "RESOLVE_START", $"Unit={unitName} INI={iniPath}");
    }

    public static void RecordDependencyResolveEnd(string unitName, int totalNodes, int found, int missing, long elapsedMs)
    {
        Record("DEPENDENCY", "RESOLVE_END",
            $"Unit={unitName} Total={totalNodes} Found={found} Missing={missing} Elapsed={elapsedMs}ms");
    }

    // ═══════════════════════════════════════════════════════════════
    //  6) ICONS
    // ═══════════════════════════════════════════════════════════════

    public static void RecordIconSearch(string iconName, string[] searchedPaths, bool found, string failReason = "")
    {
        Interlocked.Increment(ref _iconSearchAttempts);
        if (found)
        {
            Interlocked.Increment(ref _iconFound);
            Record("ICON", "FOUND", $"Name={iconName}");
        }
        else
        {
            Interlocked.Increment(ref _iconNotFound);
            var paths = string.Join(" | ", searchedPaths);
            Record("ICON", "NOT_FOUND", $"Name={iconName} Reason={failReason} Searched=[{paths}]");
        }
    }

    public static void RecordIconLoad(string textureName, bool success, string source, string failReason = "")
    {
        if (success)
            Record("ICON", "TEXTURE_LOADED", $"Texture={textureName} Source={source}");
        else
            Record("ICON", "TEXTURE_FAIL", $"Texture={textureName} Source={source} Reason={failReason}");
    }

    // ═══════════════════════════════════════════════════════════════
    //  7) EXTRACTION / TRANSFER
    // ═══════════════════════════════════════════════════════════════

    public static void RecordTransferStart(string unitName, string source, string destination, int totalFiles)
    {
        Record("TRANSFER", "START", $"Unit={unitName} Source={source} Dest={destination} Files={totalFiles}");
    }

    public static void RecordTransferFile(string fileName, bool success, long sizeBytes, string failReason = "")
    {
        if (success)
            Record("TRANSFER", "FILE_OK", $"File={fileName} Size={sizeBytes:N0}B");
        else
            Record("TRANSFER", "FILE_FAIL", $"File={fileName} Reason={failReason}");
    }

    public static void RecordTransferEnd(string unitName, bool success, int transferred, int failed, long elapsedMs, string message = "")
    {
        Record("TRANSFER", "END",
            $"Unit={unitName} Success={success} Transferred={transferred} Failed={failed} Elapsed={elapsedMs}ms Msg={message}");
    }

    public static void RecordTransferRollback(string reason, int filesRolledBack)
    {
        Record("TRANSFER", "ROLLBACK", $"Reason={reason} FilesRolledBack={filesRolledBack}");
    }

    // ═══════════════════════════════════════════════════════════════
    //  8) ERRORS & HANG DETECTION
    // ═══════════════════════════════════════════════════════════════

    public static void RecordError(string category, string message, Exception? ex = null)
    {
        var detail = ex != null
            ? $"{message} | Exception={ex.GetType().Name}: {ex.Message} | Stack={ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"
            : message;
        Record("ERROR", category, detail);
    }

    public static void RecordWarning(string category, string message)
    {
        Record("WARNING", category, message);
    }

    public static void SetLastLogicalLine(string description)
    {
        Interlocked.Exchange(ref _lastLogicalLine, description);
    }

    private static void HangDetectorCallback(object? state)
    {
        var now = _appStopwatch.ElapsedMilliseconds;
        var lastBeat = Interlocked.Read(ref _lastHeartbeat);
        var gap = now - lastBeat;

        // Only detect hangs during active operations (not idle user waiting)
        if (gap > 10000 && Interlocked.CompareExchange(ref _activeOperations, 0, 0) > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("╔══════════════════════════════════════════════════════════════════╗");
            sb.AppendLine($"║  ⚠ POTENTIAL HANG DETECTED - No events for {gap / 1000}s              ║");
            sb.AppendLine("╠══════════════════════════════════════════════════════════════════╣");
            sb.AppendLine($"║  Last File:    {_lastFile}");
            sb.AppendLine($"║  Last Logic:   {_lastLogicalLine}");
            sb.AppendLine("║  Last 20 Events:");

            lock (_recentLock)
            {
                for (int i = 0; i < 20; i++)
                {
                    var idx = (_recentIndex - 20 + i + 200) % 20;
                    var evt = _recentEvents[idx];
                    if (evt != null)
                        sb.AppendLine($"║    {evt}");
                }
            }

            sb.AppendLine("╚══════════════════════════════════════════════════════════════════╝");

            try
            {
                lock (_fileLock)
                {
                    File.AppendAllText(_logPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BlackBox] Log write failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Dump the last 20 events (useful for crash reporting).
    /// </summary>
    public static string DumpRecentEvents()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== LAST 20 EVENTS ===");
        lock (_recentLock)
        {
            for (int i = 0; i < 20; i++)
            {
                var idx = (_recentIndex - 20 + i + 200) % 20;
                var evt = _recentEvents[idx];
                if (evt != null) sb.AppendLine(evt);
            }
        }
        sb.AppendLine($"Last File: {_lastFile}");
        sb.AppendLine($"Last Logic: {_lastLogicalLine}");
        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    //  TimedScope - auto records start/end/duration
    // ═══════════════════════════════════════════════════════════════

    public sealed class TimedScope : IDisposable
    {
        private readonly string _category;
        private readonly string _operation;
        private readonly string _callerFile;
        private readonly string _callerMember;
        private readonly int _callerLine;
        private readonly Stopwatch _sw;

        internal TimedScope(string category, string operation, string callerFile, string callerMember, int callerLine)
        {
            _category = category;
            _operation = operation;
            _callerFile = Path.GetFileName(callerFile);
            _callerMember = callerMember;
            _callerLine = callerLine;
            _sw = Stopwatch.StartNew();

            Record("TIMING", $"{_category}_START",
                $"{_operation} [{_callerFile}:{_callerMember}:{_callerLine}]");
            SetLastLogicalLine($"{_category}.{_operation} at {_callerFile}:{_callerLine}");
        }

        public void Dispose()
        {
            _sw.Stop();
            var ms = _sw.ElapsedMilliseconds;
            Record("TIMING", $"{_category}_END",
                $"{_operation} Elapsed={ms}ms [{_callerFile}:{_callerMember}:{_callerLine}]");

            if (ms > 50)
            {
                RecordSlowOperation(_category, _operation, ms);
            }
        }
    }
}
