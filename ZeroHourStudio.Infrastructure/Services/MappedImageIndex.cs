using System.Text.RegularExpressions;
using ZeroHourStudio.Infrastructure.Archives;

namespace ZeroHourStudio.Infrastructure.Services;

public record MappedImageEntry(string ImageName, string TextureFile, int TextureWidth, int TextureHeight, int Left, int Top, int Right, int Bottom);

public class MappedImageIndex
{
    private readonly Dictionary<string, MappedImageEntry> _index = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _index.Count;

    public MappedImageEntry? Find(string imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName)) return null;
        _index.TryGetValue(imageName, out var entry);
        return entry;
    }

    public async Task BuildIndexAsync(string modPath)
    {
        _index.Clear();

        // Scan loose MappedImages INI files
        var mappedImageDirs = new[]
        {
            Path.Combine(modPath, "Data", "INI", "MappedImages"),
            Path.Combine(modPath, "INI", "MappedImages"),
            Path.Combine(modPath, "Data", "INI", "MappedImages", "HandCreated"),
            Path.Combine(modPath, "Data", "INI", "MappedImages", "TextureSize_512"),
        };

        foreach (var dir in mappedImageDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.ini", SearchOption.AllDirectories))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    ParseMappedImages(content);
                }
                catch { }
            }
        }

        // Scan BIG archives for MappedImages INIs
        var bigFiles = Directory.Exists(modPath)
            ? Directory.GetFiles(modPath, "*.big", SearchOption.AllDirectories)
            : Array.Empty<string>();

        foreach (var bigPath in bigFiles)
        {
            try
            {
                using var manager = new BigArchiveManager(bigPath);
                await manager.LoadAsync();

                var entries = manager.GetFileList()
                    .Where(e => e.Contains("MappedImages", StringComparison.OrdinalIgnoreCase) &&
                                e.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var entry in entries)
                {
                    try
                    {
                        var data = await manager.ExtractFileAsync(entry);
                        var content = System.Text.Encoding.GetEncoding(1252).GetString(data);
                        ParseMappedImages(content);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private void ParseMappedImages(string content)
    {
        var blocks = Regex.Split(content, @"(?=MappedImage\s)", RegexOptions.IgnoreCase);

        foreach (var block in blocks)
        {
            var nameMatch = Regex.Match(block, @"^MappedImage\s+(\S+)", RegexOptions.IgnoreCase);
            if (!nameMatch.Success) continue;

            var imageName = nameMatch.Groups[1].Value.Trim();

            var textureMatch = Regex.Match(block, @"Texture\s*=?\s*(\S+)", RegexOptions.IgnoreCase);
            if (!textureMatch.Success) continue;
            var textureFile = textureMatch.Groups[1].Value.Trim();

            var twMatch = Regex.Match(block, @"TextureWidth\s*=?\s*(\d+)", RegexOptions.IgnoreCase);
            var thMatch = Regex.Match(block, @"TextureHeight\s*=?\s*(\d+)", RegexOptions.IgnoreCase);
            int tw = twMatch.Success ? int.Parse(twMatch.Groups[1].Value) : 512;
            int th = thMatch.Success ? int.Parse(thMatch.Groups[1].Value) : 512;

            int left = 0, top = 0, right = tw, bottom = th;
            var coordsMatch = Regex.Match(block, @"Coords\s*=?\s*Left:\s*(\d+)\s+Top:\s*(\d+)\s+Right:\s*(\d+)\s+Bottom:\s*(\d+)", RegexOptions.IgnoreCase);
            if (coordsMatch.Success)
            {
                left = int.Parse(coordsMatch.Groups[1].Value);
                top = int.Parse(coordsMatch.Groups[2].Value);
                right = int.Parse(coordsMatch.Groups[3].Value);
                bottom = int.Parse(coordsMatch.Groups[4].Value);
            }

            _index[imageName] = new MappedImageEntry(imageName, textureFile, tw, th, left, top, right, bottom);
        }
    }
}
