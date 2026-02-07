using System.Collections.ObjectModel;
using System.Windows.Input;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.UseCases;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.Normalization;
using ZeroHourStudio.UI.WPF.Commands;
using ZeroHourStudio.UI.WPF.Core;

namespace ZeroHourStudio.UI.WPF.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IAnalyzeUnitDependenciesUseCase _analyzeUseCase;
        private readonly ITransferUnitUseCase _transferUseCase;
        private readonly SmartNormalization _normalizer;

        private string _searchText = string.Empty;
        private bool _isLoading;
        private string _statusMessage = "جاهز";
        private int _progressValue;
        private SageUnit? _selectedUnit;
        private UnitDependencyGraph? _currentGraph;
        private ObservableCollection<SageUnit> _units = new();

        public MainViewModel(
            IAnalyzeUnitDependenciesUseCase analyzeUseCase,
            ITransferUnitUseCase transferUseCase,
            SmartNormalization normalizer)
        {
            _analyzeUseCase = analyzeUseCase;
            _transferUseCase = transferUseCase;
            _normalizer = normalizer;

            SearchCommand = new RelayCommand(_ => PerformSearch());
            TransferCommand = new AsyncRelayCommand(_ => TransferUnitAsync(), _ => CanTransfer());
            LoadModCommand = new AsyncRelayCommand(_ => LoadModAsync());
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

        private void PerformSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            // استخدام التطبيع الذكي
            var normalized = _normalizer.NormalizeFactionName(SearchText);
            SearchText = normalized.Value;
            
            StatusMessage = $"تم التطبيع إلى: {normalized.Value}";
        }

        public async Task LoadModAsync()
        {
            IsLoading = true;
            StatusMessage = "جاري مسح الملفات (35,326 عنصراً)...";
            ProgressValue = 0;

            try
            {
                // محاكاة عملية الفهرسة الطويلة
                for (int i = 0; i <= 100; i += 10)
                {
                    ProgressValue = i;
                    await Task.Delay(100); // محاكاة العمل
                }

                // إضافة بعض الوحدات التجريبية
                Units.Clear();
                Units.Add(new SageUnit { TechnicalName = "ChinaTankOverlord", Side = "China", BuildCost = 2000, ModelW3D = "NVOVLRD.W3D" });
                Units.Add(new SageUnit { TechnicalName = "USAFighterRaptor", Side = "USA", BuildCost = 1200, ModelW3D = "AVRAPTOR.W3D" });
                Units.Add(new SageUnit { TechnicalName = "GLAInfantryRebel", Side = "GLA", BuildCost = 150, ModelW3D = "UIREBEL.W3D" });

                StatusMessage = "تم تحميل المود بنجاح.";
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

        private bool CanTransfer() => SelectedUnit != null && CurrentGraph != null;

        private async Task TransferUnitAsync()
        {
            if (SelectedUnit == null || CurrentGraph == null) return;

            IsLoading = true;
            StatusMessage = $"جاري نقل {SelectedUnit.TechnicalName}...";

            var request = new TransferUnitRequest
            {
                UnitId = SelectedUnit.TechnicalName,
                DependencyGraph = CurrentGraph,
                DestinationFolderPath = "C:\\ZeroHour_Target_Mod"
            };

            var response = await _transferUseCase.ExecuteAsync(request);
            StatusMessage = response.Message;
            IsLoading = false;
        }
    }
}
