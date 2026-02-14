using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.Implementations;
using ZeroHourStudio.Infrastructure.Logging;

namespace ZeroHourStudio.UI.WPF.Core
{
    /// <summary>
    /// نظام إحصائيات وتشخيص متقدم
    /// </summary>
    public class AdvancedStatisticsSystem
    {
        private readonly SimpleLogger _logger;
        private readonly UnitDiscoveryService _discoveryService;
        private readonly ModBigFileReader _bigFileReader;

        public Dictionary<string, object> Statistics { get; private set; } = new();
        public List<string> DiagnosticMessages { get; private set; } = new();

        public AdvancedStatisticsSystem(SimpleLogger logger, UnitDiscoveryService discoveryService, ModBigFileReader bigFileReader)
        {
            _logger = logger;
            _discoveryService = discoveryService;
            _bigFileReader = bigFileReader;
        }

        /// <summary>
        /// تشغيل تشخيص شامل للمود
        /// </summary>
        public async Task<Dictionary<string, object>> RunComprehensiveDiagnostic(string modPath)
        {
            Statistics.Clear();
            DiagnosticMessages.Clear();

            _logger.LogInfo("بدء التشخيص الشامل للمود: " + modPath);
            DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] بدء تشخيص المود: {modPath}");

            // 1. التحقق من وجود المود
            var pathExists = System.IO.Directory.Exists(modPath);
            Statistics["ModPathExists"] = pathExists;
            DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] وجود مسار المود: {pathExists}");

            if (!pathExists)
            {
                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ مسار المود غير موجود!");
                return Statistics;
            }

            // 2. فحص ملفات BIG
            await AnalyzeBigFiles(modPath);

            // 3. فحص ملفات INI
            await AnalyzeIniFiles(modPath);

            // 4. فحص مجلدات الأصول
            AnalyzeAssetDirectories(modPath);

            // 5. محاولة اكتشاف الوحدات
            await DiscoverUnits(modPath);

            // 6. فحص الفصائل
            await DiscoverFactions(modPath);

            // 7. فحص الأسلحة
            await DiscoverWeapons(modPath);

            // 8. إحصائيات الأداء
            CalculatePerformanceStats();

            _logger.LogInfo($"اكتمل التشخيص: {DiagnosticMessages.Count} رسالة");
            DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ✅ اكتمل التشخيص الشامل");

