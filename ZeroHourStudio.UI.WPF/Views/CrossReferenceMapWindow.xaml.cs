using System.Windows;
using System.Windows.Controls;
using ZeroHourStudio.Infrastructure.Analysis;

namespace ZeroHourStudio.UI.WPF.Views
{
    /// <summary>
    /// نافذة خريطة المراجع التقاطعية
    /// </summary>
    public partial class CrossReferenceMapWindow : Window
    {
        private readonly CrossReferenceAnalyzer _analyzer = new();
        private CrossReferenceReport? _report;
        private string _currentCategory = "Weapon";

        public CrossReferenceMapWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// تحميل وتحليل المود
        /// </summary>
        public async Task AnalyzeModAsync(string modPath)
        {
            StatsText.Text = "جاري التحليل...";
            // Run heavy parsing on background thread
            _report = await Task.Run(() => _analyzer.AnalyzeModAsync(modPath)).ConfigureAwait(true);
            StatsText.Text = _report.TotalResources > 0
                ? $"📊 {_report.TotalResources} مورد | {_report.SharedResources} مشترك"
                : "لم يتم العثور على موارد. تأكد من أن مسار المود المصدر يحتوي على ملفات INI مفكوكة (مثل Data\\INI).";
            ShowCategory("Weapon");
            EmptyCrossRefMessage.Visibility = _report.TotalResources == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowCategory(string category)
        {
            _currentCategory = category;
            if (_report == null) return;

            var list = category switch
            {
                "Weapon" => _report.WeaponReferences,
                "Armor" => _report.ArmorReferences,
                "FX" => _report.FxReferences,
                "Model" => _report.ModelReferences,
                "Texture" => _report.TextureReferences,
                _ => _report.WeaponReferences
            };

            var search = SearchBox.Text?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(search))
            {
                list = list.Where(r => r.ResourceName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            ResourceList.ItemsSource = list;
            UsersText.Text = "";
        }

        private void Category_Weapons(object sender, RoutedEventArgs e) => ShowCategory("Weapon");
        private void Category_Armor(object sender, RoutedEventArgs e) => ShowCategory("Armor");
        private void Category_FX(object sender, RoutedEventArgs e) => ShowCategory("FX");
        private void Category_Models(object sender, RoutedEventArgs e) => ShowCategory("Model");
        private void Category_Textures(object sender, RoutedEventArgs e) => ShowCategory("Texture");

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ShowCategory(_currentCategory);

        private void ResourceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResourceList.SelectedItem is CrossReference crossRef)
            {
                UsersText.Text = string.Join("، ", crossRef.UsedByUnits);
            }
            else
            {
                UsersText.Text = "";
            }
        }
    }
}
