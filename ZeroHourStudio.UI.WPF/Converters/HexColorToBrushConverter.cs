using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ZeroHourStudio.UI.WPF.Converters
{
    /// <summary>
    /// تحويل string hex color إلى SolidColorBrush
    /// مثال: "#FF6B00" → Orange brush
    /// </summary>
    public class HexColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string hexColor || string.IsNullOrWhiteSpace(hexColor))
                return Brushes.White;

            try
            {
                // إزالة # إذا كانت موجودة
                hexColor = hexColor.TrimStart('#');

                // التأكد من أن الطول صحيح
                if (hexColor.Length != 6 && hexColor.Length != 8)
                    return Brushes.White;

                // إضافة FF (alpha) إذا لم يكن موجوداً
                if (hexColor.Length == 6)
                    hexColor = "FF" + hexColor;

                // تحويل إلى color
                var color = Color.FromArgb(
                    System.Convert.ToByte(hexColor.Substring(0, 2), 16),
                    System.Convert.ToByte(hexColor.Substring(2, 2), 16),
                    System.Convert.ToByte(hexColor.Substring(4, 2), 16),
                    System.Convert.ToByte(hexColor.Substring(6, 2), 16));

                return new SolidColorBrush(color);
            }
            catch
            {
                return Brushes.White;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}