# SageDeepCore - المفهوم التقني

**ZeroHour Studio** منصة هندسة عكسية لمحرك SAGE في Command & Conquer Generals: Zero Hour.

---

## الفكرة الأساسية

البرنامج يعمل كـ **Unit Migration Engine** (محرك نقل وحدات):
- اختيار أي وحدة (دبابة، جندي، طائرة) من مود المصدر
- نقلها بكل "جيناتها البرمجية" وأصولها البصرية إلى مود الهدف
- بضغطة زر واحدة

---

## المكونات التقنية (SageDeepCore)

### 1. الفهرسة الذكية (Smart Indexing)
- مسح آلاف الملفات داخل أرشيفات BIG وملفات INI
- معالجة متوازية (Parallel) للسرعة
- **الملفات:** `UnitDiscoveryService`, `ModBigFileReader`, `BigArchiveManager`

### 2. تتبع سلسلة التبعيات (Dependency Chain)
تتبع الشجرة الكاملة:
```
Object (الكود) → Weapon (السلاح) → Projectile (القذيفة) 
  → W3D Model (المجسم) → DDS Textures (الصور) → Audio (الأصوات)
```
- **الملف:** `SmartDependencyResolver`

### 3. الربط الذكي (Smart Relinking)
- إذا الوحدة المنقولة تفتقد CommandSet (زر البناء) في المود المستهدف
- البرنامج ينشئ الزر برمجياً ويحقنه في ملفات المود
- ضمان ظهور الوحدة فوراً داخل اللعبة
- **الملف:** `CommandSetPatchService`

### 4. النقل الذكي (Smart Transfer)
- نقل الملفات مع الحفاظ على هيكل المجلدات
- دعم الاستخراج من أرشيفات BIG
- Rollback عند الفشل
- **الملف:** `SmartTransferService`
