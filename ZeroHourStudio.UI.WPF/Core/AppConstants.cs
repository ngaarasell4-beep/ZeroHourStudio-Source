using System.Windows;

namespace ZeroHourStudio.UI.WPF.Core
{
    /// <summary>
    /// ثوابت وإعدادات التطبيق
    /// </summary>
    public static class AppConstants
    {
        // ============================================================
        // Application Info
        // ============================================================

        /// <summary>
        /// اسم التطبيق
        /// </summary>
        public const string ApplicationName = "ZeroHour Studio V2";

        /// <summary>
        /// إصدار التطبيق
        /// </summary>
        public const string ApplicationVersion = "2.0.0";

        /// <summary>
        /// الشركة المطورة
        /// </summary>
        public const string CompanyName = "ZeroHour Development";

        // ============================================================
        // UI Configuration
        // ============================================================

        /// <summary>
        /// حد أقصى للوحدات المعروضة في القائمة
        /// </summary>
        public const int MaxUnitsDisplayed = int.MaxValue;

        /// <summary>
        /// حد أقصى للتنبيهات المحفوظة
        /// </summary>
        public const int MaxNotificationsStored = 50;

        /// <summary>
        /// عمق أقصى لشجرة التبعات
        /// </summary>
        public const int MaxDependencyTreeDepth = 3;

        /// <summary>
        /// حد أقصى لعدد العقد المعروضة في الشجرة
        /// </summary>
        public const int MaxDependencyNodesDisplayed = 20;

        // ============================================================
        // Performance Settings
        // ============================================================

        /// <summary>
        /// Timeout لعملية البحث (بـ milliseconds)
        /// </summary>
        public const int SearchTimeoutMs = 5000;

        /// <summary>
        /// Timeout لعملية التحميل (بـ milliseconds)
        /// </summary>
        public const int LoadTimeoutMs = 30000;

        /// <summary>
        /// Timeout لعملية النقل (بـ milliseconds)
        /// </summary>
        public const int TransferTimeoutMs = 60000;

        // ============================================================
        // Validation Rules
        /// </summary>

        /// <summary>
        /// الحد الأدنى لطول اسم الوحدة
        /// </summary>
        public const int MinUnitNameLength = 1;

        /// <summary>
        /// الحد الأقصى لطول اسم الوحدة
        /// </summary>
        public const int MaxUnitNameLength = 256;

        /// <summary>
        /// الحد الأدنى لنسبة اكتمال الوحدة للسماح بالنقل
        /// </summary>
        public const int MinCompletionPercentageForTransfer = 100;

        // ============================================================
        // Color Codes (Hex)
        /// </summary>

        /// <summary>
        /// لون النجاح (أخضر)
        /// </summary>
        public const string ColorSuccess = "#00AA00";

        /// <summary>
        /// لون التحذير (برتقالي)
        /// </summary>
        public const string ColorWarning = "#FFAA00";

        /// <summary>
        /// لون الخطأ (أحمر)
        /// </summary>
        public const string ColorError = "#DD0000";

        /// <summary>
        /// لون خرج حرج (أحمر داكن)
        /// </summary>
        public const string ColorCritical = "#990000";

        /// <summary>
        /// لون المعلومات (أزرق)
        /// </summary>
        public const string ColorInfo = "#0066CC";

        /// <summary>
        /// لون غير معروف (رمادي)
        /// </summary>
        public const string ColorUnknown = "#808080";

        // ============================================================
        // Archive Settings
        // ============================================================

        /// <summary>
        /// حد أقصى للبحث في الأرشيف
        /// </summary>
        public const long MaxArchiveSearchSize = 10L * 1024 * 1024 * 1024; // 10 GB

        /// <summary>
        /// الامتدادات المدعومة للملفات
        /// </summary>
        public static readonly string[] SupportedFileExtensions = new[]
        {
            ".w3d",  // 3D Models
            ".dds",  // Textures
            ".tga",  // Textures
            ".wav",  // Audio
            ".mp3",  // Audio
            ".w3x",  // Visual Effects
            ".ini",  // Configuration
        };

        // ============================================================
        // Cache Settings
        // ============================================================

        /// <summary>
        /// مدة صلاحية الـ Cache (بـ minutes)
        /// </summary>
        public const int CacheExpirationMinutes = 60;

        /// <summary>
        /// حد أقصى لحجم الـ Cache (بـ entries)
        /// </summary>
        public const int MaxCacheEntries = 500;

        // ============================================================
        // Paths
        // ============================================================

        /// <summary>
        /// مجلد البيانات للمستخدم
        /// </summary>
        public static readonly string UserDataFolder = 
            System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                ApplicationName);

        /// <summary>
        /// مجلد السجلات (Logs)
        /// </summary>
        public static readonly string LogsFolder = 
            System.IO.Path.Combine(UserDataFolder, "Logs");

        /// <summary>
        /// مجلد ذاكرة التخزين المؤقت (Cache)
        /// </summary>
        public static readonly string CacheFolder = 
            System.IO.Path.Combine(UserDataFolder, "Cache");

        // ============================================================
        // Window Sizes
        // ============================================================

        /// <summary>
        /// الحد الأدنى لعرض النافذة
        /// </summary>
        public static readonly int MinWindowWidth = 800;

        /// <summary>
        /// الحد الأدنى لارتفاع النافذة
        /// </summary>
        public static readonly int MinWindowHeight = 600;

        /// <summary>
        /// العرض الافتراضي للنافذة
        /// </summary>
        public static readonly int DefaultWindowWidth = 1200;

        /// <summary>
        /// الارتفاع الافتراضي للنافذة
        /// </summary>
        public static readonly int DefaultWindowHeight = 800;
    }
}
