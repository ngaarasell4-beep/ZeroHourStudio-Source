using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Application.Interfaces;
using ZeroHourStudio.Domain.Entities;
using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Infrastructure.Implementations;

namespace ZeroHourStudio.Infrastructure.Services
{
    /// <summary>
    /// خدمة متخصصة لتحليل تبعيات الأسلحة بالكامل
    /// </summary>
    /// <summary>
    /// خدمة متخصصة لتحليل تبعيات الأسلحة بالكامل
    /// </summary>
    public class WeaponAnalysisService : IWeaponAnalysisService
    {
        private readonly IIniParser _iniParser;
        private readonly IBigFileReader _bigFileReader;

        public WeaponAnalysisService(IIniParser iniParser, IBigFileReader bigFileReader)
        {
            _iniParser = iniParser;
            _bigFileReader = bigFileReader;
        }

        /// <summary>
        /// تحليل كامل لتبعيات أسلحة الوحدة
        /// </summary>
        public async Task<WeaponDependencyAnalysis> AnalyzeWeaponDependenciesAsync(string unitName, string modPath)
        {
            var analysis = new WeaponDependencyAnalysis
            {
                UnitName = unitName
            };

            // تحميل بيانات الوحدة
            var unitData = await LoadUnitDataAsync(unitName, modPath);
            analysis.Faction = GetFactionFromData(unitData);

            // تحليل الأسلحة
            analysis.Weapons = await AnalyzeWeaponsAsync(unitData, modPath);
            
            // تحليل القذائف
            analysis.ProjectileTypes = await AnalyzeProjectilesAsync(analysis.Weapons, modPath);
            
            // تحليل أنواع الضرر
            analysis.DamageTypes = await AnalyzeDamageTypesAsync(analysis.Weapons, modPath);
            
            // تحليل الملفات الصوتية
            analysis.AudioFiles = await AnalyzeAudioFilesAsync(analysis.Weapons, modPath);
            
            // تحليل المؤثرات البصرية
            analysis.VisualEffects = await AnalyzeVisualEffectsAsync(analysis.Weapons, modPath);

            // التحقق من وجود الملفات
            await ValidateDependenciesAsync(analysis, modPath);

            return analysis;
        }

        /// <summary>
        /// تحليل سلاسل الأسلحة
        /// </summary>
        private async Task<List<WeaponChain>> AnalyzeWeaponsAsync(Dictionary<string, Dictionary<string, string>> unitData, string modPath)
        {
            var weapons = new List<WeaponChain>();

            // استخراج أنواع الأسلحة من بيانات الوحدة
            var weaponSlots = new[]
            {
                "PrimaryWeapon", "SecondaryWeapon", "TertiaryWeapon",
                "WeaponA", "WeaponB", "WeaponC", "AntiAirWeapon",
                "Primary", "Secondary", "Tertiary"
            };

            foreach (var slot in weaponSlots)
            {
                if (unitData.TryGetValue("Object", out var objectSection) && 
                    objectSection.TryGetValue(slot, out var weaponName) && 
                    !string.IsNullOrWhiteSpace(weaponName))
                {
                    var weaponChain = await AnalyzeWeaponChainAsync(weaponName, slot, modPath);
                    if (weaponChain != null)
                    {
                        weapons.Add(weaponChain);
                    }
                }
            }

            return weapons;
        }

        /// <summary>
        /// تحليل سلسلة سلاح واحدة
        /// </summary>
        private async Task<WeaponChain?> AnalyzeWeaponChainAsync(string weaponName, string weaponType, string modPath)
        {
            try
            {
                var weaponData = await LoadWeaponDataAsync(weaponName, modPath);
                if (weaponData.Count == 0) return null;

                if (!weaponData.TryGetValue("Weapon", out var weaponSection))
                    return null;

                var chain = new WeaponChain
                {
                    WeaponName = weaponName,
                    WeaponType = weaponType,
                    Damage = ParseDouble(weaponSection.GetValueOrDefault("Damage", "0")),
                    Range = ParseDouble(weaponSection.GetValueOrDefault("Range", "0")),
                    FireRate = ParseDouble(weaponSection.GetValueOrDefault("FireRate", "0")),
                    DamageType = weaponSection.GetValueOrDefault("DamageType", "UNKNOWN"),
                    ProjectileName = weaponSection.GetValueOrDefault("ProjectileName", ""),
                    AudioFire = weaponSection.GetValueOrDefault("FireSound", ""),
                    AudioExplosion = weaponSection.GetValueOrDefault("ExplosionSound", ""),
                    VisualEffect = weaponSection.GetValueOrDefault("FireFX", ""),
                    ModelFile = weaponSection.GetValueOrDefault("Model", "")
                };

                // إضافة الملفات المرتبطة
                chain.RelatedFiles = await GetRelatedFilesAsync(chain, modPath);

                // التحقق من اكتمال السلسلة
                await ValidateWeaponChainAsync(chain, modPath);

                return chain;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// الحصول على الملفات المرتبطة بالسلاح
        /// </summary>
        private async Task<List<string>> GetRelatedFilesAsync(WeaponChain chain, string modPath)
        {
            var files = new List<string>();

            // إضافة ملفات النموذج
            if (!string.IsNullOrWhiteSpace(chain.ModelFile))
            {
                files.Add(chain.ModelFile);
            }

            // إضافة ملفات القذائف
            if (!string.IsNullOrWhiteSpace(chain.ProjectileName))
            {
                files.Add($"{chain.ProjectileName}.w3d");
                files.Add($"{chain.ProjectileName}_tex.dds");
            }

            // إضافة ملفات الصوت
            if (!string.IsNullOrWhiteSpace(chain.AudioFire))
            {
                files.Add($"{chain.AudioFire}.wav");
            }

            if (!string.IsNullOrWhiteSpace(chain.AudioExplosion))
            {
                files.Add($"{chain.AudioExplosion}.wav");
            }

            // إضافة ملفات التأثيرات
            if (!string.IsNullOrWhiteSpace(chain.VisualEffect))
            {
                files.Add($"{chain.VisualEffect}.fx");
            }

            return files.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        }

        /// <summary>
        /// التحقق من اكتمال سلسلة السلاح
        /// </summary>
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
                }
            }

