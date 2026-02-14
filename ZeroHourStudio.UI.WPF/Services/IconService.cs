using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeroHourStudio.Infrastructure.Services;
using ZeroHourStudio.Infrastructure.Archives;
using ZeroHourStudio.Infrastructure.Logging;

namespace ZeroHourStudio.UI.WPF.Services;

public class IconService
{
    private readonly MappedImageIndex _mappedIndex;
    private readonly ConcurrentDictionary<string, BitmapSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte[]> _texturePixelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (int W, int H)> _textureSizeCache = new(StringComparer.OrdinalIgnoreCase);
    private string _modPath = string.Empty;
    public int PreloadedCount { get; private set; }

    public IconService(MappedImageIndex mappedIndex)
    {
        _mappedIndex = mappedIndex;
    }

    public void SetModPath(string modPath) => _modPath = modPath;

    /// <summary>
    /// Cache-only lookup. Never blocks, never opens files. Returns null if not pre-loaded.
    /// </summary>
    public BitmapSource? GetIcon(string buttonImageName)
    {
        if (string.IsNullOrWhiteSpace(buttonImageName)) return null;
        _iconCache.TryGetValue(buttonImageName, out var bmp);
        return bmp;
    }

    /// <summary>
    /// Pre-load all icons for the given button image names.
    /// Must be called from background thread. Bitmaps are Frozen for cross-thread use.
    /// </summary>
    public async Task PreloadIconsAsync(IEnumerable<string> buttonImageNames)
    {
        PreloadedCount = 0;
        BlackBoxRecorder.Record("ICON", "PRELOAD_START", $"ModPath={_modPath}");
        var iconSw = System.Diagnostics.Stopwatch.StartNew();

        // 1) Collect unique texture files needed
        var neededTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var neededEntries = new List<(string ImageName, MappedImageEntry Entry)>();

        foreach (var name in buttonImageNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var entry = _mappedIndex.Find(name);
            if (entry == null)
            {
                _iconCache[name] = null;
                BlackBoxRecorder.RecordIconSearch(name, Array.Empty<string>(), false, "MappedImage entry not found in index");
                continue;
            }
            neededTextures.Add(entry.TextureFile);
            neededEntries.Add((name, entry));
        }

        // 2) Pre-load all needed textures (loose files + BIG archives)
        await Task.Run(() => PreloadTextures(neededTextures));

        // 3) Crop all icons from cached textures
        var failedIcons = new List<string>();
        foreach (var (imageName, entry) in neededEntries)
        {
            try
            {
                var bmp = CropIcon(entry);
                _iconCache[imageName] = bmp;
                if (bmp != null)
                {
                    PreloadedCount++;
                    BlackBoxRecorder.RecordIconSearch(imageName, new[] { entry.TextureFile }, true);
                }
                else
                {
                    failedIcons.Add(imageName);
                    BlackBoxRecorder.RecordIconSearch(imageName, new[] { entry.TextureFile }, false, "CropIcon returned null (texture not loaded or coords invalid)");
                }
            }
            catch (Exception ex)
            {
                _iconCache[imageName] = null;
                failedIcons.Add(imageName);
                BlackBoxRecorder.RecordIconSearch(imageName, new[] { entry.TextureFile }, false, $"CropIcon exception: {ex.Message}");
            }
        }

        iconSw.Stop();
        BlackBoxRecorder.Record("ICON", "PRELOAD_END", $"Loaded={PreloadedCount} Failed={neededEntries.Count - PreloadedCount} Textures={neededTextures.Count} Elapsed={iconSw.ElapsedMilliseconds}ms");
        
        // تقرير مفصل بالأيقونات الفاشلة
        if (failedIcons.Count > 0)
        {
            var failedList = string.Join(", ", failedIcons.Take(10));
            if (failedIcons.Count > 10)
                failedList += $" ... and {failedIcons.Count - 10} more";
            
            BlackBoxRecorder.Record("ICON", "FAILED_LIST", failedList);
        }
    }

    private void PreloadTextures(HashSet<string> textureFiles)
    {
        // Build archive index: filename -> archivePath::entryPath
        var archiveIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(_modPath))
        {
            var bigFiles = Directory.GetFiles(_modPath, "*.big", SearchOption.TopDirectoryOnly);
            foreach (var bigPath in bigFiles)
            {
                try
                {
                    using var manager = new BigArchiveManager(bigPath);
                    manager.LoadAsync().GetAwaiter().GetResult();
                    foreach (var entry in manager.GetFileList())
                    {
                        var ext = Path.GetExtension(entry).ToLowerInvariant();
                        if (ext == ".tga" || ext == ".dds")
                        {
                            var fileName = Path.GetFileName(entry);
                            if (textureFiles.Contains(fileName) && !archiveIndex.ContainsKey(fileName))
                                archiveIndex[fileName] = $"{bigPath}::{entry}";
                        }
                    }
                }
                catch (Exception ex) { BlackBoxRecorder.Record("ICON_SERVICE", "ARCHIVE_LOAD_ERROR", $"Archive={bigPath}, Error={ex.Message}"); }
            }
        }

