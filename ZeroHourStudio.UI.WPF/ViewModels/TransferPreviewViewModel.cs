using System.Collections.Generic;
using System.Linq;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.UI.WPF.Core;

namespace ZeroHourStudio.UI.WPF.ViewModels
{
    public class TransferPreviewViewModel : ViewModelBase
    {
        public string UnitName { get; set; } = string.Empty;
        public string SourceFaction { get; set; } = string.Empty;
        public int DependencyCount { get; set; }

        /// <summary>ملخص التبعيات حسب النوع (أسلحة: 3، ترقيات: 2، ...)</summary>
        public string DependencyBreakdown { get; set; } = string.Empty;

        /// <summary>عدد التبعيات المفقودة</summary>
        public int MissingCount { get; set; }

        public string TargetModName { get; set; } = string.Empty;
        public string TargetFaction { get; set; } = string.Empty;
        public int SlotNumber { get; set; }
        public string CommandSetName { get; set; } = string.Empty;

        public List<string> Warnings { get; set; } = new();
        public bool HasWarnings => Warnings.Count > 0;

        public List<string> Notes { get; set; } = new();
        public bool HasNotes => Notes.Count > 0;

        public string Summary { get; set; } = string.Empty;

        /// <summary>بناء ملخص التبعيات من الرسم</summary>
        public static string BuildDependencyBreakdown(UnitDependencyGraph? graph)
        {
            if (graph?.AllNodes == null || graph.AllNodes.Count == 0)
                return "لا توجد تبعيات";

            var labels = new Dictionary<DependencyType, string>
            {
                [DependencyType.Weapon] = "أسلحة",
                [DependencyType.Upgrade] = "ترقيات",
                [DependencyType.ObjectINI] = "تعريفات INI",
                [DependencyType.Projectile] = "مقذوفات",
                [DependencyType.Model3D] = "نماذج",
                [DependencyType.Texture] = "نسيج",
                [DependencyType.Audio] = "أصوات",
                [DependencyType.FXList] = "مؤثرات",
                [DependencyType.Armor] = "دروع",
                [DependencyType.Locomotor] = "محركات",
                [DependencyType.CommandSet] = "أزرار قيادة",
                [DependencyType.ParticleSystem] = "جسيمات",
                [DependencyType.OCL] = "قوائم إنشاء",
                [DependencyType.Custom] = "أخرى"
            };

            var byType = graph.AllNodes
                .Where(n => n.Type != DependencyType.ObjectINI || n.Name != graph.UnitName)
                .GroupBy(n => n.Type)
                .Select(g => (Type: g.Key, Count: g.Count()))
                .Where(x => x.Count > 0)
                .OrderByDescending(x => x.Count)
                .Select(x => labels.TryGetValue(x.Type, out var label) ? $"{label}: {x.Count}" : $"{x.Type}: {x.Count}")
                .ToList();

            return byType.Count > 0 ? string.Join("، ", byType) : "لا توجد تبعيات";
        }
    }
}
