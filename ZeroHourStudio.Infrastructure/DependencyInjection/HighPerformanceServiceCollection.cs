using Microsoft.Extensions.DependencyInjection;
using ZeroHourStudio.Infrastructure.HighPerformance;

namespace ZeroHourStudio.Infrastructure.DependencyInjection;

/// <summary>
/// ãÍÑÑ ÇáÍŞä ÇáÊÇÈÚ (Dependency Injection Extension)
/// ááÚãáíÇÊ ÇáãæÌåÉ äÍæ ÇáÃÏÇÁ ÇáÚÇáíÉ
///
/// ÇáÇÓÊÎÏÇã:
/// var services = new ServiceCollection();
/// services.AddHighPerformanceServices();
/// var provider = services.BuildServiceProvider();
/// </summary>
public static class HighPerformanceServiceCollectionExtensions
{
    /// <summary>
    /// ÊÓÌíá ÌãíÚ ÎÏãÇÊ ÇáÃÏÇÁ ÇáÚÇáíÉ İí ÍÇæíÉ DI
    /// </summary>
    public static IServiceCollection AddHighPerformanceServices(
        this IServiceCollection services,
        int searchCacheCapacity = 10000)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // ÊÓÌíá ãÍÑß ÇáÇÓÊÎÑÇÌ ßÜ Singleton
        services.AddSingleton<HighPerformanceExtractionEngine>();

        // ÊÓÌíá ãÍÑß ÇáÈÍË ßÜ Singleton
        services.AddSingleton(sp => new UTF16ArabicBinarySearchCache(searchCacheCapacity));

        // ÊÓÌíá ãÍÑß Íá ÇáÊÈÚíÇÊ ßÜ Transient
        services.AddTransient<RecursiveAssetResolver>((sp) =>
        {
            var extractionEngine = sp.GetRequiredService<HighPerformanceExtractionEngine>();
            var searchCache = sp.GetRequiredService<UTF16ArabicBinarySearchCache>();

            return new RecursiveAssetResolver(extractionEngine, searchCache);
        });

        // ÊÓÌíá ÇáÎÏãÉ ÇáãæÍÏÉ ááÃÏÇÁ ÇáÚÇáí
        services.AddScoped<IHighPerformanceService, HighPerformanceService>();

        return services;
    }
}

/// <summary>
/// æÇÌåÉ ÇáÎÏãÉ ÇáãæÍÏÉ ááÃÏÇÁ ÇáÚÇáí
/// ÊÌãÚ Èíä ÌãíÚ ãÍÑßÇÊ ÇáÃÏÇÁ ÇáÚÇáíÉ
/// </summary>
public interface IHighPerformanceService
{
    /// <summary>
    /// ÇÓÊÎÑÇÌ ãÍÊæì INI ÈÓÑÚÉ
    /// </summary>
    Task<Dictionary<string, Dictionary<string, string>>> ExtractIniAsync(string filePath);

    /// <summary>
    /// ÇáÈÍË Úä ÃÕá ÈÔßá ÚäŞæÏí
    /// </summary>
    Task<Application.Models.DependencyNode> ResolveAssetAsync(string assetPath, string baseDirectory);

    /// <summary>
    /// ÅÌÑÇÁ ÈÍË ÚÑÈí ãÊŞÏã
    /// </summary>
    int BinarySearchArabic(string[] items, string target);

    /// <summary>
    /// ÅäÔÇÁ ÊŞÑíÑ ÔÇãá ááÊÈÚíÇÊ
    /// </summary>
    DependencyTreeReport GenerateDependencyReport(Application.Models.DependencyNode rootNode);
}

/// <summary>
/// ÊäİíĞ ÇáÎÏãÉ ÇáãæÍÏÉ
/// </summary>
public class HighPerformanceService : IHighPerformanceService
{
    private readonly HighPerformanceExtractionEngine _extractionEngine;
    private readonly UTF16ArabicBinarySearchCache _searchCache;
    private readonly RecursiveAssetResolver _assetResolver;

    public HighPerformanceService(
        HighPerformanceExtractionEngine extractionEngine,
        UTF16ArabicBinarySearchCache searchCache,
        RecursiveAssetResolver assetResolver)
    {
        _extractionEngine = extractionEngine ?? throw new ArgumentNullException(nameof(extractionEngine));
        _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        _assetResolver = assetResolver ?? throw new ArgumentNullException(nameof(assetResolver));
    }

    public async Task<Dictionary<string, Dictionary<string, string>>> ExtractIniAsync(string filePath)
    {
        return await _extractionEngine.ExtractIniContentAsync(filePath);
    }

    public async Task<Application.Models.DependencyNode> ResolveAssetAsync(string assetPath, string baseDirectory)
    {
        return await _assetResolver.ResolveAssetRecursivelyAsync(assetPath, baseDirectory);
    }

    public int BinarySearchArabic(string[] items, string target)
    {
        return _searchCache.BinarySearchArabic(items, target);
    }

    public DependencyTreeReport GenerateDependencyReport(Application.Models.DependencyNode rootNode)
    {
        return _assetResolver.GenerateDependencyTreeReport(rootNode);
    }
}
