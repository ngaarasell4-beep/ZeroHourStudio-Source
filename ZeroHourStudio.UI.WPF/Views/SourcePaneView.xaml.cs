using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.UI.WPF.Services;
using ZeroHourStudio.UI.WPF.ViewModels;

namespace ZeroHourStudio.UI.WPF.Views
{
    public partial class SourcePaneView : UserControl
    {
        private Point _dragStartPoint;

        public SourcePaneView()
        {
            InitializeComponent();
        }

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "اختر مجلد المود المصدر"
            };

            if (dialog.ShowDialog() == true && DataContext is SourcePaneViewModel vm)
            {
                vm.ModPath = dialog.FolderName;
            }
        }

        private void UnitList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void UnitList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            var currentPos = e.GetPosition(null);
            var diff = _dragStartPoint - currentPos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (sender is ListBox listBox && listBox.SelectedItem is SageUnit unit)
                {
                    DragDropService.BeginDragUnit(listBox, unit);
                }
            }
        }
    }
}
