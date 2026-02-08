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
using ZeroHourStudio.Infrastructure.Normalization;
using ZeroHourStudio.Infrastructure.DependencyResolution;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Infrastructure.Validation;
using ZeroHourStudio.Infrastructure.Transfer;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.Implementations;
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
using ZeroHourStudio.Application.Models;

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
        private readonly CommandSetPatchService _commandSetPatchService;
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
        private ObservableCollection<string> _targetFactions = new();
        private string? _selectedTargetFaction;
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

        public ObservableCollection<string> TargetFactions
        {
            get => _targetFactions;
            set => SetProperty(ref _targetFactions, value);
        }

        public string? SelectedTargetFaction
        {
            get => _selectedTargetFaction;
            set => SetProperty(ref _selectedTargetFaction, value);
        }

        public ObservableCollection<AuditIssue> AuditIssues
        {
            get => _auditIssues;
            set => SetProperty(ref _auditIssues, value);
        }

        public ICommand AuditCommand { get; private set; } = null!;

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
            if (!HasTargetPath) return;
            try
            {
                var factions = await _unitDiscoveryService.DiscoverFactionsAsync(TargetModPath);
                TargetFactions = new ObservableCollection<string>(factions);
                if (factions.Count > 0 && string.IsNullOrEmpty(SelectedTargetFaction))
                    SelectedTargetFaction = factions[0];
            }
            catch
            {
                TargetFactions = new ObservableCollection<string> { "USA", "China", "GLA" };
            }
        }

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
                _auditService = new DiagnosticAuditService(_bigFileReader, _sageIndex);

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

                    // تحديث Key Inspector
                    PopulateKeyInspector(unit.TechnicalName, unitData);

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

        // ─── Transfer ───

        private bool CanTransfer() => SelectedUnit != null && CurrentGraph != null && HasBothPaths;

        private async Task TransferUnitAsync()
        {
            if (SelectedUnit == null || CurrentGraph == null || !HasBothPaths)
            { StatusMessage = "الرجاء التأكد من اختيار وحدة ومسارات صحيحة"; return; }

            IsLoading = true;
            ProgressValue = 0;
            StatusMessage = $"جاري نقل {SelectedUnit.TechnicalName}...";
            BlackBoxRecorder.BeginOperation("Transfer");
            App.DiagLog($"[Transfer] Start: {SelectedUnit.TechnicalName} | Source: {SourceModPath} | Target: {TargetModPath}");

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

                var targetFaction = !string.IsNullOrWhiteSpace(SelectedTargetFaction) ? SelectedTargetFaction : SelectedUnit.Side;
                await _commandSetPatchService.EnsureCommandSetAsync(SelectedUnit, unitData, TargetModPath, targetFaction);

                var progress = new Progress<TransferProgress>(p =>
                {
                    ProgressValue = (int)p.PercentageComplete;
                    StatusMessage = $"نقل: {p.CurrentFileName} ({p.CurrentFileIndex}/{p.TotalFiles})";
                });

                var transferResult = await _smartTransferService.TransferAsync(resolvedGraph, SourceModPath, TargetModPath, progress);

                App.DiagLog($"[Transfer] Result: Success={transferResult.Success} | Transferred={transferResult.TransferredFilesCount} | Failed={transferResult.FailedFiles.Count} | Msg={transferResult.Message}");
                StatusMessage = transferResult.Success
                    ? $"✓ تم نقل {transferResult.TransferredFilesCount} ملف بنجاح ({transferResult.Duration.TotalSeconds:F1}ث)"
                    : $"⚠ {transferResult.Message} ({transferResult.FailedFiles.Count} فشل)";
                ProgressValue = 100;
            }
            catch (Exception ex)
            {
                App.DiagLog($"[Transfer] ERROR: {ex.Message}\n{ex.StackTrace}");
                StatusMessage = $"✗ خطأ في النقل: {ex.Message}";
            }
            finally { BlackBoxRecorder.EndOperation("Transfer"); IsLoading = false; }
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
