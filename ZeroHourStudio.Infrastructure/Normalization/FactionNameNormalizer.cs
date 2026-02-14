using ZeroHourStudio.Domain.ValueObjects;

namespace ZeroHourStudio.Infrastructure.Normalization;

/// <summary>
/// خدمة تطبيع أسماء الفصائل باستخدام SmartNormalization
/// توفر طريقة موحدة للعمل مع التطبيع الذكي
/// </summary>
public class FactionNameNormalizer
{
    private readonly SmartNormalization _smartNormalization;

    public FactionNameNormalizer()
    {
        _smartNormalization = new SmartNormalization();
    }

    public FactionNameNormalizer(SmartNormalization smartNormalization)
    {
        _smartNormalization = smartNormalization ?? throw new ArgumentNullException(nameof(smartNormalization));
    }

    /// <summary>
    /// تطبيع اسم الفصيل
    /// مثال: "China Nuke General" -> FactionName("FactionChinaNukeGeneral")
    /// </summary>
    public FactionName Normalize(string factionInput)
    {
        return _smartNormalization.NormalizeFactionName(factionInput);
    }

    /// <summary>
    /// محاولة الحصول على الفصيل المعروف الأقرب
    /// </summary>
    public KnownFaction? TryFindClosestFaction(string factionInput)
    {
        var normalized = _smartNormalization.NormalizeFactionName(factionInput);
        var knownFactions = _smartNormalization.GetKnownFactions();

        return knownFactions.FirstOrDefault(f =>
            f.NormalizedName.Equals(normalized.Value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// الحصول على قائمة الفصائل المعروفة
    /// </summary>
    public IEnumerable<string> GetKnownFactionNames()
    {
        return _smartNormalization.GetKnownFactions()
            .Select(f => f.NormalizedName);
    }

    /// <summary>
    /// تسجيل فصيل جديد
    /// </summary>
    public void RegisterFaction(string normalizedName, params string[] aliases)
    {
        _smartNormalization.RegisterFaction(normalizedName, aliases);
    }
}