            chain.IsComplete = allFilesExist && chain.MissingFiles.Count == 0;
        }

        /// <summary>
        /// تحليل القذائف
        /// </summary>
        private async Task<List<string>> AnalyzeProjectilesAsync(List<WeaponChain> weapons, string modPath)
        {
            var projectiles = new List<string>();

            foreach (var weapon in weapons)
            {
                if (!string.IsNullOrWhiteSpace(weapon.ProjectileName) && !projectiles.Contains(weapon.ProjectileName))
                {
                    projectiles.Add(weapon.ProjectileName);
                }
            }

            return projectiles;
        }

        /// <summary>
        /// تحليل أنواع الضرر
        /// </summary>
        private async Task<List<string>> AnalyzeDamageTypesAsync(List<WeaponChain> weapons, string modPath)
        {
            var damageTypes = new HashSet<string>();

            foreach (var weapon in weapons)
            {
                if (!string.IsNullOrWhiteSpace(weapon.DamageType))
                {
                    damageTypes.Add(weapon.DamageType);
                }
            }

            return damageTypes.ToList();
        }

        /// <summary>
        /// تحليل الملفات الصوتية
        /// </summary>
        private async Task<List<string>> AnalyzeAudioFilesAsync(List<WeaponChain> weapons, string modPath)
        {
            var audioFiles = new List<string>();

            foreach (var weapon in weapons)
            {
                if (!string.IsNullOrWhiteSpace(weapon.AudioFire) && !audioFiles.Contains(weapon.AudioFire))
                {
                    audioFiles.Add(weapon.AudioFire);
                }

                if (!string.IsNullOrWhiteSpace(weapon.AudioExplosion) && !audioFiles.Contains(weapon.AudioExplosion))
                {
                    audioFiles.Add(weapon.AudioExplosion);
                }
            }

            return audioFiles;
        }

        /// <summary>
        /// تحليل المؤثرات البصرية
        /// </summary>
        private async Task<List<string>> AnalyzeVisualEffectsAsync(List<WeaponChain> weapons, string modPath)
        {
            var effects = new List<string>();

            foreach (var weapon in weapons)
            {
                if (!string.IsNullOrWhiteSpace(weapon.VisualEffect) && !effects.Contains(weapon.VisualEffect))
                {
                    effects.Add(weapon.VisualEffect);
                }
            }

            return effects;
        }

        /// <summary>
        /// التحقق من وجود التبعيات
        /// </summary>
        private async Task ValidateDependenciesAsync(WeaponDependencyAnalysis analysis, string modPath)
        {
            var allFiles = new List<string>();
            allFiles.AddRange(analysis.Weapons.SelectMany(w => w.RelatedFiles));

            var existingFiles = new List<string>();
            var missingFiles = new List<string>();

            foreach (var file in allFiles)
            {
                var exists = await CheckFileExistsAsync(file, modPath);
                if (exists)
                {
                    existingFiles.Add(file);
                }
                else
                {
                    missingFiles.Add(file);
                }
            }

            analysis.FoundDependencies = existingFiles.Count;
            analysis.MissingDependencies = missingFiles.Count;
        }

        // Helper Methods
        private async Task<Dictionary<string, Dictionary<string, string>>> LoadUnitDataAsync(string unitName, string modPath)
        {
            try
            {
                var unitData = await _iniParser.ParseAsync(modPath + "\\Object.ini");
                return unitData;
            }
            catch
            {
                return new Dictionary<string, Dictionary<string, string>>();
            }
        }

        private async Task<Dictionary<string, Dictionary<string, string>>> LoadWeaponDataAsync(string weaponName, string modPath)
        {
            try
            {
                var weaponData = await _iniParser.ParseAsync(modPath + "\\Weapon.ini");
                return weaponData;
            }
            catch
            {
                return new Dictionary<string, Dictionary<string, string>>();
            }
        }

        private string GetFactionFromData(Dictionary<string, Dictionary<string, string>> unitData)
        {
            if (unitData.TryGetValue("Object", out var objectSection))
            {
                return objectSection.GetValueOrDefault("Side", "Unknown");
            }
            return "Unknown";
        }

        private double ParseDouble(string value)
        {
            return double.TryParse(value, out var result) ? result : 0.0;
        }

        private async Task<bool> CheckFileExistsAsync(string fileName, string modPath)
        {
            try
            {
                return await _bigFileReader.FileExistsAsync(modPath, fileName);
            }
            catch
            {
                return false;
            }
        }
    }
}