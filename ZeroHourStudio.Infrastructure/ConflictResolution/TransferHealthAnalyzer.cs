using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Infrastructure.ConflictResolution;

/// <summary>
/// محلل صحة النقل الشامل - يقيّم عملية النقل ويعطي نسبة نجاح متوقعة
/// </summary>
public class TransferHealthAnalyzer
{
    /// <summary>
    /// تحليل صحة عملية النقل الشاملة
    /// </summary>
    public TransferHealthReport Analyze(
        UnitDependencyGraph graph,
        ConflictReport conflicts,
        List<ManualEditResolution> manualEdits,
        string targetModPath,
        string targetFaction,
        bool hasAvailableSlot)
    {
        var report = new TransferHealthReport();

        // === إجراء الفحوصات ===
        report.Checks.Add(CheckDependencyCompleteness(graph));
        report.Checks.Add(CheckMissingAssets(graph));
        report.Checks.Add(CheckConflictSeverity(conflicts));
        report.Checks.Add(CheckTargetModStructure(targetModPath));
        report.Checks.Add(CheckSlotAvailability(hasAvailableSlot));
        report.Checks.Add(CheckFactionCompatibility(graph, targetFaction));
        report.Checks.Add(CheckManualEditsResolution(manualEdits));
        report.Checks.Add(CheckOrphanedDefinitions(graph));

        // === حساب نسبة النجاح ===
        report.SuccessScore = CalculateScore(report.Checks, conflicts, graph, manualEdits);

        // === تحليل المخاطر ===
        report.Risks = AnalyzeRisks(graph, conflicts, hasAvailableSlot, targetFaction);

        // === التوصيات الذكية ===
        report.Recommendations = GenerateRecommendations(report, graph, conflicts, manualEdits);

        // === الملخص ===
        report.Summary = GenerateSummary(report);

        return report;
    }

    // ==================== الفحوصات ====================

    private HealthCheck CheckDependencyCompleteness(UnitDependencyGraph graph)
    {
        var completion = graph.GetCompletionPercentage();
        return new HealthCheck
        {
            Name = "اكتمال التبعيات",
            Description = "فحص أن جميع التبعيات المطلوبة موجودة ومتاحة",
            Passed = completion >= 80,
            Details = $"{completion:F0}% من التبعيات مكتملة ({graph.FoundCount} موجود، {graph.MissingCount} مفقود)",
            FailureSeverity = completion < 50 ? ConflictSeverity.Critical : ConflictSeverity.High
        };
    }

    private HealthCheck CheckMissingAssets(UnitDependencyGraph graph)
    {
        var missingAssets = graph.AllNodes.Where(n => n.Status == AssetStatus.Missing).ToList();
        var criticalMissing = missingAssets.Where(n =>
            n.Type == DependencyType.ObjectINI ||
            n.Type == DependencyType.Weapon ||
            n.Type == DependencyType.Armor ||
            n.Type == DependencyType.Model3D).ToList();

        return new HealthCheck
        {
            Name = "الملفات الحرجة",
            Description = "فحص وجود الملفات الأساسية (نماذج، أسلحة، دروع)",
            Passed = criticalMissing.Count == 0,
            Details = criticalMissing.Count == 0
                ? "جميع الملفات الحرجة موجودة"
                : $"{criticalMissing.Count} ملف حرج مفقود: {string.Join(", ", criticalMissing.Select(n => n.Name).Take(4))}",
            FailureSeverity = ConflictSeverity.Critical
        };
    }

    private HealthCheck CheckConflictSeverity(ConflictReport conflicts)
    {
        var criticalConflicts = conflicts.Conflicts.Count(c =>
            c.DefinitionType.Equals("Object", StringComparison.OrdinalIgnoreCase) ||
            c.DefinitionType.Equals("ObjectINI", StringComparison.OrdinalIgnoreCase));

        return new HealthCheck
        {
            Name = "تعارضات الكائنات",
            Description = "فحص وجود تعارضات حرجة في تعريفات الكائنات",
            Passed = criticalConflicts == 0,
            Details = criticalConflicts == 0
                ? $"لا توجد تعارضات حرجة (إجمالي: {conflicts.Conflicts.Count})"
                : $"{criticalConflicts} تعارض حرج يتطلب حل فوري",
            FailureSeverity = ConflictSeverity.High
        };
    }

