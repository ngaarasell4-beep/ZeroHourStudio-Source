using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZeroHourStudio.Infrastructure.Monitoring;
using ZeroHourStudio.Infrastructure.Filtering;
using ZeroHourStudio.Infrastructure.Parsers;

namespace ZeroHourStudio.Infrastructure.Services
{
    /// <summary>
    /// استخراج ذكي للفصائل من Object/*.ini فقط
    /// </summary>
    public class SmartFactionExtractor
    {
        private readonly SAGE_IniParser _parser;

        public SmartFactionExtractor(SAGE_IniParser parser)
        {
            _parser = parser;
        }

        /// <summary>
        /// استخراج الفصائل من Object/*.ini
        /// يعمل فقط من ملفات Object/*.ini
        /// يفلتر INFANTRY, VEHICLE, AIRCRAFT فقط
        /// </summary>
        public async Task<FactionExtractionResult> ExtractFactionsAsync(string modPath)
        {
            MonitoringService.Instance.Log("FACTION_EXTRACT", modPath, "START", "Beginning faction extraction");

            var result = new FactionExtractionResult();
            var objectPath = Path.Combine(modPath, "Data", "INI", "Object");

            if (!Directory.Exists(objectPath))
            {
                MonitoringService.Instance.Log("FACTION_EXTRACT", modPath, "ERROR", "Object directory not found");
                return result;
            }

            var iniFiles = Directory.GetFiles(objectPath, "*.ini");
            MonitoringService.Instance.Log("FACTION_EXTRACT", objectPath, "INFO", $"Found {iniFiles.Length} INI files");

            foreach (var iniFile in iniFiles)
            {
                MonitoringService.Instance.Log("FILE_OPEN", Path.GetFileName(iniFile), "START", "Parsing");
                
                var data = await _parser.ParseAsync(iniFile);

                foreach (var section in data)
                {
                    var objectName = section.Key;
                    var objectData = section.Value;

                    // استخراج KindOf و Side
                    if (!objectData.TryGetValue("KindOf", out var kindOf))
                        continue;

                    if (!objectData.TryGetValue("Side", out var side))
                        continue;

                    // تطبيق الفلترة الصارمة
                    if (!ObjectTypeFilter.IsCombatUnit(kindOf, objectName, out var rejectReason))
                        continue;

                    var objectType = ObjectTypeFilter.GetObjectType(kindOf);

                    // إضافة الفصيل
                    if (!result.Factions.ContainsKey(side))
                    {
                        result.Factions[side] = new FactionData { Name = side };
                        MonitoringService.Instance.Log("FACTION_FOUND", side, "NEW", "Faction discovered");
                    }

                    // إضافة الوحدة
                    var combatUnit = new CombatUnitData
                    {
                        Name = objectName,
                        Type = objectType,
                        Faction = side,
                        ObjectData = objectData
                    };

                    result.Factions[side].Units.Add(combatUnit);
                    result.TotalUnits++;

                    MonitoringService.Instance.Log("UNIT_ADDED", objectName, objectType, side,
                        $"Faction={side}, Type={objectType}");
                }
            }

            MonitoringService.Instance.Log("FACTION_EXTRACT", "COMPLETE", "SUCCESS",
                $"{result.Factions.Count} factions, {result.TotalUnits} units");

            return result;
        }
    }

    /// <summary>
    /// نتيجة استخراج الفصائل
    /// </summary>
    public class FactionExtractionResult
    {
        public Dictionary<string, FactionData> Factions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int TotalUnits { get; set; }
    }

    /// <summary>
    /// بيانات فصيل واحد
    /// </summary>
    public class FactionData
    {
        public string Name { get; set; } = string.Empty;
        public List<CombatUnitData> Units { get; } = new();
    }

    /// <summary>
    /// بيانات وحدة قتالية
    /// </summary>
    public class CombatUnitData
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Faction { get; set; } = string.Empty;
        public Dictionary<string, string> ObjectData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
