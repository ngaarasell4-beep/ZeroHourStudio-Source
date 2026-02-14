using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ZeroHourStudio.UI.WPF.Converters
{
    /// <summary>
    /// يحول الرقم 0 إلى Collapsed وأي رقم آخر إلى Visible (لإخفاء عنصر عند القيمة 0).
    /// </summary>
    public class ZeroToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i)
                return i == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (value is long l)
                return l == 0 ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
