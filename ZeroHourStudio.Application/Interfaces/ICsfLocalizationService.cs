using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Application.Interfaces;

public interface ICsfLocalizationService
{
    /// <summary>
    /// قراءة ملف CSF وإرجاع جميع المدخلات
    /// </summary>
    Task<List<CsfEntry>> ReadCsfAsync(string csfFilePath);

    /// <summary>
    /// كتابة مدخلات CSF إلى ملف
    /// </summary>
    Task WriteCsfAsync(string csfFilePath, List<CsfEntry> entries);

    /// <summary>
    /// توليد مدخلات CSF القياسية لوحدة معينة
    /// </summary>
    List<CsfEntry> GenerateEntriesForUnit(string unitName, string displayName, string description = "");
}
