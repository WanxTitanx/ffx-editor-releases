using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Offline analyze + patch <c>ppp_dataA</c> / KeThRes static surfaces in Family D magic DLLs.
    /// Runtime decode (host+2840) still required for full 1 MiB — this targets on-disk PPP payload.
    /// </summary>
    internal static class MagicDllKeThResPppPatch
    {
        public sealed record RegionSlice(
            string Id,
            uint ImageVa,
            int FileOffset,
            int Length);

        public sealed record PatchCandidate(
            string Kind,
            int FileOffset,
            uint ImageVa,
            int Length,
            string BeforeHex,
            string AfterHex,
            string Note,
            double Score);

        public sealed record AnalyzeResult(
            int MagicId,
            string DllPath,
            string PrimaryResource,
            RegionSlice? DataA,
            RegionSlice? KeThResBlob,
            IReadOnlyList<PatchCandidate> Candidates,
            IReadOnlyList<string> Notes);

        public sealed record PatchResult(
            int MagicId,
            string SourceDll,
            string OutputDll,
            string SourceSha256,
            string OutputSha256,
            int BytesChanged,
            IReadOnlyList<PatchCandidate> Applied,
            AnalyzeResult Analysis);

        public static AnalyzeResult Analyze(MagicDllInspection inspection, MagicDllKeThResParser.MagicDllKeThResParseResult? parse = null, bool includeDataSection = false)
        {
            parse ??= MagicDllKeThResParser.Parse(inspection, ps3MagicRoot: null, maxPhyreMatches: 4);
            byte[] bytes = File.ReadAllBytes(inspection.FilePath);
            var notes = new List<string>();
            RegionSlice? dataA = null;
            RegionSlice? blob = null;

            MagicDllKeThResParser.KeThResResourceSpec? primary = parse.PrimaryBoltResource;
            if (primary != null && TryParseVa(primary.Entry.DataRvaAHex, out uint dataAVa))
            {
                int off = inspection.RvaToFileOffset((int)(dataAVa - MagicDllKeThResParser.ImageBase));
                dataA = new RegionSlice("ppp_dataA", dataAVa, off, Math.Min(0x1000, bytes.Length - off));
                notes.Add($"dataA @ 0x{dataAVa:X} file 0x{off:X} len {dataA.Length}");
            }
            else
            {
                notes.Add("No pppKeThRes32x4 dataA — parse failed.");
            }

            try
            {
                int blobOff = inspection.RvaToFileOffset((int)(MagicDllKeThResDumper.KeThResBlobVa - MagicDllKeThResParser.ImageBase));
                int blobLen = Math.Min(MagicDllKeThResDumper.RuntimeBlobSize, bytes.Length - blobOff);
                blob = new RegionSlice("kethres_blob", MagicDllKeThResDumper.KeThResBlobVa, blobOff, blobLen);
                int blobNz = bytes.AsSpan(blobOff, blobLen).ToArray().Count(b => b != 0);
                notes.Add($"kethres_blob on-disk nz={blobNz} (offset table only if nz&lt;2k)");
            }
            catch
            {
                notes.Add("kethres_blob RVA unmapped in PE.");
            }

            var candidates = new List<PatchCandidate>();
            if (dataA != null)
            {
                candidates.AddRange(ScanFloatVec4Blue(bytes, inspection, dataA));
                candidates.AddRange(ScanInt16Color15(bytes, inspection, dataA));
                candidates.AddRange(ScanRgba8Strips(bytes, inspection, dataA));
            }

            if (blob != null)
                notes.Add("kethres_blob on-disk nz tracked — never patch blob (offset table; RT2 softlock 2026-06-14).");

            if (includeDataSection)
            {
                foreach (MagicDllSection sec in inspection.Sections.Where(s => s.Name.Equals(".data", StringComparison.OrdinalIgnoreCase)))
                {
                    if (sec.RawSize <= 0)
                        continue;
                    var slice = new RegionSlice(".data", (uint)(MagicDllKeThResParser.ImageBase + sec.VirtualAddress), sec.RawPointer, sec.RawSize);
                    foreach (PatchCandidate c in ScanFloatVec4Blue(bytes, inspection, slice))
                    {
                        if (dataA != null && c.FileOffset >= dataA.FileOffset && c.FileOffset < dataA.FileOffset + dataA.Length)
                            continue;
                        candidates.Add(c);
                    }
                }

                notes.Add("includeDataSection=true — .data vec4 hits may duplicate retired RT2− sites.");
            }

            candidates = candidates
                .GroupBy(c => c.FileOffset)
                .Select(g => g.OrderByDescending(c => c.Score).First())
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.FileOffset)
                .ToList();

            if (candidates.Count == 0)
                notes.Add("No color patch candidates in static PPP surfaces — pixels likely materialize only after host+2840.");

            return new AnalyzeResult(
                inspection.MagicId ?? 0,
                inspection.FilePath,
                primary?.Name ?? "?",
                dataA,
                blob,
                candidates,
                notes);
        }

        public static PatchResult ApplyOrangeBoltPatch(
            MagicDllInspection inspection,
            string outputDll,
            int maxPatches = 24,
            Ps3MagicColorTransform? transform = null,
            bool includeDataSection = false)
        {
            transform ??= Ps3MagicColorTransform.ThundaFiraOrangeBolt;
            AnalyzeResult analysis = Analyze(inspection, includeDataSection: includeDataSection);
            byte[] bytes = File.ReadAllBytes(inspection.FilePath);
            var applied = new List<PatchCandidate>();

            foreach (PatchCandidate c in analysis.Candidates.Take(maxPatches))
            {
                if (IsProtectedPatchOffset(bytes, c.FileOffset))
                    continue;
                if (!IsAllowedPatchCandidate(c, analysis.DataA))
                    continue;
                if (c.Kind == "float_vec4")
                {
                    if (!TryPatchFloatVec4(bytes, c.FileOffset, transform, out byte[] before, out byte[] after))
                        continue;
                    applied.Add(c with
                    {
                        BeforeHex = ToHex(before),
                        AfterHex = ToHex(after),
                        Note = $"patched vec4 {transform}",
                    });
                }
                else if (c.Kind is "rgba8" or "rgba8_strip")
                {
                    int len = c.Kind == "rgba8_strip" ? c.Length : 4;
                    if (!TryPatchRgba8(bytes, c.FileOffset, len, transform, out byte[] before, out byte[] after))
                        continue;
                    applied.Add(c with
                    {
                        BeforeHex = ToHex(before),
                        AfterHex = ToHex(after),
                        Note = $"patched {len}B rgba {transform}",
                    });
                }
                else if (c.Kind == "int16_color15")
                {
                    if (!TryPatchInt16Color15(bytes, c.FileOffset, transform, out byte[] before, out byte[] after))
                        continue;
                    applied.Add(c with
                    {
                        BeforeHex = ToHex(before),
                        AfterHex = ToHex(after),
                        Note = $"patched int16/15 rgb {transform}",
                    });
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputDll)) ?? ".");
            File.WriteAllBytes(outputDll, bytes);

            return new PatchResult(
                inspection.MagicId ?? 0,
                inspection.FilePath,
                outputDll,
                Sha256Hex(File.ReadAllBytes(inspection.FilePath)),
                Sha256Hex(bytes),
                applied.Sum(a => a.Length),
                applied,
                analysis);
        }

        public static void WriteAnalyzeJson(AnalyzeResult r, string path)
        {
            var obj = new
            {
                magicId = r.MagicId,
                dll = r.DllPath,
                primary = r.PrimaryResource,
                dataA = r.DataA == null ? null : new
                {
                    r.DataA.Id,
                    imageVa = $"0x{r.DataA.ImageVa:X}",
                    fileOffset = $"0x{r.DataA.FileOffset:X}",
                    r.DataA.Length,
                },
                kethresBlob = r.KeThResBlob == null ? null : new
                {
                    r.KeThResBlob.Id,
                    imageVa = $"0x{r.KeThResBlob.ImageVa:X}",
                    fileOffset = $"0x{r.KeThResBlob.FileOffset:X}",
                    r.KeThResBlob.Length,
                },
                candidateCount = r.Candidates.Count,
                candidates = r.Candidates.Select(c => new
                {
                    c.Kind,
                    fileOffset = $"0x{c.FileOffset:X}",
                    imageVa = $"0x{c.ImageVa:X}",
                    c.Length,
                    c.Score,
                    c.Note,
                    before = c.BeforeHex,
                }),
                notes = r.Notes,
            };

            File.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void WritePatchMarkdown(PatchResult r, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# KeThRes PPP patch — `magic_{r.MagicId:D4}.dll`");
            sb.AppendLine();
            sb.AppendLine($"- **Source:** `{r.SourceDll}`");
            sb.AppendLine($"- **Output:** `{r.OutputDll}`");
            sb.AppendLine($"- **SHA-256:** `{r.SourceSha256[..16]}…` → `{r.OutputSha256[..16]}…`");
            sb.AppendLine($"- **Patches applied:** {r.Applied.Count}");
            sb.AppendLine();
            sb.AppendLine("## Applied");
            sb.AppendLine();
            if (r.Applied.Count == 0)
            {
                sb.AppendLine("_No static candidates patched — see analyze notes._");
            }
            else
            {
                sb.AppendLine("| Kind | VA | Off | Score | Before | After | Note |");
                sb.AppendLine("|------|-----|-----|------:|--------|-------|------|");
                foreach (PatchCandidate c in r.Applied)
                {
                    sb.AppendLine($"| `{c.Kind}` | `0x{c.ImageVa:X}` | `0x{c.FileOffset:X}` | {c.Score:F0} | `{c.BeforeHex}` | `{c.AfterHex}` | {c.Note} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Analysis notes");
            sb.AppendLine();
            foreach (string n in r.Analysis.Notes)
                sb.AppendLine($"- {n}");

            sb.AppendLine();
            sb.AppendLine("**RT2:** in-game only when testing visual change.");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        static IEnumerable<PatchCandidate> ScanFloatVec4Blue(byte[] bytes, MagicDllInspection inspection, RegionSlice region)
        {
            int end = region.FileOffset + region.Length - 16;
            for (int off = region.FileOffset; off <= end; off += 4)
            {
                float r = BitConverter.ToSingle(bytes, off);
                float g = BitConverter.ToSingle(bytes, off + 4);
                float b = BitConverter.ToSingle(bytes, off + 8);
                float a = BitConverter.ToSingle(bytes, off + 12);
                if (!IsPlausibleColorComponent(r) || !IsPlausibleColorComponent(g) || !IsPlausibleColorComponent(b))
                    continue;
                if (!IsPlausibleColorComponent(a) || a < 0.01f)
                    continue;
                if (b < g * 0.85f || b < r + 0.08f)
                    continue;
                if (IsProtectedPatchOffset(bytes, off))
                    continue;

                double score = 50 + (b - r) * 40 + (g > 0.2 ? 10 : 0);
                if (Math.Abs(r - 0.3f) < 0.08 && Math.Abs(g - 0.5f) < 0.12 && Math.Abs(b - 0.8f) < 0.15)
                    score += 40;

                yield return new PatchCandidate(
                    "float_vec4",
                    off,
                    FileOffsetToVa(inspection, off),
                    16,
                    ToHex(bytes.AsSpan(off, 16)),
                    "",
                    $"float vec4 R={r:F3} G={g:F3} B={b:F3} A={a:F3}",
                    score);
            }
        }

        static IEnumerable<PatchCandidate> ScanInt16Color15(byte[] bytes, MagicDllInspection inspection, RegionSlice region)
        {
            int end = region.FileOffset + region.Length - 8;
            for (int off = region.FileOffset; off <= end; off += 2)
            {
                short r = BitConverter.ToInt16(bytes, off);
                short g = BitConverter.ToInt16(bytes, off + 2);
                short b = BitConverter.ToInt16(bytes, off + 4);
                short a = BitConverter.ToInt16(bytes, off + 6);
                if (r is < 0 or > 15 || g is < 0 or > 15 || b is < 0 or > 15)
                    continue;
                if (a is < 0 or > 15)
                    continue;
                if (b <= g || b <= r)
                    continue;

                double score = 35 + (b - r) * 4;
                if (r is >= 3 and <= 8 && g is >= 4 and <= 10 && b is >= 8 and <= 14)
                    score += 25;

                yield return new PatchCandidate(
                    "int16_color15",
                    off,
                    FileOffsetToVa(inspection, off),
                    8,
                    ToHex(bytes.AsSpan(off, 8)),
                    "",
                    $"int16/15 R={r} G={g} B={b} A={a}",
                    score);
            }
        }

        static IEnumerable<PatchCandidate> ScanRgba8Strips(byte[] bytes, MagicDllInspection inspection, RegionSlice region)
        {
            int start = region.FileOffset;
            int limit = region.FileOffset + region.Length;
            int runStart = -1;
            int runLen = 0;

            void Flush()
            {
                if (runLen < 4)
                {
                    runStart = -1;
                    runLen = 0;
                    return;
                }

                int off = runStart;
                byte r = bytes[off];
                byte g = bytes[off + 1];
                byte b = bytes[off + 2];
                byte a = bytes[off + 3];
                double score = 20 + runLen + (b > r + 20 ? 25 : 0);
                string kind = runLen >= 8 ? "rgba8_strip" : "rgba8";
                // yield via local - use list pattern below
            }

            var singles = new List<PatchCandidate>();
            for (int off = start; off + 4 <= limit; off += 4)
            {
                byte r = bytes[off];
                byte g = bytes[off + 1];
                byte b = bytes[off + 2];
                byte a = bytes[off + 3];
                if (a < 8 || b < 40 || b < r + 15)
                    continue;
                if (r > 200 && g > 200)
                    continue; // sentinel -1 patterns
                if (IsProtectedPatchOffset(bytes, off))
                    continue;
                if (IsPrintableAsciiFour(bytes, off))
                    continue;

                double score = 25 + (b - r) * 0.5;
                singles.Add(new PatchCandidate(
                    "rgba8",
                    off,
                    FileOffsetToVa(inspection, off),
                    4,
                    ToHex(bytes.AsSpan(off, 4)),
                    "",
                    $"rgba8 R={r} G={g} B={b} A={a}",
                    score));
            }

            return singles;
        }

        static bool TryPatchFloatVec4(byte[] bytes, int off, Ps3MagicColorTransform t, out byte[] before, out byte[] after)
        {
            before = bytes.AsSpan(off, 16).ToArray();
            float r = BitConverter.ToSingle(bytes, off);
            float g = BitConverter.ToSingle(bytes, off + 4);
            float b = BitConverter.ToSingle(bytes, off + 8);
            float a = BitConverter.ToSingle(bytes, off + 12);
            r = Clamp01((float)(r * t.RedScale));
            g = Clamp01((float)(g * t.GreenScale));
            b = Clamp01((float)(b * t.BlueScale));
            a = Clamp01((float)(a * t.AlphaScale));
            BitConverter.TryWriteBytes(bytes.AsSpan(off), r);
            BitConverter.TryWriteBytes(bytes.AsSpan(off + 4), g);
            BitConverter.TryWriteBytes(bytes.AsSpan(off + 8), b);
            BitConverter.TryWriteBytes(bytes.AsSpan(off + 12), a);
            after = bytes.AsSpan(off, 16).ToArray();
            return !before.AsSpan().SequenceEqual(after);
        }

        static bool TryPatchRgba8(byte[] bytes, int off, int len, Ps3MagicColorTransform t, out byte[] before, out byte[] after)
        {
            before = bytes.AsSpan(off, len).ToArray();
            for (int i = 0; i + 4 <= len; i += 4)
            {
                byte r = bytes[off + i];
                byte g = bytes[off + i + 1];
                byte b = bytes[off + i + 2];
                byte a = bytes[off + i + 3];
                bytes[off + i] = ToByte(r * t.RedScale);
                bytes[off + i + 1] = ToByte(g * t.GreenScale);
                bytes[off + i + 2] = ToByte(b * t.BlueScale);
                bytes[off + i + 3] = ToByte(a * t.AlphaScale);
            }

            after = bytes.AsSpan(off, len).ToArray();
            return !before.AsSpan().SequenceEqual(after);
        }

        static bool TryPatchInt16Color15(byte[] bytes, int off, Ps3MagicColorTransform t, out byte[] before, out byte[] after)
        {
            before = bytes.AsSpan(off, 8).ToArray();
            short r = BitConverter.ToInt16(bytes, off);
            short g = BitConverter.ToInt16(bytes, off + 2);
            short b = BitConverter.ToInt16(bytes, off + 4);
            short a = BitConverter.ToInt16(bytes, off + 6);
            r = (short)Math.Clamp((int)Math.Round(r * t.RedScale), 0, 15);
            g = (short)Math.Clamp((int)Math.Round(g * t.GreenScale), 0, 15);
            b = (short)Math.Clamp((int)Math.Round(b * t.BlueScale), 0, 15);
            a = (short)Math.Clamp((int)Math.Round(a * t.AlphaScale), 0, 15);
            BitConverter.TryWriteBytes(bytes.AsSpan(off), r);
            BitConverter.TryWriteBytes(bytes.AsSpan(off + 2), g);
            BitConverter.TryWriteBytes(bytes.AsSpan(off + 4), b);
            BitConverter.TryWriteBytes(bytes.AsSpan(off + 6), a);
            after = bytes.AsSpan(off, 8).ToArray();
            return !before.AsSpan().SequenceEqual(after);
        }

        static bool IsPlausibleColorComponent(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v is >= -0.05f and <= 1.6f;

        static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);

        static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

        static uint FileOffsetToVa(MagicDllInspection inspection, int fileOffset)
        {
            try
            {
                return (uint)(MagicDllKeThResParser.ImageBase + inspection.FileOffsetToRva(fileOffset));
            }
            catch
            {
                return 0;
            }
        }

        static bool TryParseVa(string text, out uint va)
        {
            va = 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out va);
        }

        static string ToHex(ReadOnlySpan<byte> span) =>
            Convert.ToHexString(span).ToLowerInvariant();

        /// <summary>RT2 2026-06-14: cyan vec4 strip in dataA (+0x58C..+0x620); +0x350 was PE strings — not color.</summary>
        static bool IsAllowedPatchCandidate(PatchCandidate c, RegionSlice? dataA)
        {
            if (dataA == null)
                return false;
            if (c.Kind != "float_vec4")
                return false;
            int rel = c.FileOffset - dataA.FileOffset;
            return rel is >= MagicDllKeThResRelocAnalyzer.CyanVec4RelStart
                and <= MagicDllKeThResRelocAnalyzer.CyanVec4RelEnd;
        }

        static bool IsProtectedPatchOffset(byte[] bytes, int off)
        {
            if (off < 0 || off + 4 > bytes.Length)
                return true;
            // PPP opcode catalog + inline names (magic_0094 PE layout).
            if (off is >= 0x8294 and < 0x8700)
                return true;
            uint u = BitConverter.ToUInt32(bytes, off);
            if (u == MagicDllKeThResParser.PppEntryMagic)
                return true;
            return IsPrintableAsciiFour(bytes, off);
        }

        static bool IsPrintableAsciiFour(byte[] bytes, int off)
        {
            int printable = 0;
            for (int i = 0; i < 4 && off + i < bytes.Length; i++)
            {
                byte b = bytes[off + i];
                if (b is >= 0x20 and <= 0x7E)
                    printable++;
            }

            return printable >= 3;
        }

        static string Sha256Hex(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
