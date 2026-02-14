using System.Text.Json;
using ZeroHourStudio.Domain.Models;

namespace ZeroHourStudio.Infrastructure.Templates;

/// <summary>
/// مدير قوالب النقل - حفظ/تحميل/تطبيق قوالب النقل
/// </summary>
public class TransferTemplateManager
{
    private readonly string _templatesDir;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TransferTemplateManager(string? appDataDir = null)
    {
        _templatesDir = appDataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZeroHourStudio", "Templates");
        Directory.CreateDirectory(_templatesDir);
    }

    /// <summary>
    /// حفظ قالب جديد
    /// </summary>
    public async Task SaveTemplateAsync(TransferTemplate template)
    {
        var filePath = Path.Combine(_templatesDir, $"{template.Id}.json");
        var json = JsonSerializer.Serialize(template, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        System.Diagnostics.Debug.WriteLine($"[TemplateManager] Saved template '{template.Name}' to {filePath}");
    }

    /// <summary>
    /// تحميل جميع القوالب
    /// </summary>
    public async Task<List<TransferTemplate>> LoadTemplatesAsync()
    {
        var templates = new List<TransferTemplate>();
        try
        {
            if (!Directory.Exists(_templatesDir))
                return templates;

            foreach (var file in Directory.GetFiles(_templatesDir, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var template = JsonSerializer.Deserialize<TransferTemplate>(json, _jsonOptions);
                    if (template != null)
                        templates.Add(template);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TemplateManager] Error loading {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TemplateManager] LoadTemplates ERROR: {ex.Message}");
        }

        return templates.OrderByDescending(t => t.LastUsedAt).ToList();
    }

    /// <summary>
    /// حذف قالب
    /// </summary>
    public Task DeleteTemplateAsync(string templateId)
    {
        var filePath = Path.Combine(_templatesDir, $"{templateId}.json");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            System.Diagnostics.Debug.WriteLine($"[TemplateManager] Deleted template {templateId}");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// تحديث آخر استخدام للقالب
    /// </summary>
    public async Task MarkTemplateUsedAsync(TransferTemplate template, string unitName)
    {
        template.LastUsedAt = DateTime.Now;
        template.TimesUsed++;
        if (!template.UnitNames.Contains(unitName, StringComparer.OrdinalIgnoreCase))
            template.UnitNames.Add(unitName);
        await SaveTemplateAsync(template);
    }
}
