using System.Collections.ObjectModel;
using System.Windows.Input;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.UseCases;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.Normalization;
using ZeroHourStudio.Infrastructure.DependencyResolution;
using ZeroHourStudio.Infrastructure.Transfer;
using ZeroHourStudio.UI.WPF.Commands;
using ZeroHourStudio.UI.WPF.Core;

namespace ZeroHourStudio.UI.WPF.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IAnalyzeUnitDependenciesUseCase _analyzeUseCase;
        private readonly ITransferUnitUseCase _transferUseCase;
        private readonly SmartNormalization _normalizer;
        private readonly IDependencyResolver _dependencyResolver;
        private readonly ISmartTransferService _smartTransferService;

        private string _searchText = string.Empty;
        private bool _isLoading;
        private string _statusMessage = "جاهز";
        private int _progressValue;
        private SageUnit? _selectedUnit;
        private UnitDependencyGraph? _currentGraph;
        private ObservableCollection<SageUnit> _units = new();
        private string _sourceModPath = string.Empty;
        private string _targetModPath = string.Empty;

        public MainViewModel(
            IAnalyzeUnitDependenciesUseCase analyzeUseCase,
            ITransferUnitUseCase transferUseCase,
            SmartNormalization normalizer,
            IDependencyResolver dependencyResolver,
            ISmartTransferService smartTransferService)
        {
            _analyzeUseCase = analyzeUseCase;
            _transferUseCase = transferUseCase;
            _normalizer = normalizer;
            _dependencyResolver = dependencyResolver;
            _smartTransferService = smartTransferService;

            SearchCommand = new RelayCommand(_ => PerformSearch());
            TransferCommand = new AsyncRelayCommand(_ => TransferUnitAsync(), _ => CanTransfer());
            LoadModCommand = new AsyncRelayCommand(_ => LoadModAsync(), _ => CanLoadMod());
            BrowseSourceCommand = new RelayCommand(_ => { /* سيتم التنفيذ في Code-Behind */ });
            BrowseTargetCommand = new RelayCommand(_ => { /* سيتم التنفيذ في Code-Behind */ });
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    // تطبيق التطبيع الذكي تلقائياً عند تغيير النص (إذا لزم الأمر)
                    // أو يمكن تركه لزر البحث
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
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
                if (SetProperty(ref _selectedUnit, value))
                {
                    if (value != null)
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

        public ICommand SearchCommand { get; }
        public ICommand TransferCommand { get; }
        public ICommand LoadModCommand { get; }
        public ICommand BrowseSourceCommand { get; }
        public ICommand BrowseTargetCommand { get; }

        public string SourceModPath
        {
            get => _sourceModPath;
            set
            {
                if (SetProperty(ref _sourceModPath, value))
                {
                    OnPropertyChanged(nameof(HasSourcePath));
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
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool HasSourcePath => !string.IsNullOrWhiteSpace(SourceModPath);
        public bool HasTargetPath => !string.IsNullOrWhiteSpace(TargetModPath);
        public bool HasBothPaths => HasSourcePath && HasTargetPath;

        private void PerformSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            // استخدام التطبيع الذكي
            var normalized = _normalizer.NormalizeFactionName(SearchText);
            SearchText = normalized.Value;
            
            StatusMessage = $"تم التطبيع إلى: {normalized.Value}";
        }

        private bool CanLoadMod() => HasSourcePath;

        public async Task LoadModAsync()
        {
            if (!HasSourcePath)
            {
                StatusMessage = "الرجاء اختيار مسار المود المصدر أولاً";
                return;
            }

            IsLoading = true;
            StatusMessage = "جاري مسح ملفات الـ BIG...";
            ProgressValue = 0;

            try
            {
                // محاكاة عملية الفهرسة الطويلة (35,326 عنصر)
                for (int i = 0; i <= 100; i += 5)
                {
                    ProgressValue = i;
                    StatusMessage = $"جاري مسح ملفات الـ BIG... ({i}%)";
                    await Task.Delay(50); // محاكاة العمل
                }

                // إضافة بعض الوحدات التجريبية
                Units.Clear();
                Units.Add(new SageUnit { TechnicalName = "ChinaTankOverlord", Side = "China", BuildCost = 2000, ModelW3D = "NVOVLRD.W3D" });
                Units.Add(new SageUnit { TechnicalName = "USAFighterRaptor", Side = "USA", BuildCost = 1200, ModelW3D = "AVRAPTOR.W3D" });
                Units.Add(new SageUnit { TechnicalName = "GLAInfantryRebel", Side = "GLA", BuildCost = 150, ModelW3D = "UIREBEL.W3D" });

                StatusMessage = $"تم تحميل المود بنجاح. ({Units.Count} وحدة)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"خطأ في تحميل المود: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task AnalyzeSelectedUnitAsync(SageUnit unit)
        {
            StatusMessage = $"جاري تحليل {unit.TechnicalName}...";
            
            var request = new AnalyzeDependenciesRequest
            {
                UnitId = unit.TechnicalName,
                UnitName = unit.TechnicalName,
                UnitData = new Dictionary<string, string> 
                { 
                    { "Model", unit.ModelW3D },
                    { "WeaponSet", "Primary" }
                }
            };

            var response = await _analyzeUseCase.ExecuteAsync(request);
            if (response.Success)
            {
                CurrentGraph = response.DependencyGraph;
                StatusMessage = $"اكتمال التحليل: {response.CompletionPercentage:F0}%";
            }
            else
            {
                StatusMessage = $"فشل التحليل: {response.ErrorMessage}";
            }
        }

        private bool CanTransfer() => SelectedUnit != null && CurrentGraph != null && HasBothPaths;

        private async Task TransferUnitAsync()
        {
            if (SelectedUnit == null || CurrentGraph == null || !HasBothPaths)
            {
                StatusMessage = "الرجاء التأكد من اختيار وحدة ومسارات صحيحة";
                return;
            }

            IsLoading = true;
            ProgressValue = 0;
            StatusMessage = $"جاري نقل {SelectedUnit.TechnicalName} والتبعيات...";

            try
            {
                // 1. حل التبعيات الذكي
                StatusMessage = $"جاري حل التبعيات لـ {SelectedUnit.TechnicalName}...";
                var resolvedGraph = await _dependencyResolver.ResolveDependenciesAsync(
                    SelectedUnit.TechnicalName,
                    SourceModPath);

                if (resolvedGraph.AllNodes.Count == 0)
                {
                    StatusMessage = "لم يتم العثور على أي تبعيات للنقل";
                    IsLoading = false;
                    return;
                }

                // 2. التحقق من التبعيات
                StatusMessage = "جاري التحقق من وجود الملفات...";
                await _dependencyResolver.ValidateDependenciesAsync(resolvedGraph, SourceModPath);

                // 3. بدء عملية النقل الذكي
                var progress = new Progress<TransferProgress>(p =>
                {
                    ProgressValue = (int)p.PercentageComplete;
                    StatusMessage = $"جاري نقل: {p.CurrentFileName} ({p.CurrentFileIndex}/{p.TotalFiles})";
                });

                var transferResult = await _smartTransferService.TransferAsync(
                    resolvedGraph,
                    SourceModPath,
                    TargetModPath,
                    progress);

                if (transferResult.Success)
                {
                    StatusMessage = $"✓ تم نقل {transferResult.TransferredFilesCount} ملف بنجاح في {transferResult.Duration.TotalSeconds:F1}ث";
                    ProgressValue = 100;
                }
                else
                {
                    StatusMessage = $"⚠ {transferResult.Message}";
                    if (transferResult.FailedFiles.Count > 0)
                    {
                        StatusMessage += $" ({transferResult.FailedFiles.Count} فشل)";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ خطأ في النقل: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
