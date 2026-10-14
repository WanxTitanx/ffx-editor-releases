using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace FFXProjectEditor.FfxLib.Archives;

internal sealed record VbfEntry(string Path, long Size, long PackedSize, long Offset,
    int BlockStart, int BlockCount, uint OpaqueField);

/// <summary>
/// Streaming PC SRYK reader. Format evidence: research_tools/Ps2/vbf_reader.py
/// and docs/reverse/_archive/playbook/FFX_VBF_PACKER_2026-09-18.md. This codec is
/// independent of the legacy VBFTool implementation (Topher / Kaldaien).
/// </summary>
internal sealed class VbfArchive
{
    public const uint Magic = 0x4b595253;
    public const int BlockSize = 65536;
    internal const int MaxHeaderSize = 256 * 1024 * 1024;
    internal const int MaxEntries = 1_000_000;
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    readonly ushort[] blocks;
    readonly byte[] headerDigest;
    readonly DateTime modified;
    public string FilePath { get; }
    public long FileSize { get; }
    public int HeaderSize { get; }
    public IReadOnlyList<VbfEntry> Entries { get; }

    VbfArchive(string path, long size, int headerSize, DateTime modified,
        byte[] digest, ushort[] blocks, VbfEntry[] entries)
    {
        FilePath = path; FileSize = size; HeaderSize = headerSize;
        this.modified = modified; headerDigest = digest; this.blocks = blocks;
        Entries = Array.AsReadOnly(entries);
    }

    public static VbfArchive Open(string path, CancellationToken token = default)
    {
        path = System.IO.Path.GetFullPath(path);
        VbfSafeFileSystem.RejectLinks(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(input, Utf8, true);
        if (input.Length < 36 || reader.ReadUInt32() != Magic)
            throw new InvalidDataException("Not a PC SRYK VBF archive.");
        uint headerSize = reader.ReadUInt32();
        ulong count = reader.ReadUInt64();
        if (count > MaxEntries || headerSize > MaxHeaderSize ||
            headerSize < 20UL + count * 48 || headerSize > input.Length - 16)
            throw new InvalidDataException("Invalid VBF header size or entry count.");
        token.ThrowIfCancellationRequested();
        input.Position = 0;
        byte[] header = new byte[(int)headerSize];
        input.ReadExactly(header);
        byte[] digest = MD5.HashData(header);
        input.Position = input.Length - 16;
        byte[] footer = new byte[16];
        input.ReadExactly(footer);
        if (!CryptographicOperations.FixedTimeEquals(digest, footer))
            throw new InvalidDataException("VBF header checksum mismatch.");

        int n = (int)count, recordStart = 16 + n * 16, namesStart = 16 + n * 48;
        uint stringBytes = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(namesStart, 4));
        if (stringBytes < 4 || stringBytes > header.Length - namesStart)
            throw new InvalidDataException("Invalid VBF filename table.");
        int blocksStart = namesStart + (int)stringBytes;
        if ((header.Length - blocksStart) % 2 != 0)
            throw new InvalidDataException("Invalid VBF block table length.");
        var blocks = new ushort[(header.Length - blocksStart) / 2];
        for (int i = 0; i < blocks.Length; i++)
            blocks[i] = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(blocksStart + i * 2, 2));

