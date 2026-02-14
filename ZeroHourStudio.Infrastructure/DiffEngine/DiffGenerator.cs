using System.Text;

namespace ZeroHourStudio.Infrastructure.DiffEngine;

/// <summary>
/// نوع سطر الـ Diff
/// </summary>
public enum DiffLineType
{
    Unchanged,
    Added,
    Removed,
    Modified,
    Header
}

/// <summary>
/// سطر واحد في نتيجة الـ Diff
/// </summary>
public class DiffLine
{
    public int LineNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public DiffLineType Type { get; set; }
    public string? ParallelContent { get; set; }
}

/// <summary>
/// إحصائيات الـ Diff
/// </summary>
public class DiffStatistics
{
    public int TotalLines { get; set; }
    public int AddedLines { get; set; }
    public int RemovedLines { get; set; }
    public int ModifiedLines { get; set; }
    public int UnchangedLines { get; set; }
    public double ChangePercentage => TotalLines == 0 ? 0 : Math.Round((double)(AddedLines + RemovedLines + ModifiedLines) / TotalLines * 100, 1);
}

/// <summary>
/// ملف Diff واحد
/// </summary>
public class FileDiff
{
    public string FileName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public List<DiffLine> Lines { get; set; } = new();
    public DiffStatistics Statistics { get; set; } = new();
}