    private HealthCheck CheckTargetModStructure(string targetModPath)
    {
        var hasDataFolder = Directory.Exists(Path.Combine(targetModPath, "Data"));
        var hasIniFolder = Directory.Exists(Path.Combine(targetModPath, "Data", "INI"));
        var structureOk = hasDataFolder && hasIniFolder;

        return new HealthCheck
        {
            Name = "هيكل المود الهدف",
            Description = "فحص صحة هيكل مجلدات المود الهدف",
            Passed = structureOk,
            Details = structureOk
                ? "هيكل المجلدات صحيح (Data/INI موجود)"
                : "هيكل المجلدات غير مكتمل - سيتم إنشاء المجلدات المفقودة",
            FailureSeverity = ConflictSeverity.Low
        };
    }

    private HealthCheck CheckSlotAvailability(bool hasSlot)
    {
        return new HealthCheck
        {
            Name = "توفر Slot في CommandSet",
            Description = "فحص وجود موقع متاح في قائمة بناء المصنع",
            Passed = hasSlot,
            Details = hasSlot
                ? "يوجد Slot متاح في CommandSet المصنع"
                : "لا يوجد Slot متاح - سيتم توسيع CommandSet آلياً",
            FailureSeverity = ConflictSeverity.Medium
        };
    }

    private HealthCheck CheckFactionCompatibility(UnitDependencyGraph graph, string targetFaction)
    {
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "America", "USA", "China", "GLA", "AmericaSuperWeapon", "AmericaLaser", "AmericaAirForce",
              "ChinaTank", "ChinaNuke", "ChinaInfantry", "GLAToxin", "GLAStealth", "GLADemolition" };

        var isKnownFaction = supported.Contains(targetFaction ?? "");

