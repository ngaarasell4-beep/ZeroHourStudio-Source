using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroHourStudio.Infrastructure.GraphEngine;
using ZeroHourStudio.Infrastructure.DependencyAnalysis;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.Archives;

namespace ZeroHourStudio.Infrastructure.Examples
{
    /// <summary>
    /// مثال بسيط لاستخدام محرك GraphEngine
    /// Simple example demonstrating GraphEngine usage
    /// </summary>
    public class GraphEngineUsageExample
    {
        public static async Task DemoAsync()
        {
            Console.WriteLine("=== 🚀 GraphEngine Demo ===");
            Console.WriteLine();

            // Step 1: Initialize services
            Console.WriteLine("📦 Initializing services...");
            
            // Note: في بيئة حقيقية، تحتاج لتهيئة هذه الخدمات بشكل صحيح
            // In a real environment, you need to properly initialize these services
            
            /* 
            var parser = new SAGE_IniParser();
            var archiveManager = new BigArchiveManager(@"D:\Games\ZeroHour\INI.big");
            var reader = new BigFileReader();
            
            var dependencyAnalyzer = new UnitDependencyAnalyzer(parser, reader);
            var weaponService = new WeaponAnalysisService(parser, reader);
            var defIndex = new SageDefinitionIndex();
            var compService = new ComprehensiveDependencyService(
                dependencyAnalyzer, 
                new AssetReferenceHunter(...), 
                new UnitCompletionValidator(...)
            );
            
            // Create the enhanced engine
            var engine = new EnhancedSageCore(
                dependencyAnalyzer,
                weaponService,
                defIndex,
                compService
            );
            
            // Subscribe to events for live updates
            engine.OnStatusUpdate += (msg) => Console.WriteLine($"📢 {msg}");
            engine.OnProgressUpdate += (progress) => Console.WriteLine($"⏳ Progress: {progress}%");
            engine.OnWarning += (msg) => Console.WriteLine($"⚠️  {msg}");
            engine.OnError += (msg) => Console.WriteLine($"❌ {msg}");
            
            Console.WriteLine();
            
            // Step 2: Initialize the engine
            Console.WriteLine("🔧 Initializing engine...");
            await engine.InitializeEngineAsync(@"D:\Games\ZeroHour\");
            
            Console.WriteLine();
            Console.WriteLine($"✅ Engine initialized: {engine.IsInitialized}");
            Console.WriteLine($"📁 Loaded from: {engine.LoadedPath}");
            Console.WriteLine($"📊 Definitions loaded: {engine.ObjectDefinitionMap.Count}");
            Console.WriteLine();
            
            // Step 3: Resolve dependencies for a unit
            string unitName = "USA_Tank_Crusader";
            Console.WriteLine($"🔍 Resolving dependencies for: {unitName}");
            
            var package = await engine.ResolveDependencyChainAsync(unitName, includeWeapons: true);
            
            Console.WriteLine($"📦 Transfer Package Results:");
            Console.WriteLine($"   - Dependencies: {package.Dependencies.Count}");
            Console.WriteLine($"   - Assets: {package.Assets.Count}");
            Console.WriteLine($"   - Weapon Chain: {package.WeaponChain.Count}");
            Console.WriteLine($"   - Total: {package.TotalDependencyCount}");
            
            if (package.Warnings.Count > 0)
            {
                Console.WriteLine($"   ⚠️  Warnings: {package.Warnings.Count}");
                foreach (var warning in package.Warnings)
                {
                    Console.WriteLine($"      - {warning}");
                }
            }
            
            Console.WriteLine();
            
            // Step 4: Smart reference hunting
            Console.WriteLine($"🔎 Searching for references to: {unitName}");
            
            var hunter = new SmartReferenceHunter(engine, dependencyAnalyzer);
            hunter.OnSearchUpdate += (msg) => Console.WriteLine($"   {msg}");
            
            var searchResult = await hunter.DeepSearchAsync(unitName);
            
            Console.WriteLine($"📊 Search Results:");
            Console.WriteLine($"   - Direct References: {searchResult.DirectReferences.Count}");
            Console.WriteLine($"   - Reverse Dependencies: {searchResult.ReverseDependencies.Count}");
            Console.WriteLine($"   - Related Objects: {searchResult.RelatedObjects.Count}");
            Console.WriteLine($"   - Total Found: {searchResult.TotalReferencesFound}");
            
            Console.WriteLine();
            
            // Step 5: Extract files
            string outputPath = @"D:\Output\ExtractedUnit\";
            Console.WriteLine($"📤 Extracting files to: {outputPath}");
            
            var extractor = new SmartDependencyExtractor(engine, archiveManager);
            extractor.OnExtractionUpdate += (msg) => Console.WriteLine($"   {msg}");
            extractor.OnProgress += (current, total) => 
                Console.WriteLine($"   ⏳ Progress: {current}/{total} ({(current * 100 / total)}%)");
            
            var extractResult = await extractor.ExtractPhysicalFilesAsync(
                package, 
                outputPath,
                new SmartDependencyExtractor.ExtractionOptions
                {
                    WriteIniBlocks = true,
                    RollbackOnFailure = true
                }
            );
            
            Console.WriteLine($"📊 Extraction Results:");
            Console.WriteLine($"   - Success: {extractResult.Success}");
            Console.WriteLine($"   - Files Extracted: {extractResult.TotalFilesExtracted}");
            Console.WriteLine($"   - Dependencies: {extractResult.ExtractedDependencies.Count}");
            Console.WriteLine($"   - Assets: {extractResult.ExtractedAssets.Count}");
            
            if (!string.IsNullOrEmpty(extractResult.IniFileCreated))
            {
                Console.WriteLine($"   - INI File: {extractResult.IniFileCreated}");
            }
            
            if (extractResult.FailedExtractions.Count > 0)
            {
                Console.WriteLine($"   ⚠️  Failed: {extractResult.FailedExtractions.Count}");
            }
            
            */
            
            Console.WriteLine();
            Console.WriteLine("=== ✅ Demo Complete ===");
            Console.WriteLine();
            Console.WriteLine("📝 Note: This is a demonstration. In a real application:");
            Console.WriteLine("   1. Initialize services with actual game paths");
            Console.WriteLine("   2. Handle errors and exceptions properly");
            Console.WriteLine("   3. Use dependency injection for service management");
            Console.WriteLine("   4. Implement proper logging");
        }
        
