using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.UI.WPF.Converters
{
    public class SeverityColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is AuditSeverity severity)
            {
                return severity switch
                {
                    AuditSeverity.Critical => Brushes.DarkRed,
                    AuditSeverity.Error => Brushes.Red,
                    AuditSeverity.Warning => Brushes.Orange,
                    AuditSeverity.Info => Brushes.LightBlue,
                    _ => Brushes.White
                };
            }
            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}
