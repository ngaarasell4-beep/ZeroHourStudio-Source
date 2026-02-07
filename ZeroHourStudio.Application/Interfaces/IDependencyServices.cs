using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Application.Interfaces;

/// <summary>
/// واجهة لتحليل تبعيات الوحدة
/// </summary>
public interface IUnitDependencyAnalyzer
{
    Task<UnitDependencyGraph> AnalyzeDependenciesAsync(
        string unitId,
        string unitName,
        Dictionary<string, string> unitData);

    List<string> GetDependencyPathsAsText(UnitDependencyGraph graph);
}

/// <summary>
/// واجهة للتحقق من اكتمال الوحدة
/// </summary>
public interface IUnitCompletionValidator
{
    ValidationResult ValidateUnitCompletion(
        string unitId,
        UnitDependencyGraph dependencyGraph,
        Dictionary<string, bool>? additionalChecks = null);

    CompletionStatus EvaluateCompletionStatus(UnitDependencyGraph graph);

    string GenerateDetailedReport(ValidationResult validationResult, UnitDependencyGraph? graph);
}
