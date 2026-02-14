using System;
using System.Globalization;
using System.Windows.Data;

namespace ZeroHourStudio.UI.WPF.Converters;

/// <summary>
/// تحويل نسبة الاكتمال + عرض العنصر الأب → عرض بالبكسل
/// </summary>
public class CompletionToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 &&
            values[0] is double percentage &&
            values[1] is double containerWidth)
        {
            return Math.Max(0, containerWidth * (percentage / 100.0));
        }

        // fallback: single value mode
        if (values.Length >= 1 && values[0] is double pct)
        {
            return Math.Max(0, 200.0 * (pct / 100.0));
        }

        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
