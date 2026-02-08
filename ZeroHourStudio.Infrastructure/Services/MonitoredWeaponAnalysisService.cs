using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Infrastructure.Implementations;
using ZeroHourStudio.Infrastructure.Monitoring;
using ZeroHourStudio.Infrastructure.Filtering;

namespace ZeroHourStudio.Infrastructure.Services
{
    /// <summary>
    /// خدمة محسّنة لتحليل الأسلحة مع مراقبة شاملة
    /// ✓ نظام مراقبة كامل
    /// ✓ فلترة صارمة
    /// ✓ حدود واضحة
    /// ✓ رفض تلقائي للناقص
    /// </summary>
    /// <summary>
    /// خدمة محسّنة لتحليل الأسلحة مع مراقبة شاملة
    /// ✓ نظام مراقبة كامل
    /// ✓ فلترة صارمة
    /// ✓ حدود واضحة
    /// ✓ رفض تلقائي للناقص
    /// </summary>
    public class MonitoredWeaponAnalysisService : IWeaponAnalysisService
    {
        private readonly IIniParser _iniParser;
        private readonly IBigFileReader _bigFileReader;
        private readonly WeaponCompletionValidator _validator;

        public MonitoredWeaponAnalysisService(IIniParser iniParser, IBigFileReader bigFileReader)
        {
            _iniParser = iniParser;
            _bigFileReader = bigFileReader;
            _validator = new WeaponCompletionValidator();
        }

        /// <summary>
        /// تحليل كامل لتبعيات أسلحة الوحدة مع مراقبة
        /// </summary>
        public async Task<WeaponDependencyAnalysis> AnalyzeWeaponDependenciesAsync(string unitName, string modPath)
        {
            MonitoringService.Instance.Log("WEAPON_ANALYSIS", unitName, "START", "Beginning analysis");

            var analysis = new WeaponDependencyAnalysis { UnitName = unitName };

            try
            {
                // تحميل بيانات الوحدة
                var unitData = await LoadUnitDataAsync(unitName, modPath);
                analysis.Faction = GetFactionFromData(unitData);

                // تحليل الأسلحة مع الفلترة
                analysis.Weapons = await AnalyzeWeaponsWithValidationAsync(unitData, modPath);
                
                // تحليل التبعيات فقط للأسلحة المقبولة
                analysis.ProjectileTypes = await AnalyzeProjectilesAsync(analysis.Weapons, modPath);
                analysis.DamageTypes = await AnalyzeDamageTypesAsync(analysis.Weapons, modPath);
                analysis.AudioFiles = await AnalyzeAudioFilesAsync(analysis.Weapons, modPath);
                analysis.VisualEffects = await AnalyzeVisualEffectsAsync(analysis.Weapons, modPath);

                // التحقق النهائي
                await ValidateDependenciesAsync(analysis, modPath);

                MonitoringService.Instance.Log("WEAPON_ANALYSIS", unitName, "COMPLETE",
                    $"{analysis.Weapons.Count} accepted weapons");
            }
            catch (Exception ex)
            {
                MonitoringService.Instance.Log("WEAPON_ANALYSIS", unitName, "ERROR", ex.Message, ex.StackTrace ?? "");
                throw;
            }

            return analysis;
        }

