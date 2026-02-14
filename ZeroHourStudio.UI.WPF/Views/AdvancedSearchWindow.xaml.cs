using System.Windows;

namespace ZeroHourStudio.UI.WPF.Views
{
    public partial class AdvancedSearchWindow : Window
    {
        public AdvancedSearchWindow()
        {
            InitializeComponent();
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder: ربط البحث بمصدر الوحدات (قائمة المصدر) عند التوفر
            MessageBox.Show("استخدم صندوق البحث في لوحة المصدر للبحث عن الوحدات، أو معاينة الفروقات للمقارنة.", "بحث متقدم", 
                          MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder: إعادة تعيين حقول النافذة
            MessageBox.Show("إعادة التعيين متاحة عند تفعيل البحث المتقدم.", "إعادة تعيين", 
                          MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder: تصدير النتائج (CSV/Excel) عند تفعيل البحث
            MessageBox.Show("التصدير متاح عند تفعيل البحث المتقدم وتوفر نتائج.", "تصدير", 
                          MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}