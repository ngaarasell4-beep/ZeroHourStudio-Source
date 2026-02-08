using System.Collections.ObjectModel;
using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.UI.WPF.Models;

/// <summary>
/// مجموعة وحدات تابعة لفصيل واحد (للعرض المجمّع)
/// </summary>
public class FactionGroup
{
    public string FactionName { get; set; } = string.Empty;
    public ObservableCollection<SageUnit> Units { get; } = new();
}
