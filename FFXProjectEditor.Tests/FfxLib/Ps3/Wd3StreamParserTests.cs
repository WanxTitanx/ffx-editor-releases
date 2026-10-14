using System;
using System.Text;
using FFXProjectEditor.FfxLib.Ps3;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ps3
{
    /// <summary>
    /// Tests for Wd3StreamParser, validating the WD3 container format parsing
    /// against mock data modeled on magic_0086 and magic_0087 DLL structures.
    /// </summary>
    public class Wd3StreamParserTests
    {
        // Constants matching the WD3 format from wd3_parser_v2.py
        const string Wd3Magic = "WD3\x01";
        const int HeaderSize = 16;
        const int PaddingSize = 16;
        const int StreamTableOffset = 0x20;
        const int StreamHeaderSize = 32;
        const int StreamCount = 5;

        /// <summary>
        /// Build a mock WD3 blob that mimics the structure found in magic_0086/magic_0087 DLLs.
        /// The blob has 5 streams with realistic offsets, scales, and packed data values.
        /// </summary>
        static byte[] BuildMockWd3Blob(int streamDataSize = 64)
        {
            // Layout:
            //   +0x00  Header (16 bytes)
            //   +0x10  Padding (16 bytes)
            //   +0x20  Stream pointer table (5 × 4 = 20 bytes)
            //   +0x34  Stream headers (5 × 32 = 160 bytes)
            //   +0xD4  Stream data (5 × streamDataSize bytes)

            int streamHeadersStart = StreamTableOffset + StreamCount * 4; // 0x34
            int streamDataStart = streamHeadersStart + StreamCount * StreamHeaderSize; // 0xD4
            int totalSize = streamDataStart + StreamCount * streamDataSize;

            byte[] blob = new byte[totalSize];

            // --- Header ---
            // Magic "WD3\x01"
            byte[] magic = Encoding.ASCII.GetBytes(Wd3Magic);
            Array.Copy(magic, 0, blob, 0, 4);
            // Total size
            WriteU32(blob, 4, (uint)totalSize);
            // Stream count
            blob[8] = (byte)StreamCount;
            // Reserved
            blob[9] = 0;

            // --- Padding (16 bytes at +0x10) --- already zeros

            // --- Stream pointer table (5 × u32 at +0x20) ---
            // Each pointer points to the stream header relative to blob base
            for (int i = 0; i < StreamCount; i++)
                WriteU32(blob, StreamTableOffset + i * 4, (uint)(streamHeadersStart + i * StreamHeaderSize));

            // --- Stream headers (32 bytes each) ---
            // Realistic packed data values from wd3_parser_v2.py analysis
            uint[] packedValues = { 0x5fc600ff, 0x5fc600ff, 0x5fc600ff, 0x5fc600ff, 0x5fc600ff };
            float[] scales = { 4.0f, 4.0f, 4.0f, 4.0f, 4.0f };

            for (int i = 0; i < StreamCount; i++)
            {
                int hdrOff = streamHeadersStart + i * StreamHeaderSize;
                uint startOffset = (uint)(streamDataStart + i * streamDataSize);
                uint endOffset = (uint)(streamDataStart + (i + 1) * streamDataSize);

                // +0x00: unused
                WriteU32(blob, hdrOff + 0, 0);
                // +0x04: end_offset
                WriteU32(blob, hdrOff + 4, endOffset);
                // +0x08: start_offset
                WriteU32(blob, hdrOff + 8, startOffset);
                // +0x0C: packed1
                WriteU32(blob, hdrOff + 12, packedValues[i]);
                // +0x10: packed2
                WriteU32(blob, hdrOff + 16, 0xfe886e2b);
                // +0x14: scale (f32)
                WriteF32(blob, hdrOff + 20, scales[i]);
                // +0x18: unused
                WriteU32(blob, hdrOff + 24, 0);
                // +0x1C: unused
                WriteU32(blob, hdrOff + 28, 0);

                // Fill stream data with a recognizable pattern
                for (int j = 0; j < streamDataSize; j++)
                    blob[startOffset + j] = (byte)(0x10 * i + j);
            }

            return blob;
        }

        /// <summary>
        /// Build a mock WD3 blob where the last stream has end_offset=0,
        /// meaning "end of blob" (as seen in real magic DLLs).
        /// </summary>
        static byte[] BuildMockWd3BlobWithZeroEndOffset(int streamDataSize = 64)
        {
            byte[] blob = BuildMockWd3Blob(streamDataSize);

            int streamHeadersStart = StreamTableOffset + StreamCount * 4;
            int streamDataStart = streamHeadersStart + StreamCount * StreamHeaderSize;
            int totalSize = streamDataStart + StreamCount * streamDataSize;

            // Set last stream's end_offset to 0 (meaning "end of blob")
            int lastHdrOff = streamHeadersStart + (StreamCount - 1) * StreamHeaderSize;
            WriteU32(blob, lastHdrOff + 4, 0);

            return blob;
        }

        static void WriteU32(byte[] buf, int offset, uint value)
        {
            BitConverter.GetBytes(value).CopyTo(buf, offset);
        }

        static void WriteF32(byte[] buf, int offset, float value)
        {
            BitConverter.GetBytes(value).CopyTo(buf, offset);
        }

        // =====================================================================
        //  Valid WD3 parsing tests
        // =====================================================================

        [Fact]
        public void Parse_ValidWd3Blob_ReturnsCorrectMagic()
        {
            byte[] blob = BuildMockWd3Blob();

            Wd3Blob result = Wd3StreamParser.Parse(blob, 0);

            Assert.Equal(Wd3Magic, result.Header.Magic);
        }

        [Fact]
        public void Parse_ValidWd3Blob_Returns5Streams()
        {
            byte[] blob = BuildMockWd3Blob();

            Wd3Blob result = Wd3StreamParser.Parse(blob, 0);

            Assert.Equal(5, result.Header.StreamCount);
            Assert.Equal(5, result.Streams.Length);
        }

        [Fact]
        public void Parse_ValidWd3Blob_ReturnsCorrectTotalSize()
        {
            byte[] blob = BuildMockWd3Blob();

            Wd3Blob result = Wd3StreamParser.Parse(blob, 0);

            Assert.Equal((uint)blob.Length, result.Header.TotalSize);
        }

        [Fact]
        public void Parse_ValidWd3Blob_ExtractsStreamDataCorrectly()
        {
            int streamDataSize = 64;
            byte[] blob = BuildMockWd3Blob(streamDataSize);

            Wd3Blob result = Wd3StreamParser.Parse(blob, 0);

            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(streamDataSize, result.Streams[i].Data.Length);
                // Verify the recognizable pattern
                for (int j = 0; j < streamDataSize; j++)
                    Assert.Equal((byte)(0x10 * i + j), result.Streams[i].Data[j]);
            }
        }

        [Fact]
        public void Parse_ValidWd3Blob_ParsesStreamHeadersCorrectly()
        {
            byte[] blob = BuildMockWd3Blob();

            Wd3Blob result = Wd3StreamParser.Parse(blob, 0);

            int streamHeadersStart = StreamTableOffset + StreamCount * 4;
            int streamDataStart = streamHeadersStart + StreamCount * StreamHeaderSize;
            int streamDataSize = 64;

            for (int i = 0; i < 5; i++)
            {
                uint expectedStart = (uint)(streamDataStart + i * streamDataSize);
                uint expectedEnd = (uint)(streamDataStart + (i + 1) * streamDataSize);
                Assert.Equal(expectedStart, result.Streams[i].Header.StartOffset);
                Assert.Equal(expectedEnd, result.Streams[i].Header.EndOffset);
                Assert.Equal(4.0f, result.Streams[i].Header.Scale);
                Assert.Equal(0x5fc600ffu, result.Streams[i].Header.PackedData);
            }
        }

        [Fact]
        public void Parse_ValidWd3BlobWithZeroEndOffset_TreatsZeroAsEndOfBlob()
        {
            int streamDataSize = 64;
            byte[] blob = BuildMockWd3BlobWithZeroEndOffset(streamDataSize);

            Wd3Blob result = Wd3StreamParser.Parse(blob, 0);

            // Last stream should have end_offset resolved to total_size
            uint totalSize = result.Header.TotalSize;
            Assert.Equal(0u, result.Streams[4].Header.EndOffset); // raw value is 0
            // But the data length should be streamDataSize (totalSize - startOffset)
            Assert.Equal(streamDataSize, result.Streams[4].Data.Length);
        }

        [Fact]
        public void Parse_ValidWd3BlobAtNonZeroOffset_ParsesCorrectly()
        {
            // Simulate a DLL where the WD3 blob is at some offset in the .data section
            byte[] blob = BuildMockWd3Blob();
            int offset = 0x1000;
            byte[] dllBytes = new byte[offset + blob.Length];
            Array.Copy(blob, 0, dllBytes, offset, blob.Length);

            Wd3Blob result = Wd3StreamParser.Parse(dllBytes, offset);

            Assert.Equal(Wd3Magic, result.Header.Magic);
            Assert.Equal(5, result.Streams.Length);
            Assert.Equal((uint)blob.Length, result.Header.TotalSize);
        }

        // =====================================================================
        //  Invalid magic tests
        // =====================================================================

        [Fact]
        public void Parse_InvalidMagic_ThrowsWd3ParseException()
        {
            byte[] blob = BuildMockWd3Blob();
            // Corrupt the magic
            blob[0] = (byte)'X';
            blob[1] = (byte)'X';
            blob[2] = (byte)'X';

            Wd3ParseException ex = Assert.Throws<Wd3ParseException>(() => Wd3StreamParser.Parse(blob, 0));
            Assert.Contains("Invalid WD3 magic", ex.Message);
        }

        [Fact]
        public void Parse_WrongVersion_ThrowsWd3ParseException()
        {
            byte[] blob = BuildMockWd3Blob();
            // Change version byte from 0x01 to 0x02
            blob[3] = 0x02;

            Wd3ParseException ex = Assert.Throws<Wd3ParseException>(() => Wd3StreamParser.Parse(blob, 0));
            Assert.Contains("Invalid WD3 magic", ex.Message);
        }

        [Fact]
        public void Parse_EmptyBuffer_ThrowsWd3ParseException()
        {
            byte[] blob = Array.Empty<byte>();

            Assert.Throws<Wd3ParseException>(() => Wd3StreamParser.Parse(blob, 0));
        }

        [Fact]
        public void Parse_TruncatedHeader_ThrowsWd3ParseException()
        {
            byte[] blob = BuildMockWd3Blob();
            // Only 8 bytes — not enough for the 16-byte header
            byte[] truncated = new byte[8];
            Array.Copy(blob, 0, truncated, 0, 8);

            Wd3ParseException ex = Assert.Throws<Wd3ParseException>(() => Wd3StreamParser.Parse(truncated, 0));
            Assert.Contains("truncated", ex.Message);
        }

        // =====================================================================
        //  Edge case tests
        // =====================================================================

        [Fact]
        public void Parse_NullBytes_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Wd3StreamParser.Parse(null!, 0));
        }

        [Fact]
        public void Parse_NegativeOffset_ThrowsArgumentOutOfRangeException()
        {
            byte[] blob = BuildMockWd3Blob();

            Assert.Throws<ArgumentOutOfRangeException>(() => Wd3StreamParser.Parse(blob, -1));
        }

        [Fact]
        public void Parse_ReservedByte_PreservedInHeader()
        {
            byte[] blob = BuildMockWd3Blob();
            blob[9] = 0x42; // Set a non-zero reserved byte

            Wd3Blob result = Wd3StreamParser.Parse(blob, 0);

            Assert.Equal(0x42, result.Header.Reserved);
        }

        /// <summary>
        /// Integration-style test: build a mock WD3 blob that mimics the magic_0086
        /// .data section structure (WD3 blob embedded at a .data section offset).
        /// Validates the full parse → extract → verify cycle.
        /// </summary>
        [Fact]
        public void Parse_Magic0086StyleBlob_FullCycleValidation()
        {
            // Simulate a .data section with PE headers + WD3 blob
            int dataSectionOffset = 0x200;
            byte[] wd3Blob = BuildMockWd3Blob(128);
            byte[] dllBytes = new byte[dataSectionOffset + wd3Blob.Length];
            Array.Copy(wd3Blob, 0, dllBytes, dataSectionOffset, wd3Blob.Length);

            Wd3Blob result = Wd3StreamParser.Parse(dllBytes, dataSectionOffset);

            // Validate header
            Assert.Equal(Wd3Magic, result.Header.Magic);
            Assert.Equal((uint)wd3Blob.Length, result.Header.TotalSize);
            Assert.Equal(5, result.Header.StreamCount);

            // Validate all 5 streams have non-empty data
            foreach (Wd3Stream stream in result.Streams)
            {
                Assert.Equal(128, stream.Data.Length);
                Assert.True(stream.Data.Length > 0);
                Assert.Equal(4.0f, stream.Header.Scale);
            }
        }

        /// <summary>
        /// Integration-style test: build a mock WD3 blob that mimics the magic_0087
        /// .data section structure with zero end_offset on the last stream.
        /// </summary>
        [Fact]
        public void Parse_Magic0087StyleBlob_ZeroEndOffsetResolvesCorrectly()
        {
            int dataSectionOffset = 0x200;
            byte[] wd3Blob = BuildMockWd3BlobWithZeroEndOffset(96);
            byte[] dllBytes = new byte[dataSectionOffset + wd3Blob.Length];
            Array.Copy(wd3Blob, 0, dllBytes, dataSectionOffset, wd3Blob.Length);

            Wd3Blob result = Wd3StreamParser.Parse(dllBytes, dataSectionOffset);

            Assert.Equal(5, result.Streams.Length);
            // Last stream with end_offset=0 should still get its data
            Assert.Equal(96, result.Streams[4].Data.Length);
        }
    }
}
