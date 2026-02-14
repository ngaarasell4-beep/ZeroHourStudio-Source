using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ZeroHourStudio.Infrastructure.DiffEngine;

namespace ZeroHourStudio.UI.WPF.Views
{
    /// <summary>
    /// نافذة عرض الفروقات (Diff Viewer)
    /// </summary>
    public partial class DiffViewerWindow : Window
    {
        private readonly DiffGenerator _diffGenerator = new();
        private List<FileDiff> _diffs = new();

        public DiffViewerWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// تحميل الفروقات بين مودين
        /// </summary>
        public async Task LoadDiffsAsync(string sourceModPath, string targetModPath)
        {
            try
            {
                _diffs = await _diffGenerator.GenerateModDiff(sourceModPath, targetModPath);
                FileSelector.ItemsSource = _diffs;

                if (_diffs.Count > 0)
                {
                    FileSelector.SelectedIndex = 0;
                    StatsText.Text = $"📊 {_diffs.Count} ملف تم تعديله";
                }
                else
                {
                    StatsText.Text = "✓ لا توجد فروقات";
                    DiffListBox.ItemsSource = null;
                }
            }
            catch (System.Exception ex)
            {
                StatsText.Text = $"خطأ: {ex.Message}";
            }
        }

        /// <summary>
        /// تحميل Diff لملف واحد
        /// </summary>
        public async Task LoadSingleDiffAsync(string sourcePath, string targetPath, string label)
        {
            try
            {
                var diff = await _diffGenerator.GenerateDiff(sourcePath, targetPath, label);
                _diffs = new List<FileDiff> { diff };
                FileSelector.ItemsSource = _diffs;
                FileSelector.SelectedIndex = 0;
            }
            catch (System.Exception ex)
            {
                StatsText.Text = $"خطأ: {ex.Message}";
            }
        }

        private void FileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileSelector.SelectedItem is FileDiff diff)
            {
                DiffListBox.ItemsSource = diff.Lines;
                StatsText.Text = $"+{diff.Statistics.AddedLines}  -{diff.Statistics.RemovedLines}  ~{diff.Statistics.ModifiedLines}  ({diff.Statistics.ChangePercentage}% تغيير)";
            }
        }

        private void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            if (FileSelector.SelectedItem is not FileDiff diff)
            {
                MessageBox.Show("اختر ملفاً أولاً", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "HTML Files|*.html",
                FileName = $"Diff_{diff.FileName.Replace(Path.DirectorySeparatorChar, '_')}.html"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var html = _diffGenerator.ExportAsHtml(diff);
                File.WriteAllText(saveDialog.FileName, html);
                MessageBox.Show($"تم التصدير إلى:\n{saveDialog.FileName}", "✓ تم التصدير", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
