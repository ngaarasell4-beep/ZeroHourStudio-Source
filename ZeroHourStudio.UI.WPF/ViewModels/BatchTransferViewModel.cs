using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZeroHourStudio.Infrastructure.Transfer;

namespace ZeroHourStudio.UI.WPF.ViewModels;

/// <summary>
/// ViewModel للنقل الدفعي - يربط بين BatchTransferService وواجهة المستخدم
/// </summary>
public class BatchTransferViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

    // === خصائص العرض ===
    public string SourceModName { get; set; } = string.Empty;
    public string TargetModName { get; set; } = string.Empty;

    private int _totalUnits;
    public int TotalUnits
    {
        get => _totalUnits;
        set { _totalUnits = value; OnPropertyChanged(); }
    }

    private int _completedCount;
    public int CompletedCount
    {
        get => _completedCount;
        set { _completedCount = value; OnPropertyChanged(); }
    }

    private double _overallProgress;
    public double OverallProgress
    {
        get => _overallProgress;
        set { _overallProgress = value; OnPropertyChanged(); }
    }

    private string _progressMessage = "جاهز للبدء";
    public string ProgressMessage
    {
        get => _progressMessage;
        set { _progressMessage = value; OnPropertyChanged(); }
    }

    private string _summaryText = "";
    public string SummaryText
    {
        get => _summaryText;
        set { _summaryText = value; OnPropertyChanged(); }
    }

    private bool _canStart = true;
    public bool CanStart
    {
        get => _canStart;
        set { _canStart = value; OnPropertyChanged(); }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStart)); }
    }

    public ObservableCollection<BatchUnitResult> UnitResults { get; } = new();

    /// <summary>
    /// تحميل الوحدات المختارة
    /// </summary>
    public void LoadUnits(IEnumerable<string> unitNames)
    {
        UnitResults.Clear();
        foreach (var name in unitNames)
        {
            UnitResults.Add(new BatchUnitResult
            {
                UnitName = name,
                Status = BatchUnitStatus.Pending,
                StatusMessage = "في الانتظار..."
            });
        }
        TotalUnits = UnitResults.Count;
        SummaryText = $"{TotalUnits} وحدة جاهزة للنقل";
    }

    /// <summary>
    /// تحديث حالة وحدة
    /// </summary>
    public void UpdateUnit(BatchUnitResult result)
    {
        var existing = UnitResults.FirstOrDefault(u => u.UnitName == result.UnitName);
        if (existing != null)
        {
            var index = UnitResults.IndexOf(existing);
            UnitResults[index] = result;
        }

        CompletedCount = UnitResults.Count(u =>
            u.Status is BatchUnitStatus.Succeeded or
            BatchUnitStatus.Failed or BatchUnitStatus.Skipped);

        OverallProgress = TotalUnits > 0 ? (CompletedCount * 100.0) / TotalUnits : 0;
    }

    /// <summary>
    /// تحديث من تقرير النقل النهائي
    /// </summary>
    public void ApplyReport(BatchTransferReport report)
    {
        UnitResults.Clear();
        foreach (var r in report.UnitResults)
            UnitResults.Add(r);

        CompletedCount = report.TotalUnits;
        OverallProgress = 100;
        IsRunning = false;
        CanStart = false;
        SummaryText = report.Summary;
        ProgressMessage = "✅ اكتمل النقل الدفعي";
    }
}
