// ── FevReader.cs ────────────────────────────────────────────────────
// Standalone, self-contained FEV (FMOD Event) binary reader for research.
// NOT part of FFXProjectEditor — research-only tool in work/research_tools/.
//
// Derived from:
//   - vgmstream/src/meta/fsb_fev.h (losnoco/vgmstream)
//   - Empirical hex analysis of FFX PC .fev files
//
// FFX PC uses RIFF-wrapped FEV format (version 0x45 = FMOD Ex 69.0).
// The LGCY chunk contains the legacy FEV1 data with event hierarchy,
// wave bank definitions, categories, and sound definitions.
//
// Key insight: the editor's existing FevLegacyReader uses heuristic
// byte-pattern search for seId. This parser does a full structural parse.
// ──────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FevReference
{
    public class FevChunk
    {
        public string Id;
        public int Offset;
        public int BodyOffset;
        public int Size;
    }

    public class FevFile
    {
        public uint Version;
        public string BankName;
        public List<string> Strings = new();  // STRR string table
        public string Language;
        public List<FevObject> Objects = new();
    }

    public class FevObject
    {
        public uint Id;
        public uint Type;
    }

    public static class FevReader
    {
        /// <summary>
        /// Walk top-level RIFF chunks and inner LIST sub-chunks.
        /// Returns all chunks found.
        /// </summary>
        public static List<FevChunk> ParseChunks(byte[] data)
        {
            var all = new List<FevChunk>();
            // FIX 2026-09-15 (FEV-FIX, lane FFX-STRUCTURES): old bound `data.Length - 8`
            // truncated the walk and the trailing LIST chunk failed the overflow check
            // ("LIST final dá OVERFLOW" — artifacts/2026-09-15/script-validation/
            // menu-phyre-magic-audio-encoding.md §4), so only FMT was ever surfaced.
            // Layout evidence (master doc FFX_STRUCTURE_COMPLETE_2026-09-14.md §11.24 +
            // real banks): the RIFF u32 at 0x04 holds fileSize-8, so the payload ends at
            // 8+riffSize == data.Length on intact banks — the final LIST ends exactly at
            // EOF (ffx2_music.fev 26,180 B: LIST 0x18..0x6644; ffx_music.fev 35,898 B:
            // LIST 0x18..0x8C3A), never 8 bytes short. New bound honors the declared
            // RIFF end, clamped to file size so corrupt headers degrade gracefully and
            // editor-appended FFX2SEID trailers (20 B past riffEnd, read by
            // FevLegacyReader.TryReadRegistrationTrailer) are not walked as chunks.
            int end = data.Length;
            if (data.Length >= 8)
            {
                long riffEnd = 8L + BitConverter.ToUInt32(data, 4);
                if (riffEnd >= 12 && riffEnd < data.Length)
                    end = (int)riffEnd;
            }
            WalkChunks(data, 12, end, all);
            return all;
        }

        static void WalkChunks(byte[] data, int start, int end, List<FevChunk> outList, string indent = "")
        {
            int pos = start;
            while (pos + 8 <= end)
            {
                string id = Encoding.ASCII.GetString(data, pos, 4);
                int size = BitConverter.ToInt32(data, pos + 4);
                int body = pos + 8;
                if (body + size > end)
                {
                    Console.Error.WriteLine($"{indent}chunk {id} at {pos:X} size {size} OVERFLOW");
                    break;
                }
                outList.Add(new FevChunk { Id = id, Offset = pos, BodyOffset = body, Size = size });
                Console.WriteLine($"{indent}chunk {id,-4} at 0x{pos:X}  size={size}  body=0x{body:X}-0x{body + size:X}");
                pos = body + size + (size & 1); // RIFF alignment: word-aligned
                if (outList.Count > 256) break;
            }
        }

        /// <summary>
        /// NEW 2026-09-16 (FMT-AUDIO audit): descend into a top-level LIST chunk's
        /// PROJ/EVCT body and enumerate its sub-chunks (OBCT/PROP/LGCY/EPRP/STRR/
        /// LANG for PROJ banks). The reader previously surfaced only FMT+LIST at
        /// the top level — every event/string/object lived one level deeper and
        /// was invisible to chunk enumeration. Verified on ffx_music.fev: LIST
        /// (0x18) -> PROJ -> OBCT(33) + PROP + LGCY(28461) + EPRP + STRR(180) +
        /// LANG, sub-chunks word-aligned.
        /// Returns empty list for non-LIST chunks.
        /// </summary>
        public static List<FevChunk> ParseListSubChunks(byte[] data, FevChunk listChunk)
        {
            var result = new List<FevChunk>();
            if (listChunk == null || listChunk.Id != "LIST") return result;
            int body = listChunk.BodyOffset;
            int end = body + listChunk.Size;
            if (end > data.Length) end = data.Length;
            if (body + 4 > end) return result;
            // body[0..3] = list type (PROJ/EVCT); sub-chunks follow
            int pos = body + 4;
            while (pos + 8 <= end)
            {
                string id = Encoding.ASCII.GetString(data, pos, 4);
                int size = BitConverter.ToInt32(data, pos + 4);
                int cbody = pos + 8;
                if (size < 0 || cbody + size > end) break;
                result.Add(new FevChunk { Id = id, Offset = pos, BodyOffset = cbody, Size = size });
                pos = cbody + size + (size & 1);
                if (result.Count > 256) break;
            }
            return result;
        }

        /// <summary>
        /// Parse the STRR string table chunk.
        /// Format: u32 count, u32 offsets[count], then null-terminated strings.
        /// </summary>
        public static List<string> ParseStrr(byte[] data, FevChunk strrChunk)
        {
            var result = new List<string>();
            int body = strrChunk.BodyOffset;
            int count = BitConverter.ToInt32(data, body);
            if (count <= 0 || count > 10000) return result;

            var offsets = new int[count];
            for (int i = 0; i < count; i++)
                offsets[i] = BitConverter.ToInt32(data, body + 4 + i * 4);

            int strBufStart = body + 4 + count * 4;
            for (int i = 0; i < count; i++)
            {
                int absOff = strBufStart + offsets[i];
                if (absOff < data.Length)
                {
                    int end = Array.IndexOf(data, (byte)0, absOff);
                    if (end < 0) end = Math.Min(absOff + 256, data.Length);
                    result.Add(Encoding.ASCII.GetString(data, absOff, end - absOff));
                }
                else
                    result.Add("");
            }
            return result;
        }

        /// <summary>
        /// Parse the OBCT chunk (object table).
        /// Format: u32 count, then count × (u32 objectId, u32 objectType).
        /// </summary>
        public static List<FevObject> ParseObct(byte[] data, FevChunk obctChunk)
        {
            var result = new List<FevObject>();
            int body = obctChunk.BodyOffset;
            int count = BitConverter.ToInt32(data, body);
            // FIX 2026-09-16 (FMT-AUDIO audit): entries start at body+4 (right
            // after the u32 count), each 8B — the old bound `body + 8 + i*8 + 8`
            // charged 4 phantom bytes per entry and dropped the LAST object
            // (ffx_music.fev: count=33, reader surfaced only 32).
            for (int i = 0; i < count && body + 4 + (i + 1) * 8 <= body + obctChunk.Size; i++)
            {
                int off = body + 4 + i * 8;
                result.Add(new FevObject
                {
                    Id = BitConverter.ToUInt32(data, off),
                    Type = BitConverter.ToUInt32(data, off + 4)
                });
            }
            return result;
        }

        /// <summary>
        /// Extract the FEV version from the FMT chunk body.
        /// FFX uses version 0x00450000 (69.0 = FMOD Ex 4.45).
        /// </summary>
        public static uint ReadVersion(byte[] data)
        {
            if (data.Length >= 0x18
                && Encoding.ASCII.GetString(data, 0, 4) == "RIFF"
                && Encoding.ASCII.GetString(data, 8, 4) == "FEV "
                && Encoding.ASCII.GetString(data, 0x0C, 4) == "FMT ")
                return BitConverter.ToUInt32(data, 0x14);
            return 0;
        }

        /// <summary>
        /// Find all occurrences of a u32 pattern in the data.
        /// Used for seId search.
        /// </summary>
        public static List<int> FindUInt32(byte[] data, uint value)
        {
            var hits = new List<int>();
            byte[] needle = BitConverter.GetBytes(value);
            for (int i = 0; i <= data.Length - 4; i++)
            {
                if (data[i] == needle[0] && data[i + 1] == needle[1]
                    && data[i + 2] == needle[2] && data[i + 3] == needle[3])
                    hits.Add(i);
            }
            return hits;
        }
    }
}