/// <summary>
/// محرك توليد الـ Diff للمقارنة بين ملفات INI
/// </summary>
public class DiffGenerator
{
    /// <summary>
    /// توليد قائمة Diff لجميع ملفات المود
    /// </summary>
    public async Task<List<FileDiff>> GenerateModDiff(string sourceModPath, string targetModPath)
    {
        var diffs = new List<FileDiff>();
        try
        {
            var sourceIniDir = Path.Combine(sourceModPath, "Data", "INI");
            var targetIniDir = Path.Combine(targetModPath, "Data", "INI");

            if (!Directory.Exists(sourceIniDir) || !Directory.Exists(targetIniDir))
                return diffs;

            var sourceFiles = Directory.GetFiles(sourceIniDir, "*.ini", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(sourceIniDir, f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var targetFiles = Directory.GetFiles(targetIniDir, "*.ini", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(targetIniDir, f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Files in both (potential modifications)
            foreach (var file in sourceFiles.Intersect(targetFiles, StringComparer.OrdinalIgnoreCase))
            {
                var sourcePath = Path.Combine(sourceIniDir, file);
                var targetPath = Path.Combine(targetIniDir, file);
                var diff = await GenerateDiff(sourcePath, targetPath, file);
                if (diff.Lines.Any(l => l.Type != DiffLineType.Unchanged))
                    diffs.Add(diff);
            }

            // Files only in source (new)
            foreach (var file in sourceFiles.Except(targetFiles, StringComparer.OrdinalIgnoreCase))
            {
                var sourcePath = Path.Combine(sourceIniDir, file);
                var sourceLines = await File.ReadAllLinesAsync(sourcePath);
                var diff = new FileDiff
                {
                    FileName = file,
                    SourcePath = sourcePath,
                    TargetPath = "(new)"
                };
                int lineNum = 0;
                foreach (var line in sourceLines)
                {
                    diff.Lines.Add(new DiffLine
                    {
                        LineNumber = ++lineNum,
                        Content = line,
                        Type = DiffLineType.Added
                    });
                }
                diff.Statistics = ComputeStatistics(diff.Lines);
                diffs.Add(diff);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiffGenerator] ERROR: {ex.Message}");
        }

        return diffs;
    }

    /// <summary>
    /// توليد Diff بين ملفين
    /// </summary>
    public async Task<FileDiff> GenerateDiff(string sourcePath, string targetPath, string? label = null)
    {
        var diff = new FileDiff
        {
            FileName = label ?? Path.GetFileName(sourcePath),
            SourcePath = sourcePath,
            TargetPath = targetPath
        };

        try
        {
            var sourceLines = File.Exists(sourcePath) ? await File.ReadAllLinesAsync(sourcePath) : Array.Empty<string>();
            var targetLines = File.Exists(targetPath) ? await File.ReadAllLinesAsync(targetPath) : Array.Empty<string>();

            var sourceSet = sourceLines.ToHashSet();
            var targetSet = targetLines.ToHashSet();

            int maxLen = Math.Max(sourceLines.Length, targetLines.Length);
            for (int i = 0; i < maxLen; i++)
            {
                var srcLine = i < sourceLines.Length ? sourceLines[i] : null;
                var tgtLine = i < targetLines.Length ? targetLines[i] : null;

                if (srcLine == tgtLine)
                {
                    diff.Lines.Add(new DiffLine
                    {
                        LineNumber = i + 1,
                        Content = srcLine ?? "",
                        Type = DiffLineType.Unchanged,
                        ParallelContent = tgtLine
                    });
                }
                else if (srcLine != null && tgtLine != null)
                {
                    diff.Lines.Add(new DiffLine
                    {
                        LineNumber = i + 1,
                        Content = srcLine,
                        Type = DiffLineType.Modified,
                        ParallelContent = tgtLine
                    });
                }
                else if (srcLine != null)
                {
                    diff.Lines.Add(new DiffLine
                    {
                        LineNumber = i + 1,
                        Content = srcLine,
                        Type = DiffLineType.Removed
                    });
                }
                else if (tgtLine != null)
                {
                    diff.Lines.Add(new DiffLine
                    {
                        LineNumber = i + 1,
                        Content = tgtLine,
                        Type = DiffLineType.Added
                    });
                }
            }

            diff.Statistics = ComputeStatistics(diff.Lines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiffGenerator] GenerateDiff ERROR: {ex.Message}");
        }

        return diff;
    }

    /// <summary>
    /// تصدير Diff كـ HTML
    /// </summary>
    public string ExportAsHtml(FileDiff diff)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><style>");
        sb.AppendLine("body { font-family: 'Consolas', monospace; background: #1E1E2E; color: #CCC; padding: 20px; }");
        sb.AppendLine(".diff-header { color: #00CC66; font-size: 18px; margin-bottom: 10px; }");
        sb.AppendLine(".line { display: flex; padding: 2px 8px; }");
        sb.AppendLine(".line-num { width: 50px; color: #666; text-align: right; padding-right: 10px; }");
        sb.AppendLine(".added { background: #1A3A2A; color: #00DD77; }");
        sb.AppendLine(".removed { background: #3A1A1A; color: #FF6666; }");
        sb.AppendLine(".modified { background: #3A3A1A; color: #FFD700; }");
        sb.AppendLine(".stats { color: #87CEEB; margin: 10px 0; }");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<div class='diff-header'>📄 {diff.FileName}</div>");
        sb.AppendLine($"<div class='stats'>+{diff.Statistics.AddedLines} -{diff.Statistics.RemovedLines} ~{diff.Statistics.ModifiedLines} ({diff.Statistics.ChangePercentage}% changed)</div>");

        foreach (var line in diff.Lines)
        {
            var cssClass = line.Type switch
            {
                DiffLineType.Added => "added",
                DiffLineType.Removed => "removed",
                DiffLineType.Modified => "modified",
                _ => ""
            };
            var prefix = line.Type switch
            {
                DiffLineType.Added => "+",
                DiffLineType.Removed => "-",
                DiffLineType.Modified => "~",
                _ => " "
            };
            sb.AppendLine($"<div class='line {cssClass}'><span class='line-num'>{line.LineNumber}</span><span>{prefix} {System.Net.WebUtility.HtmlEncode(line.Content)}</span></div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static DiffStatistics ComputeStatistics(List<DiffLine> lines)
    {
        return new DiffStatistics
        {
            TotalLines = lines.Count,
            AddedLines = lines.Count(l => l.Type == DiffLineType.Added),
            RemovedLines = lines.Count(l => l.Type == DiffLineType.Removed),
            ModifiedLines = lines.Count(l => l.Type == DiffLineType.Modified),
            UnchangedLines = lines.Count(l => l.Type == DiffLineType.Unchanged)
        };
    }
}
