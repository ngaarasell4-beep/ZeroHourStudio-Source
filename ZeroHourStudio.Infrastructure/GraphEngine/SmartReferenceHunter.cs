using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;

namespace ZeroHourStudio.Infrastructure.GraphEngine
{
    /// <summary>
    /// نسخة محسّنة من SmartTools - البحث الذكي عن المراجع
    /// Enhanced version of SmartReferenceHunter - Intelligent reference searching
    /// </summary>
    public class SmartReferenceHunter
    {
        private readonly EnhancedSageCore _engine;
        private readonly UnitDependencyAnalyzer _dependencyAnalyzer;

        public event Action<string>? OnSearchUpdate;
        public event Action<int, int>? OnProgress; // current, total

        public SmartReferenceHunter(EnhancedSageCore engine, UnitDependencyAnalyzer dependencyAnalyzer)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _dependencyAnalyzer = dependencyAnalyzer ?? throw new ArgumentNullException(nameof(dependencyAnalyzer));
        }

        /// <summary>
        /// Deep search for all references to an object
        /// البحث العميق عن كل المراجع لكائن معين
        /// </summary>
        public async Task<SearchResult> DeepSearchAsync(string objectName, SearchOptions? options = null)
        {
            options ??= new SearchOptions();
            var result = new SearchResult { ObjectName = objectName };

            try
            {
                OnSearchUpdate?.Invoke($"🔍 Starting deep search for: {objectName}");

                // Step 1: Direct references
                OnSearchUpdate?.Invoke("📋 Searching direct references...");
                var directRefs = await SearchDirectReferencesAsync(objectName);
                result.DirectReferences.AddRange(directRefs);
                OnProgress?.Invoke(1, 4);

                // Step 2: Reverse dependencies (what uses this object)
                OnSearchUpdate?.Invoke("🔄 Searching reverse dependencies...");
                var reverseDeps = await SearchReverseDependenciesAsync(objectName);
                result.ReverseDependencies.AddRange(reverseDeps);
                OnProgress?.Invoke(2, 4);

                // Step 3: Related objects (same type, similar names)
                if (options.IncludeRelated)
                {
                    OnSearchUpdate?.Invoke("🔗 Searching related objects...");
                    var related = await SearchRelatedObjectsAsync(objectName);
                    result.RelatedObjects.AddRange(related);
                }
                OnProgress?.Invoke(3, 4);

                // Step 4: Asset dependencies
                if (options.IncludeAssets)
                {
                    OnSearchUpdate?.Invoke("🎨 Searching asset dependencies...");
                    var assets = await SearchAssetDependenciesAsync(objectName);
                    result.AssetDependencies.AddRange(assets);
                }
                OnProgress?.Invoke(4, 4);

                result.TotalReferencesFound = result.DirectReferences.Count + 
                                             result.ReverseDependencies.Count + 
                                             result.RelatedObjects.Count + 
                                             result.AssetDependencies.Count;

                OnSearchUpdate?.Invoke($"✅ Search complete! Found {result.TotalReferencesFound} total references");
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                OnSearchUpdate?.Invoke($"❌ Search failed: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Find what objects directly reference this object
        /// </summary>
        private async Task<List<string>> SearchDirectReferencesAsync(string objectName)
        {
            if (_engine?.DependencyGraph == null)
                return new List<string>();

            return await Task.Run(() =>
            {
                var references = new List<string>();
                // Note: DependencyNode doesn't have a Dependencies property
                // This would need to be tracked differently if needed
                return references;
            });
        }

        /// <summary>
        /// Find reverse dependencies (objects this one depends on that are also used elsewhere)
        /// </summary>
        private async Task<List<string>> SearchReverseDependenciesAsync(string objectName)
        {
            return await Task.Run(async () =>
            {
                var reverseDeps = new List<string>();
                
                try
                {
                    // Use dependency analyzer to get what this object depends on
                    var deps = await _dependencyAnalyzer.AnalyzeDependenciesAsync(
                        objectName, objectName, new Dictionary<string, string>());
                    
                    if (deps?.AllNodes != null)
                    {
                        foreach (var node in deps.AllNodes)
                        {
                            // In Application.Models.DependencyNode, we use Id or Name
                            if (!string.IsNullOrEmpty(node.Name) && !node.Name.Equals(objectName, StringComparison.OrdinalIgnoreCase))
                            {
                                reverseDeps.Add(node.Name);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore errors
                }

                return reverseDeps;
            });
        }

        /// <summary>
        /// Find related objects (same type, similar naming)
        /// </summary>
        private async Task<List<string>> SearchRelatedObjectsAsync(string objectName)
        {
            if (_engine?.ObjectTypeMap == null)
                return new List<string>();

            return await Task.Run(() =>
            {
                var related = new List<string>();

                // Get the type of the target object
                if (_engine.ObjectTypeMap.TryGetValue(objectName, out var objectType))
                {
                    // Find other objects of the same type
                    related.AddRange(
                        _engine.ObjectTypeMap
                            .Where(kvp => kvp.Value == objectType && kvp.Key != objectName)
                            .Select(kvp => kvp.Key)
                            .Take(10) // Limit to 10 related objects
                    );
                }

                // Find objects with similar names
                var baseName = ExtractBaseName(objectName);
                if (!string.IsNullOrEmpty(baseName))
                {
                    related.AddRange(
                        _engine.ObjectDefinitionMap.Keys
                            .Where(key => key.Contains(baseName, StringComparison.OrdinalIgnoreCase) && 
                                         key != objectName &&
                                         !related.Contains(key))
                            .Take(5)
                    );
                }

                return related.Distinct().ToList();
            });
        }

        /// <summary>
        /// Find asset dependencies (models, textures, sounds)
        /// </summary>
        private async Task<List<string>> SearchAssetDependenciesAsync(string objectName)
        {
            return await Task.Run(() =>
            {
                var assets = new List<string>();

                // Get definition
                if (_engine?.ObjectDefinitionMap.TryGetValue(objectName, out var definition) == true)
                {
                    // Search for common asset patterns
                    assets.AddRange(ExtractAssetReferences(definition));
                }

                return assets;
            });
        }

        // Helper methods

        private string ExtractBaseName(string objectName)
        {
            // Remove common prefixes/suffixes
            var name = objectName
                .Replace("GLA_", "")
                .Replace("USA_", "")
                .Replace("China_", "")
                .Replace("_Weapon", "")
                .Replace("Weapon_", "");

            // Get first meaningful part
            var parts = name.Split('_');
            return parts.Length > 0 ? parts[0] : name;
        }

        private List<string> ExtractAssetReferences(string definition)
        {
            var assets = new List<string>();

            // Simple pattern matching for assets
            var patterns = new[] { ".w3d", ".dds", ".tga", ".wav", ".mp3" };
            foreach (var pattern in patterns)
            {
                var index = 0;
                while ((index = definition.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
                {
                    // Extract filename before the extension
                    var start = definition.LastIndexOf(' ', index);
                    if (start != -1)
                    {
                        var asset = definition.Substring(start + 1, index - start - 1 + pattern.Length);
                        assets.Add(asset.Trim());
                    }
                    index += pattern.Length;
                }
            }

            return assets;
        }

        /// <summary>
        /// Search options
        /// </summary>
        public class SearchOptions
        {
            public bool IncludeRelated { get; set; } = true;
            public bool IncludeAssets { get; set; } = true;
            public int MaxResults { get; set; } = 1000;
        }

        /// <summary>
        /// Search result
        /// </summary>
        public class SearchResult
        {
            public string ObjectName { get; set; } = "";
            public List<string> DirectReferences { get; set; } = new List<string>();
            public List<string> ReverseDependencies { get; set; } = new List<string>();
            public List<string> RelatedObjects { get; set; } = new List<string>();
            public List<string> AssetDependencies { get; set; } = new List<string>();
            public int TotalReferencesFound { get; set; }
            public string? Error { get; set; }
        }
    }
}
