using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using ZeroHourStudio.UI.WPF.Services;

namespace ZeroHourStudio.UI.WPF.Converters;

public class ButtonImageToIconConverter : IValueConverter
{
    public static IconService? IconService { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (IconService == null || value is not string buttonImage || string.IsNullOrWhiteSpace(buttonImage))
            return null;

        return IconService.GetIcon(buttonImage);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
