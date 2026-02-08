using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Infrastructure.Archives;

namespace ZeroHourStudio.Infrastructure.GraphEngine
{
    /// <summary>
    /// نسخة محسّنة - استخراج فيزيائي ذكي للملفات والاعتماديات
    /// Enhanced Smart Dependency Extractor - Intelligent physical file extraction
    /// </summary>
    public class SmartDependencyExtractor
    {
        private readonly EnhancedSageCore _engine;
        private readonly IBigFileReader? _archiveReader;

        public event Action<string>? OnExtractionUpdate;
        public event Action<int, int>? OnProgress; // current, total
        public event Action<string>? OnWarning;

        private List<ExtractedFile> _extractedFiles = new List<ExtractedFile>();
        private string? _currentDestination;

        public SmartDependencyExtractor(EnhancedSageCore engine, IBigFileReader? archiveReader = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _archiveReader = archiveReader;
        }

        /// <summary>
        /// Extract physical files from a transfer package to destination
        /// استخراج الملفات الفيزيائية من حزمة النقل إلى الوجهة
        /// </summary>
        public async Task<ExtractionResult> ExtractPhysicalFilesAsync(
            EnhancedSageCore.TransferPackage package, 
            string destination,
            ExtractionOptions options = null)
        {
            options ??= new ExtractionOptions();
            var result = new ExtractionResult { Destination = destination };
            _currentDestination = destination;
            _extractedFiles.Clear();

            try
            {
                OnExtractionUpdate?.Invoke($"📦 Starting extraction to: {destination}");

                // Create destination directory
                if (!Directory.Exists(destination))
                {
                    Directory.CreateDirectory(destination);
                    OnExtractionUpdate?.Invoke($"📁 Created directory: {destination}");
                }

                // Extract dependencies
                OnExtractionUpdate?.Invoke($"🔧 Extracting {package.Dependencies.Count} dependencies...");
                await ExtractDependenciesAsync(package.Dependencies, destination, result);

                // Extract assets
                OnExtractionUpdate?.Invoke($"🎨 Extracting {package.Assets.Count} assets...");
                await ExtractAssetsAsync(package.Assets, destination, result);

                // Write INI blocks
                if (options.WriteIniBlocks && package.IniBlocks.Any())
                {
                    OnExtractionUpdate?.Invoke($"📝 Writing {package.IniBlocks.Count} INI blocks...");
                    await WriteIniBlocksAsync(package.IniBlocks, destination, result);
                }

                result.Success = true;
                result.TotalFilesExtracted = _extractedFiles.Count;
                OnExtractionUpdate?.Invoke($"✅ Extraction complete! {result.TotalFilesExtracted} files extracted");

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                OnExtractionUpdate?.Invoke($"❌ Extraction failed: {ex.Message}");

                // Rollback if requested
                if (options.RollbackOnFailure)
                {
                    await RollbackExtractionAsync();
                }

                return result;
            }
        }

