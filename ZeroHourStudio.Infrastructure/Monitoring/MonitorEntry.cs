using System;

namespace ZeroHourStudio.Infrastructure.Monitoring
{
    /// <summary>
    /// سجل مراقبة واحد - يحتوي على تفاصيل كل عملية
    /// </summary>
    public class MonitorEntry
    {
        public DateTime Timestamp { get; set; }
        public TimeSpan Elapsed { get; set; }
        public string Operation { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        
        public override string ToString()
        {
            return $"[{Elapsed.TotalSeconds:F3}s] {Operation} | {Target} | {Result} | {Reason}";
        }
    }
}