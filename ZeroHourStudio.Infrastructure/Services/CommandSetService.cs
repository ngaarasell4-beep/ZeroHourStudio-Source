using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Infrastructure.Services;

/// <summary>
/// خدمة تغليف لعمليات CommandSet لتوحيد الاستخدام
/// </summary>
public class CommandSetService
{
    private readonly CommandSetPatchService _patchService = new();

    public Task<CommandSetPatchResult> EnsureCommandSetAsync(
        SageUnit unit,
        Dictionary<string, string> unitData,
        string targetModPath)
    {
        return _patchService.EnsureCommandSetAsync(unit, unitData, targetModPath);
    }

    public Task<string?> FindRealCommandSetName(string targetModPath, string commandSetName)
    {
        return _patchService.FindRealCommandSetName(targetModPath, commandSetName);
    }
}
