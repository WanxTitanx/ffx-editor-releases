using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── EventPackage (EV01) ────────────────────────────────────────────────────────────────
// Parses the EV01 container used by every field event .ebp. File layout:
//   [0x00] "EV01" magic, then a table of u32 chunk start offsets terminated by 0xFFFFFFFF.
//   chunk 0 = ATEL script, 1 = JP text, 2 = unknown, 3 = FTCX (kana), 4 = EN text.
//   A chunk offset == 0 means the chunk is absent. Scan() uses chunk 0 (AtelBytes).
// ──────────────────────────────────────────────────────────────────────────────────────

public sealed record EventPackageChunk(int Index, int Offset, byte[] Bytes);

public sealed class EventPackage
{
    public const string Magic = "EV01";
    public string Path { get; }
    public IReadOnlyList<EventPackageChunk> Chunks { get; }
    public byte[] AtelBytes => Chunks.Count == 0 ? [] : Chunks[0].Bytes;

    private EventPackage(string path, IReadOnlyList<EventPackageChunk> chunks) { Path = path; Chunks = chunks; }

    public static EventPackage Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != Magic)
            throw new InvalidDataException($"'{path}' is not an EV01 event package.");
        var offsets = new List<int>();
        for (int cursor = 4; cursor <= bytes.Length - 4; cursor += 4)
        {
            uint raw = BitConverter.ToUInt32(bytes, cursor);
            if (raw == uint.MaxValue) break;
            int offset = checked((int)raw);
            if (offset != 0 && (offset < 0x10 || offset > bytes.Length))
                throw new InvalidDataException($"Event package chunk offset 0x{offset:X} is outside the file.");
            offsets.Add(offset);
            if (offset == bytes.Length) break;
        }
        if (offsets.Count < 2)
            throw new InvalidDataException("Event package does not contain a complete chunk table.");
        var chunks = new List<EventPackageChunk>();
        for (int i = 0; i < offsets.Count - 1; i++)
        {
            int start = offsets[i];
            if (start == 0) { chunks.Add(new EventPackageChunk(i, 0, [])); continue; }
            int end = offsets.Skip(i + 1).FirstOrDefault(o => o >= start);
            if (end == 0) end = bytes.Length;
            if (end < start || end > bytes.Length)
                throw new InvalidDataException($"Event package chunk {i} has invalid bounds.");
            chunks.Add(new EventPackageChunk(i, start, bytes.AsSpan(start, end - start).ToArray()));
        }
        return new EventPackage(System.IO.Path.GetFullPath(path), chunks);
    }
}
