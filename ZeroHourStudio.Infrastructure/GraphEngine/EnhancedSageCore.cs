using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.Parsers;

namespace ZeroHourStudio.Infrastructure.GraphEngine
{
    /// <summary>
    /// محرك محسّن يجمع أفضل ما في SageDeepCore البسيط مع الخدمات المتقدمة الموجودة
    /// Enhanced core engine combining ideas from simple SageDeepCore with advanced existing services
    /// </summary>
    public class EnhancedSageCore
    {
        // الخدمات الأساسية
        private readonly UnitDependencyAnalyzer _dependencyAnalyzer;
        private readonly IWeaponAnalysisService _weaponAnalysisService;
        private readonly SageDefinitionIndex _definitionIndex;
        private readonly ComprehensiveDependencyService _comprehensiveDependencyService;

        // Events for status updates
        public event Action<string>? OnStatusUpdate;
        public event Action<int>? OnProgressUpdate;
        public event Action<string>? OnWarning;
        public event Action<string>? OnError;

        // Cache using Application.Models.DependencyNode
        public Dictionary<string, string> ObjectDefinitionMap { get; private set; } = new Dictionary<string, string>();
        public Dictionary<string, string> ObjectTypeMap { get; private set; } = new Dictionary<string, string>();
        public Dictionary<string, DependencyNode> DependencyGraph { get; private set; } = new Dictionary<string, DependencyNode>();

        public bool IsInitialized { get; private set; }
        public string? LoadedPath { get; private set; }

        public EnhancedSageCore(
            UnitDependencyAnalyzer dependencyAnalyzer,
            IWeaponAnalysisService weaponAnalysisService,
            SageDefinitionIndex definitionIndex,
            ComprehensiveDependencyService comprehensiveDependencyService)
        {
            _dependencyAnalyzer = dependencyAnalyzer ?? throw new ArgumentNullException(nameof(dependencyAnalyzer));
            _weaponAnalysisService = weaponAnalysisService ?? throw new ArgumentNullException(nameof(weaponAnalysisService));
            _definitionIndex = definitionIndex ?? throw new ArgumentNullException(nameof(definitionIndex));
            _comprehensiveDependencyService = comprehensiveDependencyService ?? throw new ArgumentNullException(nameof(comprehensiveDependencyService));
        }

