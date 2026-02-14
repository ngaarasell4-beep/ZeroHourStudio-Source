using System.Windows;
using ZeroHourStudio.Infrastructure.Analysis;

namespace ZeroHourStudio.UI.WPF.Views
{
    /// <summary>
    /// نافذة تقرير التوازن
    /// </summary>
    public partial class BalanceReportWindow : Window
    {
        private readonly BalanceAnalyzer _analyzer = new();

        public BalanceReportWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// تحليل توازن وحدة
        /// </summary>
        public async Task AnalyzeUnitAsync(string modPath, string unitName)
        {
            UnitNameText.Text = unitName;
            OverallScoreText.Text = "جاري التحليل...";

            // Run heavy parsing on background thread
            var report = await Task.Run(() => _analyzer.AnalyzeUnit(modPath, unitName)).ConfigureAwait(true);

            UnitNameText.Text = report.UnitName;
            OverallScoreText.Text = $"{report.OverallScore}%";
            OverallScoreText.Foreground = new System.Windows.Media.SolidColorBrush(
                report.OverallScore > 120 ? System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00) :
                report.OverallScore > 80 ? System.Windows.Media.Color.FromRgb(0x00, 0xCC, 0x66) :
                System.Windows.Media.Color.FromRgb(0xFF, 0x66, 0x66));

            VerdictText.Text = report.OverallVerdict;
            PeerCountText.Text = report.PeerCount > 0
                ? $"مقارنة مع {report.PeerCount} وحدة"
                : "لم يتم العثور على وحدات للمقارنة. تأكد من أن مسار المود المصدر يحتوي على ملفات INI (مثل Data\\INI).";

            RatingsList.ItemsSource = report.Ratings;
            EmptyBalanceMessage.Visibility = (report.PeerCount == 0 || report.Ratings.Count == 0)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
