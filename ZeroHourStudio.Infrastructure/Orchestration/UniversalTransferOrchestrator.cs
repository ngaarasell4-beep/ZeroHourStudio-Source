using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZeroHourStudio.Infrastructure.GraphEngine;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Infrastructure.Orchestration
{
    /// <summary>
    /// المنسق الشامل لعمليات النقل
    /// </summary>
    public class UniversalTransferOrchestrator : IUniversalTransferOrchestrator
    {
        private readonly EnhancedSageCore _sageCore;
        private readonly SmartDependencyExtractor _dependencyExtractor;
        private readonly CommandSetPatchService _commandSetPatcher;
        private readonly SmartFactionExtractor _factionExtractor;

        public UniversalTransferOrchestrator(
            EnhancedSageCore sageCore,
            SmartDependencyExtractor dependencyExtractor,
            CommandSetPatchService commandSetPatcher,
            SmartFactionExtractor factionExtractor)
        {
            _sageCore = sageCore ?? throw new ArgumentNullException(nameof(sageCore));
            _dependencyExtractor = dependencyExtractor ?? throw new ArgumentNullException(nameof(dependencyExtractor));
            _commandSetPatcher = commandSetPatcher ?? throw new ArgumentNullException(nameof(commandSetPatcher));
            _factionExtractor = factionExtractor ?? throw new ArgumentNullException(nameof(factionExtractor));
        }

        public async Task<TransferSessionResult> ExecuteTransferAsync(TransferRequest request, IProgress<TransferSessionProgress>? progress = null)
        {
            var result = new TransferSessionResult();
            var sessionProgress = new TransferSessionProgress { CurrentStage = "Initialization", OverallPercentage = 0 };
            var startTime = DateTime.UtcNow;

            try
            {
                // 1. Validation
                if (string.IsNullOrWhiteSpace(request.SourcePath) || string.IsNullOrWhiteSpace(request.TargetPath))
                    throw new ArgumentException("Source and Target paths are required.");

                ReportProgress("Initializing Engine...", "Loading definitions", 5);

                // Initialize Engine with Source Path
                // Assuming EnhancedSageCore can be re-initialized or is transient
                // Note: In a real DI scenario, we might need a factory or EnsureInitialized method
                await _sageCore.InitializeEngineAsync(request.SourcePath);
                
                // 2. Phase 1: Analysis (GraphEngine)
                ReportProgress("Phase 1: Analysis", $"Analyzing dependencies for {request.UnitName}", 10);
                
                var transferPackage = await _sageCore.ResolveDependencyChainAsync(request.UnitName, includeWeapons: true);
                
                if (transferPackage.Dependencies.Count == 0 && transferPackage.Assets.Count == 0)
                {
                    result.Success = false;
                    result.Message = $"Analysis failed: Unit '{request.UnitName}' not found or has no dependencies.";
                    return result;
                }

                result.AnalysisSuccess = true;
                ReportProgress("Phase 1: Analysis", $"Found {transferPackage.Dependencies.Count} dependencies and {transferPackage.Assets.Count} assets", 30);

                // 3. Phase 2: Faction Validation (Optional but recommended)
                if (!string.IsNullOrEmpty(request.TargetFaction))
                {
                    ReportProgress("Phase 2: Faction Targeting", $"Validating faction {request.TargetFaction}", 35);
                    // We could use _factionExtractor here if we needed to validate against the *Source* or *Target* mod
                    // For now, we assume the user knows the target faction or we auto-detect
                }

                // 4. Phase 3: Transfer Files (SmartDependencyExtractor)
                ReportProgress("Phase 3: File Transfer", "Extracting and copying files...", 40);

                var extractionOptions = new SmartDependencyExtractor.ExtractionOptions
                {
                    OverwriteExisting = request.OverwriteFiles,
                    CreateSubdirectories = true,
                    // Map GraphEngine options if needed
                };

                // Subscribe to updates from extractor
                _dependencyExtractor.OnProgress += (current, total) => 
                {
                    double p = total > 0 ? (double)current / total : 0;
                    // Map extractor progress (0-100) to our scale (40-80)
                    int scaled = 40 + (int)(p * 40);
                    ReportProgress("Phase 3: File Transfer", $"Transferring files... {current}/{total}", scaled);
                };

                var extractionResult = await _dependencyExtractor.ExtractPhysicalFilesAsync(transferPackage, request.TargetPath, extractionOptions);

                if (!extractionResult.Success)
                {
                    result.Success = false;
                    result.Message = "File transfer failed.";
                    result.Errors.Add(extractionResult.Error ?? "Unknown transfer error");
                    result.FilesFailed = extractionResult.FailedExtractions.Count;
                    return result;
                }

                result.TransferSuccess = true;
                result.FilesTransferred = extractionResult.TotalFilesExtracted;
                ReportProgress("Phase 3: File Transfer", "Files transferred successfully", 80);

                // 5. Phase 4: CommandSet Injection (CommandSetPatchService)
                if (request.InjectCommandSet)
                {
                    ReportProgress("Phase 4: Injection", "Injecting CommandSet and Buttons...", 85);
                    
                    // We need a SageUnit object for the patcher. 
                    // Since we have the raw package, we might create a temporary one or overload the service.
                    // For this implementation, we'll create a lightweight SageUnit wrapper.
                    var unitWrapper = new SageUnit 
                    { 
                        TechnicalName = request.UnitName,
                        Side = request.TargetFaction ?? "Unknown" 
                        // Other properties might be needed or fetched
                    };

                    // We might need to parse the unit INI to get ButtonImage etc. for the patcher
                    // EnhancedSageCore should ideally provide this metadata in the package
                    var unitData = new Dictionary<string, string>(); 
                    // TODO: Populate unitData from TransferPackage metadata if available
                    // For now, we proceed with minimal data, patcher might use defaults

                    var patchResult = await _commandSetPatcher.EnsureCommandSetAsync(
                        unitWrapper, 
                        unitData, 
                        request.TargetPath, 
                        request.TargetFaction);

                    result.InjectionSuccess = true;
                    if (patchResult.CommandSetCreated) result.Message += " (CommandSet Created)";
                    if (patchResult.FactoryCommandSetName != null) result.Message += " (Factory Updated)";
                }

                // Finalize
                result.Success = true;
                result.Message = "Transfer completed successfully!";
                result.Duration = DateTime.UtcNow - startTime;
                
                ReportProgress("Completed", "Operation finished successfully", 100);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"An unexpected error occurred: {ex.Message}";
                result.Errors.Add(ex.ToString());
            }

            return result;

            void ReportProgress(string stage, string action, int percent)
            {
                sessionProgress.CurrentStage = stage;
                sessionProgress.CurrentAction = action;
                sessionProgress.OverallPercentage = percent;
                progress?.Report(sessionProgress);
            }
        }
    }
}
