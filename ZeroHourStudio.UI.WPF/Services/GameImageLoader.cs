using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ZeroHourStudio.Domain.Models;
using ZeroHourStudio.Infrastructure.Services;

namespace ZeroHourStudio.UI.WPF.Services;

/// <summary>
/// يحوّل CommandBarResult (من المحرك العلائقي) إلى CommandButtonSlot مع أيقونات حقيقية.
/// يستخدم IconService الموجود لتحويل TGA/DDS → BitmapSource.
/// </summary>
public class GameImageLoader
{
    private readonly IconService _iconService;

    public GameImageLoader(IconService iconService)
    {
        _iconService = iconService;
    }

    /// <summary>
    /// تحويل CommandBarResult → قائمة CommandButtonSlot مع أيقونات
    /// </summary>
    public async Task<List<CommandButtonSlot>> LoadCommandBarWithIconsAsync(CommandBarResult bar)
    {
        var slots = new List<CommandButtonSlot>();

        System.Diagnostics.Debug.WriteLine(
            $"\n[GameImageLoader] ════ Loading icons for '{bar.ObjectName}' ({bar.CommandSetName}) ════");
        System.Diagnostics.Debug.WriteLine(
            $"[GameImageLoader] Total slots: {bar.TotalSlots}, Occupied: {bar.OccupiedSlots}");

        // 1. جمع كل أسماء ButtonImage للتحميل المسبق
        var imageNames = new List<string>();
        foreach (var slot in bar.Slots)
        {
            if (slot.IsOccupied && !string.IsNullOrEmpty(slot.ButtonImage))
            {
                imageNames.Add(slot.ButtonImage);
                System.Diagnostics.Debug.WriteLine(
                    $"[GameImageLoader]   Slot #{slot.SlotNumber}: ButtonImage='{slot.ButtonImage}', " +
                    $"Cmd={slot.Command}, Obj={slot.UnitObject}, Texture={slot.TexturePath ?? "NULL"}");
            }
        }

        // 2. تحميل الأيقونات مسبقاً (خلفي — TGA/DDS → BitmapSource)
        if (imageNames.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[GameImageLoader] Preloading {imageNames.Count} icons...");
            await Task.Run(() => _iconService.PreloadIconsAsync(imageNames));
            System.Diagnostics.Debug.WriteLine(
                $"[GameImageLoader] Preload complete: {_iconService.PreloadedCount}/{imageNames.Count} loaded");
        }

        // 3. بناء CommandButtonSlot لكل خانة مع الأيقونة
        int loadedCount = 0, failedCount = 0;
        foreach (var slot in bar.Slots)
        {
            var buttonSlot = new CommandButtonSlot
            {
                SlotNumber = slot.SlotNumber,
                IsEmpty = !slot.IsOccupied,
                OccupiedBy = slot.ButtonName,
                ButtonImageName = slot.ButtonImage,
                Command = slot.Command,
                Icon = slot.ButtonImage,
                Description = slot.Label ?? slot.ButtonName,
                Type = ClassifyButtonType(slot.Command),
            };

            // ربط الأيقونة الحقيقية
            if (slot.IsOccupied && !string.IsNullOrEmpty(slot.ButtonImage))
            {
                var icon = _iconService.GetIcon(slot.ButtonImage);
                buttonSlot.IconSource = icon;

                if (icon != null)
                {
                    loadedCount++;
                }
                else
                {
                    failedCount++;
                    System.Diagnostics.Debug.WriteLine(
                        $"[GameImageLoader] ✗ FAILED Slot #{slot.SlotNumber}: ButtonImage='{slot.ButtonImage}', " +
                        $"TexturePath='{slot.TexturePath ?? "NULL"}' — icon not loaded");
                }
            }

            slots.Add(buttonSlot);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[GameImageLoader] ════ Result: {loadedCount} icons loaded, {failedCount} failed ════\n");

        return slots;
    }

    /// <summary>
    /// تحميل أيقونة واحدة بالاسم
    /// </summary>
    public BitmapSource? GetIcon(string buttonImageName)
    {
        return _iconService.GetIcon(buttonImageName);
    }

    /// <summary>
    /// تصنيف نوع الزر حسب الأمر
    /// </summary>
    private static ButtonType ClassifyButtonType(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return ButtonType.Empty;

        return command.ToUpperInvariant() switch
        {
            "DO_PRODUCE" => ButtonType.Unit,
            "DO_UPGRADE" => ButtonType.Upgrade,
            "DO_SPECIAL_POWER" or "DO_SPECIAL_POWER_FROM_COMMAND_CENTER" => ButtonType.SpecialPower,
            _ => ButtonType.Command
        };
    }
}
