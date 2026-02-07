![ZeroHour Studio V2 - Phase 2](https://img.shields.io/badge/Phase-2-blue)

# ZeroHour Studio V2 - المرحلة الثانية: طبقة البنية التحتية (Infrastructure)

## 📋 نظرة عامة

تم بناء طبقة Infrastructure الشاملة بما يتضمن:
- قراءة ملفات BIG مع نظام الأولوية (Mounting Priority)
- تحليل ملفات INI المتقدم مع استخراج الكائنات الكاملة  
- نظام SmartNormalization لحل مشكلة البحث عن الفصائل
- خدمات مساعدة للتحقق والتخزين المؤقت

---

## 🏗️ البنية المعمارية

### 1️⃣ **Archives** - إدارة ملفات البيانات
**ملف:** `BigArchiveManager.cs`

- ✅ يستخدم `BinaryReader` و `MemoryMappedFile` للأداء العالي
- ✅ نظام Mounting Priority: الملفات التي تبدأ بـ `!!` لها الأولوية
- ✅ فهرسة سريعة لملفات الأرشيف
- ✅ تحميل آمن للملفات الضخمة

**المميزات:**
```csharp
// استخراج ملف من الأرشيف
byte[] data = await manager.ExtractFileAsync("filename.dds");

// التحقق من وجود ملف
bool exists = manager.FileExists("model.w3d");

// الحصول على معلومات الملف
var entry = manager.GetFileInfo("texture.dds");
```

---

### 2️⃣ **Parsers** - تحليل ملفات INI
**ملف:** `SAGE_IniParser.cs`

- ✅ غير حساس لحالة الأحرف (Case-Insensitive)
- ✅ يتجاهل التعليقات التي تبدأ بـ `;` أو `//`
- ✅ يستخدم `ReadOnlySpan<char>` للأداء العالي
- ✅ استخراج كود الكائنات الكامل (Object ... End)

**المميزات:**
```csharp
var parser = new SAGE_IniParser();
await parser.ParseAsync("unit.ini");

// استخراج كائن كامل
string objectCode = parser.ExtractObject("UnitName");

// الحصول على قيمة
string value = parser.GetValue("Section", "Key");

// الحصول على جميع الأقسام والمفاتيح
var sections = parser.GetSections();
var keys = parser.GetKeys("Section");
```

---

### 3️⃣ **Normalization** - تطبيع أسماء الفصائل
**ملفات:** `SmartNormalization.cs`, `FactionNameNormalizer.cs`

#### المشكلة الأصلية:
```
البحث عن "China Nuke General" فشل لأن النظام يتوقع: "FactionChinaNukeGeneral"
```

#### الحل:
- ✅ **التطبيع التلقائي**: إزالة المسافات → تحويل لأحرف صغيرة → إضافة بادئة
- ✅ **Fuzzy Matching**: مطابقة تقريبية باستخدام Levenshtein Distance
- ✅ **10 فصائل معروفة** محفوظة في النظام

**المميزات:**
```csharp
var normalizer = new FactionNameNormalizer();

// تطبيع بسيط
var factionName = normalizer.Normalize("China Nuke General");
// ✅ النتيجة: FactionChinaNukeGeneral

// Fuzzy Matching - يجد الأقرب حتى مع أخطاء إملائية
var faction = normalizer.TryFindClosestFaction("ChiNa NuKe");
// ✅ يجد: FactionChinaNukeGeneral

// الفصائل المعروفة:
// - USA
// - ChinaNuke (النواة)
// - ChinaInf (المشاة)
// - GLAInf (مشاة القذاف)
// - GLAAir (الجو)
// - GLATerror (الإرهاب)
// - SuperWeapon
// - KingRaptor
// - Tower
// - Skirmish
```

---

### 4️⃣ **Implementations** - تنفيذ الواجهات
**ملفات:** `BigFileReader.cs`, `IniParser.cs`

تنفيذ كامل للواجهات المعرّفة في Application:
- ✅ `IBigFileReader` - قراءة أرشيفات اللعبة
- ✅ `IIniParser` - تحليل ملفات INI

```csharp
// تنفيذ IBigFileReader
var reader = new BigFileReader("path/to/archive.big");
var files = await reader.ReadAsync("archive.big");
await reader.ExtractAsync("archive.big", "file.ini", "output.ini");

// تنفيذ IIniParser  
var parser = new IniParser();
var data = await parser.ParseAsync("unit.ini");
var value = await parser.GetValueAsync("unit.ini", "Section", "Key");
```

---

### 5️⃣ **Services** - الخدمات الموحدة
**ملف:** `ArchiveProcessingService.cs`

خدمة شاملة تجمع جميع المكونات في واجهة موحدة:

```csharp
using var service = new ArchiveProcessingService();

// تحميل البيانات
await service.LoadArchiveAsync("game.big");
await service.LoadIniFileAsync("unit.ini");

// استخدام الخدمات المختلفة
var files = service.GetLoadedArchiveFiles();
byte[] fileData = await service.ExtractFileFromArchiveAsync("file.dds");
string value = service.GetIniValue("Section", "Key");
string normalized = service.NormalizeFactionName("china nuke general");
```

---

### 6️⃣ **Helpers** - الدوال المساعدة
**ملفات:** `DataProcessingHelpers.cs`, `ValidationHelpers.cs`

#### DataProcessingHelpers:
- تطبيع مسارات الملفات
- التحقق من صحة ملفات DDS و W3D
- حساب أحجام الملفات

#### ValidationHelpers:
- التحقق من أسماء الوحدات
- تحليل آمن للأرقام والقيم المنطقية
- التحقق من صحة الملفات

```csharp
// التحقق من صحة ملف DDS
bool isDds = DataProcessingHelpers.IsValidDdsFile("texture.dds");

// تطبيع مسار الملف
string normalized = DataProcessingHelpers.NormalizeFilePath(@"C:\Game\Textures\unit.dds");

// تحليل آمن
if (ValidationHelpers.TryParseInt("100", out int cost))
{
    // cost = 100
}
```

---

### 7️⃣ **Caching** - التخزين المؤقت
**ملف:** `CacheManager.cs`

نظام تخزين مؤقت ذكي مع انتهاء الصلاحية:

```csharp
var cache = new CacheManager(TimeSpan.FromHours(1));

// تخزين الملفات والنصوص
cache.CacheFile("model.w3d", fileData);
cache.CacheString("unit_name", "Unit1");

// استرجاع البيانات
var cached = cache.GetCachedFile("model.w3d");

// تنظيف منتهي الصلاحية
cache.RemoveExpiredEntries();
```

---

### 8️⃣ **Logging** - نظام التسجيل
**ملف:** `SimpleLogger.cs`

نظام تسجيل بسيط لتتبع العمليات والأخطاء:

```csharp
var logger = new SimpleLogger(consoleOutput: true);

logger.LogInfo("تم التحميل");
logger.LogWarning("تحذير");
logger.LogError("خطأ", exception);

var logs = logger.GetLogs();
```

---

## 🔧 كيفية الاستخدام

### مثال 1: قراءة ملف BIG واستخراج محتويات
```csharp
using var manager = new BigArchiveManager("data.big");
await manager.LoadAsync();

// قائمة الملفات
var files = manager.GetFileList();

// استخراج ملف
byte[] data = await manager.ExtractFileAsync("unit.ini");
```

### مثال 2: تحليل ملف INI واستخراج كائن
```csharp
var parser = new SAGE_IniParser();
await parser.ParseAsync("unit.ini");

// استخراج كائن كامل
string objectCode = parser.ExtractObject("GDI_Soldier");

// الحصول على جميع الأقسام
var sections = parser.GetSections();
```

### مثال 3: تطبيع اسم فصيل وحل مشكلة البحث
```csharp
var normalizer = new FactionNameNormalizer();

// تطبيع
var normalized = normalizer.Normalize("China Nuke General");
Console.WriteLine(normalized.Value); // "FactionChinaNukeGeneral"

// Fuzzy Matching
var faction = normalizer.TryFindClosestFaction("usa");
// يجد الفصيل الأقرب حتى مع أخطاء إملائية
```

### مثال 4: استخدام الخدمة الموحدة
```csharp
using var service = new ArchiveProcessingService();

// تحميل
await service.LoadArchiveAsync("game.big");
await service.LoadIniFileAsync("unit.ini");

// عمليات مختلفة
var files = service.GetLoadedArchiveFiles();
byte[] data = await service.ExtractFileFromArchiveAsync("texture.dds");
string value = service.GetIniValue("Unit", "BuildCost");
string normalized = service.NormalizeFactionName("china nuke");
```

---

## 📊 ملخص الملفات المُنشأة

| المجلد | الملفات | الوصف |
|--------|--------|--------|
| **Archives** | BigArchiveManager.cs | قراءة ملفات BIG مع Mounting Priority |
| **Parsers** | SAGE_IniParser.cs | تحليل INI متقدم |
| **Normalization** | SmartNormalization.cs, FactionNameNormalizer.cs | تطبيع أسماء الفصائل + Fuzzy Matching |
| **Implementations** | BigFileReader.cs, IniParser.cs | تنفيذ الواجهات من Application |
| **Helpers** | DataProcessingHelpers.cs, ValidationHelpers.cs | دوال مساعدة وتحقق |
| **Services** | ArchiveProcessingService.cs | خدمة موحدة شاملة |
| **Caching** | CacheManager.cs | تخزين مؤقت ذكي |
| **Logging** | SimpleLogger.cs | تسجيل الأحداث والأخطاء |

**المجموع: 12 ملف في 8 مجلدات**

---

## ✅ معايير النجاح للمرحلة الثانية

- ✅ BigArchiveManager يستخدم MemoryMappedFile و BinaryReader
- ✅ نظام Mounting Priority فعّال (ملفات !! لها الأولوية)
- ✅ SAGE_IniParser يستخدم ReadOnlySpan<char> والأداء عالية
- ✅ استخراج كائنات كاملة (Object ... End)
- ✅ SmartNormalization يحول "China Nuke General" → "FactionChinaNukeGeneral"
- ✅ Fuzzy Matching بنسبة 70% للمطابقة
- ✅ جميع الكلاسات تنفذ الواجهات من Application
- ✅ Clean Architecture محترمة: الخارج يشير للداخل

---

## 🚀 الخطوة التالية: المرحلة الثالثة

سيتضمن:
- **Application Layer**: Use Cases و Services
- **بناء منطق العمل الأساسي**: معالجة الوحدات والفصائل
- **Unit Tests**: اختبارات شاملة
- **WPF Layer**: واجهة المستخدم الرسومية
