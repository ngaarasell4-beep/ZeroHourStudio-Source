using ZeroHourStudio.Domain.Models;
using ZeroHourStudio.Infrastructure.Parsers;

namespace ZeroHourStudio.Infrastructure.Services;

/// <summary>
/// محلل أزرار الأوامر - يوفر تحليلاً تفصيلياً لأزرار CommandSet مع معلومات الأيقونات والنوع
/// </summary>
public class CommandButtonAnalyzer
{
    private readonly CommandSetAnalyzer _commandSetAnalyzer;

    public CommandButtonAnalyzer(CommandSetAnalyzer commandSetAnalyzer)
    {
        _commandSetAnalyzer = commandSetAnalyzer ?? throw new ArgumentNullException(nameof(commandSetAnalyzer));
    }

    /// <summary>
    /// تحليل أزرار CommandSet لفصيل معين وإرجاع معلومات تفصيلية
    /// هام: يتعامل مع CommandSet المحدد فقط، وليس جميع CommandSets للفصيل
    /// </summary>
    public async Task<CommandButtonAnalysis> AnalyzeCommandSet(string modPath, string factionName, string? specificCommandSetName = null)
    {
        var result = new CommandButtonAnalysis
        {
            FactionName = factionName
        };

        try
        {
            System.Diagnostics.Debug.WriteLine($"\n[CommandButtonAnalyzer.AnalyzeCommandSet] === ANALYSIS START ===");
            System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] ModPath: {modPath}");
            System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] FactionName: {factionName}");

            var analysis = await _commandSetAnalyzer.AnalyzeModCommandSetsAsync(modPath);

