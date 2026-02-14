namespace ZeroHourStudio.Domain.Entities;

/// <summary>
/// يمثل فصيلاً واحداً كما يراه محرك SAGE - لا بيانات وهمية.
/// InternalName: الاسم من INI (مثل "FactionAmericaSuperWeapon")
/// DisplayName: الاسم المترجم من CSF (مثل "General Alexander") أو null إذا لا يوجد
/// Side: الجانب الأساسي (America / China / GLA / أخرى)
/// </summary>
public class FactionInfo
{
    /// <summary>الاسم الداخلي كما ورد في PlayerTemplate أو Side= في Object INI</summary>
    public string InternalName { get; set; } = string.Empty;

    /// <summary>الاسم المعروض من CSF (DisplayName label). null إذا لم يُعثر عليه.</summary>
    public string? DisplayName { get; set; }

    /// <summary>الجانب الأساسي (America, China, GLA) — مستخرج من PlayerTemplate.Side أو Side= في Object.</summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>هل هذا فصيل لاعب (Playable) أم فصيل نظام (Observer/Civilian)؟</summary>
    public bool IsPlayable { get; set; } = true;

    /// <summary>مصدر الاكتشاف — أي ملف/أرشيف أُستخرج منه هذا الفصيل</summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>عدد الوحدات القتالية المُكتشفة لهذا الفصيل</summary>
    public int UnitCount { get; set; }

    /// <summary>الاسم المعروض: DisplayName إن وُجد، وإلا InternalName</summary>
    public string ResolvedName => DisplayName ?? InternalName;

    public override string ToString() => ResolvedName;
}

/// <summary>
/// نتيجة اكتشاف الفصائل — حالة حية وليست بيانات مزيفة
/// </summary>
public class FactionDiscoveryResult
{
    public List<FactionInfo> Factions { get; set; } = new();
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public FactionDiscoverySource Source { get; set; }
    public int FilesScanned { get; set; }

    /// <summary>أسماء الفصائل الداخلية فقط</summary>
    public List<string> InternalNames => Factions.Select(f => f.InternalName).ToList();

    /// <summary>أسماء العرض (مع Fallback للاسم الداخلي)</summary>
    public List<string> ResolvedNames => Factions.Select(f => f.ResolvedName).ToList();
}

public enum FactionDiscoverySource
{
    /// <summary>لم يتم الاكتشاف بعد</summary>
    None,
    /// <summary>من PlayerTemplate INI</summary>
    PlayerTemplate,
    /// <summary>من Side= في Object definitions</summary>
    ObjectSide,
    /// <summary>من CommandSet analysis</summary>
    CommandSet,
    /// <summary>من أرشيفات BIG</summary>
    BigArchive,
}
