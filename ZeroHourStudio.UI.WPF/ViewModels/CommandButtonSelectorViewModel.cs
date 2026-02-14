using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ZeroHourStudio.Domain.Models;

namespace ZeroHourStudio.UI.WPF.ViewModels
{
    /// <summary>
    /// ViewModel لنافذة اختيار موقع الزر في CommandSet
    /// </summary>
    public class CommandButtonSelectorViewModel : INotifyPropertyChanged
    {
        private string _unitName = string.Empty;
        private string _factionName = string.Empty;
        private string _commandSetName = string.Empty;
        private bool _hasEmptySlot;
        private CommandButtonSlot? _selectedButton;
        private CommandButtonSlot? _selectedButtonToReplace;
        private bool _userConfirmed;

        public string UnitName
        {
            get => _unitName;
            set { _unitName = value; OnPropertyChanged(); }
        }

        public string FactionName
        {
            get => _factionName;
            set { _factionName = value; OnPropertyChanged(); }
        }

        public string CommandSetName
        {
            get => _commandSetName;
            set { _commandSetName = value; OnPropertyChanged(); }
        }

        public bool HasEmptySlot
        {
            get => _hasEmptySlot;
            set { _hasEmptySlot = value; OnPropertyChanged(); OnPropertyChanged(nameof(NoEmptySlot)); }
        }

        public bool NoEmptySlot => !HasEmptySlot;

        public ObservableCollection<CommandButtonSlot> Buttons { get; set; } = new();

        public ObservableCollection<CommandButtonSlot> OccupiedButtons =>
            new(Buttons.Where(b => !b.IsEmpty));

        public CommandButtonSlot? SelectedButton
        {
            get => _selectedButton;
            set
            {
                _selectedButton = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        public CommandButtonSlot? SelectedButtonToReplace
        {
            get => _selectedButtonToReplace;
            set
            {
                _selectedButtonToReplace = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        public bool HasSelection => SelectedButton != null || SelectedButtonToReplace != null;

        public bool UserConfirmed
        {
            get => _userConfirmed;
            set { _userConfirmed = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Build selection result from current state
        /// </summary>
        public ButtonSelectionResult? GetSelectionResult()
        {
            // زر فارغ محدد
            if (SelectedButton != null)
            {
                return new ButtonSelectionResult
                {
                    SlotNumber = SelectedButton.SlotNumber,
                    ReplaceExisting = !SelectedButton.IsEmpty,
                    ReplacedButtonName = SelectedButton.IsEmpty ? null : SelectedButton.OccupiedBy
                };
            }

            // زر مشغول محدد للاستبدال
            if (SelectedButtonToReplace != null)
            {
                return new ButtonSelectionResult
                {
                    SlotNumber = SelectedButtonToReplace.SlotNumber,
                    ReplaceExisting = true,
                    ReplacedButtonName = SelectedButtonToReplace.OccupiedBy
                };
            }

            return null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