        /// <summary>
        /// تحليل الأسلحة مع التحقق والفلترة
        /// </summary>
        private async Task<List<WeaponChain>> AnalyzeWeaponsWithValidationAsync(
            Dictionary<string, Dictionary<string, string>> unitData, string modPath)
        {
            var acceptedWeapons = new List<WeaponChain>();
            var weaponSlots = new[] { "PrimaryWeapon", "SecondaryWeapon", "TertiaryWeapon", 
                                     "WeaponA", "WeaponB", "WeaponC", "AntiAirWeapon" };

            foreach (var slot in weaponSlots)
            {
                if (unitData.TryGetValue("Object", out var objectSection) && 
                    objectSection.TryGetValue(slot, out var weaponName) && 
                    !string.IsNullOrWhiteSpace(weaponName))
                {
                    var weaponChain = await AnalyzeWeaponChainAsync(weaponName, slot, modPath);
                    
                    if (weaponChain != null)
                    {
                        // التحقق الصارم من الاكتمال
                        if (_validator.IsWeaponComplete(weaponChain, out var rejectReason))
                        {
                            // فحص الحدود
                            var depCount = weaponChain.RelatedFiles?.Count ?? 0;
                            if (DependencyLimits.IsWithinDependencyLimit(depCount, weaponName, out _))
                            {
                                acceptedWeapons.Add(weaponChain);
                                MonitoringService.Instance.Log("WEAPON_ACCEPT", weaponName, "COMPLETE", 
                                    $"Slot={slot}, Deps={depCount}");
                            }
                            else
                            {
                                MonitoringService.Instance.Log("WEAPON_REJECT", weaponName, "LIMIT_EXCEEDED",
                                    $"Dependencies={depCount} > {DependencyLimits.MAX_DEPENDENCIES}");
                            }
                        }
                        else
                        {
                            MonitoringService.Instance.Log("WEAPON_REJECT", weaponName, "INCOMPLETE", rejectReason);
                        }
                    }
                }
            }

            return acceptedWeapons;
        }

        /// <summary>
        /// تحليل سلسلة سلاح واحدة
        /// </summary>
        private async Task<WeaponChain?> AnalyzeWeaponChainAsync(string weaponName, string weaponType, string modPath)
        {
            try
            {
                MonitoringService.Instance.Log("WEAPON_CHAIN", weaponName, "START", $"Type={weaponType}");

                var weaponData = await LoadWeaponDataAsync(weaponName, modPath);
                if (weaponData.Count == 0)
                {
                    MonitoringService.Instance.Log("WEAPON_CHAIN", weaponName, "FAIL", "No weapon data found");
                    return null;
                }

                if (!weaponData.TryGetValue("Weapon", out var weaponSection))
                {
                    MonitoringService.Instance.Log("WEAPON_CHAIN", weaponName, "FAIL", "No Weapon section");
                    return null;
                }

                var chain = new WeaponChain
                {
                    WeaponName = weaponName,
                    WeaponType = weaponType,
                    Damage = ParseDouble(weaponSection.GetValueOrDefault("Damage", "0")),
                    Range = ParseDouble(weaponSection.GetValueOrDefault("Range", "0")),
                    FireRate = ParseDouble(weaponSection.GetValueOrDefault("FireRate", "0")),
                    DamageType = weaponSection.GetValueOrDefault("DamageType", "UNKNOWN"),
                    ProjectileName = weaponSection.GetValueOrDefault("ProjectileNugget", ""),
                    AudioFire = weaponSection.GetValueOrDefault("FireSound", ""),
                    AudioExplosion = weaponSection.GetValueOrDefault("ExplosionSound", ""),
                    VisualEffect = weaponSection.GetValueOrDefault("FireFX", ""),
                    ModelFile = weaponSection.GetValueOrDefault("Model", "")
                };

                chain.RelatedFiles = await GetRelatedFilesAsync(chain, modPath);
                await ValidateWeaponChainAsync(chain, modPath);

                return chain;
            }
            catch (Exception ex)
            {
                MonitoringService.Instance.Log("WEAPON_CHAIN", weaponName, "ERROR", ex.Message);
                return null;
            }
        }

        private async Task<List<string>> GetRelatedFilesAsync(WeaponChain chain, string modPath)
        {
            var files = new List<string>();
            if (!string.IsNullOrWhiteSpace(chain.ModelFile)) files.Add(chain.ModelFile);
            if (!string.IsNullOrWhiteSpace(chain.ProjectileName)) 
            {
                files.Add($"{chain.ProjectileName}.w3d");
                files.Add($"{chain.ProjectileName}_tex.dds");
            }
            if (!string.IsNullOrWhiteSpace(chain.AudioFire)) files.Add($"{chain.AudioFire}.wav");
            if (!string.IsNullOrWhiteSpace(chain.AudioExplosion)) files.Add($"{chain.AudioExplosion}.wav");
            if (!string.IsNullOrWhiteSpace(chain.VisualEffect)) files.Add($"{chain.VisualEffect}.fx");
            return files.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        }

