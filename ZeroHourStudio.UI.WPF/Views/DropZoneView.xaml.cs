using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.UI.WPF.Services;
using ZeroHourStudio.UI.WPF.ViewModels;

namespace ZeroHourStudio.UI.WPF.Views
{
    public partial class DropZoneView : UserControl
    {
        private Storyboard? _scannerStoryboard;

        public DropZoneView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            StartScannerAnimation();
        }

        private void StartScannerAnimation()
        {
            var height = ActualHeight > 0 ? ActualHeight : 150;

            var animation = new DoubleAnimation
            {
                From = -10,
                To = height,
                Duration = TimeSpan.FromSeconds(3),
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase()
            };

            _scannerStoryboard = new Storyboard();
            _scannerStoryboard.Children.Add(animation);
            Storyboard.SetTarget(animation, ScannerLine);
            Storyboard.SetTargetProperty(animation,
                new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

            _scannerStoryboard.Begin();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (DragDropService.HasUnitData(e))
            {
                e.Effects = DragDropEffects.Copy;
                DropBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF));
                DropBorder.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x00, 0xD4, 0xFF),
                    BlurRadius = 25,
                    ShadowDepth = 0
                };
                DropIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF));
                DropText.Text = "أفلت الوحدة هنا";
                DropText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF));

                // تسريع الماسح
                _scannerStoryboard?.Stop();
                var fastAnim = new DoubleAnimation
                {
                    From = -10,
                    To = ActualHeight > 0 ? ActualHeight : 150,
                    Duration = TimeSpan.FromSeconds(1.2),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                ScannerTransform.BeginAnimation(TranslateTransform.YProperty, fastAnim);
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            ResetDropZone();
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            ResetDropZone();

            var unit = DragDropService.ExtractUnit(e);
            if (unit != null)
            {
                // إرسال إلى PortingStudioViewModel عبر TargetPane
                var window = Window.GetWindow(this);
                if (window?.DataContext is PortingStudioViewModel studioVM)
                {
                    studioVM.TargetPane.HandleUnitDrop(unit);
                }
            }
            e.Handled = true;
        }

        private void ResetDropZone()
        {
            DropBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0x00, 0xD4, 0xFF));
            DropBorder.Effect = null;
            DropIcon.Foreground = new SolidColorBrush(Color.FromArgb(0x1A, 0x00, 0xD4, 0xFF));
            DropText.Text = "اسحب وحدة هنا";
            DropText.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x44, 0x55));

            // إعادة سرعة الماسح العادية
            ScannerTransform.BeginAnimation(TranslateTransform.YProperty, null);
            StartScannerAnimation();
        }
    }
}
