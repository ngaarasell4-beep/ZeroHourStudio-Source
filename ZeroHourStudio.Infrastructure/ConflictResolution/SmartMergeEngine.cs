using System.Text;

namespace ZeroHourStudio.Infrastructure.ConflictResolution;

/// <summary>
/// حالة حقل في نتيجة الدمج
/// </summary>
public enum MergeFieldStatus
{
    /// <summary>الحقل موجود في كلا التعريفين بنفس القيمة</summary>
    Identical,
    /// <summary>الحقل معدّل (قيمة مختلفة)</summary>
    Modified,
    /// <summary>الحقل موجود في المصدر فقط (جديد)</summary>
    SourceOnly,
    /// <summary>الحقل موجود في الهدف فقط (موجود)</summary>
    TargetOnly
}

/// <summary>
/// استراتيجية الدمج
/// </summary>
public enum MergeStrategy
{
    /// <summary>دمج ذكي - يأخذ أفضل ما في الاثنين</summary>
    SmartMerge,
    /// <summary>المصدر يفوز عند التعارض</summary>
    SourceWins,
    /// <summary>الهدف يفوز عند التعارض</summary>
    TargetWins
}

/// <summary>
/// حقل واحد في نتيجة الدمج
/// </summary>
public class MergeField
{
    public string Key { get; set; } = string.Empty;
    public string? SourceValue { get; set; }
    public string? TargetValue { get; set; }
    public string FinalValue { get; set; } = string.Empty;
    public MergeFieldStatus Status { get; set; }

    /// <summary>أيقونة الحالة</summary>
    public string StatusIcon => Status switch
    {
        MergeFieldStatus.Identical => "⚪",
        MergeFieldStatus.Modified => "🟡",
        MergeFieldStatus.SourceOnly => "🟢",
        MergeFieldStatus.TargetOnly => "🔵",
        _ => "⚪"
    };

    /// <summary>نص الحالة</summary>
    public string StatusText => Status switch
    {
        MergeFieldStatus.Identical => "متطابق",
        MergeFieldStatus.Modified => "معدّل",
        MergeFieldStatus.SourceOnly => "من المصدر",
        MergeFieldStatus.TargetOnly => "من الهدف",
        _ => ""
    };
}

/// <summary>
/// نتيجة دمج تعريفين
/// </summary>
public class MergeResult
{
    public string DefinitionName { get; set; } = string.Empty;
    public string DefinitionType { get; set; } = string.Empty;
    public List<MergeField> Fields { get; set; } = new();
    public string MergedContent { get; set; } = string.Empty;
    public MergeStrategy StrategyUsed { get; set; }

    public int IdenticalCount => Fields.Count(f => f.Status == MergeFieldStatus.Identical);
    public int ModifiedCount => Fields.Count(f => f.Status == MergeFieldStatus.Modified);
    public int SourceOnlyCount => Fields.Count(f => f.Status == MergeFieldStatus.SourceOnly);
    public int TargetOnlyCount => Fields.Count(f => f.Status == MergeFieldStatus.TargetOnly);
    public int TotalFields => Fields.Count;

    public string Summary =>
        $"{IdenticalCount} متطابق | {ModifiedCount} معدّل | {SourceOnlyCount} جديد | {TargetOnlyCount} موجود";
}

/// <summary>
/// محرك الدمج الذكي - يدمج تعريفات INI حقل بحقل بدلاً من الكتابة فوقها
/// </summary>
public class SmartMergeEngine
{
    private readonly IniDefinitionParser _parser = new();

