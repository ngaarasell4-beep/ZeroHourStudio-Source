using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.Logging;

namespace ZeroHourStudio.Infrastructure.ConflictResolution;

/// <summary>
/// خدمة كشف التعارضات - تقارن تعريفات المصدر مع الهدف
/// </summary>
public class ConflictDetectionService : IConflictResolutionService
{
    public async Task<ConflictReport> DetectConflictsAsync(
        UnitDependencyGraph sourceGraph,
        string targetModPath)
    {
        var report = new ConflictReport
        {
            UnitName = sourceGraph.UnitName ?? sourceGraph.UnitId ?? "Unknown"
        };

        if (string.IsNullOrWhiteSpace(targetModPath) || !Directory.Exists(targetModPath))
            return report;

        await Task.Run(() =>
        {
            // بناء فهرس الملفات الموجودة في الهدف
            var targetFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(targetModPath, "*.*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(targetModPath, file);
                targetFiles.Add(relative.Replace('\\', '/'));
            }

            // بناء فهرس تعريفات INI في الهدف
            var targetDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var iniFiles = Directory.EnumerateFiles(targetModPath, "*.ini", SearchOption.AllDirectories);
            foreach (var iniFile in iniFiles)
            {
                try
                {
                    var content = File.ReadAllText(iniFile);
                    var lines = content.Split('\n');
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        // كشف رؤوس البلوكات: Object, Weapon, FXList, OCL, etc.
                        if (trimmed.Length > 0 && !trimmed.StartsWith(";") && !trimmed.StartsWith("//"))
                        {
                            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && IsDefinitionHeader(parts[0]))
                            {
                                targetDefinitions.Add(parts[1]);
                            }
                        }
                    }
                }
                catch (Exception ex) { BlackBoxRecorder.Record("CONFLICT_DETECTION", "INI_READ_ERROR", $"File={iniFile}, Error={ex.Message}"); }
            }

            // فحص كل عقدة تبعية
            foreach (var node in sourceGraph.AllNodes)
            {
                // فحص تعارض الأسماء
                if (targetDefinitions.Contains(node.Name))
                {
                    report.Conflicts.Add(new ConflictEntry
                    {
                        DefinitionName = node.Name,
                        DefinitionType = node.Type.ToString(),
                        Kind = ConflictKind.Duplicate,
                        SuggestedRename = GenerateSuggestedName(node.Name)
                    });
                }

                // فحص تعارض الملفات
                if (!string.IsNullOrWhiteSpace(node.FullPath))
                {
                    var relativePath = node.FullPath.Replace('\\', '/');
                    if (targetFiles.Contains(relativePath))
                    {
                        report.Conflicts.Add(new ConflictEntry
                        {
                            DefinitionName = node.Name,
                            DefinitionType = node.Type.ToString(),
                            SourceFile = node.FullPath,
                            TargetFile = Path.Combine(targetModPath, relativePath),
                            Kind = ConflictKind.FileOverwrite,
                            SuggestedRename = ""
                        });
                    }
                }
            }
        });

        return report;
    }

    public Task<Dictionary<string, string>> GenerateRenameMapAsync(ConflictReport report)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var conflict in report.Conflicts.Where(c => c.Kind == ConflictKind.Duplicate || c.Kind == ConflictKind.NameCollision))
        {
            if (!string.IsNullOrWhiteSpace(conflict.SuggestedRename) && !map.ContainsKey(conflict.DefinitionName))
            {
                map[conflict.DefinitionName] = conflict.SuggestedRename;
            }
        }

        return Task.FromResult(map);
    }

    public Task<string> ApplyRenamesAsync(string iniContent, Dictionary<string, string> renameMap)
    {
        var result = iniContent;
        foreach (var kvp in renameMap.OrderByDescending(k => k.Key.Length))
        {
            result = result.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
        }
        return Task.FromResult(result);
    }

    private static bool IsDefinitionHeader(string keyword)
    {
        return keyword is "Object" or "Weapon" or "FXList" or "ObjectCreationList"
            or "ParticleSystem" or "Armor" or "LocomotorSet" or "Locomotor"
            or "ModuleTag" or "CommandButton" or "CommandSet"
            or "SpecialPower" or "Upgrade" or "Science"
            or "PlayerTemplate" or "MultisoundSoundBankNugget";
    }

    private static string GenerateSuggestedName(string originalName)
    {
        // إضافة بادئة ZH_ للأسماء المتعارضة
        if (originalName.StartsWith("ZH_", StringComparison.OrdinalIgnoreCase))
            return originalName + "_v2";
        return "ZH_" + originalName;
    }
}
