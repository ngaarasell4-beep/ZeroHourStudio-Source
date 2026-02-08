using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Application.Models
{
    /// <summary>
    /// نموذج محسّن لعرض تبعيات السلاح بالكامل
    /// </summary>
    public class WeaponDependencyAnalysis
    {
        public string UnitName { get; set; } = string.Empty;
        public string Faction { get; set; } = string.Empty;
        public List<WeaponChain> Weapons { get; set; } = new();
        public List<string> ProjectileTypes { get; set; } = new();
        public List<string> DamageTypes { get; set; } = new();
        public List<string> AudioFiles { get; set; } = new();
        public List<string> VisualEffects { get; set; } = new();
        public int TotalDependencies => Weapons.Count + ProjectileTypes.Count + DamageTypes.Count + AudioFiles.Count + VisualEffects.Count;
        public int FoundDependencies { get; set; }
        public int MissingDependencies { get; set; }
        public double CompletionPercentage => TotalDependencies > 0 ? (double)FoundDependencies / TotalDependencies * 100 : 0;
        public string Status => CompletionPercentage >= 95 ? "مكتمل" : CompletionPercentage >= 80 ? "جيد" : "غير مكتمل";
        public bool IsComplete => CompletionPercentage >= 95;
    }

    /// <summary>
    /// يمثل سلسلة سلاح كاملة
    /// </summary>
    public class WeaponChain
    {
        public string WeaponName { get; set; } = string.Empty;
        public string WeaponType { get; set; } = string.Empty; // Primary, Secondary, etc.
        public string ProjectileName { get; set; } = string.Empty;
        public string DamageType { get; set; } = string.Empty;
        public double Damage { get; set; }
        public double Range { get; set; }
        public double FireRate { get; set; }
        public string? AudioFire { get; set; }
        public string? AudioExplosion { get; set; }
        public string? VisualEffect { get; set; }
        public string? ModelFile { get; set; }
        public List<string> RelatedFiles { get; set; } = new();
        public bool IsComplete { get; set; }
        public List<string> MissingFiles { get; set; } = new();
    }

    /// <summary>
    /// نموذج محسّن لعرض تبعيات الوحدة
    /// </summary>
    public class EnhancedDependencyGraph : UnitDependencyGraph
    {
        public WeaponDependencyAnalysis? WeaponAnalysis { get; set; }
        public List<WeaponChain> WeaponChains { get; set; } = new();
        public int WeaponAnalysisCount => WeaponChains.Count;
        public int DependencyCount => AllNodes.Count;
        public int AnalysisCount => WeaponChains.Count + (WeaponAnalysis?.TotalDependencies ?? 0);
        
        /// <summary>
        /// عدد الأسلحة المرتبطة بالوحدة
        /// </summary>
        public int WeaponCount => WeaponChains.Count;
        
        /// <summary>
        /// عدد ملفات الصوت المرتبطة
        /// </summary>
        public int AudioCount => AllNodes.Count(n => n.Type == DependencyType.Audio);
        
        /// <summary>
        /// عدد ملفات الرسوميات المرتبطة
        /// </summary>
        public int VisualCount => AllNodes.Count(n => n.Type == DependencyType.Model3D || n.Type == DependencyType.Texture);
        
        /// <summary>
        /// عدد ملفات التأثيرات المرتبطة
        /// </summary>
        public int EffectCount => AllNodes.Count(n => n.Type == DependencyType.VisualEffect || n.Type == DependencyType.FXList);
    }
}