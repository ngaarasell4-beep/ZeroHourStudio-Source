using System.Collections.ObjectModel;
using System.Windows.Input;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Domain.Models;
using ZeroHourStudio.Infrastructure.ConflictResolution;
using ZeroHourStudio.UI.WPF.Commands;
using ZeroHourStudio.UI.WPF.Core;

namespace ZeroHourStudio.UI.WPF.ViewModels;

/// <summary>
/// ViewModel المعاينة الذكية - يدمج محرك التشخيص ومحلل الصحة وحل التعديلات اليدوية
/// </summary>
public class IntelligentPreviewViewModel : ViewModelBase
{
    private readonly ConflictIntelligenceEngine _intelligenceEngine;
    private readonly ManualEditAutoResolver _autoResolver;
    private readonly TransferHealthAnalyzer _healthAnalyzer;

    // === معلومات الوحدة الأساسية ===
    public string UnitName { get; set; } = string.Empty;
    public string SourceFaction { get; set; } = string.Empty;
    public int DependencyCount { get; set; }
    public string TargetModName { get; set; } = string.Empty;
    public string TargetFaction { get; set; } = string.Empty;
    public int SlotNumber { get; set; }
    public string CommandSetName { get; set; } = string.Empty;

    // === تقرير الصحة ===
    private TransferHealthReport _healthReport = new();
    public TransferHealthReport HealthReport
    {
        get => _healthReport;
        set
        {
            SetProperty(ref _healthReport, value);
            OnPropertyChanged(nameof(HealthScore));
            OnPropertyChanged(nameof(HealthGrade));
            OnPropertyChanged(nameof(HealthColor));
            OnPropertyChanged(nameof(HealthSummary));
            OnPropertyChanged(nameof(PassedChecks));
            OnPropertyChanged(nameof(FailedChecks));
        }
    }

    public int HealthScore => HealthReport.SuccessScore;
    public string HealthGrade => HealthReport.HealthGrade;
    public string HealthColor => HealthReport.HealthColor;
    public string HealthSummary => HealthReport.Summary;
    public int PassedChecks => HealthReport.PassedChecks;
    public int FailedChecks => HealthReport.FailedChecks;

    // === التشخيصات ===
    private ObservableCollection<ConflictDiagnosis> _diagnoses = new();
    public ObservableCollection<ConflictDiagnosis> Diagnoses
    {
        get => _diagnoses;
        set => SetProperty(ref _diagnoses, value);
    }

    public bool HasDiagnoses => Diagnoses.Count > 0;
    public int DiagnosesCount => Diagnoses.Count;
    public int AutoFixableCount => Diagnoses.Count(d => d.AutoFixable);

    // === التعديلات اليدوية ===
    private ObservableCollection<ManualEditResolution> _manualEdits = new();
    public ObservableCollection<ManualEditResolution> ManualEdits
    {
        get => _manualEdits;
        set => SetProperty(ref _manualEdits, value);
    }

    public bool HasManualEdits => ManualEdits.Count > 0;
    public int ManualEditsResolved => ManualEdits.Count(m => m.AutoResolved);
    public int ManualEditsPending => ManualEdits.Count(m => !m.AutoResolved);

    // === الفحوصات ===
    private ObservableCollection<HealthCheck> _healthChecks = new();
    public ObservableCollection<HealthCheck> HealthChecks
    {
        get => _healthChecks;
        set => SetProperty(ref _healthChecks, value);
    }

    // === المخاطر ===
    private ObservableCollection<TransferRisk> _risks = new();
    public ObservableCollection<TransferRisk> Risks
    {
        get => _risks;
        set => SetProperty(ref _risks, value);
    }

    public bool HasRisks => Risks.Count > 0;

    // === التوصيات ===
    private ObservableCollection<string> _recommendations = new();
    public ObservableCollection<string> Recommendations
    {
        get => _recommendations;
        set => SetProperty(ref _recommendations, value);
    }

