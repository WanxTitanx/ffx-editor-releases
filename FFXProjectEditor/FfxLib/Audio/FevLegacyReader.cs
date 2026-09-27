using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.Audio
{
    /// <summary>Read-only probe + sequence blob locate for legacy FMOD FEV battle banks.</summary>
    public static class FevLegacyReader
    {
        public const int DefaultSequenceBlobBytes = 96;
        public static readonly byte[] RegistrationMagic = "FFX2SEID"u8.ToArray();

        public sealed record FevProbe(
            bool Ok,
            string? FormatHint,
            long Size,
            IReadOnlyList<FevChunkInfo> Chunks,
            string? Error);

        public sealed record FevChunkInfo(string FourCc, int HeaderOffset, int BodyOffset, int BodySize);

        public sealed record SequenceBlobHit(
            uint SeId,
            int AbsoluteOffset,
            int BlobLength,
            string ChunkFourCc,
            int ChunkBodyOffset);

        public static FevProbe Probe(string fevPath)
        {
            if (!File.Exists(fevPath))
                return new FevProbe(false, null, 0, [], "file not found");

            byte[] bytes = File.ReadAllBytes(fevPath);
            if (bytes.Length < 12)
                return new FevProbe(false, null, bytes.Length, [], "too small");

            string riff = Encoding.ASCII.GetString(bytes, 0, 4);
            if (riff != "RIFF" && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0)) != 0x46455620)
                return new FevProbe(false, "not RIFF/FEV", bytes.Length, [], null);

            IReadOnlyList<FevChunkInfo> chunks = ParseChunks(bytes);
            string hint = chunks.Any(c => c.FourCc is "FEV " or "FMT ")
                ? "legacy FMOD FEV (RIFF)"
                : "RIFF container";
            return new FevProbe(true, hint, bytes.Length, chunks, null);
        }

        public static IReadOnlyList<FevChunkInfo> ParseChunks(byte[] bytes)
        {
            var chunks = new List<FevChunkInfo>();
            int pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + 4));
                int body = pos + 8;
                if (body + size > bytes.Length)
                    break;
                chunks.Add(new FevChunkInfo(id, pos, body, size));
                pos = body + size + (size & 1);
                if (chunks.Count > 128) break;
            }
            return chunks;
        }

        public static IReadOnlyList<int> FindSeIdOffsets(byte[] fevBytes, uint seId)
        {
            var hits = new List<int>();
            byte[] needle = BitConverter.GetBytes(seId);
            for (int i = 0; i <= fevBytes.Length - 4; i++)
            {
                if (fevBytes[i] == needle[0] && fevBytes[i + 1] == needle[1]
                    && fevBytes[i + 2] == needle[2] && fevBytes[i + 3] == needle[3])
                    hits.Add(i);
            }
            return hits;
        }

        public static SequenceBlobHit? TryLocateSequenceBlob(string fevPath, uint seId, int blobBytes = DefaultSequenceBlobBytes)
        {
            if (!File.Exists(fevPath))
                return null;

            byte[] bytes = File.ReadAllBytes(fevPath);
            SequenceBlobHit? bySeId = TryLocateBySeIdBytes(bytes, seId, blobBytes);
            if (bySeId != null)
                return bySeId;

            return null;
        }

        public static SequenceBlobHit? TryLocateSequenceByFsbIndex(string fevPath, int fsbSampleIndex0, int blobBytes = DefaultSequenceBlobBytes)
        {
            if (!File.Exists(fevPath))
                return null;

            byte[] bytes = File.ReadAllBytes(fevPath);
            foreach (int width in new[] { 4, 2 })
            {
                byte[] needle = width == 4
                    ? BitConverter.GetBytes((uint)fsbSampleIndex0)
                    : BitConverter.GetBytes((ushort)fsbSampleIndex0);

                for (int i = 0; i <= bytes.Length - width; i++)
                {
                    if (!bytes.AsSpan(i, width).SequenceEqual(needle))
                        continue;

                    IReadOnlyList<FevChunkInfo> chunks = ParseChunks(bytes);
                    FevChunkInfo? chunk = chunks.FirstOrDefault(c => i >= c.BodyOffset && i < c.BodyOffset + c.BodySize);
                    int blobStart = chunk != null
                        ? Math.Max(chunk.BodyOffset, i - blobBytes / 4)
                        : Math.Max(0, i - 16);
                    int blobEnd = chunk != null
                        ? Math.Min(chunk.BodyOffset + chunk.BodySize, i + blobBytes * 3 / 4)
                        : Math.Min(bytes.Length, blobStart + blobBytes);
                    int len = blobEnd - blobStart;
                    if (len < 16) continue;

                    return new SequenceBlobHit(
                        (uint)fsbSampleIndex0,
                        blobStart,
                        len,
                        chunk?.FourCc ?? "FSBIDX",
                        chunk?.BodyOffset ?? 0);
                }
            }

            return null;
        }

        static SequenceBlobHit? TryLocateBySeIdBytes(byte[] bytes, uint seId, int blobBytes)
        {
            IReadOnlyList<int> offsets = FindSeIdOffsets(bytes, seId);
            if (offsets.Count == 0)
                return null;

            IReadOnlyList<FevChunkInfo> chunks = ParseChunks(bytes);
            foreach (int off in offsets.OrderBy(o => o))
            {
                FevChunkInfo? chunk = chunks.FirstOrDefault(c => off >= c.BodyOffset && off < c.BodyOffset + c.BodySize);
                if (chunk == null) continue;

                int blobStart = Math.Max(chunk.BodyOffset, off - blobBytes / 4);
                int blobEnd = Math.Min(chunk.BodyOffset + chunk.BodySize, off + blobBytes * 3 / 4);
                int len = blobEnd - blobStart;
                if (len < 16) continue;

                return new SequenceBlobHit(seId, blobStart, len, chunk.FourCc, chunk.BodyOffset);
            }

            int first = offsets[0];
            return new SequenceBlobHit(seId, Math.Max(0, first - 16), blobBytes, "?", 0);
        }

        public static bool ContainsSeId(string fevPath, uint seId)
        {
            if (!File.Exists(fevPath))
                return false;

            byte[] bytes = File.ReadAllBytes(fevPath);
            if (FindSeIdOffsets(bytes, seId).Count > 0)
                return true;

            return TryReadRegistrationTrailer(bytes, out uint regSeId, out _) && regSeId == seId;
        }

        public static bool TryReadRegistrationTrailer(byte[] fevBytes, out uint seId, out int fsbSampleIndex0)
        {
            seId = 0;
            fsbSampleIndex0 = -1;
            if (fevBytes.Length < RegistrationMagic.Length + 12)
                return false;

            int magicAt = IndexOf(fevBytes, RegistrationMagic);
            if (magicAt < 0 || magicAt + RegistrationMagic.Length + 8 > fevBytes.Length)
                return false;

            int pos = magicAt + RegistrationMagic.Length;
            seId = BinaryPrimitives.ReadUInt32LittleEndian(fevBytes.AsSpan(pos));
            fsbSampleIndex0 = (int)BinaryPrimitives.ReadUInt32LittleEndian(fevBytes.AsSpan(pos + 4));
            return true;
        }

        static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                    return i;
            }
            return -1;
        }

        public static IReadOnlyList<uint> ScanCandidateSequenceIds(string fevPath, uint minId = 8000, uint maxId = 20000)
        {
            var hits = new HashSet<uint>();
            byte[] bytes = File.ReadAllBytes(fevPath);
            for (int i = 0; i <= bytes.Length - 4; i += 4)
            {
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i));
                if (v >= minId && v <= maxId)
                    hits.Add(v);
            }
            var list = new List<uint>(hits);
            list.Sort();
            return list;
        }

        public static IReadOnlyList<uint> FindSeIdGaps(
            IReadOnlyList<uint> corpusSeIds,
            string? fevPath = null,
            uint minId = 8000,
            uint maxId = 20000)
        {
            var used = new HashSet<uint>(corpusSeIds);
            if (!string.IsNullOrWhiteSpace(fevPath) && File.Exists(fevPath))
            {
                foreach (uint id in ScanCandidateSequenceIds(fevPath, minId, maxId))
                    used.Add(id);
            }

            var gaps = new List<uint>();
            for (uint id = minId; id <= maxId && gaps.Count < 64; id++)
            {
                if (!used.Contains(id))
                    gaps.Add(id);
            }
            return gaps;
        }
    }
}
