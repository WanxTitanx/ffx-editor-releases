using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Hierarchical color editor for WD3 streams embedded in FFX magic DLLs.
    ///
    /// WD3 (Windowed Data 3) is a container format with 5 OVERLAPPING streams
    /// that represent different views into the same vertex/UV/color/index data.
    /// Each stream contains PPP (sprite) records, and each type_tag=3 record
    /// contains sprites with 4-byte RGBA color fields (uint8, /128 normalization).
    ///
    /// This class replaces the obsolete <see cref="PrismMagicDllRecolor"/> approach
    /// which scanned the entire .data section for vec4 floats — hitting matrix
    /// blocks and corrupting stream offsets. Instead, this class:
    ///   1. Parses the WD3 blob with <see cref="Wd3StreamParser"/>
    ///   2. Walks PPP records WITHIN each stream
    ///   3. Patches ONLY the color bytes at known sprite offsets (+18 RGBA)
    ///
    /// This hierarchical awareness is the fix for the "only the explosion changes"
    /// bug: by patching color bytes in ALL 5 overlapping streams, every byte
    /// that the runtime interprets as a color gets the new value.
    ///
    /// Reference: docs/reverse/magic_dlls/PPP_OPCODE_ENCODING.md
    /// </summary>
    internal static class Wd3StreamRecolor
    {
        // ── PPP record constants (from FFX_Magic_PPP_BuildDrawableFromOpcode_ProcessCmd @ 0x71d600) ──

        /// <summary>PPP record header size in bytes (opcode + type_tag + count + 6×u16).</summary>
        const int RecordHeaderSize = 16;

        /// <summary>Sprite data size for type_tag=3 records (vertices + colors + UVs + padding).</summary>
        const int SpriteSize = 40;

        /// <summary>Offset of RGBA color bytes within each 40-byte sprite.</summary>
        const int SpriteColorOffset = 18;

        /// <summary>Number of color bytes per sprite (R, G, B, A).</summary>
        const int ColorByteCount = 4;

        /// <summary>type_tag value indicating a texture/sprite record with sprite data.</summary>
        const byte TypeTagSprite = 3;

        /// <summary>Safety limit: maximum sprites per record (matches wd3_parser_v2.py).</summary>
        const int MaxSpritesPerRecord = 100;

        /// <summary>Color normalization factor: float = byte / 128.0f.</summary>
        const float ColorNormalizationFactor = 128.0f;

        // ── WD3 magic bytes ──

        static readonly byte[] Wd3Magic = { (byte)'W', (byte)'D', (byte)'3', 0x01 };

        /// <summary>
        /// Result of a WD3 stream recolor operation.
        /// </summary>
        /// <param name="SourceDll">Path to the source DLL file.</param>
        /// <param name="OutputDll">Path to the output DLL file.</param>
        /// <param name="PatchCount">Number of color byte patches applied.</param>
        /// <param name="Patches">List of individual byte patches with file offsets.</param>
        public sealed record RecolorResult(
            string SourceDll,
            string OutputDll,
            int PatchCount,
            IReadOnlyList<MagicDllBytePatch> Patches);

        // ═══════════════════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Apply a recolor to a single WD3 stream within a magic DLL.
        ///
        /// Parses the WD3 blob, walks PPP records in the specified stream,
        /// and replaces all RGBA color bytes with the new color values.
        /// </summary>
        /// <param name="sourceDll">Path to the source magic DLL file.</param>
        /// <param name="outputDll">Path to write the modified DLL.</param>
        /// <param name="streamIdx">Index of the WD3 stream to recolor (0-4).</param>
        /// <param name="r">Red component (0.0–1.0+, normalized by /128).</param>
        /// <param name="g">Green component (0.0–1.0+, normalized by /128).</param>
        /// <param name="b">Blue component (0.0–1.0+, normalized by /128).</param>
        /// <param name="a">Alpha component (0.0–1.0+, normalized by /128).</param>
        /// <returns>A <see cref="RecolorResult"/> with patch details.</returns>
        /// <exception cref="FileNotFoundException">Source DLL not found.</exception>
        /// <exception cref="InvalidOperationException">WD3 magic not found in the DLL.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Stream index out of range.</exception>
        public static RecolorResult Apply(
            string sourceDll,
            string outputDll,
            int streamIdx,
            float r,
            float g,
            float b,
            float a)
        {
            if (!File.Exists(sourceDll))
                throw new FileNotFoundException("Source DLL not found.", sourceDll);

            byte[] bytes = File.ReadAllBytes(sourceDll);
            int blobOffset = FindWd3Offset(bytes);
            if (blobOffset < 0)
                throw new InvalidOperationException(
                    $"WD3 magic 'WD3\\x01' not found in {sourceDll}.");

            Wd3Blob blob = Wd3StreamParser.Parse(bytes, blobOffset);

            if (streamIdx < 0 || streamIdx >= blob.Streams.Length)
                throw new ArgumentOutOfRangeException(
                    nameof(streamIdx),
                    streamIdx,
                    $"Stream index must be 0–{blob.Streams.Length - 1}, got {streamIdx}.");

            byte[] colorBytes = FloatToColorBytes(r, g, b, a);
            List<MagicDllBytePatch> patches = PatchStreamColors(
                bytes, blob, streamIdx, colorBytes);

            WriteOutputDll(outputDll, bytes);

            return new RecolorResult(sourceDll, outputDll, patches.Count, patches);
        }

        /// <summary>
        /// Apply a recolor to ALL WD3 streams within a magic DLL.
        ///
        /// This is the hierarchical fix for the "only the explosion changes" bug.
        /// WD3 has 5 overlapping streams — patching only one stream misses color
        /// bytes that are interpreted differently by other streams. By patching
        /// ALL streams, every byte the runtime interprets as a color gets the
        /// new value.
        /// </summary>
        /// <param name="sourceDll">Path to the source magic DLL file.</param>
        /// <param name="outputDll">Path to write the modified DLL.</param>
        /// <param name="r">Red component (0.0–1.0+, normalized by /128).</param>
        /// <param name="g">Green component (0.0–1.0+, normalized by /128).</param>
        /// <param name="b">Blue component (0.0–1.0+, normalized by /128).</param>
        /// <param name="a">Alpha component (0.0–1.0+, normalized by /128).</param>
        /// <returns>A <see cref="RecolorResult"/> with patch details (deduplicated by file offset).</returns>
        /// <exception cref="FileNotFoundException">Source DLL not found.</exception>
        /// <exception cref="InvalidOperationException">WD3 magic not found in the DLL.</exception>
        public static RecolorResult ApplyToAllStreams(
            string sourceDll,
            string outputDll,
            float r,
            float g,
            float b,
            float a)
        {
            if (!File.Exists(sourceDll))
                throw new FileNotFoundException("Source DLL not found.", sourceDll);

            byte[] bytes = File.ReadAllBytes(sourceDll);
            int blobOffset = FindWd3Offset(bytes);
            if (blobOffset < 0)
                throw new InvalidOperationException(
                    $"WD3 magic 'WD3\\x01' not found in {sourceDll}.");

            Wd3Blob blob = Wd3StreamParser.Parse(bytes, blobOffset);
            byte[] colorBytes = FloatToColorBytes(r, g, b, a);

            // Patch all streams, collecting patches with deduplication by file offset.
            // Streams overlap, so the same physical byte may be a color byte in
            // multiple streams. We patch it for each stream (idempotent — same value)
            // but only report unique file offsets in the result.
            var patchesByOffset = new Dictionary<int, MagicDllBytePatch>();
            for (int i = 0; i < blob.Streams.Length; i++)
            {
                List<MagicDllBytePatch> streamPatches = PatchStreamColors(
                    bytes, blob, i, colorBytes);
                foreach (MagicDllBytePatch patch in streamPatches)
                {
                    patchesByOffset[patch.FileOffset] = patch;
                }
            }

            List<MagicDllBytePatch> patches = new(patchesByOffset.Values);
            patches.Sort((a, b) => a.FileOffset.CompareTo(b.FileOffset));

            WriteOutputDll(outputDll, bytes);

            return new RecolorResult(sourceDll, outputDll, patches.Count, patches);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  WD3 blob location
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Scan the DLL bytes for the WD3 magic "WD3\x01" (4 bytes: 0x57 0x44 0x33 0x01).
        /// Returns the file offset of the magic, or -1 if not found.
        /// </summary>
        static int FindWd3Offset(byte[] bytes)
        {
            if (bytes.Length < Wd3Magic.Length)
                return -1;

            // Scan the entire file for the 4-byte magic "WD3\x01".
            // The magic is specific enough (including the 0x01 version byte) that
            // false positives in PE headers are extremely unlikely.
            // This matches the approach in scripts/wd3_parser_v2.py:find_wd3().
            for (int i = 0; i <= bytes.Length - Wd3Magic.Length; i++)
            {
                if (bytes[i] == Wd3Magic[0]
                    && bytes[i + 1] == Wd3Magic[1]
                    && bytes[i + 2] == Wd3Magic[2]
                    && bytes[i + 3] == Wd3Magic[3])
                {
                    return i;
                }
            }
            return -1;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Color conversion
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Convert float RGBA (0.0–1.0+) to uint8 color bytes using /128 normalization.
        /// float 1.0 → byte 128 (0x80), float 0.0 → byte 0 (0x00), float 2.0 → byte 255.
        /// </summary>
        static byte[] FloatToColorBytes(float r, float g, float b, float a)
        {
            return new byte[]
            {
                FloatToColorByte(r),
                FloatToColorByte(g),
                FloatToColorByte(b),
                FloatToColorByte(a),
            };
        }

        /// <summary>
        /// Convert a single float color component to a uint8 byte.
        /// The runtime normalizes: float = byte / 128.0, so byte = float * 128.
        /// Clamped to 0–255.
        /// </summary>
        static byte FloatToColorByte(float value)
        {
            int intValue = (int)Math.Round(value * ColorNormalizationFactor, MidpointRounding.AwayFromZero);
            return (byte)Math.Clamp(intValue, 0, 255);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  PPP record walking and color patching
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Walk PPP records in a single WD3 stream and patch all RGBA color bytes.
        ///
        /// PPP record layout (from FFX_Magic_PPP_BuildDrawableFromOpcode_ProcessCmd):
        ///   +0:  u8  opcode_byte (alpha function selector)
        ///   +1:  u8  type_tag (3 = texture/sprite with sprite data)
        ///   +2:  u16 count (sprite count)
        ///   +4:  u16 f1–f6 (6 × u16 metadata)
        ///   +16: sprite data (count × 40 bytes, only for type_tag=3)
        ///
        /// Sprite layout (40 bytes):
        ///   +0–17:  3 vertices (3 × int16 × 3 = 18 bytes)
        ///   +18–21: RGBA colors (4 × uint8, /128 normalization)
        ///   +20–27: UV coordinates (4 × uint16, overlapping with B/A at +20/+21)
        ///   +28–39: padding/indices
        /// </summary>
        /// <param name="dllBytes">The full DLL byte array (modified in-place).</param>
        /// <param name="blob">The parsed WD3 blob.</param>
        /// <param name="streamIdx">Index of the stream to patch.</param>
        /// <param name="colorBytes">4-byte RGBA color payload.</param>
        /// <returns>List of patches applied.</returns>
        static List<MagicDllBytePatch> PatchStreamColors(
            byte[] dllBytes,
            Wd3Blob blob,
            int streamIdx,
            byte[] colorBytes)
        {
            var patches = new List<MagicDllBytePatch>();
            Wd3Stream stream = blob.Streams[streamIdx];
            byte[] data = stream.Data;

            if (data.Length < RecordHeaderSize)
                return patches;

            // Absolute file offset where this stream's data begins.
            int streamDataFileOffset = blob.BlobOffset + (int)stream.Header.StartOffset;
            string colorHex = BitConverter.ToString(colorBytes)
                .Replace("-", "", StringComparison.Ordinal);

            int offset = 0;
            while (offset + RecordHeaderSize <= data.Length)
            {
                byte typeTag = data[offset + 1];
                ushort count = BitConverter.ToUInt16(data, offset + 2);

                int recordSize;
                if (typeTag == TypeTagSprite)
                {
                    // type_tag=3: 16-byte header + count × 40-byte sprites
                    int spriteCount = Math.Min((int)count, (int)MaxSpritesPerRecord);
                    recordSize = RecordHeaderSize + SpriteSize * spriteCount;

                    if (offset + recordSize > data.Length)
                        break;

                    // Patch color bytes in each sprite
                    for (int s = 0; s < spriteCount; s++)
                    {
                        int spriteDataOffset = offset + RecordHeaderSize + s * SpriteSize;
                        int colorDataOffset = spriteDataOffset + SpriteColorOffset;

                        if (colorDataOffset + ColorByteCount > data.Length)
                            break;

                        int fileOffset = streamDataFileOffset + colorDataOffset;

                        // Patch the DLL bytes in-place
                        for (int c = 0; c < ColorByteCount; c++)
                            dllBytes[fileOffset + c] = colorBytes[c];

                        patches.Add(new MagicDllBytePatch
                        {
                            FileOffset = fileOffset,
                            Hex = colorHex,
                            Note = $"stream {streamIdx} record@0x{offset:X} sprite {s} RGBA"
                        });
                    }
                }
                else
                {
                    // Regular record: just the 16-byte header
                    recordSize = RecordHeaderSize;
                }

                offset += recordSize;
            }

            return patches;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Output
        // ═══════════════════════════════════════════════════════════════════════

        static void WriteOutputDll(string outputDll, byte[] bytes)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(outputDll));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(outputDll, bytes);
        }
    }
}
