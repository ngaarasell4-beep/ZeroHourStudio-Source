using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Application.UseCases;
using ZeroHourStudio.Infrastructure.Archives;
using ZeroHourStudio.Infrastructure.ConflictResolution;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.DependencyResolution;
using ZeroHourStudio.Infrastructure.Implementations;
using ZeroHourStudio.Infrastructure.Localization;
using ZeroHourStudio.Infrastructure.Normalization;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.Transfer;
using ZeroHourStudio.Infrastructure.Validation;

namespace ZeroHourStudio.UI.WPF.Services;

/// <summary>
/// مصنع الخدمات المركزي - ينشئ جميع الخدمات المطلوبة
/// </summary>
public static class ServiceFactory
{
    public static SmartNormalization CreateNormalization() => new();
    public static UnitDiscoveryService CreateUnitDiscovery() => new();
    public static CommandSetPatchService CreateCommandSetPatch() => new();
    public static SageDefinitionIndex CreateSageIndex() => new();
    public static MappedImageIndex CreateMappedImageIndex() => new();
    public static SAGE_IniParser CreateIniParser() => new();

    public static ModBigFileReader CreateBigFileReader(string modPath)
        => new(modPath);

    public static SmartDependencyResolver CreateDependencyResolver(IBigFileReader bigFileReader)
        => new(bigFileReader);

    public static SmartTransferService CreateTransferService(IBigFileReader? bigFileReader = null)
        => new(bigFileReader);

    public static IAnalyzeUnitDependenciesUseCase CreateAnalyzeUseCase()
    {
        var parser = CreateIniParser();
        var analyzer = new UnitDependencyAnalyzer(parser);
        var validator = new UnitCompletionValidator();
        return new AnalyzeUnitDependenciesUseCase(analyzer, validator);
    }

    public static ITransferUnitUseCase CreateTransferUseCase(IBigFileReader bigFileReader)
        => new TransferUnitUseCase(bigFileReader);

    public static MonitoredWeaponAnalysisService CreateWeaponAnalysis(SAGE_IniParser parser, IBigFileReader bigFileReader)
        => new(parser, bigFileReader);

    public static ConflictDetectionService CreateConflictDetection()
        => new();

    public static SmartRenamingService CreateSmartRenaming()
        => new();

    public static VirtualFileSystem CreateVirtualFileSystem()
        => new();

    public static CsfLocalizationService CreateCsfLocalization()
        => new();

    public static IconService CreateIconService(MappedImageIndex mappedImageIndex)
        => new(mappedImageIndex);

    // === خدمات الذكاء الاصطناعي ===
    public static ConflictIntelligenceEngine CreateConflictIntelligence()
        => new();

    public static ManualEditAutoResolver CreateManualEditResolver()
        => new();

    public static TransferHealthAnalyzer CreateTransferHealthAnalyzer()
        => new();

    // === القوى الخارقة ===
    public static SmartMergeEngine CreateSmartMerge()
        => new();

    public static IniDefinitionParser CreateIniParser2()
        => new();

    public static TransferJournal CreateTransferJournal(string targetModPath)
        => new(targetModPath);

    public static RollbackService CreateRollbackService()
        => new();

    public static BatchTransferService CreateBatchTransfer(TransferPipelineService pipeline)
        => new(pipeline);

}
