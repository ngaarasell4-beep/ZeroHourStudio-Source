using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZeroHourStudio.Infrastructure.Orchestration
{
    /// <summary>
    /// واجهة المنسق الشامل لعمليات النقل
    /// يربط بين التحليل، استخراج الفصائل، حقن الأوامر، ونقل الملفات
    /// </summary>
    public interface IUniversalTransferOrchestrator
    {
        /// <summary>
        /// تنفيذ عملية نقل شاملة (End-to-End)
        /// </summary>
        Task<TransferSessionResult> ExecuteTransferAsync(TransferRequest request, IProgress<TransferSessionProgress>? progress = null);
    }

    /// <summary>
    /// طلب نقل وحدة
    /// </summary>
    public class TransferRequest
    {
        public string SourcePath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string? TargetFaction { get; set; }
        public bool InjectCommandSet { get; set; } = true;
        public bool OverwriteFiles { get; set; } = false;
    }

    /// <summary>
    /// نتيجة جلسة النقل
    /// </summary>
    public class TransferSessionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public int FilesTransferred { get; set; }
        public int FilesFailed { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        
        // تفاصيل المراحل
        public bool AnalysisSuccess { get; set; }
        public bool TransferSuccess { get; set; }
        public bool InjectionSuccess { get; set; }
    }

    /// <summary>
    /// تقدم عملية النقل
    /// </summary>
    public class TransferSessionProgress
    {
        public string CurrentStage { get; set; } = string.Empty; // "Analyzing", "Transferring", "Injecting", "Finalizing"
        public string CurrentAction { get; set; } = string.Empty;
        public int OverallPercentage { get; set; }
    }
}
