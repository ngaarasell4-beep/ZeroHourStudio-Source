using System.Text.Json;
using ZeroHourStudio.Domain.Models;

namespace ZeroHourStudio.Infrastructure.Profiles;

/// <summary>
/// حفظ وتحميل ملفات التعريف السريعة (مسار المصدر، الهدف، الفصيل).
/// </summary>
public class TransferProfileService
{
    private readonly string _profilesDir;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TransferProfileService()
    {
        _profilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZeroHourStudio", "Profiles");
        Directory.CreateDirectory(_profilesDir);
    }

    public async Task SaveAsync(TransferProfile profile)
    {
        profile.LastUsedAt = DateTime.UtcNow;
        var path = Path.Combine(_profilesDir, $"{profile.Id}.json");
        var json = JsonSerializer.Serialize(profile, JsonOpts);
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<List<TransferProfile>> LoadAllAsync()
    {
        var list = new List<TransferProfile>();
        if (!Directory.Exists(_profilesDir)) return list;

        foreach (var file in Directory.GetFiles(_profilesDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var p = JsonSerializer.Deserialize<TransferProfile>(json, JsonOpts);
                if (p != null) list.Add(p);
            }
            catch { /* ignore corrupted */ }
        }

        return list.OrderByDescending(p => p.LastUsedAt).ToList();
    }

    public async Task DeleteAsync(string profileId)
    {
        var path = Path.Combine(_profilesDir, $"{profileId}.json");
        if (File.Exists(path))
            File.Delete(path);
        await Task.CompletedTask;
    }
}
