using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.UI.WPF.Converters;

/// <summary>
/// تحويل نوع التبعية إلى أيقونة Unicode
/// </summary>
public class DependencyTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DependencyType type)
        {
            return type switch
            {
                DependencyType.Model3D => "\u25A0",            // ■ مكعب
                DependencyType.Texture => "\u25C6",            // ◆ ماسة
                DependencyType.Audio => "\u266B",              // ♫ موسيقى
                DependencyType.Weapon => "\u2694",             // ⚔ أسلحة
                DependencyType.FXList => "\u2726",             // ✦ تأثير
                DependencyType.VisualEffect => "\u2728",       // ✨ لمعة
                DependencyType.Projectile => "\u2699",         // ⚙ ترس
                DependencyType.Armor => "\u25D7",              // ◗ درع
                DependencyType.ObjectINI => "\u25B6",          // ▶ ملف
                DependencyType.OCL => "\u2747",                // ❇ إنشاء
                DependencyType.Locomotor => "\u27A4",          // ➤ حركة
                DependencyType.CommandSet => "\u2630",         // ☰ أوامر
                DependencyType.Upgrade => "\u2B06",            // ⬆ ترقية
                DependencyType.ParticleSystem => "\u2734",     // ✴ جسيمات
                _ => "\u25CB"                                  // ○ دائرة
            };
        }
        return "\u25CB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
