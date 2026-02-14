using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.DependencyResolution;
using ZeroHourStudio.Infrastructure.Services; // For SageDefinitionIndex

namespace ZeroHourStudio.Infrastructure.Diagnostics
{
    public class DiagnosticAuditService
    {
        private readonly IDependencyResolver _dependencyResolver;
        private readonly SageDefinitionIndex _definitionIndex;

        public DiagnosticAuditService(
            IDependencyResolver dependencyResolver,
            SageDefinitionIndex definitionIndex)
        {
            _dependencyResolver = dependencyResolver ?? throw new ArgumentNullException(nameof(dependencyResolver));
            _definitionIndex = definitionIndex ?? throw new ArgumentNullException(nameof(definitionIndex));
        }

        public async Task<DiagnosticReport> AuditUnitAsync(string unitName, string modPath)
        {
            var report = new DiagnosticReport
            {
                UnitName = unitName,
                ScanTime = DateTime.Now
            };

            // 1. Quick sanity check: is the unit even in the index?
            var def = _definitionIndex.Lookup(unitName);
            if (def == null)
            {
                report.Issues.Add(new AuditIssue
                {
                    Severity = AuditSeverity.Critical,
                    Category = AuditCategory.BrokenLink,
                    Message = $"Unit '{unitName}' definition not found in index.",
                    Location = "Index"
                });
                return report;
            }

            // 2. Check empty keys (supplementary - catches empty key=value pairs the resolver doesn't check)
            CheckEmptyKeys(report, def);

            // 3. Use the Smart Engine to resolve the full dependency graph
            //    This handles InheritFrom, OCL chains, deep recursion, multi-encoding
            if (_dependencyResolver is SmartDependencyResolver smartResolver)
                smartResolver.SageIndex = _definitionIndex;

            UnitDependencyGraph graph;
            try
            {
                graph = await _dependencyResolver.ResolveDependenciesAsync(unitName, modPath);
                await _dependencyResolver.ValidateDependenciesAsync(graph, modPath);
            }
            catch (Exception ex)
            {
                report.Issues.Add(new AuditIssue
                {
                    Severity = AuditSeverity.Warning,
                    Category = AuditCategory.LogicError,
                    Message = $"Dependency resolution partially failed: {ex.Message}",
                    Location = def.SourceFile
                });
                return report;
            }

            // 4. Convert graph node statuses into audit issues
            foreach (var node in graph.AllNodes)
            {
                if (node.Status == AssetStatus.Missing)
                {
                    var category = IsFileType(node.Type) ? AuditCategory.MissingFile : AuditCategory.BrokenLink;
                    var severity = IsCriticalType(node.Type) ? AuditSeverity.Error : AuditSeverity.Warning;

                    report.Issues.Add(new AuditIssue
                    {
                        Severity = severity,
                        Category = category,
                        Message = $"Missing {node.Type}: '{node.Name}'",
                        Location = def.SourceFile,
                        Key = node.Type.ToString(),
                        Value = node.Name
                    });
                }
                else if (node.Status == AssetStatus.Invalid)
                {
                    report.Issues.Add(new AuditIssue
                    {
                        Severity = AuditSeverity.Warning,
                        Category = AuditCategory.LogicError,
                        Message = $"Invalid {node.Type}: '{node.Name}'",
                        Location = def.SourceFile,
                        Key = node.Type.ToString(),
                        Value = node.Name
                    });
                }
            }

            // 5. Add summary info
            if (graph.AllNodes.Count > 0)
            {
                report.Issues.Add(new AuditIssue
                {
                    Severity = AuditSeverity.Info,
                    Category = AuditCategory.BrokenLink,
                    Message = $"Scanned {graph.AllNodes.Count} dependencies (depth {graph.MaxDepth}): {graph.FoundCount} found, {graph.MissingCount} missing — {graph.GetCompletionPercentage():F0}% complete",
                    Location = "SmartDependencyResolver"
                });
            }

            return report;
        }

        private void CheckEmptyKeys(DiagnosticReport report, SageDefinition def)
        {
            var lines = def.RawContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(";") || trimmed.StartsWith("//")) continue;

                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                {
                    var key = trimmed.Substring(0, eqIdx).Trim();
                    var value = trimmed.Substring(eqIdx + 1).Trim();

                    // Remove comments from value
                    var cIdx = value.IndexOf(';');
                    if (cIdx >= 0) value = value.Substring(0, cIdx).Trim();
                    cIdx = value.IndexOf("//", StringComparison.Ordinal);
                    if (cIdx >= 0) value = value.Substring(0, cIdx).Trim();

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        report.Issues.Add(new AuditIssue
                        {
                            Severity = AuditSeverity.Warning,
                            Category = AuditCategory.EmptyKey,
                            Message = $"Key '{key}' is empty.",
                            Location = $"{def.SourceFile} [{def.BlockType}:{def.Name}]",
                            Key = key
                        });
                    }
                }
            }
        }

        /// <summary>
        /// File-based dependency types (assets on disk)
        /// </summary>
        private static bool IsFileType(DependencyType type)
        {
            return type == DependencyType.Model3D ||
                   type == DependencyType.Texture ||
                   type == DependencyType.Audio;
        }

        /// <summary>
        /// Types that are critical for the unit to function
        /// </summary>
        private static bool IsCriticalType(DependencyType type)
        {
            return type == DependencyType.ObjectINI ||
                   type == DependencyType.Weapon ||
                   type == DependencyType.Model3D ||
                   type == DependencyType.Armor;
        }
    }
}
