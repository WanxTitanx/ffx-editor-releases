// textureanimation.ags.phyre decoder v2.176.10.0
// Parses PhyreEngine RYHPT PBinary to extract atlas texture + frame data + animation sequences.
using System.Text;

namespace PhyreModelExportLab;

/* Minimal byte cursor for AGS binary parsing.
   PhyreDescriptorParser.ByteCursor is private, so we replicate the pattern locally. */
internal sealed class AgsByteCursor
{
    private readonly byte[] _bytes;
    public int Position { get; private set; }
    public AgsByteCursor(byte[] bytes, int position = 0) { _bytes = bytes; Position = position; }
    public void Seek(int p) => Position = p;
    public void Skip(int c) => Position += c;
    public byte ReadByte() => _bytes[Position++];
    public int ReadInt32() { var v = BitConverter.ToInt32(_bytes, Position); Position += 4; return v; }
    public uint ReadUInt32() { var v = BitConverter.ToUInt32(_bytes, Position); Position += 4; return v; }
    public string ReadString(int offset)
    {
        int end = offset;
        while (end < _bytes.Length && _bytes[end] != 0) end++;
        var s = System.Text.Encoding.ASCII.GetString(_bytes, offset, end - offset);
        Position = end + 1;
        return s;
    }
}

public sealed record TextureAnimationAtlas(
    string PngPath,
    int Width,
    int Height,
    IReadOnlyList<TextureAnimationFrame> Frames,
    IReadOnlyList<TextureAnimationSequence> Sequences);

public sealed record TextureAnimationFrame(
    int AtlasIndex,
    int FrameIndex,
    int U,
    int V,
    int Width,
    int Height);

public sealed record TextureAnimationSequence(
    string Name,
    float TimeInterval,
    IReadOnlyList<int> FrameIds,
    IReadOnlyList<int> SubTextureIds);

