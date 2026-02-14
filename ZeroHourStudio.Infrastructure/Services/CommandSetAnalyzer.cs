using System.Text.RegularExpressions;
using ZeroHourStudio.Domain.Models;
using ZeroHourStudio.Infrastructure.Parsers;

namespace ZeroHourStudio.Infrastructure.Services
{
    public class CommandSetAnalyzer
    {
        private readonly SAGE_IniParser _iniParser;
        private static readonly Regex CommandSetHeaderRegex = new(@"^\s*CommandSet\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SlotRegex = new(@"^\s*(\d+)\s*=\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public CommandSetAnalyzer(SAGE_IniParser iniParser)
        {
            _iniParser = iniParser ?? throw new ArgumentNullException(nameof(iniParser));
        }

        public async Task<CommandSetAnalysis> AnalyzeModCommandSetsAsync(string modPath)
        {
            var analysis = new CommandSetAnalysis
            {
                ModPath = modPath
            };

            try
            {
                // ✅ المسارات المحتملة لـ CommandSet.ini (النسخة الأولى توضح ترتيب البحث)
                var possiblePaths = new[]
                {
                    Path.Combine(modPath, "Data", "INI", "CommandSet.ini"),  // المسار الافتراضي
                    Path.Combine(modPath, "Data", "INI", "commandset.ini"),  // case-insensitive
                    Path.Combine(modPath, "CommandSet.ini"),                  // جذر المود
                    Path.Combine(modPath, "commandset.ini"),                  // جذر (lowercase)
                };

                System.Diagnostics.Debug.WriteLine($"\n[CommandSetAnalyzer.AnalyzeModCommandSetsAsync] === STARTING ANALYSIS ===");
                System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] Input modPath: {modPath}");
                System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] modPath exists: {Directory.Exists(modPath)}");

                string? commandSetPath = null;
                foreach (var path in possiblePaths)
                {
                    var exists = File.Exists(path);
                    System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] Checking: {path} -> {(exists ? "✓ FOUND" : "✗ NOT FOUND")}");
                    if (exists)
                    {
                        commandSetPath = path;
                        System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] ✓✓✓ Selected CommandSet.ini at: {commandSetPath}");
                        break;
                    }
                }

