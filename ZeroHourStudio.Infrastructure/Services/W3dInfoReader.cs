using System.Text;
using ZeroHourStudio.Infrastructure.Archives;

namespace ZeroHourStudio.Infrastructure.Services;

/// <summary>
/// معلومات Mesh واحد من ملف W3D
/// </summary>
public class W3dMeshInfo
{
    public string MeshName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public int VertexCount { get; set; }
    public int FaceCount { get; set; }
}

/// <summary>
/// معلومات ملف W3D
/// </summary>
public class W3dFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public List<W3dMeshInfo> Meshes { get; set; } = new();
    public List<string> TextureNames { get; set; } = new();
    public int TotalVertices => Meshes.Sum(m => m.VertexCount);
    public int TotalFaces => Meshes.Sum(m => m.FaceCount);
    public string HierarchyName { get; set; } = string.Empty;
    public int AnimationCount { get; set; }
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// قارئ معلومات ملفات W3D (Westwood 3D) - يستخرج البيانات الوصفية من الصيغة الثنائية
/// يدعم القراءة من الملفات المفكوكة وأرشيفات BIG
/// </summary>
public class W3dInfoReader
{
    // W3D Chunk Types
    private const uint W3D_CHUNK_MESH = 0x00000000;
    private const uint W3D_CHUNK_MESH_HEADER3 = 0x0000001F;
    private const uint W3D_CHUNK_VERTICES = 0x00000002;
    private const uint W3D_CHUNK_TRIANGLES = 0x00000020;
    private const uint W3D_CHUNK_TEXTURE_NAME = 0x00000031;
    private const uint W3D_CHUNK_HIERARCHY = 0x00000100;
    private const uint W3D_CHUNK_HIERARCHY_HEADER = 0x00000101;
    private const uint W3D_CHUNK_ANIMATION = 0x00000200;
    private const uint W3D_CHUNK_COMPRESSED_ANIMATION = 0x00000280;
    private const uint W3D_CHUNK_HLOD = 0x00000700;

    /// <summary>
    /// قراءة معلومات W3D من ملف على القرص
    /// </summary>
    public async Task<W3dFileInfo> ReadFromFileAsync(string filePath)
    {
        var info = new W3dFileInfo { FileName = Path.GetFileName(filePath) };
        try
        {
            if (!File.Exists(filePath))
            {
                info.ErrorMessage = "الملف غير موجود";
                return info;
            }

            var data = await File.ReadAllBytesAsync(filePath);
            info.FileSize = data.Length;
            ParseW3dData(data, info);
        }
        catch (Exception ex)
        {
            info.ErrorMessage = ex.Message;
        }
        return info;
    }

    /// <summary>
    /// قراءة معلومات W3D من أرشيف BIG
    /// </summary>
    public async Task<W3dFileInfo> ReadFromArchiveAsync(string archivePath, string entryPath)
    {
        var info = new W3dFileInfo { FileName = Path.GetFileName(entryPath) };
        try
        {
            using var mgr = new BigArchiveManager(archivePath);
            await mgr.LoadAsync();
            var data = await mgr.ExtractFileAsync(entryPath);
            info.FileSize = data.Length;
            ParseW3dData(data, info);
        }
        catch (Exception ex)
        {
            info.ErrorMessage = ex.Message;
        }
        return info;
    }

    /// <summary>
    /// قراءة معلومات W3D من مسار المود (يبحث في الملفات المفكوكة و BIG)
    /// </summary>
    public async Task<W3dFileInfo> ReadFromModAsync(string modPath, string w3dFileName)
    {
        if (string.IsNullOrWhiteSpace(w3dFileName))
            return new W3dFileInfo { ErrorMessage = "اسم الملف فارغ" };

        // Ensure .w3d extension
        if (!w3dFileName.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase))
            w3dFileName += ".w3d";

        // Try loose files first
        var loosePaths = new[]
        {
            Path.Combine(modPath, "Art", "W3D", w3dFileName),
            Path.Combine(modPath, "W3D", w3dFileName),
            Path.Combine(modPath, "Art", w3dFileName),
        };

        foreach (var p in loosePaths)
        {
            if (File.Exists(p))
                return await ReadFromFileAsync(p);
        }

        // Try BIG archives
        if (Directory.Exists(modPath))
        {
            foreach (var bigFile in Directory.GetFiles(modPath, "*.big", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var mgr = new BigArchiveManager(bigFile);
                    await mgr.LoadAsync();
                    var match = mgr.GetFileList()
                        .FirstOrDefault(e => e.EndsWith(w3dFileName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        var data = await mgr.ExtractFileAsync(match);
                        var info = new W3dFileInfo
                        {
                            FileName = w3dFileName,
                            FileSize = data.Length
                        };
                        ParseW3dData(data, info);
                        return info;
                    }
                }
                catch { /* skip */ }
            }
        }

        return new W3dFileInfo
        {
            FileName = w3dFileName,
            ErrorMessage = "لم يتم العثور على الملف في المود"
        };
    }

