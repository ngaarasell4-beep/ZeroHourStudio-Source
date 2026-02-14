using System.Windows;
using System.Windows.Controls;
using ZeroHourStudio.Domain.Models;
using ZeroHourStudio.Infrastructure.Services;

namespace ZeroHourStudio.UI.WPF.Views
{
    /// <summary>
    /// نافذة تحويل فصيل الوحدة
    /// </summary>
    public partial class FactionConversionWindow : Window
    {
        private readonly FactionAdapterService _adapter = new();
        private string _unitContent = string.Empty;
        private string _unitName = string.Empty;

        public bool ConversionApplied { get; private set; }
        public string? ConvertedContent { get; private set; }
        public FactionConversionRules? AppliedRules { get; private set; }

        public FactionConversionWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// تحميل بيانات الوحدة للتحويل
        /// </summary>
        public void LoadUnit(string unitName, string unitContent)
        {
            _unitName = unitName;
            _unitContent = unitContent;
            UnitInfoText.Text = $"الوحدة: {unitName}";
            UpdatePreview();
        }

        private FactionConversionRules BuildRules()
        {
            var sourceFaction = (SourceFactionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "USA";
            var targetFaction = (TargetFactionCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "China";

            return new FactionConversionRules
            {
                SourceFaction = sourceFaction,
                TargetFaction = targetFaction,
                ConvertVoices = ChkVoice.IsChecked == true,
                ConvertColors = ChkColor.IsChecked == true,
                RenamePrefixes = ChkPrefix.IsChecked == true,
                ConvertWeapons = ChkWeapon.IsChecked == true,
                ConvertUpgrades = ChkUpgrade.IsChecked == true
            };
        }

        private void UpdatePreview()
        {
            if (string.IsNullOrEmpty(_unitContent))
            {
                ChangesList.ItemsSource = null;
                ChangesCountText.Text = "";
                NoContentMessage.Visibility = System.Windows.Visibility.Visible;
                return;
            }

            NoContentMessage.Visibility = System.Windows.Visibility.Collapsed;
            var rules = BuildRules();
            var preview = _adapter.PreviewConversion(_unitContent, _unitName, rules);
            ChangesList.ItemsSource = preview.Changes;
            ChangesCountText.Text = $"{preview.TotalChanges} تغيير";
        }

        private void FactionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();
        private void Option_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            var rules = BuildRules();
            ConvertedContent = _adapter.ConvertUnitToFaction(_unitContent, rules);
            AppliedRules = rules;
            ConversionApplied = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
