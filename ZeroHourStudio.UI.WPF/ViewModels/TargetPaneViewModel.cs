using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Domain.Models;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.Logging;
using ZeroHourStudio.UI.WPF.Commands;
using ZeroHourStudio.UI.WPF.Core;

namespace ZeroHourStudio.UI.WPF.ViewModels;

/// <summary>
/// ViewModel الجزء الأيمن - متصفح المود الهدف + منطقة الإسقاط
/// </summary>
public class TargetPaneViewModel : ViewModelBase
{
    private readonly UnitDiscoveryService _unitDiscovery = new();
    private readonly FactionDiscoveryService _factionDiscovery = new();
    private readonly CommandSetAnalyzer _commandSetAnalyzer = new(new ZeroHourStudio.Infrastructure.Parsers.SAGE_IniParser());

    // === SAGE Relational Engine ===
    private readonly CommandChainService _commandChain = new();

    /// <summary>محرك علائقي مبني — يُعاد استخدامه من PortingStudioViewModel</summary>
    public CommandChainService TargetCommandChain => _commandChain;

    // === Events ===
    public event EventHandler? ModLoaded;
    public event EventHandler<SageUnit>? UnitDropped;

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

    private ObservableCollection<TargetFactionOption> _targetFactionOptions = new();
    public ObservableCollection<TargetFactionOption> TargetFactionOptions
    {
        get => _targetFactionOptions;
        set => SetProperty(ref _targetFactionOptions, value);
    }

    private string _selectedFaction = string.Empty;
    public string SelectedFaction
    {
        get => _selectedFaction;
        set => SetProperty(ref _selectedFaction, value);
    }

    private CommandSetAnalysis? _targetModAnalysis;
    public CommandSetAnalysis? TargetModAnalysis
    {
        get => _targetModAnalysis;
        set => SetProperty(ref _targetModAnalysis, value);
    }

    private string _targetModSlotsInfo = string.Empty;
    public string TargetModSlotsInfo
    {
        get => _targetModSlotsInfo;
        set => SetProperty(ref _targetModSlotsInfo, value);
    }

