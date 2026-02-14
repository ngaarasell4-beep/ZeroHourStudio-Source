using System.Collections.Generic;

namespace ZeroHourStudio.Domain.Models
{
    /// <summary>
    /// نوع الزر في CommandSet
    /// </summary>
    public enum ButtonType
    {
        Empty,
        Unit,
        Upgrade,
        SpecialPower,
        Command
    }

    /// <summary>
    /// معلومات زر واحد في CommandSet
    /// </summary>
    public class CommandButtonSlot : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        private object? _iconSource; // BitmapSource — object to avoid WPF dependency in Domain

        public int SlotNumber { get; set; }
        public bool IsEmpty { get; set; }
        public string? OccupiedBy { get; set; }
        public string? Icon { get; set; }
        public string? Description { get; set; }
        public ButtonType Type { get; set; }

        /// <summary>اسم ButtonImage من INI (مثل "SNBattlemaster")</summary>
        public string? ButtonImageName { get; set; }

        /// <summary>نوع الأمر (مثل "DO_PRODUCE")</summary>
        public string? Command { get; set; }

        /// <summary>أيقونة محملة — object لتجنب تبعية WPF في Domain</summary>
        public object? IconSource
        {
            get => _iconSource;
            set
            {
                _iconSource = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IconSource)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasIcon)));
            }
        }

        /// <summary>هل يملك أيقونة محملة</summary>
        public bool HasIcon => IconSource != null;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// نتيجة تحليل أزرار CommandSet لفصيل معين
    /// </summary>
    public class CommandButtonAnalysis
    {
        public string FactionName { get; set; } = string.Empty;
        public string CommandSetName { get; set; } = string.Empty;
        public int TotalSlots { get; set; }
        public int OccupiedSlots { get; set; }
        public int EmptySlots => TotalSlots - OccupiedSlots;
        public List<CommandButtonSlot> Buttons { get; set; } = new();
    }

    /// <summary>
    /// نتيجة اختيار المستخدم لموقع الزر
    /// </summary>
    public class ButtonSelectionResult
    {
        public int SlotNumber { get; set; }
        public bool ReplaceExisting { get; set; }
        public string? ReplacedButtonName { get; set; }
    }
}
