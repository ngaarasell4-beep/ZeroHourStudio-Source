using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.Services; // For SageDefinitionIndex

namespace ZeroHourStudio.Infrastructure.Diagnostics
{
    public class DiagnosticAuditService
    {
        private readonly IBigFileReader _bigFileReader;
        private readonly SageDefinitionIndex _definitionIndex;

        public DiagnosticAuditService(
            IBigFileReader bigFileReader,
            SageDefinitionIndex definitionIndex)
        {
            _bigFileReader = bigFileReader ?? throw new ArgumentNullException(nameof(bigFileReader));
            _definitionIndex = definitionIndex ?? throw new ArgumentNullException(nameof(definitionIndex));
        }

        public async Task<DiagnosticReport> AuditUnitAsync(string unitName, string modPath)
        {
            var report = new DiagnosticReport
            {
                UnitName = unitName,
                ScanTime = DateTime.Now
            };

            // 1. Get Definition
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

            // 2. Check Empty Keys (Manual Parse of RawContent)
            CheckEmptyKeys(report, def);

            // 3. Extract and Verify References
            var references = SageDefinitionIndex.ExtractReferences(def);
            foreach (var r in references)
            {
                await VerifyReferenceAsync(report, r, def.SourceFile, modPath);
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

        private async Task VerifyReferenceAsync(DiagnosticReport report, SageReference refer, string sourceFile, string modPath)
        {
            // Objects/Defintions
            if (IsDefinitionReference(refer.Type))
            {
                if (!_definitionIndex.Contains(refer.Name))
                {
                    // Ignore common non-objects
                    if (IsIgnoredValue(refer.Name)) return;

                    report.Issues.Add(new AuditIssue
                    {
                        Severity = AuditSeverity.Error,
                        Category = AuditCategory.BrokenLink,
                        Message = $"Referenced object '{refer.Name}' ({refer.Type}) not found.",
                        Location = $"{sourceFile}",
                        Key = refer.SourceKey,
                        Value = refer.Name
                    });
                }
            }
            // Files
            else if (IsFileReference(refer.Type))
            {
                var fileName = ResolveFileName(refer.Name, refer.Type);
                bool exists = await _bigFileReader.FileExistsAsync(modPath, fileName);
                
                if (!exists)
                {
                    // Try alternatives for textures (tga vs dds)
                    if (fileName.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                    {
                        if (await _bigFileReader.FileExistsAsync(modPath, fileName.Replace(".dds", ".tga"))) return;
                    }

                    report.Issues.Add(new AuditIssue
                    {
                        Severity = AuditSeverity.Error,
                        Category = AuditCategory.MissingFile,
                        Message = $"File '{fileName}' not found.",
                        Location = $"{sourceFile}",
                        Key = refer.SourceKey,
                        Value = refer.Name
                    });
                }
            }
        }

        private bool IsDefinitionReference(SageRefType type)
        {
            return type == SageRefType.Object ||
                   type == SageRefType.Weapon || 
                   type == SageRefType.Locomotor ||
                   type == SageRefType.Armor ||
                   type == SageRefType.CommandSet ||
                   type == SageRefType.FXList ||
                   type == SageRefType.OCL ||
                   type == SageRefType.ParticleSystem ||
                   type == SageRefType.Upgrade ||
                   type == SageRefType.Science;
        }

        private bool IsFileReference(SageRefType type)
        {
             return type == SageRefType.Model ||
                    type == SageRefType.Texture ||
                    type == SageRefType.Audio ||
                    type == SageRefType.Image;
        }

        private bool IsIgnoredValue(string value)
        {
            return value.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("Null", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("0", StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveFileName(string name, SageRefType type)
        {
            if (name.Contains(".")) return name;

            return type switch
            {
                SageRefType.Model => name + ".w3d",
                SageRefType.Texture => name + ".dds",
                SageRefType.Audio => name + ".wav", // or .mp3, but mostly wav/mp3 checks need more logic
                SageRefType.Image => name + ".tga", // Portraits are usually tga/dds
                _ => name
            };
        }
    }
}