        foreach (var texFile in textureFiles)
        {
            if (_texturePixelCache.ContainsKey(texFile)) continue;

            // Try loose files first
            var loaded = TryLoadLooseTexture(texFile);
            if (loaded) continue;

            // Try BIG archive
            if (archiveIndex.TryGetValue(texFile, out var archRef))
            {
                TryLoadArchiveTexture(texFile, archRef);
            }
        }
    }

    private bool TryLoadLooseTexture(string textureFileName)
    {
        var searchPaths = new[]
        {
            Path.Combine(_modPath, "Art", "Textures", textureFileName),
            Path.Combine(_modPath, "Data", "Art", "Textures", textureFileName),
        };

        foreach (var p in searchPaths)
        {
            if (!File.Exists(p)) continue;
            try
            {
                var data = File.ReadAllBytes(p);
                var ext = Path.GetExtension(p).ToLowerInvariant();
                byte[]? pixels = null;
                int w = 0, h = 0;
                if (ext == ".tga") pixels = LoadTgaFromBytes(data, out w, out h);
                else if (ext == ".dds") pixels = LoadDdsFromBytes(data, out w, out h);
                if (pixels != null && w > 0 && h > 0)
                {
                    _texturePixelCache[textureFileName] = pixels;
                    _textureSizeCache[textureFileName] = (w, h);
                    BlackBoxRecorder.RecordIconLoad(textureFileName, true, $"LooseFile={p}");
                    return true;
                }
                else
                {
                    BlackBoxRecorder.RecordIconLoad(textureFileName, false, $"LooseFile={p}", "Decode returned null or zero size");
                }
            }
            catch (Exception ex)
            {
                BlackBoxRecorder.RecordIconLoad(textureFileName, false, $"LooseFile={p}", ex.Message);
            }
        }
        return false;
    }

    private void TryLoadArchiveTexture(string textureFileName, string archiveRef)
    {
        try
        {
            var parts = archiveRef.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return;

            using var manager = new BigArchiveManager(parts[0]);
            manager.LoadAsync().GetAwaiter().GetResult();
            var data = manager.ExtractFileAsync(parts[1]).GetAwaiter().GetResult();

            var ext = Path.GetExtension(textureFileName).ToLowerInvariant();
            byte[]? pixels = null;
            int w = 0, h = 0;
            if (ext == ".tga") pixels = LoadTgaFromBytes(data, out w, out h);
            else if (ext == ".dds") pixels = LoadDdsFromBytes(data, out w, out h);
            if (pixels != null && w > 0 && h > 0)
            {
                _texturePixelCache[textureFileName] = pixels;
                _textureSizeCache[textureFileName] = (w, h);
            }
        }
        catch (Exception ex) { BlackBoxRecorder.Record("ICON_SERVICE", "TEXTURE_LOAD_ERROR", $"Texture={textureFileName}, ArchiveRef={archiveRef}, Error={ex.Message}"); }
    }

    private BitmapSource? CropIcon(MappedImageEntry entry)
    {
        if (!_texturePixelCache.TryGetValue(entry.TextureFile, out var texturePixels)) return null;
        if (!_textureSizeCache.TryGetValue(entry.TextureFile, out var size)) return null;

        int texW = size.W, texH = size.H;
        int left = Math.Max(0, entry.Left);
        int top = Math.Max(0, entry.Top);
        int right = Math.Min(texW, entry.Right);
        int bottom = Math.Min(texH, entry.Bottom);
        int cropW = right - left;
        int cropH = bottom - top;
        if (cropW <= 0 || cropH <= 0) return null;

        var cropped = new byte[cropW * cropH * 4];
        for (int y = 0; y < cropH; y++)
        {
            int srcOffset = ((top + y) * texW + left) * 4;
            int dstOffset = y * cropW * 4;
            if (srcOffset + cropW * 4 > texturePixels.Length) break;
            Buffer.BlockCopy(texturePixels, srcOffset, cropped, dstOffset, cropW * 4);
        }

        var bmp = BitmapSource.Create(cropW, cropH, 96, 96, PixelFormats.Bgra32, null, cropped, cropW * 4);
        bmp.Freeze();
        return bmp;
    }

    private static byte[]? LoadTgaFromBytes(byte[] data, out int width, out int height)
    {
        width = 0; height = 0;
        try
        {
            if (data.Length < 18) return null;
            int idLength = data[0];
            int colorMapType = data[1];
            int imageType = data[2];
            width = BitConverter.ToUInt16(data, 12);
            height = BitConverter.ToUInt16(data, 14);
            int bpp = data[16];
            int descriptor = data[17];
            bool topToBottom = (descriptor & 0x20) != 0;
            int bytesPerPixel = bpp / 8;
            if (bytesPerPixel < 3 || bytesPerPixel > 4) return null;
            int colorMapLength = BitConverter.ToUInt16(data, 5);
            int colorMapEntrySize = data[7];
            int colorMapBytes = colorMapType != 0 ? colorMapLength * (colorMapEntrySize / 8) : 0;
            int pixelDataOffset = 18 + idLength + colorMapBytes;
            var pixels = new byte[width * height * 4];

            if (imageType == 2)
            {
                for (int i = 0; i < width * height; i++)
                {
                    int srcIdx = pixelDataOffset + i * bytesPerPixel;
                    if (srcIdx + bytesPerPixel > data.Length) break;
                    int row = i / width, col = i % width;
                    int destRow = topToBottom ? row : (height - 1 - row);
                    int dstIdx = (destRow * width + col) * 4;
                    pixels[dstIdx] = data[srcIdx]; pixels[dstIdx + 1] = data[srcIdx + 1];
                    pixels[dstIdx + 2] = data[srcIdx + 2];
                    pixels[dstIdx + 3] = bytesPerPixel == 4 ? data[srcIdx + 3] : (byte)255;
                }
            }
            else if (imageType == 10)
            {
                int srcIdx = pixelDataOffset;
                int pixelIndex = 0;
                while (pixelIndex < width * height && srcIdx < data.Length)
                {
                    byte header = data[srcIdx++];
                    int count = (header & 0x7F) + 1;
                    bool isRle = (header & 0x80) != 0;
                    if (isRle)
                    {
                        if (srcIdx + bytesPerPixel > data.Length) break;
                        byte b = data[srcIdx], g = data[srcIdx + 1], r = data[srcIdx + 2];
                        byte a = bytesPerPixel == 4 ? data[srcIdx + 3] : (byte)255;
                        srcIdx += bytesPerPixel;
                        for (int j = 0; j < count && pixelIndex < width * height; j++, pixelIndex++)
                        {
                            int row = pixelIndex / width, col = pixelIndex % width;
                            int destRow = topToBottom ? row : (height - 1 - row);
                            int dstIdx = (destRow * width + col) * 4;
                            pixels[dstIdx] = b; pixels[dstIdx + 1] = g; pixels[dstIdx + 2] = r; pixels[dstIdx + 3] = a;
                        }
                    }
                    else
                    {
                        for (int j = 0; j < count && pixelIndex < width * height; j++, pixelIndex++)
                        {
                            if (srcIdx + bytesPerPixel > data.Length) break;
                            int row = pixelIndex / width, col = pixelIndex % width;
                            int destRow = topToBottom ? row : (height - 1 - row);
                            int dstIdx = (destRow * width + col) * 4;
                            pixels[dstIdx] = data[srcIdx]; pixels[dstIdx + 1] = data[srcIdx + 1];
                            pixels[dstIdx + 2] = data[srcIdx + 2];
                            pixels[dstIdx + 3] = bytesPerPixel == 4 ? data[srcIdx + 3] : (byte)255;
                            srcIdx += bytesPerPixel;
                        }
                    }
                }
            }
            else return null;
            return pixels;
        }
        catch { return null; }
    }

    private static byte[]? LoadDdsFromBytes(byte[] data, out int width, out int height)
    {
        width = 0; height = 0;
        try
        {
            if (data.Length < 128) return null;
            if (data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ') return null;
            height = BitConverter.ToInt32(data, 12);
            width = BitConverter.ToInt32(data, 16);
            int flags = BitConverter.ToInt32(data, 80);
            int rgbBitCount = BitConverter.ToInt32(data, 88);
            int rMask = BitConverter.ToInt32(data, 92);
            if ((flags & 0x40) != 0)
            {
                int bpp = rgbBitCount / 8;
                if (bpp < 3 || bpp > 4) return null;
                int headerSize = 128;
                var pixels = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    int srcIdx = headerSize + i * bpp;
                    if (srcIdx + bpp > data.Length) break;
                    int dstIdx = i * 4;
                    if (rMask == 0x00FF0000)
                    { pixels[dstIdx] = data[srcIdx]; pixels[dstIdx + 1] = data[srcIdx + 1]; pixels[dstIdx + 2] = data[srcIdx + 2]; }
                    else
                    { pixels[dstIdx] = data[srcIdx + 2]; pixels[dstIdx + 1] = data[srcIdx + 1]; pixels[dstIdx + 2] = data[srcIdx]; }
                    pixels[dstIdx + 3] = bpp == 4 ? data[srcIdx + 3] : (byte)255;
                }
                return pixels;
            }
            return null;
        }
        catch { return null; }
    }
}
