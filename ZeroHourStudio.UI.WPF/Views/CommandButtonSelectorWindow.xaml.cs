using System.Windows;
using ZeroHourStudio.Domain.Models;
using ZeroHourStudio.UI.WPF.ViewModels;

namespace ZeroHourStudio.UI.WPF.Views
{
    /// <summary>
    /// نافذة اختيار موقع الزر في CommandSet
    /// </summary>
    public partial class CommandButtonSelectorWindow : Window
    {
        public bool UserConfirmed { get; private set; }
        public ButtonSelectionResult? Selection { get; private set; }

        public CommandButtonSelectorWindow()
        {
            InitializeComponent();
        }

        private void SlotButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is CommandButtonSlot slot)
            {
                var vm = DataContext as CommandButtonSelectorViewModel;
                if (vm == null) return;

                // إلغاء تحديد كل الأزرار أولاً
                foreach (var btn in vm.Buttons)
                    btn.IsSelected = false;

                // تحديد الزر المختار بصرياً
                slot.IsSelected = true;

                if (slot.IsEmpty)
                {
                    vm.SelectedButton = slot;
                    vm.SelectedButtonToReplace = null;
                }
                else
                {
                    // السماح باستبدال أي زر مشغول مباشرة
                    vm.SelectedButtonToReplace = slot;
                    vm.SelectedButton = null;
                }
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as CommandButtonSelectorViewModel;
            if (vm == null) return;

            Selection = vm.GetSelectionResult();
            if (Selection != null)
            {
                UserConfirmed = true;
                vm.UserConfirmed = true;
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            UserConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
