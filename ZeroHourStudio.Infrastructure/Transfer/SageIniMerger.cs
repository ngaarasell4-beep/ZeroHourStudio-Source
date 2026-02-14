using System.Text;
using System.Text.RegularExpressions;

namespace ZeroHourStudio.Infrastructure.Transfer;

/// <summary>
/// نوع البلوك في SAGE INI
/// </summary>
public enum SectionType
{
    Object,
    Weapon,
    Armor,
    DamageFX,
    FXList,
    ObjectCreationList,
    Locomotor,
    CommandButton,
    CommandSet,
    SpecialPower,
    Upgrade,
    Science,
    ExperienceLevel,
    PlayerTemplate,
    AudioEvent,
    MappedImage,
    Animation2D,
    ParticleSystem,
    ModifierList,
    CrateData,
    Other
}

/// <summary>
/// استراتيجية الدمج عند وجود تعارض
/// </summary>
public enum MergeStrategy
{
    /// قرار ذكي تلقائي
    Smart,
    /// إعادة تسمية البلوك الجديد
    Rename,
    /// دمج محتوى البلوكين
    Merge,
    /// استبدال البلوك القديم بالجديد
    Replace,
    /// تخطي البلوك الجديد
    Skip
}

/// <summary>
/// بلوك واحد في ملف SAGE INI (مثل Object, Weapon, Armor...)
/// </summary>
public class IniSection
{
    public SectionType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> RawLines { get; set; } = new();
    public int OriginalStartLine { get; set; }
    public int OriginalEndLine { get; set; }

    /// <summary>
    /// المحتوى الكامل للبلوك بما فيه السطر الأول و End
    /// </summary>
    public string FullContent => string.Join(Environment.NewLine, RawLines);

    /// <summary>
    /// المحتوى الداخلي فقط (بدون السطر الأول و End)
    /// </summary>
    public string InnerContent
    {
        get
        {
            if (RawLines.Count <= 2) return string.Empty;
            return string.Join(Environment.NewLine, RawLines.Skip(1).Take(RawLines.Count - 2));
        }
    }
}

/// <summary>
/// ملف SAGE INI محلل بالكامل
/// </summary>
public class IniFile
{
    public string FilePath { get; set; } = string.Empty;
    public List<IniSection> Sections { get; set; } = new();
    /// أسطر خارج أي بلوك (تعليقات، إعدادات عامة، #include)
    public List<string> PreambleLines { get; set; } = new();
    /// أسطر بين البلوكات (تعليقات، أسطر فارغة)
    public List<(int AfterSectionIndex, List<string> Lines)> InterstitialLines { get; set; } = new();

    public IniSection? FindSection(string name)
        => Sections.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public List<IniSection> FindSectionsByType(SectionType type)
        => Sections.Where(s => s.Type == type).ToList();

    public void RemoveSection(IniSection section)
        => Sections.Remove(section);
}

