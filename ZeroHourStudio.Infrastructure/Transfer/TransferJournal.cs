using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroHourStudio.Infrastructure.Transfer;

/// <summary>
/// عملية واحدة في سجل النقل (ملف منسوخ، تعديل INI، إلخ)
/// </summary>
public class JournalOperation
{
    /// <summary>نوع العملية</summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>المسار المتأثر</summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>المسار المصدر</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>مسار النسخة الاحتياطية (إن وجدت)</summary>
    public string? BackupPath { get; set; }

    /// <summary>هل الملف كان موجوداً سابقاً (تم الكتابة فوقه)</summary>
    public bool WasOverwritten { get; set; }

    /// <summary>ملاحظات</summary>
    public string? Note { get; set; }
}

/// <summary>
/// مدخل واحد في سجل النقل - يمثل عملية نقل وحدة كاملة
/// </summary>
public class TransferJournalEntry
{
    /// <summary>معرف فريد</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>اسم الوحدة المنقولة</summary>
    public string UnitName { get; set; } = string.Empty;

    /// <summary>الفصيل المصدر</summary>
    public string SourceFaction { get; set; } = string.Empty;

    /// <summary>الفصيل الهدف</summary>
    public string TargetFaction { get; set; } = string.Empty;

    /// <summary>مسار المود المصدر</summary>
    public string SourceModPath { get; set; } = string.Empty;

    /// <summary>مسار المود الهدف</summary>
    public string TargetModPath { get; set; } = string.Empty;

    /// <summary>تاريخ النقل</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>مدة النقل</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>عدد الملفات المنقولة</summary>
    public int FilesTransferred { get; set; }

    /// <summary>إعادة التسميات المطبقة</summary>
    public Dictionary<string, string> AppliedRenames { get; set; } = new();

    /// <summary>العمليات الفردية</summary>
    public List<JournalOperation> Operations { get; set; } = new();

    /// <summary>هل تم التراجع عنها</summary>
    public bool IsRolledBack { get; set; }

    /// <summary>تاريخ التراجع</summary>
    public DateTime? RolledBackAt { get; set; }
}

/// <summary>
/// سجل النقل - يحفظ تاريخ جميع عمليات النقل مع القدرة على التراجع
/// </summary>
public class TransferJournal
{
    private readonly string _journalDir;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TransferJournal(string targetModPath)
    {
        if (string.IsNullOrWhiteSpace(targetModPath))
            throw new ArgumentException("مسار المود الهدف مطلوب.", nameof(targetModPath));
        _journalDir = Path.Combine(targetModPath, "Data", ".zhs_journal");
        Directory.CreateDirectory(_journalDir);
        Directory.CreateDirectory(Path.Combine(_journalDir, "backups"));
    }

