using System.Collections.ObjectModel;
using System.Windows.Input;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.Localization;
using ZeroHourStudio.UI.WPF.Commands;
using ZeroHourStudio.UI.WPF.Core;

namespace ZeroHourStudio.UI.WPF.ViewModels;

/// <summary>
/// ViewModel محرر CSF - تحرير نصوص التوطين مع دعم العربية
/// </summary>
public class CsfEditorViewModel : ViewModelBase
{
    private readonly CsfLocalizationService _csfService;

    private ObservableCollection<CsfEntryVM> _entries = new();
    public ObservableCollection<CsfEntryVM> Entries
    {
        get => _entries;
        set => SetProperty(ref _entries, value);
    }

    private CsfEntryVM? _selectedEntry;
    public CsfEntryVM? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    private string _filterText = string.Empty;
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
                ApplyFilter();
        }
    }

    private string _csfFilePath = string.Empty;
    public string CsfFilePath
    {
        get => _csfFilePath;
        set => SetProperty(ref _csfFilePath, value);
    }

    private bool _hasChanges;
    public bool HasChanges
    {
        get => _hasChanges;
        set => SetProperty(ref _hasChanges, value);
    }

    // الكل
    private List<CsfEntryVM> _allEntries = new();

    // === Commands ===
    public ICommand LoadCsfCommand { get; }
    public ICommand SaveCsfCommand { get; }
    public ICommand AddEntryCommand { get; }

    public CsfEditorViewModel(CsfLocalizationService csfService)
    {
        _csfService = csfService;
        LoadCsfCommand = new AsyncRelayCommand(_ => LoadCsfAsync(), _ => !string.IsNullOrEmpty(CsfFilePath));
        SaveCsfCommand = new AsyncRelayCommand(_ => SaveCsfAsync(), _ => HasChanges);
        AddEntryCommand = new RelayCommand(_ => AddNewEntry());
    }

    public async Task LoadCsfAsync()
    {
        if (string.IsNullOrEmpty(CsfFilePath) || !System.IO.File.Exists(CsfFilePath))
            return;

        var entries = await _csfService.ReadCsfAsync(CsfFilePath);
        _allEntries = entries.Select(e => new CsfEntryVM
        {
            Label = e.Label,
            EnglishText = e.EnglishText,
            ArabicText = e.ArabicText,
            IsNew = false
        }).ToList();

        ApplyFilter();
        HasChanges = false;
    }

    public async Task SaveCsfAsync()
    {
        if (string.IsNullOrEmpty(CsfFilePath)) return;

        var entries = _allEntries.Select(vm => new CsfEntry(vm.Label, vm.EnglishText, vm.ArabicText)).ToList();
        await _csfService.WriteCsfAsync(CsfFilePath, entries);
        HasChanges = false;
    }

    public void AddEntries(List<CsfEntry> newEntries)
    {
        foreach (var entry in newEntries)
        {
            var existing = _allEntries.FirstOrDefault(e =>
                string.Equals(e.Label, entry.Label, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.EnglishText = entry.EnglishText;
                if (!string.IsNullOrEmpty(entry.ArabicText))
                    existing.ArabicText = entry.ArabicText;
            }
            else
            {
                _allEntries.Add(new CsfEntryVM
                {
                    Label = entry.Label,
                    EnglishText = entry.EnglishText,
                    ArabicText = entry.ArabicText,
                    IsNew = true
                });
            }
        }

        ApplyFilter();
        HasChanges = true;
    }

    private void AddNewEntry()
    {
        var entry = new CsfEntryVM
        {
            Label = "NEW:Label",
            EnglishText = "New Entry",
            ArabicText = "",
            IsNew = true
        };
        _allEntries.Add(entry);
        ApplyFilter();
        SelectedEntry = Entries.LastOrDefault();
        HasChanges = true;
    }

    private void ApplyFilter()
    {
        IEnumerable<CsfEntryVM> filtered = _allEntries;

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var filter = FilterText.Trim();
            filtered = filtered.Where(e =>
                e.Label.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                e.EnglishText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                e.ArabicText.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        Entries = new ObservableCollection<CsfEntryVM>(filtered);
    }
}

/// <summary>
/// ViewModel مدخل CSF واحد
/// </summary>
public class CsfEntryVM : ViewModelBase
{
    private string _label = string.Empty;
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    private string _englishText = string.Empty;
    public string EnglishText
    {
        get => _englishText;
        set => SetProperty(ref _englishText, value);
    }

    private string _arabicText = string.Empty;
    public string ArabicText
    {
        get => _arabicText;
        set => SetProperty(ref _arabicText, value);
    }

    public bool IsNew { get; set; }
}
