using ZeroHourStudio.Application.Models;

namespace ZeroHourStudio.Infrastructure.ConflictResolution;

/// <summary>
/// نظام ملفات افتراضي - يربط أنواع التبعيات بمسارات SAGE الصحيحة
/// </summary>
public class VirtualFileSystem
{
    // خريطة أنواع الملفات إلى مسارات SAGE القياسية
    private static readonly Dictionary<string, string> ExtensionToPath = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".w3d", "Art/W3D" },
        { ".wak", "Art/W3D" },
        { ".dds", "Art/Textures" },
        { ".tga", "Art/Textures" },
        { ".bmp", "Art/Textures" },
        { ".jpg", "Art/Textures" },
        { ".png", "Art/Textures" },
        { ".wav", "Data/Audio/Sounds" },
        { ".mp3", "Data/Audio/Sounds" },
        { ".ini", "Data/INI" },
        { ".str", "Data/INI" },
        { ".csf", "Data" },
        { ".wnd", "Window" },
        { ".map", "Maps" },
    };

    // خريطة أنواع التبعيات إلى مجلدات SAGE
    private static readonly Dictionary<DependencyType, string> TypeToFolder = new()
    {
        { DependencyType.Model3D, "Art/W3D" },
        { DependencyType.Texture, "Art/Textures" },
        { DependencyType.Audio, "Data/Audio/Sounds" },
        { DependencyType.VisualEffect, "Art/W3D" },
        { DependencyType.Weapon, "Data/INI" },
        { DependencyType.FXList, "Data/INI" },
        { DependencyType.Projectile, "Data/INI" },
        { DependencyType.Armor, "Data/INI" },
        { DependencyType.ObjectINI, "Data/INI" },
        { DependencyType.Custom, "Data/INI" },
    };

    /// <summary>
    /// الحصول على مسار الهدف الصحيح لملف بناءً على نوع التبعية وامتداد الملف
    /// </summary>
    public string ResolveTargetPath(string fileName, DependencyType type, string targetModRoot)
    {
        var ext = Path.GetExtension(fileName);

        // أولاً: حاول عبر الامتداد
        if (!string.IsNullOrEmpty(ext) && ExtensionToPath.TryGetValue(ext, out var extPath))
        {
            return Path.Combine(targetModRoot, extPath, fileName);
        }

        // ثانياً: حاول عبر نوع التبعية
        if (TypeToFolder.TryGetValue(type, out var typeFolder))
        {
            return Path.Combine(targetModRoot, typeFolder, fileName);
        }

        // افتراضي: Data/INI
        return Path.Combine(targetModRoot, "Data/INI", fileName);
    }

    /// <summary>
    /// الحصول على المسار النسبي الصحيح لملف في بنية SAGE
    /// </summary>
    public string GetRelativeSagePath(string fileName, DependencyType type)
    {
        var ext = Path.GetExtension(fileName);

        if (!string.IsNullOrEmpty(ext) && ExtensionToPath.TryGetValue(ext, out var extPath))
        {
            return Path.Combine(extPath, fileName).Replace('\\', '/');
        }

        if (TypeToFolder.TryGetValue(type, out var typeFolder))
        {
            return Path.Combine(typeFolder, fileName).Replace('\\', '/');
        }

        return Path.Combine("Data/INI", fileName).Replace('\\', '/');
    }

    /// <summary>
    /// تأكد من وجود هيكل المجلدات المطلوب في الهدف
    /// </summary>
    public void EnsureDirectoryStructure(string targetModRoot)
    {
        var requiredDirs = new[]
        {
            "Art/W3D",
            "Art/Textures",
            "Data/Audio/Sounds",
            "Data/Audio/Speech",
            "Data/INI",
            "Data/INI/Object",
            "Window",
        };

        foreach (var dir in requiredDirs)
        {
            var fullPath = Path.Combine(targetModRoot, dir);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);
        }
    }

    /// <summary>
    /// الحصول على نوع التبعية من امتداد الملف
    /// </summary>
    public static DependencyType InferTypeFromExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".w3d" or ".wak" => DependencyType.Model3D,
            ".dds" or ".tga" or ".bmp" or ".jpg" or ".png" => DependencyType.Texture,
            ".wav" or ".mp3" => DependencyType.Audio,
            _ => DependencyType.Custom
        };
    }
}
