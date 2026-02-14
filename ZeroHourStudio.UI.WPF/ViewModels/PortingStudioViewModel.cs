using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.Archives;
using ZeroHourStudio.Infrastructure.ConflictResolution;
using ZeroHourStudio.Infrastructure.DependencyResolution;
using ZeroHourStudio.Infrastructure.Implementations;
using ZeroHourStudio.Infrastructure.Localization;
using ZeroHourStudio.Infrastructure.Logging;
using ZeroHourStudio.Infrastructure.Monitoring;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.Transfer;
using ZeroHourStudio.UI.WPF.Commands;
using ZeroHourStudio.UI.WPF.Converters;
using ZeroHourStudio.UI.WPF.Core;
using ZeroHourStudio.UI.WPF.Services;

namespace ZeroHourStudio.UI.WPF.ViewModels;

/// <summary>
/// ViewModel الرئيسي لاستوديو النقل v3.0 - المنسق الأعلى
/// يحتوي على SourcePaneVM + TargetPaneVM ويدير خط النقل
/// </summary>
public class PortingStudioViewModel : ViewModelBase
{
    // === Sub-ViewModels ===
    public SourcePaneViewModel SourcePane { get; }
    public TargetPaneViewModel TargetPane { get; }
    public DependencyGraphViewModel DependencyGraph { get; }
    public ConflictResolutionViewModel ConflictResolution { get; }
    public CsfEditorViewModel CsfEditor { get; }

    // === Services ===
    private SmartDependencyResolver _dependencyResolver = null!;
    private SmartTransferService _transferService = null!;
    private TransferPipelineService _pipeline = null!;
    private readonly ConflictDetectionService _conflictDetection;
    private readonly SmartRenamingService _renamingService;
    private readonly VirtualFileSystem _virtualFs;
    private readonly CsfLocalizationService _csfService;
    private readonly CommandSetPatchService _commandSetPatch;
    private SageDefinitionIndex _sageIndex;
    private ModBigFileReader _bigFileReader = null!;
    private MappedImageIndex _mappedImageIndex;
    private IconService? _iconService;
    private MonitoredWeaponAnalysisService? _weaponAnalysis;
    private readonly UnitDiscoveryService _unitDiscovery;
    private readonly CommandSetAnalyzer _commandSetAnalyzer;
    private readonly CommandButtonAnalyzer _commandButtonAnalyzer;
    private readonly RollbackService _rollbackService;
    private readonly GameTargetAnalyzer _gameTargetAnalyzer = new();
    private readonly AdaptiveTransferEngine _adaptiveEngine = new();
    private TargetGameProfile? _targetProfile;
    // === SAGE Relational Data Engine ===
    private readonly CommandChainService _commandChainService = new();
    private readonly TransferSanitizer _transferSanitizer = new();
    private Services.GameImageLoader? _gameImageLoader;

    // === IntelligentPreview — نتائج التشخيص الذكي ===
    public IntelligentPreviewViewModel? LastPreviewResult { get; private set; }

    // === Transfer History ===
    private ObservableCollection<TransferJournalEntry> _transferHistory = new();
    public ObservableCollection<TransferJournalEntry> TransferHistory
    {
        get => _transferHistory;
        set => SetProperty(ref _transferHistory, value);
    }

    // === State ===
    private string _statusMessage = "مرحباً بك في استوديو النقل v3.0";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private int _progressValue;
    public int ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    private bool _isTransferring;
    public bool IsTransferring
    {
        get => _isTransferring;
        set => SetProperty(ref _isTransferring, value);
    }

    private bool _showConflictDialog;
    public bool ShowConflictDialog
    {
        get => _showConflictDialog;
        set => SetProperty(ref _showConflictDialog, value);
    }

    private bool _showCsfEditor;
    public bool ShowCsfEditor
    {
        get => _showCsfEditor;
        set => SetProperty(ref _showCsfEditor, value);
    }

    // === Unit Preview (Cameo Viewer) ===
    private System.Windows.Media.Imaging.BitmapSource? _selectedUnitPreviewImage;
    public System.Windows.Media.Imaging.BitmapSource? SelectedUnitPreviewImage
    {
        get => _selectedUnitPreviewImage;
        set => SetProperty(ref _selectedUnitPreviewImage, value);
    }

    private string _selectedUnitName = string.Empty;
    public string SelectedUnitName
    {
        get => _selectedUnitName;
        set => SetProperty(ref _selectedUnitName, value);
    }

    private string _selectedUnitInfo = string.Empty;
    public string SelectedUnitInfo
    {
        get => _selectedUnitInfo;
        set => SetProperty(ref _selectedUnitInfo, value);
    }

    private string _selectedUnitModel = string.Empty;
    public string SelectedUnitModel
    {
        get => _selectedUnitModel;
        set => SetProperty(ref _selectedUnitModel, value);
    }

    public bool HasSelectedUnit => SourcePane.SelectedUnit != null;

    // DropZone state: Idle, DragOver, Analyzing, Ready, Transferring
    private string _dropZoneState = "Idle";
    public string DropZoneState
    {
        get => _dropZoneState;
        set => SetProperty(ref _dropZoneState, value);
    }

    // === Commands ===
    public ICommand TransferSelectedCommand { get; private set; } = null!;
    public ICommand ToggleCsfEditorCommand { get; private set; } = null!;
    public ICommand BatchTransferCommand { get; private set; } = null!;
    public ICommand RollbackLastCommand { get; private set; } = null!;
    public ICommand LoadHistoryCommand { get; private set; } = null!;
    public ICommand PreviewDiffCommand { get; private set; } = null!;
    public ICommand ManageTemplatesCommand { get; private set; } = null!;
    public ICommand CrossReferenceMapCommand { get; private set; } = null!;
    public ICommand BalanceReportCommand { get; private set; } = null!;
    public ICommand FactionConversionCommand { get; private set; } = null!;
    public ICommand ManageProfilesCommand { get; private set; } = null!;
    public ICommand ExportTransferLogCommand { get; private set; } = null!;
    public ICommand ImportTransferLogCommand { get; private set; } = null!;
    public ICommand ValidateIniCommand { get; private set; } = null!;
    public ICommand W3dPreviewCommand { get; private set; } = null!;

