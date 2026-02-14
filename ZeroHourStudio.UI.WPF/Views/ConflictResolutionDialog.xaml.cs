using System.Windows;
using System.Windows.Controls;
using ZeroHourStudio.UI.WPF.ViewModels;

namespace ZeroHourStudio.UI.WPF.Views
{
    public partial class ConflictResolutionDialog : UserControl
    {
        public ConflictResolutionDialog()
        {
            InitializeComponent();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window?.DataContext is PortingStudioViewModel vm)
            {
                vm.ShowConflictDialog = false;
            }
        }

        private async void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window?.DataContext is PortingStudioViewModel vm)
            {
                await vm.ConfirmConflictResolutionAsync();
            }
        }
        private void SmartMerge_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = Window.GetWindow(this);
                var portingVM = window?.DataContext as ZeroHourStudio.UI.WPF.ViewModels.PortingStudioViewModel;
                var conflictVM = portingVM?.ConflictResolution;

                var mergeVM = new MergeConflictViewModel();

                // تحميل بيانات التعارض الفعلية إذا كانت موجودة
                if (conflictVM != null && conflictVM.Conflicts.Count > 0)
                {
                    var sourceData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var targetData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var conflict in conflictVM.Conflicts)
                    {
                        sourceData[conflict.OriginalName] = conflict.OriginalName;
                        targetData[conflict.OriginalName] = conflict.SuggestedRename;
                    }

                    mergeVM.LoadFromRawData(
                        conflictVM.UnitName + " (المصدر)",
                        conflictVM.UnitName + " (الهدف)",
                        sourceData, targetData);
                }

                var mergeWindow = new MergeConflictResolverWindow
                {
                    DataContext = mergeVM,
                    Owner = Window.GetWindow(this)
                };
                mergeWindow.ShowDialog();

                if (mergeWindow.UserConfirmed)
                {
                    System.Diagnostics.Debug.WriteLine("[SmartMerge] User confirmed smart merge");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartMerge] ERROR: {ex.Message}");
                MessageBox.Show($"خطأ في الدمج الذكي: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