    public bool HasRecommendations => Recommendations.Count > 0;

    // === حالة عامة ===
    private bool _canAutoResolveAll;
    public bool CanAutoResolveAll
    {
        get => _canAutoResolveAll;
        set => SetProperty(ref _canAutoResolveAll, value);
    }

    private string _overallSummary = string.Empty;
    public string OverallSummary
    {
        get => _overallSummary;
        set => SetProperty(ref _overallSummary, value);
    }

    // === Commands ===
    public ICommand AutoResolveAllCommand { get; }

    public IntelligentPreviewViewModel()
    {
        _intelligenceEngine = new ConflictIntelligenceEngine();
        _autoResolver = new ManualEditAutoResolver();
        _healthAnalyzer = new TransferHealthAnalyzer();

        AutoResolveAllCommand = new RelayCommand(_ => { /* handled in codebehind for simplicity */ });
    }

    /// <summary>
    /// تشغيل التشخيص الكامل
    /// </summary>
    public void RunDiagnosis(
        SageUnit unit,
        UnitDependencyGraph graph,
        ConflictReport conflicts,
        string targetModPath,
        string targetFaction,
        bool hasAvailableSlot,
        CommandSetSlotInfo? slotInfo)
    {
        // 1. تشخيص التعارضات
        var diagList = _intelligenceEngine.DiagnoseConflicts(conflicts, graph);
        Diagnoses = new ObservableCollection<ConflictDiagnosis>(diagList);

        // 2. حل التعديلات اليدوية
        var editsList = _autoResolver.AnalyzeAndResolve(
            unit, graph, conflicts, targetModPath, targetFaction, hasAvailableSlot, slotInfo);
        ManualEdits = new ObservableCollection<ManualEditResolution>(editsList);

        // 3. تحليل الصحة
        HealthReport = _healthAnalyzer.Analyze(
            graph, conflicts, editsList, targetModPath, targetFaction, hasAvailableSlot);

        // 4. تحديث المجموعات
        HealthChecks = new ObservableCollection<HealthCheck>(HealthReport.Checks);
        Risks = new ObservableCollection<TransferRisk>(HealthReport.Risks);
        Recommendations = new ObservableCollection<string>(HealthReport.Recommendations);

        // 5. حساب الحالة العامة
        CanAutoResolveAll = diagList.All(d => d.AutoFixable) && editsList.All(m => m.AutoResolved);

        // 6. الملخص
        OverallSummary = GenerateOverallSummary(diagList, editsList);

        // 7. إشعار التغييرات
        OnPropertyChanged(nameof(HasDiagnoses));
        OnPropertyChanged(nameof(DiagnosesCount));
        OnPropertyChanged(nameof(AutoFixableCount));
        OnPropertyChanged(nameof(HasManualEdits));
        OnPropertyChanged(nameof(ManualEditsResolved));
        OnPropertyChanged(nameof(ManualEditsPending));
        OnPropertyChanged(nameof(HasRisks));
        OnPropertyChanged(nameof(HasRecommendations));
    }

    private string GenerateOverallSummary(
        List<ConflictDiagnosis> diagnoses,
        List<ManualEditResolution> edits)
    {
        var parts = new List<string>();

        if (diagnoses.Count > 0)
        {
            var critical = diagnoses.Count(d => d.Severity == ConflictSeverity.Critical);
            var high = diagnoses.Count(d => d.Severity == ConflictSeverity.High);
            var autoFixable = diagnoses.Count(d => d.AutoFixable);

            parts.Add($"{diagnoses.Count} تعارض ({critical} حرج، {high} عالي) | {autoFixable} قابل للحل التلقائي");
        }
        else
        {
            parts.Add("✓ لا توجد تعارضات");
        }

        var resolved = edits.Count(e => e.AutoResolved);
        var total = edits.Count;
        parts.Add($"{resolved}/{total} تعديل محلول تلقائياً");

        return string.Join(" | ", parts);
    }
}
