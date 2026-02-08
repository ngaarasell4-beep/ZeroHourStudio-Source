namespace ZeroHourStudio.Application.Models;

/// <summary>
/// حزمة سلاح كاملة مرتبطة بوحدة قتالية.
/// كل عنصر إلزامي - إذا غاب واحد يُرفض السلاح بالكامل.
/// </summary>
public sealed class WeaponPackage
{
    public string OwnerUnit { get; set; } = string.Empty;
    public string Slot { get; set; } = string.Empty;           // PRIMARY / SECONDARY / TERTIARY

    // ═══ العناصر الخمسة الإلزامية ═══
    public WeaponElement Weapon { get; set; } = new();
    public WeaponElement Projectile { get; set; } = new();
    public WeaponElement DamageType { get; set; } = new();
    public WeaponElement FX { get; set; } = new();
    public WeaponElement Audio { get; set; } = new();

    // ═══ أيقونة اختيارية (فقط إذا CommandButton + ButtonImage + MappedImage) ═══
    public WeaponIconInfo? Icon { get; set; }

    // ═══ كل التبعيات المكتشفة لهذا السلاح ═══
    public List<DependencyNode> AllDependencies { get; set; } = new();

    public bool IsComplete => Weapon.Found; // السلاح موجود يكفي - البقية اختيارية

    public bool HasProjectile => Projectile.Found;
    public bool HasDamageType => DamageType.Found;
    public bool HasFX => FX.Found;
    public bool HasAudio => Audio.Found;

    public int DependencyCount => AllDependencies.Count;

    public string RejectReason
    {
        get
        {
            if (!Weapon.Found) return "Missing: Weapon";
            return $"OK (Proj:{HasProjectile}, Dmg:{HasDamageType}, FX:{HasFX}, Aud:{HasAudio})";
        }
    }
}

/// <summary>
/// عنصر واحد داخل حزمة السلاح
/// </summary>
public sealed class WeaponElement
{
    public string Name { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public bool Found { get; set; }
}

/// <summary>
/// معلومات أيقونة السلاح (اختيارية)
/// </summary>
public sealed class WeaponIconInfo
{
    public string CommandButtonName { get; set; } = string.Empty;
    public string ButtonImageName { get; set; } = string.Empty;
    public string MappedImageName { get; set; } = string.Empty;
    public string TextureFile { get; set; } = string.Empty;
    public bool IsValid { get; set; }
}

/// <summary>
/// نتيجة تحليل أسلحة وحدة كاملة
/// </summary>
public sealed class WeaponTransferManifest
{
    public string UnitName { get; set; } = string.Empty;
    public string UnitKindOf { get; set; } = string.Empty;

    // ═══ تبعيات الوحدة الأساسية (Model, Textures, Audio, Locomotor, Armor) ═══
    public List<DependencyNode> UnitDependencies { get; set; } = new();
    public string LocomotorName { get; set; } = string.Empty;
    public string ArmorName { get; set; } = string.Empty;
    public string CommandSetName { get; set; } = string.Empty;
    public string ButtonImageName { get; set; } = string.Empty;

    // ═══ أسلحة الوحدة ═══
    public List<WeaponPackage> AcceptedWeapons { get; set; } = new();
    public List<WeaponPackage> RejectedWeapons { get; set; } = new();

    public int TotalDependencies => UnitDependencies.Count + AcceptedWeapons.Sum(w => w.DependencyCount);
    public int WeaponCount => AcceptedWeapons.Count;
    public bool IsTransferable => AcceptedWeapons.Count > 0 && TotalDependencies <= 80;

    public string Summary =>
        $"{UnitName}: {AcceptedWeapons.Count} weapons, {UnitDependencies.Count} unit deps, {TotalDependencies} total";
}
