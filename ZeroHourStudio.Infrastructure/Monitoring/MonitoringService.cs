using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ZeroHourStudio.Infrastructure.Monitoring
{
    /// <summary>
    /// نظام مراقبة شامل يسجل كل عملية من بداية البرنامج حتى نهايته
    /// CRITICAL: هذا النظام يعمل تلقائياً بدون تدخل المستخدم
    /// </summary>
    public class MonitoringService : IDisposable
    {
        private readonly List<MonitorEntry> _entries;
        private readonly Stopwatch _globalStopwatch;
        private readonly string _logFilePath;
        private readonly StreamWriter _logWriter;
        private readonly object _lock = new object();
        private static MonitoringService? _instance;

        
        public static MonitoringService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MonitoringService();
                }
                return _instance;
            }
        }

        public int EntryCount => _entries.Count;
        public IReadOnlyList<MonitorEntry> Entries => _entries.AsReadOnly();

        private MonitoringService()
        {
            _entries = new List<MonitorEntry>();
            _globalStopwatch = Stopwatch.StartNew();
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logFilePath = $"weapon_extraction_monitor_{timestamp}.log";
            
            _logWriter = new StreamWriter(_logFilePath, append: false);
            _logWriter.WriteLine("═════════════════════════════════════════════════════");
            _logWriter.WriteLine("ZERO HOUR WEAPON EXTRACTION - MONITORING LOG");
            _logWriter.WriteLine($"Started: {DateTime.Now}");
            _logWriter.WriteLine("═════════════════════════════════════════════════════");
            _logWriter.WriteLine();
            _logWriter.Flush();
        }

        /// <summary>
        /// تسجيل عملية - يتم استدعاؤها تلقائياً من جميع الخدمات
        /// </summary>
        public void Log(string operation, string target, string result, string reason, string details = "")
        {
            lock (_lock)
            {
                var entry = new MonitorEntry
                {
                    Timestamp = DateTime.Now,
                    Elapsed = _globalStopwatch.Elapsed,
                    Operation = operation,
                    Target = target,
                    Result = result,
                    Reason = reason,
                    Details = details
                };

                _entries.Add(entry);

                // كتابة فورية للسجل
                var logLine = entry.ToString();
                if (!string.IsNullOrWhiteSpace(details))
                {
                    logLine += $"\n    Details: {details}";
                }
                
                _logWriter.WriteLine(logLine);
                _logWriter.Flush();
            }
        }

        /// <summary>
        /// تحليل الأنماط - يكتشف أسباب الفشل المتكررة
        /// </summary>
        public FailurePatternAnalysis AnalyzeFailurePatterns()
        {
            var analysis = new FailurePatternAnalysis();
            
            var rejections = _entries.Where(e => e.Result.Contains("REJECT") || e.Result.Contains("SKIP") || e.Result.Contains("MISSING")).ToList();
            
            // تجميع حسب السبب
            var reasonGroups = rejections.GroupBy(e => e.Reason).OrderByDescending(g => g.Count()).ToList();
            
            analysis.TotalRejections = rejections.Count;
            analysis.TopRejectionReasons = reasonGroups.Take(10)
                .Select(g => (g.Key, g.Count()))
                .ToList();
            
            // اكتشاف الحلقات
            var loopDetection = _entries.Where(e => e.Reason.Contains("LOOP") || e.Reason.Contains("CIRCULAR")).ToList();
            analysis.LoopDetectionCount = loopDetection.Count;
            
            // اكتشاف الانفجارات
            var overflows = _entries.Where(e => e.Reason.Contains("OVERFLOW") || e.Reason.Contains("EXCEED")).ToList();
            analysis.OverflowCount = overflows.Count;
            
            return analysis;
        }

        /// <summary>
        /// إنشاء تقرير نهائي
        /// </summary>
        public void GenerateFinalReport(string filePath)
        {
            var analysis = AnalyzeFailurePatterns();
            
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine("═════════════════════════════════════════════════════");
                writer.WriteLine("WEAPON EXTRACTION - FINAL MONITORING REPORT");
                writer.WriteLine("═════════════════════════════════════════════════════");
                writer.WriteLine();
                writer.WriteLine($"Total Operations: {_entries.Count}");
                writer.WriteLine($"Total Duration: {_globalStopwatch.Elapsed:hh\\:mm\\:ss}");
                writer.WriteLine();
                writer.WriteLine("═════════════════════════════════════════════════════");
                writer.WriteLine("FAILURE PATTERN ANALYSIS");
                writer.WriteLine("═════════════════════════════════════════════════════");
                writer.WriteLine($"Total Rejections: {analysis.TotalRejections}");
                writer.WriteLine($"Loop Detections: {analysis.LoopDetectionCount}");
                writer.WriteLine($"Overflow Events: {analysis.OverflowCount}");
                writer.WriteLine();
                writer.WriteLine("Top Rejection Reasons:");
                foreach (var (reason, count) in analysis.TopRejectionReasons)
                {
                    writer.WriteLine($"  {count,5}x: {reason}");
                }
                writer.WriteLine();
                writer.WriteLine("═════════════════════════════════════════════════════");
            }
        }

        public void Dispose()
        {
            _globalStopwatch.Stop();
            
            _logWriter.WriteLine();
            _logWriter.WriteLine("═════════════════════════════════════════════════════");
            _logWriter.WriteLine($"MONITORING COMPLETE - {_entries.Count} entries");
            _logWriter.WriteLine($"Duration: {_globalStopwatch.Elapsed:hh\\:mm\\:ss}");
            _logWriter.WriteLine("═════════════════════════════════════════════════════");
            _logWriter.Dispose();
        }
    }

    /// <summary>
    /// نتيجة تحليل أنماط الفشل
    /// </summary>
    public class FailurePatternAnalysis
    {
        public int TotalRejections { get; set; }
        public int LoopDetectionCount { get; set; }
        public int OverflowCount { get; set; }
        public List<(string Reason, int Count)> TopRejectionReasons { get; set; } = new();
    }
}