/// <summary>
/// نتيجة عملية دمج واحدة
/// </summary>
public class MergeResult
{
    public bool Success { get; set; }
    public bool Renamed { get; set; }
    public bool Merged { get; set; }
    public bool Replaced { get; set; }
    public bool Skipped { get; set; }
    public string? OriginalName { get; set; }
    public string? NewName { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// نتيجة عملية دمج كاملة لعدة بلوكات
/// </summary>
public class BatchMergeResult
{
    public bool Success { get; set; }
    public int TotalSections { get; set; }
    public int AddedCount { get; set; }
    public int SkippedCount { get; set; }
    public int RenamedCount { get; set; }
    public int ReplacedCount { get; set; }
    public int MergedCount { get; set; }
    public int ErrorCount { get; set; }
    public List<MergeResult> Details { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public string Summary => $"تم: {AddedCount} إضافة، {SkippedCount} تخطي، {RenamedCount} إعادة تسمية، {ReplacedCount} استبدال، {MergedCount} دمج، {ErrorCount} خطأ";
}

/// <summary>
/// محرك دمج SAGE INI الذكي
/// يحلل ملفات INI إلى بلوكات منفصلة ويدمجها بذكاء
/// </summary>
public class SageIniMerger
{
    // أنماط التعرف على بداية البلوكات
    private static readonly Regex BlockHeaderRegex = new(
        @"^\s*(\w+)\s+(\S+)",
        RegexOptions.Compiled);

    // أنواع البلوكات المعروفة في SAGE
    private static readonly Dictionary<string, SectionType> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Object"] = SectionType.Object,
        ["ObjectReskin"] = SectionType.Object,
        ["ChildObject"] = SectionType.Object,
        ["Weapon"] = SectionType.Weapon,
        ["Armor"] = SectionType.Armor,
        ["DamageFX"] = SectionType.DamageFX,
        ["FXList"] = SectionType.FXList,
        ["FXListAtBonePos"] = SectionType.FXList,
        ["ObjectCreationList"] = SectionType.ObjectCreationList,
        ["Locomotor"] = SectionType.Locomotor,
        ["LocomotorTemplate"] = SectionType.Locomotor,
        ["CommandButton"] = SectionType.CommandButton,
        ["CommandSet"] = SectionType.CommandSet,
        ["SpecialPower"] = SectionType.SpecialPower,
        ["Upgrade"] = SectionType.Upgrade,
        ["Science"] = SectionType.Science,
        ["ExperienceLevel"] = SectionType.ExperienceLevel,
        ["PlayerTemplate"] = SectionType.PlayerTemplate,
        ["AudioEvent"] = SectionType.AudioEvent,
        ["DialogEvent"] = SectionType.AudioEvent,
        ["MusicTrack"] = SectionType.AudioEvent,
        ["MappedImage"] = SectionType.MappedImage,
        ["Animation2D"] = SectionType.Animation2D,
        ["Animation"] = SectionType.Animation2D,
        ["ParticleSystem"] = SectionType.ParticleSystem,
        ["ModifierList"] = SectionType.ModifierList,
        ["CrateData"] = SectionType.CrateData,
    };

    // =========================================
    // === التحليل (Parse) ===
    // =========================================

    /// <summary>
    /// تحليل ملف INI إلى بلوكات منفصلة
    /// </summary>
    public IniFile Parse(string filePath)
    {
        if (!File.Exists(filePath))
            return new IniFile { FilePath = filePath };

        var lines = File.ReadAllLines(filePath);
        return ParseLines(lines, filePath);
    }

    /// <summary>
    /// تحليل محتوى INI من نص
    /// </summary>
    public IniFile ParseContent(string content, string virtualPath = "")
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return ParseLines(lines, virtualPath);
    }

    private IniFile ParseLines(string[] lines, string filePath)
    {
        var ini = new IniFile { FilePath = filePath };
        IniSection? currentSection = null;
        int depth = 0;
        var pendingLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // داخل بلوك
            if (currentSection != null)
            {
                currentSection.RawLines.Add(line);

                if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
                {
                    depth--;
                    if (depth <= 0)
                    {
                        currentSection.OriginalEndLine = i;
                        ini.Sections.Add(currentSection);
                        currentSection = null;
                        depth = 0;
                    }
                }
                else if (IsSubBlockOpener(trimmed, depth))
                {
                    depth++;
                }
                continue;
            }

            // خارج أي بلوك — فحص بداية بلوك جديد
            if (!string.IsNullOrWhiteSpace(trimmed) &&
                !trimmed.StartsWith(";") &&
                !trimmed.StartsWith("//") &&
                !trimmed.StartsWith("#") &&
                !trimmed.Contains('=') &&
                !trimmed.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                var match = BlockHeaderRegex.Match(trimmed);
                if (match.Success)
                {
                    var typeName = match.Groups[1].Value;
                    var name = match.Groups[2].Value;

                    // تأكد أنه بلوك حقيقي (كلمة أولى كبيرة + اسم)
                    if (typeName.Length > 1 && char.IsUpper(typeName[0]))
                    {
                        // حفظ الأسطر المعلقة قبل البلوك
                        if (ini.Sections.Count == 0 && pendingLines.Count > 0)
                        {
                            ini.PreambleLines.AddRange(pendingLines);
                        }
                        else if (pendingLines.Count > 0)
                        {
                            ini.InterstitialLines.Add((ini.Sections.Count - 1, new List<string>(pendingLines)));
                        }
                        pendingLines.Clear();

                        currentSection = new IniSection
                        {
                            TypeName = typeName,
                            Type = KnownTypes.TryGetValue(typeName, out var st) ? st : SectionType.Other,
                            Name = name,
                            OriginalStartLine = i,
                            RawLines = { line }
                        };
                        depth = 1;
                        continue;
                    }
                }
            }

            // سطر خارج بلوك
            pendingLines.Add(line);
        }

        // أسطر متبقية بعد آخر بلوك
        if (pendingLines.Count > 0)
        {
            ini.InterstitialLines.Add((ini.Sections.Count - 1, pendingLines));
        }

        return ini;
    }

    /// <summary>
    /// التعرف على فاتحات البلوكات الفرعية
    /// </summary>
    private static bool IsSubBlockOpener(string trimmed, int depth)
    {
        if (depth <= 0) return false;
        if (string.IsNullOrWhiteSpace(trimmed)) return false;
        if (trimmed.StartsWith(";") || trimmed.StartsWith("//")) return false;
        if (trimmed.Contains('=')) return false;
        if (trimmed.Equals("End", StringComparison.OrdinalIgnoreCase)) return false;

        // كلمة واحدة — بلوكات فرعية معروفة
        if (!trimmed.Contains(' '))
        {
            var singleWordBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "DefaultConditionState", "ConditionState", "TransitionState",
                "ModelConditionState", "AnimationState", "IdleAnimationState",
                "Prerequisites", "UnitSpecificSounds", "UnitSpecificFX",
            };
            return singleWordBlocks.Contains(trimmed);
        }

