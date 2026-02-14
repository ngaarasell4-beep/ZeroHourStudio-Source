using System.Windows;

namespace ZeroHourStudio.UI.WPF.Views;

/// <summary>
/// نافذة المعاينة الذكية - Code Behind
/// </summary>
public partial class IntelligentTransferPreviewWindow : Window
{
    /// <summary>
    /// هل أكد المستخدم المتابعة؟
    /// </summary>
    public bool UserConfirmed { get; private set; }

    public IntelligentTransferPreviewWindow()
    {
        InitializeComponent();
    }

    private void Proceed_Click(object sender, RoutedEventArgs e)
    {
        UserConfirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        UserConfirmed = false;
        Close();
    }

    private void AutoResolveAll_Click(object sender, RoutedEventArgs e)
    {
        // زر "حل الكل تلقائياً" - يعرض رسالة تأكيد
        var result = MessageBox.Show(
            "سيتم تطبيق جميع الحلول التلقائية المتاحة.\nهل تريد المتابعة؟",
            "حل تلقائي",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            UserConfirmed = true;
            Close();
        }
    }
}
