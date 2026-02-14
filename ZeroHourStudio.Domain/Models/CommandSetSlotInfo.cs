using System.Collections.Generic;
using System.Linq;

namespace ZeroHourStudio.Domain.Models
{
    public class CommandSetSlotInfo
    {
        public string FactionName { get; set; } = string.Empty;
        public int SlotNumber { get; set; }
        public bool IsOccupied { get; set; }
        public string? OccupiedBy { get; set; }
        public string CommandSetName { get; set; } = string.Empty;
    }

    public class CommandSetAnalysis
    {
        public string ModPath { get; set; } = string.Empty;
        public Dictionary<string, FactionCommandSetInfo> FactionSlots { get; set; } = new();
        public int TotalSlots { get; set; }
        public int OccupiedSlots { get; set; }
        public int AvailableSlots => TotalSlots - OccupiedSlots;
    }

    public class FactionCommandSetInfo
    {
        public string FactionName { get; set; } = string.Empty;
        public List<CommandSetSlotInfo> Slots { get; set; } = new();
        public int TotalSlots => Slots.Count;
        public int AvailableSlots => Slots.Count(s => !s.IsOccupied);
        public int OccupiedSlots => Slots.Count(s => s.IsOccupied);
        public CommandSetSlotInfo? GetFirstAvailableSlot()
        {
            return Slots.FirstOrDefault(s => !s.IsOccupied);
        }
    }
}
