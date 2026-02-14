using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Infrastructure.Localization;

/// <summary>
/// خدمة التوطين CSF - تجمع بين القارئ والكاتب وتوليد المدخلات القياسية
/// </summary>
public class CsfLocalizationService : ICsfLocalizationService
{
    private readonly CsfFileReader _reader;
    private readonly CsfFileWriter _writer;

    public CsfLocalizationService()
    {
        _reader = new CsfFileReader();
        _writer = new CsfFileWriter();
    }

    public Task<List<CsfEntry>> ReadCsfAsync(string csfFilePath)
    {
        return _reader.ReadAsync(csfFilePath);
    }

    public Task WriteCsfAsync(string csfFilePath, List<CsfEntry> entries)
    {
        return _writer.WriteAsync(csfFilePath, entries);
    }

    /// <summary>
    /// توليد مدخلات CSF القياسية لوحدة SAGE
    /// </summary>
    public List<CsfEntry> GenerateEntriesForUnit(string unitName, string displayName, string description = "")
    {
        var entries = new List<CsfEntry>();

        // OBJECT: - اسم الوحدة الظاهر
        entries.Add(new CsfEntry($"OBJECT:{unitName}", displayName));

        // CONTROLBAR: - نص زر الأوامر
        entries.Add(new CsfEntry($"CONTROLBAR:Command_{unitName}", $"Build {displayName}"));

        // وصف الوحدة
        if (!string.IsNullOrEmpty(description))
        {
            entries.Add(new CsfEntry($"OBJECT:{unitName}_DESC", description));
        }

        return entries;
    }

    /// <summary>
    /// دمج مدخلات جديدة في ملف CSF موجود
    /// </summary>
    public async Task MergeEntriesAsync(string csfFilePath, List<CsfEntry> newEntries)
    {
        var existing = await ReadCsfAsync(csfFilePath);
        var labelIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < existing.Count; i++)
        {
            labelIndex[existing[i].Label] = i;
        }

        foreach (var entry in newEntries)
        {
            if (labelIndex.TryGetValue(entry.Label, out var idx))
            {
                // تحديث المدخل الموجود
                existing[idx] = entry;
            }
            else
            {
                // إضافة مدخل جديد
                existing.Add(entry);
            }
        }

        await WriteCsfAsync(csfFilePath, existing);
    }
}
