namespace ZeroHourStudio.Infrastructure.Transfer;

/// <summary>
/// نتيجة عملية التراجع
/// </summary>
public class RollbackResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int RestoredFiles { get; set; }
    public int DeletedFiles { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// خدمة التراجع - تعكس عملية نقل كاملة بناءً على سجل TransferJournal
/// </summary>
public class RollbackService
{
    /// <summary>
    /// تنفيذ التراجع الكامل عن عملية نقل
    /// </summary>
    public async Task<RollbackResult> RollbackAsync(
        TransferJournalEntry entry,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        var result = new RollbackResult();
        var total = entry.Operations.Count;

        if (entry.IsRolledBack)
        {
            result.Success = false;
            result.Message = "تم التراجع عن هذه العملية سابقاً";
            return result;
        }

        // التراجع بالترتيب العكسي (LIFO)
        var reversedOps = entry.Operations.AsEnumerable().Reverse().ToList();

        for (int i = 0; i < reversedOps.Count; i++)
        {
            var op = reversedOps[i];
            progress?.Report((i + 1, total, $"تراجع: {Path.GetFileName(op.TargetPath)}"));

            try
            {
                switch (op.OperationType)
                {
                    case "FileCopy":
                        RollbackFileCopy(op, result);
                        break;

                    case "IniModification":
                        RollbackIniModification(op, result);
                        break;

                    case "FileCreated":
                        RollbackFileCreated(op, result);
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"خطأ في التراجع عن {op.TargetPath}: {ex.Message}");
            }
        }

        // تنظيف المجلدات الفارغة
        CleanupEmptyDirectories(entry.TargetModPath);

        // تحديث السجل
        var journal = new TransferJournal(entry.TargetModPath);
        await journal.MarkAsRolledBackAsync(entry);

        result.Success = result.Errors.Count == 0;
        result.Message = result.Success
            ? $"✅ تم التراجع بنجاح - حُذف {result.DeletedFiles} ملف، استُعيد {result.RestoredFiles} ملف"
            : $"⚠ تم التراجع مع {result.Errors.Count} خطأ";

        return result;
    }

    private void RollbackFileCopy(JournalOperation op, RollbackResult result)
    {
        if (op.WasOverwritten && !string.IsNullOrEmpty(op.BackupPath) && File.Exists(op.BackupPath))
        {
            // استعادة النسخة الاحتياطية
            File.Copy(op.BackupPath, op.TargetPath, true);
            result.RestoredFiles++;
        }
        else if (!op.WasOverwritten && File.Exists(op.TargetPath))
        {
            // حذف الملف المنسوخ (لم يكن موجوداً قبل النقل)
            File.Delete(op.TargetPath);
            result.DeletedFiles++;
        }
    }

    private void RollbackIniModification(JournalOperation op, RollbackResult result)
    {
        if (!string.IsNullOrEmpty(op.BackupPath) && File.Exists(op.BackupPath))
        {
            File.Copy(op.BackupPath, op.TargetPath, true);
            result.RestoredFiles++;
        }
    }

    private void RollbackFileCreated(JournalOperation op, RollbackResult result)
    {
        if (File.Exists(op.TargetPath))
        {
            File.Delete(op.TargetPath);
            result.DeletedFiles++;
        }
    }

    /// <summary>
    /// تنظيف المجلدات الفارغة بعد الحذف
    /// </summary>
    private void CleanupEmptyDirectories(string rootPath)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length))
            {
                if (Directory.Exists(dir) &&
                    !Directory.EnumerateFileSystemEntries(dir).Any() &&
                    !dir.Contains(".zhs_journal"))
                {
                    Directory.Delete(dir);
                }
            }
        }
        catch
        {
            // ليست حرجة
        }
    }

    /// <summary>
    /// الحصول على ملخص عملية التراجع قبل تنفيذها
    /// </summary>
    public RollbackPreview PreviewRollback(TransferJournalEntry entry)
    {
        var preview = new RollbackPreview
        {
            UnitName = entry.UnitName,
            TransferDate = entry.Timestamp,
            TotalOperations = entry.Operations.Count
        };

        foreach (var op in entry.Operations)
        {
            switch (op.OperationType)
            {
                case "FileCopy" when op.WasOverwritten:
                    preview.FilesToRestore++;
                    break;
                case "FileCopy" when !op.WasOverwritten:
                    preview.FilesToDelete++;
                    break;
                case "FileCreated":
                    preview.FilesToDelete++;
                    break;
                case "IniModification":
                    preview.FilesToRestore++;
                    break;
            }
        }

        return preview;
    }
}

/// <summary>
/// معاينة عملية التراجع
/// </summary>
public class RollbackPreview
{
    public string UnitName { get; set; } = string.Empty;
    public DateTime TransferDate { get; set; }
    public int TotalOperations { get; set; }
    public int FilesToDelete { get; set; }
    public int FilesToRestore { get; set; }

    public string Summary =>
        $"سيتم حذف {FilesToDelete} ملف واستعادة {FilesToRestore} ملف";
}
