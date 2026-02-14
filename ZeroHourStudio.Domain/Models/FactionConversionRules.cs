namespace ZeroHourStudio.Domain.Models;

/// <summary>
/// قواعد تحويل الفصائل
/// </summary>
public class FactionConversionRules
{
    public string SourceFaction { get; set; } = string.Empty;
    public string TargetFaction { get; set; } = string.Empty;

    /// <summary>
    /// قواعد استبدال الأصوات
    /// </summary>
    public Dictionary<string, string> VoiceMapping { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "AmericaVoice", "ChinaVoice" },
        { "USAVoice", "CHNVoice" },
        { "GLAVoice", "USAVoice" }
    };

    /// <summary>
    /// قواعد استبدال الألوان
    /// </summary>
    public Dictionary<string, string> ColorMapping { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "R:0 G:0 B:200", "R:200 G:0 B:0" },    // USA blue → China red
        { "R:200 G:0 B:0", "R:0 G:200 B:0" },     // China red → GLA green
        { "R:0 G:200 B:0", "R:0 G:0 B:200" }      // GLA green → USA blue
    };

    /// <summary>
    /// البادئات المطلوبة لكل فصيل
    /// </summary>
    public static Dictionary<string, string> FactionPrefixes { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "USA", "Ame" },
        { "America", "Ame" },
        { "China", "Chn" },
        { "GLA", "GLA" }
    };

    /// <summary>
    /// خيارات التحويل
    /// </summary>
    public bool ConvertVoices { get; set; } = true;
    public bool ConvertColors { get; set; } = true;
    public bool ConvertWeapons { get; set; } = false;
    public bool RenamePrefixes { get; set; } = true;
    public bool ConvertUpgrades { get; set; } = false;
}
