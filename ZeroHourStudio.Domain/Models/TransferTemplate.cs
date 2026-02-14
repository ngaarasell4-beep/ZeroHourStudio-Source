using System.Text.Json.Serialization;

namespace ZeroHourStudio.Domain.Models;

/// <summary>
/// قالب نقل وحدة - يحفظ إعدادات النقل لإعادة استخدامها
/// </summary>
public class TransferTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; }

    // Transfer settings
    public string? TargetFaction { get; set; }
    public int? PreferredSlot { get; set; }
    public bool AutoSelectEmptySlot { get; set; } = true;
    public bool IncludeWeapons { get; set; } = true;
    public bool IncludeUpgrades { get; set; } = true;
    public bool IncludeAudio { get; set; } = true;
    public bool IncludeTextures { get; set; } = true;
    public bool IncludeModels { get; set; } = true;

    // History
    public List<string> UnitNames { get; set; } = new();
    public int TimesUsed { get; set; }
}
