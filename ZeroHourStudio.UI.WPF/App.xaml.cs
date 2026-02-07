using System.Windows;
using System.Windows.Threading;

namespace ZeroHourStudio.UI.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// تهيئة التطبيق
        /// </summary>
        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Startup Handler
        /// </summary>
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // يمكن إضافة custom startup logic هنا
        }

        /// <summary>
        /// Exit Handler
        /// </summary>
        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Cleanup على إغلاق البرنامج
        }

        /// <summary>
        /// Unhandled Exception Handler
        /// </summary>
        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // معالجة الأخطاء غير المتوقعة
            MessageBox.Show(
                $"حدث خطأ غير متوقع:\n{e.Exception.Message}",
                "خطأ",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }
    }
}
