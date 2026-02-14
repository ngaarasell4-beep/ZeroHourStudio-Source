using System.Collections.ObjectModel;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.UI.WPF.Core;

namespace ZeroHourStudio.UI.WPF.ViewModels;

/// <summary>
/// ViewModel شجرة التبعيات المرئية
/// </summary>
public class DependencyGraphViewModel : ViewModelBase
{
    private ObservableCollection<DependencyNodeVM> _rootNodes = new();
    public ObservableCollection<DependencyNodeVM> RootNodes
    {
        get => _rootNodes;
        set => SetProperty(ref _rootNodes, value);
    }

    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    private int _foundCount;
    public int FoundCount
    {
        get => _foundCount;
        set => SetProperty(ref _foundCount, value);
    }

    private int _missingCount;
    public int MissingCount
    {
        get => _missingCount;
        set => SetProperty(ref _missingCount, value);
    }

    private int _weaponCount;
    public int WeaponCount
    {
        get => _weaponCount;
        set => SetProperty(ref _weaponCount, value);
    }

    private double _completionPercentage;
    public double CompletionPercentage
    {
        get => _completionPercentage;
        set => SetProperty(ref _completionPercentage, value);
    }

    public void UpdateFromGraph(UnitDependencyGraph graph)
    {
        TotalCount = graph.AllNodes.Count;
        FoundCount = graph.FoundCount;
        MissingCount = graph.MissingCount;
        CompletionPercentage = graph.GetCompletionPercentage();

        if (graph is EnhancedDependencyGraph enhanced)
        {
            WeaponCount = enhanced.WeaponCount;
        }

        // بناء شجرة UI
        var rootNodes = new ObservableCollection<DependencyNodeVM>();
        if (graph.RootNode != null)
        {
            rootNodes.Add(BuildNodeVM(graph.RootNode));
        }
        RootNodes = rootNodes;
    }

    private DependencyNodeVM BuildNodeVM(DependencyNode node)
    {
        var vm = new DependencyNodeVM
        {
            Name = node.Name,
            Type = node.Type,
            Status = node.Status,
            FullPath = node.FullPath ?? string.Empty,
        };

        // تحديد لون حسب النوع
        vm.TypeColor = node.Type switch
        {
            DependencyType.Model3D => "#00D4FF",       // سماوي
            DependencyType.Texture => "#FF6B00",       // برتقالي
            DependencyType.Audio => "#00FF88",         // أخضر
            DependencyType.Weapon => "#FF3366",        // أحمر
            DependencyType.FXList => "#FFD700",        // ذهبي
            DependencyType.VisualEffect => "#9B59B6",  // بنفسجي
            DependencyType.Projectile => "#E67E22",    // برتقالي غامق
            DependencyType.Armor => "#1ABC9C",         // فيروزي
            DependencyType.ObjectINI => "#3498DB",     // أزرق
            _ => "#8899AA"
        };

        // تحديد أيقونة حسب النوع
        vm.TypeIcon = node.Type switch
        {
            DependencyType.Model3D => "\u25A0",      // ■
            DependencyType.Texture => "\u25C6",      // ◆
            DependencyType.Audio => "\u266B",        // ♫
            DependencyType.Weapon => "\u2694",       // ⚔
            DependencyType.FXList => "\u2726",       // ✦
            DependencyType.VisualEffect => "\u2728", // ✨
            _ => "\u25CB"                            // ○
        };

        if (node.Dependencies != null)
        {
            foreach (var child in node.Dependencies)
            {
                vm.Children.Add(BuildNodeVM(child));
            }
        }

        return vm;
    }
}

/// <summary>
/// ViewModel عقدة تبعية واحدة في الشجرة
/// </summary>
public class DependencyNodeVM : ViewModelBase
{
    public string Name { get; set; } = string.Empty;
    public DependencyType Type { get; set; }
    public AssetStatus Status { get; set; }
    public string FullPath { get; set; } = string.Empty;
    public string TypeColor { get; set; } = "#8899AA";
    public string TypeIcon { get; set; } = "\uE8A5";
    public bool IsFound => Status == AssetStatus.Found;
    public string StatusText => IsFound ? "\u2713" : "\u2717";
    public ObservableCollection<DependencyNodeVM> Children { get; } = new();
}