        /// <summary>
        /// مثال مبسط جداً - بدون تهيئة كاملة
        /// Very simplified example - without full initialization
        /// </summary>
        public static void QuickDemo()
        {
            Console.WriteLine("=== 🚀 GraphEngine Quick Demo ===");
            Console.WriteLine();
            Console.WriteLine("The GraphEngine consists of 3 main components:");
            Console.WriteLine();
            Console.WriteLine("1️⃣  EnhancedSageCore");
            Console.WriteLine("   - Main engine that coordinates everything");
            Console.WriteLine("   - Integrates with existing services");
            Console.WriteLine("   - Provides events for status updates");
            Console.WriteLine();
            Console.WriteLine("2️⃣  SmartReferenceHunter");
            Console.WriteLine("   - Intelligent reference searching");
            Console.WriteLine("   - Finds direct and reverse dependencies");
            Console.WriteLine("   - Discovers related objects");
            Console.WriteLine();
            Console.WriteLine("3️⃣  SmartDependencyExtractor");
            Console.WriteLine("   - Physical file extraction");
            Console.WriteLine("   - Rollback support on failure");
            Console.WriteLine("   - Writes INI definition blocks");
            Console.WriteLine();
            Console.WriteLine("📚 For detailed usage, see: walkthrough.md");
            Console.WriteLine();
        }
    }
}
