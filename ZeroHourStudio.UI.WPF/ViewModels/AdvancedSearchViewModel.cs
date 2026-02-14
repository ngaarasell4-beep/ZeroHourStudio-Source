using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using ZeroHourStudio.UI.WPF.Core;

namespace ZeroHourStudio.UI.WPF.ViewModels
{
    public class AdvancedSearchViewModel : ViewModelBase
    {
        private bool _searchByName = true;
        private bool _searchByFaction = true;
        private bool _searchByWeaponType = false;
        private bool _searchByModel = false;
        private string _weaponTypeFilter = string.Empty;
        private string _factionFilter = string.Empty;
        private string _nameFilter = string.Empty;
        private ObservableCollection<string> _availableFactions = new();
        private ObservableCollection<string> _availableWeaponTypes = new();
        private bool _showOnlyComplete = false;
        private bool _showOnlyIncomplete = false;

        public bool SearchByName
        {
            get => _searchByName;
            set => SetProperty(ref _searchByName, value);
        }

        public bool SearchByFaction
        {
            get => _searchByFaction;
            set => SetProperty(ref _searchByFaction, value);
        }

        public bool SearchByWeaponType
        {
            get => _searchByWeaponType;
            set => SetProperty(ref _searchByWeaponType, value);
        }

        public bool SearchByModel
        {
            get => _searchByModel;
            set => SetProperty(ref _searchByModel, value);
        }

        public string WeaponTypeFilter
        {
            get => _weaponTypeFilter;
            set => SetProperty(ref _weaponTypeFilter, value);
        }

        public string FactionFilter
        {
            get => _factionFilter;
            set => SetProperty(ref _factionFilter, value);
        }

        public string NameFilter
        {
            get => _nameFilter;
            set => SetProperty(ref _nameFilter, value);
        }

        public ObservableCollection<string> AvailableFactions
        {
            get => _availableFactions;
            set => SetProperty(ref _availableFactions, value);
        }

        public ObservableCollection<string> AvailableWeaponTypes
        {
            get => _availableWeaponTypes;
            set => SetProperty(ref _availableWeaponTypes, value);
        }

        public bool ShowOnlyComplete
        {
            get => _showOnlyComplete;
            set => SetProperty(ref _showOnlyComplete, value);
        }

        public bool ShowOnlyIncomplete
        {
            get => _showOnlyIncomplete;
            set => SetProperty(ref _showOnlyIncomplete, value);
        }

        public AdvancedSearchViewModel()
        {
            // Factions populated dynamically from loaded mod — no hardcoded list
            AvailableFactions = new ObservableCollection<string>();

            AvailableWeaponTypes = new ObservableCollection<string>
            {
                "Primary", "Secondary", "Tertiary", "WeaponA", "WeaponB", "WeaponC",
                "AntiAir", "AntiGround", "AntiBuilding", "Sniper", "Flame", "Explosive",
                "MachineGun", "Cannon", "Rocket", "Missile", "Nuclear", "Toxin"
            };
        }

        /// <summary>
        /// تحديث قائمة الفصائل من بيانات المود الحية
        /// </summary>
        public void UpdateFactions(IEnumerable<string> factions)
        {
            AvailableFactions.Clear();
            foreach (var f in factions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                AvailableFactions.Add(f);
        }
    }
}