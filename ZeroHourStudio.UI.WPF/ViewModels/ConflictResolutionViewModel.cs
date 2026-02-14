using System.Collections.ObjectModel;
using System.Windows.Input;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.UI.WPF.Commands;
using ZeroHourStudio.UI.WPF.Core;

namespace ZeroHourStudio.UI.WPF.ViewModels;

/// <summary>
/// ViewModel حل التعارضات - يعرض التعارضات المكتشفة مع خيارات الحل
/// </summary>
public class ConflictResolutionViewModel : ViewModelBase
{
    private ObservableCollection<ConflictItemVM> _conflicts = new();
    public ObservableCollection<ConflictItemVM> Conflicts
    {
        get => _conflicts;
        set => SetProperty(ref _conflicts, value);
    }

    private string _unitName = string.Empty;
    public string UnitName
    {
        get => _unitName;
        set => SetProperty(ref _unitName, value);
    }

    private int _totalConflicts;
    public int TotalConflicts
    {
        get => _totalConflicts;
        set => SetProperty(ref _totalConflicts, value);
    }

    // === Commands ===
    public ICommand RenameAllCommand { get; }
    public ICommand SkipAllCommand { get; }
    public ICommand OverwriteAllCommand { get; }

    public ConflictResolutionViewModel()
    {
        RenameAllCommand = new RelayCommand(_ => SetAllResolution(ConflictResolutionAction.Rename));
        SkipAllCommand = new RelayCommand(_ => SetAllResolution(ConflictResolutionAction.Skip));
        OverwriteAllCommand = new RelayCommand(_ => SetAllResolution(ConflictResolutionAction.Overwrite));
    }

    public void LoadConflicts(ConflictReport report)
    {
        UnitName = report.UnitName;
        TotalConflicts = report.Conflicts.Count;

        var items = new ObservableCollection<ConflictItemVM>();
        foreach (var conflict in report.Conflicts)
        {
            items.Add(new ConflictItemVM
            {
                OriginalName = conflict.DefinitionName,
                DefinitionType = conflict.DefinitionType,
                Kind = conflict.Kind,
                SuggestedRename = conflict.SuggestedRename,
                ProposedName = conflict.SuggestedRename,
                SelectedAction = conflict.Kind == ConflictKind.FileOverwrite
                    ? ConflictResolutionAction.Overwrite
                    : ConflictResolutionAction.Rename
            });
        }
        Conflicts = items;
    }

    public Dictionary<string, string> GetRenameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Conflicts)
        {
            if (item.SelectedAction == ConflictResolutionAction.Rename &&
                !string.IsNullOrWhiteSpace(item.ProposedName))
            {
                map[item.OriginalName] = item.ProposedName;
            }
        }
        return map;
    }

    private void SetAllResolution(ConflictResolutionAction action)
    {
        foreach (var item in Conflicts)
        {
            item.SelectedAction = action;
        }
    }
}

/// <summary>
/// عنصر تعارض واحد مع خيارات الحل
/// </summary>
public class ConflictItemVM : ViewModelBase
{
    public string OriginalName { get; set; } = string.Empty;
    public string DefinitionType { get; set; } = string.Empty;
    public ConflictKind Kind { get; set; }
    public string SuggestedRename { get; set; } = string.Empty;

    private string _proposedName = string.Empty;
    public string ProposedName
    {
        get => _proposedName;
        set => SetProperty(ref _proposedName, value);
    }

    private ConflictResolutionAction _selectedAction = ConflictResolutionAction.Rename;
    public ConflictResolutionAction SelectedAction
    {
        get => _selectedAction;
        set => SetProperty(ref _selectedAction, value);
    }

    public string KindText => Kind switch
    {
        ConflictKind.Duplicate => "تعريف مكرر",
        ConflictKind.NameCollision => "تضارب أسماء",
        ConflictKind.FileOverwrite => "ملف موجود",
        _ => "غير معروف"
    };
}

public enum ConflictResolutionAction
{
    Skip,
    Overwrite,
    Rename
}
