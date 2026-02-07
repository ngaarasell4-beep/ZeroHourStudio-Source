namespace ZeroHourStudio.Infrastructure;

/// <summary>
/// دليل الاستخدام الشامل لطبقة Infrastructure
/// 
/// المكونات الرئيسية:
/// 1. BigArchiveManager: قراءة ملفات BIG مع Mounting Priority
/// 2. SAGE_IniParser: تحليل ملفات INI مع استخراج الكائنات الكاملة
/// 3. SmartNormalization: تطبيع أسماء الفصائل مع Fuzzy Matching
/// 4. ArchiveProcessingService: خدمة موحدة تجمع كل المكونات
/// 5. CacheManager: تخزين مؤقت لتحسين الأداء
/// </summary>
internal static class UsageExamples
{
    /// <summary>
    /// مثال 1: قراءة ملف BIG
    /// </summary>
    public static async Task Example1_ReadBigArchive()
    {
        using var manager = new Archives.BigArchiveManager("path/to/archive.big");
        await manager.LoadAsync();

        // الحصول على قائمة الملفات
        var files = manager.GetFileList();

        // استخراج ملف محدد
        byte[] fileData = await manager.ExtractFileAsync("filename.ini");

        // التحقق من وجود ملف
        bool exists = manager.FileExists("test.w3d");
    }

    /// <summary>
    /// مثال 2: تحليل ملف INI
    /// </summary>
    public static async Task Example2_ParseIniFile()
    {
        var parser = new Parsers.SAGE_IniParser();
        await parser.ParseAsync("path/to/sage.ini");

        // الحصول على قيمة
        string? buildCost = parser.GetValue("UnitName", "BuildCost");

        // استخراج كائن كامل
        string? objectCode = parser.ExtractObject("UnitName");

        // الحصول على جميع الأقسام
        var sections = parser.GetSections();

        // الحصول على جميع الكائنات
        var allObjects = parser.GetFullObjects();
    }

    /// <summary>
    /// مثال 3: تطبيع أسماء الفصائل (حل مشكلة البحث)
    /// </summary>
    public static void Example3_NormalizeFactionName()
    {
        var normalizer = new Normalization.FactionNameNormalizer();

        // تطبيع بسيط
        var normalized = normalizer.Normalize("China Nuke General");
        // النتيجة: FactionName("FactionChinaNukeGeneral")

        // البحث عن الفصيل الأقرب
        var faction = normalizer.TryFindClosestFaction("ChiNa NuKe");
        // سيجد: FactionChinaNukeGeneral (Fuzzy Matching)

        // الحصول على قائمة الفصائل المعروفة
        var knownFactions = normalizer.GetKnownFactionNames();

        // تسجيل فصيل جديد
        normalizer.RegisterFaction("FactionCustom", "custom", "new faction");
    }

    /// <summary>
    /// مثال 4: الخدمة الموحدة (الخيار الأفضل)
    /// </summary>
    public static async Task Example4_ArchiveProcessingService()
    {
        using var service = new Services.ArchiveProcessingService();

        // تحميل الأرشيف وملف INI
        await service.LoadArchiveAsync("path/to/game.big");
        await service.LoadIniFileAsync("path/to/unit.ini");

        // الحصول على قائمة الملفات
        var files = service.GetLoadedArchiveFiles();

        // استخراج ملف
        byte[] fileData = await service.ExtractFileFromArchiveAsync("texture.dds");

        // الحصول على قيمة من INI
        string? unitName = service.GetIniValue("Section", "Key");

        // تطبيع الفصيل
        string normalized = service.NormalizeFactionName("china nuke general");

        // البحث عن فصيل
        var faction = service.FindClosestFaction("usa");
    }

    /// <summary>
    /// مثال 5: التخزين المؤقت (Caching)
    /// </summary>
    public static void Example5_Caching()
    {
        var cache = new Caching.CacheManager(TimeSpan.FromMinutes(30));

        // تخزين بيانات
        byte[] data = new byte[1024];
        cache.CacheFile("model.w3d", data);

        // استرجاع البيانات
        var cached = cache.GetCachedFile("model.w3d");

        // التحقق من وجود البيانات
        bool isCached = cache.HasCachedFile("model.w3d");

        // تنظيف العناصر المنتهية الصلاحية
        cache.RemoveExpiredEntries();

        // الحصول على حجم الذاكرة المؤقتة
        long cacheSize = cache.GetCacheSize();
    }

    /// <summary>
    /// مثال 6: المساعدات والتحقق
    /// </summary>
    public static void Example6_Helpers()
    {
        // التحقق من صحة اسم الوحدة
        bool isValid = Helpers.ValidationHelpers.IsValidUnitName("UnitName_01");

        // تطبيع مسار الملف
        string normalized = Helpers.DataProcessingHelpers.NormalizeFilePath(@"C:\Game\Textures\unit.dds");

        // التحقق من ملف DDS
        bool isDds = Helpers.DataProcessingHelpers.IsValidDdsFile("texture.dds");

        // التحقق من ملف W3D
        bool isW3d = Helpers.DataProcessingHelpers.IsValidW3dFile("model.w3d");

        // محاولة تحليل عدد صحيح
        if (Helpers.ValidationHelpers.TryParseInt("100", out int result))
        {
            // النتيجة: result = 100
        }
    }

    /// <summary>
    /// مثال 7: التسجيل (Logging)
    /// </summary>
    public static void Example7_Logging()
    {
        var logger = new Logging.SimpleLogger(consoleOutput: true);

        logger.LogInfo("تم تحميل الأرشيف بنجاح");
        logger.LogWarning("هذا تحذير");
        logger.LogError("خطأ:", new Exception("تفاصيل الخطأ"));
        logger.LogDebug("رسالة تصحيح");

        // الحصول على السجلات
        var logs = logger.GetLogs();
    }
}