            System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] CommandSetAnalyzer returned {analysis.FactionSlots.Count} factions");

            FactionCommandSetInfo? factionInfo = null;
            if (!analysis.FactionSlots.TryGetValue(factionName, out factionInfo))
            {
                System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] ✗ Faction '{factionName}' not found directly");
                // Try normalized key
                var normalized = NormalizeFactionKey(factionName);
                System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] Trying normalized: '{normalized}'");
                analysis.FactionSlots.TryGetValue(normalized, out factionInfo);
            }

            if (factionInfo == null)
            {
                System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] ✗✗ No faction info found");
                System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] Available factions: {string.Join(", ", analysis.FactionSlots.Keys)}");
                System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] Creating empty 12-slot layout as fallback");
                result.CommandSetName = $"{factionName}CommandCenter";
                result.TotalSlots = 12;
                result.OccupiedSlots = 0;
                for (int i = 1; i <= 12; i++)
                {
                    result.Buttons.Add(new CommandButtonSlot
                    {
                        SlotNumber = i,
                        IsEmpty = true,
                        Type = ButtonType.Empty,
                        Description = "فارغ"
                    });
                }
                System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] === FALLBACK: 12 empty slots created ===");
                return result;
            }

            // Build detailed button info from slot data
            System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] ✓ Found faction: {factionInfo.FactionName}");

            // 🔴 CRITICAL FIX: Get slots ONLY from the specific CommandSet, not all CommandSets for faction
            var slotsToUse = factionInfo.Slots;
            if (!string.IsNullOrWhiteSpace(specificCommandSetName))
            {
                System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] Filtering to specific CommandSet: '{specificCommandSetName}'");
                slotsToUse = factionInfo.Slots
                    .Where(s => s.CommandSetName == specificCommandSetName)
                    .ToList();

                if (slotsToUse.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] ⚠️ No slots found for CommandSet '{specificCommandSetName}', using first 14 from first CommandSet");
                    // 再び fallback: 使用第一個 CommandSet 的前 14 個 slots
                    var firstCommandSet = factionInfo.Slots.FirstOrDefault()?.CommandSetName;
                    if (!string.IsNullOrWhiteSpace(firstCommandSet))
                    {
                        slotsToUse = factionInfo.Slots
                            .Where(s => s.CommandSetName == firstCommandSet)
                            .Take(14)
                            .ToList();
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] No specific CommandSet requested, taking first 14 slots");
                slotsToUse = factionInfo.Slots.Take(14).ToList();
            }

            result.CommandSetName = slotsToUse.FirstOrDefault()?.CommandSetName ?? $"{factionName}CommandCenter";
            result.TotalSlots = slotsToUse.Count;
            result.OccupiedSlots = slotsToUse.Count(s => s.IsOccupied);
            System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] CommandSetName: {result.CommandSetName}");
            System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] Total Slots: {result.TotalSlots}, Occupied: {result.OccupiedSlots}");

            foreach (var slot in slotsToUse)
            {
                var button = new CommandButtonSlot
                {
                    SlotNumber = slot.SlotNumber,
                    IsEmpty = !slot.IsOccupied,
                    OccupiedBy = slot.OccupiedBy,
                    Type = slot.IsOccupied ? ClassifyButton(slot.OccupiedBy) : ButtonType.Empty,
                    Description = slot.IsOccupied
                        ? $"Slot {slot.SlotNumber}: {slot.OccupiedBy}"
                        : $"Slot {slot.SlotNumber}: فارغ ✓"
                };

                result.Buttons.Add(button);
                System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer]   Slot {slot.SlotNumber}: {(slot.IsOccupied ? $"OCCUPIED ({slot.OccupiedBy})" : "EMPTY")}");
            }

            // If fewer than 14 slots found (CommandSet should have 14), pad to match
            while (result.Buttons.Count < 14)
            {
                var nextSlot = result.Buttons.Count + 1;
                result.Buttons.Add(new CommandButtonSlot
                {
                    SlotNumber = nextSlot,
                    IsEmpty = true,
                    Type = ButtonType.Empty,
                    Description = $"Slot {nextSlot}: فارغ ✓"
                });
                result.TotalSlots = result.Buttons.Count;
            }

            System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] === ANALYSIS COMPLETE ===");
            System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] Final: {result.EmptySlots} empty / {result.TotalSlots} total");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] ERROR analyzing '{factionName}': {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// البحث عن أول مكان فارغ
    /// </summary>
    public async Task<(bool hasSpace, int? emptySlot)> FindEmptySlot(string modPath, string commandSetName)
    {
        try
        {
            // Use a simplified approach: parse the faction from commandSetName
            var analysis = await _commandSetAnalyzer.AnalyzeModCommandSetsAsync(modPath);
            foreach (var (_, factionInfo) in analysis.FactionSlots)
            {
                var firstAvailable = factionInfo.GetFirstAvailableSlot();
                if (firstAvailable != null)
                    return (true, firstAvailable.SlotNumber);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CommandButtonAnalyzer] FindEmptySlot ERROR: {ex.Message}");
        }

        return (false, null);
    }

    /// <summary>
    /// الحصول على الأزرار المشغولة فقط
    /// </summary>
    public async Task<List<CommandButtonSlot>> GetOccupiedButtons(string modPath, string factionName)
    {
        var analysis = await AnalyzeCommandSet(modPath, factionName);
        return analysis.Buttons.Where(b => !b.IsEmpty).ToList();
    }

    /// <summary>
    /// تصنيف نوع الزر بناءً على اسمه
    /// </summary>
    private static ButtonType ClassifyButton(string? buttonName)
    {
        if (string.IsNullOrWhiteSpace(buttonName))
            return ButtonType.Empty;

        if (buttonName.Contains("Upgrade", StringComparison.OrdinalIgnoreCase))
            return ButtonType.Upgrade;
        if (buttonName.Contains("SpecialPower", StringComparison.OrdinalIgnoreCase) ||
            buttonName.Contains("Special_Power", StringComparison.OrdinalIgnoreCase))
            return ButtonType.SpecialPower;
        if (buttonName.Contains("Command_", StringComparison.OrdinalIgnoreCase) &&
            !buttonName.Contains("Command_Construct", StringComparison.OrdinalIgnoreCase))
            return ButtonType.Command;

        return ButtonType.Unit;
    }

    private static string NormalizeFactionKey(string faction)
        => ZeroHourStudio.Domain.ValueObjects.FactionName.NormalizeFactionKey(faction);
}
