using Microsoft.Extensions.DependencyInjection;
using ZeroHourStudio.Infrastructure.HighPerformance;
using ZeroHourStudio.Infrastructure.DependencyInjection;

namespace ZeroHourStudio.Infrastructure.Examples;

/// <summary>
/// √„À·… ⁄„·Ì… ⁄·Ï «” Œœ«„ „ﬂÊ‰«  «·√œ«¡ «·⁄«·Ì…
/// 
/// ÌÊ÷Õ Â–« «·„·› ﬂÌ›Ì… «” Œœ«„:
/// 1. „Õ—ﬂ «·«” Œ—«Ã «·”—Ì⁄
/// 2. „Õ—ﬂ «·»ÕÀ «·À‰«∆Ì «·„ ﬁœ„
/// 3. ‰Ÿ«„ Õ· «· »⁄Ì«  «·⁄‰ﬁÊœÌ
/// 4. Dependency Injection
/// </summary>
public static class HighPerformanceUsageExamples
{
    /// <summary>
    /// „À«· 1: «” Œœ«„ „Õ—ﬂ «·«” Œ—«Ã «·”—Ì⁄
    /// </summary>
    public static async Task Example1_FastExtractionEngine()
    {
        Console.WriteLine("=== „À«· 1: „Õ—ﬂ «·«” Œ—«Ã «·”—Ì⁄ ===\n");

        using var engine = new HighPerformanceExtractionEngine();

        // «” Œ—«Ã „Õ ÊÏ „·› INI
        try
        {
            var iniData = await engine.ExtractIniContentAsync("path/to/units.ini");

            Console.WriteLine("?  „ «” Œ—«Ã „Õ ÊÏ INI »‰Ã«Õ");
            Console.WriteLine($"  ⁄œœ «·√ﬁ”«„: {iniData.Count}");

            foreach (var section in iniData.Keys.Take(3))
            {
                Console.WriteLine($"  - «·ﬁ”„: {section}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Œÿ√: {ex.Message}");
        }

        // «” Œ—«Ã ﬂ«∆‰ ﬂ«„·
        try
        {
            var objectCode = await engine.ExtractCompleteObjectAsync(
                "path/to/objects.ini",
                "M14Rifle");

            if (objectCode != null)
            {
                Console.WriteLine("\n?  „ «” Œ—«Ã «·ﬂ«∆‰ »‰Ã«Õ");
                Console.WriteLine($"  «·ÿÊ·: {objectCode.Length} Õ—›");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n? Œÿ√: {ex.Message}");
        }
    }

    /// <summary>
    /// „À«· 2: «” Œœ«„ „Õ—ﬂ «·»ÕÀ «·À‰«∆Ì „⁄ œ⁄„ «·⁄—»Ì…
    /// </summary>
    public static void Example2_ArabicBinarySearch()
    {
        Console.WriteLine("\n=== „À«· 2: «·»ÕÀ «·À‰«∆Ì „⁄ œ⁄„ «·⁄—»Ì… ===\n");

        using var searchCache = new UTF16ArabicBinarySearchCache();

        // »Ì«‰«   Ã—Ì»Ì…
        var weapons = new[] { "AK47", "M16", "M4", "M14", "Handgun", "Sniper" };

        // «·»ÕÀ «·À‰«∆Ì
        int index = searchCache.BinarySearchArabic(weapons, "M16");
        Console.WriteLine($"? «·»ÕÀ ⁄‰ 'M16': «·›Â—” = {index}");

        // «·»ÕÀ ⁄‰ ‰ÿ«ﬁ
        var (start, end) = searchCache.BinarySearchRangeArabic(weapons, "M");
        Console.WriteLine($"? «·»ÕÀ ⁄‰ ‰ÿ«ﬁ 'M': „‰ {start} ≈·Ï {end}");

        // Fuzzy Search
        var fuzzyResults = searchCache.FuzzySearchArabic(weapons, "M14", maxDistance: 1);
        Console.WriteLine($"? «·»ÕÀ «·›«“Ì ⁄‰ 'M14':");
        foreach (var (word, distance) in fuzzyResults)
        {
            Console.WriteLine($"  - {word} («·„”«›…: {distance})");
        }

        //  Õ·Ì·  ﬂ—«— «·ﬂ·„« 
        string text = "„Õ„œ ⁄·Ì „Õ„œ ”«—… ⁄·Ì ⁄·Ì —«∆œ";
        var frequency = searchCache.AnalyzeWordFrequency(text);
        Console.WriteLine($"\n?  Õ·Ì·  ﬂ—«— «·ﬂ·„« :");
        foreach (var (word, count) in frequency.OrderByDescending(x => x.Value).Take(3))
        {
            Console.WriteLine($"  - {word}: {count} „—« ");
        }

        // ≈Õ’«∆Ì«  «·ﬂ«‘
        var (indexCount, tokenCount, freqCount) = searchCache.GetCacheStats();
        Console.WriteLine($"\n? ≈Õ’«∆Ì«  «·ﬂ«‘:");
        Console.WriteLine($"  - ›Â«—” «·»ÕÀ: {indexCount}");
        Console.WriteLine($"  - «· Êﬂ‰« : {tokenCount}");
        Console.WriteLine($"  -  Õ·Ì·«  «· ﬂ—«—: {freqCount}");
    }

    /// <summary>
    /// „À«· 3: Õ· «· »⁄Ì«  «·⁄‰ﬁÊœÌ
    /// </summary>
    public static async Task Example3_RecursiveAssetResolver()
    {
        Console.WriteLine("\n=== „À«· 3: Õ· «· »⁄Ì«  «·⁄‰ﬁÊœÌ ===\n");

        using var engine = new HighPerformanceExtractionEngine();
        using var searchCache = new UTF16ArabicBinarySearchCache();
        using var resolver = new RecursiveAssetResolver(engine, searchCache);

        try
        {
            // Õ· «· »⁄Ì« 
            var rootNode = await resolver.ResolveAssetRecursivelyAsync(
                "D:/Mods/MyMod/data/units/m1_abrams.ini",
                "D:/Mods/MyMod/data");

            Console.WriteLine("?  „ Õ· «· »⁄Ì«  »‰Ã«Õ");
            Console.WriteLine($"  «·√’·: {rootNode.Name}");
            Console.WriteLine($"  «·‰Ê⁄: {rootNode.Type}");
            Console.WriteLine($"  «·Õ«·…: {rootNode.Status}");
            Console.WriteLine($"  «·⁄„ﬁ: {rootNode.Depth}");
            Console.WriteLine($"  ⁄œœ «· »⁄Ì«  «·„»«‘—…: {rootNode.Dependencies.Count}");

            //  Ê·Ìœ  ﬁ—Ì—
            var report = resolver.GenerateDependencyTreeReport(rootNode);
            Console.WriteLine($"\n?  ﬁ—Ì— «· »⁄Ì« :");
            Console.WriteLine($"  - ≈Ã„«·Ì «·⁄ﬁœ: {report.TotalNodes}");
            Console.WriteLine($"  - «·⁄„ﬁ «·√ﬁ’Ï: {report.MaxDepth}");
            Console.WriteLine($"  - Êﬁ  «·≈‰‘«¡: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Œÿ√: {ex.Message}");
        }
    }

    /// <summary>
    /// „À«· 4: «” Œœ«„ Dependency Injection
    /// </summary>
    public static async Task Example4_DependencyInjection()
    {
        Console.WriteLine("\n=== „À«· 4: Dependency Injection ===\n");

        // ≈⁄œ«œ «·Œœ„« 
        var services = new ServiceCollection();
        services.AddHighPerformanceServices(searchCacheCapacity: 5000);
        var provider = services.BuildServiceProvider();

        try
        {
            // «·Õ’Ê· ⁄·Ï «·Œœ„…
            var hpService = provider.GetRequiredService<IHighPerformanceService>();

            Console.WriteLine("?  „ ≈⁄œ«œ Œœ„«  «·√œ«¡ «·⁄«·Ì…");

            // «” Œœ«„ «·Œœ„…
            var iniData = await hpService.ExtractIniAsync("path/to/data.ini");
            Console.WriteLine($"?  „ «” Œ—«Ã INI: {iniData.Count} ﬁ”„");

            var assets = await hpService.ResolveAssetAsync(
                "path/to/unit.ini",
                "D:/Mods/MyMod");
            Console.WriteLine($"?  „ Õ· «· »⁄Ì« : {assets.Name}");

            var searchResult = hpService.BinarySearchArabic(
                new[] { "Item1", "Item2", "Item3" },
                "Item2");
            Console.WriteLine($"? ‰ ÌÃ… «·»ÕÀ: {searchResult}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Œÿ√: {ex.Message}");
        }
    }

    /// <summary>
    /// „À«· 5: «·‹ Pipeline «·ﬂ«„·
    /// </summary>
    public static async Task Example5_CompletePipeline()
    {
        Console.WriteLine("\n=== „À«· 5: «·‹ Pipeline «·ﬂ«„· ===\n");

        using var engine = new HighPerformanceExtractionEngine();
        using var searchCache = new UTF16ArabicBinarySearchCache();
        using var resolver = new RecursiveAssetResolver(engine, searchCache);

        try
        {
            Console.WriteLine("«·„—Õ·… 1: ﬁ—«¡… „·› «·ÊÕœ…...");
            var unitText = await engine.ReadTextEfficientlyAsync("D:/Mods/MyMod/units.ini");
            Console.WriteLine($"?  „ «·ﬁ—«¡… ({unitText.Length} Õ—›)");

            Console.WriteLine("\n«·„—Õ·… 2: «” Œ—«Ã „Õ ÊÏ INI...");
            var iniData = await engine.ExtractIniContentAsync("D:/Mods/MyMod/units.ini");
            Console.WriteLine($"?  „ «·«” Œ—«Ã ({iniData.Count} ﬁ”„)");

            Console.WriteLine("\n«·„—Õ·… 3: „⁄«·Ã… «·‰’ «·⁄—»Ì...");
            var normalized = searchCache.NormalizeArabicText(unitText);
            var tokens = searchCache.TokenizeArabicText(normalized);
            Console.WriteLine($"?  „ «·„⁄«·Ã… ({tokens.Length}  Êﬂ‰)");

            Console.WriteLine("\n«·„—Õ·… 4: Õ· «· »⁄Ì«  «·⁄‰ﬁÊœÌ…...");
            var dependencyNode = await resolver.ResolveAssetRecursivelyAsync(
                "D:/Mods/MyMod/data/units.ini",
                "D:/Mods/MyMod/data");
            Console.WriteLine($"?  „ «·Õ· ({dependencyNode.Name})");

            Console.WriteLine("\n«·„—Õ·… 5:  Ê·Ìœ «· ﬁ—Ì—...");
            var report = resolver.GenerateDependencyTreeReport(dependencyNode);
            Console.WriteLine($"?  „ «· ﬁ—Ì— ({report.TotalNodes} ⁄ﬁœ…)");

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("? «ﬂ „· «·‹ Pipeline »‰Ã«Õ!");
            Console.WriteLine(new string('=', 50));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Œÿ√ ›Ì «·‹ Pipeline: {ex.Message}");
        }
    }

    /// <summary>
    /// „À«· 6: ﬁÌ«” «·√œ«¡
    /// </summary>
    public static async Task Example6_PerformanceBenchmark()
    {
        Console.WriteLine("\n=== „À«· 6: ﬁÌ«” «·√œ«¡ ===\n");

        using var engine = new HighPerformanceExtractionEngine();
        using var searchCache = new UTF16ArabicBinarySearchCache();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // «Œ »«— «·«” Œ—«Ã
        try
        {
            await engine.ExtractIniContentAsync("D:/Mods/MyMod/data.ini");
            stopwatch.Stop();
            Console.WriteLine($"? «” Œ—«Ã INI: {stopwatch.ElapsedMilliseconds}ms");
        }
        catch { }

        stopwatch.Restart();

        // «Œ »«— «·»ÕÀ «·À‰«∆Ì
        var items = Enumerable.Range(0, 100000).Select(i => $"Item{i}").ToArray();
        int result = searchCache.BinarySearchArabic(items, "Item50000");
        stopwatch.Stop();
        Console.WriteLine($"? »ÕÀ À‰«∆Ì (100k ⁄‰’—): {stopwatch.ElapsedMilliseconds}ms");

        stopwatch.Restart();

        // «Œ »«— Fuzzy Search
        var fuzzyResults = searchCache.FuzzySearchArabic(
            items.Take(1000).ToArray(),
            "Item500",
            maxDistance: 2);
        stopwatch.Stop();
        Console.WriteLine($"? »ÕÀ ›«“Ì (1k ⁄‰’—): {stopwatch.ElapsedMilliseconds}ms");
    }
}