    private void ParseW3dData(byte[] data, W3dFileInfo info)
    {
        if (data.Length < 8)
        {
            info.ErrorMessage = "ملف صغير جداً";
            return;
        }

        try
        {
            int offset = 0;
            W3dMeshInfo? currentMesh = null;

            while (offset + 8 <= data.Length)
            {
                uint chunkType = BitConverter.ToUInt32(data, offset);
                uint chunkSizeRaw = BitConverter.ToUInt32(data, offset + 4);
                bool hasSubChunks = (chunkSizeRaw & 0x80000000) != 0;
                int chunkSize = (int)(chunkSizeRaw & 0x7FFFFFFF);
                int dataStart = offset + 8;

                if (chunkSize < 0 || dataStart + chunkSize > data.Length)
                    break;

                switch (chunkType)
                {
                    case W3D_CHUNK_MESH:
                        currentMesh = new W3dMeshInfo();
                        info.Meshes.Add(currentMesh);
                        // Has sub-chunks, recurse into them
                        if (hasSubChunks)
                        {
                            ParseMeshSubChunks(data, dataStart, chunkSize, currentMesh, info);
                        }
                        break;

                    case W3D_CHUNK_HIERARCHY:
                        if (hasSubChunks)
                            ParseHierarchySubChunks(data, dataStart, chunkSize, info);
                        break;

                    case W3D_CHUNK_ANIMATION:
                    case W3D_CHUNK_COMPRESSED_ANIMATION:
                        info.AnimationCount++;
                        break;

                    case W3D_CHUNK_HLOD:
                        // HLOD has hierarchy info
                        break;
                }

                offset = dataStart + chunkSize;
            }

            info.IsValid = true;
        }
        catch (Exception ex)
        {
            info.ErrorMessage = $"خطأ في التحليل: {ex.Message}";
        }
    }

    private void ParseMeshSubChunks(byte[] data, int start, int length, W3dMeshInfo mesh, W3dFileInfo info)
    {
        int offset = start;
        int end = start + length;

        while (offset + 8 <= end)
        {
            uint chunkType = BitConverter.ToUInt32(data, offset);
            uint chunkSizeRaw = BitConverter.ToUInt32(data, offset + 4);
            int chunkSize = (int)(chunkSizeRaw & 0x7FFFFFFF);
            int dataStart = offset + 8;

            if (chunkSize < 0 || dataStart + chunkSize > end)
                break;

            switch (chunkType)
            {
                case W3D_CHUNK_MESH_HEADER3:
                    // MeshHeader3: version(4) + attrs(4) + meshName(16*2) + containerName(16*2) + faceCount(4) + vertCount(4) + ...
                    if (chunkSize >= 44)
                    {
                        // version = 4 bytes, attrs = 4 bytes
                        int nameOffset = dataStart + 8;
                        mesh.MeshName = ReadFixedString(data, nameOffset, 16);
                        mesh.ContainerName = ReadFixedString(data, nameOffset + 16, 16);
                        // face count at offset 40, vert count at offset 44
                        if (dataStart + 44 <= data.Length)
                            mesh.FaceCount = BitConverter.ToInt32(data, dataStart + 40);
                        if (dataStart + 48 <= data.Length)
                            mesh.VertexCount = BitConverter.ToInt32(data, dataStart + 44);
                    }
                    break;

                case W3D_CHUNK_VERTICES:
                    // Each vertex is 12 bytes (3 floats)
                    if (mesh.VertexCount == 0 && chunkSize >= 12)
                        mesh.VertexCount = chunkSize / 12;
                    break;

                case W3D_CHUNK_TRIANGLES:
                    // Each triangle is 32 bytes
                    if (mesh.FaceCount == 0 && chunkSize >= 32)
                        mesh.FaceCount = chunkSize / 32;
                    break;

                case W3D_CHUNK_TEXTURE_NAME:
                    var texName = ReadNullTerminatedString(data, dataStart, chunkSize);
                    if (!string.IsNullOrWhiteSpace(texName) &&
                        !info.TextureNames.Contains(texName, StringComparer.OrdinalIgnoreCase))
                        info.TextureNames.Add(texName);
                    break;
            }

            offset = dataStart + chunkSize;
        }
    }

    private void ParseHierarchySubChunks(byte[] data, int start, int length, W3dFileInfo info)
    {
        int offset = start;
        int end = start + length;

        while (offset + 8 <= end)
        {
            uint chunkType = BitConverter.ToUInt32(data, offset);
            uint chunkSizeRaw = BitConverter.ToUInt32(data, offset + 4);
            int chunkSize = (int)(chunkSizeRaw & 0x7FFFFFFF);
            int dataStart = offset + 8;

            if (chunkSize < 0 || dataStart + chunkSize > end)
                break;

            if (chunkType == W3D_CHUNK_HIERARCHY_HEADER && chunkSize >= 24)
            {
                // version(4) + name(16) + numPivots(4)
                info.HierarchyName = ReadFixedString(data, dataStart + 4, 16);
            }

            offset = dataStart + chunkSize;
        }
    }

    private static string ReadFixedString(byte[] data, int offset, int maxLength)
    {
        if (offset + maxLength > data.Length) maxLength = data.Length - offset;
        if (maxLength <= 0) return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < maxLength; i++)
        {
            byte b = data[offset + i];
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    private static string ReadNullTerminatedString(byte[] data, int offset, int maxLength)
    {
        if (offset + maxLength > data.Length) maxLength = data.Length - offset;
        if (maxLength <= 0) return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < maxLength; i++)
        {
            byte b = data[offset + i];
            if (b == 0) break;
            sb.Append((char)b);
        }
        return sb.ToString();
    }
}