    public PortingStudioViewModel()
    {
        // إنشاء الخدمات
        _conflictDetection = ServiceFactory.CreateConflictDetection();
        _renamingService = ServiceFactory.CreateSmartRenaming();
        _virtualFs = ServiceFactory.CreateVirtualFileSystem();
        _csfService = ServiceFactory.CreateCsfLocalization();
        _commandSetPatch = ServiceFactory.CreateCommandSetPatch();
        _sageIndex = ServiceFactory.CreateSageIndex();
        _mappedImageIndex = ServiceFactory.CreateMappedImageIndex();
        _unitDiscovery = ServiceFactory.CreateUnitDiscovery();
        _commandSetAnalyzer = new CommandSetAnalyzer(new SAGE_IniParser());
        _commandButtonAnalyzer = new CommandButtonAnalyzer(_commandSetAnalyzer);
        _rollbackService = ServiceFactory.CreateRollbackService();

        // إنشاء sub-ViewModels
        SourcePane = new SourcePaneViewModel();
        TargetPane = new TargetPaneViewModel();
        DependencyGraph = new DependencyGraphViewModel();
        ConflictResolution = new ConflictResolutionViewModel();
        CsfEditor = new CsfEditorViewModel(_csfService);

        // ربط أحداث
        SourcePane.ModLoaded += OnSourceModLoaded;
        SourcePane.UnitSelected += OnUnitSelected;
        TargetPane.ModLoaded += OnTargetModLoaded;
        TargetPane.UnitDropped += OnUnitDropped;

        InitializeCommands();
    }

    private void InitializeCommands()
    {
        TransferSelectedCommand = new AsyncRelayCommand(
            _ => ExecuteTransferAsync(),
            _ => CanTransfer());

        ToggleCsfEditorCommand = new RelayCommand(_ => ShowCsfEditor = !ShowCsfEditor);

        BatchTransferCommand = new AsyncRelayCommand(
            _ => ExecuteBatchTransferAsync(),
            _ => !string.IsNullOrEmpty(SourcePane.ModPath) && !string.IsNullOrEmpty(TargetPane.ModPath) && !IsTransferring);

        RollbackLastCommand = new AsyncRelayCommand(
            _ => RollbackLastTransferAsync(),
            _ => !string.IsNullOrEmpty(TargetPane.ModPath) && !IsTransferring);

        LoadHistoryCommand = new AsyncRelayCommand(
            _ => LoadTransferHistoryAsync(),
            _ => !string.IsNullOrEmpty(TargetPane.ModPath));

        PreviewDiffCommand = new AsyncRelayCommand(
            _ => PreviewDiffAsync(),
            _ => !string.IsNullOrEmpty(SourcePane.ModPath) && !string.IsNullOrEmpty(TargetPane.ModPath));

        ManageTemplatesCommand = new RelayCommand(_ => ManageTemplates());

        CrossReferenceMapCommand = new AsyncRelayCommand(
            _ => ShowCrossReferenceMapAsync(),
            _ => !string.IsNullOrEmpty(SourcePane.ModPath));

        BalanceReportCommand = new AsyncRelayCommand(
            _ => ShowBalanceReportAsync(),
            _ => SourcePane.SelectedUnit != null && !string.IsNullOrEmpty(SourcePane.ModPath));

        FactionConversionCommand = new AsyncRelayCommand(
            _ => ShowFactionConversionAsync(),
            _ => SourcePane.SelectedUnit != null);

        ManageProfilesCommand = new RelayCommand(_ => ManageProfiles());
        ExportTransferLogCommand = new AsyncRelayCommand(_ => ExportTransferLogAsync(), _ => !string.IsNullOrEmpty(TargetPane.ModPath));
        ImportTransferLogCommand = new AsyncRelayCommand(_ => ImportTransferLogAsync());

        ValidateIniCommand = new AsyncRelayCommand(
            _ => ValidateIniAsync(),
            _ => !string.IsNullOrEmpty(SourcePane.ModPath));

        W3dPreviewCommand = new AsyncRelayCommand(
            _ => ShowW3dPreviewAsync(),
            _ => SourcePane.SelectedUnit != null && !string.IsNullOrWhiteSpace(SourcePane.SelectedUnit.ModelW3D));
    }

