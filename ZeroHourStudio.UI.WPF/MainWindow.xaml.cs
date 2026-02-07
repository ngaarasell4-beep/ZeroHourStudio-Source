using System.Windows;

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
    }
}
