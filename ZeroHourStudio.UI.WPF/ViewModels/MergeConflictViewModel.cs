using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZeroHourStudio.Infrastructure.ConflictResolution;

namespace ZeroHourStudio.UI.WPF.ViewModels;

/// <summary>
/// ViewModel لنافذة حل التعارضات الذكي باستخدام SmartMergeEngine
/// </summary>
public class MergeConflictViewModel : INotifyPropertyChanged
{
    private readonly SmartMergeEngine _mergeEngine = new();
    private ObservableCollection<MergeFieldVM> _fields = new();
    private string _sourceName = string.Empty;
    private string _targetName = string.Empty;
    private string _mergePreview = string.Empty;
    private MergeStrategy _selectedStrategy = MergeStrategy.SmartMerge;

    public string SourceName
    {
        get => _sourceName;
        set { _sourceName = value; OnPropertyChanged(); }
    }

    public string TargetName
    {
        get => _targetName;
        set { _targetName = value; OnPropertyChanged(); }
    }

    public ObservableCollection<MergeFieldVM> Fields
    {
        get => _fields;
        set { _fields = value; OnPropertyChanged(); }
    }

    public MergeStrategy SelectedStrategy
    {
        get => _selectedStrategy;
        set { _selectedStrategy = value; OnPropertyChanged(); UpdatePreview(); }
    }

    public string MergePreview
    {
        get => _mergePreview;
        set { _mergePreview = value; OnPropertyChanged(); }
    }

    public bool UserConfirmed { get; set; }

    /// <summary>
    /// المحتوى المدمج النهائي
    /// </summary>
    public string MergedContent { get; private set; } = string.Empty;

    /// <summary>
    /// تحميل حقول الدمج من محتوى INI خام
    /// </summary>
    public void LoadFromIniContent(string sourceContent, string targetContent, string definitionName)
    {
        SourceName = definitionName + " (المصدر)";
        TargetName = definitionName + " (الهدف)";

        var result = _mergeEngine.Merge(sourceContent, targetContent, definitionName, SelectedStrategy);
        MergedContent = result.MergedContent;

        var items = new ObservableCollection<MergeFieldVM>();
        foreach (var field in result.Fields)
        {
            items.Add(new MergeFieldVM
            {
                FieldName = field.Key,
                SourceValue = field.SourceValue ?? "",
                TargetValue = field.TargetValue ?? "",
                MergedValue = field.FinalValue,
                UseSource = field.Status == MergeFieldStatus.SourceOnly || field.Status == MergeFieldStatus.Modified,
                HasConflict = field.Status == MergeFieldStatus.Modified
            });
        }

        Fields = items;
        MergePreview = result.MergedContent;
    }

    /// <summary>
    /// تحميل حقول من بيانات خام (key=value)
    /// </summary>
    public void LoadFromRawData(string sourceName, string targetName,
        Dictionary<string, string> sourceData, Dictionary<string, string> targetData)
    {
        SourceName = sourceName;
        TargetName = targetName;

        var allKeys = sourceData.Keys.Union(targetData.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        var items = new ObservableCollection<MergeFieldVM>();
        foreach (var key in allKeys)
        {
            sourceData.TryGetValue(key, out var srcVal);
            targetData.TryGetValue(key, out var tgtVal);

            items.Add(new MergeFieldVM
            {
                FieldName = key,
                SourceValue = srcVal ?? "",
                TargetValue = tgtVal ?? "",
                MergedValue = srcVal ?? tgtVal ?? "",
                UseSource = _selectedStrategy == MergeStrategy.SourceWins,
                HasConflict = srcVal != tgtVal && srcVal != null && tgtVal != null
            });
        }

        Fields = items;
        UpdatePreview();
    }

    /// <summary>
    /// الحصول على النتيجة النهائية
    /// </summary>
    public Dictionary<string, string> GetMergedResult()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Fields)
        {
            var value = field.UseSource ? field.SourceValue : field.MergedValue;
            if (!string.IsNullOrWhiteSpace(value))
                result[field.FieldName] = value;
        }
        return result;
    }

    private void UpdatePreview()
    {
        var lines = new List<string>();
        foreach (var field in Fields)
        {
            var finalValue = field.UseSource ? field.SourceValue : field.MergedValue;
            if (!string.IsNullOrWhiteSpace(finalValue))
                lines.Add($"  {field.FieldName} = {finalValue}");
        }
        MergePreview = string.Join("\n", lines);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// حقل واحد في نتيجة الدمج
/// </summary>
public class MergeFieldVM : INotifyPropertyChanged
{
    public string FieldName { get; set; } = string.Empty;
    public string SourceValue { get; set; } = string.Empty;
    public string TargetValue { get; set; } = string.Empty;

    private string _mergedValue = string.Empty;
    public string MergedValue
    {
        get => _mergedValue;
        set { _mergedValue = value; OnPropertyChanged(); }
    }

    private bool _useSource = true;
    public bool UseSource
    {
        get => _useSource;
        set { _useSource = value; OnPropertyChanged(); }
    }

    public bool HasConflict { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
