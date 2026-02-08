using System.Threading.Tasks;
using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Application.Interfaces
{
    /// <summary>
    /// واجهة خدمة تحليل الأسلحة
    /// </summary>
    public interface IWeaponAnalysisService
    {
        /// <summary>
        /// تحليل كامل لتبعيات أسلحة الوحدة
        /// </summary>
        Task<WeaponDependencyAnalysis> AnalyzeWeaponDependenciesAsync(string unitName, string modPath);
    }
}
