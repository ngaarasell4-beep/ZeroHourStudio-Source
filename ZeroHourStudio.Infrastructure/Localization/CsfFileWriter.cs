using System.Text;
using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Infrastructure.Localization;

/// <summary>
/// كاتب ملفات CSF - يكتب بالتنسيق الثنائي المطابق لمحرك SAGE
/// </summary>
public class CsfFileWriter
{
    public async Task WriteAsync(string filePath, List<CsfEntry> entries, uint language = 0)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.ASCII);

        // Header
        writer.Write((uint)0x43534620); // " FSC"
        writer.Write((uint)3);          // CSF version 3
        writer.Write((uint)entries.Count); // Number of labels
        writer.Write((uint)entries.Count); // Number of strings
        writer.Write((uint)0);           // Unused
        writer.Write(language);          // Language

        foreach (var entry in entries)
        {
            WriteLabel(writer, entry);
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(filePath, ms.ToArray());
    }

    private void WriteLabel(BinaryWriter writer, CsfEntry entry)
    {
        bool hasExtra = !string.IsNullOrEmpty(entry.ArabicText);

        // Label header: " LBL"
        writer.Write((uint)0x4C424C20);
        // Number of string pairs
        writer.Write((uint)1);
        // Label name length
        var labelBytes = Encoding.ASCII.GetBytes(entry.Label);
        writer.Write(labelBytes.Length);
        // Label name
        writer.Write(labelBytes);

        // String header
        if (hasExtra)
            writer.Write((uint)0x57525453); // "WRTS"
        else
            writer.Write((uint)0x52545320); // " RTS"

        // Encode string with XOR 0xFFFF
        var encoded = EncodeString(entry.EnglishText);
        writer.Write(entry.EnglishText.Length); // char count
        writer.Write(encoded);

        // Extra string (Arabic text for WRTS)
        if (hasExtra)
        {
            var extraBytes = Encoding.ASCII.GetBytes(entry.ArabicText);
            writer.Write(extraBytes.Length);
            writer.Write(extraBytes);
        }
    }

    /// <summary>
    /// تشفير نص CSF - UTF-16LE مع XOR 0xFFFF
    /// </summary>
    private static byte[] EncodeString(string text)
    {
        var bytes = new byte[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            ushort ch = (ushort)text[i];
            ushort encoded = (ushort)(ch ^ 0xFFFF);
            bytes[i * 2] = (byte)(encoded & 0xFF);
            bytes[i * 2 + 1] = (byte)((encoded >> 8) & 0xFF);
        }
        return bytes;
    }
}