        /// <summary>
        /// Extract a single file from archive
        /// </summary>
        public async Task<bool> ExtractSingleFileAsync(string fileName, string destination)
        {
            try
            {
                if (_archiveReader == null)
                {
                    OnWarning?.Invoke("⚠️ No archive manager available");
                    return false;
                }

                OnExtractionUpdate?.Invoke($"📄 Extracting: {fileName}");

                var outputPath = Path.Combine(destination, fileName);
                var directory = Path.GetDirectoryName(outputPath);
                
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Use IBigFileReader to extract directly to disk
                // We pass empty string as filePath because ModBigFileReader handles the lookup internally
                await _archiveReader.ExtractAsync("", fileName, outputPath);

                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    
                    _extractedFiles.Add(new ExtractedFile
                    {
                        FileName = fileName,
                        FullPath = outputPath,
                        Size = fileInfo.Length,
                        ExtractedAt = DateTime.Now
                    });

                    OnExtractionUpdate?.Invoke($"✅ Extracted: {fileName} ({fileInfo.Length} bytes)");
                    return true;
                }
                else
                {
                    OnWarning?.Invoke($"⚠️ Extraction reported success but file not found: {fileName}");
                    return false;
                }
            }
            catch (FileNotFoundException)
            {
                OnWarning?.Invoke($"⚠️ File not found in archives: {fileName}");
                return false;
            }
            catch (Exception ex)
            {
                OnWarning?.Invoke($"⚠️ Failed to extract {fileName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Rollback extraction - delete all extracted files
        /// </summary>
        public async Task RollbackExtractionAsync()
        {
            OnExtractionUpdate?.Invoke($"🔄 Rolling back extraction...");
            
            int deleted = 0;
            foreach (var file in _extractedFiles)
            {
                try
                {
                    if (File.Exists(file.FullPath))
                    {
                        File.Delete(file.FullPath);
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    OnWarning?.Invoke($"⚠️ Failed to delete {file.FileName}: {ex.Message}");
                }
            }

            // Try to delete the destination directory if empty
            try
            {
                if (!string.IsNullOrEmpty(_currentDestination) && Directory.Exists(_currentDestination) && !Directory.EnumerateFileSystemEntries(_currentDestination).Any())
                {
                    Directory.Delete(_currentDestination);
                }
            }
            catch
            {
                // Ignore errors when deleting directory
            }

            _extractedFiles.Clear();
            OnExtractionUpdate?.Invoke($"🔄 Rollback complete! Deleted {deleted} files");
            
            await Task.CompletedTask;
        }

        // Private helper methods

        private async Task ExtractDependenciesAsync(List<string> dependencies, string destination, ExtractionResult result)
        {
            int current = 0;
            int total = dependencies.Count;

            foreach (var dep in dependencies)
            {
                current++;
                OnProgress?.Invoke(current, total);

                var success = await ExtractSingleFileAsync(dep, destination);
                if (success)
                {
                    result.ExtractedDependencies.Add(dep);
                }
                else
                {
                    result.FailedExtractions.Add(dep);
                }
            }
        }

        private async Task ExtractAssetsAsync(List<string> assets, string destination, ExtractionResult result)
        {
            // Create assets subdirectory
            var assetsPath = Path.Combine(destination, "Assets");
            if (!Directory.Exists(assetsPath))
            {
                Directory.CreateDirectory(assetsPath);
            }

            int current = 0;
            int total = assets.Count;

            foreach (var asset in assets)
            {
                current++;
                OnProgress?.Invoke(current, total);

                var success = await ExtractSingleFileAsync(asset, assetsPath);
                if (success)
                {
                    result.ExtractedAssets.Add(asset);
                }
                else
                {
                    result.FailedExtractions.Add(asset);
                }
            }
        }

        private async Task WriteIniBlocksAsync(List<EnhancedSageCore.IniBlock> iniBlocks, string destination, ExtractionResult result)
        {
            var iniPath = Path.Combine(destination, "ExtractedDefinitions.ini");
            
            using (var writer = new StreamWriter(iniPath, false))
            {
                await writer.WriteLineAsync("; ================================================");
                await writer.WriteLineAsync("; Extracted Definitions - Generated by ZeroHour Studio");
                await writer.WriteLineAsync($"; Date: {DateTime.Now}");
                await writer.WriteLineAsync("; ================================================");
                await writer.WriteLineAsync();

                foreach (var block in iniBlocks)
                {
                    await writer.WriteLineAsync($"; {block.Name} ({block.BlockType})");
                    if (!string.IsNullOrEmpty(block.Code))
                    {
                        await writer.WriteLineAsync(block.Code);
                    }
                    else if (block.RawLines?.Any() == true)
                    {
                        foreach (var line in block.RawLines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                    await writer.WriteLineAsync();
                }
            }

            if (File.Exists(iniPath))
            {
                _extractedFiles.Add(new ExtractedFile
                {
                    FileName = "ExtractedDefinitions.ini",
                    FullPath = iniPath,
                    Size = new FileInfo(iniPath).Length,
                    ExtractedAt = DateTime.Now
                });
            }

            result.IniFileCreated = iniPath;
        }

        /// <summary>
        /// Extraction options
        /// </summary>
        public class ExtractionOptions
        {
            public bool WriteIniBlocks { get; set; } = true;
            public bool RollbackOnFailure { get; set; } = true;
            public bool OverwriteExisting { get; set; } = false;
            public bool CreateSubdirectories { get; set; } = true;
        }

        /// <summary>
        /// Extraction result
        /// </summary>
        public class ExtractionResult
        {
            public string Destination { get; set; } = "";
            public bool Success { get; set; }
            public string? Error { get; set; }
            public int TotalFilesExtracted { get; set; }
            public List<string> ExtractedDependencies { get; set; } = new List<string>();
            public List<string> ExtractedAssets { get; set; } = new List<string>();
            public List<string> FailedExtractions { get; set; } = new List<string>();
            public string? IniFileCreated { get; set; }
        }

        /// <summary>
        /// Information about an extracted file
        /// </summary>
        private class ExtractedFile
        {
            public string FileName { get; set; } = "";
            public string FullPath { get; set; } = "";
            public long Size { get; set; }
            public DateTime ExtractedAt { get; set; }
        }
    }
}
