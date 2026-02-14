namespace ZeroHourStudio.Domain.Entities
{
    /// <summary>
    /// مستوى خطورة التعارض
    /// </summary>
    public enum ConflictSeverity
    {
        /// <summary>تأثير بسيط - بصري فقط</summary>
        Low,
        /// <summary>تأثير متوسط - قد يسبب مشاكل طفيفة</summary>
        Medium,
        /// <summary>تأثير عالي - سيسبب مشاكل</summary>
        High,
        /// <summary>تأثير حرج - سيمنع اللعبة من العمل</summary>
        Critical
    }

    /// <summary>
    /// نوع التعديل اليدوي
    /// </summary>
    public enum ManualEditType
    {
        CommandSetSlotInsert,
        CommandButtonGenerate,
        ObjectIniOverride,
        FactoryIntegration,
        WeaponReferenceUpdate,
        FXListReferenceUpdate,
        ArmorDefinitionPatch,
        LocomotorPatch,
        UpgradePatch,
        Other
    }

    /// <summary>
    /// تشخيص ذكي لتعارض واحد - يشرح السبب والحل
    /// </summary>
    public class ConflictDiagnosis
    {
        /// <summary>اسم التعريف المتعارض</summary>
        public string DefinitionName { get; set; } = string.Empty;

        /// <summary>نوع التعريف (Object, Weapon, FXList...)</summary>
        public string DefinitionType { get; set; } = string.Empty;

        /// <summary>نوع التعارض</summary>
        public ConflictKind ConflictKind { get; set; }

        /// <summary>مستوى الخطورة</summary>
        public ConflictSeverity Severity { get; set; }

        /// <summary>السبب الجذري للتعارض</summary>
        public string RootCause { get; set; } = string.Empty;

        /// <summary>شرح مفصل لما سيحدث إذا لم يُحل</summary>
        public string Explanation { get; set; } = string.Empty;

        /// <summary>التأثير المتوقع</summary>
        public string Impact { get; set; } = string.Empty;

        /// <summary>هل يمكن حله تلقائياً؟</summary>
        public bool AutoFixable { get; set; }

        /// <summary>الحلول المتاحة مرتبة حسب الأولوية</summary>
        public List<SuggestedSolution> Solutions { get; set; } = new();

        /// <summary>أيقونة بحسب الخطورة</summary>
        public string SeverityIcon => Severity switch
        {
            ConflictSeverity.Critical => "🔴",
            ConflictSeverity.High => "🟠",
            ConflictSeverity.Medium => "🟡",
            ConflictSeverity.Low => "🟢",
            _ => "⚪"
        };

        /// <summary>نص الخطورة</summary>
        public string SeverityText => Severity switch
        {
            ConflictSeverity.Critical => "حرج",
            ConflictSeverity.High => "عالي",
            ConflictSeverity.Medium => "متوسط",
            ConflictSeverity.Low => "منخفض",
            _ => "غير محدد"
        };
    }

    /// <summary>
    /// حل مقترح لتعارض
    /// </summary>
    public class SuggestedSolution
    {
        /// <summary>عنوان الحل</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>وصف الحل بالتفصيل</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>هل يمكن تطبيقه تلقائياً؟</summary>
        public bool IsAutoApplicable { get; set; }

        /// <summary>أولوية الحل (1 = الأفضل)</summary>
        public int Priority { get; set; }

        /// <summary>نوع الإجراء</summary>
        public string ActionType { get; set; } = string.Empty;

        /// <summary>تقدير وقت التطبيق الآلي</summary>
        public string EstimatedTime { get; set; } = string.Empty;
    }

    /// <summary>
    /// نتيجة حل تعديل يدوي واحد
    /// </summary>
    public class ManualEditResolution
    {
        /// <summary>نوع التعديل</summary>
        public ManualEditType EditType { get; set; }

        /// <summary>وصف التعديل المطلوب</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>هل تم حله تلقائياً؟</summary>
        public bool AutoResolved { get; set; }

        /// <summary>ماذا تم تطبيقه</summary>
        public string AppliedFix { get; set; } = string.Empty;

        /// <summary>رسالة حالة</summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>أيقونة الحالة</summary>
        public string StatusIcon => AutoResolved ? "✅" : "⚠";
    }

    /// <summary>
    /// فحص صحي واحد
    /// </summary>
    public class HealthCheck
    {
        /// <summary>اسم الفحص</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>الوصف</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>هل نجح الفحص</summary>
        public bool Passed { get; set; }

        /// <summary>التفاصيل</summary>
        public string Details { get; set; } = string.Empty;

        /// <summary>الخطورة إذا فشل</summary>
        public ConflictSeverity FailureSeverity { get; set; }
    }

    /// <summary>
    /// خطر محتمل
    /// </summary>
    public class TransferRisk
    {
        /// <summary>وصف الخطر</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>الخطورة</summary>
        public ConflictSeverity Severity { get; set; }

        /// <summary>كيفية التخفيف</summary>
        public string Mitigation { get; set; } = string.Empty;
    }

    /// <summary>
    /// تقرير صحة النقل الشامل
    /// </summary>
    public class TransferHealthReport
    {
        /// <summary>نسبة النجاح المتوقعة (0-100)</summary>
        public int SuccessScore { get; set; }

        /// <summary>تصنيف الصحة</summary>
        public string HealthGrade => SuccessScore switch
        {
            >= 90 => "ممتاز",
            >= 70 => "جيد",
            >= 50 => "مقبول",
            >= 30 => "ضعيف",
            _ => "حرج"
        };

        /// <summary>لون الصحة</summary>
        public string HealthColor => SuccessScore switch
        {
            >= 90 => "#00FF88",
            >= 70 => "#00D4FF",
            >= 50 => "#FFD700",
            >= 30 => "#FF6B00",
            _ => "#FF3366"
        };

        /// <summary>الفحوصات التي تمت</summary>
        public List<HealthCheck> Checks { get; set; } = new();

        /// <summary>المخاطر المكتشفة</summary>
        public List<TransferRisk> Risks { get; set; } = new();

        /// <summary>التوصيات الذكية</summary>
        public List<string> Recommendations { get; set; } = new();

        /// <summary>عدد الفحوصات الناجحة</summary>
        public int PassedChecks => Checks.Count(c => c.Passed);

        /// <summary>عدد الفحوصات الفاشلة</summary>
        public int FailedChecks => Checks.Count(c => !c.Passed);

        /// <summary>ملخص نصي</summary>
        public string Summary { get; set; } = string.Empty;
    }

    /// <summary>
    /// تقرير التشخيص الشامل - يجمع كل نتائج الذكاء
    /// </summary>
    public class DiagnosisReport
    {
        /// <summary>اسم الوحدة</summary>
        public string UnitName { get; set; } = string.Empty;

        /// <summary>تاريخ التشخيص</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>تقرير الصحة</summary>
        public TransferHealthReport Health { get; set; } = new();

        /// <summary>تشخيصات التعارضات</summary>
        public List<ConflictDiagnosis> Diagnoses { get; set; } = new();

        /// <summary>التعديلات اليدوية ونتائج حلها</summary>
        public List<ManualEditResolution> ManualEdits { get; set; } = new();

        /// <summary>عدد التعارضات القابلة للحل التلقائي</summary>
        public int AutoFixableCount => Diagnoses.Count(d => d.AutoFixable);

        /// <summary>عدد التعديلات اليدوية المحلولة</summary>
        public int ManualEditsResolved => ManualEdits.Count(m => m.AutoResolved);

        /// <summary>عدد التعديلات اليدوية غير المحلولة</summary>
        public int ManualEditsPending => ManualEdits.Count(m => !m.AutoResolved);

        /// <summary>هل يمكن حل كل شيء تلقائياً</summary>
        public bool CanAutoResolveAll => Diagnoses.All(d => d.AutoFixable) &&
                                         ManualEdits.All(m => m.AutoResolved);

        /// <summary>مستوى الخطر الأعلى</summary>
        public ConflictSeverity OverallRiskLevel
        {
            get
            {
                if (Diagnoses.Count == 0) return ConflictSeverity.Low;
                return Diagnoses.Max(d => d.Severity);
            }
        }
    }
}
