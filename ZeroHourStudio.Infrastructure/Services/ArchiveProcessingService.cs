using ZeroHourStudio.Infrastructure.Parsers;
using ZeroHourStudio.Infrastructure.Normalization;
using ZeroHourStudio.Infrastructure.Archives;

namespace ZeroHourStudio.Infrastructure.Services;

/// <summary>
/// خدمة موحدة لإدارة العمليات الشاملة للنظام
/// توفر واجهة سهلة للتفاعل مع جميع مكونات Infrastructure
/// </summary>
public class ArchiveProcessingService : IDisposable
{
    private readonly SmartNormalization _smartNormalization;
    private SAGE_IniParser? _iniParser;
    private BigArchiveManager? _archiveManager;

    public ArchiveProcessingService()
    {
        _smartNormalization = new SmartNormalization();
    }

    /// <summary>
    /// تحميل ملف أرشيف BIG
    /// </summary>
    public async Task<bool> LoadArchiveAsync(string archivePath)
    {
        try
        {
            _archiveManager?.Dispose();
            _archiveManager = new BigArchiveManager(archivePath);
            await _archiveManager.LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"فشل تحميل الأرشيف: {archivePath}", ex);
        }
    }

    /// <summary>
    /// تحميل وتحليل ملف INI
    /// </summary>
    public async Task<bool> LoadIniFileAsync(string filePath)
    {
        try
        {
            _iniParser = new SAGE_IniParser();
            await _iniParser.ParseAsync(filePath);
            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"فشل تحليل ملف INI: {filePath}", ex);
        }
    }

    /// <summary>
    /// الحصول على قائمة الملفات في الأرشيف المحمّل
    /// </summary>
    public IEnumerable<string> GetLoadedArchiveFiles()
    {
        return _archiveManager?.GetFileList() ?? Enumerable.Empty<string>();
    }

    /// <summary>
    /// استخراج ملف من الأرشيف
    /// </summary>
    public async Task<byte[]> ExtractFileFromArchiveAsync(string fileName)
    {
        if (_archiveManager == null)
            throw new InvalidOperationException("لا يوجد أرشيف محمّل");

        return await _archiveManager.ExtractFileAsync(fileName);
    }

    /// <summary>
    /// الحصول على قيمة من ملف INI المحمّل
    /// </summary>
    public string? GetIniValue(string section, string key)
    {
        if (_iniParser == null)
            throw new InvalidOperationException("لا يوجد ملف INI محمّل");

        return _iniParser.GetValue(section, key);
    }

    /// <summary>
    /// استخراج كائن كامل من ملف INI
    /// </summary>
    public string? ExtractObjectFromIni(string objectName)
    {
        if (_iniParser == null)
            throw new InvalidOperationException("لا يوجد ملف INI محمّل");

        return _iniParser.ExtractObject(objectName);
    }

    /// <summary>
    /// تطبيع اسم فصيل مع SmartNormalization
    /// </summary>
    public string NormalizeFactionName(string factionInput)
    {
        var factionName = _smartNormalization.NormalizeFactionName(factionInput);
        return factionName.Value;
    }

    /// <summary>
    /// البحث عن فصيل معروف باستخدام Fuzzy Matching
    /// </summary>
    public KnownFaction? FindClosestFaction(string factionInput)
    {
        var factionName = _smartNormalization.NormalizeFactionName(factionInput);
        var knownFactions = _smartNormalization.GetKnownFactions();

        return knownFactions.FirstOrDefault(f =>
            f.NormalizedName.Equals(factionName.Value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// الحصول على قائمة الفصائل المعروفة
    /// </summary>
    public IEnumerable<KnownFaction> GetKnownFactions()
    {
        return _smartNormalization.GetKnownFactions();
    }

    public void Dispose()
    {
        _archiveManager?.Dispose();
        _iniParser = null;
    }
}
