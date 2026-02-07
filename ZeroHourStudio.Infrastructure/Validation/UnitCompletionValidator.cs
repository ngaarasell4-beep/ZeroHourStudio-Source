using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;

namespace ZeroHourStudio.Infrastructure.Validation;

/// <summary>
/// خدمة التحقق من اكتمال وسلامة الوحدات
/// تفحص الوحدات للتأكد من:
/// - وجود جميع الملفات الحرجة (خاصة W3D)
/// - وجود CommandSet الصحيح
/// - عدم وجود مراجع معطوبة
/// </summary>
public class UnitCompletionValidator : IUnitCompletionValidator
{
    private readonly HashSet<string> _requiredFileTypes;
    private readonly HashSet<string> _optionalFileTypes;

    public UnitCompletionValidator()
    {
        // الملفات الحرجة (يجب تواجدها)
        _requiredFileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".w3d",  // النموذج ثلاثي الأبعاد
        };

        // الملفات الاختيارية
        _optionalFileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dds",  // النسيج
            ".tga",  // النسيج البديل
            ".wav",  // الصوت
            ".mp3",  // الصوت البديل
        };
    }

    /// <summary>
    /// التحقق من اكتمال الوحدة
    /// </summary>
    public ValidationResult ValidateUnitCompletion(
        string unitId,
        UnitDependencyGraph dependencyGraph,
        Dictionary<string, bool>? additionalChecks = null)
    {
        if (string.IsNullOrEmpty(unitId))
            throw new ArgumentNullException(nameof(unitId));

        if (dependencyGraph == null)
            throw new ArgumentNullException(nameof(dependencyGraph));

        var result = new ValidationResult
        {
            UnitId = unitId,
            IsValid = true
        };

        // الفحص الأول: البحث عن الملفات الحرجة المفقودة
        CheckCriticalFiles(dependencyGraph, result);

        // الفحص الثاني: البحث عن الملفات الاختيارية المفقودة
        CheckOptionalFiles(dependencyGraph, result);

        // الفحص الثالث: التحقق من CommandSet
        if (additionalChecks != null && additionalChecks.ContainsKey("CommandSet"))
        {
            CheckCommandSet(additionalChecks["CommandSet"], result);
        }

        // الفحص الرابع: التحقق من المراجع المعطوبة
        CheckBrokenReferences(dependencyGraph, result);

        // تحديد حالة الصحة الإجمالية
        DetermineOverallValidity(result);

        return result;
    }

    /// <summary>
    /// فحص الملفات الحرجة
    /// </summary>
    private void CheckCriticalFiles(UnitDependencyGraph graph, ValidationResult result)
    {
        var missingCritical = graph.AllNodes
            .Where(n => _requiredFileTypes.Any(ft => n.Name.EndsWith(ft, StringComparison.OrdinalIgnoreCase)))
            .Where(n => n.Status == AssetStatus.Missing)
            .ToList();

        foreach (var node in missingCritical)
        {
            result.IsValid = false;

            var error = new ValidationError
            {
                Code = "CRITICAL_FILE_MISSING",
                Message = $"ملف حرج مفقود: {node.Name}",
                Severity = ErrorSeverity.Critical,
                RelatedFile = node.Name
            };

            result.Errors.Add(error);
        }

        // تحذير إذا كانت جميع الملفات الحرجة موجودة
        var hasCriticalFiles = graph.AllNodes
            .Where(n => _requiredFileTypes.Any(ft => n.Name.EndsWith(ft, StringComparison.OrdinalIgnoreCase)))
            .Any(n => n.Status == AssetStatus.Found);

        if (!hasCriticalFiles && graph.AllNodes.Count > 0)
        {
            result.Errors.Add(new ValidationError
            {
                Code = "NO_CRITICAL_FILES",
                Message = "لا توجد أي ملفات حرجة في الوحدة",
                Severity = ErrorSeverity.Critical
            });
        }
    }

    /// <summary>
    /// فحص الملفات الاختيارية
    /// </summary>
    private void CheckOptionalFiles(UnitDependencyGraph graph, ValidationResult result)
    {
        var missingOptional = graph.AllNodes
            .Where(n => _optionalFileTypes.Any(ft => n.Name.EndsWith(ft, StringComparison.OrdinalIgnoreCase)))
            .Where(n => n.Status == AssetStatus.Missing)
            .ToList();

        if (missingOptional.Count > 0)
        {
            var warning = new ValidationWarning
            {
                Code = "OPTIONAL_FILES_MISSING",
                Message = $"عدد {missingOptional.Count} من الملفات الاختيارية مفقودة"
            };

            result.Warnings.Add(warning);

            foreach (var node in missingOptional)
            {
                warning.Message += $"\n  - {node.Name}";
            }
        }
    }

    /// <summary>
    /// التحقق من وجود CommandSet
    /// </summary>
    private void CheckCommandSet(bool commandSetExists, ValidationResult result)
    {
        if (!commandSetExists)
        {
            result.Errors.Add(new ValidationError
            {
                Code = "COMMANDSET_NOT_FOUND",
                Message = "CommandSet المرتبط بالوحدة غير موجود في ملفات INI",
                Severity = ErrorSeverity.Critical
            });

            result.IsValid = false;
        }
    }

    /// <summary>
    /// فحص المراجع المعطوبة (Non-exist-ing references)
    /// </summary>
    private void CheckBrokenReferences(UnitDependencyGraph graph, ValidationResult result)
    {
        var brokenReferences = graph.AllNodes
            .Where(n => n.Status == AssetStatus.Invalid || 
                       (n.Status == AssetStatus.NotVerified && n.Dependencies.Count == 0 && n.Depth > 0))
            .ToList();

        if (brokenReferences.Count > 0)
        {
            result.Warnings.Add(new ValidationWarning
            {
                Code = "BROKEN_REFERENCES",
                Message = $"عدد {brokenReferences.Count} من المراجع المعطوبة"
            });
        }
    }

    /// <summary>
    /// تحديد حالة الصحة الإجمالية
    /// </summary>
    private void DetermineOverallValidity(ValidationResult result)
    {
        // إذا كانت هناك أخطاء حرجة، الوحدة غير صحيحة
        bool hasCriticalErrors = result.Errors.Any(e => e.Severity == ErrorSeverity.Critical);

        if (hasCriticalErrors)
        {
            result.IsValid = false;
        }
        else
        {
            result.IsValid = true;
        }
    }

    /// <summary>
    /// التحقق من اكتمال الأصول للرسم البياني بأكمله
    /// </summary>
    public CompletionStatus EvaluateCompletionStatus(UnitDependencyGraph graph)
    {
        if (graph.AllNodes.Count == 0)
            return CompletionStatus.CannotVerify;

        double completionPercentage = graph.GetCompletionPercentage();

        // التحقق من وجود الملفات الحرجة
        bool hasCriticalFiles = graph.AllNodes
            .Any(n => n.Name.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase) &&
                     n.Status == AssetStatus.Found);

        if (!hasCriticalFiles)
            return CompletionStatus.Incomplete;

        return completionPercentage switch
        {
            100 => CompletionStatus.Complete,
            >= 80 => CompletionStatus.Partial,
            > 0 => CompletionStatus.Incomplete,
            _ => CompletionStatus.CannotVerify
        };
    }

    /// <summary>
    /// الحصول على تقرير مفصل عن حالة الوحدة
    /// </summary>
    public string GenerateDetailedReport(ValidationResult validationResult, UnitDependencyGraph? graph = null)
    {
        var report = new System.Text.StringBuilder();

        report.AppendLine($"╔════════════════════════════════════════════╗");
        report.AppendLine($"║      تقرير التحقق من الوحدة               ║");
        report.AppendLine($"╚════════════════════════════════════════════╝");
        report.AppendLine();

        report.AppendLine($"معرف الوحدة: {validationResult.UnitId}");
        report.AppendLine($"الحالة: {(validationResult.IsValid ? "✅ صحيحة" : "❌ بها مشاكل")}");
        report.AppendLine($"نسبة النجاح: {validationResult.GetSuccessPercentage():F1}%");
        report.AppendLine();

        if (validationResult.Errors.Count > 0)
        {
            report.AppendLine("الأخطاء:");
            foreach (var error in validationResult.Errors)
            {
                report.AppendLine($"  [{error.Severity}] {error.Code}: {error.Message}");
            }
            report.AppendLine();
        }

        if (validationResult.Warnings.Count > 0)
        {
            report.AppendLine("التحذيرات:");
            foreach (var warning in validationResult.Warnings)
            {
                report.AppendLine($"  [⚠️ WARNING] {warning.Code}: {warning.Message}");
            }
            report.AppendLine();
        }

        if (graph != null)
        {
            report.AppendLine("معلومات الرسم البياني:");
            report.AppendLine($"  إجمالي العقد: {graph.AllNodes.Count}");
            report.AppendLine($"  الملفات الموجودة: {graph.FoundCount}");
            report.AppendLine($"  الملفات المفقودة: {graph.MissingCount}");
            report.AppendLine($"  العمق الأقصى: {graph.MaxDepth}");
            report.AppendLine($"  الحجم الإجمالي: {FormatBytes(graph.TotalSizeInBytes)}");
            report.AppendLine($"  نسبة الاكتمال: {graph.GetCompletionPercentage():F1}%");
        }

        return report.ToString();
    }

    /// <summary>
    /// تنسيق حجم الملف بشكل إنساني
    /// </summary>
    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:F2} {sizes[order]}";
    }
}
