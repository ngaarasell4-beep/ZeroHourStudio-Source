using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ZeroHourStudio.Infrastructure.Logging;
using ZeroHourStudio.Infrastructure.Monitoring;

namespace ZeroHourStudio.UI.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public static readonly string DiagLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "ZeroHourStudio_diag.log");

        /// <summary>
        /// تهيئة التطبيق
        /// </summary>
        public App()
        {
            // تسجيل CodePages لدعم Windows-1252 encoding المطلوب لملفات SAGE INI
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            InitializeComponent();
        }

        public static void DiagLog(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(DiagLogPath, line);
            }
            catch { }
        }

        /// <summary>
        /// Startup Handler
        /// </summary>
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                File.WriteAllText(DiagLogPath, $"=== ZeroHour Studio V2 Diagnostic Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
                DiagLog("Application started");

                var bbLogPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "weapon_blackbox_runtime.log");
                BlackBoxRecorder.Initialize(bbLogPath);
            }
            catch { }
        }

        /// <summary>
        /// Exit Handler
        /// </summary>
        private void Application_Exit(object sender, ExitEventArgs e)
        {
            DiagLog("Application exiting");
            
            // إنشاء تقرير المراقبة النهائي
            try
            {
                var monitor = MonitoringService.Instance;
                var reportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "weapon_extraction_final_report.txt");
                monitor.GenerateFinalReport(reportPath);
                monitor.Dispose();
                DiagLog($"Monitoring report generated: {reportPath}");
            }
            catch (Exception ex)
            {
                DiagLog($"Failed to generate monitoring report: {ex.Message}");
            }
            
            BlackBoxRecorder.Shutdown();
        }

        /// <summary>
        /// Unhandled Exception Handler
        /// </summary>
        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string errorMessage = $"خطأ غير متوقع:\n\nالرسالة: {e.Exception.Message}\n\nالمصدر: {e.Exception.Source}";
            
            BlackBoxRecorder.RecordError("UNHANDLED", e.Exception.Message, e.Exception);
            DiagLog($"[UNHANDLED EXCEPTION] {e.Exception.Message}");
            DiagLog($"  Source: {e.Exception.Source}");
            DiagLog($"  StackTrace: {e.Exception.StackTrace}");
            if (e.Exception.InnerException != null)
                DiagLog($"  Inner: {e.Exception.InnerException.Message}");
            
            MessageBox.Show(
                errorMessage,
                "خطأ",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }
    }
}
