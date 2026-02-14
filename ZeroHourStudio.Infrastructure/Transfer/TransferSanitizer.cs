using System.Text.RegularExpressions;
using ZeroHourStudio.Infrastructure.Logging;

namespace ZeroHourStudio.Infrastructure.Transfer;

// ════════════════════════════════════════════════════════
//  نماذج التطهير
// ════════════════════════════════════════════════════════

/// <summary>
/// خيارات تطهير كود INI المنقول
/// </summary>
public class SanitizeOptions
{
    /// <summary>إزالة شروط Prerequisite (مثل Prerequisite = ...)</summary>
    public bool StripPrerequisites { get; set; } = true;

    /// <summary>إزالة متطلبات ScienceRequired</summary>
    public bool StripScience { get; set; } = true;

    /// <summary>إزالة شروط الرتبة Rank</summary>
    public bool StripRank { get; set; } = true;

    /// <summary>إزالة متطلبات الترقية RequiredUpgrade (يحل المشكلة #2)</summary>
    public bool StripUpgradeRequirements { get; set; } = true;

    /// <summary>إزالة شروط Condition من CommandButton</summary>
    public bool StripButtonConditions { get; set; } = true;

    /// <summary>ضمان قابلية البناء: إدخال Cmd = DO_PRODUCE إذا مفقود</summary>
    public bool EnsureBuildable { get; set; } = true;

    /// <summary>تعليق الأسطر بدلاً من حذفها (يُبقي الكود الأصلي مرئياً)</summary>
    public bool CommentOutInsteadOfRemove { get; set; } = false;
}

/// <summary>
/// نتيجة عملية التطهير
/// </summary>
public class SanitizeResult
{
    public bool Success { get; set; }
    public string SanitizedContent { get; set; } = string.Empty;
    public int LinesRemoved { get; set; }
    public int LinesCommented { get; set; }
    public int LinesInjected { get; set; }
    public List<string> Changes { get; set; } = new();
}

// ════════════════════════════════════════════════════════
//  TransferSanitizer — المطهّر
// ════════════════════════════════════════════════════════

