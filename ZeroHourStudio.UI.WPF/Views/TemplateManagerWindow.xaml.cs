using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ZeroHourStudio.Domain.Models;
using ZeroHourStudio.Infrastructure.Templates;

namespace ZeroHourStudio.UI.WPF.Views
{
    /// <summary>
    /// نافذة إدارة قوالب النقل
    /// </summary>
    public partial class TemplateManagerWindow : Window
    {
        private readonly TransferTemplateManager _templateManager = new();
        public TransferTemplate? SelectedTemplate { get; private set; }
        public bool TemplateApplied { get; private set; }

        /// <summary>فصائل متاحة للاختيار (من المود الهدف). إن وُجدت تُستخدم بدل القائمة الثابتة.</summary>
        public IEnumerable<string>? AvailableFactions { get; set; }

        public TemplateManagerWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                PopulateFactionCombo();
                await LoadTemplatesAsync();
            };
        }

        private void PopulateFactionCombo()
        {
            FactionCombo.Items.Clear();
            var factions = AvailableFactions?.ToList() ?? new List<string>();

            if (factions.Count == 0)
            {
                // No fake data — show real state to user
                FactionCombo.Items.Add(new ComboBoxItem
                {
                    Content = "⚠ لم يُعثر على فصائل (تحقق من السجلات)",
                    IsEnabled = false,
                    Foreground = System.Windows.Media.Brushes.OrangeRed
                });
                FactionCombo.SelectedIndex = 0;
                return;
            }

            foreach (var f in factions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                FactionCombo.Items.Add(new ComboBoxItem { Content = f });
            if (FactionCombo.Items.Count > 0)
                FactionCombo.SelectedIndex = 0;
        }

        private async Task LoadTemplatesAsync()
        {
            var templates = await _templateManager.LoadTemplatesAsync();
            TemplatesList.ItemsSource = templates;
            NoTemplatesText.Visibility = templates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TemplatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedTemplate = TemplatesList.SelectedItem as TransferTemplate;
        }

        private async void SaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            var name = TemplateNameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("الرجاء إدخال اسم القالب", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var factionItem = FactionCombo.SelectedItem as ComboBoxItem;
            var template = new TransferTemplate
            {
                Name = name,
                Description = TemplateDescBox.Text?.Trim() ?? "",
                TargetFaction = factionItem?.Content?.ToString() ?? string.Empty
            };

            await _templateManager.SaveTemplateAsync(template);
            MessageBox.Show($"✓ تم حفظ القالب '{name}'", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadTemplatesAsync();

            TemplateNameBox.Clear();
            TemplateDescBox.Clear();
        }

        private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTemplate == null)
            {
                MessageBox.Show("اختر قالباً أولاً", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            TemplateApplied = true;
            DialogResult = true;
            Close();
        }

        private async void DeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not TransferTemplate template)
                return;

            var result = MessageBox.Show(
                $"هل تريد حذف القالب '{template.Name}'؟",
                "تأكيد الحذف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _templateManager.DeleteTemplateAsync(template.Id);
                await LoadTemplatesAsync();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
