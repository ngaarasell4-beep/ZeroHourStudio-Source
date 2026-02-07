using System.Diagnostics.CodeAnalysis;
using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Infrastructure.Helpers;

/// <summary>
/// مساعدات لمعالجة البيانات والتحويلات
/// </summary>
public static class DataProcessingHelpers
{
    /// <summary>
    /// معالجة مرجع الملف وتطبيع المسار
    /// </summary>
    public static string NormalizeFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return string.Empty;

        return Path.GetFullPath(filePath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .ToLowerInvariant();
    }

    /// <summary>
    /// استخراج اسم الملف من المسار الكامل
    /// </summary>
    public static string ExtractFileName(string filePath)
    {
        return string.IsNullOrWhiteSpace(filePath) 
            ? string.Empty 
            : Path.GetFileName(filePath);
    }

    /// <summary>
    /// التحقق من صحة ملف الصورة DDS
    /// </summary>
    public static bool IsValidDdsFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);
            
            // توقيع DDS = "DDS " (0x20534444)
            uint signature = br.ReadUInt32();
            return signature == 0x20534444;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// التحقق من صحة ملف W3D
    /// </summary>
    public static bool IsValidW3dFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);
            
            // توقيع W3D = "W3D!"
            string signature = new string(br.ReadChars(4));
            return signature == "W3D!";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// إنشاء DependencyNode من المعلومات المتعددة
    /// </summary>
    public static DependencyNode CreateDependencyNode(
        string unitId,
        string? ddsPath = null,
        string? w3dPath = null,
        string? audioPath = null)
    {
        if (string.IsNullOrWhiteSpace(unitId))
            throw new ArgumentNullException(nameof(unitId));

        return new DependencyNode
        {
            UnitId = unitId,
            DdsFilePath = NormalizeFilePath(ddsPath ?? string.Empty),
            W3dFilePath = NormalizeFilePath(w3dPath ?? string.Empty),
            AudioFilePath = NormalizeFilePath(audioPath ?? string.Empty)
        };
    }

    /// <summary>
    /// التحقق مما إذا كانت DependencyNode تحتوي على جميع الملفات المطلوبة
    /// </summary>
    public static bool HasAllRequiredFiles(DependencyNode node)
    {
        return !string.IsNullOrEmpty(node.DdsFilePath) &&
               !string.IsNullOrEmpty(node.W3dFilePath);
    }

    /// <summary>
    /// حساب حجم ملفات DependencyNode بالكامل
    /// </summary>
    public static long CalculateTotalSize(DependencyNode node)
    {
        long totalSize = 0;

        if (!string.IsNullOrEmpty(node.DdsFilePath) && File.Exists(node.DdsFilePath))
            totalSize += new FileInfo(node.DdsFilePath).Length;

        if (!string.IsNullOrEmpty(node.W3dFilePath) && File.Exists(node.W3dFilePath))
            totalSize += new FileInfo(node.W3dFilePath).Length;

        if (!string.IsNullOrEmpty(node.AudioFilePath) && File.Exists(node.AudioFilePath))
            totalSize += new FileInfo(node.AudioFilePath).Length;

        return totalSize;
    }
}
