using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZeroHourStudio.Infrastructure.Services;

namespace ZeroHourStudio.UI.WPF.Views
{
    /// <summary>
    /// نافذة عرض معلومات نموذج W3D
    /// </summary>
    public partial class W3dPreviewWindow : Window
    {
        public W3dPreviewWindow()
        {
            InitializeComponent();
        }

        public void ShowInfo(W3dFileInfo info)
        {
            FileNameText.Text = $"{info.FileName} ({FormatBytes(info.FileSize)})";

            if (!info.IsValid || !string.IsNullOrEmpty(info.ErrorMessage))
            {
                ErrorText.Text = !string.IsNullOrEmpty(info.ErrorMessage)
                    ? $"⚠ {info.ErrorMessage}"
                    : "⚠ لم يتم التعرف على صيغة الملف";
                ErrorText.Visibility = Visibility.Visible;
                DetailsList.Visibility = Visibility.Collapsed;
                return;
            }

            MeshCountText.Text = info.Meshes.Count.ToString("N0");
            VertexCountText.Text = info.TotalVertices.ToString("N0");
            FaceCountText.Text = info.TotalFaces.ToString("N0");
            TextureCountText.Text = info.TextureNames.Count.ToString("N0");

            var items = new List<object>();

            // Add meshes
            foreach (var mesh in info.Meshes)
            {
                items.Add(new W3dDetailItem
                {
                    Icon = "🔷",
                    Title = string.IsNullOrEmpty(mesh.MeshName) ? "(بدون اسم)" : mesh.MeshName,
                    Details = $"{mesh.VertexCount:N0} نقطة | {mesh.FaceCount:N0} مثلث",
                    Color = "#00D4FF"
                });
            }

            // Add textures
            if (info.TextureNames.Count > 0)
            {
                items.Add(new W3dDetailItem
                {
                    Icon = "🎨",
                    Title = "الأنسجة (Textures)",
                    Details = "",
                    Color = "#FF69B4",
                    IsSeparator = true
                });

                foreach (var tex in info.TextureNames)
                {
                    items.Add(new W3dDetailItem
                    {
                        Icon = "  📄",
                        Title = tex,
                        Details = "",
                        Color = "#8899AA"
                    });
                }
            }

            // Add hierarchy info
            if (!string.IsNullOrEmpty(info.HierarchyName))
            {
                items.Add(new W3dDetailItem
                {
                    Icon = "🦴",
                    Title = $"التسلسل الهرمي: {info.HierarchyName}",
                    Details = "",
                    Color = "#FFD700"
                });
            }

            if (info.AnimationCount > 0)
            {
                items.Add(new W3dDetailItem
                {
                    Icon = "🎬",
                    Title = $"الحركات: {info.AnimationCount}",
                    Details = "",
                    Color = "#00FF88"
                });
            }

            DetailsList.ItemsSource = items;
            DetailsList.ItemTemplate = CreateDetailTemplate();
        }

        private DataTemplate CreateDetailTemplate()
        {
            var template = new DataTemplate(typeof(W3dDetailItem));

            var gridFactory = new FrameworkElementFactory(typeof(Grid));

            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(30));
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);

            gridFactory.AppendChild(col0);
            gridFactory.AppendChild(col1);
            gridFactory.AppendChild(col2);

            var iconFactory = new FrameworkElementFactory(typeof(TextBlock));
            iconFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Icon"));
            iconFactory.SetValue(TextBlock.FontSizeProperty, 14.0);
            iconFactory.SetValue(Grid.ColumnProperty, 0);
            iconFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

            var titleFactory = new FrameworkElementFactory(typeof(TextBlock));
            titleFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title"));
            titleFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
            titleFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xF3)));
            titleFactory.SetValue(Grid.ColumnProperty, 1);
            titleFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

            var detailFactory = new FrameworkElementFactory(typeof(TextBlock));
            detailFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Details"));
            detailFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
            detailFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x88, 0x99, 0xAA)));
            detailFactory.SetValue(Grid.ColumnProperty, 2);
            detailFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            detailFactory.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));

            gridFactory.AppendChild(iconFactory);
            gridFactory.AppendChild(titleFactory);
            gridFactory.AppendChild(detailFactory);

            template.VisualTree = gridFactory;
            return template;
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:F1} {sizes[order]}";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }

    internal class W3dDetailItem
    {
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Details { get; set; } = "";
        public string Color { get; set; } = "#FFFFFF";
        public bool IsSeparator { get; set; }
    }
}
