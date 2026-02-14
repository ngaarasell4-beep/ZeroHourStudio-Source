using System.Text;
using System.Windows;
using ZeroHourStudio.Infrastructure.Logging;
using ZeroHourStudio.UI.WPF.ViewModels;

namespace ZeroHourStudio.UI.WPF.Views
{
    public partial class PortingStudioWindow : Window
    {
        private readonly PortingStudioViewModel _viewModel;

        public PortingStudioWindow()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            InitializeComponent();
            _viewModel = new PortingStudioViewModel();
            DataContext = _viewModel;

            App.DiagLog("[PortingStudio] Window initialized");
            BlackBoxRecorder.Record("PORTING_STUDIO", "INIT", "Window created");
        }

        // === Window Control Buttons ===

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
