using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Infrastructure.DependencyResolution;

/// <summary>
/// واجهة لحل التبعيات بذكاء
/// يقوم بتحليل وربط جميع التبعيات المطلوبة لوحدة معينة
/// </summary>
public interface IDependencyResolver
{
    /// <summary>
    /// حل جميع التبعيات لوحدة معينة
    /// </summary>
    /// <param name="unitName">اسم الوحدة</param>
    /// <param name="sourceModPath">مسار المود المصدر</param>
    /// <returns>رسم بياني كامل للتبعيات</returns>
    Task<UnitDependencyGraph> ResolveDependenciesAsync(string unitName, string sourceModPath);

    /// <summary>
    /// التحقق من وجود جميع التبعيات
    /// </summary>
    Task<bool> ValidateDependenciesAsync(UnitDependencyGraph graph, string sourceModPath);

    /// <summary>
    /// الحصول على قائمة الملفات التي يجب نقلها
    /// </summary>
    Task<List<DependencyNode>> GetFilesToTransferAsync(UnitDependencyGraph graph);
}
