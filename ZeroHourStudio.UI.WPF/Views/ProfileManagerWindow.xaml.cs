using System.Windows;
using System.Windows.Controls;
using ZeroHourStudio.Domain.Models;
using ZeroHourStudio.Infrastructure.Profiles;

namespace ZeroHourStudio.UI.WPF.Views
{
    public partial class ProfileManagerWindow : Window
    {
        private readonly TransferProfileService _service = new();

        /// <summary>الملف المحمّل (بعد النقر على تحميل المحدد).</summary>
        public TransferProfile? LoadedProfile { get; private set; }

        public string CurrentSourcePath { get; set; } = string.Empty;
        public string CurrentTargetPath { get; set; } = string.Empty;
        public string? CurrentTargetFaction { get; set; }

        public ProfileManagerWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => _ = LoadListAsync();
        }

        private async System.Threading.Tasks.Task LoadListAsync()
        {
            var list = await _service.LoadAllAsync();
            ProfilesList.ItemsSource = list;
        }

        private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadSelectedBtn.IsEnabled = ProfilesList.SelectedItem != null;
        }

        private async void SaveCurrent_Click(object sender, RoutedEventArgs e)
        {
            var name = NewProfileName.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("أدخل اسماً للملف.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var profile = new TransferProfile
            {
                Name = name,
                SourceModPath = CurrentSourcePath ?? "",
                TargetModPath = CurrentTargetPath ?? "",
                TargetFaction = CurrentTargetFaction
            };
            await _service.SaveAsync(profile);
            NewProfileName.Clear();
            await LoadListAsync();
            MessageBox.Show($"تم حفظ الملف '{name}'.", "تم", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesList.SelectedItem is TransferProfile p)
            {
                LoadedProfile = p;
                DialogResult = true;
                Close();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
