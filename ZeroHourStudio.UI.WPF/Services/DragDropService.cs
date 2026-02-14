using System.Windows;
using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.UI.WPF.Services;

/// <summary>
/// خدمة السحب والإفلات - مساعدات لتغليف وفك DataObject في WPF
/// </summary>
public static class DragDropService
{
    public const string UnitDataFormat = "ZeroHourStudio.SageUnit";
    public const string UnitsDataFormat = "ZeroHourStudio.SageUnits";

    /// <summary>
    /// بدء عملية سحب وحدة واحدة
    /// </summary>
    public static void BeginDragUnit(UIElement source, SageUnit unit)
    {
        var data = new DataObject(UnitDataFormat, unit);
        DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
    }

    /// <summary>
    /// بدء عملية سحب عدة وحدات
    /// </summary>
    public static void BeginDragUnits(UIElement source, List<SageUnit> units)
    {
        var data = new DataObject(UnitsDataFormat, units);
        DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
    }

    /// <summary>
    /// استخراج وحدة من حدث الإفلات
    /// </summary>
    public static SageUnit? ExtractUnit(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(UnitDataFormat))
            return e.Data.GetData(UnitDataFormat) as SageUnit;
        return null;
    }

    /// <summary>
    /// استخراج عدة وحدات من حدث الإفلات
    /// </summary>
    public static List<SageUnit>? ExtractUnits(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(UnitsDataFormat))
            return e.Data.GetData(UnitsDataFormat) as List<SageUnit>;

        // محاولة استخراج وحدة واحدة وتحويلها لقائمة
        var single = ExtractUnit(e);
        if (single != null)
            return new List<SageUnit> { single };

        return null;
    }

    /// <summary>
    /// هل يحتوي الحدث على بيانات وحدة صالحة؟
    /// </summary>
    public static bool HasUnitData(DragEventArgs e)
    {
        return e.Data.GetDataPresent(UnitDataFormat) || e.Data.GetDataPresent(UnitsDataFormat);
    }
}
