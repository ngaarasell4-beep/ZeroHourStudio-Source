namespace ZeroHourStudio.Application.Models;

/// <summary>
/// يمثل عقدة في الرسم البياني للتبعات
/// </summary>
public class DependencyNode
{
    /// <summary>
    /// معرف فريد للعقدة
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// اسم الملف أو المورد
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// نوع التبعية (INI, Asset, Audio, Model, Texture, etc.)
    /// </summary>
    public DependencyType Type { get; set; }

    /// <summary>
    /// المسار الكامل للمورد
    /// </summary>
    public string? FullPath { get; set; }

    /// <summary>
    /// مسار ملف الـ Texture بصيغة DDS
    /// يُستخدم لتحديد موقع ملف الـ Texture بدقة
    /// </summary>
    /// <example>Art\Textures\Crusader.dds</example>
    public string? DdsFilePath { get; set; }

    /// <summary>
    /// مسار ملف الـ 3D Model بصيغة W3D
    /// يُستخدم لتحديد موقع ملف النموذج ثلاثي الأبعاد
    /// </summary>
    /// <example>Art\W3D\Crusader.w3d</example>
    public string? W3dFilePath { get; set; }

    /// <summary>
    /// مسار ملف الصوت
    /// يمكن أن يكون WAV, MP3, أو أي صيغة صوت مدعومة
    /// </summary>
    /// <example>Data\Audio\Sounds\CrusaderMoveLoop.wav</example>
    public string? AudioFilePath { get; set; }

    /// <summary>
    /// مسار ملف الـ INI التعريفي
    /// يُستخدم للأسلحة، الدروع، والكائنات
    /// </summary>
    /// <example>Data\INI\Object\AmericaVehicle.ini</example>
    public string? IniFilePath { get; set; }

    /// <summary>
    /// حالة المورد (Found, Missing, Invalid)
    /// </summary>
    public AssetStatus Status { get; set; } = AssetStatus.Unknown;

    /// <summary>
    /// التبعات المباشرة لهذه العقدة
    /// </summary>
    public List<DependencyNode> Dependencies { get; set; } = new();

    /// <summary>
    /// مستوى العمق في الرسم البياني
    /// </summary>
    public int Depth { get; set; } = 0;

    /// <summary>
    /// ما إذا كانت هذه العقدة تم زيارتها (لتجنب الحلقات)
    /// </summary>
    public bool IsVisited { get; set; } = false;

    /// <summary>
    /// الحجم بالبايتات (إن أمكن الحصول عليه)
    /// </summary>
    public long? SizeInBytes { get; set; }

    /// <summary>
    /// تاريخ آخر تعديل
    /// </summary>
    public DateTime? LastModified { get; set; }

    public override string ToString() => $"{Name} ({Type})";
}

/// <summary>
/// أنواع التبعيات
/// </summary>
public enum DependencyType
{
    /// <summary>ملف INI للكائن الأساسي</summary>
    ObjectINI,

    /// <summary>ملف درع (Armor)</summary>
    Armor,

    /// <summary>ملف سلاح (Weapon)</summary>
    Weapon,

    /// <summary>ملف قذيفة (Projectile)</summary>
    Projectile,

    /// <summary>ملف مؤثرات صوتية (FXList)</summary>
    FXList,

    /// <summary>ملف صوت (Audio)</summary>
    Audio,

    /// <summary>نموذج ثلاثي الأبعاد (3D Model)</summary>
    Model3D,

    /// <summary>نسيج (Texture)</summary>
    Texture,

    /// <summary>تأثيرات بصرية (Visual Effects)</summary>
    VisualEffect,

    /// <summary>قائمة إنشاء كائنات (ObjectCreationList)</summary>
    OCL,

    /// <summary>محرك حركة (Locomotor)</summary>
    Locomotor,

    /// <summary>مجموعة أوامر (CommandSet / CommandButton)</summary>
    CommandSet,

    /// <summary>ترقية (Upgrade)</summary>
    Upgrade,

    /// <summary>نظام جسيمات (ParticleSystem)</summary>
    ParticleSystem,

    /// <summary>ملف مخصص آخر</summary>
    Custom
}

/// <summary>
/// حالة المورد/الملف
/// </summary>
public enum AssetStatus
{
    /// <summary>لم يتم تحديد الحالة بعد</summary>
    Unknown,

    /// <summary>المورد موجود وصحيح</summary>
    Found,

    /// <summary>المورد مفقود</summary>
    Missing,

    /// <summary>المورس موجود لكن تالف أو غير صالح</summary>
    Invalid,

    /// <summary>المورد موجود لكن لم يتم التحقق منه</summary>
    NotVerified
}
