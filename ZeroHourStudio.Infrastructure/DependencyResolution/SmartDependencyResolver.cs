using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using System.Text.RegularExpressions;

namespace ZeroHourStudio.Infrastructure.DependencyResolution;

/// <summary>
/// محرك حل التبعيات الذكي
/// يقوم بتحليل ملفات INI و BIG ليكتشف جميع التبعيات المطلوبة لوحدة
/// </summary>
public class SmartDependencyResolver : IDependencyResolver
{
    private readonly IBigFileReader _bigFileReader;
    private readonly Dictionary<string, DependencyNode> _cachedNodes = new();
    private HashSet<string> _visitedNodes = new();

    public SmartDependencyResolver(IBigFileReader bigFileReader)
    {
        _bigFileReader = bigFileReader ?? throw new ArgumentNullException(nameof(bigFileReader));
    }

    /// <summary>
    /// حل جميع التبعيات لوحدة معينة
    /// </summary>
    public async Task<UnitDependencyGraph> ResolveDependenciesAsync(string unitName, string sourceModPath)
    {
        var graph = new UnitDependencyGraph
        {
            UnitId = unitName,
            UnitName = unitName,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            // 1. البحث عن ملف INI للوحدة
            var iniFileName = $"{unitName}.ini";
            var iniPath = Path.Combine(sourceModPath, "Data", "INI", "Object", iniFileName);

            if (!File.Exists(iniPath))
            {
                graph.Status = CompletionStatus.CannotVerify;
                graph.Notes = $"لم يتم العثور على ملف INI: {iniFileName}";
                return graph;
            }

            // 2. إنشاء عقدة جذر
            var rootNode = new DependencyNode
            {
                Name = iniFileName,
                Type = DependencyType.ObjectINI,
                FullPath = iniPath,
                Status = AssetStatus.Found,
                Depth = 0
            };

            graph.RootNode = rootNode;
            graph.AllNodes.Add(rootNode);

            // 3. تحليل ملف INI لاستخراج التبعيات
            _visitedNodes.Clear();
            _cachedNodes.Clear();

            await ParseIniDependenciesAsync(rootNode, sourceModPath, graph);

            // 4. حساب الإحصائيات
            graph.FoundCount = graph.AllNodes.Count(n => n.Status == AssetStatus.Found);
            graph.MissingCount = graph.AllNodes.Count(n => n.Status == AssetStatus.Missing);
            graph.MaxDepth = graph.AllNodes.Max(n => n.Depth);

            // 5. تحديد حالة الاكتمال
            if (graph.MissingCount == 0)
                graph.Status = CompletionStatus.Complete;
            else if (graph.FoundCount > graph.MissingCount)
                graph.Status = CompletionStatus.Partial;
            else
                graph.Status = CompletionStatus.Incomplete;

            return graph;
        }
        catch (Exception ex)
        {
            graph.Status = CompletionStatus.CannotVerify;
            graph.Notes = $"خطأ أثناء حل التبعيات: {ex.Message}";
            return graph;
        }
    }

