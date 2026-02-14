using System.Windows;

namespace ZeroHourStudio.UI.WPF.Views
{
    public partial class TransferPreviewWindow : Window
    {
        public bool UserConfirmed { get; private set; }

        public TransferPreviewWindow()
        {
            InitializeComponent();
        }

        private void Proceed_Click(object sender, RoutedEventArgs e)
        {
            UserConfirmed = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            UserConfirmed = false;
            Close();
        }
    }
}
