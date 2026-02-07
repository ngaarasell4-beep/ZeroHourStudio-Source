using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Parsers;

namespace ZeroHourStudio.Infrastructure.DependencyAnalysis;

/// <summary>
/// محلل التبعيات الذكي - يقوم ببناء رسم بياني كامل للتبعات
/// يتتبع السلسلة: Object INI -> Armor.ini -> Weapon.ini -> Projectile.ini -> FXList.ini -> Audio.ini
/// </summary>
public class UnitDependencyAnalyzer : IUnitDependencyAnalyzer
{
    private readonly SAGE_IniParser _iniParser;
    private readonly HashSet<string> _visitedNodes; // لتجنب الحلقات
    private const int MaxDepth = 10; // عمق أقصى للرسم البياني

    /// <summary>
    /// السلسلة الأساسية للتبعيات
    /// </summary>
    private static readonly string[] DependencyChain = new[]
    {
        "armor.ini",
        "weapon.ini",
        "projectile.ini",
        "fxlist.ini",
        "audio.ini"
    };

    public UnitDependencyAnalyzer(SAGE_IniParser iniParser)
    {
        _iniParser = iniParser ?? throw new ArgumentNullException(nameof(iniParser));
        _visitedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// بناء رسم بياني كامل لتبعيات الوحدة
    /// </summary>
    public async Task<UnitDependencyGraph> AnalyzeDependenciesAsync(
        string unitId,
        string unitName,
        Dictionary<string, string> unitData)
    {
        if (string.IsNullOrWhiteSpace(unitId))
            throw new ArgumentNullException(nameof(unitId));

        _visitedNodes.Clear();

        var graph = new UnitDependencyGraph
        {
            UnitId = unitId,
            UnitName = unitName
        };

        // إنشاء العقدة الجذر
        var rootNode = new DependencyNode
        {
            Name = $"{unitName}.ini",
            Type = DependencyType.ObjectINI,
            Depth = 0,
            Status = AssetStatus.NotVerified
        };

        graph.RootNode = rootNode;
        graph.AllNodes.Add(rootNode);
        _visitedNodes.Add(rootNode.Name);

        // البدء ببناء الرسم البياني بشكل عودي
        await BuildDependencyGraphRecursiveAsync(
            rootNode,
            unitData,
            graph,
            depth: 0);

        // حساب الإحصائيات
        CalculateGraphStatistics(graph);

        return graph;
    }

    /// <summary>
    /// بناء الرسم البياني بشكل عودي
    /// </summary>
    private async Task BuildDependencyGraphRecursiveAsync(
        DependencyNode parentNode,
        Dictionary<string, string> nodeData,
        UnitDependencyGraph graph,
        int depth)
    {
        // منع الذهاب لعمق أكبر من الحد الأقصى أو الحلقات
        if (depth >= MaxDepth || parentNode.IsVisited)
            return;

        parentNode.IsVisited = true;

        // معالجة سلسلة التبعيات الأساسية
        foreach (var dependencyFile in DependencyChain)
        {
            // البحث عن المرجع لهذا الملف التبعي
            var referenceValue = FindReferenceInData(nodeData, dependencyFile);

            if (!string.IsNullOrEmpty(referenceValue))
            {
                var childNode = await CreateDependencyNodeAsync(
                    referenceValue,
                    MapFileToDependencyType(dependencyFile),
                    depth + 1);

                if (childNode != null && !_visitedNodes.Contains(childNode.Name))
                {
                    parentNode.Dependencies.Add(childNode);
                    graph.AllNodes.Add(childNode);
                    _visitedNodes.Add(childNode.Name);

                    // محاولة تحليل تبعيات الملف الجديد
                    // (قد لا يكون ممكناً إذا لم يكن لدينا بيانات الملف)
                }
            }
        }

        // تحديث عمق الرسم البياني
        if (depth > graph.MaxDepth)
            graph.MaxDepth = depth;
    }

    /// <summary>
    /// البحث عن مرجع معين في بيانات الوحدة
    /// </summary>
    private string? FindReferenceInData(Dictionary<string, string> unitData, string referenceKey)
    {
        if (unitData == null || string.IsNullOrEmpty(referenceKey))
            return null;

        // البحث بصيغ مختلفة
        var keys = new[]
        {
            referenceKey,
            referenceKey.Replace(".ini", ""),
            $"{referenceKey}Name",
            $"Inherits{referenceKey.Replace(".ini", "")}",
        };

        foreach (var key in keys)
        {
            if (unitData.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// إنشاء عقدة تبعية جديدة
    /// </summary>
    private async Task<DependencyNode?> CreateDependencyNodeAsync(
        string referenceName,
        DependencyType type,
        int depth)
    {
        if (string.IsNullOrWhiteSpace(referenceName))
            return null;

        var node = new DependencyNode
        {
            Name = referenceName,
            Type = type,
            Depth = depth,
            Status = AssetStatus.NotVerified
        };

        return await Task.FromResult(node);
    }

    /// <summary>
    /// تحويل اسم الملف إلى نوع التبعية
    /// </summary>
    private DependencyType MapFileToDependencyType(string fileName)
    {
        return fileName.ToLowerInvariant() switch
        {
            "armor.ini" => DependencyType.Armor,
            "weapon.ini" => DependencyType.Weapon,
            "projectile.ini" => DependencyType.Projectile,
            "fxlist.ini" => DependencyType.FXList,
            "audio.ini" => DependencyType.Audio,
            _ => DependencyType.Custom
        };
    }

    /// <summary>
    /// حساب إحصائيات الرسم البياني
    /// </summary>
    private void CalculateGraphStatistics(UnitDependencyGraph graph)
    {
        graph.FoundCount = graph.AllNodes.Count(n => n.Status == AssetStatus.Found);
        graph.MissingCount = graph.AllNodes.Count(n => n.Status == AssetStatus.Missing);

        graph.TotalSizeInBytes = graph.AllNodes
            .Where(n => n.SizeInBytes.HasValue)
            .Sum(n => n.SizeInBytes.Value);

        // تحديد حالة الاكتمال
        double completionPercentage = graph.GetCompletionPercentage();

        graph.Status = completionPercentage switch
        {
            100 => CompletionStatus.Complete,
            >= 80 => CompletionStatus.Partial,
            > 0 => CompletionStatus.Incomplete,
            _ => CompletionStatus.CannotVerify
        };
    }

    /// <summary>
    /// الحصول على مسار الاعتماديات كنصوص
    /// </summary>
    public List<string> GetDependencyPathsAsText(UnitDependencyGraph graph)
    {
        var paths = new List<string>();

        if (graph.RootNode == null)
            return paths;

        void TraversePaths(DependencyNode node, List<string> currentPath)
        {
            currentPath.Add($"{node.Name} ({node.Type})");

            if (node.Dependencies.Count == 0)
            {
                paths.Add(string.Join(" -> ", currentPath));
            }
            else
            {
                foreach (var child in node.Dependencies)
                {
                    TraversePaths(child, new List<string>(currentPath));
                }
            }
        }

        TraversePaths(graph.RootNode, new List<string>());
        return paths;
    }

    /// <summary>
    /// الحصول على عداد التبعيات حسب النوع
    /// </summary>
    public Dictionary<DependencyType, int> GetDependencyCountByType(UnitDependencyGraph graph)
    {
        return graph.AllNodes
            .GroupBy(n => n.Type)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
