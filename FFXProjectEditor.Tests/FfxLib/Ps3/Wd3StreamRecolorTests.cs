using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ps3;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ps3
{
    /// <summary>
    /// Tests for <see cref="Wd3StreamRecolor"/>, validating hierarchical color
    /// patching of WD3 streams against mock DLLs modeled on magic_0086 and
    /// magic_0087 structures.
    ///
    /// The key insight: WD3 has 5 overlapping streams. The recolor must patch
    /// color bytes WITHIN each stream's PPP records (at sprite offset +18),
    /// not scan the entire .data for vec4 floats (the obsolete approach).
    /// </summary>
    public class Wd3StreamRecolorTests
    {
        // ── PPP record constants (must match Wd3StreamRecolor internals) ──
        const int RecordHeaderSize = 16;
        const int SpriteSize = 40;
        const int SpriteColorOffset = 18;
        const int ColorByteCount = 4;
        const int StreamHeaderSize = 32;
        const int StreamTableOffset = 0x20;
        const int StreamCount = 5;

        // Default white color in byte form (float 1.0 = byte 128 = 0x80)
        static readonly byte[] WhiteBytes = { 0x80, 0x80, 0x80, 0x80 };
        // Magenta (1.0, 0.0, 1.0, 1.0) in byte form
        static readonly byte[] MagentaBytes = { 0x80, 0x00, 0x80, 0x80 };

        // ═══════════════════════════════════════════════════════════════════════
        //  Mock DLL builder
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Build a mock DLL with an embedded WD3 blob containing PPP records
        /// with sprite color bytes. Mimics the structure of magic_0086/magic_0087.
        /// </summary>
        /// <param name="dataOffset">File offset where the WD3 blob starts.</param>
        /// <param name="streamCount">Number of streams (typically 5).</param>
        /// <param name="spritesPerRecord">Sprites per type_tag=3 record.</param>
        /// <param name="overlappingStreams">If true, all streams point to the same data region.</param>
        /// <param name="zeroEndOffsetOnLastStream">If true, last stream's end_offset=0 (magic_0087 style).</param>
        /// <param name="initialColor">Initial RGBA color bytes in each sprite.</param>
        static byte[] BuildMockDll(
            int dataOffset = 0x200,
            int streamCount = StreamCount,
            int spritesPerRecord = 2,
            bool overlappingStreams = false,
            bool zeroEndOffsetOnLastStream = false,
            byte[]? initialColor = null)
        {
            initialColor ??= WhiteBytes;

            // Stream data: one type_tag=3 record with spritesPerRecord sprites
            int recordSize = RecordHeaderSize + SpriteSize * spritesPerRecord;
            int streamDataSize = recordSize;

            // WD3 blob layout (relative to blob base)
            int streamHeadersStart = StreamTableOffset + streamCount * 4;
            int streamDataStart = streamHeadersStart + streamCount * StreamHeaderSize;
            int totalBlobSize = overlappingStreams
                ? streamDataStart + streamDataSize
                : streamDataStart + streamCount * streamDataSize;

            int dllSize = dataOffset + totalBlobSize;
            byte[] dll = new byte[dllSize];

            // Fill pre-blob area with 0xCC (simulates PE headers)
            for (int i = 0; i < dataOffset; i++)
                dll[i] = 0xCC;

            // ── WD3 Header (16 bytes) ──
            int b = dataOffset;
            dll[b + 0] = (byte)'W';
            dll[b + 1] = (byte)'D';
            dll[b + 2] = (byte)'3';
            dll[b + 3] = 0x01;
            WriteU32(dll, b + 4, (uint)totalBlobSize);
            dll[b + 8] = (byte)streamCount;

            // ── Stream pointer table (streamCount × u32 at +0x20) ──
            for (int i = 0; i < streamCount; i++)
                WriteU32(dll, b + StreamTableOffset + i * 4,
                    (uint)(streamHeadersStart + i * StreamHeaderSize));

            // ── Stream headers and data ──
            for (int i = 0; i < streamCount; i++)
            {
                int hdrOff = b + streamHeadersStart + i * StreamHeaderSize;
                uint startOff, endOff;

                if (overlappingStreams)
                {
                    startOff = (uint)streamDataStart;
                    endOff = (uint)(streamDataStart + streamDataSize);
                }
                else
                {
                    startOff = (uint)(streamDataStart + i * streamDataSize);
                    endOff = (uint)(streamDataStart + (i + 1) * streamDataSize);
                }

                if (zeroEndOffsetOnLastStream && i == streamCount - 1)
                    endOff = 0;

                WriteU32(dll, hdrOff + 0, 0);
                WriteU32(dll, hdrOff + 4, endOff);
                WriteU32(dll, hdrOff + 8, startOff);
                WriteU32(dll, hdrOff + 12, 0x5fc600ff);
                WriteU32(dll, hdrOff + 16, 0xfe886e2b);
                WriteF32(dll, hdrOff + 20, 4.0f);
                WriteU32(dll, hdrOff + 24, 0);
                WriteU32(dll, hdrOff + 28, 0);

                // Write stream data only once per unique region
                int dataAbsOff = b + (int)startOff;
                bool alreadyWritten = false;
                for (int j = 0; j < i; j++)
                {
                    int prevHdrOff = b + streamHeadersStart + j * StreamHeaderSize;
                    uint prevStart = BitConverter.ToUInt32(dll, prevHdrOff + 8);
                    if (prevStart == startOff)
                    {
                        alreadyWritten = true;
                        break;
                    }
                }

                if (!alreadyWritten)
                    WritePppRecord(dll, dataAbsOff, spritesPerRecord, initialColor);
            }

            return dll;
        }

        /// <summary>
        /// Write a PPP record with type_tag=3 and the given sprite count.
        /// Each sprite has vertices (zeros), RGBA colors at +18, and zero UVs/padding.
        /// </summary>
        static void WritePppRecord(byte[] dll, int offset, int spriteCount, byte[] color)
        {
            // Record header (16 bytes)
            dll[offset + 0] = 0x41;  // opcode: alpha=1.0 (full)
            dll[offset + 1] = 0x03;  // type_tag=3 (texture/sprite)
            WriteU16(dll, offset + 2, (ushort)spriteCount);
            // f1–f6: already zero

            // Sprites (each 40 bytes)
            for (int s = 0; s < spriteCount; s++)
            {
                int sprOff = offset + RecordHeaderSize + s * SpriteSize;
                // Vertices (+0–17): already zero
                // Colors (+18–21): RGBA
                dll[sprOff + 18] = color[0];
                dll[sprOff + 19] = color[1];
                dll[sprOff + 20] = color[2];
                dll[sprOff + 21] = color[3];
                // UVs (+20–27) and padding (+28–39): already zero
            }
        }

        static void WriteU16(byte[] buf, int offset, ushort value) =>
            BitConverter.GetBytes(value).CopyTo(buf, offset);

        static void WriteU32(byte[] buf, int offset, uint value) =>
            BitConverter.GetBytes(value).CopyTo(buf, offset);

        static void WriteF32(byte[] buf, int offset, float value) =>
            BitConverter.GetBytes(value).CopyTo(buf, offset);

        /// <summary>Write mock DLL to a temp file and return the path.</summary>
        static string WriteTempDll(byte[] dllBytes, string suffix = "src")
        {
            string path = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_{suffix}_{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, dllBytes);
            return path;
        }

        /// <summary>Get the expected file offset of a sprite's color bytes.</summary>
        static int GetColorFileOffset(int dataOffset, int streamIdx, int spriteIdx,
            bool overlapping = false)
        {
            int streamHeadersStart = StreamTableOffset + StreamCount * 4;
            int streamDataStart = streamHeadersStart + StreamCount * StreamHeaderSize;
            int streamStart = overlapping
                ? streamDataStart
                : streamDataStart + streamIdx * (RecordHeaderSize + SpriteSize * 2);
            return dataOffset + streamStart + RecordHeaderSize + spriteIdx * SpriteSize + SpriteColorOffset;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Apply — single stream
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Apply_SingleStream_PatchesColorBytesAtCorrectOffsets()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 2);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 0,
                        r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                // 2 sprites → 2 color patches
                Assert.Equal(2, result.PatchCount);
                Assert.Equal(2, result.Patches.Count);

                // Verify the output DLL has the correct color bytes
                byte[] output = File.ReadAllBytes(outPath);

                int color0 = GetColorFileOffset(0x200, 0, 0);
                int color1 = GetColorFileOffset(0x200, 0, 1);

                Assert.Equal(MagentaBytes[0], output[color0 + 0]);
                Assert.Equal(MagentaBytes[1], output[color0 + 1]);
                Assert.Equal(MagentaBytes[2], output[color0 + 2]);
                Assert.Equal(MagentaBytes[3], output[color0 + 3]);

                Assert.Equal(MagentaBytes[0], output[color1 + 0]);
                Assert.Equal(MagentaBytes[1], output[color1 + 1]);
                Assert.Equal(MagentaBytes[2], output[color1 + 2]);
                Assert.Equal(MagentaBytes[3], output[color1 + 3]);
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void Apply_SingleStream_DoesNotModifyNonColorBytes()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 2);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 0,
                    r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                byte[] output = File.ReadAllBytes(outPath);

                // Verify that non-color bytes in the sprite are unchanged (still zero)
                int sprite0Start = GetColorFileOffset(0x200, 0, 0) - SpriteColorOffset;
                // Vertices (+0–17) should be zero
                for (int i = 0; i < 18; i++)
                    Assert.Equal(0, output[sprite0Start + i]);

                // Padding (+22–39) should be zero (UVs overlap with B/A at +20/+21)
                for (int i = 22; i < 40; i++)
                    Assert.Equal(0, output[sprite0Start + i]);

                // Verify the PE header padding (0xCC) is unchanged
                for (int i = 0; i < 0x200; i++)
                    Assert.Equal(0xCC, output[i]);
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void Apply_SingleStream_PreservesOtherStreams()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 2, overlappingStreams: false);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 0,
                    r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                byte[] output = File.ReadAllBytes(outPath);

                // Stream 1's color bytes should still be white (unchanged)
                int color1_0 = GetColorFileOffset(0x200, 1, 0);
                Assert.Equal(WhiteBytes[0], output[color1_0 + 0]);
                Assert.Equal(WhiteBytes[1], output[color1_0 + 1]);
                Assert.Equal(WhiteBytes[2], output[color1_0 + 2]);
                Assert.Equal(WhiteBytes[3], output[color1_0 + 3]);
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void Apply_ColorConversion_FloatToByteBy128()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 1);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                // 0.5 → 64 (0x40), 0.25 → 32 (0x20), 0.75 → 96 (0x60), 1.0 → 128 (0x80)
                Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 0,
                    r: 0.5f, g: 0.25f, b: 0.75f, a: 1.0f);

                byte[] output = File.ReadAllBytes(outPath);
                int colorOff = GetColorFileOffset(0x200, 0, 0);

                Assert.Equal(0x40, output[colorOff + 0]); // R: 0.5 * 128 = 64
                Assert.Equal(0x20, output[colorOff + 1]); // G: 0.25 * 128 = 32
                Assert.Equal(0x60, output[colorOff + 2]); // B: 0.75 * 128 = 96
                Assert.Equal(0x80, output[colorOff + 3]); // A: 1.0 * 128 = 128
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void Apply_ColorClamping_AboveMaxClampsTo255()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 1);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                // 2.0 * 128 = 256 → clamped to 255 (0xFF)
                Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 0,
                    r: 2.0f, g: 2.0f, b: 2.0f, a: 2.0f);

                byte[] output = File.ReadAllBytes(outPath);
                int colorOff = GetColorFileOffset(0x200, 0, 0);

                Assert.Equal(0xFF, output[colorOff + 0]);
                Assert.Equal(0xFF, output[colorOff + 1]);
                Assert.Equal(0xFF, output[colorOff + 2]);
                Assert.Equal(0xFF, output[colorOff + 3]);
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ApplyToAllStreams — hierarchical
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public void ApplyToAllStreams_NonOverlapping_PatchesAllStreams()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 2, overlappingStreams: false);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.ApplyToAllStreams(srcPath, outPath,
                        r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                // 5 streams × 2 sprites = 10 patches (non-overlapping → all unique)
                Assert.Equal(10, result.PatchCount);

                byte[] output = File.ReadAllBytes(outPath);

                // Verify all 5 streams have magenta colors
                for (int stream = 0; stream < 5; stream++)
                {
                    int colorOff = GetColorFileOffset(0x200, stream, 0);
                    Assert.Equal(MagentaBytes[0], output[colorOff + 0]);
                    Assert.Equal(MagentaBytes[1], output[colorOff + 1]);
                    Assert.Equal(MagentaBytes[2], output[colorOff + 2]);
                    Assert.Equal(MagentaBytes[3], output[colorOff + 3]);
                }
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void ApplyToAllStreams_Overlapping_DeduplicatesPatches()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 2, overlappingStreams: true);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.ApplyToAllStreams(srcPath, outPath,
                        r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                // All 5 streams point to the same data → 2 unique color offsets
                Assert.Equal(2, result.PatchCount);

                // Verify all patches have unique file offsets
                var offsets = result.Patches.Select(p => p.FileOffset).Distinct();
                Assert.Equal(2, offsets.Count());

                byte[] output = File.ReadAllBytes(outPath);
                int color0 = GetColorFileOffset(0x200, 0, 0, overlapping: true);
                int color1 = GetColorFileOffset(0x200, 0, 1, overlapping: true);

                Assert.Equal(MagentaBytes[0], output[color0 + 0]);
                Assert.Equal(MagentaBytes[1], output[color0 + 1]);
                Assert.Equal(MagentaBytes[2], output[color0 + 2]);
                Assert.Equal(MagentaBytes[3], output[color0 + 3]);

                Assert.Equal(MagentaBytes[0], output[color1 + 0]);
                Assert.Equal(MagentaBytes[1], output[color1 + 1]);
                Assert.Equal(MagentaBytes[2], output[color1 + 2]);
                Assert.Equal(MagentaBytes[3], output[color1 + 3]);
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Magic_0086 / Magic_0087 style validation
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Apply_Magic0086StyleBlob_PatchesColorBytes()
        {
            // magic_0086 (Thundara): 5 non-overlapping streams with PPP records.
            // Stream map from PPP_OPCODE_ENCODING.md:
            //   Stream 0: start=0x2b10, end=0x1aec0
            //   Stream 1: start=0x2a70, end=0x5b90
            //   etc.
            // We use a simplified version with the same structure (5 streams, PPP records).
            byte[] dllBytes = BuildMockDll(
                dataOffset: 0x200,
                streamCount: 5,
                spritesPerRecord: 3,
                overlappingStreams: false,
                zeroEndOffsetOnLastStream: false);

            string srcPath = WriteTempDll(dllBytes, "0086");
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_0086_out_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 0,
                        r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                // 3 sprites → 3 patches
                Assert.Equal(3, result.PatchCount);

                byte[] output = File.ReadAllBytes(outPath);

                // Verify all 3 sprites in stream 0 have magenta colors
                for (int s = 0; s < 3; s++)
                {
                    int colorOff = GetColorFileOffset(0x200, 0, s);
                    Assert.Equal(MagentaBytes[0], output[colorOff + 0]);
                    Assert.Equal(MagentaBytes[1], output[colorOff + 1]);
                    Assert.Equal(MagentaBytes[2], output[colorOff + 2]);
                    Assert.Equal(MagentaBytes[3], output[colorOff + 3]);
                }
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void ApplyToAllStreams_Magic0086Style_PatchesAllStreams()
        {
            byte[] dllBytes = BuildMockDll(
                dataOffset: 0x200,
                streamCount: 5,
                spritesPerRecord: 2,
                overlappingStreams: false,
                zeroEndOffsetOnLastStream: false);

            string srcPath = WriteTempDll(dllBytes, "0086");
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_0086_all_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.ApplyToAllStreams(srcPath, outPath,
                        r: 0.0f, g: 1.0f, b: 0.0f, a: 1.0f); // Green

                // 5 streams × 2 sprites = 10 patches
                Assert.Equal(10, result.PatchCount);

                byte[] output = File.ReadAllBytes(outPath);
                byte[] greenBytes = { 0x00, 0x80, 0x00, 0x80 };

                for (int stream = 0; stream < 5; stream++)
                {
                    for (int s = 0; s < 2; s++)
                    {
                        int colorOff = GetColorFileOffset(0x200, stream, s);
                        Assert.Equal(greenBytes[0], output[colorOff + 0]);
                        Assert.Equal(greenBytes[1], output[colorOff + 1]);
                        Assert.Equal(greenBytes[2], output[colorOff + 2]);
                        Assert.Equal(greenBytes[3], output[colorOff + 3]);
                    }
                }
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void Apply_Magic0087Style_ZeroEndOffset_PatchesColorBytes()
        {
            // magic_0087 has the identical stream structure as magic_0086,
            // but with zero end_offset on the last stream (meaning "end of blob").
            byte[] dllBytes = BuildMockDll(
                dataOffset: 0x200,
                streamCount: 5,
                spritesPerRecord: 2,
                overlappingStreams: false,
                zeroEndOffsetOnLastStream: true);

            string srcPath = WriteTempDll(dllBytes, "0087");
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_0087_out_{Guid.NewGuid():N}.dll");

            try
            {
                // Patch the last stream (which has zero end_offset)
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 4,
                        r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                Assert.Equal(2, result.PatchCount);

                byte[] output = File.ReadAllBytes(outPath);
                int colorOff = GetColorFileOffset(0x200, 4, 0);
                Assert.Equal(MagentaBytes[0], output[colorOff + 0]);
                Assert.Equal(MagentaBytes[1], output[colorOff + 1]);
                Assert.Equal(MagentaBytes[2], output[colorOff + 2]);
                Assert.Equal(MagentaBytes[3], output[colorOff + 3]);
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void ApplyToAllStreams_Magic0087Style_PatchesAllStreams()
        {
            byte[] dllBytes = BuildMockDll(
                dataOffset: 0x200,
                streamCount: 5,
                spritesPerRecord: 2,
                overlappingStreams: false,
                zeroEndOffsetOnLastStream: true);

            string srcPath = WriteTempDll(dllBytes, "0087");
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_0087_all_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.ApplyToAllStreams(srcPath, outPath,
                        r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                // 5 streams × 2 sprites = 10 patches
                Assert.Equal(10, result.PatchCount);

                byte[] output = File.ReadAllBytes(outPath);

                // Verify all streams (including the last with zero end_offset)
                for (int stream = 0; stream < 5; stream++)
                {
                    int colorOff = GetColorFileOffset(0x200, stream, 0);
                    Assert.Equal(MagentaBytes[0], output[colorOff + 0]);
                    Assert.Equal(MagentaBytes[1], output[colorOff + 1]);
                    Assert.Equal(MagentaBytes[2], output[colorOff + 2]);
                    Assert.Equal(MagentaBytes[3], output[colorOff + 3]);
                }
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Error handling
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Apply_SourceDllNotFound_ThrowsFileNotFoundException()
        {
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            Assert.Throws<FileNotFoundException>(() =>
                Wd3StreamRecolor.Apply("nonexistent.dll", outPath, 0,
                    1.0f, 0.0f, 1.0f, 1.0f));
        }

        [Fact]
        public void Apply_Wd3MagicNotFound_ThrowsInvalidOperationException()
        {
            byte[] noWd3 = new byte[0x1000];
            for (int i = 0; i < noWd3.Length; i++)
                noWd3[i] = 0xCC;

            string srcPath = WriteTempDll(noWd3);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    Wd3StreamRecolor.Apply(srcPath, outPath, 0,
                        1.0f, 0.0f, 1.0f, 1.0f));
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void Apply_InvalidStreamIndex_ThrowsArgumentOutOfRangeException()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 1);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 99,
                        1.0f, 0.0f, 1.0f, 1.0f));

                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: -1,
                        1.0f, 0.0f, 1.0f, 1.0f));
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void ApplyToAllStreams_SourceDllNotFound_ThrowsFileNotFoundException()
        {
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            Assert.Throws<FileNotFoundException>(() =>
                Wd3StreamRecolor.ApplyToAllStreams("nonexistent.dll", outPath,
                    1.0f, 0.0f, 1.0f, 1.0f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Patch metadata
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Apply_PatchesContainCorrectFileOffsets()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 2);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 0,
                        r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                int expectedColor0 = GetColorFileOffset(0x200, 0, 0);
                int expectedColor1 = GetColorFileOffset(0x200, 0, 1);

                Assert.Equal(expectedColor0, result.Patches[0].FileOffset);
                Assert.Equal(expectedColor1, result.Patches[1].FileOffset);
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void Apply_PatchesContainCorrectHexPayload()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 1);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 0,
                        r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                // Magenta bytes: 0x80, 0x00, 0x80, 0x80 → hex "80008080"
                Assert.Equal("80008080", result.Patches[0].Hex);
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        [Fact]
        public void Apply_ResultContainsSourceAndOutputPaths()
        {
            byte[] dllBytes = BuildMockDll(spritesPerRecord: 1);
            string srcPath = WriteTempDll(dllBytes);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_out_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 0,
                        r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                Assert.Equal(srcPath, result.SourceDll);
                Assert.Equal(outPath, result.OutputDll);
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Multiple records per stream
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Apply_StreamWithMultipleRecords_PatchesAllSprites()
        {
            // Build a stream with 2 type_tag=3 records (each with 1 sprite)
            // + 1 command record (type_tag=1, no sprites)
            int dataOffset = 0x200;
            int spritesPerRecord1 = 1;
            int spritesPerRecord2 = 1;
            int record1Size = RecordHeaderSize + SpriteSize * spritesPerRecord1;
            int record2Size = RecordHeaderSize + SpriteSize * spritesPerRecord2;
            int cmdRecordSize = RecordHeaderSize;
            int streamDataSize = record1Size + record2Size + cmdRecordSize;

            int streamHeadersStart = StreamTableOffset + StreamCount * 4;
            int streamDataStart = streamHeadersStart + StreamCount * StreamHeaderSize;
            int totalBlobSize = streamDataStart + streamDataSize;
            int dllSize = dataOffset + totalBlobSize;

            byte[] dll = new byte[dllSize];
            for (int i = 0; i < dataOffset; i++)
                dll[i] = 0xCC;

            // WD3 header
            dll[dataOffset + 0] = (byte)'W';
            dll[dataOffset + 1] = (byte)'D';
            dll[dataOffset + 2] = (byte)'3';
            dll[dataOffset + 3] = 0x01;
            WriteU32(dll, dataOffset + 4, (uint)totalBlobSize);
            dll[dataOffset + 8] = (byte)StreamCount;

            // Stream pointer table
            for (int i = 0; i < StreamCount; i++)
                WriteU32(dll, dataOffset + StreamTableOffset + i * 4,
                    (uint)(streamHeadersStart + i * StreamHeaderSize));

            // Stream headers — all point to the same data (stream 0 only for this test)
            for (int i = 0; i < StreamCount; i++)
            {
                int hdrOff = dataOffset + streamHeadersStart + i * StreamHeaderSize;
                WriteU32(dll, hdrOff + 0, 0);
                WriteU32(dll, hdrOff + 4, (uint)(streamDataStart + streamDataSize));
                WriteU32(dll, hdrOff + 8, (uint)streamDataStart);
                WriteU32(dll, hdrOff + 12, 0x5fc600ff);
                WriteU32(dll, hdrOff + 16, 0xfe886e2b);
                WriteF32(dll, hdrOff + 20, 4.0f);
            }

            // Stream data: record1 (type_tag=3, 1 sprite) + record2 (type_tag=3, 1 sprite) + cmd (type_tag=1)
            int dataOff = dataOffset + streamDataStart;

            // Record 1: type_tag=3, 1 sprite
            dll[dataOff + 0] = 0x41;
            dll[dataOff + 1] = 0x03;
            WriteU16(dll, dataOff + 2, 1);
            dll[dataOff + RecordHeaderSize + SpriteColorOffset + 0] = 0x80;
            dll[dataOff + RecordHeaderSize + SpriteColorOffset + 1] = 0x80;
            dll[dataOff + RecordHeaderSize + SpriteColorOffset + 2] = 0x80;
            dll[dataOff + RecordHeaderSize + SpriteColorOffset + 3] = 0x80;

            // Record 2: type_tag=3, 1 sprite
            int rec2Off = dataOff + record1Size;
            dll[rec2Off + 0] = 0x41;
            dll[rec2Off + 1] = 0x03;
            WriteU16(dll, rec2Off + 2, 1);
            dll[rec2Off + RecordHeaderSize + SpriteColorOffset + 0] = 0x80;
            dll[rec2Off + RecordHeaderSize + SpriteColorOffset + 1] = 0x80;
            dll[rec2Off + RecordHeaderSize + SpriteColorOffset + 2] = 0x80;
            dll[rec2Off + RecordHeaderSize + SpriteColorOffset + 3] = 0x80;

            // Command record: type_tag=1, no sprites
            int cmdOff = dataOff + record1Size + record2Size;
            dll[cmdOff + 0] = 0x41;
            dll[cmdOff + 1] = 0x01; // type_tag=1 (not sprite)
            WriteU16(dll, cmdOff + 2, 0);

            string srcPath = WriteTempDll(dll);
            string outPath = Path.Combine(Path.GetTempPath(),
                $"wd3_recolor_multi_{Guid.NewGuid():N}.dll");

            try
            {
                Wd3StreamRecolor.RecolorResult result =
                    Wd3StreamRecolor.Apply(srcPath, outPath, streamIdx: 0,
                        r: 1.0f, g: 0.0f, b: 1.0f, a: 1.0f);

                // 2 type_tag=3 records × 1 sprite each = 2 patches
                // The command record (type_tag=1) has no sprites → no patches
                Assert.Equal(2, result.PatchCount);

                byte[] output = File.ReadAllBytes(outPath);

                // Verify sprite in record 1
                int color1 = dataOff + RecordHeaderSize + SpriteColorOffset;
                Assert.Equal(MagentaBytes[0], output[color1 + 0]);
                Assert.Equal(MagentaBytes[1], output[color1 + 1]);

                // Verify sprite in record 2
                int color2 = rec2Off + RecordHeaderSize + SpriteColorOffset;
                Assert.Equal(MagentaBytes[0], output[color2 + 0]);
                Assert.Equal(MagentaBytes[1], output[color2 + 1]);
            }
            finally
            {
                Cleanup(srcPath, outPath);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Helpers
        // ═══════════════════════════════════════════════════════════════════════

        static void Cleanup(params string[] paths)
        {
            foreach (string path in paths)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch { }
            }
        }
    }
}