    private ObservableCollection<TransferLogItem> _transferLog = new();
    public ObservableCollection<TransferLogItem> TransferLog
    {
        get => _transferLog;
        set => SetProperty(ref _transferLog, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusText = "اختر مسار المود الهدف";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // === Commands ===
    public ICommand LoadModCommand { get; }

    public TargetPaneViewModel()
    {
        LoadModCommand = new AsyncRelayCommand(_ => LoadTargetAsync(), _ => HasPath && !IsLoading);
    }

    public async Task LoadTargetAsync()
    {
        if (!HasPath) return;

        IsLoading = true;
        StatusText = "جاري تحليل المود الهدف...";

        try
        {
            // === Primary: FactionDiscoveryService (PlayerTemplate + BIG) ===
            var discoveryResult = await _factionDiscovery.DiscoverFactionsAsync(ModPath);
            var factions = discoveryResult.InternalNames;

            // === Fallback: UnitDiscovery Side= ===
            if (factions.Count == 0)
            {
                BlackBoxRecorder.Record("TARGET_PANE", "FACTION_FALLBACK", "Trying UnitDiscovery.DiscoverFactionsAsync");
                factions = await _unitDiscovery.DiscoverFactionsAsync(ModPath);
            }

            // === Fallback: Manual PlayerTemplate parse ===
            if (factions.Count == 0)
            {
                BlackBoxRecorder.Record("TARGET_PANE", "FACTION_FALLBACK", "Trying DiscoverFactionsManually");
                factions = await DiscoverFactionsManually(ModPath);
            }

            await AnalyzeTargetModCommandSetsAsync();

            if (factions.Count > 0)
            {
                UpdateTargetFactionOptions(factions);
                if (TargetFactionOptions.Count > 0 && string.IsNullOrEmpty(SelectedFaction))
                    SelectedFaction = SuggestBestFaction(null);
                StatusText = $"{TargetFactionOptions.Count} فصيل | المصدر: {discoveryResult.Source}";
            }
            else
            {
                // No factions found — show real state, NOT fake data
                TargetFactionOptions = new ObservableCollection<TargetFactionOption>();
                StatusText = "⚠ لم يُعثر على فصائل (تحقق من السجلات)";
                BlackBoxRecorder.Record("TARGET_PANE", "NO_FACTIONS", discoveryResult.ErrorMessage ?? "All strategies exhausted");
            }

            ModLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Report the real error — no masking with fake data
            TargetFactionOptions = new ObservableCollection<TargetFactionOption>();
            StatusText = $"⚠ خطأ في اكتشاف الفصائل: {ex.Message}";
            BlackBoxRecorder.Record("TARGET_PANE", "FACTION_ERROR", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public string SuggestBestFaction(string? preferredFaction)
    {
        if (TargetFactionOptions.Count == 0)
            return string.Empty;

        var suggestions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var option in TargetFactionOptions)
        {
            var score = 0;

            if (!string.IsNullOrWhiteSpace(preferredFaction) &&
                option.Name.Equals(preferredFaction, StringComparison.OrdinalIgnoreCase))
                score += 100;

            if (option.TotalSlots > 0)
                score += option.AvailableSlots * 10;

            if (option.Name.Contains("America", StringComparison.OrdinalIgnoreCase) ||
                option.Name.Contains("USA", StringComparison.OrdinalIgnoreCase))
                score += 5;

            suggestions[option.Name] = score;
        }

        return suggestions.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key ?? TargetFactionOptions[0].Name;
    }

    private async Task<List<string>> DiscoverFactionsManually(string modPath)
    {
        var factions = new List<string>();

        try
        {
            var playerTemplatePath = Path.Combine(modPath, "Data", "INI", "PlayerTemplate.ini");
            if (!File.Exists(playerTemplatePath))
                playerTemplatePath = Path.Combine(modPath, "PlayerTemplate.ini");

            if (File.Exists(playerTemplatePath))
            {
                var content = await File.ReadAllTextAsync(playerTemplatePath);
                var lines = content.Split('\n');

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("PlayerTemplate", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var factionName = parts[1].Replace("Faction", "");
                            if (!factions.Contains(factionName, StringComparer.OrdinalIgnoreCase))
                                factions.Add(factionName);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return factions;
    }

    private async Task AnalyzeTargetModCommandSetsAsync()
    {
        if (!HasPath)
            return;

        try
        {
            // === Legacy analyzer (slot counts) ===
            StatusText = "جاري تحليل CommandSets...";
            TargetModAnalysis = await _commandSetAnalyzer.AnalyzeModCommandSetsAsync(ModPath);

            if (TargetModAnalysis != null)
                TargetModSlotsInfo = $"📊 Slots: {TargetModAnalysis.AvailableSlots} متاح من {TargetModAnalysis.TotalSlots}";

            // === SAGE Relational Engine (بناء الفهرس للاستخدام لاحقاً) ===
            StatusMessage(StatusText = "⏳ بناء فهرس المحرك العلائقي...");
            var targetMappedIndex = new MappedImageIndex();
            await targetMappedIndex.BuildIndexAsync(ModPath);
            await _commandChain.BuildIndexAsync(ModPath, targetMappedIndex);

            if (_commandChain.IsBuilt)
            {
                TargetModSlotsInfo +=
                    $" | 🧠 Objects={_commandChain.ObjectCount}, Sets={_commandChain.CommandSetCount}, Buttons={_commandChain.CommandButtonCount}";
                BlackBoxRecorder.Record("TARGET_PANE", "RELATIONAL_INDEX_BUILT",
                    $"Objects={_commandChain.ObjectCount}, Sets={_commandChain.CommandSetCount}");
            }
        }
        catch (Exception ex)
        {
            TargetModSlotsInfo = "⚠ فشل تحليل CommandSets";
            BlackBoxRecorder.Record("TARGET_PANE", "ANALYZE_ERROR", ex.Message);
        }
    }

    /// <summary>عرض رسالة الحالة (helper)</summary>
    private static string StatusMessage(string msg) => msg;

    private void UpdateTargetFactionOptions(IReadOnlyList<string> factions)
    {
        var options = new List<TargetFactionOption>();

        foreach (var faction in factions)
        {
            int available = 0;
            int total = 0;

            if (TargetModAnalysis != null)
            {
                if (!TargetModAnalysis.FactionSlots.TryGetValue(faction, out var info))
                {
                    var normalized = NormalizeFactionKey(faction);
                    TargetModAnalysis.FactionSlots.TryGetValue(normalized, out info);
                }

                if (info != null)
                {
                    available = info.AvailableSlots;
                    total = info.TotalSlots;
                }
            }

            options.Add(new TargetFactionOption
            {
                Name = faction,
                AvailableSlots = available,
                TotalSlots = total
            });
        }

        TargetFactionOptions = new ObservableCollection<TargetFactionOption>(options);
    }

    private static string NormalizeFactionKey(string faction)
        => ZeroHourStudio.Domain.ValueObjects.FactionName.NormalizeFactionKey(faction);

    /// <summary>
    /// يُستدعى عند إسقاط وحدة في منطقة الهدف
    /// </summary>
    public void HandleUnitDrop(SageUnit unit)
    {
        UnitDropped?.Invoke(this, unit);
    }

    /// <summary>
    /// إضافة سجل نقل ناجح
    /// </summary>
    public void AddTransferLog(string unitName)
    {
        TransferLog.Insert(0, new TransferLogItem
        {
            UnitName = unitName,
            Timestamp = DateTime.Now,
            Success = true
        });
    }
}

/// <summary>
/// عنصر سجل النقل
/// </summary>
public class TransferLogItem : ViewModelBase
{
    public string UnitName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool Success { get; set; }
    public string TimeText => Timestamp.ToString("HH:mm:ss");
}

public class TargetFactionOption
{
    public string Name { get; set; } = string.Empty;
    public int AvailableSlots { get; set; }
    public int TotalSlots { get; set; }
    public bool HasAvailableSlots => TotalSlots == 0 || AvailableSlots > 0;
    public string AvailableSlotsText => TotalSlots == 0 ? "؟" : $"{AvailableSlots} متاح";
    public string AvailabilityColor
    {
        get
        {
            if (TotalSlots == 0) return "#888888";
            if (AvailableSlots == 0) return "#FF6666";
            if (AvailableSlots <= 2) return "#FFB366";
            return "#00CC66";
        }
    }
}
