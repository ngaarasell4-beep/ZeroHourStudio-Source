namespace ZeroHourStudio.Domain.Entities;

/// <summary>
/// يمثل عقدة الاعتماديات التي تربط الوحدة بملفاتها
/// </summary>
public class DependencyNode
{
    /// <summary>
    /// معرف فريد للعقدة
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// معرف الوحدة المرتبطة
    /// </summary>
    public string UnitId { get; set; } = string.Empty;

    /// <summary>
    /// مسار ملف DDS (النسيج)
    /// </summary>
    public string? DdsFilePath { get; set; }

    /// <summary>
    /// مسار ملف W3D (النموذج ثلاثي الأبعاد)
    /// </summary>
    public string? W3dFilePath { get; set; }

    /// <summary>
    /// مسار ملف الصوت/الموسيقى
    /// </summary>
    public string? AudioFilePath { get; set; }

    /// <summary>
    /// مسار ملف الـ INI التعريفي
    /// </summary>
    public string? IniFilePath { get; set; }

    /// <summary>
    /// تاريخ إنشاء العقدة
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// تاريخ آخر تحديث
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