    private void ManageProfiles()
    {
        try
        {
            var window = new Views.ProfileManagerWindow
            {
                Owner = System.Windows.Application.Current.MainWindow,
                CurrentSourcePath = SourcePane.ModPath ?? "",
                CurrentTargetPath = TargetPane.ModPath ?? "",
                CurrentTargetFaction = TargetPane.SelectedFaction
            };
            window.ShowDialog();
            if (window.DialogResult == true && window.LoadedProfile is { } p)
            {
                SourcePane.ModPath = p.SourceModPath;
                TargetPane.ModPath = p.TargetModPath;
                TargetPane.SelectedFaction = p.TargetFaction ?? "";
                StatusMessage = "✓ تم تطبيق الملف. اضغط 'تحميل المود' و'تحميل الهدف' إن لزم.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ: {ex.Message}";
        }
    }

    private bool CanTransfer()
    {
        return SourcePane.SelectedUnit != null
            && !string.IsNullOrEmpty(SourcePane.ModPath)
            && !string.IsNullOrEmpty(TargetPane.ModPath)
            && !IsTransferring;
    }

    // === Event Handlers ===

    private async void OnSourceModLoaded(object? sender, EventArgs e)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "جاري تحميل المود المصدر...";

            // إعادة إنشاء الخدمات بالمسار الجديد
            _bigFileReader = ServiceFactory.CreateBigFileReader(SourcePane.ModPath);
            _dependencyResolver = ServiceFactory.CreateDependencyResolver(_bigFileReader);
            _transferService = ServiceFactory.CreateTransferService(_bigFileReader);

            await _bigFileReader.ReadAsync("");

            // بناء فهرس SAGE
            StatusMessage = "جاري فهرسة تعريفات SAGE...";
            await _sageIndex.BuildIndexAsync(SourcePane.ModPath);

            // بناء فهرس الأيقونات
            StatusMessage = "جاري فهرسة الأيقونات...";
            await _mappedImageIndex.BuildIndexAsync(SourcePane.ModPath);
            _iconService = ServiceFactory.CreateIconService(_mappedImageIndex);
            _iconService.SetModPath(SourcePane.ModPath);
            ButtonImageToIconConverter.IconService = _iconService;

            // تحميل أيقونات الوحدات
            var buttonImages = SourcePane.AllUnits
                .Select(u => u.ButtonImage)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            await _iconService.PreloadIconsAsync(buttonImages);

            // إنشاء Weapon Analysis
            var parser = ServiceFactory.CreateIniParser();
            _weaponAnalysis = ServiceFactory.CreateWeaponAnalysis(parser, _bigFileReader);

            // إنشاء Pipeline
            _pipeline = new TransferPipelineService(
                _dependencyResolver, _transferService,
                _conflictDetection, _renamingService,
                _virtualFs, _csfService,
                _commandSetPatch, _sageIndex);

            StatusMessage = $"تم تحميل {SourcePane.AllUnits.Count} وحدة | {_sageIndex.Count} تعريف SAGE";
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ: {ex.Message}";
            App.DiagLog($"[PortingStudio] Source load error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void OnTargetModLoaded(object? sender, EventArgs e)
    {
        StatusMessage = $"المود الهدف: {TargetPane.TargetFactionOptions.Count} فصيل — جاري التحليل...";
        CommandManager.InvalidateRequerySuggested();

        try
        {
            _targetProfile = await Task.Run(() => _gameTargetAnalyzer.AnalyzeAsync(TargetPane.ModPath));
            StatusMessage = $"المود الهدف: {_targetProfile.Summary}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OnTargetModLoaded] Analysis error: {ex.Message}");
            StatusMessage = $"المود الهدف: {TargetPane.TargetFactionOptions.Count} فصيل";
        }
    }

    private async void OnUnitSelected(object? sender, SageUnit unit)
    {
        if (unit == null) return;

        // === Update Cameo Preview ===
        SelectedUnitName = unit.TechnicalName;
        SelectedUnitPreviewImage = _iconService?.GetIcon(unit.ButtonImage);
        var unitStats = SourcePane.GetUnitData(unit.TechnicalName);
        var infoParts = new List<string>();
        infoParts.Add($"الفصيل: {unit.Side}");
        if (unit.BuildCost > 0) infoParts.Add($"التكلفة: ${unit.BuildCost}");
        if (unitStats != null)
        {
            if (unitStats.TryGetValue("MaxHealth", out var hp)) infoParts.Add($"الصحة: {hp}");
            if (unitStats.TryGetValue("Speed", out var speed)) infoParts.Add($"السرعة: {speed}");
            if (unitStats.TryGetValue("VisionRange", out var vision)) infoParts.Add($"الرؤية: {vision}");
        }
        SelectedUnitInfo = string.Join(" | ", infoParts);
        SelectedUnitModel = !string.IsNullOrWhiteSpace(unit.ModelW3D) ? $"نموذج: {unit.ModelW3D}" : "";
        OnPropertyChanged(nameof(HasSelectedUnit));
        CommandManager.InvalidateRequerySuggested();

        try
        {
            DropZoneState = "Analyzing";
            StatusMessage = $"جاري تحليل {unit.TechnicalName}...";

            _dependencyResolver.SageIndex = _sageIndex;
            var unitData = SourcePane.GetUnitData(unit.TechnicalName);
            var unitIniPath = SourcePane.GetUnitIniPath(unit.TechnicalName);

            var graph = await _dependencyResolver.ResolveDependenciesAsync(
                unit.TechnicalName, SourcePane.ModPath, unitIniPath, unitData);
            await _dependencyResolver.ValidateDependenciesAsync(graph, SourcePane.ModPath);

            // تحليل الأسلحة
            EnhancedDependencyGraph enhanced;
            try
            {
                if (_weaponAnalysis == null)
                    throw new InvalidOperationException("Weapon analysis service not initialized");

                var weaponAnalysis = await _weaponAnalysis.AnalyzeWeaponDependenciesAsync(
                    unit.TechnicalName, SourcePane.ModPath);

                enhanced = new EnhancedDependencyGraph
                {
                    RootNode = graph.RootNode,
                    AllNodes = graph.AllNodes,
                    Status = graph.Status,
                    FoundCount = graph.FoundCount,
                    MissingCount = graph.MissingCount,
                    UnitId = graph.UnitId,
                    UnitName = graph.UnitName,
                    MaxDepth = graph.MaxDepth,
                    TotalSizeInBytes = graph.TotalSizeInBytes,
                    CreatedAt = graph.CreatedAt,
                    Notes = graph.Notes,
                    WeaponAnalysis = weaponAnalysis,
                    WeaponChains = weaponAnalysis.Weapons
                };
            }
            catch
            {
                enhanced = new EnhancedDependencyGraph
                {
                    RootNode = graph.RootNode,
                    AllNodes = graph.AllNodes,
                    Status = graph.Status,
                    FoundCount = graph.FoundCount,
                    MissingCount = graph.MissingCount,
                    UnitId = graph.UnitId,
                    UnitName = graph.UnitName,
                };
            }

            DependencyGraph.UpdateFromGraph(enhanced);
            DropZoneState = "Ready";
            StatusMessage = $"{unit.TechnicalName}: {enhanced.AllNodes.Count} تبعية | {enhanced.GetCompletionPercentage():F0}%";

            if (TargetPane.TargetFactionOptions.Count > 0)
                TargetPane.SelectedFaction = TargetPane.SuggestBestFaction(unit.Side);
        }
        catch (Exception ex)
        {
            DropZoneState = "Idle";
            StatusMessage = $"خطأ في التحليل: {ex.Message}";
        }
    }

    private async void OnUnitDropped(object? sender, SageUnit unit)
    {
        if (unit == null || string.IsNullOrEmpty(TargetPane.ModPath)) return;

        DropZoneState = "Analyzing";
        StatusMessage = $"جاري تحليل التعارضات لـ {unit.TechnicalName}...";

        try
        {
            var unitData = SourcePane.GetUnitData(unit.TechnicalName);
            var unitIniPath = SourcePane.GetUnitIniPath(unit.TechnicalName);

            // تحليل التبعيات
            var graph = await _pipeline.AnalyzeDependenciesAsync(
                unit, SourcePane.ModPath, unitIniPath, unitData);

            DependencyGraph.UpdateFromGraph(graph);

            // كشف التعارضات
            var conflicts = await _pipeline.DetectConflictsAsync(graph, TargetPane.ModPath);

            var proceed = await ConfirmTransferAsync(unit, graph, conflicts);
            if (!proceed)
            {
                DropZoneState = "Idle";
                return;
            }

            if (conflicts.HasConflicts)
            {
                ConflictResolution.LoadConflicts(conflicts);
                ShowConflictDialog = true;
                DropZoneState = "Ready";
                StatusMessage = $"تم كشف {conflicts.Conflicts.Count} تعارض - يرجى حلها";
            }
            else
            {
                // لا توجد تعارضات - نقل مباشر
                await ExecuteTransferForUnitAsync(unit, graph, unitData, null);
            }
        }
        catch (Exception ex)
        {
            DropZoneState = "Idle";
            StatusMessage = $"خطأ: {ex.Message}";
        }
    }

    private async Task ExecuteTransferAsync()
    {
        if (SourcePane.SelectedUnit == null) return;

        var unit = SourcePane.SelectedUnit;
        var unitData = SourcePane.GetUnitData(unit.TechnicalName);
        var unitIniPath = SourcePane.GetUnitIniPath(unit.TechnicalName);

        try
        {
            var graph = await _pipeline.AnalyzeDependenciesAsync(
                unit, SourcePane.ModPath, unitIniPath, unitData);

            var conflicts = await _pipeline.DetectConflictsAsync(graph, TargetPane.ModPath);

            var proceed = await ConfirmTransferAsync(unit, graph, conflicts);
            if (!proceed)
                return;

            if (conflicts.HasConflicts)
            {
                ConflictResolution.LoadConflicts(conflicts);
                ShowConflictDialog = true;
            }
            else
            {
                await ExecuteTransferForUnitAsync(unit, graph, unitData, null);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ: {ex.Message}";
        }
    }

    private async Task ExecuteTransferForUnitAsync(
        SageUnit unit,
        UnitDependencyGraph graph,
        Dictionary<string, string>? unitData,
        Dictionary<string, string>? renameMap)
    {
        IsTransferring = true;
        DropZoneState = "Transferring";
        ProgressValue = 0;

        try
        {
            var targetFaction = !string.IsNullOrWhiteSpace(TargetPane.SelectedFaction)
                ? TargetPane.SelectedFaction : unit.Side;

            // === المحرك التكيفي الجديد ===
            var adaptiveProgress = new Progress<AdaptiveTransferProgress>(p =>
            {
                ProgressValue = p.Percentage;
                StatusMessage = $"نقل: {p.Stage}" + (!string.IsNullOrEmpty(p.CurrentFile) ? $" — {p.CurrentFile}" : "");
            });

            var request = new AdaptiveTransferRequest
            {
                UnitName = unit.TechnicalName,
                SourceModPath = SourcePane.ModPath,
                TargetModPath = TargetPane.ModPath,
                TargetFaction = targetFaction,
                SourceFaction = unit.Side,
                DependencyGraph = graph,
                UnitData = unitData,
                RenameMap = renameMap,
            };

            var adaptiveResult = await Task.Run(() =>
                _adaptiveEngine.TransferAsync(request, _targetProfile, adaptiveProgress));

            if (adaptiveResult.Success)
            {
                TargetPane.AddTransferLog(unit.TechnicalName);

                // توليد CSF عبر Pipeline القديم (لا يزال مفيداً)
                try
                {
                    var csfEntries = _csfService.GenerateEntriesForUnit(unit.TechnicalName, unit.TechnicalName);
                    CsfEditor.AddEntries(csfEntries);
                    var csfPath = Path.Combine(TargetPane.ModPath, "Data", "generals.csf");
                    if (File.Exists(csfPath))
                        await _csfService.MergeEntriesAsync(csfPath, csfEntries);
                }
                catch (Exception csfEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Transfer] CSF error (non-fatal): {csfEx.Message}");
                }

                // حقن CommandSet
                try
                {
                    if (unitData != null)
                        await _commandSetPatch.EnsureCommandSetAsync(unit, unitData, TargetPane.ModPath, targetFaction);
                }
                catch (Exception cmdEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Transfer] CommandSet error (non-fatal): {cmdEx.Message}");
                }

                // === تطهير INI المنقول (إزالة RequiredUpgrade/Prerequisite/ScienceRequired/Rank) ===
                try
                {
                    var iniDataDir = Path.Combine(TargetPane.ModPath, "Data", "INI");
                    if (Directory.Exists(iniDataDir))
                    {
                        var lastModified = Directory.GetFiles(iniDataDir, "*.ini")
                            .Where(f => File.GetLastWriteTime(f) >= DateTime.Now.AddMinutes(-2))
                            .ToList();

                        var totalSanitized = 0;
                        foreach (var iniFile in lastModified)
                        {
                            var content = await File.ReadAllTextAsync(iniFile);
                            var sanitizeResult = _transferSanitizer.Sanitize(content);
                            if (sanitizeResult.Success && sanitizeResult.LinesRemoved > 0)
                            {
                                await File.WriteAllTextAsync(iniFile, sanitizeResult.SanitizedContent);
                                totalSanitized += sanitizeResult.LinesRemoved;
                                System.Diagnostics.Debug.WriteLine(
                                    $"[TransferSanitizer] ✓ {Path.GetFileName(iniFile)}: {sanitizeResult.LinesRemoved} سطر محذوف");
                            }
                        }

                        if (totalSanitized > 0)
                            StatusMessage += $" | 🧹 تطهير: {totalSanitized} قيد محذوف";
                    }
                }
                catch (Exception sanEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Transfer] Sanitizer error (non-fatal): {sanEx.Message}");
                }

                DropZoneState = "Idle";
                ProgressValue = 100;
                StatusMessage = $"✓ {adaptiveResult.Summary}";

                // عرض تحذيرات التحقق إن وجدت
                if (adaptiveResult.Validation?.Warnings.Count > 0)
                {
                    StatusMessage += $" | ⚠ {adaptiveResult.Validation.Warnings.Count} تحذير";
                }
            }
            else
            {
                DropZoneState = "Idle";
                StatusMessage = $"✗ {adaptiveResult.Message}";
            }
        }
        catch (Exception ex)
        {
            DropZoneState = "Idle";
            StatusMessage = $"✗ خطأ في النقل: {ex.Message}";
        }
        finally
        {
            IsTransferring = false;
        }
    }

    private async Task<bool> ConfirmTransferAsync(SageUnit unit, UnitDependencyGraph graph, ConflictReport conflicts)
    {
        var targetFaction = !string.IsNullOrWhiteSpace(TargetPane.SelectedFaction)
            ? TargetPane.SelectedFaction : unit?.Side ?? "";

        if (string.IsNullOrWhiteSpace(targetFaction))
        {
            StatusMessage = "⚠ لم يتم اختيار فصيل هدف — حمّل المود الهدف أولاً";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TargetPane.ModPath))
        {
            StatusMessage = "⚠ لم يتم تحديد مسار المود الهدف";
            return false;
        }

        System.Diagnostics.Debug.WriteLine($"\n[ConfirmTransferAsync] === TRANSFER CONFIRMATION STARTED ===");
        System.Diagnostics.Debug.WriteLine($"[ConfirmTransferAsync] Target Mod Path: {TargetPane.ModPath}");
        System.Diagnostics.Debug.WriteLine($"[ConfirmTransferAsync] Target Faction: {targetFaction}");
        System.Diagnostics.Debug.WriteLine($"[ConfirmTransferAsync] Unit Name: {unit?.TechnicalName}");

        // === SAGE Relational Data Engine: تحليل علائقي بدلاً من تجميع أعمى ===
        StatusMessage = "جاري تحليل أزرار القيادة...";

        // ═══ إعادة استخدام الفهرس العلائقي من TargetPane (أو بناء جديد) ═══
        var chainService = TargetPane.TargetCommandChain;

        if (!chainService.IsBuilt)
        {
            StatusMessage = "⏳ بناء فهرس المحرك العلائقي للهدف...";
            var targetMappedIndex = new MappedImageIndex();
            await targetMappedIndex.BuildIndexAsync(TargetPane.ModPath);

            System.Diagnostics.Debug.WriteLine(
                $"[ConfirmTransferAsync] Target MappedImageIndex: {targetMappedIndex.Count} images");

            await chainService.BuildIndexAsync(TargetPane.ModPath, targetMappedIndex);
        }

        // إنشاء GameImageLoader إذا لم يُنشأ بعد
        if (_gameImageLoader == null && chainService.IsBuilt)
        {
            var targetMappedIndex2 = new MappedImageIndex();
            await targetMappedIndex2.BuildIndexAsync(TargetPane.ModPath);
            var targetIconService = new IconService(targetMappedIndex2);
            targetIconService.SetModPath(TargetPane.ModPath);
            _gameImageLoader = new Services.GameImageLoader(targetIconService);

            System.Diagnostics.Debug.WriteLine(
                $"[ConfirmTransferAsync] ✓ Target IconService + GameImageLoader ready");
        }

        System.Diagnostics.Debug.WriteLine(
            $"[ConfirmTransferAsync] CommandChainService.IsBuilt={chainService.IsBuilt}, " +
            $"Objects={chainService.ObjectCount}, Sets={chainService.CommandSetCount}, " +
            $"Buttons={chainService.CommandButtonCount}");

        // --- المسار القديم (fallback) ---
        var (hasSpace, availableSlot, slotMessage) =
            await _commandSetAnalyzer.CheckAvailableSlotAsync(TargetPane.ModPath, targetFaction);

        var buttonAnalysis = await _commandButtonAnalyzer.AnalyzeCommandSet(
            TargetPane.ModPath,
            targetFaction,
            availableSlot?.CommandSetName);

        // ═══ المسار العلائقي المستقل — Smart Target Routing ═══
        CommandBarResult? relationalBar = null;
        if (chainService.IsBuilt)
        {
            var unitName = unit?.TechnicalName ?? "";
            var unitType = DetectUnitType(unitName);
            System.Diagnostics.Debug.WriteLine(
                $"[SmartRouting] Unit='{unitName}', DetectedType={unitType}");

            var factionBuildings = chainService.GetFactionProductionBuildings(targetFaction);

            System.Diagnostics.Debug.WriteLine(
                $"[SmartRouting] Faction buildings found: {factionBuildings.Count}");
            foreach (var (bld, cs) in factionBuildings)
            {
                var score = ScoreBuildingForUnitType(bld, unitType);
                System.Diagnostics.Debug.WriteLine($"  → {bld} → {cs} (score={score})");
            }

            // --- اختيار أنسب مبنى ---
            (string ObjectName, string CommandSetName)? bestBuilding = null;

            if (factionBuildings.Count > 0)
            {
                var topCandidate = factionBuildings
                    .OrderByDescending(b => ScoreBuildingForUnitType(b.ObjectName, unitType))
                    .First();

                var topScore = ScoreBuildingForUnitType(topCandidate.ObjectName, unitType);

                if (topScore > 10)
                {
                    // ✓ مبنى مناسب لنوع الوحدة
                    bestBuilding = topCandidate;
                    System.Diagnostics.Debug.WriteLine(
                        $"[SmartRouting] ✓ Best match: '{topCandidate.ObjectName}' (score={topScore})");
                }
                else
                {
                    // ⚠ لم يُعثر على مبنى مناسب — fallback أوسع
                    System.Diagnostics.Debug.WriteLine(
                        $"[SmartRouting] ⚠ No good match (best score={topScore}). Trying ALL buildings...");

                    var allBuildings = chainService.GetAllProductionBuildings();
                    System.Diagnostics.Debug.WriteLine(
                        $"[SmartRouting] All production buildings: {allBuildings.Count}");

                    if (allBuildings.Count > 0)
                    {
                        var globalBest = allBuildings
                            .OrderByDescending(b => ScoreBuildingForUnitType(b.ObjectName, unitType))
                            .First();
                        var globalScore = ScoreBuildingForUnitType(globalBest.ObjectName, unitType);

                        if (globalScore > topScore)
                        {
                            bestBuilding = globalBest;
                            System.Diagnostics.Debug.WriteLine(
                                $"[SmartRouting] ✓ Global fallback: '{globalBest.ObjectName}' (score={globalScore})");
                        }
                        else
                        {
                            bestBuilding = topCandidate; // أفضل ما لدينا
                        }
                    }
                    else
                    {
                        bestBuilding = topCandidate;
                    }
                }
            }
            else
            {
                // لم يُعثر على أي مبنى للفصيل — محاولة ALL
                System.Diagnostics.Debug.WriteLine(
                    $"[SmartRouting] ⚠ No faction buildings! Trying ALL buildings...");

                var allBuildings = chainService.GetAllProductionBuildings();
                if (allBuildings.Count > 0)
                {
                    bestBuilding = allBuildings
                        .OrderByDescending(b => ScoreBuildingForUnitType(b.ObjectName, unitType))
                        .First();
                    System.Diagnostics.Debug.WriteLine(
                        $"[SmartRouting] ✓ Global fallback: '{bestBuilding.Value.ObjectName}'");
                }
                else
                {
                    // Fallback: محاولة FindObjectByCommandSet بمخرج المحلل القديم
                    var targetObject = chainService.FindObjectByCommandSet(
                        availableSlot?.CommandSetName ?? buttonAnalysis.CommandSetName);
                    if (targetObject != null)
                    {
                        relationalBar = chainService.GetBuildingCommandBar(targetObject);
                        System.Diagnostics.Debug.WriteLine(
                            $"[SmartRouting] ✓ Legacy fallback: '{targetObject}' → " +
                            $"{relationalBar.OccupiedSlots}/{relationalBar.TotalSlots}");
                    }
                }
            }

            if (bestBuilding.HasValue && relationalBar == null)
            {
                relationalBar = chainService.GetBuildingCommandBar(bestBuilding.Value.ObjectName);
                System.Diagnostics.Debug.WriteLine(
                    $"[SmartRouting] ═══ FINAL: '{bestBuilding.Value.ObjectName}' → " +
                    $"{relationalBar.OccupiedSlots}/{relationalBar.TotalSlots} occupied, " +
                    $"CS={relationalBar.CommandSetName} ═══");
            }
        }

        System.Diagnostics.Debug.WriteLine($"[ConfirmTransferAsync] hasSpace: {hasSpace}, slot: {availableSlot?.SlotNumber}");
        System.Diagnostics.Debug.WriteLine($"[ConfirmTransferAsync] ButtonAnalysis: {buttonAnalysis.EmptySlots} empty / {buttonAnalysis.TotalSlots} total");
        System.Diagnostics.Debug.WriteLine($"[ConfirmTransferAsync] relationalBar: {(relationalBar != null ? $"{relationalBar.ObjectName} ({relationalBar.OccupiedSlots}/{relationalBar.TotalSlots})" : "NULL")}");

        // === إنشاء selectorVM ===
        List<Domain.Models.CommandButtonSlot> slotsForDisplay;

        if (relationalBar != null && relationalBar.Slots.Count > 0)
        {
            // ── المسار العلائقي: أيقونات حقيقية من TGA/DDS ──
            if (_gameImageLoader != null)
            {
                StatusMessage = "⏳ تحميل أيقونات الخانات...";
                slotsForDisplay = await _gameImageLoader.LoadCommandBarWithIconsAsync(relationalBar);
                System.Diagnostics.Debug.WriteLine(
                    $"[UI] ✓ Relational icons loaded: {slotsForDisplay.Count(s => s.HasIcon)} icons / {slotsForDisplay.Count} total");
            }
            else
            {
                // GameImageLoader غير جاهز — fallback بدون أيقونات
                slotsForDisplay = relationalBar.Slots.Select(s => new Domain.Models.CommandButtonSlot
                {
                    SlotNumber = s.SlotNumber,
                    IsEmpty = !s.IsOccupied,
                    OccupiedBy = s.ButtonName,
                    ButtonImageName = s.ButtonImage,
                    Command = s.Command,
                    Icon = s.ButtonImage,
                    Description = s.Label ?? s.ButtonName,
                    Type = Domain.Models.ButtonType.Unit
                }).ToList();
            }
        }
        else
        {
            // ── الفولباك القديم (Blind Aggregation) ──
            slotsForDisplay = buttonAnalysis.Buttons;
        }

        var selectorVM = new CommandButtonSelectorViewModel
        {
            UnitName = unit?.TechnicalName ?? "Unknown",
            FactionName = targetFaction,
            CommandSetName = relationalBar?.CommandSetName ?? buttonAnalysis.CommandSetName,
            Buttons = new ObservableCollection<Domain.Models.CommandButtonSlot>(slotsForDisplay),
            HasEmptySlot = slotsForDisplay.Any(s => s.IsEmpty)
        };

        var selectorWindow = new Views.CommandButtonSelectorWindow
        {
            DataContext = selectorVM,
            Owner = System.Windows.Application.Current.MainWindow
        };

        selectorWindow.ShowDialog();

        if (!selectorWindow.UserConfirmed)
        {
            StatusMessage = "تم إلغاء النقل (لم يتم اختيار زر)";
            return false;
        }

        // تحديث الـ Slot المختار (لو المستخدم اختار slot معين)
        if (selectorVM.SelectedButtonToReplace != null)
        {
            availableSlot = new Domain.Models.CommandSetSlotInfo
            {
                SlotNumber = selectorVM.SelectedButtonToReplace.SlotNumber,
                CommandSetName = buttonAnalysis.CommandSetName
            };
        }

        StatusMessage = "🧠 جاري التشخيص الذكي...";

        // === إنشاء ViewModel المعاينة الذكية ===
        var previewVM = new IntelligentPreviewViewModel
        {
            UnitName = unit?.TechnicalName ?? "Unknown",
            SourceFaction = unit?.Side ?? "Unknown",
            DependencyCount = graph.AllNodes.Count,
            TargetModName = Path.GetFileName(TargetPane.ModPath),
            TargetFaction = targetFaction,
            SlotNumber = availableSlot?.SlotNumber ?? 0,
            CommandSetName = availableSlot?.CommandSetName ?? "غير محدد"
        };

        // === تشغيل التشخيص الكامل ===
        if (unit != null)
        {
            previewVM.RunDiagnosis(
                unit, graph, conflicts,
                TargetPane.ModPath, targetFaction,
                hasSpace, availableSlot);
        }

        StatusMessage = $"🧠 التشخيص مكتمل - الصحة: {previewVM.HealthScore}%";

        // === عرض النافذة الذكية ===
        var previewWindow = new Views.IntelligentTransferPreviewWindow
        {
            DataContext = previewVM,
            Owner = System.Windows.Application.Current.MainWindow
        };

        previewWindow.ShowDialog();

        if (!previewWindow.UserConfirmed)
        {
            StatusMessage = "تم إلغاء النقل من قبل المستخدم";
            return false;
        }

        // تخزين نتائج المعاينة للاستخدام في النقل
        LastPreviewResult = previewVM;

        return true;
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

    /// <summary>
    /// تأكيد حل التعارضات والمتابعة بالنقل
    /// </summary>
    public async Task ConfirmConflictResolutionAsync()
    {
        ShowConflictDialog = false;
        var renameMap = ConflictResolution.GetRenameMap();
        var unit = SourcePane.SelectedUnit;
        if (unit == null) return;

        var unitData = SourcePane.GetUnitData(unit.TechnicalName);
        var unitIniPath = SourcePane.GetUnitIniPath(unit.TechnicalName);

        var graph = await _pipeline.AnalyzeDependenciesAsync(
            unit, SourcePane.ModPath, unitIniPath, unitData);

        await ExecuteTransferForUnitAsync(unit, graph, unitData, renameMap);
    }

    // =======================================
    // === القوى الخارقة: النقل الدفعي ===
    // =======================================

    private async Task ExecuteBatchTransferAsync()
    {
        if (SourcePane.Units == null || SourcePane.Units.Count == 0)
        {
            StatusMessage = "لا توجد وحدات متاحة للنقل الدفعي";
            return;
        }

        var batchVM = new BatchTransferViewModel
        {
            SourceModName = Path.GetFileName(SourcePane.ModPath),
            TargetModName = Path.GetFileName(TargetPane.ModPath)
        };
        batchVM.LoadUnits(SourcePane.Units.Select(u => u.TechnicalName));

        var batchWindow = new Views.BatchTransferWindow
        {
            DataContext = batchVM,
            Owner = System.Windows.Application.Current.MainWindow
        };

        batchWindow.Show();

        if (!batchWindow.TransferStarted) return;

        batchVM.IsRunning = true;
        StatusMessage = "📦 جاري النقل الدفعي...";

        var batchService = ServiceFactory.CreateBatchTransfer(_pipeline);
        var request = new BatchTransferRequest
        {
            Units = SourcePane.Units.ToList(),
            SourceModPath = SourcePane.ModPath,
            TargetModPath = TargetPane.ModPath,
            TargetFaction = TargetPane.SelectedFaction ?? "",
            SkipCriticalConflicts = true,
            AutoRename = true,
            UnitDataProvider = name => SourcePane.GetUnitData(name),
            UnitIniPathProvider = name => SourcePane.GetUnitIniPath(name)
        };

        var progress = new Progress<BatchTransferProgress>(p =>
        {
            batchVM.ProgressMessage = $"{p.Phase}: {p.CurrentUnitName}";
            batchVM.OverallProgress = p.OverallPercentage;
        });

        var report = await batchService.ExecuteBatchAsync(request, progress);
        batchVM.ApplyReport(report);
        batchWindow.OnTransferComplete();

        StatusMessage = report.Summary;
    }

    // =======================================
    // === القوى الخارقة: التراجع ===
    // =======================================

    private async Task RollbackLastTransferAsync()
    {
        try
        {
            var journal = ServiceFactory.CreateTransferJournal(TargetPane.ModPath);
            var entries = await journal.LoadAllEntriesAsync();
            var lastEntry = entries.FirstOrDefault(e => !e.IsRolledBack);

            if (lastEntry == null)
            {
                StatusMessage = "⚠ لا توجد عمليات نقل قابلة للتراجع";
                return;
            }

            var preview = _rollbackService.PreviewRollback(lastEntry);

            var confirm = System.Windows.MessageBox.Show(
                $"هل تريد التراجع عن نقل {lastEntry.UnitName}?\n\n" +
                $"📅 تاريخ النقل: {lastEntry.Timestamp:yyyy-MM-dd HH:mm}\n" +
                $"{preview.Summary}",
                "⏪ تأكيد التراجع",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (confirm != System.Windows.MessageBoxResult.Yes) return;

            StatusMessage = $"⏪ جاري التراجع عن {lastEntry.UnitName}...";

            var rollbackProgress = new Progress<(int current, int total, string message)>(p =>
            {
                ProgressValue = (p.current * 100) / Math.Max(p.total, 1);
                StatusMessage = p.message;
            });

            var result = await _rollbackService.RollbackAsync(lastEntry, rollbackProgress);
            StatusMessage = result.Message;
            ProgressValue = 100;

            // تحديث السجل
            await LoadTransferHistoryAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ خطأ في التراجع: {ex.Message}";
        }
    }

    // =======================================
    // === القوى الخارقة: سجل النقل ===
    // =======================================

    private async Task LoadTransferHistoryAsync()
    {
        try
        {
            var journal = ServiceFactory.CreateTransferJournal(TargetPane.ModPath);
            var entries = await journal.LoadAllEntriesAsync();
            TransferHistory = new ObservableCollection<TransferJournalEntry>(entries);
            StatusMessage = $"📜 تم تحميل {entries.Count} سجل نقل";
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ في تحميل السجل: {ex.Message}";
        }
    }

    private async Task ExportTransferLogAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPane.ModPath)) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "تصدير سجل النقل",
            Filter = "ملف JSON|*.json|جميع الملفات|*.*",
            DefaultExt = "json",
            FileName = $"TransferLog_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var journal = ServiceFactory.CreateTransferJournal(TargetPane.ModPath);
            await journal.ExportToFileAsync(dlg.FileName);
            StatusMessage = "✓ تم تصدير سجل النقل";
            System.Windows.MessageBox.Show($"تم تصدير السجل إلى:\n{dlg.FileName}", "تصدير", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ: {ex.Message}";
            System.Windows.MessageBox.Show($"فشل التصدير: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task ImportTransferLogAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "استيراد سجل النقل",
            Filter = "ملف JSON|*.json|جميع الملفات|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var entries = await TransferJournal.ImportFromFileAsync(dlg.FileName);
            StatusMessage = $"✓ تم استيراد {entries.Count} مدخل";
            System.Windows.MessageBox.Show($"تم استيراد {entries.Count} مدخل من السجل.\n(للعرض والنسخ الاحتياطي — التطبيق يعرض السجل الحالي للمود الهدف فقط.)", "استيراد", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ: {ex.Message}";
            System.Windows.MessageBox.Show($"فشل الاستيراد: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    // =======================================
    // === أدوات متقدمة: معاينة الفروقات ===
    // =======================================

    private async Task PreviewDiffAsync()
    {
        if (string.IsNullOrEmpty(SourcePane.ModPath) || string.IsNullOrEmpty(TargetPane.ModPath))
        {
            StatusMessage = "الرجاء تحديد مسار المود المصدر والهدف أولاً";
            return;
        }

        StatusMessage = "جاري تحليل الفروقات...";

        try
        {
            var diffWindow = new Views.DiffViewerWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            diffWindow.Show();
            await diffWindow.LoadDiffsAsync(SourcePane.ModPath, TargetPane.ModPath);
            StatusMessage = "✓ تم فتح نافذة الفروقات";
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ في تحليل الفروقات: {ex.Message}";
        }
    }

    // =======================================
    // === أدوات متقدمة: إدارة القوالب ===
    // =======================================

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

                if (!string.IsNullOrWhiteSpace(template.TargetFaction))
                {
                    TargetPane.SelectedFaction = template.TargetFaction;
                }

                StatusMessage = $"✓ تم تطبيق القالب: {template.Name}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ في إدارة القوالب: {ex.Message}";
        }
    }

    /// <summary>
    /// دمج الفصائل من المود المصدر والهدف للقوالب
    /// </summary>
    private List<string> CombineAvailableFactions()
    {
        var factions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add target factions
        if (TargetPane.TargetFactionOptions != null)
        {
            foreach (var opt in TargetPane.TargetFactionOptions)
            {
                if (!string.IsNullOrWhiteSpace(opt.Name))
                    factions.Add(opt.Name);
            }
        }

        // Add source factions
        if (SourcePane.Factions != null)
        {
            foreach (var f in SourcePane.Factions)
            {
                if (!string.IsNullOrWhiteSpace(f) && f != "الكل")
                    factions.Add(f);
            }
        }

        // No fake data — empty means "scan required"
        return factions.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // =======================================
    // === أدوات متقدمة: عارض W3D ===
    // =======================================

    private async Task ShowW3dPreviewAsync()
    {
        if (SourcePane.SelectedUnit == null || string.IsNullOrWhiteSpace(SourcePane.SelectedUnit.ModelW3D))
            return;

        StatusMessage = "جاري قراءة نموذج W3D...";

        try
        {
            var reader = new Infrastructure.Services.W3dInfoReader();
            var info = await reader.ReadFromModAsync(SourcePane.ModPath, SourcePane.SelectedUnit.ModelW3D);

            var window = new Views.W3dPreviewWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowInfo(info);
            window.Show();

            StatusMessage = info.IsValid
                ? $"✓ W3D: {info.Meshes.Count} أجزاء، {info.TotalVertices:N0} نقطة"
                : $"⚠ W3D: {info.ErrorMessage}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ في قراءة W3D: {ex.Message}";
        }
    }

    // =======================================
    // === أدوات متقدمة: فحص INI ===
    // =======================================

    private async Task ValidateIniAsync()
    {
        StatusMessage = "جاري فحص ملفات INI...";

        try
        {
            var validator = new Infrastructure.Validation.IniSyntaxValidator();
            var report = await Task.Run(() => validator.ValidateModAsync(SourcePane.ModPath));

            var window = new Views.IniValidationWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowReport(report);
            window.Show();

            StatusMessage = report.IsClean
                ? $"✓ فحص INI: {report.FilesScanned} ملف - لا توجد مشاكل"
                : $"⚠ فحص INI: {report.TotalErrors} خطأ، {report.TotalWarnings} تحذير في {report.FilesScanned} ملف";
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ في فحص INI: {ex.Message}";
        }
    }

    // =======================================
    // === أدوات متقدمة: خريطة المراجع ===
    // =======================================

    private async Task ShowCrossReferenceMapAsync()
    {
        StatusMessage = "جاري تحليل المراجع التقاطعية...";

        try
        {
            var crossRefWindow = new Views.CrossReferenceMapWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            crossRefWindow.Show();
            await crossRefWindow.AnalyzeModAsync(SourcePane.ModPath);
            StatusMessage = "✓ تم فتح خريطة المراجع التقاطعية";
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ: {ex.Message}";
        }
    }

    // =======================================
    // === أدوات متقدمة: تقرير التوازن ===
    // =======================================

    private async Task ShowBalanceReportAsync()
    {
        if (SourcePane.SelectedUnit == null) return;
        StatusMessage = "جاري تحليل التوازن...";

        try
        {
            var balanceWindow = new Views.BalanceReportWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            balanceWindow.Show();
            await balanceWindow.AnalyzeUnitAsync(SourcePane.ModPath, SourcePane.SelectedUnit.TechnicalName);
            StatusMessage = "✓ تم فتح تقرير التوازن";
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ: {ex.Message}";
        }
    }

    // =======================================
    // === أدوات متقدمة: تحويل الفصيل ===
    // =======================================

    private async Task ShowFactionConversionAsync()
    {
        if (SourcePane.SelectedUnit == null) return;

        var unitName = SourcePane.SelectedUnit.TechnicalName;
        StatusMessage = "جاري تحميل بيانات الوحدة...";

        try
        {
            var content = await SourcePane.GetUnitIniContentAsync(unitName).ConfigureAwait(true);

            var window = new Views.FactionConversionWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.LoadUnit(unitName, content ?? "");
            window.ShowDialog();

            if (window.ConversionApplied && window.ConvertedContent != null)
            {
                StatusMessage = $"✓ تم تحويل '{unitName}' بنجاح";
            }
            else
            {
                StatusMessage = "تحويل فصيل الوحدة";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════
    //  Smart Target Routing — تحديد المبنى حسب نوع الوحدة
    // ═══════════════════════════════════════════════════

    /// <summary>اكتشاف نوع الوحدة من الاسم التقني</summary>
    private static string DetectUnitType(string unitName)
    {
        if (string.IsNullOrWhiteSpace(unitName))
            return "UNKNOWN";

        var upper = unitName.ToUpperInvariant();

        if (upper.Contains("VEHICLE") || upper.Contains("TANK") ||
            upper.Contains("HUMVEE") || upper.Contains("TRUCK") ||
            upper.Contains("OVERLORD") || upper.Contains("BATTLEM") ||
            upper.Contains("CRUSADER") || upper.Contains("MARAUDER") ||
            upper.Contains("SCORPION") || upper.Contains("QUAD") ||
            upper.Contains("JARMEN") || upper.Contains("DRAGON"))
            return "VEHICLE";

        if (upper.Contains("INFANTRY") || upper.Contains("RANGER") ||
            upper.Contains("SOLDIER") || upper.Contains("TROOPER") ||
            upper.Contains("REBEL") || upper.Contains("HACKER") ||
            upper.Contains("WORKER") || upper.Contains("REDHGUARD"))
            return "INFANTRY";

        if (upper.Contains("AIRCRAFT") || upper.Contains("JET") ||
            upper.Contains("HELICOPTER") || upper.Contains("AURORA") ||
            upper.Contains("RAPTOR") || upper.Contains("COMANCHE") ||
            upper.Contains("MIG") || upper.Contains("HELIX"))
            return "AIRCRAFT";

        // Fallback: assume VEHICLE for unknown
        return "VEHICLE";
    }

    /// <summary>تسجيل نقاط المبنى حسب نوع الوحدة</summary>
    private static int ScoreBuildingForUnitType(string buildingName, string unitType)
    {
        var upper = buildingName.ToUpperInvariant();
        int score = 0;

        switch (unitType)
        {
            case "VEHICLE":
                if (upper.Contains("WARFACTORY") || upper.Contains("ARMS") ||
                    upper.Contains("ARMSDEALER") || upper.Contains("FACTORY"))
                    score += 100;
                else if (upper.Contains("BARRACKS"))
                    score += 10; // low priority for vehicles
                else if (upper.Contains("AIRFIELD"))
                    score += 5;
                break;

            case "INFANTRY":
                if (upper.Contains("BARRACKS"))
                    score += 100;
                else if (upper.Contains("WARFACTORY") || upper.Contains("ARMS") || upper.Contains("FACTORY"))
                    score += 10;
                else if (upper.Contains("AIRFIELD"))
                    score += 5;
                break;

            case "AIRCRAFT":
                if (upper.Contains("AIRFIELD") || upper.Contains("AIRFORCE"))
                    score += 100;
                else if (upper.Contains("WARFACTORY") || upper.Contains("ARMS") || upper.Contains("FACTORY"))
                    score += 10;
                else if (upper.Contains("BARRACKS"))
                    score += 5;
                break;

            default:
                // Generic scoring
                if (upper.Contains("WARFACTORY") || upper.Contains("ARMS") || upper.Contains("FACTORY"))
                    score += 50;
                else if (upper.Contains("BARRACKS"))
                    score += 40;
                else if (upper.Contains("AIRFIELD"))
                    score += 30;
                break;
        }

        // Bonus for production buildings with available slots
        score += 1; // tie-breaker by name

        return score;
    }
}
