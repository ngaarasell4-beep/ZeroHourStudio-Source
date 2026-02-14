using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.AssetManagement;
using ZeroHourStudio.Infrastructure.Validation;
using ZeroHourStudio.Infrastructure.Services;

namespace ZeroHourStudio.Infrastructure.DependencyAnalysis;

/// <summary>
/// يوفر أمثلة استخدام لنظام تحليل التبعيات الشامل
/// </summary>
internal static class DependencyAnalysisExamples
{
    /// <summary>
    /// مثال 1: بناء رسم بياني للتبعيات
    /// </summary>
    public static async Task Example1_BuildDependencyGraph()
    {
        var iniParser = new SAGE_IniParser();
        var analyzer = new UnitDependencyAnalyzer(iniParser);

        // بيانات وحدة مثالية
        var unitData = new Dictionary<string, string>
        {
            { "Name", "GDI_Ranger" },
            { "Armor", "InfantryArmor" },
            { "Weapon", "RifleWeapon" },
            { "Projectile", "RifleProjectile" },
            { "FXList", "InfantryFX" },
            { "Audio", "InfantryAudio" }
        };

        // بناء الرسم البياني
        var graph = await analyzer.AnalyzeDependenciesAsync(
            "unit_001",
            "GDI Ranger",
            unitData);

        // طباعة النتائج
        Console.WriteLine($"Graph Depth: {graph.MaxDepth}");
        Console.WriteLine($"Total Nodes: {graph.AllNodes.Count}");
        Console.WriteLine($"Completion: {graph.GetCompletionPercentage():F1}%");

        // طباعة مسارات التبعيات
        var paths = analyzer.GetDependencyPathsAsText(graph);
        foreach (var path in paths)
        {
            Console.WriteLine($"  Path: {path}");
        }
    }

    /// <summary>
    /// مثال 2: البحث عن الأصول
    /// </summary>
    public static async Task Example2_FindAssets()
    {
        var hunter = new AssetReferenceHunter();

        // البحث عن مورد معين
        var assets = await hunter.FindAssetsAsync("GDI_Ranger");
        foreach (var asset in assets)
        {
            Console.WriteLine($"Found: {asset.Name} ({asset.Type}) - Status: {asset.Status}");
        }

        // الحصول على إحصائيات
        var stats = await hunter.GetAssetStatisticsAsync();
        Console.WriteLine($"Total Assets: {stats.TotalAssetCount}");
        Console.WriteLine($"Total Size: {stats.GetTotalSizeInMB():F2} MB");
    }

    /// <summary>
    /// مثال 3: التحقق من اكتمال الوحدة
    /// </summary>
    public static void Example3_ValidateUnit()
    {
        var validator = new UnitCompletionValidator();

        // بناء رسم بياني (مثالي)
        var graph = new UnitDependencyGraph
        {
            UnitId = "unit_001",
            UnitName = "GDI Ranger",
            AllNodes = new List<DependencyNode>
            {
                new() { Name = "ranger.ini", Type = DependencyType.ObjectINI, Status = AssetStatus.Found },
                new() { Name = "ranger.w3d", Type = DependencyType.Model3D, Status = AssetStatus.Found },
                new() { Name = "ranger.dds", Type = DependencyType.Texture, Status = AssetStatus.Found },
                new() { Name = "ranger_fire.wav", Type = DependencyType.Audio, Status = AssetStatus.Missing }
            }
        };

        // التحقق
        var result = validator.ValidateUnitCompletion("unit_001", graph);

        Console.WriteLine($"Valid: {result.IsValid}");
        Console.WriteLine($"Errors: {result.Errors.Count}");
        Console.WriteLine($"Warnings: {result.Warnings.Count}");

        // طباعة التقرير
        string report = validator.GenerateDetailedReport(result, graph);
        Console.WriteLine(report);
    }

    /// <summary>
    /// مثال 4: التحليل الشامل
    /// </summary>
    public static async Task Example4_ComprehensiveAnalysis()
    {
        var iniParser = new SAGE_IniParser();
        var analyzer = new UnitDependencyAnalyzer(iniParser);
        var hunter = new AssetReferenceHunter();
        var validator = new UnitCompletionValidator();

        var service = new ComprehensiveDependencyService(
            analyzer,
            hunter,
            validator);

        // بيانات الوحدة
        var unitData = new Dictionary<string, string>
        {
            { "Name", "GDI_Ranger" },
            { "Armor", "InfantryArmor" },
            { "Weapon", "RifleWeapon" }
        };

        // تحليل شامل
        var result = await service.AnalyzeUnitComprehensivelyAsync(
            "unit_001",
            "GDI Ranger",
            unitData);

        // طباعة النتائج
        Console.WriteLine(result.ToString());
        Console.WriteLine();

        // طباعة التقرير
        string report = service.GenerateComprehensiveReport(result);
        Console.WriteLine(report);
    }

    /// <summary>
    /// مثال 5: تحليل عدة وحدات
    /// </summary>
    public static async Task Example5_AnalyzeMultipleUnits()
    {
        var iniParser = new SAGE_IniParser();
        var analyzer = new UnitDependencyAnalyzer(iniParser);
        var hunter = new AssetReferenceHunter();
        var validator = new UnitCompletionValidator();

        var service = new ComprehensiveDependencyService(
            analyzer,
            hunter,
            validator);

        // مجموعة من الوحدات
        var units = new Dictionary<string, (string, Dictionary<string, string>)>
        {
            {
                "unit_001",
                ("GDI Ranger", new Dictionary<string, string>
                {
                    { "Name", "GDI_Ranger" },
                    { "Armor", "InfantryArmor" }
                })
            },
            {
                "unit_002",
                ("GDI Soldier", new Dictionary<string, string>
                {
                    { "Name", "GDI_Soldier" },
                    { "Armor", "InfantryArmor" }
                })
            }
        };

        // تحليل الكل
        var results = await service.AnalyzeMultipleUnitsAsync(units);

        foreach (var result in results)
        {
            Console.WriteLine($"  {result.UnitName}: {result.CompletionStatus}");
        }
    }
}
