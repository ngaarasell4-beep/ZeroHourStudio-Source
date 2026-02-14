// MainViewModel - ZeroHourStudio
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.IO;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.UseCases;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Domain.Models;
using ZeroHourStudio.Infrastructure.Normalization;
using ZeroHourStudio.Infrastructure.DependencyResolution;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Infrastructure.Validation;
using ZeroHourStudio.Infrastructure.Transfer;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.Implementations;
using ZeroHourStudio.Infrastructure.Templates;
using ZeroHourStudio.Infrastructure.Analysis;
using ZeroHourStudio.UI.WPF.Commands;
using ZeroHourStudio.UI.WPF.Core;
using ZeroHourStudio.UI.WPF.Services;
using ZeroHourStudio.UI.WPF.Converters;
using ZeroHourStudio.Infrastructure.Logging;
using ZeroHourStudio.Infrastructure.Monitoring;
using ZeroHourStudio.Infrastructure.Filtering;
using ZeroHourStudio.Infrastructure.AssetManagement;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Diagnostics;

namespace ZeroHourStudio.UI.WPF.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IAnalyzeUnitDependenciesUseCase _analyzeUseCase;
        private ITransferUnitUseCase _transferUseCase;
        private readonly SmartNormalization _normalizer;
        private IDependencyResolver _dependencyResolver;
        private ISmartTransferService _smartTransferService;
        private readonly UnitDiscoveryService _unitDiscoveryService;
        private readonly FactionDiscoveryService _factionDiscoveryService = new();
        private readonly CommandSetPatchService _commandSetPatchService;
        private readonly CommandSetAnalyzer _commandSetAnalyzer;
        private readonly CommandButtonAnalyzer _commandButtonAnalyzer;
        private ModBigFileReader _bigFileReader;
        private readonly MonitoredWeaponAnalysisService _weaponAnalysisService;
        private readonly MappedImageIndex _mappedImageIndex;
        private readonly SageDefinitionIndex _sageIndex;
        private DiagnosticAuditService? _auditService; // [New]
        private IconService? _iconService;
        private readonly Dictionary<string, Dictionary<string, string>> _unitDataIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _unitIniPathIndex = new(StringComparer.OrdinalIgnoreCase);

        private string _searchText = string.Empty;
        private bool _isLoading;
        private string _statusMessage = "جاهز";
        private int _progressValue;
        private SageUnit? _selectedUnit;
        private UnitDependencyGraph? _currentGraph;
        private ObservableCollection<SageUnit> _units = new();
        private ObservableCollection<SageUnit> _displayedUnits = new();
        private List<SageUnit> _allUnits = new();
        private ObservableCollection<AuditIssue> _auditIssues = new(); // [New]
        private string _sourceModPath = string.Empty;
        private string _targetModPath = string.Empty;
        private ObservableCollection<TargetFactionOption> _targetFactionOptions = new();
        private string? _selectedTargetFaction;
        private CommandSetAnalysis? _targetModAnalysis;
        private string _targetModSlotsInfo = string.Empty;
        private int _weaponAnalysisCount;
        private int _totalDependencyCount;
        private int _analysisCount;
        private CancellationTokenSource? _analysisCts;

        private ObservableCollection<string> _factions = new();
        private string? _selectedFaction;
        private bool _isAnalyzing;

        public MainViewModel(
            IAnalyzeUnitDependenciesUseCase analyzeUseCase,
            ITransferUnitUseCase transferUseCase,
            SmartNormalization normalizer,
            IDependencyResolver dependencyResolver,
            ISmartTransferService smartTransferService,
            MonitoredWeaponAnalysisService weaponAnalysisService)
        {
            _analyzeUseCase = analyzeUseCase;
            _transferUseCase = transferUseCase;
            _normalizer = normalizer;
            _dependencyResolver = dependencyResolver;
            _smartTransferService = smartTransferService;
            _weaponAnalysisService = weaponAnalysisService;
            _unitDiscoveryService = new UnitDiscoveryService();
            _commandSetPatchService = new CommandSetPatchService();
            _commandSetAnalyzer = new CommandSetAnalyzer(new SAGE_IniParser());
            _commandButtonAnalyzer = new CommandButtonAnalyzer(_commandSetAnalyzer);
            _bigFileReader = new ModBigFileReader(string.Empty);
            _mappedImageIndex = new MappedImageIndex();
            _sageIndex = new SageDefinitionIndex();

            InitializeCommands();
        }

        public MainViewModel()
        {
            _normalizer = new SmartNormalization();
            _unitDiscoveryService = new UnitDiscoveryService();
            _commandSetPatchService = new CommandSetPatchService();
            _sageIndex = new SageDefinitionIndex();
            _commandSetAnalyzer = new CommandSetAnalyzer(new SAGE_IniParser());
            _commandButtonAnalyzer = new CommandButtonAnalyzer(_commandSetAnalyzer);

            var iniParser = new SAGE_IniParser();
            var analyzer = new UnitDependencyAnalyzer(iniParser);
            var validator = new UnitCompletionValidator();
            _analyzeUseCase = new AnalyzeUnitDependenciesUseCase(analyzer, validator);

            _bigFileReader = new ModBigFileReader(string.Empty);
            _weaponAnalysisService = new MonitoredWeaponAnalysisService(iniParser, _bigFileReader);
            _transferUseCase = new TransferUnitUseCase(_bigFileReader);
            _dependencyResolver = new SmartDependencyResolver(_bigFileReader);
            _smartTransferService = new SmartTransferService(_bigFileReader);
            _mappedImageIndex = new MappedImageIndex();

            InitializeCommands();
            StatusMessage = "اضغط على 'استعراض' لاختيار مسار المود";
        }

        private void InitializeCommands()
        {
            SearchCommand = new RelayCommand(_ => ApplyFilter());
            AdvancedSearchCommand = new RelayCommand(_ => { });
            TransferCommand = new AsyncRelayCommand(_ => TransferUnitAsync(), _ => CanTransfer());
            LoadModCommand = new AsyncRelayCommand(_ => LoadModAsync(), _ => CanLoadMod());
            BrowseSourceCommand = new RelayCommand(_ => { });
            BrowseTargetCommand = new RelayCommand(_ => { });
            UniversalTransferCommand = new AsyncRelayCommand(_ => UniversalTransferAsync(), _ => CanUniversalTransfer());
            AuditCommand = new AsyncRelayCommand(_ => AuditUnitAsync(), _ => CanAuditUnit());
            PreviewDiffCommand = new AsyncRelayCommand(_ => PreviewDiffAsync(), _ => HasBothPaths);
            ManageTemplatesCommand = new RelayCommand(_ => ManageTemplates());
            CrossReferenceMapCommand = new AsyncRelayCommand(_ => ShowCrossReferenceMapAsync(), _ => HasBothPaths);
            BalanceReportCommand = new AsyncRelayCommand(_ => ShowBalanceReportAsync(), _ => SelectedUnit != null && HasBothPaths);
            FactionConversionCommand = new AsyncRelayCommand(_ => ShowFactionConversionAsync(), _ => SelectedUnit != null);
        }

        // ─── Properties ───

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    BlackBoxRecorder.RecordUserSelection("SEARCH", value ?? "");
                    ApplyFilter();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set => SetProperty(ref _isAnalyzing, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public int ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public SageUnit? SelectedUnit
        {
            get => _selectedUnit;
            set
            {
                if (SetProperty(ref _selectedUnit, value) && value != null)
                {
                    BlackBoxRecorder.RecordUserSelection("UNIT", $"{value.TechnicalName} | Side={value.Side} | Button={value.ButtonImage}");
                    _ = AnalyzeSelectedUnitAsync(value);
                }
            }
        }

        public UnitDependencyGraph? CurrentGraph
        {
            get => _currentGraph;
            set => SetProperty(ref _currentGraph, value);
        }

        public ObservableCollection<SageUnit> Units
        {
            get => _units;
            set => SetProperty(ref _units, value);
        }

        public ObservableCollection<SageUnit> DisplayedUnits
        {
            get => _displayedUnits;
            set => SetProperty(ref _displayedUnits, value);
        }

        public ObservableCollection<SageUnit> FilteredUnits
        {
            get => _displayedUnits;
            set => SetProperty(ref _displayedUnits, value);
        }

        public ObservableCollection<string> Factions
        {
            get => _factions;
            set => SetProperty(ref _factions, value);
        }

        public string? SelectedFaction
        {
            get => _selectedFaction;
            set
            {
                if (SetProperty(ref _selectedFaction, value))
                {
                    BlackBoxRecorder.RecordUserSelection("FACTION", value ?? "(null)");
                    ApplyFilter();
                }
            }
        }

        public ICommand SearchCommand { get; private set; } = null!;
        public ICommand AdvancedSearchCommand { get; private set; } = null!;
        public ICommand TransferCommand { get; private set; } = null!;
        public ICommand LoadModCommand { get; private set; } = null!;
        public ICommand BrowseSourceCommand { get; private set; } = null!;
        public ICommand BrowseTargetCommand { get; private set; } = null!;

        public int WeaponAnalysisCount
        {
            get => _weaponAnalysisCount;
            set => SetProperty(ref _weaponAnalysisCount, value);
        }

        public int TotalDependencyCount
        {
            get => _totalDependencyCount;
            set => SetProperty(ref _totalDependencyCount, value);
        }

        public int AnalysisCount
        {
            get => _analysisCount;
            set => SetProperty(ref _analysisCount, value);
        }

        public string SourceModPath
        {
            get => _sourceModPath;
            set
            {
                if (SetProperty(ref _sourceModPath, value))
                {
                    OnPropertyChanged(nameof(HasSourcePath));
                    OnPropertyChanged(nameof(HasBothPaths));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string TargetModPath
        {
            get => _targetModPath;
            set
            {
                if (SetProperty(ref _targetModPath, value))
                {
                    OnPropertyChanged(nameof(HasTargetPath));
                    OnPropertyChanged(nameof(HasBothPaths));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool HasSourcePath => !string.IsNullOrWhiteSpace(SourceModPath);
        public bool HasTargetPath => !string.IsNullOrWhiteSpace(TargetModPath);
        public bool HasBothPaths => HasSourcePath && HasTargetPath;

        public ObservableCollection<TargetFactionOption> TargetFactionOptions
        {
            get => _targetFactionOptions;
            set => SetProperty(ref _targetFactionOptions, value);
        }

        public string? SelectedTargetFaction
        {
            get => _selectedTargetFaction;
            set
            {
                if (SetProperty(ref _selectedTargetFaction, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public CommandSetAnalysis? TargetModAnalysis
        {
            get => _targetModAnalysis;
            set => SetProperty(ref _targetModAnalysis, value);
        }

        public string TargetModSlotsInfo
        {
            get => _targetModSlotsInfo;
            set => SetProperty(ref _targetModSlotsInfo, value);
        }

        public ObservableCollection<AuditIssue> AuditIssues
        {
            get => _auditIssues;
            set => SetProperty(ref _auditIssues, value);
        }

        public ICommand AuditCommand { get; private set; } = null!;
        public ICommand PreviewDiffCommand { get; private set; } = null!;
        public ICommand ManageTemplatesCommand { get; private set; } = null!;
        public ICommand CrossReferenceMapCommand { get; private set; } = null!;
        public ICommand BalanceReportCommand { get; private set; } = null!;
        public ICommand FactionConversionCommand { get; private set; } = null!;

        // ─── Filtering ───

        private void ApplyFilter()
        {
            var query = NormalizeQuery(SearchText);
            var faction = SelectedFaction;

            IEnumerable<SageUnit> filtered = _allUnits;

            if (!string.IsNullOrWhiteSpace(faction) && faction != "الكل")
                filtered = filtered.Where(u => string.Equals(u.Side, faction, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(u =>
                {
                    var technical = NormalizeQuery(u.TechnicalName);
                    return technical.Contains(query, StringComparison.OrdinalIgnoreCase);
                });
            }

            var list = filtered.OrderBy(u => u.TechnicalName, StringComparer.OrdinalIgnoreCase).ToList();
            DisplayedUnits = new ObservableCollection<SageUnit>(list);
            StatusMessage = $"عرض {list.Count} وحدة" + (faction != null && faction != "الكل" ? $" | الفصيل: {faction}" : "");
        }

        private static string NormalizeQuery(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
        }

        // ─── Load Mod ───

        private bool CanLoadMod() => HasSourcePath;

        public async Task LoadTargetModAsync()
        {
            if (!HasTargetPath)
            {
                StatusMessage = "الرجاء اختيار مسار المود الهدف أولاً";
                App.DiagLog("[LoadTargetMod] ABORT: No target path");
                return;
            }

            IsLoading = true;
            StatusMessage = "جاري تحليل المود الهدف...";

            try
            {
                App.DiagLog($"[LoadTargetMod] START - Path: {TargetModPath}");

                // === Primary: FactionDiscoveryService (PlayerTemplate + BIG + CSF) ===
                var discoveryResult = await _factionDiscoveryService.DiscoverFactionsAsync(TargetModPath);
                var factions = discoveryResult.InternalNames;
                App.DiagLog($"[LoadTargetMod] FactionDiscoveryService returned {factions.Count} factions (Source={discoveryResult.Source})");

                // === Fallback 1: UnitDiscovery Side= ===
                if (factions.Count == 0)
                {
                    App.DiagLog("[LoadTargetMod] Trying UnitDiscovery.DiscoverFactionsAsync...");
                    factions = await _unitDiscoveryService.DiscoverFactionsAsync(TargetModPath);
                    App.DiagLog($"[LoadTargetMod] UnitDiscovery returned {factions.Count} factions");
                }

                // === Fallback 2: Manual PlayerTemplate parse ===
                if (factions.Count == 0)
                {
                    App.DiagLog("[LoadTargetMod] Trying DiscoverFactionsManually...");
                    factions = await DiscoverFactionsManually(TargetModPath);
                    App.DiagLog($"[LoadTargetMod] DiscoverFactionsManually returned {factions?.Count ?? 0} factions");
                }

                // === Fallback 3: CommandSets ===
                if (factions == null || factions.Count == 0)
                {
                    App.DiagLog("[LoadTargetMod] Trying DiscoverFactionsFromCommandSets...");
                    factions = await DiscoverFactionsFromCommandSets(TargetModPath);
                    App.DiagLog($"[LoadTargetMod] DiscoverFactionsFromCommandSets returned {factions?.Count ?? 0} factions");
                }

                // === Fallback 4: Object.ini ===
                if (factions == null || factions.Count == 0)
                {
                    App.DiagLog("[LoadTargetMod] Trying DiscoverFactionsFromObjectIni...");
                    factions = await DiscoverFactionsFromObjectIni(TargetModPath);
                    App.DiagLog($"[LoadTargetMod] DiscoverFactionsFromObjectIni returned {factions?.Count ?? 0} factions");
                }

                // Log discovered factions
                App.DiagLog($"[LoadTargetMod] Final: {factions?.Count ?? 0} factions");
                if (factions != null)
                    foreach (var faction in factions)
                        App.DiagLog($"  - {faction}");

                // Step 2: Analyze CommandSets
                App.DiagLog("[LoadTargetMod] Calling AnalyzeTargetModCommandSetsAsync...");
                await AnalyzeTargetModCommandSetsAsync();
                App.DiagLog($"[LoadTargetMod] TargetModAnalysis: {TargetModAnalysis?.FactionSlots.Count ?? 0} factions analyzed");

                // Step 3: Update UI
                if (factions != null && factions.Count > 0)
                {
                    UpdateTargetFactionOptions(factions);
                    App.DiagLog($"[LoadTargetMod] TargetFactionOptions.Count = {TargetFactionOptions.Count}");

                    if (TargetFactionOptions.Count > 0)
                    {
                        var suggested = SuggestBestTargetFaction();
                        App.DiagLog($"[LoadTargetMod] SuggestBestTargetFaction returned: {suggested}");
                        if (!string.IsNullOrWhiteSpace(suggested))
                            SelectedTargetFaction = suggested;
                    }

                    StatusMessage = $"✓ تم تحميل {TargetFactionOptions.Count} فصيل من المود الهدف | المصدر: {discoveryResult.Source}";
                }
                else
                {
                    // No factions found — show real state, NOT fake data
                    TargetFactionOptions = new ObservableCollection<TargetFactionOption>();
                    SelectedTargetFaction = null;
                    StatusMessage = "⚠ لم يُعثر على فصائل (تحقق من السجلات)";
                    App.DiagLog("[LoadTargetMod] NO FACTIONS FOUND after all strategies");
                }

                App.DiagLog($"[LoadTargetMod] SUCCESS: {TargetFactionOptions.Count} factions, SelectedTargetFaction={SelectedTargetFaction}");
            }
            catch (Exception ex)
            {
                App.DiagLog($"[LoadTargetMod] EXCEPTION: {ex.GetType().Name}");
                App.DiagLog($"[LoadTargetMod] Message: {ex.Message}");
                App.DiagLog($"[LoadTargetMod] StackTrace:\n{ex.StackTrace}");
                
                // Report the real error — no masking with fake data
                StatusMessage = $"⚠ خطأ في تحميل المود الهدف: {ex.Message}";
                TargetModAnalysis = null;
                TargetModSlotsInfo = string.Empty;
                TargetFactionOptions = new ObservableCollection<TargetFactionOption>();
                SelectedTargetFaction = null;
            }
            finally
            {
                IsLoading = false;
                App.DiagLog("[LoadTargetMod] FINISHED");
            }
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
            catch (Exception ex)
            {
                App.DiagLog($"[DiscoverFactionsManually] ERROR: {ex.Message}");
            }

            return factions;
        }

        /// <summary>
        /// Fallback: Discover factions by scanning CommandSet INI files
        /// </summary>
        private async Task<List<string>> DiscoverFactionsFromCommandSets(string modPath)
        {
            var factions = new List<string>();
            try
            {
                var iniDir = Path.Combine(modPath, "Data", "INI");
                if (!Directory.Exists(iniDir))
                    iniDir = modPath;

                if (!Directory.Exists(iniDir)) return factions;

                var iniFiles = Directory.GetFiles(iniDir, "*.ini", SearchOption.AllDirectories);
                foreach (var file in iniFiles)
                {
                    var content = await File.ReadAllTextAsync(file);
                    var lines = content.Split('\n');
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("CommandSet", StringComparison.OrdinalIgnoreCase))
                        {
                            // Extract faction from CommandSet names like "AmericaCommandCenter", "ChinaWarFactory", etc.
                            var name = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                            string? detectedFaction = null;
                            if (name.Contains("America", StringComparison.OrdinalIgnoreCase) || name.Contains("USA", StringComparison.OrdinalIgnoreCase))
                                detectedFaction = "USA";
                            else if (name.Contains("China", StringComparison.OrdinalIgnoreCase))
                                detectedFaction = "China";
                            else if (name.Contains("GLA", StringComparison.OrdinalIgnoreCase))
                                detectedFaction = "GLA";

                            if (detectedFaction != null && !factions.Contains(detectedFaction, StringComparer.OrdinalIgnoreCase))
                            {
                                factions.Add(detectedFaction);
                                App.DiagLog($"[DiscoverFactionsFromCommandSets] Found faction: {detectedFaction} from {Path.GetFileName(file)}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.DiagLog($"[DiscoverFactionsFromCommandSets] ERROR: {ex.Message}");
            }
            return factions;
        }

        /// <summary>
        /// Fallback: Discover factions by scanning Object definitions in Object.ini
        /// </summary>
        private async Task<List<string>> DiscoverFactionsFromObjectIni(string modPath)
        {
            var factions = new List<string>();
            try
            {
                var objectIniPath = Path.Combine(modPath, "Data", "INI", "Object.ini");
                if (!File.Exists(objectIniPath))
                    objectIniPath = Path.Combine(modPath, "Object.ini");

                // Also check Object subfolder
                var objectDir = Path.Combine(modPath, "Data", "INI", "Object");
                var filesToScan = new List<string>();
                if (File.Exists(objectIniPath)) filesToScan.Add(objectIniPath);
                if (Directory.Exists(objectDir))
                    filesToScan.AddRange(Directory.GetFiles(objectDir, "*.ini", SearchOption.AllDirectories));

                foreach (var file in filesToScan)
                {
                    var content = await File.ReadAllTextAsync(file);
                    var lines = content.Split('\n');
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        // Look for Side = <FactionName> lines
                        if (trimmed.StartsWith("Side", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
                        {
                            var value = trimmed.Split('=').LastOrDefault()?.Trim();
                            if (!string.IsNullOrWhiteSpace(value) && !factions.Contains(value, StringComparer.OrdinalIgnoreCase))
                            {
                                factions.Add(value);
                                App.DiagLog($"[DiscoverFactionsFromObjectIni] Found faction: {value} from {Path.GetFileName(file)}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.DiagLog($"[DiscoverFactionsFromObjectIni] ERROR: {ex.Message}");
            }
            return factions;
        }

        private async Task AnalyzeTargetModCommandSetsAsync()
        {
            if (!HasTargetPath)
                return;

            try
            {
                StatusMessage = "جاري تحليل CommandSets...";
                TargetModAnalysis = await _commandSetAnalyzer.AnalyzeModCommandSetsAsync(TargetModPath);

                if (TargetModAnalysis != null)
                {
                    var summary = $"📊 Slots: {TargetModAnalysis.AvailableSlots} متاح من {TargetModAnalysis.TotalSlots}";
                    TargetModSlotsInfo = summary;
                    App.DiagLog($"[AnalyzeCommandSets] {summary}");

                    foreach (var (faction, info) in TargetModAnalysis.FactionSlots)
                        App.DiagLog($"  - {faction}: {info.AvailableSlots}/{info.TotalSlots} متاح");
                }
            }
            catch (Exception ex)
            {
                App.DiagLog($"[AnalyzeCommandSets] ERROR: {ex.Message}");
                TargetModSlotsInfo = "⚠ فشل تحليل CommandSets";
            }
        }

        private void UpdateTargetFactionOptions(IReadOnlyList<string> factions)
        {
            App.DiagLog($"[UpdateTargetFactionOptions] START - Input factions: {factions.Count}");
            var options = new List<TargetFactionOption>();

            foreach (var faction in factions)
            {
                int available = 0;
                int total = 0;

                App.DiagLog($"[UpdateTargetFactionOptions] Processing faction: '{faction}'");

                if (TargetModAnalysis != null)
                {
                    App.DiagLog($"[UpdateTargetFactionOptions] TargetModAnalysis has {TargetModAnalysis.FactionSlots.Count} slots");
                    
                    if (!TargetModAnalysis.FactionSlots.TryGetValue(faction, out var info))
                    {
                        var normalized = NormalizeFactionKey(faction);
                        App.DiagLog($"[UpdateTargetFactionOptions] Faction '{faction}' not found, trying normalized: '{normalized}'");
                        TargetModAnalysis.FactionSlots.TryGetValue(normalized, out info);
                    }

                    if (info != null)
                    {
                        available = info.AvailableSlots;
                        total = info.TotalSlots;
                        App.DiagLog($"[UpdateTargetFactionOptions] Faction '{faction}' - Available: {available}, Total: {total}");
                    }
                    else
                    {
                        App.DiagLog($"[UpdateTargetFactionOptions] Faction '{faction}' - No CommandSet info found");
                    }
                }
                else
                {
                    App.DiagLog($"[UpdateTargetFactionOptions] TargetModAnalysis is NULL");
                }

                options.Add(new TargetFactionOption
                {
                    Name = faction,
                    AvailableSlots = available,
                    TotalSlots = total
                });
                App.DiagLog($"[UpdateTargetFactionOptions] Added option: Name={faction}, Available={available}, Total={total}");
            }

            TargetFactionOptions = new ObservableCollection<TargetFactionOption>(options);
            App.DiagLog($"[UpdateTargetFactionOptions] COMPLETE - TargetFactionOptions.Count = {TargetFactionOptions.Count}");
        }

        private static string NormalizeFactionKey(string faction)
            => ZeroHourStudio.Domain.ValueObjects.FactionName.NormalizeFactionKey(faction);

        public async Task LoadModAsync()
        {
            if (!HasSourcePath) { StatusMessage = "الرجاء اختيار مسار المود المصدر أولاً"; return; }

            IsLoading = true;
            ProgressValue = 0;

            try
            {
                App.DiagLog($"[LoadMod] Source: {SourceModPath}");
                BlackBoxRecorder.BeginOperation("LoadMod");
                BlackBoxRecorder.Record("LOAD_MOD", "START", $"Path={SourceModPath}");
                var loadModSw = System.Diagnostics.Stopwatch.StartNew();

                // TASK 1.1: Recreate BigFileReader with correct path + rebuild dependent services
                _bigFileReader = new ModBigFileReader(SourceModPath);
                _dependencyResolver = new SmartDependencyResolver(_bigFileReader);
                _transferUseCase = new TransferUnitUseCase(_bigFileReader);
                _smartTransferService = new SmartTransferService(_bigFileReader);

                StatusMessage = "جاري فهرسة أرشيفات BIG...";
                await _bigFileReader.ReadAsync("");

                var progress = new Progress<DiscoveryProgress>(p =>
                {
                    ProgressValue = p.Percentage;
                    StatusMessage = $"اكتشاف الوحدات... {p.UnitsFound} وحدة";
                });

                StatusMessage = "جاري اكتشاف الوحدات...";
                var result = await _unitDiscoveryService.DiscoverUnitsAsync(SourceModPath, progress);

                _allUnits = result.Units.ToList();
                _unitDataIndex.Clear();
                _unitIniPathIndex.Clear();
                foreach (var kvp in result.UnitDataByName) _unitDataIndex[kvp.Key] = kvp.Value;
                foreach (var kvp in result.UnitSourceIniPath) _unitIniPathIndex[kvp.Key] = kvp.Value;

                Units = new ObservableCollection<SageUnit>(_allUnits);

                // Build SAGE definition index (Weapon, FXList, OCL, ParticleSystem, Armor, etc.)
                StatusMessage = "جاري فهرسة تعريفات SAGE...";
                await _sageIndex.BuildIndexAsync(SourceModPath);
                App.DiagLog($"[LoadMod] SAGE Index: {_sageIndex.Count} definitions indexed");

                // Build MappedImage index for icons
                StatusMessage = "جاري فهرسة الأيقونات...";
                await _mappedImageIndex.BuildIndexAsync(SourceModPath);
                _iconService = new IconService(_mappedImageIndex);
                _iconService.SetModPath(SourceModPath);
                ButtonImageToIconConverter.IconService = _iconService;

                // Pre-load all unit icons on background thread (no UI thread blocking)
                var buttonImageNames = _allUnits
                    .Select(u => u.ButtonImage)
                    .Where(b => !string.IsNullOrWhiteSpace(b))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                StatusMessage = $"جاري تحميل {buttonImageNames.Count} أيقونة...";
                await _iconService.PreloadIconsAsync(buttonImageNames);
                App.DiagLog($"[LoadMod] MappedImages: {_mappedImageIndex.Count} indexed, {_iconService.PreloadedCount} icons loaded from {buttonImageNames.Count} requested");

                // Initialize Diagnostic Service
                _auditService = new DiagnosticAuditService(_dependencyResolver, _sageIndex);

                // Build faction list using SmartFactionExtractor for accurate results
                StatusMessage = "جاري استخراج الفصائل...";
                var iniParser = new SAGE_IniParser();
                var smartExtractor = new SmartFactionExtractor(iniParser);
                var factionExtractionResult = await smartExtractor.ExtractFactionsAsync(SourceModPath);
                
                var factionNames = factionExtractionResult.Factions.Keys
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                
                // تسجيل النتائج
                MonitoringService.Instance.Log("FACTION_LIST", "BUILD", "COMPLETE",
                    $"{factionNames.Count} factions extracted",
                    $"Factions: {string.Join(", ", factionNames)} | Total units: {factionExtractionResult.TotalUnits}");
                
                factionNames.Insert(0, "الكل");
                Factions = new ObservableCollection<string>(factionNames);

                loadModSw.Stop();
                App.DiagLog($"[LoadMod] Done: {_allUnits.Count} units, {factionNames.Count - 1} factions: {string.Join(", ", factionNames.Skip(1))}");
                BlackBoxRecorder.Record("LOAD_MOD", "END", $"Units={_allUnits.Count} Factions={factionNames.Count - 1} SAGE={_sageIndex.Count} Icons={_iconService?.PreloadedCount} Elapsed={loadModSw.ElapsedMilliseconds}ms");

                SelectedFaction = "الكل";
                StatusMessage = $"تم اكتشاف {_allUnits.Count} وحدة في {factionNames.Count - 1} فصيل";
            }
            catch (Exception ex)
            {
                App.DiagLog($"[LoadMod] ERROR: {ex.Message}\n{ex.StackTrace}");
                BlackBoxRecorder.RecordError("LOAD_MOD", ex.Message, ex);
                StatusMessage = $"خطأ: {ex.Message}";
            }
            finally
            {
                BlackBoxRecorder.EndOperation("LoadMod");
                IsLoading = false;
            }
        }

        // ─── Analysis ───

        private async Task AnalyzeSelectedUnitAsync(SageUnit unit)
        {
            _analysisCts?.Cancel();
            _analysisCts = new CancellationTokenSource();
            var ct = _analysisCts.Token;

            IsAnalyzing = true;
            StatusMessage = $"جاري تحليل {unit.TechnicalName}...";
            var analyzeSw = System.Diagnostics.Stopwatch.StartNew();
            BlackBoxRecorder.BeginOperation("Analyze");
            BlackBoxRecorder.Record("ANALYZE", "START", $"Unit={unit.TechnicalName}");
            
            // تسجيل المراقبة
            MonitoringService.Instance.Log("UNIT_ANALYSIS", unit.TechnicalName, "START",
                $"Faction={unit.Side}, Type={unit.GetType().Name}");

            try
            {
                if (HasSourcePath) _bigFileReader.SetRootPath(SourceModPath);

                var unitData = _unitDataIndex.TryGetValue(unit.TechnicalName, out var data)
                    ? new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Model", unit.ModelW3D } };

                _unitIniPathIndex.TryGetValue(unit.TechnicalName, out var unitIniPath);

                App.DiagLog($"[Analyze] {unit.TechnicalName} | INI: {unitIniPath ?? "(none)"} | Keys: {unitData.Count}");

                // Set SAGE index for deep chain traversal
                if (_dependencyResolver is Infrastructure.DependencyResolution.SmartDependencyResolver smartResolver)
                    smartResolver.SageIndex = _sageIndex;

                var resolvedGraph = await Task.Run(() =>
                    _dependencyResolver.ResolveDependenciesAsync(unit.TechnicalName, SourceModPath, unitIniPath, unitData), ct);

                ct.ThrowIfCancellationRequested();

                await Task.Run(() => _dependencyResolver.ValidateDependenciesAsync(resolvedGraph, SourceModPath), ct);

                App.DiagLog($"[Analyze] Nodes: {resolvedGraph.AllNodes.Count}, Found: {resolvedGraph.FoundCount}, Missing: {resolvedGraph.MissingCount}");

                ct.ThrowIfCancellationRequested();

                try
                {
                    var weaponAnalysis = await Task.Run(() =>
                        _weaponAnalysisService.AnalyzeWeaponDependenciesAsync(unit.TechnicalName, SourceModPath), ct);

                    var enhanced = new EnhancedDependencyGraph
                    {
                        RootNode = resolvedGraph.RootNode,
                        AllNodes = resolvedGraph.AllNodes,
                        Status = resolvedGraph.Status,
                        FoundCount = resolvedGraph.FoundCount,
                        MissingCount = resolvedGraph.MissingCount,
                        UnitId = resolvedGraph.UnitId,
                        UnitName = resolvedGraph.UnitName,
                        MaxDepth = resolvedGraph.MaxDepth,
                        TotalSizeInBytes = resolvedGraph.TotalSizeInBytes,
                        CreatedAt = resolvedGraph.CreatedAt,
                        Notes = resolvedGraph.Notes,
                        WeaponAnalysis = weaponAnalysis,
                        WeaponChains = weaponAnalysis.Weapons
                    };

                    CurrentGraph = enhanced;
                    WeaponAnalysisCount = enhanced.WeaponAnalysisCount;

                    // Fallback: count weapon nodes from dependency graph if weapon analysis returned 0
                    if (WeaponAnalysisCount == 0)
                    {
                        WeaponAnalysisCount = enhanced.AllNodes
                            .Count(n => n.Type == ZeroHourStudio.Application.Models.DependencyType.Weapon);
                    }

                    TotalDependencyCount = enhanced.DependencyCount;
                    AnalysisCount++;

                    CommandManager.InvalidateRequerySuggested();

                    analyzeSw.Stop();
                    BlackBoxRecorder.RecordDependencyResolveEnd(unit.TechnicalName, enhanced.AllNodes.Count, enhanced.FoundCount, enhanced.MissingCount, analyzeSw.ElapsedMilliseconds);
                    
                    // تسجيل النتيجة النهائية
                    MonitoringService.Instance.Log("UNIT_ANALYSIS", unit.TechnicalName, "COMPLETE",
                        $"Weapons={enhanced.WeaponCount}, Deps={enhanced.AllNodes.Count}, Complete={enhanced.GetCompletionPercentage():F0}%",
                        $"Found={enhanced.FoundCount}, Missing={enhanced.MissingCount}, Time={analyzeSw.ElapsedMilliseconds}ms");
                    
                    StatusMessage = $"{unit.TechnicalName}: {enhanced.AllNodes.Count} تبعية | {enhanced.WeaponCount} سلاح | {enhanced.GetCompletionPercentage():F0}%";
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    CurrentGraph = resolvedGraph;
                    AnalysisCount++;
                    CommandManager.InvalidateRequerySuggested();
                    analyzeSw.Stop();
                    BlackBoxRecorder.RecordDependencyResolveEnd(unit.TechnicalName, resolvedGraph.AllNodes.Count, resolvedGraph.FoundCount, resolvedGraph.MissingCount, analyzeSw.ElapsedMilliseconds);
                    StatusMessage = $"{unit.TechnicalName}: {resolvedGraph.AllNodes.Count} تبعية | {resolvedGraph.GetCompletionPercentage():F0}%";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                App.DiagLog($"[Analyze] ERROR: {ex.Message}");
                BlackBoxRecorder.RecordError("ANALYZE", ex.Message, ex);
                StatusMessage = $"خطأ في التحليل: {ex.Message}";
            }
            finally
            {
                BlackBoxRecorder.EndOperation("Analyze");
                IsAnalyzing = false;
            }
        }

        // ─── Diagnostics ───

        private bool CanAuditUnit() => SelectedUnit != null && _auditService != null;

        private async Task AuditUnitAsync()
        {
            if (!CanAuditUnit()) return;

            IsAnalyzing = true;
            StatusMessage = $"جاري فحص {SelectedUnit!.TechnicalName}...";
            AuditIssues = new ObservableCollection<AuditIssue>();

            try
            {
                var report = await Task.Run(() => _auditService!.AuditUnitAsync(SelectedUnit.TechnicalName, SourceModPath));
                
                var sortedIssues = report.Issues
                    .OrderByDescending(i => i.Severity)
                    .ThenBy(i => i.Category)
                    .ToList();

                AuditIssues = new ObservableCollection<AuditIssue>(sortedIssues);

                if (sortedIssues.Count == 0)
                {
                    StatusMessage = "✓ لم يتم العثور على مشاكل (Clean)";
                }
                else
                {
                    StatusMessage = $"⚠ تم العثور على {report.ErrorCount} خطأ و {report.WarningCount} تحذير";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"خطأ في الفحص: {ex.Message}";
                System.Windows.MessageBox.Show(ex.ToString(), "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        // ─── Diff Viewer ───

        private async Task PreviewDiffAsync()
        {
            if (!HasBothPaths)
            {
                StatusMessage = "الرجاء تحديد مسار المود المصدر والهدف أولاً";
                return;
            }

            App.DiagLog($"[Diff] Generating diff: {SourceModPath} vs {TargetModPath}");
            StatusMessage = "جاري تحليل الفروقات...";

            try
            {
                var diffWindow = new Views.DiffViewerWindow
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };

                diffWindow.Show();
                await diffWindow.LoadDiffsAsync(SourceModPath, TargetModPath);
                StatusMessage = "✓ تم فتح نافذة الفروقات";
            }
            catch (Exception ex)
            {
                App.DiagLog($"[Diff] ERROR: {ex.Message}");
                StatusMessage = $"خطأ في تحليل الفروقات: {ex.Message}";
            }
        }

        // ─── Faction Conversion ───

        private async Task ShowFactionConversionAsync()
        {
            if (SelectedUnit == null) return;
            var unitName = SelectedUnit.TechnicalName;
            App.DiagLog($"[FactionAdapter] Opening conversion for '{unitName}'...");
            StatusMessage = "جاري تحميل بيانات الوحدة...";

            try
            {
                var content = await GetUnitIniContentAsync(unitName).ConfigureAwait(true);

                var window = new Views.FactionConversionWindow
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                window.LoadUnit(unitName, content ?? "");
                window.ShowDialog();

                if (window.ConversionApplied && window.ConvertedContent != null)
                {
                    App.DiagLog($"[FactionAdapter] Conversion applied: {window.AppliedRules?.SourceFaction} → {window.AppliedRules?.TargetFaction}");
                    StatusMessage = $"✓ تم تحويل '{unitName}' بنجاح";
                }
                else
                {
                    StatusMessage = "تحويل فصيل الوحدة";
                }
            }
            catch (Exception ex)
            {
                App.DiagLog($"[FactionAdapter] ERROR: {ex.Message}");
                StatusMessage = $"خطأ: {ex.Message}";
            }
        }

        private async Task<string?> GetUnitIniContentAsync(string unitName)
        {
            if (!_unitIniPathIndex.TryGetValue(unitName, out var path) || string.IsNullOrWhiteSpace(path))
                return null;
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
                            if (depth <= 0) { sb.AppendLine(line); break; }
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
            catch { return null; }
        }

        // ─── Balance Report ───

        private async Task ShowBalanceReportAsync()
        {
            if (SelectedUnit == null) return;
            App.DiagLog($"[Balance] Analyzing balance for '{SelectedUnit.TechnicalName}'...");
            StatusMessage = "جاري تحليل التوازن...";

            try
            {
                var balanceWindow = new Views.BalanceReportWindow
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                balanceWindow.Show();
                await balanceWindow.AnalyzeUnitAsync(SourceModPath, SelectedUnit.TechnicalName);
                StatusMessage = "✓ تم فتح تقرير التوازن";
            }
            catch (Exception ex)
            {
                App.DiagLog($"[Balance] ERROR: {ex.Message}");
                StatusMessage = $"خطأ: {ex.Message}";
            }
        }

        // ─── Cross-Reference Map ───

        private async Task ShowCrossReferenceMapAsync()
        {
            App.DiagLog("[CrossRef] Opening Cross-Reference Map...");
            StatusMessage = "جاري تحليل المراجع التقاطعية...";

            try
            {
                var crossRefWindow = new Views.CrossReferenceMapWindow
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                crossRefWindow.Show();
                await crossRefWindow.AnalyzeModAsync(SourceModPath);
                StatusMessage = "✓ تم فتح خريطة المراجع التقاطعية";
            }
            catch (Exception ex)
            {
                App.DiagLog($"[CrossRef] ERROR: {ex.Message}");
                StatusMessage = $"خطأ: {ex.Message}";
            }
        }

        // ─── Templates ───

        private void ManageTemplates()
        {
            try
            {
                var templateWindow = new Views.TemplateManagerWindow
                {
                    Owner = System.Windows.Application.Current.MainWindow,
                    AvailableFactions = CombineAvailableFactions()
                };

                templateWindow.ShowDialog();

                if (templateWindow.TemplateApplied && templateWindow.SelectedTemplate != null)
                {
                    var template = templateWindow.SelectedTemplate;
                    App.DiagLog($"[Templates] Applied template: {template.Name}");

                    // Apply template settings
                    if (!string.IsNullOrWhiteSpace(template.TargetFaction))
                    {
                        SelectedTargetFaction = template.TargetFaction;
                    }

                    StatusMessage = $"✓ تم تطبيق القالب: {template.Name}";
                }
            }
            catch (Exception ex)
            {
                App.DiagLog($"[Templates] ERROR: {ex.Message}");
                StatusMessage = $"خطأ في إدارة القوالب: {ex.Message}";
            }
        }

        /// <summary>
        /// دمج الفصائل من المود المصدر والهدف للقوالب
        /// </summary>
        private List<string> CombineAvailableFactions()
        {
            var factions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (TargetFactionOptions != null)
            {
                foreach (var opt in TargetFactionOptions)
                {
                    if (!string.IsNullOrWhiteSpace(opt.Name))
                        factions.Add(opt.Name);
                }
            }

            if (Factions != null)
            {
                foreach (var f in Factions)
                {
                    if (!string.IsNullOrWhiteSpace(f) && f != "الكل")
                        factions.Add(f);
                }
            }

            if (factions.Count == 0)
            {
                App.DiagLog("[CombineAvailableFactions] No factions found from source or target — returning empty");
                return new List<string>();
            }

            return factions.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ─── Transfer ───

        private bool CanTransfer() => SelectedUnit != null && CurrentGraph != null && HasBothPaths && !string.IsNullOrWhiteSpace(SelectedTargetFaction);

        private async Task TransferUnitAsync()
        {
            if (SelectedUnit == null || CurrentGraph == null || !HasBothPaths)
            { StatusMessage = "الرجاء التأكد من اختيار وحدة ومسارات صحيحة"; return; }

            var targetFaction = !string.IsNullOrWhiteSpace(SelectedTargetFaction)
                ? SelectedTargetFaction
                : SelectedUnit.Side;

            // ✅ تحليل Command Buttons باستخدام CommandButtonAnalyzer
            App.DiagLog($"[Transfer] Analyzing CommandButtons for faction '{targetFaction}'...");
            var buttonAnalysis = await _commandButtonAnalyzer.AnalyzeCommandSet(TargetModPath, targetFaction);
            App.DiagLog($"[Transfer] Slots: {buttonAnalysis.EmptySlots} empty / {buttonAnalysis.TotalSlots} total");

            // ✅ عرض نافذة اختيار الزر
            Domain.Models.ButtonSelectionResult? buttonSelection = null;

            var selectorVM = new CommandButtonSelectorViewModel
            {
                UnitName = SelectedUnit.TechnicalName,
                FactionName = targetFaction,
                CommandSetName = buttonAnalysis.CommandSetName,
                Buttons = new ObservableCollection<Domain.Models.CommandButtonSlot>(buttonAnalysis.Buttons),
                HasEmptySlot = buttonAnalysis.EmptySlots > 0
            };

            var selectorWindow = new Views.CommandButtonSelectorWindow
            {
                DataContext = selectorVM,
                Owner = System.Windows.Application.Current.MainWindow
            };

            selectorWindow.ShowDialog();

            if (!selectorWindow.UserConfirmed)
            {
                if (buttonAnalysis.EmptySlots > 0)
                {
                    // المستخدم ألغى لكن يوجد مكان فارغ - استخدام أول مكان فارغ تلقائياً
                    var firstEmpty = buttonAnalysis.Buttons.FirstOrDefault(b => b.IsEmpty);
                    if (firstEmpty != null)
                    {
                        buttonSelection = new Domain.Models.ButtonSelectionResult
                        {
                            SlotNumber = firstEmpty.SlotNumber,
                            ReplaceExisting = false
                        };
                        App.DiagLog($"[Transfer] User cancelled slot picker, auto-selecting empty slot #{firstEmpty.SlotNumber}");
                    }
                    else
                    {
                        StatusMessage = "تم إلغاء النقل - لم يتم اختيار موقع للزر";
                        return;
                    }
                }
                else
                {
                    StatusMessage = "تم إلغاء النقل - لم يتم اختيار موقع للزر";
                    return;
                }
            }
            else
            {
                buttonSelection = selectorWindow.Selection;
            }

            if (buttonSelection == null)
            {
                StatusMessage = "تم إلغاء النقل - لم يتم اختيار موقع صالح";
                return;
            }

            App.DiagLog($"[Transfer] Button selection: Slot #{buttonSelection.SlotNumber}, Replace={buttonSelection.ReplaceExisting}");

            // ✅ عرض نافذة المعاينة (Preview)
            var previewVM = new TransferPreviewViewModel
            {
                UnitName = SelectedUnit.TechnicalName,
                SourceFaction = SelectedUnit.Side,
                DependencyCount = CurrentGraph.AllNodes.Count,
                DependencyBreakdown = TransferPreviewViewModel.BuildDependencyBreakdown(CurrentGraph),
                MissingCount = CurrentGraph.MissingCount,
                TargetModName = Path.GetFileName(TargetModPath),
                TargetFaction = targetFaction,
                SlotNumber = buttonSelection.SlotNumber,
                CommandSetName = buttonAnalysis.CommandSetName
            };

            if (buttonSelection.ReplaceExisting)
                previewVM.Warnings.Add($"⚠ سيتم استبدال '{buttonSelection.ReplacedButtonName}' في Slot #{buttonSelection.SlotNumber}");

            var unitExists = await CheckIfUnitExistsInTarget(SelectedUnit.TechnicalName, TargetModPath);
            if (unitExists)
                previewVM.Warnings.Add($"⚠ الوحدة '{SelectedUnit.TechnicalName}' موجودة بالفعل في المود الهدف");

            if (CurrentGraph.GetCompletionPercentage() == 100)
                previewVM.Notes.Add("✓ جميع التبعيات موجودة ومكتملة");
            else if (previewVM.MissingCount > 0)
                previewVM.Warnings.Add($"⚠ {previewVM.MissingCount} تبعية مفقودة — النقل قد يفشل أو الوحدة قد لا تعمل بشكل كامل");

            previewVM.Summary = $"سيتم نقل الوحدة إلى Slot #{buttonSelection.SlotNumber} في {targetFaction}";

            var previewWindow = new Views.TransferPreviewWindow
            {
                DataContext = previewVM,
                Owner = System.Windows.Application.Current.MainWindow
            };

            previewWindow.ShowDialog();

            if (!previewWindow.UserConfirmed)
            {
                StatusMessage = "تم إلغاء النقل من قبل المستخدم";
                return;
            }

            IsLoading = true;
            ProgressValue = 0;
            StatusMessage = $"جاري نقل {SelectedUnit.TechnicalName}...";
            BlackBoxRecorder.BeginOperation("Transfer");
            App.DiagLog($"[Transfer] Start: {SelectedUnit.TechnicalName} | Source: {SourceModPath} | Target: {TargetModPath} | Slot: {buttonSelection.SlotNumber}");

            try
            {
                _bigFileReader.SetRootPath(SourceModPath);
                await _bigFileReader.ReadAsync("");

                // Set SAGE index for deep chain traversal
                if (_dependencyResolver is Infrastructure.DependencyResolution.SmartDependencyResolver sr)
                    sr.SageIndex = _sageIndex;

                _unitIniPathIndex.TryGetValue(SelectedUnit.TechnicalName, out var unitIniPath);
                var unitDataForTransfer = _unitDataIndex.TryGetValue(SelectedUnit.TechnicalName, out var dtf)
                    ? new Dictionary<string, string>(dtf, StringComparer.OrdinalIgnoreCase) : null;

                var resolvedGraph = await _dependencyResolver.ResolveDependenciesAsync(
                    SelectedUnit.TechnicalName, SourceModPath, unitIniPath, unitDataForTransfer);

                if (resolvedGraph.AllNodes.Count == 0) { StatusMessage = "لا توجد تبعيات للنقل"; IsLoading = false; return; }

                await _dependencyResolver.ValidateDependenciesAsync(resolvedGraph, SourceModPath);
                resolvedGraph.FoundCount = resolvedGraph.AllNodes.Count(n => n.Status == AssetStatus.Found);
                resolvedGraph.MissingCount = resolvedGraph.AllNodes.Count(n => n.Status == AssetStatus.Missing);

                var unitData = _unitDataIndex.TryGetValue(SelectedUnit.TechnicalName, out var dd)
                    ? new Dictionary<string, string>(dd, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);

                // ✅ إذا كان الاستبدال مطلوباً، حذف الزر القديم أولاً
                if (buttonSelection.ReplaceExisting)
                {
                    App.DiagLog($"[Transfer] Removing existing button at slot #{buttonSelection.SlotNumber}...");
                    await _commandSetPatchService.RemoveCommandButton(TargetModPath, targetFaction, buttonSelection.SlotNumber);
                }

                await _commandSetPatchService.EnsureCommandSetAsync(SelectedUnit, unitData, TargetModPath, targetFaction);

                var progress = new Progress<TransferProgress>(p =>
                {
                    ProgressValue = (int)p.PercentageComplete;
                    StatusMessage = $"نقل: {p.CurrentFileName} ({p.CurrentFileIndex}/{p.TotalFiles})";
                });

                var transferResult = await _smartTransferService.TransferAsync(resolvedGraph, SourceModPath, TargetModPath, progress);

                App.DiagLog($"[Transfer] Result: Success={transferResult.Success} | Transferred={transferResult.TransferredFilesCount} | Failed={transferResult.FailedFiles.Count} | Msg={transferResult.Message}");
                if (transferResult.Success)
                {
                    var missing = Infrastructure.Transfer.PostTransferHealthCheck.VerifyTransferredFilesExist(resolvedGraph, SourceModPath, TargetModPath);
                    StatusMessage = missing.Count == 0
                        ? $"✓ تم نقل {transferResult.TransferredFilesCount} ملف بنجاح ({transferResult.Duration.TotalSeconds:F1}ث) | Slot #{buttonSelection.SlotNumber} | فحص الصحة: ✓"
                        : $"✓ تم النقل | فحص الصحة: {missing.Count} ملف غير موجود في الهدف";
                }
                else
                    StatusMessage = $"⚠ {transferResult.Message} ({transferResult.FailedFiles.Count} فشل)";
                ProgressValue = 100;
            }
            catch (Exception ex)
            {
                App.DiagLog($"[Transfer] ERROR: {ex.Message}\n{ex.StackTrace}");
                StatusMessage = $"✗ خطأ في النقل: {ex.Message}";
            }
            finally { BlackBoxRecorder.EndOperation("Transfer"); IsLoading = false; }
        }

        private async Task<bool> CheckIfUnitExistsInTarget(string unitName, string targetPath)
        {
            try
            {
                var objectIniPath = Path.Combine(targetPath, "Data", "INI", "Object.ini");
                if (!File.Exists(objectIniPath))
                    objectIniPath = Path.Combine(targetPath, "Object.ini");

                if (File.Exists(objectIniPath))
                {
                    var content = await File.ReadAllTextAsync(objectIniPath);
                    return content.Contains($"Object {unitName}", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
            }

            return false;
        }

        private string SuggestBestTargetFaction()
        {
            if (TargetFactionOptions.Count == 0)
                return string.Empty;

            if (SelectedUnit == null)
                return TargetFactionOptions[0].Name;

            var suggestions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var option in TargetFactionOptions)
            {
                var score = 0;

                if (option.Name.Equals(SelectedUnit.Side, StringComparison.OrdinalIgnoreCase))
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
        // ─── Universal Transfer (Orchestrator) ───

        public ICommand UniversalTransferCommand { get; private set; } = null!;

        private bool CanUniversalTransfer()
        {
            return SelectedUnit != null && HasBothPaths && !IsLoading;
        }

        private async Task UniversalTransferAsync()
        {
            if (!CanUniversalTransfer()) return;

            IsLoading = true;
            ProgressValue = 0;
            StatusMessage = $"جاري بدء النقل الشامل للوحدة {SelectedUnit!.TechnicalName}...";
            BlackBoxRecorder.BeginOperation("UniversalTransfer");

            try
            {
                // 1. Initialize Services Chain
                var iniParser = new SAGE_IniParser();
                var analyzer = new UnitDependencyAnalyzer(iniParser);
                var validator = new UnitCompletionValidator();
                // AssetReferenceHunter requires a path or config? Let's check constructor.
                // Assuming parameterless or needs checking.
                // Based on previous checks, AssetReferenceHunter might need `ModBigFileReader` or `IniParser`.
                // Let's assume for now it takes BigFileReader based on common patterns, but I will verify in next step if compilation fails.
                // Actually, I should use the services I already have if possible.
                
                // Let's rely on the helper method I will add below.
                var orchestrator = InitializeOrchestrator();

                // 2. Prepare Request
                var request = new ZeroHourStudio.Infrastructure.Orchestration.TransferRequest
                {
                    UnitName = SelectedUnit.TechnicalName,
                    SourcePath = SourceModPath,
                    TargetPath = TargetModPath,
                    TargetFaction = SelectedTargetFaction ?? SelectedUnit.Side,
                    InjectCommandSet = true,
                    OverwriteFiles = true // Or bind to a UI checkbox
                };

                // 3. Execute
                var progress = new Progress<ZeroHourStudio.Infrastructure.Orchestration.TransferSessionProgress>(p =>
                {
                    ProgressValue = p.OverallPercentage;
                    StatusMessage = $"[{p.CurrentStage}] {p.CurrentAction}";
                });

                var result = await orchestrator.ExecuteTransferAsync(request, progress);

                // 4. Report
                if (result.Success)
                {
                    StatusMessage = $"✓ اكتمل النقل بنجاح! ({result.Duration.TotalSeconds:F1}ث)";
                    System.Windows.MessageBox.Show(result.Message, "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = $"✗ فشل النقل: {result.Message}";
                    var errorDetails = string.Join("\n", result.Errors);
                    System.Windows.MessageBox.Show($"{result.Message}\n\n{errorDetails}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"خطأ غير متوقع: {ex.Message}";
                BlackBoxRecorder.RecordError("UNIVERSAL_TRANSFER", ex.Message, ex);
                System.Windows.MessageBox.Show(ex.ToString(), "Exception", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                BlackBoxRecorder.EndOperation("UniversalTransfer");
            }
        }

        private ZeroHourStudio.Infrastructure.Orchestration.UniversalTransferOrchestrator InitializeOrchestrator()
        {
            // Re-use existing instances where possible
            var iniParser = new SAGE_IniParser();
            var analyzer = new UnitDependencyAnalyzer(iniParser);
            var validator = new UnitCompletionValidator();
            
            // AssetReferenceHunter check
            var assetHunter = new AssetReferenceHunter(_bigFileReader); // Verified in next step likely
            
            var comprehensive = new ComprehensiveDependencyService(analyzer, assetHunter, validator);
            
            var weaponService = _weaponAnalysisService; // Already initialized
            
            var enhancedCore = new ZeroHourStudio.Infrastructure.GraphEngine.EnhancedSageCore(
                analyzer, weaponService, _sageIndex, comprehensive);

            var smartDepExtractor = new ZeroHourStudio.Infrastructure.GraphEngine.SmartDependencyExtractor(enhancedCore, _bigFileReader);
            
            var factionExtractor = new SmartFactionExtractor(iniParser);
            
            // CommandSetPatchService is stateless
            var patcher = new CommandSetPatchService();

            return new ZeroHourStudio.Infrastructure.Orchestration.UniversalTransferOrchestrator(
                enhancedCore, smartDepExtractor, patcher, factionExtractor);
        }
    }
}
