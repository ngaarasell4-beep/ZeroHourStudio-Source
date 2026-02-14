using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Infrastructure.Transfer;

/// <summary>
/// فحص صحة سريع بعد النقل: التحقق من أن الملفات المنقولة موجودة في المود الهدف.
/// </summary>
public static class PostTransferHealthCheck
{
    /// <summary>
    /// يتحقق من وجود الملفات المتوقعة في الهدف (حسب الرسم والمسار النسبي).
    /// </summary>
    /// <returns>قائمة أسماء الملفات المفقودة في الهدف (فارغة = كل شيء موجود)</returns>
    public static List<string> VerifyTransferredFilesExist(
        UnitDependencyGraph graph,
        string sourceModPath,
        string targetModPath)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(targetModPath) || !Directory.Exists(targetModPath))
            return missing;

        foreach (var node in graph.AllNodes.Where(n => n.Status == AssetStatus.Found && n.FullPath != null))
        {
            var fullPath = node.FullPath!;
            string expectedTargetPath;

            if (fullPath.Contains("::", StringComparison.Ordinal))
            {
                var entryPath = fullPath.Split(new[] { "::" }, 2, StringSplitOptions.None)[1]
                    .Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                expectedTargetPath = Path.Combine(targetModPath, entryPath);
            }
            else if (fullPath.StartsWith(sourceModPath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(sourceModPath, fullPath);
                expectedTargetPath = Path.Combine(targetModPath, relative);
            }
            else
            {
                var name = node.Name;
                if (name.Contains(Path.DirectorySeparatorChar) || name.Contains('/'))
                    expectedTargetPath = Path.Combine(targetModPath, name);
                else
                    expectedTargetPath = Path.Combine(targetModPath, "Data", name);
            }

            if (!File.Exists(expectedTargetPath) && !Directory.Exists(expectedTargetPath))
                missing.Add(node.Name);
        }

        return missing;
    }
}
