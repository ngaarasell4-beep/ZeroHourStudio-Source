using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.UI.WPF.Commands;
using ZeroHourStudio.UI.WPF.Core;

namespace ZeroHourStudio.UI.WPF.ViewModels;

/// <summary>
/// ViewModel الجزء الأيسر - متصفح المود المصدر
/// </summary>
public class SourcePaneViewModel : ViewModelBase
{
    private readonly UnitDiscoveryService _unitDiscovery = new();

    // === Events ===
    public event EventHandler? ModLoaded;
    public event EventHandler<SageUnit>? UnitSelected;

    // === Data ===
    private List<SageUnit> _allUnits = new();
    public List<SageUnit> AllUnits => _allUnits;

    private readonly Dictionary<string, Dictionary<string, string>> _unitDataIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _unitIniPathIndex = new(StringComparer.OrdinalIgnoreCase);

    // === Properties ===
    private string _modPath = string.Empty;
    public string ModPath
    {
        get => _modPath;
        set
        {
            if (SetProperty(ref _modPath, value))
                OnPropertyChanged(nameof(HasPath));
        }
    }

    public bool HasPath => !string.IsNullOrWhiteSpace(ModPath);

    private ObservableCollection<SageUnit> _units = new();
    public ObservableCollection<SageUnit> Units
    {
        get => _units;
        set => SetProperty(ref _units, value);
    }

    private ObservableCollection<string> _factions = new();
    public ObservableCollection<string> Factions
    {
        get => _factions;
        set => SetProperty(ref _factions, value);
    }

    private string _selectedFaction = "الكل";
    public string SelectedFaction
    {
        get => _selectedFaction;
        set
        {
            if (SetProperty(ref _selectedFaction, value))
                ApplyFilter();
        }
    }

    private SageUnit? _selectedUnit;
    public SageUnit? SelectedUnit
    {
        get => _selectedUnit;
        set
        {
            if (SetProperty(ref _selectedUnit, value) && value != null)
                UnitSelected?.Invoke(this, value);
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusText = "اختر مسار المود المصدر";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // === Commands ===
    public ICommand LoadModCommand { get; }
    public ICommand SearchCommand { get; }

    public SourcePaneViewModel()
    {
        LoadModCommand = new AsyncRelayCommand(_ => LoadModAsync(), _ => HasPath && !IsLoading);
        SearchCommand = new RelayCommand(_ => ApplyFilter());
    }

    public async Task LoadModAsync()
    {
        if (!HasPath) return;

        IsLoading = true;
        StatusText = "جاري اكتشاف الوحدات...";

        try
        {
            var progress = new Progress<DiscoveryProgress>(p =>
            {
                StatusText = $"اكتشاف... {p.UnitsFound} وحدة";
            });

            var result = await _unitDiscovery.DiscoverUnitsAsync(ModPath, progress);

            _allUnits = result.Units.ToList();
            _unitDataIndex.Clear();
            _unitIniPathIndex.Clear();
            foreach (var kvp in result.UnitDataByName) _unitDataIndex[kvp.Key] = kvp.Value;
            foreach (var kvp in result.UnitSourceIniPath) _unitIniPathIndex[kvp.Key] = kvp.Value;

            Units = new ObservableCollection<SageUnit>(_allUnits);

            // استخراج الفصائل
            var parser = new SAGE_IniParser();
            var extractor = new SmartFactionExtractor(parser);
            var factionResult = await extractor.ExtractFactionsAsync(ModPath);

            var factionNames = factionResult.Factions.Keys
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            factionNames.Insert(0, "الكل");
            Factions = new ObservableCollection<string>(factionNames);
            SelectedFaction = "الكل";

            StatusText = $"{_allUnits.Count} وحدة | {factionNames.Count - 1} فصيل";
            ModLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = $"خطأ: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Dictionary<string, string>? GetUnitData(string unitName)
    {
        return _unitDataIndex.TryGetValue(unitName, out var data)
            ? new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase) : null;
    }

    public string? GetUnitIniPath(string unitName)
    {
        return _unitIniPathIndex.TryGetValue(unitName, out var path) ? path : null;
    }

    /// <summary>
    /// قراءة محتوى INI الخام للوحدة (بلوك Object ... End) لاستخدامه في تحويل الفصيل والمعاينة.
    /// </summary>
    public async Task<string?> GetUnitIniContentAsync(string unitName)
    {
        var path = GetUnitIniPath(unitName);
        if (string.IsNullOrWhiteSpace(path)) return null;
        // مسارات من أرشيفات .big قد تحتوي على ::
        if (path.Contains("::", StringComparison.OrdinalIgnoreCase)) return null;

        if (!File.Exists(path)) return null;

        try
        {
            var lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
            var sb = new System.Text.StringBuilder();
            var inBlock = false;
            var depth = 0;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (trimmed.StartsWith("Object ", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("ObjectCreation", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("ObjectStatus", StringComparison.OrdinalIgnoreCase))
                {
                    var namePart = trimmed.Length > 7 ? trimmed[7..].Trim() : "";
                    if (namePart.StartsWith(unitName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(namePart, unitName, StringComparison.OrdinalIgnoreCase))
                    {
                        inBlock = true;
                        depth = 1;
                        sb.AppendLine(line);
                        continue;
                    }
                }

                if (inBlock)
                {
                    if (trimmed.StartsWith("End", StringComparison.OrdinalIgnoreCase))
                    {
                        depth--;
                        if (depth <= 0)
                        {
                            sb.AppendLine(line);
                            break;
                        }
                    }
                    else if ((trimmed.StartsWith("Body", StringComparison.OrdinalIgnoreCase) ||
                              trimmed.StartsWith("Draw", StringComparison.OrdinalIgnoreCase) ||
                              trimmed.StartsWith("Behavior", StringComparison.OrdinalIgnoreCase) ||
                              trimmed.StartsWith("WeaponSet", StringComparison.OrdinalIgnoreCase) ||
                              trimmed.StartsWith("ArmorSet", StringComparison.OrdinalIgnoreCase)) &&
                             trimmed.Contains(' ') && !trimmed.Contains('='))
                        depth++;

                    sb.AppendLine(line);
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private void ApplyFilter()
    {
        var filtered = _allUnits.AsEnumerable();

        if (!string.IsNullOrEmpty(SelectedFaction) && SelectedFaction != "الكل")
        {
            filtered = filtered.Where(u =>
                string.Equals(u.Side, SelectedFaction, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            filtered = filtered.Where(u =>
                u.TechnicalName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                u.Side.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        Units = new ObservableCollection<SageUnit>(filtered);
    }
}
