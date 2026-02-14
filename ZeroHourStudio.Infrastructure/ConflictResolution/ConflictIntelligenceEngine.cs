using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Infrastructure.ConflictResolution;

/// <summary>
/// محرك التشخيص الذكي - يحلل كل تعارض ويشرح السبب والحل
/// يستخدم قواعد معرفة C&C Generals لتقديم تشخيصات دقيقة
/// </summary>
public class ConflictIntelligenceEngine
{
    // === قواعد معرفة أنواع التعريفات وخطورتها ===
    private static readonly Dictionary<string, ConflictSeverity> TypeSeverityMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Object", ConflictSeverity.Critical },
        { "ObjectINI", ConflictSeverity.Critical },
        { "Weapon", ConflictSeverity.High },
        { "CommandSet", ConflictSeverity.High },
        { "CommandButton", ConflictSeverity.High },
        { "Armor", ConflictSeverity.High },
        { "Upgrade", ConflictSeverity.High },
        { "SpecialPower", ConflictSeverity.High },
        { "Science", ConflictSeverity.Medium },
        { "Locomotor", ConflictSeverity.Medium },
        { "LocomotorSet", ConflictSeverity.Medium },
        { "FXList", ConflictSeverity.Low },
        { "ObjectCreationList", ConflictSeverity.Medium },
        { "OCL", ConflictSeverity.Medium },
        { "ParticleSystem", ConflictSeverity.Low },
        { "Model3D", ConflictSeverity.Low },
        { "Texture", ConflictSeverity.Low },
        { "Audio", ConflictSeverity.Low },
        { "VisualEffect", ConflictSeverity.Low },
    };

    /// <summary>
    /// تشخيص جميع التعارضات في التقرير
    /// </summary>
    public List<ConflictDiagnosis> DiagnoseConflicts(ConflictReport report, UnitDependencyGraph graph)
    {
        var diagnoses = new List<ConflictDiagnosis>();

        foreach (var conflict in report.Conflicts)
        {
            var diagnosis = DiagnoseSingleConflict(conflict, graph);
            diagnoses.Add(diagnosis);
        }

        return diagnoses;
    }

    /// <summary>
    /// تشخيص تعارض واحد مع تفاصيل كاملة
    /// </summary>
    private ConflictDiagnosis DiagnoseSingleConflict(ConflictEntry conflict, UnitDependencyGraph graph)
    {
        var diagnosis = new ConflictDiagnosis
        {
            DefinitionName = conflict.DefinitionName,
            DefinitionType = conflict.DefinitionType,
            ConflictKind = conflict.Kind,
            Severity = DetermineSeverity(conflict),
        };

        switch (conflict.Kind)
        {
            case ConflictKind.Duplicate:
                DiagnoseDuplicate(diagnosis, conflict, graph);
                break;
            case ConflictKind.FileOverwrite:
                DiagnoseFileOverwrite(diagnosis, conflict);
                break;
            case ConflictKind.NameCollision:
                DiagnoseNameCollision(diagnosis, conflict);
                break;
        }

        return diagnosis;
    }

    private void DiagnoseDuplicate(ConflictDiagnosis diagnosis, ConflictEntry conflict, UnitDependencyGraph graph)
    {
        var typeName = GetArabicTypeName(conflict.DefinitionType);
        var dependentCount = CountDependents(conflict.DefinitionName, graph);

        // === السبب الجذري ===
        diagnosis.RootCause = $"التعريف '{conflict.DefinitionName}' من نوع {typeName} موجود بالفعل في المود الهدف. " +
                              $"هذا يعني أن المود الهدف يحتوي على تعريف بنفس الاسم بالضبط.";

        // === الشرح حسب النوع ===
        switch (conflict.DefinitionType.ToLowerInvariant())
        {
            case "object" or "objectini":
                diagnosis.Explanation = $"الكتابة فوق هذا التعريف ستؤدي إلى استبدال الوحدة الموجودة بالكامل. " +
                    $"جميع خصائصها (الأسلحة، الدروع، النماذج، الأصوات) ستتغير. " +
                    $"إذا كانت وحدة أساسية في المود، فقد يتسبب ذلك في عدم استقرار اللعبة.";
                diagnosis.Impact = "استبدال الوحدة بالكامل - قد يؤثر على توازن اللعبة";
                diagnosis.AutoFixable = true;
                break;

            case "weapon":
                diagnosis.Explanation = $"السلاح '{conflict.DefinitionName}' قد يكون مستخدماً من قبل وحدات أخرى في المود الهدف. " +
                    $"الكتابة فوقه ستغير سلوك جميع الوحدات التي تستخدمه. " +
                    $"يُستخدم هذا السلاح في {dependentCount} تبعية على الأقل.";
                diagnosis.Impact = $"تغيير سلوك السلاح لجميع الوحدات المستخدمة - {dependentCount} تبعية متأثرة";
                diagnosis.AutoFixable = true;
                break;

            case "commandset":
                diagnosis.Explanation = "CommandSet يتحكم في أزرار التحكم للوحدة. الكتابة فوقه ستغير أزرار التحكم " +
                    "لجميع الوحدات التي تستخدم نفس CommandSet في المود الهدف.";
                diagnosis.Impact = "تغيير أزرار التحكم - قد يؤدي لفقدان أوامر من وحدات أخرى";
                diagnosis.AutoFixable = true;
                break;

            case "commandbutton":
                diagnosis.Explanation = "CommandButton يمثل زر أمر واحد. الكتابة فوقه ستغير وظيفة هذا الزر في أي " +
                    "CommandSet يستخدمه في المود الهدف.";
                diagnosis.Impact = "تغيير وظيفة زر التحكم";
                diagnosis.AutoFixable = true;
                break;

            case "armor":
                diagnosis.Explanation = $"الدرع '{conflict.DefinitionName}' يحدد مقاومة الوحدة للأنواع المختلفة من الأسلحة. " +
                    "الكتابة فوقه ستؤثر على صلابة جميع الوحدات التي تستخدمه.";
                diagnosis.Impact = "تغيير مقاومة الضرر لوحدات متعددة";
                diagnosis.AutoFixable = true;
                break;

            case "fxlist":
                diagnosis.Explanation = "FXList يتحكم في المؤثرات البصرية فقط (انفجارات، أصوات، جسيمات). " +
                    "الكتابة فوقه لن تؤثر على سلوك اللعبة لكن ستغير المظهر البصري.";
                diagnosis.Impact = "تغيير بصري فقط - لا تأثير على اللعب";
                diagnosis.AutoFixable = true;
                break;

            case "objectcreationlist" or "ocl":
                diagnosis.Explanation = "قائمة إنشاء الكائنات تحدد ماذا يُنشأ عند حدث معين (مثل موت الوحدة). " +
                    "الكتابة فوقها ستغير سلوك الإنشاء لجميع الوحدات المستخدمة.";
                diagnosis.Impact = "تغيير سلوك إنشاء الكائنات";
                diagnosis.AutoFixable = true;
                break;

            case "upgrade":
                diagnosis.Explanation = "الترقية تتحكم في تحسينات الوحدة. الكتابة فوقها ستؤثر على شجرة الترقيات.";
                diagnosis.Impact = "تغيير نظام الترقيات";
                diagnosis.AutoFixable = true;
                break;

            case "specialpower":
                diagnosis.Explanation = "القدرة الخاصة تتحكم في قدرات الوحدة الفريدة. الكتابة فوقها ستؤثر على أي وحدة تستخدمها.";
                diagnosis.Impact = "تغيير القدرات الخاصة";
                diagnosis.AutoFixable = true;
                break;

            case "locomotor" or "locomotorset":
                diagnosis.Explanation = "محرك الحركة يتحكم في كيفية تحرك الوحدة. الكتابة فوقه ستغير سلوك حركة الوحدات المتأثرة.";
                diagnosis.Impact = "تغيير سلوك حركة الوحدات";
                diagnosis.AutoFixable = true;
                break;

            case "particlesystem":
                diagnosis.Explanation = "نظام الجسيمات يتحكم في تأثيرات بصرية (دخان، نار، غبار). تأثير بصري فقط.";
                diagnosis.Impact = "تغيير بصري فقط";
                diagnosis.AutoFixable = true;
                break;

            default:
                diagnosis.Explanation = $"التعريف '{conflict.DefinitionName}' من نوع {typeName} موجود بالفعل. " +
                    "الكتابة فوقه قد تؤثر على عناصر أخرى في المود الهدف.";
                diagnosis.Impact = "تأثير غير محدد - يُنصح بإعادة التسمية";
                diagnosis.AutoFixable = true;
                break;
        }

        // === الحلول ===
        diagnosis.Solutions.Add(new SuggestedSolution
        {
            Title = "إعادة تسمية تلقائية",
            Description = $"إعادة تسمية '{conflict.DefinitionName}' إلى '{conflict.SuggestedRename}' مع تحديث جميع المراجع داخل ملفات INI تلقائياً.",
            IsAutoApplicable = true,
            Priority = 1,
            ActionType = "Rename",
            EstimatedTime = "فوري"
        });

        diagnosis.Solutions.Add(new SuggestedSolution
        {
            Title = "الكتابة فوق التعريف الموجود",
            Description = $"استبدال التعريف الموجود في المود الهدف بالتعريف الجديد. تحذير: سيؤثر على جميع الوحدات التي تستخدم '{conflict.DefinitionName}'.",
            IsAutoApplicable = true,
            Priority = 2,
            ActionType = "Overwrite",
            EstimatedTime = "فوري"
        });

        diagnosis.Solutions.Add(new SuggestedSolution
        {
            Title = "تجاوز (عدم النقل)",
            Description = "عدم نقل هذا التعريف واستخدام النسخة الموجودة في المود الهدف. قد لا يعمل بشكل صحيح إذا كانت النسخة مختلفة.",
            IsAutoApplicable = true,
            Priority = 3,
            ActionType = "Skip",
            EstimatedTime = "فوري"
        });
    }

    private void DiagnoseFileOverwrite(ConflictDiagnosis diagnosis, ConflictEntry conflict)
    {
        diagnosis.RootCause = $"الملف '{conflict.SourceFile}' موجود بالفعل في المود الهدف بنفس المسار. " +
            $"الملف الهدف: '{conflict.TargetFile}'";

        diagnosis.Explanation = "الكتابة فوق هذا الملف ستستبدل محتواه بالكامل. " +
            "إذا أجرى المودر تعديلات على هذا الملف في المود الهدف، ستُفقد جميع تعديلاته.";

        diagnosis.Impact = "فقدان تعديلات الملف الموجود في المود الهدف";
        diagnosis.AutoFixable = false;

        diagnosis.Solutions.Add(new SuggestedSolution
        {
            Title = "الكتابة فوق الملف",
            Description = "استبدال الملف الموجود بالنسخة الجديدة.",
            IsAutoApplicable = true,
            Priority = 1,
            ActionType = "Overwrite",
            EstimatedTime = "فوري"
        });

        diagnosis.Solutions.Add(new SuggestedSolution
        {
            Title = "تجاوز الملف",
            Description = "الإبقاء على الملف الموجود وعدم نقل الملف الجديد.",
            IsAutoApplicable = true,
            Priority = 2,
            ActionType = "Skip",
            EstimatedTime = "فوري"
        });
    }

    private void DiagnoseNameCollision(ConflictDiagnosis diagnosis, ConflictEntry conflict)
    {
        diagnosis.RootCause = $"اسم التعريف '{conflict.DefinitionName}' متشابه مع تعريف موجود (تصادم أسماء).";

        diagnosis.Explanation = "رغم أن الاسم ليس مطابقاً تماماً، إلا أنه قريب بما يكفي ليسبب ارتباكاً. " +
            "يُنصح بإعادة التسمية لتجنب الخلط.";

        diagnosis.Impact = "قد يسبب ارتباكاً لكن لن يسبب أخطاء تقنية";
        diagnosis.AutoFixable = true;

        diagnosis.Solutions.Add(new SuggestedSolution
        {
            Title = "إعادة تسمية تلقائية",
            Description = $"إعادة تسمية إلى '{conflict.SuggestedRename}' لتجنب الارتباك.",
            IsAutoApplicable = true,
            Priority = 1,
            ActionType = "Rename",
            EstimatedTime = "فوري"
        });
    }

    /// <summary>
    /// تحديد خطورة التعارض بناءً على نوع التعريف ونوع التعارض
    /// </summary>
    private static ConflictSeverity DetermineSeverity(ConflictEntry conflict)
    {
        // تعارض الملفات دائماً Medium على الأقل
        if (conflict.Kind == ConflictKind.FileOverwrite)
            return ConflictSeverity.Medium;

        // تصادم الأسماء = Low
        if (conflict.Kind == ConflictKind.NameCollision)
            return ConflictSeverity.Low;

        // Duplicate: حسب نوع التعريف
        if (TypeSeverityMap.TryGetValue(conflict.DefinitionType, out var severity))
            return severity;

        return ConflictSeverity.Medium;
    }

    /// <summary>
    /// عدد التبعيات التي تعتمد على تعريف معين
    /// </summary>
    private static int CountDependents(string definitionName, UnitDependencyGraph graph)
    {
        return graph.AllNodes.Count(n =>
            n.Dependencies.Any(d => d.Name.Equals(definitionName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// ترجمة نوع التعريف للعربية
    /// </summary>
    private static string GetArabicTypeName(string type) => type.ToLowerInvariant() switch
    {
        "object" or "objectini" => "وحدة/كائن",
        "weapon" => "سلاح",
        "commandset" => "مجموعة أوامر",
        "commandbutton" => "زر أوامر",
        "armor" => "درع",
        "fxlist" => "مؤثرات بصرية",
        "objectcreationlist" or "ocl" => "قائمة إنشاء كائنات",
        "particlesystem" => "نظام جسيمات",
        "locomotor" or "locomotorset" => "محرك حركة",
        "upgrade" => "ترقية",
        "specialpower" => "قدرة خاصة",
        "science" => "علم/تقنية",
        "model3d" => "نموذج ثلاثي الأبعاد",
        "texture" => "نسيج/خامة",
        "audio" => "صوت",
        _ => type
    };
}
