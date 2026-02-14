using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.UI.WPF.Converters;

/// <summary>
/// تحويل حالة الأصل إلى لون
/// </summary>
public class AssetStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AssetStatus status)
        {
            return status switch
            {
                AssetStatus.Found => new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)),      // أخضر
                AssetStatus.Missing => new SolidColorBrush(Color.FromRgb(0xFF, 0x33, 0x66)),     // أحمر
                AssetStatus.Invalid => new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x00)),     // برتقالي
                AssetStatus.NotVerified => new SolidColorBrush(Color.FromRgb(0xFF, 0xDD, 0x44)), // أصفر
                AssetStatus.Unknown => new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xAA)),     // رمادي
                _ => new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xAA))
            };
        }
        return new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xAA));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