    /// <summary>
    /// بدء تسجيل عملية نقل جديدة
    /// </summary>
    public TransferJournalEntry BeginTransfer(string unitName, string sourceFaction,
        string targetFaction, string sourceModPath, string targetModPath)
    {
        return new TransferJournalEntry
        {
            UnitName = unitName,
            SourceFaction = sourceFaction,
            TargetFaction = targetFaction,
            SourceModPath = sourceModPath,
            TargetModPath = targetModPath,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// تسجيل عملية نسخ ملف
    /// </summary>
    public void RecordFileCopy(TransferJournalEntry entry, string sourcePath, string targetPath)
    {
        string? backupPath = null;

        // إذا كان الملف الهدف موجوداً، ننسخ احتياطياً
        if (File.Exists(targetPath))
        {
            var backupDir = Path.Combine(_journalDir, "backups", entry.Id);
            Directory.CreateDirectory(backupDir);
            var relativePath = Path.GetRelativePath(entry.TargetModPath, targetPath);
            backupPath = Path.Combine(backupDir, relativePath.Replace(Path.DirectorySeparatorChar, '_'));
            File.Copy(targetPath, backupPath, true);
        }

        entry.Operations.Add(new JournalOperation
        {
            OperationType = "FileCopy",
            SourcePath = sourcePath,
            TargetPath = targetPath,
            BackupPath = backupPath,
            WasOverwritten = backupPath != null
        });
    }

    /// <summary>
    /// تسجيل تعديل على ملف INI
    /// </summary>
    public void RecordIniModification(TransferJournalEntry entry, string filePath, string description)
    {
        // نحفظ نسخة احتياطية
        string? backupPath = null;
        if (File.Exists(filePath))
        {
            var backupDir = Path.Combine(_journalDir, "backups", entry.Id);
            Directory.CreateDirectory(backupDir);
            var fileName = $"ini_mod_{entry.Operations.Count}_{Path.GetFileName(filePath)}";
            backupPath = Path.Combine(backupDir, fileName);
            File.Copy(filePath, backupPath, true);
        }

        entry.Operations.Add(new JournalOperation
        {
            OperationType = "IniModification",
            TargetPath = filePath,
            BackupPath = backupPath,
            WasOverwritten = true,
            Note = description
        });
    }

    /// <summary>
    /// تسجيل ملف تم إنشاؤه (مثل CommandSet patch)
    /// </summary>
    public void RecordFileCreated(TransferJournalEntry entry, string filePath, string description)
    {
        entry.Operations.Add(new JournalOperation
        {
            OperationType = "FileCreated",
            TargetPath = filePath,
            WasOverwritten = false,
            Note = description
        });
    }

    /// <summary>
    /// حفظ مدخل السجل بعد اكتمال النقل
    /// </summary>
    public async Task SaveEntryAsync(TransferJournalEntry entry)
    {
        entry.Duration = DateTime.UtcNow - entry.Timestamp;
        var filePath = Path.Combine(_journalDir, $"transfer_{entry.Id}_{entry.UnitName}.json");
        var json = JsonSerializer.Serialize(entry, JsonOpts);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// تحميل جميع مدخلات السجل
    /// </summary>
    public async Task<List<TransferJournalEntry>> LoadAllEntriesAsync()
    {
        var entries = new List<TransferJournalEntry>();

        if (!Directory.Exists(_journalDir))
            return entries;

        foreach (var file in Directory.GetFiles(_journalDir, "transfer_*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var entry = JsonSerializer.Deserialize<TransferJournalEntry>(json, JsonOpts);
                if (entry != null) entries.Add(entry);
            }
            catch
            {
                // تجاهل الملفات التالفة
            }
        }

        return entries.OrderByDescending(e => e.Timestamp).ToList();
    }

    /// <summary>
    /// تحميل مدخل واحد
    /// </summary>
    public async Task<TransferJournalEntry?> LoadEntryAsync(string entryId)
    {
        var entries = await LoadAllEntriesAsync();
        return entries.FirstOrDefault(e => e.Id == entryId);
    }

    /// <summary>
    /// تحديث حالة التراجع
    /// </summary>
    public async Task MarkAsRolledBackAsync(TransferJournalEntry entry)
    {
        entry.IsRolledBack = true;
        entry.RolledBackAt = DateTime.UtcNow;
        await SaveEntryAsync(entry);
    }

    /// <summary>
    /// تصدير كل مدخلات السجل إلى ملف JSON واحد (نسخ احتياطي أو مشاركة).
    /// </summary>
    public async Task ExportToFileAsync(string filePath)
    {
        var entries = await LoadAllEntriesAsync();
        var wrapper = new { ExportedAt = DateTime.UtcNow, Count = entries.Count, Entries = entries };
        var json = JsonSerializer.Serialize(wrapper, JsonOpts);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// استيراد مدخلات من ملف JSON مُصدَّر مسبقاً (للعرض أو الاستعادة).
    /// </summary>
    public static async Task<List<TransferJournalEntry>> ImportFromFileAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = new List<TransferJournalEntry>();
        if (root.TryGetProperty("entries", out var arr))
        {
            foreach (var el in arr.EnumerateArray())
            {
                var entry = JsonSerializer.Deserialize<TransferJournalEntry>(el.GetRawText(), JsonOpts);
                if (entry != null) list.Add(entry);
            }
        }
        return list;
    }
}
