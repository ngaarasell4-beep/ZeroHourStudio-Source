namespace ZeroHourStudio.Domain.ValueObjects;

/// <summary>
/// Value Object يمثل اسم الجيش مع التطبيع التلقائي
/// يقوم بـ:
/// - إزالة المسافات
/// - تحويل الأحرف لصغيرة
/// - إضافة بادئة 'Faction' إذا لم تكن موجودة
/// </summary>
public class FactionName : IEquatable<FactionName>
{
    private const string FactionPrefix = "Faction";

    /// <summary>
    /// القيمة المطبعة للاسم
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// الاسم الأصلي قبل التطبيع
    /// </summary>
    public string Original { get; }

    public FactionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name), "اسم الجيش لا يمكن أن يكون فارغاً");

        Original = name;
        Value = NormalizeFactionName(name);
    }

    /// <summary>
    /// تطبيع اسم الجيش بحسب القواعد المحددة
    /// </summary>
    private static string NormalizeFactionName(string name)
    {
        // إزالة المسافات من البداية والنهاية
        var normalized = name.Trim();

        // تحويل الأحرف لصغيرة
        normalized = normalized.ToLowerInvariant();

        // إزالة جميع المسافات
        normalized = normalized.Replace(" ", string.Empty);

        // إضافة بادئة 'faction' إذا لم تكن موجودة
        if (!normalized.StartsWith(FactionPrefix.ToLowerInvariant()))
        {
            normalized = FactionPrefix.ToLowerInvariant() + normalized;
        }

        return normalized;
    }

    public override bool Equals(object? obj) => Equals(obj as FactionName);

    public bool Equals(FactionName? other)
    {
        if (other is null)
            return false;

        return Value == other.Value;
    }

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value;

    public static implicit operator string(FactionName factionName) => factionName.Value;

    public static explicit operator FactionName(string name) => new(name);

    /// <summary>
    /// تطبيع مفتاح الفصيل للمقارنة (USA, China, GLA) — نقطة مركزية واحدة
    /// يُستخدم في CommandSetAnalyzer, CommandButtonAnalyzer, MainViewModel, TargetPaneViewModel
    /// </summary>
    public static string NormalizeFactionKey(string faction)
    {
        if (string.IsNullOrWhiteSpace(faction)) return faction;

        if (faction.Contains("America", StringComparison.OrdinalIgnoreCase) ||
            faction.Contains("USA", StringComparison.OrdinalIgnoreCase))
            return "USA";
        if (faction.Contains("China", StringComparison.OrdinalIgnoreCase))
            return "China";
        if (faction.Contains("GLA", StringComparison.OrdinalIgnoreCase))
            return "GLA";
        return faction;
    }
}