        return new HealthCheck
        {
            Name = "توافق الفصيل",
            Description = "فحص دعم الفصيل المستهدف",
            Passed = isKnownFaction,
            Details = isKnownFaction
                ? $"الفصيل '{targetFaction}' مدعوم بالكامل"
                : $"الفصيل '{targetFaction}' غير معروف - ربط المصنع قد لا يعمل تلقائياً",
            FailureSeverity = ConflictSeverity.Medium
        };
    }

    private HealthCheck CheckManualEditsResolution(List<ManualEditResolution> edits)
    {
        var resolved = edits.Count(e => e.AutoResolved);
        var total = edits.Count;
        var allResolved = resolved == total;

        return new HealthCheck
        {
            Name = "حل التعديلات التلقائي",
            Description = "فحص أن جميع التعديلات اليدوية المطلوبة تم حلها آلياً",
            Passed = allResolved,
            Details = allResolved
                ? $"جميع التعديلات محلولة تلقائياً ({resolved}/{total})"
                : $"{resolved}/{total} تعديل محلول - {total - resolved} يتطلب تدخل يدوي",
            FailureSeverity = ConflictSeverity.Medium
        };
    }

    private HealthCheck CheckOrphanedDefinitions(UnitDependencyGraph graph)
    {
        var orphaned = graph.AllNodes.Where(n =>
            n.Status == AssetStatus.Found &&
            n.Dependencies.Count == 0 &&
            n.Depth > 2).ToList();

        return new HealthCheck
        {
            Name = "التعريفات اليتيمة",
            Description = "فحص وجود تعريفات بدون مراجع (قد تكون غير ضرورية)",
            Passed = orphaned.Count < 5,
            Details = orphaned.Count < 5
                ? $"عدد قليل من التعريفات اليتيمة ({orphaned.Count})"
                : $"{orphaned.Count} تعريف يتيم - قد يكون بعضها غير ضروري",
            FailureSeverity = ConflictSeverity.Low
        };
    }

    // ==================== حساب النسبة ====================

    private int CalculateScore(
        List<HealthCheck> checks,
        ConflictReport conflicts,
        UnitDependencyGraph graph,
        List<ManualEditResolution> manualEdits)
    {
        double score = 100;

        // خصم بناءً على الفحوصات الفاشلة
        foreach (var check in checks.Where(c => !c.Passed))
        {
            score -= check.FailureSeverity switch
            {
                ConflictSeverity.Critical => 25,
                ConflictSeverity.High => 15,
                ConflictSeverity.Medium => 8,
                ConflictSeverity.Low => 3,
                _ => 5
            };
        }

        // خصم بناءً على التعارضات
        var conflictPenalty = Math.Min(20, conflicts.Conflicts.Count * 2);
        score -= conflictPenalty;

        // مكافأة إذا كانت جميع التعديلات محلولة
        if (manualEdits.All(m => m.AutoResolved))
            score += 5;

        // مكافأة إذا كان اكتمال التبعيات عالي
        var completion = graph.GetCompletionPercentage();
        if (completion >= 95) score += 5;

        return Math.Max(0, Math.Min(100, (int)score));
    }

    // ==================== المخاطر ====================

    private List<TransferRisk> AnalyzeRisks(
        UnitDependencyGraph graph,
        ConflictReport conflicts,
        bool hasSlot,
        string targetFaction)
    {
        var risks = new List<TransferRisk>();

        // خطر التبعيات المفقودة
        if (graph.MissingCount > 0)
        {
            risks.Add(new TransferRisk
            {
                Description = $"{graph.MissingCount} تبعية مفقودة قد تسبب أخطاء في اللعبة",
                Severity = graph.MissingCount > 5 ? ConflictSeverity.Critical : ConflictSeverity.High,
                Mitigation = "تأكد من أن الملفات المفقودة موجودة في ملفات BIG الأصلية للعبة"
            });
        }

        // خطر التعارضات الكثيرة
        if (conflicts.Conflicts.Count > 50)
        {
            risks.Add(new TransferRisk
            {
                Description = $"عدد كبير من التعارضات ({conflicts.Conflicts.Count}) - قد يسبب مشاكل غير متوقعة",
                Severity = ConflictSeverity.High,
                Mitigation = "يُنصح بإعادة تسمية جميع التعريفات المتعارضة تلقائياً"
            });
        }

        // خطر عدم وجود Slot
        if (!hasSlot)
        {
            risks.Add(new TransferRisk
            {
                Description = "لا يوجد موقع متاح في قائمة بناء المصنع",
                Severity = ConflictSeverity.Medium,
                Mitigation = "سيتم توسيع CommandSet تلقائياً - قد يزيد عدد الأزرار عن الحد المعتاد"
            });
        }

        // خطر الفصيل غير المعروف
        var knownFactions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "America", "USA", "China", "GLA" };
        if (!knownFactions.Any(f => (targetFaction ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)))
        {
            risks.Add(new TransferRisk
            {
                Description = $"الفصيل '{targetFaction}' قد لا يكون مدعوماً بالكامل",
                Severity = ConflictSeverity.Medium,
                Mitigation = "تأكد من أن المصنع المناسب محدد يدوياً"
            });
        }

        return risks;
    }

    // ==================== التوصيات ====================

    private List<string> GenerateRecommendations(
        TransferHealthReport report,
        UnitDependencyGraph graph,
        ConflictReport conflicts,
        List<ManualEditResolution> manualEdits)
    {
        var recs = new List<string>();

        if (report.SuccessScore >= 90)
        {
            recs.Add("💚 حالة ممتازة - يمكنك المتابعة بثقة");
        }
        else if (report.SuccessScore >= 70)
        {
            recs.Add("💙 حالة جيدة - بعض التنبيهات الطفيفة لا تمنع النقل");
        }

        if (conflicts.Conflicts.Count > 0)
        {
            recs.Add($"🔄 يُنصح باستخدام 'إعادة تسمية الكل' لحل {conflicts.Conflicts.Count} تعارض تلقائياً");
        }

        if (graph.MissingCount > 0 && graph.MissingCount <= 3)
        {
            recs.Add("📦 التبعيات المفقودة قليلة - غالباً موجودة في ملفات BIG الأساسية للعبة ولن تسبب مشاكل");
        }

        if (manualEdits.Any(m => !m.AutoResolved))
        {
            recs.Add("⚠ بعض التعديلات تتطلب تدخل يدوي - راجع التفاصيل في قسم التعديلات اليدوية");
        }

        if (graph.AllNodes.Count > 200)
        {
            recs.Add($"📊 الوحدة تحتوي على {graph.AllNodes.Count} تبعية - عدد كبير. تأكد من وجود مساحة كافية");
        }

        if (report.SuccessScore < 50)
        {
            recs.Add("⚠ نسبة النجاح منخفضة - يُنصح بمراجعة المشاكل المكتشفة قبل المتابعة");
        }

        return recs;
    }

    // ==================== الملخص ====================

    private string GenerateSummary(TransferHealthReport report)
    {
        var passed = report.PassedChecks;
        var failed = report.FailedChecks;
        var total = report.Checks.Count;

        return $"نتيجة الفحص: {report.SuccessScore}% ({report.HealthGrade}) | " +
               $"نجح: {passed}/{total} فحص | " +
               $"مخاطر: {report.Risks.Count} | " +
               $"توصيات: {report.Recommendations.Count}";
    }
}
