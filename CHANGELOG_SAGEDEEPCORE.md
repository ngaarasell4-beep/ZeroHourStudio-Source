# سجل التحديثات - SageDeepCore

**التاريخ:** 7 فبراير 2026

---

## الإصلاحات والتحسينات المطبقة

### 1. BigArchiveManager
- **إصلاح:** حساب صحيح لـ `bytesConsumed` عند قراءة إدخالات الأرشيف
- **إصلاح:** عند فشل قراءة إدخال يُرجع (null, 0) للإيقاف الآمن بدل تلف الفهرسة
- **إضافة:** حد أقصى 512 حرف لاسم الملف لمنع حلقة لا نهائية

### 2. SmartTransferService
- **إصلاح:** `GetRelativePath` - دعم مراجع الأرشيف (`archive::entry`) للحفاظ على هيكل المجلدات عند الاستخراج
- **إصلاح:** نقل `File.SetLastWriteTimeUtc` خارج حلقة النسخ (كان يُستدعى في كل تكرار)

### 3. UnitDiscoveryService
- **تحسين:** معالجة ملفات INI بشكل متوازي (Parallel.ForEachAsync)
- **تحسين:** معالجة إدخالات الأرشيف بشكل متوازي
- **تحسين:** استخدام `ConcurrentDictionary` و `AddOrUpdate` للدمج الآمن

### 4. CommandSetPatchService
- **تحسين:** دعم `ButtonImage`, `SelectPortrait`, `Image` من بيانات الوحدة عند إنشاء CommandButton
- **تحسين:** `ResolveFactoryCommandSet` - دعم فصائل إضافية (chinanuke, chinainf, glalair, glatet, america, american)

### 5. توثيق
- **إضافة:** `CONCEPT.md` - المفهوم التقني لـ SageDeepCore
