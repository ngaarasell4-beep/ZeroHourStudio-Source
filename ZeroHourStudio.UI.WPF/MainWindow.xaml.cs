using System.Windows;
using Microsoft.Win32;
using ZeroHourStudio.UI.WPF.ViewModels;

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
            this.Title = "ZeroHour Studio V2 - مدير نقل الوحدات";
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
            var dialog = new OpenFolderDialog
            {
                Title = "اختر مجلد المود المصدر (Zero Hour)",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.SourceModPath = dialog.FolderName;
                    // استدعاء LoadModAsync تلقائياً عند اختيار المسار
                    _ = viewModel.LoadModAsync();
                }
            }
        }

        /// <summary>
        /// Handler لزر استعراض المود الهدف
        /// </summary>
        private void BrowseTargetPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "اختر مجلد المود الهدف",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.TargetModPath = dialog.FolderName;
                }
            }
        }
    }
}