        // كلمتين+ — نمط "Word Word" بدون =
        var firstWord = trimmed.Split(' ', '\t')[0];
        return firstWord.Length > 1 && char.IsUpper(firstWord[0]);
    }

    // =========================================
    // === استخراج البلوكات المطلوبة ===
    // =========================================

    /// <summary>
    /// استخراج بلوك معين بالاسم من ملف INI
    /// </summary>
    public IniSection? ExtractSection(string filePath, string sectionName)
    {
        var ini = Parse(filePath);
        return ini.FindSection(sectionName);
    }

    /// <summary>
    /// استخراج بلوك معين من محتوى نصي
    /// </summary>
    public IniSection? ExtractSectionFromContent(string content, string sectionName)
    {
        var ini = ParseContent(content);
        return ini.FindSection(sectionName);
    }

    /// <summary>
    /// استخراج كل البلوكات من نوع معين
    /// </summary>
    public List<IniSection> ExtractSectionsByType(string filePath, SectionType type)
    {
        var ini = Parse(filePath);
        return ini.FindSectionsByType(type);
    }

    // =========================================
    // === الدمج (Merge) ===
    // =========================================

    /// <summary>
    /// دمج بلوك واحد في ملف INI هدف
    /// </summary>
    public MergeResult Merge(
        IniFile target,
        IniSection newSection,
        MergeStrategy strategy = MergeStrategy.Smart)
    {
        var result = new MergeResult
        {
            OriginalName = newSection.Name
        };

        try
        {
            // فحص التعارضات
            var existing = target.FindSection(newSection.Name);

            if (existing != null)
            {
                switch (strategy)
                {
                    case MergeStrategy.Smart:
                        if (AreSectionsIdentical(existing, newSection))
                        {
                            result.Skipped = true;
                            result.Success = true;
                            result.Message = $"تخطي '{newSection.Name}' — متطابق مع الموجود";
                            return result;
                        }
                        // مختلف — إعادة تسمية
                        var uniqueName = GenerateUniqueName(newSection.Name, target);
                        result.NewName = uniqueName;
                        newSection.Name = uniqueName;
                        UpdateHeaderLine(newSection);
                        result.Renamed = true;
                        result.Message = $"إعادة تسمية '{result.OriginalName}' → '{uniqueName}'";
                        break;

                    case MergeStrategy.Rename:
                        var renamedName = GenerateUniqueName(newSection.Name, target);
                        result.NewName = renamedName;
                        newSection.Name = renamedName;
                        UpdateHeaderLine(newSection);
                        result.Renamed = true;
                        result.Message = $"إعادة تسمية '{result.OriginalName}' → '{renamedName}'";
                        break;

                    case MergeStrategy.Replace:
                        target.RemoveSection(existing);
                        result.Replaced = true;
                        result.Message = $"استبدال '{newSection.Name}'";
                        break;

                    case MergeStrategy.Merge:
                        MergeSections(existing, newSection);
                        result.Merged = true;
                        result.Success = true;
                        result.Message = $"دمج محتوى '{newSection.Name}'";
                        return result;

                    case MergeStrategy.Skip:
                        result.Skipped = true;
                        result.Success = true;
                        result.Message = $"تخطي '{newSection.Name}' — موجود مسبقاً";
                        return result;
                }
            }

            // إضافة البلوك الجديد
            target.Sections.Add(newSection);
            result.Success = true;
            if (string.IsNullOrEmpty(result.Message))
                result.Message = $"إضافة '{newSection.Name}'";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"خطأ في دمج '{newSection.Name}': {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// دمج عدة بلوكات في ملف INI هدف
    /// </summary>
    public BatchMergeResult MergeBatch(
        IniFile target,
        IEnumerable<IniSection> sections,
        MergeStrategy strategy = MergeStrategy.Smart)
    {
        var batch = new BatchMergeResult();

        foreach (var section in sections)
        {
            batch.TotalSections++;
            try
            {
                var result = Merge(target, section, strategy);
                batch.Details.Add(result);

                if (result.Skipped) batch.SkippedCount++;
                else if (result.Renamed) { batch.RenamedCount++; batch.AddedCount++; }
                else if (result.Replaced) { batch.ReplacedCount++; batch.AddedCount++; }
                else if (result.Merged) batch.MergedCount++;
                else if (result.Success) batch.AddedCount++;
                else batch.ErrorCount++;
            }
            catch (Exception ex)
            {
                batch.ErrorCount++;
                batch.Errors.Add($"خطأ في '{section.Name}': {ex.Message}");
            }
        }

        batch.Success = batch.ErrorCount == 0;
        return batch;
    }

    /// <summary>
    /// دمج بلوكات محددة بالاسم من ملف مصدر إلى ملف هدف
    /// </summary>
    public BatchMergeResult MergeSpecificSections(
        string targetFilePath,
        string sourceContent,
        IEnumerable<string> sectionNames,
        MergeStrategy strategy = MergeStrategy.Smart)
    {
        var target = Parse(targetFilePath);
        var source = ParseContent(sourceContent);

        var sectionsToMerge = new List<IniSection>();
        foreach (var name in sectionNames)
        {
            var section = source.FindSection(name);
            if (section != null)
                sectionsToMerge.Add(section);
        }

        var result = MergeBatch(target, sectionsToMerge, strategy);

        // كتابة النتيجة
        if (result.Success || result.AddedCount > 0)
        {
            Write(target, targetFilePath);
        }

        return result;
    }

    // =========================================
    // === الكتابة (Write) ===
    // =========================================

    /// <summary>
    /// كتابة ملف INI محلل إلى ملف
    /// </summary>
    public void Write(IniFile ini, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(filePath, false, Encoding.GetEncoding(1252));

        // كتابة الأسطر الأولية
        foreach (var line in ini.PreambleLines)
            writer.WriteLine(line);

        // كتابة البلوكات مع الأسطر البينية
        for (int i = 0; i < ini.Sections.Count; i++)
        {
            var section = ini.Sections[i];

            // كتابة البلوك بأسطره الأصلية
            foreach (var line in section.RawLines)
                writer.WriteLine(line);

            // كتابة الأسطر البينية بعد هذا البلوك
            var interstitial = ini.InterstitialLines
                .FirstOrDefault(x => x.AfterSectionIndex == i);
            if (interstitial.Lines != null)
            {
                foreach (var line in interstitial.Lines)
                    writer.WriteLine(line);
            }
            else
            {
                // سطر فارغ بين البلوكات
                writer.WriteLine();
            }
        }
    }

    /// <summary>
    /// إلحاق بلوك في نهاية ملف INI موجود
    /// </summary>
    public void AppendSection(string filePath, IniSection section)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(filePath, append: true, Encoding.GetEncoding(1252));
        writer.WriteLine();
        writer.WriteLine($"; === Added by ZeroHourStudio ===");
        foreach (var line in section.RawLines)
            writer.WriteLine(line);
    }

    // =========================================
    // === أدوات مساعدة ===
    // =========================================

    /// <summary>
    /// مقارنة بلوكين — هل هما متطابقان؟
    /// </summary>
    private static bool AreSectionsIdentical(IniSection a, IniSection b)
    {
        if (a.RawLines.Count != b.RawLines.Count) return false;

        // مقارنة المحتوى الداخلي (تجاهل السطر الأول والأخير)
        for (int i = 1; i < a.RawLines.Count - 1; i++)
        {
            var lineA = a.RawLines[i].Trim();
            var lineB = b.RawLines[i].Trim();
            if (!lineA.Equals(lineB, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// توليد اسم فريد لبلوك
    /// </summary>
    private static string GenerateUniqueName(string baseName, IniFile ini)
    {
        var candidate = baseName;
        int suffix = 2;

        while (ini.FindSection(candidate) != null)
        {
            candidate = $"{baseName}_ZHS{suffix}";
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    /// تحديث السطر الأول للبلوك بعد إعادة التسمية
    /// </summary>
    private static void UpdateHeaderLine(IniSection section)
    {
        if (section.RawLines.Count == 0) return;

        var oldHeader = section.RawLines[0];
        var match = Regex.Match(oldHeader, @"^(\s*\w+\s+)\S+(.*)$");
        if (match.Success)
        {
            section.RawLines[0] = $"{match.Groups[1].Value}{section.Name}{match.Groups[2].Value}";
        }
    }

    /// <summary>
    /// دمج محتوى بلوكين (إضافة الأسطر الجديدة غير الموجودة)
    /// </summary>
    private static void MergeSections(IniSection existing, IniSection incoming)
    {
        // جمع الأسطر الداخلية الموجودة
        var existingInner = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < existing.RawLines.Count - 1; i++)
        {
            existingInner.Add(existing.RawLines[i].Trim());
        }

        // إضافة الأسطر الجديدة قبل End
        var insertIndex = existing.RawLines.Count - 1; // قبل End
        for (int i = 1; i < incoming.RawLines.Count - 1; i++)
        {
            var trimmed = incoming.RawLines[i].Trim();
            if (!existingInner.Contains(trimmed))
            {
                existing.RawLines.Insert(insertIndex, incoming.RawLines[i]);
                insertIndex++;
            }
        }
    }
}