                // ✅ معالجة حالة عدم وجود الملف العادي
                string[] lines;
                if (commandSetPath == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] ✗✗✗ CommandSet.ini NOT FOUND - using empty fallback (12 empty slots)");
                    // إرجاع فارغ بدلاً من خطأ - سيؤدي إلى 12 أزرار فارغة آمنة
                    return analysis;
                }

                lines = await File.ReadAllLinesAsync(commandSetPath);
                System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] ✓ File loaded successfully: {lines.Length} lines");

                string? currentSetName = null;
                var currentSlots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                void FlushCurrent()
                {
                    if (string.IsNullOrWhiteSpace(currentSetName))
                        return;

                    System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] Processing CommandSet: '{currentSetName}'");

                    if (!IsFactionCommandSet(currentSetName))
                    {
                        System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer]   ✗ Skipped (not a faction set)");
                        currentSetName = null;
                        currentSlots.Clear();
                        return;
                    }

                    var factionName = ExtractFactionName(currentSetName);
                    System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer]   ✓ Faction extracted: '{currentSetName}' -> '{factionName}'");

                    if (!analysis.FactionSlots.TryGetValue(factionName, out var factionInfo))
                    {
                        factionInfo = new FactionCommandSetInfo { FactionName = factionName };
                        analysis.FactionSlots[factionName] = factionInfo;
                        System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer]   ✓ Created new faction entry for '{factionName}'");
                    }

                    var slots = ExtractSlots(currentSetName, factionName, currentSlots);
                    factionInfo.Slots.AddRange(slots);
                    System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer]   ✓ Added {slots.Count} slots to {factionName}");

                    currentSetName = null;
                    currentSlots.Clear();
                }

                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    if (line.StartsWith(";") || line.StartsWith("//"))
                        continue;

                    var headerMatch = CommandSetHeaderRegex.Match(line);
                    if (headerMatch.Success)
                    {
                        FlushCurrent();
                        currentSetName = headerMatch.Groups[1].Value;
                        currentSlots.Clear();
                        continue;
                    }

                    if (currentSetName == null)
                        continue;

                    if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
                    {
                        FlushCurrent();
                        continue;
                    }

                    var slotMatch = SlotRegex.Match(line);
                    if (slotMatch.Success)
                    {
                        var key = slotMatch.Groups[1].Value.Trim();
                        var value = slotMatch.Groups[2].Value.Trim();
                        currentSlots[key] = value;
                    }
                }

                FlushCurrent();

                analysis.TotalSlots = analysis.FactionSlots.Values.Sum(f => f.TotalSlots);
                analysis.OccupiedSlots = analysis.FactionSlots.Values.Sum(f => f.OccupiedSlots);

                // ✅ تسجيل النتائج
                System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] Analysis complete:");
                foreach (var (faction, info) in analysis.FactionSlots)
                {
                    System.Diagnostics.Debug.WriteLine($"  {faction}: {info.OccupiedSlots} occupied / {info.TotalSlots} total");
                    foreach (var slot in info.Slots)
                    {
                        if (slot.IsOccupied)
                            System.Diagnostics.Debug.WriteLine($"    Slot {slot.SlotNumber}: {slot.OccupiedBy}");
                    }
                }

                return analysis;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] Error: {ex.Message}");
                return analysis;
            }
        }

        public async Task<(bool hasSpace, CommandSetSlotInfo? availableSlot, string message)>
            CheckAvailableSlotAsync(string modPath, string factionName)
        {
            System.Diagnostics.Debug.WriteLine($"\n[CommandSetAnalyzer.CheckAvailableSlotAsync] === SLOT CHECK ===");
            System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] ModPath: {modPath}");
            System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] FactionName: {factionName}");

            var analysis = await AnalyzeModCommandSetsAsync(modPath);

            System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] Analysis returned {analysis.FactionSlots.Count} factions:");
            foreach (var (key, info) in analysis.FactionSlots)
            {
                System.Diagnostics.Debug.WriteLine($"  - {key}: {info.OccupiedSlots}/{info.TotalSlots}");
            }

            if (!analysis.FactionSlots.TryGetValue(factionName, out var factionInfo))
            {
                System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] ✗ Faction '{factionName}' not found directly");
                var normalized = NormalizeFactionKey(factionName);
                System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] Trying normalized key '{normalized}'");
                analysis.FactionSlots.TryGetValue(normalized, out factionInfo);
            }

            if (factionInfo == null)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] ✗✗✗ Faction '{factionName}' (and normalized) NOT FOUND");
                return (false, null, $"الفصيل '{factionName}' غير موجود في المود الهدف");
            }

            System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] ✓ Found faction '{factionInfo.FactionName}': {factionInfo.OccupiedSlots}/{factionInfo.TotalSlots}");

            var availableSlot = factionInfo.GetFirstAvailableSlot();

            if (availableSlot == null)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] ✗ No available slots");
                return (false, null,
                    $"⚠ جميع الـ slots ممتلئة للفصيل {factionInfo.FactionName} ({factionInfo.OccupiedSlots}/{factionInfo.TotalSlots})");
            }

            System.Diagnostics.Debug.WriteLine($"[CommandSetAnalyzer] ✓ Available slot found: #{availableSlot.SlotNumber}");
            return (true, availableSlot,
                $"✓ يوجد {factionInfo.AvailableSlots} مكان متاح في {factionInfo.FactionName}");
        }

        private static string NormalizeFactionKey(string faction)
            => ZeroHourStudio.Domain.ValueObjects.FactionName.NormalizeFactionKey(faction);

        private static bool IsFactionCommandSet(string setName)
        {
            // قائمة سوداء — أنماط ليست فصائل (بدلاً من قائمة بيضاء ثابتة)
            var excludePatterns = new[]
            {
                "Observer", "SkirmishMenu", "SpecialAbility",
                "Beacon", "Structure_", "Default"
            };

            if (excludePatterns.Any(p =>
                setName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                return false;

            // قبول أي CommandSet يحتوي اسم ذو معنى (أطول من "CommandSet")
            var cleaned = setName.Replace("CommandSet_", "", StringComparison.OrdinalIgnoreCase)
                                 .Replace("CommandSet", "", StringComparison.OrdinalIgnoreCase);
            return cleaned.Length > 0;
        }

        private static string ExtractFactionName(string setName)
        {
            var withoutPrefix = setName.Replace("CommandSet_", "", StringComparison.OrdinalIgnoreCase)
                .Replace("CommandSet", "", StringComparison.OrdinalIgnoreCase)
                .Trim('_');

            // Dynamic CamelCase extraction — أول token هو الفصيل
            // "ChinaBarracks" → ["China","Barracks"] → "China"
            // "GLAVehicleRecyclerChinaTierTwo" → ["GLA","Vehicle",...] → "GLA"
            // "AmericaWarFactory" → ["America","War","Factory"] → "America"
            var tokens = Regex.Split(withoutPrefix,
                @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|_");

            if (tokens.Length == 0 || string.IsNullOrWhiteSpace(tokens[0]))
                return "Unknown";

            var faction = tokens[0]; // أول token = الفصيل

            System.Diagnostics.Debug.WriteLine(
                $"[ExtractFactionName] '{setName}' → tokens=[{string.Join(",", tokens)}] → faction='{faction}'");

            return faction;
        }

        private static List<CommandSetSlotInfo> ExtractSlots(
            string setName,
            string factionName,
            Dictionary<string, string> setData)
        {
            var slots = new List<CommandSetSlotInfo>();

            for (int i = 1; i <= 14; i++)
            {
                var slotKey = i.ToString();
                setData.TryGetValue(slotKey, out var slotValue);
                slotValue ??= string.Empty;

                var isOccupied = !string.IsNullOrWhiteSpace(slotValue) &&
                                 !slotValue.Equals("NONE", StringComparison.OrdinalIgnoreCase);

                slots.Add(new CommandSetSlotInfo
                {
                    CommandSetName = setName,
                    FactionName = factionName,
                    SlotNumber = i,
                    IsOccupied = isOccupied,
                    OccupiedBy = isOccupied ? slotValue : null
                });
            }

            return slots;
        }
    }
}