        private async Task ValidateWeaponChainAsync(WeaponChain chain, string modPath)
        {
            chain.MissingFiles.Clear();
            var allFilesExist = true;
            foreach (var file in chain.RelatedFiles)
            {
                var exists = await CheckFileExistsAsync(file, modPath);
                if (!exists)
                {
                    chain.MissingFiles.Add(file);
                    allFilesExist = false;
                    MonitoringService.Instance.Log("FILE_MISSING", file, "NOT_FOUND", chain.WeaponName);
                }
            }
            chain.IsComplete = allFilesExist && chain.MissingFiles.Count == 0;
        }

        private async Task<List<string>> AnalyzeProjectilesAsync(List<WeaponChain> weapons, string modPath)
        {
            return weapons.Where(w => !string.IsNullOrWhiteSpace(w.ProjectileName))
                         .Select(w => w.ProjectileName).Distinct().ToList();
        }

        private async Task<List<string>> AnalyzeDamageTypesAsync(List<WeaponChain> weapons, string modPath)
        {
            return weapons.Where(w => !string.IsNullOrWhiteSpace(w.DamageType))
                         .Select(w => w.DamageType).Distinct().ToList();
        }

        private async Task<List<string>> AnalyzeAudioFilesAsync(List<WeaponChain> weapons, string modPath)
        {
            var audio = new List<string>();
            foreach (var w in weapons)
            {
                if (!string.IsNullOrWhiteSpace(w.AudioFire) && !audio.Contains(w.AudioFire)) audio.Add(w.AudioFire);
                if (!string.IsNullOrWhiteSpace(w.AudioExplosion) && !audio.Contains(w.AudioExplosion)) audio.Add(w.AudioExplosion);
            }
            return audio;
        }

        private async Task<List<string>> AnalyzeVisualEffectsAsync(List<WeaponChain> weapons, string modPath)
        {
            await Task.CompletedTask; // Satisfy async requirement
            return weapons.Where(w => !string.IsNullOrWhiteSpace(w.VisualEffect))
                         .Select(w => w.VisualEffect!)
                         .Distinct()
                         .ToList();
        }

        private async Task ValidateDependenciesAsync(WeaponDependencyAnalysis analysis, string modPath)
        {
            var allFiles = analysis.Weapons.SelectMany(w => w.RelatedFiles).ToList();
            var existing = new List<string>();
            var missing = new List<string>();
            foreach (var file in allFiles)
            {
                if (await CheckFileExistsAsync(file, modPath)) existing.Add(file);
                else missing.Add(file);
            }
            analysis.FoundDependencies = existing.Count;
            analysis.MissingDependencies = missing.Count;
        }

        private async Task<Dictionary<string, Dictionary<string, string>>> LoadUnitDataAsync(string unitName, string modPath)
        {
            try { return await _iniParser.ParseAsync(modPath + "\\Object.ini"); }
            catch { return new Dictionary<string, Dictionary<string, string>>(); }
        }

        private async Task<Dictionary<string, Dictionary<string, string>>> LoadWeaponDataAsync(string weaponName, string modPath)
        {
            try { return await _iniParser.ParseAsync(modPath + "\\Weapon.ini"); }
            catch { return new Dictionary<string, Dictionary<string, string>>(); }
        }

        private string GetFactionFromData(Dictionary<string, Dictionary<string, string>> unitData)
        {
            return unitData.TryGetValue("Object", out var obj) ? obj.GetValueOrDefault("Side", "Unknown") : "Unknown";
        }

        private double ParseDouble(string value) => double.TryParse(value, out var result) ? result : 0.0;

        private async Task<bool> CheckFileExistsAsync(string fileName, string modPath)
        {
            try { return await _bigFileReader.FileExistsAsync(modPath, fileName); }
            catch { return false; }
        }
    }
}
