using System.Windows;
using System.Windows.Controls;

namespace ZeroHourStudio.UI.WPF.Views;

public partial class BatchTransferWindow : Window
{
    public bool UserConfirmed { get; private set; }
    public bool TransferStarted { get; private set; }

    public BatchTransferWindow()
    {
        InitializeComponent();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        TransferStarted = true;
        UserConfirmed = true;
        // لا نغلق النافذة - ننتظر اكتمال النقل
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        UserConfirmed = false;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// يُستدعى بعد اكتمال النقل الدفعي
    /// </summary>
    public void OnTransferComplete()
    {
        var closeButton = new Button
        {
            Content = "✓ إغلاق",
            Padding = new Thickness(18, 8, 18, 8),
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#238636")),
            Foreground = System.Windows.Media.Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeButton.Click += (s, e) => { DialogResult = true; Close(); };
    }
}
