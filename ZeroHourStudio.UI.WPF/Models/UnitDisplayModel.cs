using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.UI.WPF.Models
{
    /// <summary>
    /// ViewModel Model للـ Unit مع دعم WPF Binding والـ Status Colors
    /// </summary>
    public class UnitDisplayModel : INotifyPropertyChanged
    {
        private string _technicalName = string.Empty;
        private string _displayName = string.Empty;
        private string _faction = string.Empty;
        private UnitHealthStatus _healthStatus = UnitHealthStatus.Unknown;
        private int _completionPercentage = 0;
        private string _statusMessage = string.Empty;
        private bool _hasAllDependencies = false;
        private string _missingFiles = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// معرّف الوحدة الفني
        /// </summary>
        public string TechnicalName
        {
            get => _technicalName;
            set => SetProperty(ref _technicalName, value);
        }

        /// <summary>
        /// اسم الوحدة المعروض للمستخدم
        /// </summary>
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        /// <summary>
        /// الفصيلة المنتمية إليها
        /// </summary>
        public string Faction
        {
            get => _faction;
            set => SetProperty(ref _faction, value);
        }

        /// <summary>
        /// حالة اكتمال الوحدة (Complete, Partial, Incomplete, etc)
        /// </summary>
        public UnitHealthStatus HealthStatus
        {
            get => _healthStatus;
            set => SetProperty(ref _healthStatus, value);
        }

        /// <summary>
        /// نسبة الاكتمال (0-100)
        /// </summary>
        public int CompletionPercentage
        {
            get => _completionPercentage;
            set => SetProperty(ref _completionPercentage, value);
        }

        /// <summary>
        /// رسالة الحالة (مثل "مكتملة" أو "ناقصة W3D")
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// هل الوحدة تملك جميع التبعات؟
        /// </summary>
        public bool HasAllDependencies
        {
            get => _hasAllDependencies;
            set => SetProperty(ref _hasAllDependencies, value);
        }

        /// <summary>
        /// الملفات المفقودة (محدودة بحد أقصى من الأسطر)
        /// </summary>
        public string MissingFiles
        {
            get => _missingFiles;
            set => SetProperty(ref _missingFiles, value);
        }

        /// <summary>
        /// اللون المناسب لحالة الوحدة (للـ GUI)
        /// </summary>
        public string StatusColor
        {
            get
            {
                return HealthStatus switch
                {
                    UnitHealthStatus.Complete => "#00AA00", // أخضر
                    UnitHealthStatus.Partial => "#FFAA00",  // برتقالي
                    UnitHealthStatus.Incomplete => "#DD0000", // أحمر
                    UnitHealthStatus.Critical => "#990000",    // أحمر داكن
                    _ => "#808080" // رمادي
                };
            }
        }

        /// <summary>
        /// هل يمكن نقل هذه الوحدة؟
        /// </summary>
        public bool CanTransfer => HealthStatus == UnitHealthStatus.Complete;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual bool SetProperty<T>(
            ref T field,
            T value,
            [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);

            // تحديث الـ dependent properties
            if (propertyName == nameof(HealthStatus))
            {
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(CanTransfer));
            }

            return true;
        }
    }

    /// <summary>
    /// حالات صحة الوحدة
    /// </summary>
    public enum UnitHealthStatus
    {
        Unknown = 0,
        Incomplete = 1,    // ناقصة جداً
        Partial = 2,       // ناقصة قليلاً
        Complete = 3,      // مكتملة
        Critical = 4       // حرجة (أخطاء)
    }

    /// <summary>
    /// عقدة في شجرة التبعات (للـ TreeView)
    /// </summary>
    public class DependencyNodeDisplayModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _type = string.Empty;
        private string _status = string.Empty;
        private bool _isExpanded = false;
        private ObservableCollection<DependencyNodeDisplayModel> _children = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public ObservableCollection<DependencyNodeDisplayModel> Children
        {
            get => _children;
            set => SetProperty(ref _children, value);
        }

        public string StatusColor
        {
            get
            {
                return Status switch
                {
                    "Found" => "#00AA00",
                    "Missing" => "#DD0000",
                    "Invalid" => "#FF6600",
                    "NotVerified" => "#FFAA00",
                    _ => "#808080"
                };
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual bool SetProperty<T>(
            ref T field,
            T value,
            [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    /// <summary>
    /// نموذج التنبيه الأمني (Safety Notification)
    /// </summary>
    public class SafetyNotificationModel : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _message = string.Empty;
        private SafetyLevel _level = SafetyLevel.Info;
        private DateTime _timestamp = DateTime.Now;
        private bool _isVisible = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public SafetyLevel Level
        {
            get => _level;
            set => SetProperty(ref _level, value);
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public string LevelColor
        {
            get
            {
                return Level switch
                {
                    SafetyLevel.Critical => "#990000",
                    SafetyLevel.Error => "#DD0000",
                    SafetyLevel.Warning => "#FFAA00",
                    SafetyLevel.Info => "#0066CC",
                    _ => "#808080"
                };
            }
        }

        public string LevelIcon
        {
            get
            {
                return Level switch
                {
                    SafetyLevel.Critical => "⚠️ حرج",
                    SafetyLevel.Error => "❌ خطأ",
                    SafetyLevel.Warning => "⚡ تحذير",
                    SafetyLevel.Info => "ℹ️ معلومة",
                    _ => "❓ غير معروف"
                };
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual bool SetProperty<T>(
            ref T field,
            T value,
            [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    /// <summary>
    /// مستويات التنبيهات الأمنية
    /// </summary>
    public enum SafetyLevel
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Critical = 3
    }
}
