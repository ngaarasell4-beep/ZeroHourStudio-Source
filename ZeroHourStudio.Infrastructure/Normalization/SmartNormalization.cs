using System.Globalization;
using ZeroHourStudio.Domain.ValueObjects;

namespace ZeroHourStudio.Infrastructure.Normalization;

/// <summary>
/// نظام التطبيع الذكي لأسماء الفصائل
/// يحول "China Nuke General" إلى "FactionChinaNukeGeneral"
/// ويستخدم Fuzzy Matching للربط الذكي
/// </summary>
public class SmartNormalization
{
    // قائمة الفصائل المكتشفة في اللعبة
    private readonly List<KnownFaction> _knownFactions;
    private const int FuzzyMatchThreshold = 70; // نسبة التطابق المقبولة (%)

    public SmartNormalization()
    {
        // وحدة التهيئة: الفصائل العشرة المكتشفة
        _knownFactions = new List<KnownFaction>
        {
            new("usa", new[] { "usa", "united states", "american" }),
            new("chinanuke", new[] { "china nuke general", "china", "nuke", "nuclearchina" }),
            new("chinainf", new[] { "china infantry", "inf general", "infantry" }),
            new("glainf", new[] { "gl infantry", "gla infantry", "gla" }),
            new("glalair", new[] { "gla air force", "gla air", "gla laser", "gla airstrike" }),
            new("glatet", new[] { "gla terror", "gla terror general", "terrorist" }),
            new("superweapon", new[] { "superweapon", "super weapon", "supergen" }),
            new("kingraptor", new[] { "kingraptor", "king raptor", "beast" }),
            new("tower", new[] { "tower defense", "tower" }),
            new("skirmish", new[] { "skirmish", "random" })
        };
    }

    /// <summary>
    /// تطبيع اسم الفصيل باستخدام SmartNormalization
    /// يحول "China Nuke General" إلى "FactionChinaNukeGeneral"
    /// </summary>
    public FactionName NormalizeFactionName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentNullException(nameof(input), "اسم الفصيل لا يمكن أن يكون فارغاً");

        // محاولة المطابقة الدقيقة أولاً
        var exactMatch = FindExactMatch(input);
        if (exactMatch != null)
        {
            return new FactionName(exactMatch.NormalizedName);
        }

        // محاولة Fuzzy Matching
        var fuzzyMatch = FindFuzzyMatch(input);
        if (fuzzyMatch != null)
        {
            return new FactionName(fuzzyMatch.NormalizedName);
        }

        // إذا لم يتم العثور على مطابقة، استخدم التطبيع الأساسي
        return new FactionName(input);
    }

    /// <summary>
    /// البحث عن مطابقة دقيقة في الفصائل المعروفة
    /// </summary>
    private KnownFaction? FindExactMatch(string input)
    {
        var normalizedInput = input.Trim().ToLowerInvariant();

        foreach (var faction in _knownFactions)
        {
            // مقارنة مباشرة مع الاسم المطبّع
            if (faction.NormalizedName.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase))
                return faction;

            // مقارنة مع الأنماط المعروفة
            foreach (var alias in faction.Aliases)
            {
                if (normalizedInput.Equals(alias.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                    return faction;
            }
        }

        return null;
    }

    /// <summary>
    /// البحث عن مطابقة غامضة باستخدام Levenshtein Distance
    /// </summary>
    private KnownFaction? FindFuzzyMatch(string input)
    {
        var normalizedInput = input.Trim().ToLowerInvariant();
        KnownFaction? bestMatch = null;
        int bestScore = 0;

        foreach (var faction in _knownFactions)
        {
            // حساب المسافة مع الاسم المطبّع
            int score = CalculateSimilarity(normalizedInput, faction.NormalizedName.ToLowerInvariant());
            if (score > bestScore && score >= FuzzyMatchThreshold)
            {
                bestScore = score;
                bestMatch = faction;
            }

            // حساب المسافة مع الأنماط المعروفة
            foreach (var alias in faction.Aliases)
            {
                score = CalculateSimilarity(normalizedInput, alias.ToLowerInvariant());
                if (score > bestScore && score >= FuzzyMatchThreshold)
                {
                    bestScore = score;
                    bestMatch = faction;
                }
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// حساب نسبة التشابه بين نصيّن باستخدام Levenshtein Distance
    /// </summary>
    private static int CalculateSimilarity(string source, string target)
    {
        if (source.Length == 0) return target.Length * 100;
        if (target.Length == 0) return source.Length * 100;

        int distance = LevenshteinDistance(source, target);
        int maxLength = Math.Max(source.Length, target.Length);
        
        // حساب نسبة التشابه (100 = تطابق تام، 0 = لا تشابه)
        return (int)((1 - (double)distance / maxLength) * 100);
    }

    /// <summary>
    /// حساب مسافة Levenshtein بين نصيّن
    /// </summary>
    private static int LevenshteinDistance(string source, string target)
    {
        var matrix = new int[source.Length + 1, target.Length + 1];

        for (int i = 0; i <= source.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= target.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= source.Length; i++)
        {
            for (int j = 1; j <= target.Length; j++)
            {
                int cost = source[i - 1] == target[j - 1] ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(
                        matrix[i - 1, j] + 1,      // deletion
                        matrix[i, j - 1] + 1),    // insertion
                    matrix[i - 1, j - 1] + cost); // substitution
            }
        }

        return matrix[source.Length, target.Length];
    }

    /// <summary>
    /// الحصول على قائمة الفصائل المعروفة
    /// </summary>
    public IEnumerable<KnownFaction> GetKnownFactions() => _knownFactions;

    /// <summary>
    /// تحديث قائمة الفصائل المعروفة
    /// </summary>
    public void RegisterFaction(string normalizedName, params string[] aliases)
    {
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentNullException(nameof(normalizedName));

        if (!_knownFactions.Any(f => f.NormalizedName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            _knownFactions.Add(new KnownFaction(normalizedName, aliases));
        }
    }
}

/// <summary>
/// يمثل فصيل معروف في قاعدة البيانات
/// </summary>
public class KnownFaction
{
    /// <summary>
    /// الاسم المطبّع (مثل: FactionChinaNukeGeneral)
    /// </summary>
    public string NormalizedName { get; }

    /// <summary>
    /// الأسماء/الأنماط البديلة
    /// </summary>
    public string[] Aliases { get; }

    public KnownFaction(string normalizedName, string[] aliases)
    {
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentNullException(nameof(normalizedName));

        NormalizedName = normalizedName;
        Aliases = aliases ?? Array.Empty<string>();
    }
}
