using System.Windows;
using ZeroHourStudio.Infrastructure.ConflictResolution;
using ZeroHourStudio.UI.WPF.ViewModels;

namespace ZeroHourStudio.UI.WPF.Views
{
    /// <summary>
    /// نافذة الدمج الذكي
    /// </summary>
    public partial class MergeConflictResolverWindow : Window
    {
        public bool UserConfirmed { get; private set; }

        public MergeConflictResolverWindow()
        {
            InitializeComponent();
        }

        private void Strategy_PreferSource(object sender, RoutedEventArgs e)
        {
            if (DataContext is MergeConflictViewModel vm)
                vm.SelectedStrategy = MergeStrategy.SourceWins;
        }

        private void Strategy_PreferTarget(object sender, RoutedEventArgs e)
        {
            if (DataContext is MergeConflictViewModel vm)
                vm.SelectedStrategy = MergeStrategy.TargetWins;
        }

        private void Strategy_Smart(object sender, RoutedEventArgs e)
        {
            if (DataContext is MergeConflictViewModel vm)
                vm.SelectedStrategy = MergeStrategy.SmartMerge;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            UserConfirmed = true;
            if (DataContext is MergeConflictViewModel vm)
                vm.UserConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            UserConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