        var entries = new VbfEntry[n];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long nextBlock = 0;
        var ranges = new List<(long Start, long End)>(n);
        ReadOnlySpan<byte> nameTable = header.AsSpan(namesStart + 4, (int)stringBytes - 4);
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> record = header.AsSpan(recordStart + 32 * i, 32);
            uint blockStart = BinaryPrimitives.ReadUInt32LittleEndian(record);
            uint opaque = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
            ulong size = BinaryPrimitives.ReadUInt64LittleEndian(record[8..]);
            ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(record[16..]);
            ulong nameOffset = BinaryPrimitives.ReadUInt64LittleEndian(record[24..]);
            if (size > long.MaxValue || nameOffset >= (ulong)nameTable.Length)
                throw new InvalidDataException("Invalid VBF entry size or filename offset.");
            ReadOnlySpan<byte> nameBytes = nameTable[(int)nameOffset..];
            int end = nameBytes.IndexOf((byte)0);
            if (end < 1 || end > 4096)
                throw new InvalidDataException("Unterminated or oversized VBF filename.");
            string name;
            try { name = NormalizePath(Utf8.GetString(nameBytes[..end])); }
            catch (ArgumentException ex) { throw new InvalidDataException("Unsafe VBF filename.", ex); }
            if (!names.Add(name) || !MD5.HashData(Utf8.GetBytes(name)).AsSpan().SequenceEqual(header.AsSpan(16 + 16 * i, 16)))
                throw new InvalidDataException("Duplicate VBF filename or filename hash mismatch.");
            long blockCount = (long)(size / BlockSize + (size % BlockSize == 0 ? 0UL : 1UL));
            if (blockStart != nextBlock || blockCount > blocks.Length - nextBlock)
                throw new InvalidDataException("Invalid VBF entry block range.");
            long packed = 0;
            for (int b = 0; b < blockCount; b++)
            {
                int stored = StoredLength(blocks[(int)nextBlock + b]);
                int logical = (int)Math.Min(BlockSize, (long)size - (long)b * BlockSize);
                // 0 is a full raw block; a partial final block is raw only when
                // its stored size equals the remaining uncompressed bytes.
                if (stored > logical || (stored != logical && stored < 3))
                    throw new InvalidDataException("Invalid VBF compressed block size.");
                packed += stored;
            }
            // Original empty entries can contain leaked packer pointers in offset.
            // The consumer never reads them; validate ranges only for data-bearing entries.
            if (size != 0 && (offset < headerSize || offset > (ulong)(input.Length - 16) ||
                (ulong)packed > (ulong)(input.Length - 16) - offset))
                throw new InvalidDataException("VBF payload exceeds the archive.");
            long safeOffset = size == 0 ? headerSize : (long)offset;
            if (size != 0) ranges.Add((safeOffset, safeOffset + packed));
            entries[i] = new VbfEntry(name, (long)size, packed, safeOffset, (int)nextBlock, (int)blockCount, opaque);
            nextBlock += blockCount;
        }
        if (nextBlock != blocks.Length)
            throw new InvalidDataException("Unreferenced VBF blocks.");
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 1; i < ranges.Count; i++)
            if (ranges[i].Start < ranges[i - 1].End)
                throw new InvalidDataException("Overlapping VBF payloads.");
        return new VbfArchive(path, input.Length, (int)headerSize,
            File.GetLastWriteTimeUtc(path), digest, blocks, entries);
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An archive path is required.");
        string normalized = path.Replace('\\', '/').ToLowerInvariant();
        if (Utf8.GetByteCount(normalized) > 4096) throw new ArgumentException("Archive path is too long.");
        foreach (string part in normalized.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => char.IsControl(c) || ":*?\"<>|".Contains(c)))
                throw new ArgumentException("Unsafe archive path: " + path);
            string stem = part.Split('.')[0];
            if (stem is "con" or "prn" or "aux" or "nul" ||
                (stem.Length == 4 && (stem.StartsWith("com") || stem.StartsWith("lpt")) && stem[3] is >= '1' and <= '9'))
                throw new ArgumentException("Reserved archive filename: " + path);
        }
        return normalized;
    }

    internal static int StoredLength(ushort value) => value == 0 ? BlockSize : value;
    internal ushort BlockLength(int index) => blocks[index];

    internal FileStream OpenVerifiedSource()
    {
        var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length != FileSize || File.GetLastWriteTimeUtc(FilePath) != modified)
                throw new IOException("The source VBF changed. Reopen it before continuing.");
            byte[] header = new byte[HeaderSize];
            stream.ReadExactly(header);
            if (!MD5.HashData(header).AsSpan().SequenceEqual(headerDigest))
                throw new IOException("The source VBF header changed. Reopen it before continuing.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public void ExtractTo(VbfEntry entry, Stream output, CancellationToken token = default)
    {
        using var input = OpenVerifiedSource();
        ExtractTo(input, entry, output, token);
    }

    internal void ExtractTo(Stream input, VbfEntry entry, Stream output, CancellationToken token)
    {
        input.Position = entry.Offset;
        byte[] stored = new byte[BlockSize], raw = new byte[BlockSize];
        for (int b = 0; b < entry.BlockCount; b++)
        {
            token.ThrowIfCancellationRequested();
            int length = StoredLength(blocks[entry.BlockStart + b]);
            int logical = (int)Math.Min(BlockSize, entry.Size - (long)b * BlockSize);
            input.ReadExactly(stored.AsSpan(0, length));
            if (length == logical) output.Write(stored, 0, logical);
            else
            {
                // PC files and legacy community repacks both prefix deflate by
                // two bytes. Original files also carry an Adler32 trailer.
                using var payload = new MemoryStream(stored, 2, length - 2, false);
                using var inflater = new DeflateStream(payload, CompressionMode.Decompress);
                inflater.ReadExactly(raw.AsSpan(0, logical));
                if (inflater.ReadByte() != -1) throw new InvalidDataException("VBF block expands beyond its declared size.");
                bool zlib = (stored[0] & 15) == 8 && ((stored[0] << 8) + stored[1]) % 31 == 0;
                if (zlib && (length < 6 || BinaryPrimitives.ReadUInt32BigEndian(stored.AsSpan(length - 4, 4)) != Adler32(raw.AsSpan(0, logical))))
                    throw new InvalidDataException("VBF block checksum mismatch.");
                output.Write(raw, 0, logical);
            }
        }
    }

    static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        uint a = 1, b = 0;
        foreach (byte value in bytes) { a = (a + value) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }
}