            return Statistics;
        }

        private async Task AnalyzeBigFiles(string modPath)
        {
            try
            {
                _bigFileReader.SetRootPath(modPath);
                
                var bigFiles = System.IO.Directory.GetFiles(modPath, "*.big", System.IO.SearchOption.AllDirectories);
                Statistics["BigFilesCount"] = bigFiles.Length;
                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] عدد ملفات BIG: {bigFiles.Length}");

                // محاولة قراءة ملفات BIG
                var readableBigFiles = 0;
                foreach (var bigFile in bigFiles)
                {
                    try
                    {
                        await _bigFileReader.ReadAsync(bigFile);
                        readableBigFiles++;
                        DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ✅ ملف BIG صالح: {System.IO.Path.GetFileName(bigFile)}");
                    }
                    catch (Exception ex)
                    {
                        DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ❌ ملف BIG تالف: {System.IO.Path.GetFileName(bigFile)} - {ex.Message}");
                    }
                }

                Statistics["ReadableBigFiles"] = readableBigFiles;
                Statistics["BigFilesSize"] = bigFiles.Sum(f => new System.IO.FileInfo(f).Length);
            }
            catch (Exception ex)
            {
                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ خطأ في تحليل ملفات BIG: {ex.Message}");
                Statistics["BigFilesError"] = ex.Message;
            }
        }

        private async Task AnalyzeIniFiles(string modPath)
        {
            try
            {
                var iniFiles = System.IO.Directory.GetFiles(modPath, "*.ini", System.IO.SearchOption.AllDirectories);
                Statistics["IniFilesCount"] = iniFiles.Length;
                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] عدد ملفات INI: {iniFiles.Length}");

                var criticalIniFiles = new[] { "Object.ini", "Weapon.ini", "Faction.ini", "PlayerTemplate.ini" };
                var foundCriticalFiles = 0;

                foreach (var criticalFile in criticalIniFiles)
                {
                    var fullPath = System.IO.Path.Combine(modPath, "Data", "INI", criticalFile);
                    if (System.IO.File.Exists(fullPath))
                    {
                        foundCriticalFiles++;
                        var size = new System.IO.FileInfo(fullPath).Length;
                        DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ✅ ملف INI حرج: {criticalFile} ({size} bytes)");

                        // قراءة محتوى الملف للتحقق
                        try
                        {
                            var content = await System.IO.File.ReadAllTextAsync(fullPath);
                            var lines = content.Split('\n').Length;
                            DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] 📄 {criticalFile}: {lines} سطر");
                        }
                        catch (Exception ex)
                        {
                            DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ❌ خطأ في قراءة {criticalFile}: {ex.Message}");
                        }
                    }
                    else
                    {
                        DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ ملف INI مفقود: {criticalFile}");
                    }
                }

                Statistics["CriticalIniFiles"] = foundCriticalFiles;
            }
            catch (Exception ex)
            {
                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ خطأ في تحليل ملفات INI: {ex.Message}");
                Statistics["IniFilesError"] = ex.Message;
            }
        }

        private void AnalyzeAssetDirectories(string modPath)
        {
            try
            {
                var assetDirs = new[] { "Art", "Audio", "Maps", "Data", "Scripts" };
                var foundDirs = 0;

                foreach (var dir in assetDirs)
                {
                    var fullPath = System.IO.Path.Combine(modPath, dir);
                    if (System.IO.Directory.Exists(fullPath))
                    {
                        foundDirs++;
                        var fileCount = System.IO.Directory.GetFiles(fullPath, "*.*", System.IO.SearchOption.AllDirectories).Length;
                        DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] 📁 مجلد {dir}: {fileCount} ملف");
                    }
                    else
                    {
                        DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ مجلد مفقود: {dir}");
                    }
                }

                Statistics["AssetDirectories"] = foundDirs;
            }
            catch (Exception ex)
            {
                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ خطأ في تحليل مجلدات الأصول: {ex.Message}");
            }
        }

        private async Task DiscoverUnits(string modPath)
        {
            try
            {
                _logger.LogInfo("محاولة اكتشاف الوحدات...");
                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] 🔍 بدء اكتشاف الوحدات...");

                var progress = new Progress<Infrastructure.Services.DiscoveryProgress>(p =>
                {
                    DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] 📊 التقدم: {p.Percentage}% ({p.FilesProcessed}/{p.TotalFiles})");
                });

                var result = await _discoveryService.DiscoverUnitsAsync(modPath, progress);
                
                Statistics["DiscoveredUnits"] = result.Units.Count;
                //Statistics["DiscoveryErrors"] = result.Errors?.Count ?? 0;

                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ✅ تم اكتشاف {result.Units.Count} وحدة");

                if (result.Units.Count > 0)
                {
                    var sampleUnits = result.Units.Take(5).Select(u => u.TechnicalName);
                    DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] 📝 عينات الوحدات: {string.Join(", ", sampleUnits)}");
                }

                // if (result.Errors?.Count > 0)
                // {
                //     DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ {result.Errors.Count} خطأ في الاكتشاف");
                //     foreach (var error in result.Errors.Take(3))
                //     {
                //         DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ❌ {error}");
                //     }
                // }
            }
            catch (Exception ex)
            {
                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ خطأ في اكتشاف الوحدات: {ex.Message}");
                Statistics["UnitDiscoveryError"] = ex.Message;
            }
        }

        private async Task DiscoverFactions(string modPath)
        {
            try
            {
                var factions = await _discoveryService.DiscoverFactionsAsync(modPath);
                Statistics["DiscoveredFactions"] = factions.Count;
                
                if (factions.Count > 0)
                {
                    DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] 🏴 تم اكتشاف {factions.Count} فصيل: {string.Join(", ", factions)}");
                    Statistics["FactionList"] = factions;
                }
                else
                {
                    DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ لم يتم اكتشاف أي فصائل");
                }
            }
            catch (Exception ex)
            {
                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ خطأ في اكتشاف الفصائل: {ex.Message}");
                Statistics["FactionDiscoveryError"] = ex.Message;
            }
        }

        private async Task DiscoverWeapons(string modPath)
        {
            try
            {
                var weaponIniPath = System.IO.Path.Combine(modPath, "Data", "INI", "Weapon.ini");
                if (System.IO.File.Exists(weaponIniPath))
                {
                    var content = await System.IO.File.ReadAllTextAsync(weaponIniPath);
                    var weaponMatches = System.Text.RegularExpressions.Regex.Matches(content, @"Weapon\s+(\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    Statistics["DiscoveredWeapons"] = weaponMatches.Count;
                    DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] 🔫 تم اكتشاف {weaponMatches.Count} سلاح");
                }
                else
                {
                    DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ ملف Weapon.ini غير موجود");
                    Statistics["DiscoveredWeapons"] = 0;
                }
            }
            catch (Exception ex)
            {
                DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ خطأ في اكتشاف الأسلحة: {ex.Message}");
                Statistics["WeaponDiscoveryError"] = ex.Message;
            }
        }

        private void CalculatePerformanceStats()
        {
            var memoryUsed = GC.GetTotalMemory(false);
            Statistics["MemoryUsed"] = memoryUsed;
            Statistics["MemoryUsedMB"] = Math.Round(memoryUsed / 1024.0 / 1024.0, 2);
            
            DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] 📈 استخدام الذاكرة: {Math.Round(memoryUsed / 1024.0 / 1024.0, 2)} MB");
            DiagnosticMessages.Add($"[{DateTime.Now:HH:mm:ss}] ⏱️ وقت التشخيص: {DateTime.Now:HH:mm:ss}");
        }

        /// <summary>
        /// إنشاء تقرير تشخيص مفصل
        /// </summary>
        public string GenerateDiagnosticReport()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== تقرير تشخيص ZeroHour Studio ===");
            report.AppendLine($"الوقت: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();

            report.AppendLine("📊 الإحصائيات:");
            foreach (var stat in Statistics)
            {
                report.AppendLine($"  {stat.Key}: {stat.Value}");
            }
            report.AppendLine();

            report.AppendLine("🔍 التشخيص التفصيلي:");
            foreach (var message in DiagnosticMessages)
            {
                report.AppendLine($"  {message}");
            }

            report.AppendLine();
            report.AppendLine("💡 التوصيات:");
            
            if (!Statistics.ContainsKey("BigFilesCount") || (int)Statistics["BigFilesCount"] == 0)
            {
                report.AppendLine("  • تأكد من وجود ملفات BIG في مجلد المود");
            }
            
            if (!Statistics.ContainsKey("DiscoveredUnits") || (int)Statistics["DiscoveredUnits"] == 0)
            {
                report.AppendLine("  • تحقق من وجود ملف Object.ini في مجلد Data/INI");
                report.AppendLine("  • تأكد من أن ملفات INI تحتوي على تعريفات الوحدات الصحيحة");
            }
            
            if (!Statistics.ContainsKey("DiscoveredFactions") || (int)Statistics["DiscoveredFactions"] == 0)
            {
                report.AppendLine("  • تحقق من وجود ملف Faction.ini أو PlayerTemplate.ini");
            }

            return report.ToString();
        }
    }
}