    /// <summary>
    /// دمج تعريفين INI
    /// </summary>
    public MergeResult Merge(
        string sourceIniContent,
        string targetIniContent,
        string definitionName,
        MergeStrategy strategy = MergeStrategy.SmartMerge)
    {
        var sourceBlock = _parser.ParseDefinition(sourceIniContent, definitionName);
        var targetBlock = _parser.ParseDefinition(targetIniContent, definitionName);

        var result = new MergeResult
        {
            DefinitionName = definitionName,
            StrategyUsed = strategy
        };

        if (sourceBlock == null && targetBlock == null)
        {
            result.DefinitionType = "Unknown";
            return result;
        }

        if (sourceBlock == null)
        {
            result.DefinitionType = targetBlock!.Type;
            result.MergedContent = targetBlock.RawContent;
            foreach (var f in targetBlock.Fields.Where(f => f.Key != "__RAW__"))
            {
                result.Fields.Add(new MergeField
                {
                    Key = f.Key,
                    TargetValue = f.Value,
                    FinalValue = f.Value,
                    Status = MergeFieldStatus.TargetOnly
                });
            }
            return result;
        }

        if (targetBlock == null)
        {
            result.DefinitionType = sourceBlock.Type;
            result.MergedContent = sourceBlock.RawContent;
            foreach (var f in sourceBlock.Fields.Where(f => f.Key != "__RAW__"))
            {
                result.Fields.Add(new MergeField
                {
                    Key = f.Key,
                    SourceValue = f.Value,
                    FinalValue = f.Value,
                    Status = MergeFieldStatus.SourceOnly
                });
            }
            return result;
        }

        // === كلا التعريفين موجود - الدمج الحقيقي ===
        result.DefinitionType = sourceBlock.Type;
        MergeBlocks(sourceBlock, targetBlock, result, strategy);
        result.MergedContent = GenerateMergedContent(result, sourceBlock.Type, definitionName);

        return result;
    }

    /// <summary>
    /// دمج بلوكين حقل بحقل
    /// </summary>
    private void MergeBlocks(
        IniDefinitionBlock source,
        IniDefinitionBlock target,
        MergeResult result,
        MergeStrategy strategy)
    {
        var sourceFields = source.Fields.Where(f => f.Key != "__RAW__").ToList();
        var targetFields = target.Fields.Where(f => f.Key != "__RAW__").ToList();

        // بناء خرائط: لكل مفتاح، قائمة القيم
        var sourceMap = BuildFieldMap(sourceFields);
        var targetMap = BuildFieldMap(targetFields);

        var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in sourceMap.Keys) allKeys.Add(k);
        foreach (var k in targetMap.Keys) allKeys.Add(k);

