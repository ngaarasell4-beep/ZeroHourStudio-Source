using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Infrastructure.Transfer;

/// <summary>
/// خدمة النقل الذكي
/// تدير عملية نقل الوحدات والملفات بكفاءة عالية
/// </summary>
public class SmartTransferService : ISmartTransferService
{
    private const int BufferSize = 64 * 1024; // 64KB buffer

    /// <summary>
    /// نقل وحدة وجميع تبعياتها بذكاء
    /// </summary>
    public async Task<SmartTransferResult> TransferAsync(
        UnitDependencyGraph graph,
        string sourcePath,
        string destinationPath,
        IProgress<TransferProgress>? progress = null)
    {
        var startTime = DateTime.UtcNow;
        var result = new SmartTransferResult();
        var transferProgress = new TransferProgress();

        try
        {
            // 1. التحقق من المسارات
            if (!Directory.Exists(sourcePath))
            {
                result.Success = false;
                result.Message = $"مسار المصدر غير موجود: {sourcePath}";
                return result;
            }

            // 2. إنشاء مجلد الهدف إن لم يكن موجوداً
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }

            // 3. الحصول على قائمة الملفات للنقل
            var filesToTransfer = graph.AllNodes
                .Where(n => n.Status == AssetStatus.Found && n.FullPath != null)
                .OrderBy(n => n.Depth)
                .ToList();

            if (filesToTransfer.Count == 0)
            {
                result.Success = false;
                result.Message = "لا توجد ملفات للنقل";
                return result;
            }

            // 4. حساب الحجم الإجمالي
            transferProgress.TotalFiles = filesToTransfer.Count;
            transferProgress.TotalBytes = filesToTransfer.Sum(f => f.SizeInBytes ?? 0);

            // 5. بدء عملية النقل
            for (int i = 0; i < filesToTransfer.Count; i++)
            {
                var fileNode = filesToTransfer[i];
                transferProgress.CurrentFileIndex = i + 1;
                transferProgress.CurrentFileName = fileNode.Name;

                try
                {
                    // نقل الملف مع الحفاظ على هيكل المجلدات
                    var sourceFullPath = fileNode.FullPath ?? throw new InvalidOperationException($"ملف بدون مسار: {fileNode.Name}");
                    var relativePath = GetRelativePath(sourceFullPath, sourcePath);
                    var targetPath = Path.Combine(destinationPath, relativePath);

                    // إنشاء المجلد الهدف إن لزم الأمر
                    var targetDirectory = Path.GetDirectoryName(targetPath);
                    if (targetDirectory != null && !Directory.Exists(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                    }

                    // نقل الملف بكفاءة
                    await CopyFileAsync(sourceFullPath, targetPath, transferProgress, progress);

                    result.TransferredFilesCount++;
                    result.TransferredBytesCount += fileNode.SizeInBytes ?? 0;
                }
                catch (Exception ex)
                {
                    result.FailedFiles.Add($"{fileNode.Name}: {ex.Message}");
                }
            }

            result.Success = result.FailedFiles.Count == 0;
            result.Message = result.Success
                ? $"تم نقل {result.TransferredFilesCount} ملف بنجاح"
                : $"تم نقل {result.TransferredFilesCount} ملف مع فشل {result.FailedFiles.Count} ملف";

            result.Duration = DateTime.UtcNow - startTime;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"خطأ في عملية النقل: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// نسخ ملف مع تحديث التقدم
    /// </summary>
    private async Task CopyFileAsync(
        string sourceFile,
        string destinationFile,
        TransferProgress progress,
        IProgress<TransferProgress>? progressReporter)
    {
        using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
        using (var destinationStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            byte[] buffer = new byte[BufferSize];
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await destinationStream.WriteAsync(buffer, 0, bytesRead);
                progress.TransferredBytes += bytesRead;

                // إخطار المراقب بالتقدم
                progressReporter?.Report(progress);

                // حفظ تاريخ التعديل
                File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
            }
        }
    }

    /// <summary>
    /// الحصول على المسار النسبي
    /// </summary>
    private string GetRelativePath(string fullPath, string basePath)
    {
        if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        return Path.GetFileName(fullPath);
    }
}
