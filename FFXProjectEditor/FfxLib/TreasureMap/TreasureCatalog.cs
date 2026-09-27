using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── TreasureCatalog (takara.bin) ───────────────────────────────────────────────────────
// Reads the treasure content catalog. Layout: 0x14-byte header, then one 4-byte record per
// treasure id: [0]=raw kind, [1]=quantity, [2..3]=type (encoded item/equip id).
// TreasureKind maps rawKind: Gil=0x00, Item=0x02, Equipment=0x05, KeyItem=0x0A.
// The editor's reward names come from TreasureRewardLookup (Item_Dictionary + takara).
// ──────────────────────────────────────────────────────────────────────────────────────

public enum TreasureKind : byte
{
    Gil = 0x00,
    Item = 0x02,
    Equipment = 0x05,
    KeyItem = 0x0A
}

public sealed record TreasureRecord(
    int Id,
    int FileOffset,
    byte RawKind,
    byte Quantity,
    ushort Type)
{
    public TreasureKind? Kind => Enum.IsDefined(typeof(TreasureKind), RawKind)
        ? (TreasureKind)RawKind : null;
    public int GilAmount => RawKind == (byte)TreasureKind.Gil ? Quantity * 100 : 0;
}

public sealed class TreasureCatalog
{
    public const int HeaderLength = 0x14;
    public const int RecordLength = 4;

    public string Path { get; }
    public IReadOnlyList<TreasureRecord> Records { get; }

    private TreasureCatalog(string path, IReadOnlyList<TreasureRecord> records)
    { Path = path; Records = records; }

    public static TreasureCatalog Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("takara.bin path required.", nameof(path));
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < HeaderLength)
            throw new InvalidDataException("takara.bin shorter than 0x14-byte header.");
        int dataLength = bytes.Length - HeaderLength;
        if (dataLength % RecordLength != 0)
            throw new InvalidDataException($"takara.bin data not divisible by {RecordLength}.");

        var records = new List<TreasureRecord>(dataLength / RecordLength);
        for (int id = 0; id < dataLength / RecordLength; id++)
        {
            int off = HeaderLength + id * RecordLength;
            records.Add(new TreasureRecord(id, off, bytes[off], bytes[off + 1],
                (ushort)(bytes[off + 2] | bytes[off + 3] << 8)));
        }
        return new TreasureCatalog(System.IO.Path.GetFullPath(path), records);
    }
}