/// <summary>
/// مطهّر كود INI المنقول — يزيل القيود ويضمن الظهور الفوري للوحدة
/// 
/// عند نقل وحدة بين المودات، القيود التالية تمنعها من العمل:
/// - Prerequisite: يتطلب مبنى غير موجود في الهدف
/// - RequiredUpgrade: يتطلب ترقية غير موجودة
/// - ScienceRequired: يتطلب علم غير متاح
/// - Rank: يتطلب رتبة General
/// 
/// هذا المحرك يزيلها تلقائياً (أو يعلّقها) لضمان عمل الوحدة فوراً.
/// </summary>
public class TransferSanitizer
{
    // ── الأنماط المراد إزالتها ──
    private static readonly Regex PrerequisiteRx =
        new(@"^\s*Prerequisite\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ScienceRx =
        new(@"^\s*ScienceRequired\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RankRx =
        new(@"^\s*Rank\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UpgradeRx =
        new(@"^\s*RequiredUpgrade\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConditionRx =
        new(@"^\s*(NeededUpgrade|ButtonCondition)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CmdRx =
        new(@"^\s*Command\s*=\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ════════════════════════════════════════════════════
    //  تطهير كود Object/Weapon/Armor INI
    // ════════════════════════════════════════════════════

    /// <summary>
    /// تطهير كود INI — إزالة القيود وضمان الظهور
    /// </summary>
    public SanitizeResult Sanitize(string iniContent, SanitizeOptions? options = null)
    {
        options ??= new SanitizeOptions();

        var result = new SanitizeResult { Success = true };
        var lines = iniContent.Split('\n');
        var output = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            // تجاهل الأسطر الفارغة والتعليقات
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("//"))
            {
                output.Add(line);
                continue;
            }

            bool shouldRemove = false;
            string reason = string.Empty;

            // فحص كل نمط
            if (options.StripPrerequisites && PrerequisiteRx.IsMatch(trimmed))
            {
                shouldRemove = true;
                reason = "Prerequisite";
            }
            else if (options.StripScience && ScienceRx.IsMatch(trimmed))
            {
                shouldRemove = true;
                reason = "ScienceRequired";
            }
            else if (options.StripRank && RankRx.IsMatch(trimmed))
            {
                shouldRemove = true;
                reason = "Rank";
            }
            else if (options.StripUpgradeRequirements && UpgradeRx.IsMatch(trimmed))
            {
                shouldRemove = true;
                reason = "RequiredUpgrade";
            }
            else if (options.StripButtonConditions && ConditionRx.IsMatch(trimmed))
            {
                shouldRemove = true;
                reason = "ButtonCondition";
            }

            if (shouldRemove)
            {
                if (options.CommentOutInsteadOfRemove)
                {
                    output.Add($"; [ZHS-SANITIZED] {line}");
                    result.LinesCommented++;
                    result.Changes.Add($"تعليق {reason}: {trimmed}");
                }
                else
                {
                    result.LinesRemoved++;
                    result.Changes.Add($"حذف {reason}: {trimmed}");
                }
            }
            else
            {
                output.Add(line);
            }
        }

        result.SanitizedContent = string.Join("\n", output);
        return result;
    }

    // ════════════════════════════════════════════════════
    //  تطهير CommandButton
    // ════════════════════════════════════════════════════

    /// <summary>
    /// تطهير CommandButton — إزالة شروط + ضمان Cmd = DO_PRODUCE
    /// </summary>
    public SanitizeResult SanitizeCommandButton(string buttonContent, SanitizeOptions? options = null)
    {
        options ??= new SanitizeOptions();

        // أولاً: تطهير عام
        var result = Sanitize(buttonContent, options);

        // ثانياً: ضمان وجود Command
        if (options.EnsureBuildable)
        {
            EnsureCommand(result);
        }

        return result;
    }

    /// <summary>
    /// تطهير كود نقل كامل (Object + Weapons + Buttons)
    /// </summary>
    public SanitizeResult SanitizeTransferBundle(
        string objectContent,
        IEnumerable<string>? weaponContents = null,
        IEnumerable<string>? buttonContents = null,
        SanitizeOptions? options = null)
    {
        options ??= new SanitizeOptions();

        var combinedResult = new SanitizeResult { Success = true };
        var allContent = new List<string>();

        // تطهير Object
        var objResult = Sanitize(objectContent, options);
        allContent.Add(objResult.SanitizedContent);
        MergeResults(combinedResult, objResult);

        // تطهير Weapons
        if (weaponContents != null)
        {
            foreach (var weapon in weaponContents)
            {
                var wResult = Sanitize(weapon, options);
                allContent.Add(wResult.SanitizedContent);
                MergeResults(combinedResult, wResult);
            }
        }

        // تطهير CommandButtons
        if (buttonContents != null)
        {
            foreach (var button in buttonContents)
            {
                var bResult = SanitizeCommandButton(button, options);
                allContent.Add(bResult.SanitizedContent);
                MergeResults(combinedResult, bResult);
            }
        }

        combinedResult.SanitizedContent = string.Join("\n\n", allContent);
        return combinedResult;
    }

    // ════════════════════════════════════════════════════
    //  أدوات داخلية
    // ════════════════════════════════════════════════════

    private void EnsureCommand(SanitizeResult result)
    {
        var lines = result.SanitizedContent.Split('\n').ToList();
        bool hasCommand = lines.Any(l => CmdRx.IsMatch(l.Trim()));

        if (!hasCommand)
        {
            // إدخال Command = DO_PRODUCE قبل End
            var endIndex = lines.FindLastIndex(l =>
                l.Trim().Equals("End", StringComparison.OrdinalIgnoreCase));

            if (endIndex > 0)
            {
                lines.Insert(endIndex, "  Command = DO_PRODUCE");
                result.LinesInjected++;
                result.Changes.Add("إدخال Command = DO_PRODUCE لضمان قابلية البناء");
                result.SanitizedContent = string.Join("\n", lines);
            }
        }
    }

    private static void MergeResults(SanitizeResult target, SanitizeResult source)
    {
        target.LinesRemoved += source.LinesRemoved;
        target.LinesCommented += source.LinesCommented;
        target.LinesInjected += source.LinesInjected;
        target.Changes.AddRange(source.Changes);
    }
}
