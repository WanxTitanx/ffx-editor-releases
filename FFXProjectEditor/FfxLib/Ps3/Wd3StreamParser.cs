using System;
using System.IO;
using System.Text;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Parses the WD3 container format embedded in FFX magic DLL .data sections.
    /// The WD3 blob is a hierarchical stream container discovered empirically from
    /// reverse-engineering FFX.exe and magic DLLs.
    ///
    /// Layout:
    ///   +0x00  Header (16 bytes): magic "WD3\x01", total_size, stream_count, reserved
    ///   +0x10  Padding (16 bytes): zeros
    ///   +0x20  Stream pointer table (stream_count × u32): offsets to stream headers
    ///   +0x20+ Stream headers (32 bytes each): end_offset, start_offset, scale, packed_data
    ///   data   Stream data at the offsets specified by stream headers
    ///
    /// Reference: scripts/wd3_parser_v2.py
    /// </summary>
    public static class Wd3StreamParser
    {
        /// <summary>Expected magic bytes: "WD3" followed by version byte 0x01.</summary>
        public const string ExpectedMagic = "WD3\x01";

        /// <summary>Size of the WD3 header in bytes.</summary>
        public const int HeaderSize = 16;

        /// <summary>Size of the padding block after the header.</summary>
        public const int PaddingSize = 16;

        /// <summary>Offset of the stream pointer table relative to the blob base.</summary>
        public const int StreamTableOffset = 0x20;

        /// <summary>Size of each stream header in bytes.</summary>
        public const int StreamHeaderSize = 32;

        /// <summary>Maximum number of streams supported (matches the fixed 5×u32 pointer table).</summary>
        public const int MaxStreamCount = 5;

        /// <summary>
        /// Parse a WD3 blob from raw DLL bytes at the given data section offset.
        /// </summary>
        /// <param name="dllBytes">Raw bytes of the magic DLL (or just the .data section).</param>
        /// <param name="dataSectionOffset">Offset within <paramref name="dllBytes"/> where the WD3 blob starts.</param>
        /// <returns>A <see cref="Wd3Blob"/> containing the header and all parsed streams.</returns>
        /// <exception cref="Wd3ParseException">Thrown if the magic is invalid or the blob is truncated.</exception>
        public static Wd3Blob Parse(byte[] dllBytes, int dataSectionOffset)
        {
            if (dllBytes == null)
                throw new ArgumentNullException(nameof(dllBytes));
            if (dataSectionOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(dataSectionOffset), "data section offset must be non-negative");

            if (dataSectionOffset + HeaderSize > dllBytes.Length)
                throw new Wd3ParseException($"WD3 header at 0x{dataSectionOffset:X} is truncated: need {HeaderSize} bytes, have {dllBytes.Length - dataSectionOffset}");

            // --- Header (16 bytes) ---
            string magic = Encoding.ASCII.GetString(dllBytes, dataSectionOffset, 4);
            if (magic != ExpectedMagic)
                throw new Wd3ParseException($"Invalid WD3 magic at 0x{dataSectionOffset:X}: expected '{DisplayMagic(ExpectedMagic)}', got '{DisplayMagic(magic)}'");

            uint totalSize = BitConverter.ToUInt32(dllBytes, dataSectionOffset + 4);
            byte streamCount = dllBytes[dataSectionOffset + 8];
            byte reserved = dllBytes[dataSectionOffset + 9];

            Wd3Header header = new(magic, totalSize, streamCount, reserved);

            // --- Stream pointer table (stream_count × u32 at +0x20) ---
            int tableBase = dataSectionOffset + StreamTableOffset;
            int actualStreamCount = Math.Min((int)streamCount, MaxStreamCount);

            if (tableBase + actualStreamCount * 4 > dllBytes.Length)
                throw new Wd3ParseException($"Stream pointer table at 0x{tableBase:X} is truncated: need {actualStreamCount * 4} bytes, have {dllBytes.Length - tableBase}");

            uint[] streamPtrs = new uint[actualStreamCount];
            for (int i = 0; i < actualStreamCount; i++)
                streamPtrs[i] = BitConverter.ToUInt32(dllBytes, tableBase + i * 4);

            // --- Parse each stream header and extract data ---
            Wd3Stream[] streams = new Wd3Stream[actualStreamCount];
            for (int i = 0; i < actualStreamCount; i++)
            {
                int headerOffset = dataSectionOffset + (int)streamPtrs[i];

                if (headerOffset + StreamHeaderSize > dllBytes.Length)
                    throw new Wd3ParseException($"Stream {i} header at 0x{headerOffset:X} is truncated: need {StreamHeaderSize} bytes, have {dllBytes.Length - headerOffset}");

                Wd3StreamHeader streamHeader = ParseStreamHeader(dllBytes, headerOffset);

                // Resolve end_offset: 0 means "end of blob"
                uint endOffset = streamHeader.EndOffset;
                if (endOffset == 0)
                    endOffset = totalSize;

                uint startOffset = streamHeader.StartOffset;
                int dataLength = (int)(endOffset - startOffset);

                if (dataLength < 0)
                    throw new Wd3ParseException($"Stream {i} has negative size: end=0x{endOffset:X} start=0x{startOffset:X}");

                int dataAbsOffset = dataSectionOffset + (int)startOffset;
                if (dataAbsOffset + dataLength > dllBytes.Length)
                    throw new Wd3ParseException($"Stream {i} data at 0x{dataAbsOffset:X}+0x{dataLength:X} exceeds buffer length 0x{dllBytes.Length:X}");

                byte[] streamData = new byte[dataLength];
                if (dataLength > 0)
                    Array.Copy(dllBytes, dataAbsOffset, streamData, 0, dataLength);

                streams[i] = new Wd3Stream(streamHeader, streamData);
            }

            return new Wd3Blob(header, streams, dataSectionOffset);
        }

        /// <summary>
        /// Parse a 32-byte stream header from the byte array at the given absolute offset.
        ///
        /// Layout (empirically derived from wd3_parser_v2.py):
        ///   +0x00  u32  unused (always 0)
        ///   +0x04  u32  end_offset (exclusive, relative to blob base; 0 = end of blob)
        ///   +0x08  u32  start_offset (inclusive, relative to blob base)
        ///   +0x0C  u32  packed1 (e.g. 0x5fc600ff)
        ///   +0x10  u32  packed2 (e.g. 0xfe886e2b or 0xfbdd3da7)
        ///   +0x14  f32  scale (typically ~4.0)
        ///   +0x18  u32  unused (0)
        ///   +0x1C  u32  unused (0)
        /// </summary>
        static Wd3StreamHeader ParseStreamHeader(byte[] data, int offset)
        {
            uint endOffset = BitConverter.ToUInt32(data, offset + 4);
            uint startOffset = BitConverter.ToUInt32(data, offset + 8);
            uint packed1 = BitConverter.ToUInt32(data, offset + 12);
            uint packed2 = BitConverter.ToUInt32(data, offset + 16);
            float scale = BitConverter.ToSingle(data, offset + 20);

            return new Wd3StreamHeader(endOffset, startOffset, scale, packed1, packed2);
        }

        /// <summary>Convert a magic string to a display-safe hex representation for error messages.</summary>
        static string DisplayMagic(string magic)
        {
            StringBuilder sb = new();
            foreach (char c in magic)
            {
                if (c >= 0x20 && c <= 0x7E)
                    sb.Append(c);
                else
                    sb.Append($"\\x{(byte)c:X2}");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// WD3 container header (first 16 bytes of the blob).
    /// </summary>
    public readonly struct Wd3Header
    {
        /// <summary>4-byte magic string, typically "WD3\x01".</summary>
        public string Magic { get; }

        /// <summary>Total size of the WD3 blob in bytes.</summary>
        public uint TotalSize { get; }

        /// <summary>Number of streams in the blob (typically 5).</summary>
        public byte StreamCount { get; }

        /// <summary>Reserved byte at offset +0x09.</summary>
        public byte Reserved { get; }

        /// <summary>Version byte extracted from the magic (4th byte, typically 0x01).</summary>
        public byte Version => Magic.Length >= 4 ? (byte)Magic[3] : (byte)0;

        public Wd3Header(string magic, uint totalSize, byte streamCount, byte reserved)
        {
            Magic = magic;
            TotalSize = totalSize;
            StreamCount = streamCount;
            Reserved = reserved;
        }
    }

    /// <summary>
    /// Stream header (subset of the 32-byte on-disk stream header).
    /// </summary>
    public readonly struct Wd3StreamHeader
    {
        /// <summary>Exclusive end offset relative to blob base. 0 means end of blob.</summary>
        public uint EndOffset { get; }

        /// <summary>Inclusive start offset relative to blob base.</summary>
        public uint StartOffset { get; }

        /// <summary>Scale factor (typically ~4.0).</summary>
        public float Scale { get; }

        /// <summary>First packed metadata field (packed1 at stream header +0x0C).</summary>
        public uint PackedData { get; }

        /// <summary>Second packed metadata field (packed2 at stream header +0x10).</summary>
        public uint Packed2 { get; }

        /// <summary>Alias for <see cref="PackedData"/> (packed1).</summary>
        public uint Packed1 => PackedData;

        public Wd3StreamHeader(uint endOffset, uint startOffset, float scale, uint packedData, uint packed2 = 0)
        {
            EndOffset = endOffset;
            StartOffset = startOffset;
            Scale = scale;
            PackedData = packedData;
            Packed2 = packed2;
        }
    }

    /// <summary>
    /// A single WD3 stream: its header and the raw data bytes.
    /// </summary>
    public readonly struct Wd3Stream
    {
        public Wd3StreamHeader Header { get; }
        public byte[] Data { get; }

        // Convenience properties delegating to Header for ergonomic access
        public uint StartOffset => Header.StartOffset;
        public uint EndOffset => Header.EndOffset;
        public float Scale => Header.Scale;
        public uint Packed1 => Header.PackedData;
        public uint Packed2 => Header.Packed2;

        public Wd3Stream(Wd3StreamHeader header, byte[] data)
        {
            Header = header;
            Data = data;
        }
    }

    /// <summary>
    /// Complete parsed WD3 blob: header + all streams.
    /// </summary>
    public readonly struct Wd3Blob
    {
        public Wd3Header Header { get; }
        public Wd3Stream[] Streams { get; }

        /// <summary>File offset where the WD3 blob was found within the DLL bytes.</summary>
        public int BlobOffset { get; }

        /// <summary>Convenience accessor for the stream count.</summary>
        public byte StreamCount => Header.StreamCount;

        public Wd3Blob(Wd3Header header, Wd3Stream[] streams, int blobOffset = 0)
        {
            Header = header;
            Streams = streams;
            BlobOffset = blobOffset;
        }
    }

    /// <summary>
    /// Exception thrown when a WD3 blob cannot be parsed (invalid magic, truncated data, etc.).
    /// </summary>
    public class Wd3ParseException : Exception
    {
        public Wd3ParseException(string message) : base(message) { }
        public Wd3ParseException(string message, Exception innerException) : base(message, innerException) { }
    }
}
