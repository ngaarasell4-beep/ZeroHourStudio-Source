namespace ZeroHourStudio.Domain.Models;

/// <summary>
/// ملف تعريف سريع لحفظ مسار المصدر والهدف والفصيل لاستخدامه بنقرة واحدة.
/// </summary>
public class TransferProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string SourceModPath { get; set; } = string.Empty;
    public string TargetModPath { get; set; } = string.Empty;
    public string? TargetFaction { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}