    /// <summary>
    /// تحليل ملف INI واستخراج التبعيات
    /// </summary>
    private async Task ParseIniDependenciesAsync(DependencyNode parentNode, string sourceModPath, UnitDependencyGraph graph)
    {
        if (parentNode.FullPath == null || _visitedNodes.Contains(parentNode.FullPath))
            return;

        _visitedNodes.Add(parentNode.FullPath);

        try
        {
            var iniContent = await File.ReadAllTextAsync(parentNode.FullPath);

            // استخراج جميع المراجع للملفات
            var filePattern = new Regex(@"(?:Model|Texture|W3D|DDS|Audio|Sound|Music)\s*=\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var matches = filePattern.Matches(iniContent);

            var nextDepth = parentNode.Depth + 1;

            foreach (Match match in matches)
            {
                var filePath = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(filePath))
                    continue;

                // تنظيف المسار
                filePath = CleanFilePath(filePath);

                // البحث عن الملف
                var fullPath = FindPriorityFile(sourceModPath, filePath);
                var dependencyNode = new DependencyNode
                {
                    Name = Path.GetFileName(filePath),
                    Type = DetermineDependencyType(filePath),
                    FullPath = fullPath,
                    Status = fullPath != null && File.Exists(fullPath) ? AssetStatus.Found : AssetStatus.Missing,
                    Depth = nextDepth
                };

                // تجنب التكرار
                if (!graph.AllNodes.Any(n => n.Name == dependencyNode.Name))
                {
                    graph.AllNodes.Add(dependencyNode);
                    parentNode.Dependencies.Add(dependencyNode);

                    // المتابعة العميقة (Deep Traversal) لملفات INI المتسلسلة
                    if (dependencyNode.Status == AssetStatus.Found && 
                        dependencyNode.Type == DependencyType.ObjectINI &&
                        nextDepth < 5) // تجنب الحلقات غير المنتهية
                    {
                        await ParseIniDependenciesAsync(dependencyNode, sourceModPath, graph);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // تسجيل الخطأ دون إيقاف المعالجة
            System.Diagnostics.Debug.WriteLine($"خطأ في تحليل INI: {ex.Message}");
        }
    }

    /// <summary>
    /// البحث عن الملف مع أولوية الملفات التي تبدأ بـ !!
    /// </summary>
    private string? FindPriorityFile(string sourceModPath, string fileName)
    {
        // البحث أولاً عن نسخة الأولوية (تبدأ بـ !!)
        var priorityFileName = $"!!{fileName}";
        var priorityPath = Path.Combine(sourceModPath, "Data", priorityFileName);

        if (File.Exists(priorityPath))
            return priorityPath;

        // البحث عن الملف العادي
        var normalPath = Path.Combine(sourceModPath, "Data", fileName);
        if (File.Exists(normalPath))
            return normalPath;

        // البحث المرن (في أي مكان في المجلد)
        try
        {
            var foundFiles = Directory.GetFiles(sourceModPath, Path.GetFileName(fileName), SearchOption.AllDirectories);
            return foundFiles.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// تحديد نوع التبعية بناءً على امتداد الملف
    /// </summary>
    private DependencyType DetermineDependencyType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        return extension switch
        {
            ".w3d" => DependencyType.Model3D,
            ".dds" => DependencyType.Texture,
            ".wav" or ".mp3" or ".wma" => DependencyType.Audio,
            ".ini" => DependencyType.ObjectINI,
            ".tga" or ".bmp" => DependencyType.Texture,
            _ => DependencyType.Custom
        };
    }

    /// <summary>
    /// تنظيف مسار الملف من الأحرف الخاصة
    /// </summary>
    private string CleanFilePath(string filePath)
    {
        // إزالة علامات الاقتباس
        filePath = filePath.Trim('"', '\'');
        // السماح فقط بأحرف آمنة
        filePath = Regex.Replace(filePath, @"[\\\/]+", "/");
        // إزالة المسافات الزائدة
        return filePath.Trim();
    }

    /// <summary>
    /// التحقق من وجود جميع التبعيات
    /// </summary>
    public async Task<bool> ValidateDependenciesAsync(UnitDependencyGraph graph, string sourceModPath)
    {
        return await Task.Run(() =>
        {
            if (graph.AllNodes.Count == 0)
                return false;

            foreach (var node in graph.AllNodes)
            {
                if (node.FullPath == null || !File.Exists(node.FullPath))
                {
                    node.Status = AssetStatus.Missing;
                }
                else
                {
                    node.Status = AssetStatus.Found;
                    var fileInfo = new FileInfo(node.FullPath);
                    node.SizeInBytes = fileInfo.Length;
                    node.LastModified = fileInfo.LastWriteTimeUtc;
                }
            }

            return graph.AllNodes.All(n => n.Status == AssetStatus.Found);
        });
    }

    /// <summary>
    /// الحصول على قائمة الملفات التي يجب نقلها
    /// </summary>
    public async Task<List<DependencyNode>> GetFilesToTransferAsync(UnitDependencyGraph graph)
    {
        return await Task.Run(() =>
        {
            var filesToTransfer = graph.AllNodes
                .Where(n => n.Status == AssetStatus.Found && n.FullPath != null)
                .OrderBy(n => n.Depth) // نقل الملفات الأب أولاً
                .ToList();

            return filesToTransfer;
        });
    }
}
