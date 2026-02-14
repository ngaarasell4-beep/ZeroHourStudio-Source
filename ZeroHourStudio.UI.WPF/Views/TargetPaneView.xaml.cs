using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using ZeroHourStudio.UI.WPF.Services;
using ZeroHourStudio.UI.WPF.ViewModels;

namespace ZeroHourStudio.UI.WPF.Views
{
    public partial class TargetPaneView : UserControl
    {
        public TargetPaneView()
        {
            InitializeComponent();
        }

        private void BrowseTarget_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "اختر مجلد المود الهدف"
            };

            if (dialog.ShowDialog() == true && DataContext is TargetPaneViewModel vm)
            {
                vm.ModPath = dialog.FolderName;
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (DragDropService.HasUnitData(e))
            {
                e.Effects = DragDropEffects.Copy;
                MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x00));
                MainBorder.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x6B, 0x00),
                    BlurRadius = 20,
                    ShadowDepth = 0
                };
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x23, 0x32));
            MainBorder.Effect = null;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x23, 0x32));
            MainBorder.Effect = null;

            var unit = DragDropService.ExtractUnit(e);
            if (unit != null && DataContext is TargetPaneViewModel vm)
            {
                vm.HandleUnitDrop(unit);
            }
            e.Handled = true;
        }
    }
}
