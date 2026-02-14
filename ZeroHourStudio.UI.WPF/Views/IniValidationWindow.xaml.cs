using System.Windows;
using ZeroHourStudio.Infrastructure.Validation;

namespace ZeroHourStudio.UI.WPF.Views
{
    /// <summary>
    /// نافذة نتائج فحص INI
    /// </summary>
    public partial class IniValidationWindow : Window
    {
        public IniValidationWindow()
        {
            InitializeComponent();
        }

        public void ShowReport(IniValidationReport report)
        {
            SummaryText.Text = $"📊 {report.FilesScanned} ملف | ❌ {report.TotalErrors} خطأ | ⚠ {report.TotalWarnings} تحذير";

            if (report.Issues.Count == 0)
            {
                EmptyMessage.Visibility = Visibility.Visible;
                IssuesList.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyMessage.Visibility = Visibility.Collapsed;
                IssuesList.Visibility = Visibility.Visible;
                IssuesList.ItemsSource = report.Issues
                    .OrderByDescending(i => i.Severity == IniIssueSeverity.Error ? 1 : 0)
                    .ThenBy(i => i.FileName)
                    .ThenBy(i => i.LineNumber)
                    .ToList();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
