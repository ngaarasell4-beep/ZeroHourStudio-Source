using System.Text;
using ZeroHourStudio.Domain.Entities;

namespace ZeroHourStudio.Infrastructure.Localization;

/// <summary>
/// قارئ ملفات CSF - تنسيق ثنائي خاص بمحرك SAGE
/// CSF Format: " FSC" header, " LBL" labels, " RTS"/"WRTS" strings with XOR 0xFFFF encoding
/// </summary>
public class CsfFileReader
{
    private const uint CsfMagic = 0x43534620; // " FSC" (little-endian: " CSF")

    public async Task<List<CsfEntry>> ReadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return new List<CsfEntry>();

        var data = await File.ReadAllBytesAsync(filePath);
        return Parse(data);
    }

    private List<CsfEntry> Parse(byte[] data)
    {
        var entries = new List<CsfEntry>();

        if (data.Length < 24)
            return entries;

        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.ASCII);

        // Header: " FSC" (4 bytes)
        var magic = reader.ReadUInt32();
        // CSF version (4 bytes)
        var version = reader.ReadUInt32();
        // Number of labels (4 bytes)
        var numLabels = reader.ReadUInt32();
        // Number of strings (4 bytes)
        var numStrings = reader.ReadUInt32();
        // Unused (4 bytes)
        reader.ReadUInt32();
        // Language (4 bytes)
        var language = reader.ReadUInt32();

        for (uint i = 0; i < numLabels && ms.Position < ms.Length; i++)
        {
            try
            {
                var entry = ReadLabel(reader);
                if (entry != null)
                    entries.Add(entry);
            }
            catch
            {
                // تجاهل المدخلات التالفة
                break;
            }
        }

        return entries;
    }

    private CsfEntry? ReadLabel(BinaryReader reader)
    {
        // Label header: " LBL" (4 bytes)
        var labelMagic = reader.ReadUInt32();
        if (labelMagic != 0x4C424C20) // " LBL"
            return null;

        // Number of string pairs (4 bytes)
        var numPairs = reader.ReadUInt32();
        // Label name length (4 bytes)
        var labelLength = reader.ReadInt32();
        // Label name (ASCII)
        var labelBytes = reader.ReadBytes(labelLength);
        var label = Encoding.ASCII.GetString(labelBytes);

        string englishText = "";
        string extraText = "";

        for (uint p = 0; p < numPairs; p++)
        {
            // String header: " RTS" or "WRTS" (4 bytes)
            var stringMagic = reader.ReadUInt32();
            bool hasExtra = (stringMagic == 0x57525453); // "WRTS"

            // String length (4 bytes) - in characters (UTF-16)
            var charCount = reader.ReadInt32();

            // String data (UTF-16LE, XOR'd with 0xFFFF)
            var rawBytes = reader.ReadBytes(charCount * 2);
            var decoded = DecodeString(rawBytes, charCount);

            if (p == 0)
                englishText = decoded;

            // WRTS has an extra string
            if (hasExtra)
            {
                var extraLength = reader.ReadInt32();
                var extraBytes = reader.ReadBytes(extraLength);
                extraText = Encoding.ASCII.GetString(extraBytes);
            }
        }

        return new CsfEntry(label, englishText, extraText);
    }

    /// <summary>
    /// فك تشفير نص CSF - UTF-16LE مع XOR 0xFFFF
    /// </summary>
    private static string DecodeString(byte[] rawBytes, int charCount)
    {
        var chars = new char[charCount];
        for (int i = 0; i < charCount; i++)
        {
            int offset = i * 2;
            if (offset + 1 >= rawBytes.Length) break;

            ushort encoded = (ushort)(rawBytes[offset] | (rawBytes[offset + 1] << 8));
            ushort decoded = (ushort)(encoded ^ 0xFFFF);
            chars[i] = (char)decoded;
        }
        return new string(chars);
    }
}