        foreach (var key in allKeys)
        {
            var inSource = sourceMap.TryGetValue(key, out var srcValues);
            var inTarget = targetMap.TryGetValue(key, out var tgtValues);

            if (inSource && inTarget)
            {
                // الحقل في كليهما - مقارنة القيم
                MergeSharedFields(key, srcValues!, tgtValues!, result, strategy);
            }
            else if (inSource)
            {
                // حقول المصدر فقط - نضيفها
                foreach (var sv in srcValues!)
                {
                    result.Fields.Add(new MergeField
                    {
                        Key = key,
                        SourceValue = sv,
                        FinalValue = sv,
                        Status = MergeFieldStatus.SourceOnly
                    });
                }
            }
            else if (inTarget)
            {
                // حقول الهدف فقط - نبقيها
                foreach (var tv in tgtValues!)
                {
                    result.Fields.Add(new MergeField
                    {
                        Key = key,
                        TargetValue = tv,
                        FinalValue = tv,
                        Status = MergeFieldStatus.TargetOnly
                    });
                }
            }
        }
    }

    private void MergeSharedFields(
        string key, List<string> srcValues, List<string> tgtValues,
        MergeResult result, MergeStrategy strategy)
    {
        // حقول مفردة القيمة
        if (srcValues.Count == 1 && tgtValues.Count == 1)
        {
            var sv = srcValues[0];
            var tv = tgtValues[0];

            if (sv.Equals(tv, StringComparison.OrdinalIgnoreCase))
            {
                result.Fields.Add(new MergeField
                {
                    Key = key, SourceValue = sv, TargetValue = tv,
                    FinalValue = sv, Status = MergeFieldStatus.Identical
                });
            }
            else
            {
                var final = strategy switch
                {
                    MergeStrategy.SourceWins => sv,
                    MergeStrategy.TargetWins => tv,
                    MergeStrategy.SmartMerge => PickSmartValue(key, sv, tv),
                    _ => sv
                };

                result.Fields.Add(new MergeField
                {
                    Key = key, SourceValue = sv, TargetValue = tv,
                    FinalValue = final, Status = MergeFieldStatus.Modified
                });
            }
            return;
        }

        // حقول متعددة القيم (مثل Armor في عدة أسطر)
        var allValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var srcSet = new HashSet<string>(srcValues, StringComparer.OrdinalIgnoreCase);
        var tgtSet = new HashSet<string>(tgtValues, StringComparer.OrdinalIgnoreCase);

        foreach (var v in srcValues) allValues.Add(v);
        foreach (var v in tgtValues) allValues.Add(v);

        foreach (var val in allValues)
        {
            var inSrc = srcSet.Contains(val);
            var inTgt = tgtSet.Contains(val);

            if (inSrc && inTgt)
            {
                result.Fields.Add(new MergeField
                {
                    Key = key, SourceValue = val, TargetValue = val,
                    FinalValue = val, Status = MergeFieldStatus.Identical
                });
            }
            else if (inSrc)
            {
                result.Fields.Add(new MergeField
                {
                    Key = key, SourceValue = val,
                    FinalValue = val, Status = MergeFieldStatus.SourceOnly
                });
            }
            else
            {
                result.Fields.Add(new MergeField
                {
                    Key = key, TargetValue = val,
                    FinalValue = val, Status = MergeFieldStatus.TargetOnly
                });
            }
        }
    }

    /// <summary>
    /// اختيار ذكي للقيمة عند التعارض
    /// </summary>
    private string PickSmartValue(string key, string sourceVal, string targetVal)
    {
        // للقيم الرقمية - نأخذ الأعلى (أقوى)
        if (TryExtractNumber(sourceVal, out var srcNum) && TryExtractNumber(targetVal, out var tgtNum))
        {
            // لحقول مثل Cost, BuildTime - نأخذ الأقل (أفضل)
            if (key.Contains("Cost", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("BuildTime", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("BuildCost", StringComparison.OrdinalIgnoreCase))
            {
                return srcNum <= tgtNum ? sourceVal : targetVal;
            }

            // لبقية الحقول الرقمية (HP, Range, Damage) - نأخذ الأعلى
            return srcNum >= tgtNum ? sourceVal : targetVal;
        }

        // قيم غير رقمية - المصدر يفوز (هو الجديد)
        return sourceVal;
    }

    private static bool TryExtractNumber(string val, out double number)
    {
        // استخراج أول رقم من قيمة مثل "100.0" أو "ARMOR_PIERCING 25%"
        var match = System.Text.RegularExpressions.Regex.Match(val, @"[\d.]+");
        if (match.Success && double.TryParse(match.Value, out number))
            return true;
        number = 0;
        return false;
    }

    private static Dictionary<string, List<string>> BuildFieldMap(List<IniField> fields)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fields)
        {
            if (!map.TryGetValue(f.Key, out var list))
            {
                list = new List<string>();
                map[f.Key] = list;
            }
            list.Add(f.Value);
        }
        return map;
    }

    /// <summary>
    /// توليد المحتوى المدمج النهائي كنص INI
    /// </summary>
    private string GenerateMergedContent(MergeResult result, string type, string name)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{type} {name}");

        foreach (var field in result.Fields)
        {
            var comment = field.Status switch
            {
                MergeFieldStatus.SourceOnly => " ; [MERGED: from source]",
                MergeFieldStatus.TargetOnly => " ; [MERGED: kept from target]",
                MergeFieldStatus.Modified => $" ; [MERGED: source={field.SourceValue}, target={field.TargetValue}]",
                _ => ""
            };

            sb.AppendLine($"  {field.Key} = {field.FinalValue}{comment}");
        }

        sb.AppendLine("End");
        return sb.ToString();
    }
}