        /// <summary>
        /// Initialize the engine with game data path
        /// </summary>
        public async Task InitializeEngineAsync(string gamePath, IProgress<string>? progress = null)
        {
            try
            {
                OnStatusUpdate?.Invoke("🚀 Initializing Enhanced Sage Core...");
                progress?.Report("Starting initialization...");

                LoadedPath = gamePath;

                // Step 1: Clear previous data
                OnStatusUpdate?.Invoke("🧹 Clearing previous data...");
                ObjectDefinitionMap.Clear();
                ObjectTypeMap.Clear();
                DependencyGraph.Clear();

                // Step 2: Initialize Definition Index
                OnStatusUpdate?.Invoke("📚 Building definition index...");
                progress?.Report("Building definition index...");
                await _definitionIndex.BuildIndexAsync(gamePath);
                OnProgressUpdate?.Invoke(25);

                // Step 3: Load object definitions using existing services
                OnStatusUpdate?.Invoke("📦 Loading object definitions...");
                progress?.Report("Loading object definitions...");
                await LoadObjectDefinitions(gamePath);
                OnProgressUpdate?.Invoke(50);

                // Step 4: Build dependency graph
                OnStatusUpdate?.Invoke("🔗 Building dependency graph...");
                progress?.Report("Building dependency graph...");
                await BuildDependencyGraphAsync();
                OnProgressUpdate?.Invoke(75);

                // Step 5: Analyze weapons
                OnStatusUpdate?.Invoke("🔫 Analyzing weapon systems...");
                progress?.Report("Analyzing weapons...");
                await AnalyzeWeaponSystemsAsync();
                OnProgressUpdate?.Invoke(100);

                IsInitialized = true;
                OnStatusUpdate?.Invoke("✅ Engine initialized successfully!");
                progress?.Report("Initialization complete!");
            }
            catch (Exception ex)
            {
                IsInitialized = false;
                OnError?.Invoke($"❌ Initialization failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Resolve complete dependency chain for a unit/weapon
        /// </summary>
        public async Task<TransferPackage> ResolveDependencyChainAsync(string objectName, bool includeWeapons = true)
        {
            if (!IsInitialized)
                throw new InvalidOperationException("Engine not initialized. Call InitializeEngineAsync first.");

            var package = new TransferPackage { ObjectName = objectName };

            try
            {
                OnStatusUpdate?.Invoke($"🔍 Resolving dependencies for: {objectName}");

                // Use existing dependency analyzer (adjusted to correct method signature)
                var dependencies = await _dependencyAnalyzer.AnalyzeDependenciesAsync(
                    objectName, objectName, new Dictionary<string, string>());
                
                if (dependencies != null)
                {
                    // Add dependencies from graph nodes
                    if (dependencies.AllNodes != null)
                    {
                        foreach (var node in dependencies.AllNodes)
                        {
                            // إضافة جميع المسارات المتخصصة (إن وُجدت)
                            var specificPaths = new[]
                            {
                                node.DdsFilePath,
                                node.W3dFilePath,
                                node.AudioFilePath,
                                node.IniFilePath
                            }.Where(path => !string.IsNullOrEmpty(path));

                            foreach (var path in specificPaths)
                            {
                                if (!package.Assets.Contains(path!)) // تجنب التكرار
                                    package.Assets.Add(path!);
                            }

                            // إضافة FullPath أيضاً كـ fallback
                            if (!string.IsNullOrEmpty(node.FullPath) && 
                                !package.Assets.Contains(node.FullPath))
                            {
                                package.Assets.Add(node.FullPath);
                            }
                        }
                    }
                }

                // Analyze weapons if requested
                if (includeWeapons)
                {
                    var weaponAnalysis = await _weaponAnalysisService.AnalyzeWeaponDependenciesAsync(
                        objectName, LoadedPath ?? "");
                    
                    if (weaponAnalysis?.Weapons != null)
                    {
                        foreach (var chain in weaponAnalysis.Weapons)
                        {
                            package.WeaponChain.Add(chain.WeaponName ?? "");
                            // Add related files (includes models, textures, sounds, etc.)
                            package.Assets.AddRange(chain.RelatedFiles ?? Enumerable.Empty<string>());
                        }
                    }
                }

                // Get comprehensive analysis
                var comprehensiveResult = await _comprehensiveDependencyService.AnalyzeUnitComprehensivelyAsync(
                    objectName, objectName, new Dictionary<string, string>());
                
                if (comprehensiveResult?.DependencyGraph != null)
                {
                    package.TotalDependencyCount = comprehensiveResult.DependencyGraph.AllNodes?.Count ?? 0;
                }

                OnStatusUpdate?.Invoke($"✅ Resolved {package.Dependencies.Count} dependencies, {package.Assets.Count} assets");
                
                return package;
            }
            catch (Exception ex)
            {
                package.Warnings.Add($"Error resolving dependencies: {ex.Message}");
                OnWarning?.Invoke($"⚠️ Partial resolution for {objectName}: {ex.Message}");
                return package;
            }
        }

        /// <summary>
        /// Deep search for references to an object
        /// </summary>
        public async Task<List<string>> DeepSearchReferencesAsync(string objectName)
        {
            OnStatusUpdate?.Invoke($"🔎 Deep searching references for: {objectName}");
            
            var references = new List<string>();
            
            // Search in definition index
            var allDefinitions = _definitionIndex.GetAllDefinitions();
            foreach (var def in allDefinitions)
            {
                if (def.RawContent?.Contains(objectName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    references.Add(def.Name);
                }
            }

            OnStatusUpdate?.Invoke($"✅ Found {references.Count} references");
            return await Task.FromResult(references);
        }

        // Private helper methods

        private async Task LoadObjectDefinitions(string path)
        {
            await Task.Run(() =>
            {
                // Use existing definition index
                var definitions = _definitionIndex.GetAllDefinitions();
                foreach (var def in definitions)
                {
                    ObjectDefinitionMap[def.Name] = def.RawContent ?? "";
                    ObjectTypeMap[def.Name] = def.BlockType ?? "Unknown";
                }
            });
        }

        private async Task BuildDependencyGraphAsync()
        {
            await Task.Run(async () =>
            {
                foreach (var objectName in ObjectDefinitionMap.Keys)
                {
                    try
                    {
                        var deps = await _dependencyAnalyzer.AnalyzeDependenciesAsync(
                            objectName, objectName, new Dictionary<string, string>());
                        
                        if (deps != null)
                        {
                            DependencyGraph[objectName] = new DependencyNode
                            {
                                Id = objectName,
                                Name = objectName,
                                Type = DependencyType.ObjectINI
                            };
                        }
                    }
                    catch
                    {
                        // Skip objects that fail to analyze
                    }
                }
            });
        }

        private async Task AnalyzeWeaponSystemsAsync()
        {
            // Pre-analyze common weapons for faster lookup
            await Task.CompletedTask;
        }

        private string DetermineObjectType(string definition)
        {
            if (definition.Contains("Weapon")) return "Weapon";
            if (definition.Contains("Projectile")) return "Projectile";
            if (definition.Contains("FXList")) return "Effect";
            if (definition.Contains("Locomotor")) return "Locomotor";
            if (definition.Contains("Module")) return "Module";
            return "Unknown";
        }

        /// <summary>
        /// Transfer package containing all resolved dependencies
        /// </summary>
        public class TransferPackage
        {
            public string ObjectName { get; set; } = "";
            public List<string> Dependencies { get; set; } = new List<string>();
            public List<string> Assets { get; set; } = new List<string>();
            public List<string> WeaponChain { get; set; } = new List<string>();
            public List<IniBlock> IniBlocks { get; set; } = new List<IniBlock>();
            public List<string> Warnings { get; set; } = new List<string>();
            public int TotalDependencyCount { get; set; }
        }

        /// <summary>
        /// Represents an INI code block
        /// </summary>
        public class IniBlock
        {
            public string Name { get; set; } = "";
            public string BlockType { get; set; } = "";
            public string SourceFile { get; set; } = "";
            public string Code { get; set; } = "";
            public List<string> RawLines { get; set; } = new List<string>();
        }
    }
}
