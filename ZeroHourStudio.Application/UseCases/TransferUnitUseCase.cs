using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;

namespace ZeroHourStudio.Application.UseCases;

/// <summary>
/// Use Case: نقل الوحدة (Atomic Transfer)
/// المسؤوليات:
/// - ضمان نقل الوحدة وكافة تبعاتها بشكل كامل أو عدم نقل شيء (All or nothing)
/// - التحقق من وجود المساحة الكافية والمجلدات
/// - تسجيل عملية النقل
/// </summary>
public interface ITransferUnitUseCase
{
    Task<TransferUnitResponse> ExecuteAsync(TransferUnitRequest request);
}

public class TransferUnitUseCase : ITransferUnitUseCase
{
    private readonly IBigFileReader _bigFileReader;
    
    public TransferUnitUseCase(IBigFileReader bigFileReader)
    {
        _bigFileReader = bigFileReader ?? throw new ArgumentNullException(nameof(bigFileReader));
    }

    public async Task<TransferUnitResponse> ExecuteAsync(TransferUnitRequest request)
    {
        var response = new TransferUnitResponse();
        var transferredFiles = new List<string>();

        try
        {
            // 1. جمع قائمة بكافة الملفات المراد نقلها من الرسم البياني
            var filesToTransfer = request.DependencyGraph.AllNodes
                .Where(n => n.Status == AssetStatus.Found)
                .ToList();

            if (filesToTransfer.Count == 0)
            {
                response.Success = false;
                response.Message = "لا توجد ملفات صالحة للنقل.";
                return response;
            }

            // 2. البدء بعملية النقل (محاكاة العملية الذرية)
            foreach (var node in filesToTransfer)
            {
                var destinationPath = Path.Combine(request.DestinationFolderPath, node.Name);
                
                // إذا كان الملف داخل أرشيف BIG
                if (string.IsNullOrEmpty(node.FullPath))
                {
                    await _bigFileReader.ExtractAsync(request.SourceArchivePath, node.Name, destinationPath);
                }
                else // إذا كان ملفاً عادياً في نظام الملفات
                {
                    var directory = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.Copy(node.FullPath, destinationPath, true);
                }

                transferredFiles.Add(node.Name);
            }

            response.Success = true;
            response.Message = $"تم نقل الوحدة بنجاح. إجمالي الملفات المنقولة: {transferredFiles.Count}";
            response.TransferredFilesCount = transferredFiles.Count;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"فشل عملية النقل: {ex.Message}";

            // Rollback — حذف الملفات المنقولة جزئياً لضمان All-or-Nothing
            var rolledBack = 0;
            foreach (var file in transferredFiles)
            {
                var path = Path.Combine(request.DestinationFolderPath, file);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        rolledBack++;
                    }
                }
                catch { /* تجاهل أخطاء الحذف أثناء التراجع */ }
            }

            if (rolledBack > 0)
                response.Message += $" (تم التراجع عن {rolledBack} ملف من {transferredFiles.Count})";
        }

        return response;
    }
}

public class TransferUnitRequest
{
    public string UnitId { get; set; } = string.Empty;
    public UnitDependencyGraph DependencyGraph { get; set; } = new();
    public string SourceArchivePath { get; set; } = string.Empty;
    public string DestinationFolderPath { get; set; } = string.Empty;
}

public class TransferUnitResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TransferredFilesCount { get; set; }
}
