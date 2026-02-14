using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Application.Interfaces;

public interface IConflictResolutionService
{
    /// <summary>
    /// كشف التعارضات بين المود المصدر والمود الهدف
    /// </summary>
    Task<ConflictReport> DetectConflictsAsync(
        UnitDependencyGraph sourceGraph,
        string targetModPath);

    /// <summary>
    /// إنشاء خريطة إعادة التسمية الذكية
    /// </summary>
    Task<Dictionary<string, string>> GenerateRenameMapAsync(ConflictReport report);

    /// <summary>
    /// تطبيق إعادة التسمية على محتوى INI
    /// </summary>
    Task<string> ApplyRenamesAsync(string iniContent, Dictionary<string, string> renameMap);
}
