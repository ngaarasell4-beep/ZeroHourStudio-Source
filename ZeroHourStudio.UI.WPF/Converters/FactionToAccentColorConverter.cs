using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ZeroHourStudio.UI.WPF.Converters;

/// <summary>
/// تحويل اسم الفصيل إلى لون مميز
/// </summary>
public class FactionToAccentColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string faction)
        {
            var factionLower = faction.ToLowerInvariant();

            if (factionLower.Contains("usa") || factionLower.Contains("america"))
                return new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF)); // سماوي

            if (factionLower.Contains("china"))
                return new SolidColorBrush(Color.FromRgb(0xFF, 0x33, 0x66)); // أحمر

            if (factionLower.Contains("gla"))
                return new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)); // أخضر

            if (factionLower.Contains("boss"))
                return new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x00)); // برتقالي

            // ألوان إضافية حسب الهاش
            var hash = Math.Abs(faction.GetHashCode());
            var colors = new[]
            {
                Color.FromRgb(0x9B, 0x59, 0xB6), // بنفسجي
                Color.FromRgb(0xE6, 0x7E, 0x22), // برتقالي غامق
                Color.FromRgb(0x1A, 0xBC, 0x9C), // فيروزي
                Color.FromRgb(0xFF, 0xD7, 0x00), // ذهبي
                Color.FromRgb(0xE7, 0x4C, 0x3C), // أحمر فاتح
            };

            return new SolidColorBrush(colors[hash % colors.Length]);
        }

        return new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xAA));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