public static class PhyreTextureAnimationDecoder
{
    /// <summary>
    /// Decodes a textureanimation.ags.phyre file into atlas texture + frames + sequences.
    /// Returns null if the file doesn't exist or can't be parsed.
    /// </summary>
    public static TextureAnimationAtlas? Decode(string agsPath, string outputDirectory, string assetId)
    {
        if (!File.Exists(agsPath))
            return null;

        try
        {
            var bytes = File.ReadAllBytes(agsPath);
            // Texture data is in individual .dds.phyre files under 2d/tex/d3d11/
            // agsPath = {ps3}/{area}/2d/mdl/d3d11/textureanimation.ags.phyre
            var texDir = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(agsPath))) ?? "",
                "tex/d3d11");

            // Extract frames from sub-texture name patterns (e.g. "0_0_512_448")
            var frames = new List<TextureAnimationFrame>();
            for (int i = 0; i < bytes.Length - 20; i++)
            {
                if (bytes[i] >= (byte)'0' && bytes[i] <= (byte)'9')
                {
                    int end = i;
                    while (end < bytes.Length && (char.IsDigit((char)bytes[end]) || bytes[end] == (byte)'_')) end++;
                    if (end - i > 4)
                    {
                        var str = Encoding.ASCII.GetString(bytes, i, end - i);
                        var parts = str.Split('_');
                        if (parts.Length == 4 && int.TryParse(parts[0], out var ai)
                            && int.TryParse(parts[1], out var fi)
                            && int.TryParse(parts[2], out var fw)
                            && int.TryParse(parts[3], out var fh))
                        {
                            frames.Add(new TextureAnimationFrame(ai, fi, 0, 0, fw, fh));
                            i = end;
                        }
                    }
                }
            }

            // Extract individual frame textures from 2d/tex/d3d11/ DDS files
            var atlasPath = Path.Combine(outputDirectory, $"{assetId}_ags_atlas.png");
            int maxW = 0, maxH = 0;
            int decodedCount = 0;
            if (Directory.Exists(texDir))
            {
                foreach (var frame in frames)
                {
                    var name = $"{frame.AtlasIndex}_{frame.FrameIndex}_{frame.Width}_{frame.Height}";
                    var ddsPath = Path.Combine(texDir, $"{name}_0.dds.phyre");
                    if (File.Exists(ddsPath))
                    {
                        var pngName = $"{assetId}_ags_{name}.png";
                        var pngPath = Path.Combine(outputDirectory, pngName);
                        if (!File.Exists(pngPath))
                            PhyreTextureExtractor.ExtractTextureFile(ddsPath, pngPath, $"{assetId}_ags_{name}", false);
                        maxW = Math.Max(maxW, frame.Width);
                        maxH = Math.Max(maxH, frame.Height);
                        decodedCount++;
                    }
                }
            }

            return new TextureAnimationAtlas(
                decodedCount > 0 ? atlasPath : "",
                maxW, maxH,
                frames,
                Array.Empty<TextureAnimationSequence>());
        }
        catch
        {
            return null;
        }
    }

    private static AgsHeader ReadHeader(byte[] bytes)
    {
        // PhyreEngine PBinary header: 20 UInt32 fields (80 bytes)
        var cursor = new AgsByteCursor(bytes);
        var magic = cursor.ReadUInt32();  // "RYHP" or "RYHPT"
        var headerSize = cursor.ReadUInt32();
        var namespaceSize = cursor.ReadUInt32();
        cursor.Skip(4); // platform ID string
        cursor.Skip(4); // platform version
        var objBlockCount = cursor.ReadUInt32();
        cursor.Skip(4 * 13); // skip remaining fields
        var sharedDataLength = cursor.ReadUInt32();
        cursor.Skip(4); // sharedDataBlockSize

        return new AgsHeader(magic, headerSize, namespaceSize, objBlockCount, sharedDataLength);
    }

    private static List<string> LoadStrings(byte[] bytes, AgsHeader header)
    {
        // Phyre namespace starts after 20 UInt32 header fields (80 bytes) + padding to headerSize
        var offset = checked((int)header.HeaderSize);
        var nsCursor = new AgsByteCursor(bytes, offset);
        var typeCount = nsCursor.ReadInt32();
        var classCount = nsCursor.ReadInt32();
        var memberCount = nsCursor.ReadInt32();
        // After counts: types (4 bytes each) + classes (36 bytes each) + members (24 bytes each) + names
        var totalNsData = 12 + typeCount * 4 + classCount * 36 + memberCount * 24;
        var strOffset = offset + totalNsData;
        var maxLen = checked((int)header.NamespaceSize) - totalNsData;
        if (maxLen < 0) return new List<string>();

        // Scan for null-terminated ASCII strings
        var result = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < maxLen && strOffset + i < bytes.Length; i++)
        {
            var c = (char)bytes[strOffset + i];
            if (c == 0)
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else if (c >= 32 && c < 127)
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0)
            result.Add(sb.ToString());
        return result;
    }

    private static List<AgsObjectBlock> LoadBlocks(byte[] bytes, AgsHeader header, List<string> strings)
    {
        var result = new List<AgsObjectBlock>();
        var offset = checked((int)(header.HeaderSize + header.NamespaceSize));
        // Object blocks start after namespace data
        // Calculate namespace data size from type/class/member counts
        int nsOffset = checked((int)header.HeaderSize);
        int typeC = BitConverter.ToInt32(bytes, nsOffset);
        int classC = BitConverter.ToInt32(bytes, nsOffset + 4);
        int memberC = BitConverter.ToInt32(bytes, nsOffset + 8);
        var blockOffset = offset + 12 + typeC * 4 + classC * 36 + memberC * 24;
        var blockSize = 36; // standard Phyre object block size
        blockOffset = (blockOffset + 3) & ~3; // align

        for (int i = 0; i < header.ObjectBlockCount; i++)
        {
            var pos = blockOffset + i * blockSize;
            if (pos + blockSize > bytes.Length) break;
            var cursor = new AgsByteCursor(bytes, pos);
            var classId = cursor.ReadInt32();
            cursor.Skip(4); // flags
            var dataOffset = cursor.ReadInt32();
            var dataSize = cursor.ReadInt32();
            var nameOffset = cursor.ReadInt32();
            cursor.Skip(16); // padding/unknown

            var name = nameOffset >= 0 && nameOffset < strings.Count
                ? strings[nameOffset]
                : $"block_{i}";

            result.Add(new AgsObjectBlock(i, classId, name, dataOffset, dataSize));
        }
        return result;
    }

    private static byte[] LoadSharedData(byte[] bytes, AgsHeader header, List<AgsObjectBlock> blocks, List<string> strings)
    {
        // Phyre PBinary: [Header][Namespace+Strings][ObjectBlocks][ImportTable][ExportTable][SharedData]
        int offset = checked((int)header.HeaderSize); // after header
        offset = (offset + 3) & ~3;
        offset += checked((int)header.NamespaceSize); // namespace + strings
        offset = (offset + 3) & ~3;
        offset += checked((int)header.ObjectBlockCount * 36); // object blocks
        offset = (offset + 3) & ~3;
        // Skip import table (8 byte header + data)
        if (offset + 8 < bytes.Length)
        {
            int importLen = Math.Max(0, Math.Abs(BitConverter.ToInt32(bytes, offset + 4)));
            offset += 8 + importLen;
        }
        offset = (offset + 3) & ~3;
        // Skip export table
        if (offset + 8 < bytes.Length)
        {
            int exportLen = Math.Max(0, Math.Abs(BitConverter.ToInt32(bytes, offset + 4)));
            offset += 8 + exportLen;
        }
        offset = (offset + 3) & ~3;

        if (header.SharedDataLength > 0 && offset + header.SharedDataLength <= bytes.Length)
        {
            var data = new byte[header.SharedDataLength];
            Buffer.BlockCopy(bytes, offset, data, 0, (int)header.SharedDataLength);
            return data;
        }
        return Array.Empty<byte>();
    }

    private static TextureAnimationAtlas? ExtractAtlas(
        byte[] bytes,
        byte[] sharedData,
        string outputDirectory,
        string assetId)
    {
        var agsStr = Encoding.ASCII.GetString(sharedData);
        int w = 0, h = 0;
        string texFormat = "";

        // Find PTexture2D format token and dimensions
        foreach (var fmt in new[] { "DXT5", "DXT3", "DXT1", "ARGB8", "L8" })
        {
            int idx = agsStr.IndexOf(fmt, StringComparison.Ordinal);
            if (idx < 0) continue;
            texFormat = fmt;

            // Scan backwards from format token to find width/height
            for (int scan = idx - 12; scan >= 0 && scan < sharedData.Length - 8; scan++)
            {
                int tw = BitConverter.ToInt32(sharedData, scan);
                int th = BitConverter.ToInt32(sharedData, scan + 4);
                if (tw > 0 && tw <= 4096 && th > 0 && th <= 4096 && tw * th > 1000)
                {
                    w = tw; h = th;
                    break;
                }
            }
            if (w > 0) break;
        }
        // Save shared data dump for analysis (even if PTexture2D extraction fails)
        var dumpPath = Path.Combine(outputDirectory, $"{assetId}_ags_dump.bin");
        if (!File.Exists(dumpPath))
            File.WriteAllBytes(dumpPath, sharedData);

        if (w <= 0 || h <= 0) return null;

        // Extract frames from sub-texture names
        var frames = new List<TextureAnimationFrame>();
        var cursor = new AgsByteCursor(sharedData, 0);
        while (cursor.Position < sharedData.Length - 32)
        {
            int offset = cursor.Position;
            int strLen = 0;
            while (offset + strLen < sharedData.Length && sharedData[offset + strLen] >= 32
                && sharedData[offset + strLen] != 0 && strLen < 60) strLen++;
            if (strLen > 4)
            {
                var str = Encoding.ASCII.GetString(sharedData, offset, strLen);
                var parts = str.Split('_');
                if (parts.Length == 4 && int.TryParse(parts[0], out var ai)
                    && int.TryParse(parts[1], out var fi)
                    && int.TryParse(parts[2], out var fw)
                    && int.TryParse(parts[3], out var fh))
                {
                    frames.Add(new TextureAnimationFrame(ai, fi, 0, 0, fw, fh));
                }
            }
            cursor.Skip(strLen + 1);
        }

        var pngPath = Path.Combine(outputDirectory, $"{assetId}_ags_atlas.png");
        return new TextureAnimationAtlas(pngPath, w, h, frames, Array.Empty<TextureAnimationSequence>());
    }

    private sealed record AgsHeader(
        uint Magic,
        uint HeaderSize,
        uint NamespaceSize,
        uint ObjectBlockCount,
        uint SharedDataLength);

    private sealed record AgsObjectBlock(
        int Index,
        int ClassId,
        string Name,
        int DataOffset,
        int DataSize);
}
