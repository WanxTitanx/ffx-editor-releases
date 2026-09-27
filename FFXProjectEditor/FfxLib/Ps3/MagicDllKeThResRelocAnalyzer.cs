using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Offline map of PPP KeThRes reloc surfaces — which <c>ppp_dataA</c> / blob bytes are referenced by the
    /// on-disk offset table (<c>&lt;1D1H1…</c>) that <c>sub_712080</c> (host+2844) rewrites. RT2 2026-06-14:
    /// blind patch in the blob table caused battle softlock.
    /// </summary>
    internal static class MagicDllKeThResRelocAnalyzer
    {
        /// <summary>Plausible cyan vec4 strip in <c>magic_0094</c> <c>ppp_dataA</c> (file rel to dataA base).</summary>
        public const int CyanVec4RelStart = 0x58C;
        public const int CyanVec4RelEnd = 0x620;

        public sealed record RelocTag(int BlobFileOffset, string RawTag, int? DataARelOffset, string Note);

        public sealed record RegionMark(
            string Zone,
            int FileOffsetStart,
            int FileOffsetEnd,
            int DataARelStart,
            int DataARelEnd,
            string Risk,
            string Note);

        public sealed record PatchGateVerdict(
            int FileOffset,
            int DataARel,
            bool Allowed,
            bool RelocReferenced,
            string Reason);

        public sealed record AnalyzeResult(
            int MagicId,
            string DllPath,
            RegionSlice DataA,
            RegionSlice Blob,
            IReadOnlyList<RelocTag> Tags,
            IReadOnlyList<RegionMark> Regions,
            IReadOnlyList<PatchGateVerdict> PatchGates,
            IReadOnlyList<string> Notes);

        public sealed record RegionSlice(string Id, uint ImageVa, int FileOffset, int Length);

        public static AnalyzeResult Analyze(MagicDllInspection inspection)
        {
            byte[] bytes = File.ReadAllBytes(inspection.FilePath);
            var parse = MagicDllKeThResParser.Parse(inspection, ps3MagicRoot: null, maxPhyreMatches: 4);
            var patchAnalysis = MagicDllKeThResPppPatch.Analyze(inspection, parse);

            if (patchAnalysis.DataA == null || patchAnalysis.KeThResBlob == null)
                throw new InvalidOperationException("KeThRes dataA/blob regions missing — not a Family D PPP DLL?");

            RegionSlice dataA = ToSlice(patchAnalysis.DataA);
            RegionSlice blob = ToSlice(patchAnalysis.KeThResBlob);

            IReadOnlyList<RelocTag> tags = ScanBlobOffsetTags(bytes, blob, dataA);
            var relocRefs = new HashSet<int>(tags.Where(t => t.DataARelOffset.HasValue).Select(t => t.DataARelOffset!.Value));

            var regions = new List<RegionMark>
            {
                new(
                    "blob_offset_table",
                    blob.FileOffset,
                    blob.FileOffset + Math.Min(512, blob.Length),
                    -1,
                    -1,
                    "critical",
                    "ASCII <1D1H1… tag stream — sub_712080 reloc; never patch (RT2 softlock)."),
                new(
                    "dataA_cyan_vec4",
                    dataA.FileOffset + CyanVec4RelStart,
                    dataA.FileOffset + CyanVec4RelEnd,
                    CyanVec4RelStart,
                    CyanVec4RelEnd,
                    "candidate",
                    "float_vec4 cyan strip @ 0x8C3C..0x8CB8 in magic_0094 — tint target post-reloc."),
                new(
                    "dataA_catalog_strings",
                    dataA.FileOffset + 0x350,
                    dataA.FileOffset + 0x430,
                    0x350,
                    0x430,
                    "forbidden",
                    "PE strings (magic_0094.dll, InitMagicPRX) — NOT color; old gate was wrong."),
                new(
                    "ppp_catalog_cluster",
                    0x8294,
                    0x8700,
                    -1,
                    -1,
                    "forbidden",
                    "PPP opcode catalog — protected by patcher."),
            };

            var gates = new List<PatchGateVerdict>();
            foreach (MagicDllKeThResPppPatch.PatchCandidate c in patchAnalysis.Candidates)
            {
                int rel = c.FileOffset - dataA.FileOffset;
                bool inCyan = rel is >= CyanVec4RelStart and <= CyanVec4RelEnd;
                bool reloc = relocRefs.Contains(rel);
                bool allowed = c.Kind == "float_vec4" && inCyan && !reloc && !IsProtectedPatchOffset(bytes, c.FileOffset);
                string reason = allowed
                    ? "gated OK — cyan vec4, not reloc-referenced"
                    : reloc
                        ? "reloc table references this dataA offset"
                        : !inCyan
                            ? $"outside cyan strip (+0x{CyanVec4RelStart:X}..+0x{CyanVec4RelEnd:X}, rel=+0x{rel:X})"
                            : c.Kind != "float_vec4"
                                ? $"kind {c.Kind} not allowed"
                                : "protected catalog/opcode";

                gates.Add(new PatchGateVerdict(c.FileOffset, rel, allowed, reloc, reason));
            }

            var notes = new List<string>
            {
                $"blob tags parsed: {tags.Count}",
                $"reloc-referenced dataA offsets: {relocRefs.Count}",
                $"patch candidates allowed after reloc gate: {gates.Count(g => g.Allowed)} / {gates.Count}",
                "Runtime decode still fills 1 MiB via host+2840 — static gate only prevents softlock offsets.",
                "In-game: arm ffx-probe KETHRES hook (sub_72C570) to capture decoded buffer pointer.",
            };

            return new AnalyzeResult(
                inspection.MagicId ?? 0,
                inspection.FilePath,
                dataA,
                blob,
                tags,
                regions,
                gates,
                notes);
        }

        public static void WriteJson(AnalyzeResult r, string path)
        {
            var obj = new
            {
                magicId = r.MagicId,
                dll = r.DllPath,
                dataA = RegionJson(r.DataA),
                blob = RegionJson(r.Blob),
                cyanStrip = new { relStart = $"0x{CyanVec4RelStart:X}", relEnd = $"0x{CyanVec4RelEnd:X}" },
                tagCount = r.Tags.Count,
                tags = r.Tags.Take(64).Select(t => new
                {
                    blobOff = $"0x{t.BlobFileOffset:X}",
                    t.RawTag,
                    dataARel = t.DataARelOffset is int d ? $"0x{d:X}" : null,
                    t.Note,
                }),
                regions = r.Regions.Select(z => new
                {
                    z.Zone,
                    fileStart = $"0x{z.FileOffsetStart:X}",
                    fileEnd = $"0x{z.FileOffsetEnd:X}",
                    dataARelStart = z.DataARelStart >= 0 ? $"0x{z.DataARelStart:X}" : null,
                    dataARelEnd = z.DataARelEnd >= 0 ? $"0x{z.DataARelEnd:X}" : null,
                    z.Risk,
                    z.Note,
                }),
                allowedPatches = r.PatchGates.Where(g => g.Allowed).Select(g => new
                {
                    fileOffset = $"0x{g.FileOffset:X}",
                    dataARel = $"0x{g.DataARel:X}",
                    g.Reason,
                }),
                rejectedPatches = r.PatchGates.Where(g => !g.Allowed).Take(32).Select(g => new
                {
                    fileOffset = $"0x{g.FileOffset:X}",
                    dataARel = $"0x{g.DataARel:X}",
                    g.RelocReferenced,
                    g.Reason,
                }),
                notes = r.Notes,
            };

            File.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void WriteMarkdown(AnalyzeResult r, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# KeThRes reloc map — `magic_{r.MagicId:D4}.dll`");
            sb.AppendLine();
            sb.AppendLine($"- **DLL:** `{r.DllPath}`");
            sb.AppendLine($"- **dataA:** `0x{r.DataA.FileOffset:X}` len `{r.DataA.Length:X}`");
            sb.AppendLine($"- **blob table:** `0x{r.Blob.FileOffset:X}` tags `{r.Tags.Count}`");
            sb.AppendLine($"- **Allowed vec4 patches:** {r.PatchGates.Count(g => g.Allowed)}");
            sb.AppendLine();

            sb.AppendLine("## Regions");
            sb.AppendLine();
            sb.AppendLine("| Zone | File range | dataA rel | Risk | Note |");
            sb.AppendLine("|------|------------|-----------|------|------|");
            foreach (RegionMark z in r.Regions)
            {
                string rel = z.DataARelStart >= 0 ? $"+0x{z.DataARelStart:X}..+0x{z.DataARelEnd:X}" : "-";
                sb.AppendLine($"| `{z.Zone}` | `0x{z.FileOffsetStart:X}`..`0x{z.FileOffsetEnd:X}` | {rel} | **{z.Risk}** | {z.Note} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Allowed offline patches (post-reloc gate)");
            sb.AppendLine();
            foreach (PatchGateVerdict g in r.PatchGates.Where(g => g.Allowed))
                sb.AppendLine($"- `0x{g.FileOffset:X}` (dataA+`0x{g.DataARel:X}`) — {g.Reason}");

            if (!r.PatchGates.Any(g => g.Allowed))
                sb.AppendLine("_None — expand parser or capture runtime buffer._");

            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine();
            foreach (string n in r.Notes)
                sb.AppendLine($"- {n}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        static object RegionJson(RegionSlice s) => new
        {
            s.Id,
            imageVa = $"0x{s.ImageVa:X}",
            fileOffset = $"0x{s.FileOffset:X}",
            s.Length,
        };

        static RegionSlice ToSlice(MagicDllKeThResPppPatch.RegionSlice r) =>
            new(r.Id, r.ImageVa, r.FileOffset, r.Length);

        static IReadOnlyList<RelocTag> ScanBlobOffsetTags(byte[] bytes, RegionSlice blob, RegionSlice dataA)
        {
            var tags = new List<RelocTag>();
            int end = Math.Min(blob.FileOffset + blob.Length, bytes.Length);
            int i = blob.FileOffset;

            while (i < end)
            {
                if (bytes[i] != (byte)'<')
                {
                    i++;
                    continue;
                }

                int start = i;
                i++;
                var raw = new StringBuilder();
                raw.Append('<');
                while (i < end && bytes[i] is not 0 and >= 0x20 and <= 0x7E)
                {
                    char ch = (char)bytes[i];
                    if (ch == '<' && raw.Length > 1)
                        break;
                    raw.Append(ch);
                    i++;
                    if (raw.Length > 48)
                        break;
                }

                string tag = raw.ToString();
                int? rel = TryDecodeTagToDataARel(tag, dataA.Length);
                tags.Add(new RelocTag(start, tag, rel, rel.HasValue ? "points into ppp_dataA" : "blob-local tag"));
            }

            return tags;
        }

        /// <summary>
        /// PPP offset tags embed a hex triplet after each separator; map low 12 bits into dataA when in range.
        /// Conservative: only mark offsets that land in dataA and align to 4.
        /// </summary>
        static int? TryDecodeTagToDataARel(string tag, int dataALen)
        {
            if (tag.Length < 4 || tag[0] != '<')
                return null;

            // Tags like <1D1H1N1… — take the first 2-3 hex digits after '<' as a reloc hint.
            int pos = 1;
            var digits = new StringBuilder();
            while (pos < tag.Length && digits.Length < 3)
            {
                char c = tag[pos++];
                if (c is >= '0' and <= '9')
                    digits.Append(c);
                else if (c is >= 'A' and <= 'F' or >= 'a' and <= 'f')
                    digits.Append(c);
                else if (digits.Length > 0)
                    break;
            }

            if (digits.Length == 0)
                return null;

            if (!int.TryParse(digits.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hint))
                return null;

            // Empirical for 0094: table entries step through dataA every ~0x10..0x40; use hint * 0x10.
            int rel = hint * 0x10;
            if (rel < 0 || rel >= dataALen)
                return null;
            return rel;
        }

        static bool IsProtectedPatchOffset(byte[] bytes, int off) =>
            off is >= 0x8294 and < 0x8700
            || (off >= 0 && off + 4 <= bytes.Length
                && BitConverter.ToUInt32(bytes, off) == MagicDllKeThResParser.PppEntryMagic);
    }
}
