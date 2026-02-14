using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ZeroHourStudio.UI.WPF.ViewModels;
using ZeroHourStudio.UI.WPF.Core;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using ZeroHourStudio.Infrastructure.Logging;

namespace ZeroHourStudio.UI.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Title = "ZeroHour Studio V2 - مدير نقل الوحدات المتقدم";
            
            // تعيين DataContext للـ ViewModel
            try
            {
                this.DataContext = new MainViewModel();
            }
            catch (Exception ex)
            {
                BlackBoxRecorder.RecordError("UI_INIT", "MainViewModel creation failed", ex);
                MessageBox.Show($"خطأ في تهيئة MainViewModel: {ex.Message}\n\n{ex.StackTrace}", 
                    "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Black-Box: Global mouse click recorder
            this.PreviewMouseDown += MainWindow_PreviewMouseDown;
            BlackBoxRecorder.Record("UI", "WINDOW_LOADED", "MainWindow initialized");
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            var element = e.OriginalSource as FrameworkElement;
            var elementName = element?.Name ?? element?.GetType().Name ?? "unknown";
            var elementType = element?.GetType().Name ?? "unknown";

            // Try to get button content or text
            var detail = elementName;
            if (element is Button btn) detail = btn.Content?.ToString() ?? elementName;
            else if (element is TextBlock tb) detail = tb.Text?.Length > 30 ? tb.Text[..30] : tb.Text ?? elementName;

            BlackBoxRecorder.RecordMouseClick(
                e.ChangedButton.ToString(), pos.X, pos.Y, detail, elementType);
        }

        /// <summary>
        /// Handler لزر الإلغاء
        /// </summary>
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// Handler لزر استعراض المود المصدر
        /// </summary>
        private void BrowseSourcePath_Click(object sender, RoutedEventArgs e)
        {
            BlackBoxRecorder.RecordDialogOpen("FolderBrowser", "اختر مجلد المود المصدر");
            var dialog = new OpenFolderDialog
            {
                Title = "اختر مجلد المود المصدر (Zero Hour)",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                BlackBoxRecorder.RecordDialogResult("FolderBrowser", "OK", dialog.FolderName);
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.SourceModPath = dialog.FolderName;
                    // استدعاء LoadModAsync تلقائياً عند اختيار المسار
                    _ = viewModel.LoadModAsync();
                }
            }
            else
            {
                BlackBoxRecorder.RecordDialogResult("FolderBrowser", "CANCEL", "");
            }
        }

        /// <summary>
        /// Handler لزر استعراض المود الهدف
        /// </summary>
        private async void BrowseTargetPath_Click(object sender, RoutedEventArgs e)
        {
            BlackBoxRecorder.RecordDialogOpen("FolderBrowser", "اختر مجلد المود الهدف");
            var dialog = new OpenFolderDialog
            {
                Title = "اختر مجلد المود الهدف",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                BlackBoxRecorder.RecordDialogResult("FolderBrowser", "OK", dialog.FolderName);
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.TargetModPath = dialog.FolderName;
                    
                    try
                    {
                        await viewModel.LoadTargetModAsync();
                        App.DiagLog($"[BrowseTarget] LoadTargetModAsync completed. Factions: {viewModel.TargetFactionOptions.Count}");
                    }
                    catch (Exception ex)
                    {
                        App.DiagLog($"[BrowseTarget] ERROR: {ex.Message}\n{ex.StackTrace}");
                        MessageBox.Show($"خطأ في تحميل المود الهدف:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                BlackBoxRecorder.RecordDialogResult("FolderBrowser", "CANCEL", "");
            }
        }

        /// <summary>
        /// Handler لزر تشخيص المود
        /// </summary>
        private async void Diagnostic_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                MessageBox.Show("DataContext غير صالح", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(viewModel.SourceModPath))
            {
                MessageBox.Show("الرجاء اختيار مسار المود المصدر أولاً", "تشخيص المود", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var logger = new Infrastructure.Logging.SimpleLogger();
                var discoveryService = new Infrastructure.Services.UnitDiscoveryService();
                var bigFileReader = new Infrastructure.Implementations.ModBigFileReader(viewModel.SourceModPath);
                var statsSystem = new AdvancedStatisticsSystem(logger, discoveryService, bigFileReader);

                viewModel.StatusMessage = "جاري تشخيص المود...";
                viewModel.IsLoading = true;

                var statistics = await statsSystem.RunComprehensiveDiagnostic(viewModel.SourceModPath);
                var report = statsSystem.GenerateDiagnosticReport();

                viewModel.IsLoading = false;
                viewModel.StatusMessage = $"اكتمل التشخيص - {statistics.Count} معلومات";

                // عرض نافذة التشخيص
                var diagnosticWindow = new System.Windows.Window
                {
                    Title = "تقرير تشخيص المود",
                    Width = 800,
                    Height = 600,
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    Foreground = Brushes.White,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };

                var scrollViewer = new ScrollViewer();
                var textBlock = new TextBlock
                {
                    Text = report,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10)
                };

                scrollViewer.Content = textBlock;
                diagnosticWindow.Content = scrollViewer;
                diagnosticWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                viewModel.IsLoading = false;
                viewModel.StatusMessage = $"خطأ في التشخيص: {ex.Message}";
                MessageBox.Show($"حدث خطأ أثناء التشخيص: {ex.Message}", "خطأ", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}