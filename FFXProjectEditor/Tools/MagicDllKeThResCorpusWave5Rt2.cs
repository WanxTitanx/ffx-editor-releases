using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Wave 5: offline KeThRes / Family D corpus — scan all <c>magic_*.dll</c>, diff <c>ppp_dataA</c> vs
    /// <c>magic_0094</c> baseline, reloc tags, vec4 color outliers. No FFX runtime.
    /// </summary>
    internal static class MagicDllKeThResCorpusWave5Rt2
    {
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
        const string DefaultWave2Dir = @"work\magic_dll_logical_decompile_wave2";
        const int BaselineMagicId = 94;
        const int DataAScanSize = 0x1000;

        public static int Run(string[] args)
        {
            try
            {
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                string wave2Dir = Path.IsPathRooted(ArgValue(args, "--wave2") ?? DefaultWave2Dir)
                    ? ArgValue(args, "--wave2")!
                    : Path.Combine(repoRoot, ArgValue(args, "--wave2") ?? DefaultWave2Dir);
                string wave5Dir = Path.Combine(wave2Dir, "wave5");
                int baselineId = int.Parse(ArgValue(args, "--baseline") ?? BaselineMagicId.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

                Console.WriteLine("=== Magic DLL KeThRes Corpus — Wave 5 (inferno offline) ===");
                Console.WriteLine($"magic root : {magicRoot}");
                Console.WriteLine($"output     : {wave5Dir}");
                Console.WriteLine($"baseline   : magic_{baselineId:D4}");

                if (!Directory.Exists(magicRoot))
                {
                    Console.WriteLine($"FAIL: magic root not found: {magicRoot}");
                    return 2;
                }

                string[] dlls = Directory.GetFiles(magicRoot, "magic_*.dll", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (dlls.Length == 0)
                {
                    Console.WriteLine("FAIL: no magic_*.dll files");
                    return 3;
                }

                string baselinePath = Path.Combine(magicRoot, $"magic_{baselineId:D4}.dll");
                if (!File.Exists(baselinePath))
                {
                    Console.WriteLine($"FAIL: baseline DLL missing: {baselinePath}");
                    return 4;
                }

                BaselineSnapshot baseline = BuildBaseline(baselinePath, repoRoot, baselineId);
                var rows = new List<CorpusRow>(dlls.Length);
                int n = 0;
                foreach (string dllPath in dlls)
                {
                    n++;
                    if (n % 50 == 0)
                        Console.WriteLine($"  scan {n}/{dlls.Length}...");
                    rows.Add(ScanDll(dllPath, repoRoot, baseline));
                }

                Directory.CreateDirectory(wave5Dir);
                var summary = BuildSummary(rows, baseline);
                WriteJson(wave5Dir, "wave5_kethres_corpus.json", rows);
                WriteJson(wave5Dir, "wave5_summary.json", summary);
                WriteJson(wave5Dir, "wave5_family_d_ids.json", rows.Where(r => r.Family == "D_EgoTasklist").Select(r => r.MagicId).ToList());
                WriteJson(wave5Dir, "wave5_kethres_dll_ids.json", rows.Where(r => r.HasKeThRes).Select(r => r.MagicId).ToList());
                WriteJson(wave5Dir, "wave5_dataa_diff_vs_baseline.json",
                    rows.Where(r => r.DataADiffBytes > 0).OrderByDescending(r => r.DataADiffBytes).ToList());
                WriteJson(wave5Dir, "wave5_vec4_outliers.json",
                    rows.Where(r => r.Vec4Outliers.Count > 0).ToList());
                WriteJson(wave5Dir, "wave5_layout_match_baseline.json",
                    rows.Where(r => r.SameKeThResLayoutAsBaseline).Select(r => r.MagicId).ToList());
                File.WriteAllText(Path.Combine(wave5Dir, "WAVE5_KETHRES_CORPUS.md"), BuildMarkdown(rows, summary, baseline), new UTF8Encoding(false));

                Console.WriteLine($"DLLs scanned       : {rows.Count}");
                Console.WriteLine($"Family D           : {summary.FamilyDCount}");
                Console.WriteLine($"Has KeThRes        : {summary.KeThResCount}");
                Console.WriteLine($"Same layout as {baselineId:D4} : {summary.SameLayoutCount}");
                Console.WriteLine($"dataA diff > 0     : {summary.DataADiffCount}");
                Console.WriteLine($"vec4 outliers      : {summary.Vec4OutlierDllCount}");
                Console.WriteLine($"output             : {wave5Dir}");
                Console.WriteLine("VERDICT: PASS — wave5 KeThRes corpus ready");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        sealed record BaselineSnapshot(
            int MagicId,
            byte[] DataASlice,
            string DataASha256,
            string? PrimaryDataARva,
            uint? HandlerARva,
            int RelocTagCount);

        sealed record Vec4Outlier(int DataARel, float R, float G, float B, float A, string Note);

        sealed record CorpusRow(
            int MagicId,
            string Family,
            string Sha256,
            bool HasKeThRes,
            int KeThResResourceCount,
            int PppOpcodeCount,
            string? PrimaryDataARva,
            string? PrimaryDataBRva,
            string? HandlerARva,
            int DataAFileOffset,
            int DataALength,
            string? DataASha256,
            int DataADiffBytes,
            int CyanStripDiffBytes,
            int RelocTagCount,
            bool SameKeThResLayoutAsBaseline,
            bool ParseOk,
            string? ParseError,
            IReadOnlyList<Vec4Outlier> Vec4Outliers);

        sealed record CorpusSummary(
            int TotalDlls,
            int FamilyDCount,
            int KeThResCount,
            int SameLayoutCount,
            int DataADiffCount,
            int Vec4OutlierDllCount,
            int BaselineMagicId,
            IReadOnlyList<int> TopDiffMagicIds);

        static BaselineSnapshot BuildBaseline(string dllPath, string repoRoot, int magicId)
        {
            MagicDllInspection ins = MagicDllDecompiler.Inspect(dllPath, repoRoot);
            byte[] bytes = File.ReadAllBytes(dllPath);
            TryExtractDataA(ins, bytes, out byte[]? slice, out int fileOff, out int len, out string? dataARva, out string? handlerA, out int relocTags, out _);
            return new BaselineSnapshot(
                magicId,
                slice ?? Array.Empty<byte>(),
                slice != null ? Sha256Hex(slice) : "",
                dataARva,
                ParseRva(handlerA),
                relocTags);
        }

        static CorpusRow ScanDll(string dllPath, string repoRoot, BaselineSnapshot baseline)
        {
            MagicDllInspection ins = MagicDllDecompiler.Inspect(dllPath, repoRoot);
            int magicId = ins.MagicId ?? 0;
            MagicDllFamilyClassification fam = MagicDllFamilyClassifier.Classify(ins);
            string family = fam.Family.ToString();
            bool hasKeThRes = ins.Strings.Any(s => s.Value.Contains("pppKeThRes", StringComparison.OrdinalIgnoreCase));
            byte[] bytes = File.ReadAllBytes(dllPath);

            int keCount = 0, opcodeCount = 0;
            string? dataARva = null, dataBRva = null, handlerA = null;
            int dataAOff = 0, dataALen = 0, relocTags = 0;
            byte[]? dataASlice = null;
            string? parseError = null;
            bool parseOk = false;
            var vec4Outliers = new List<Vec4Outlier>();

            try
            {
                var parse = MagicDllKeThResParser.Parse(ins, ps3MagicRoot: null, maxPhyreMatches: 2);
                opcodeCount = parse.Opcodes.Count;
                keCount = parse.KeThResResources.Count;
                hasKeThRes |= keCount > 0;
                if (parse.PrimaryBoltResource != null)
                {
                    dataARva = parse.PrimaryBoltResource.Entry.DataRvaAHex;
                    dataBRva = parse.PrimaryBoltResource.Entry.DataRvaBHex;
                    handlerA = parse.PrimaryBoltResource.Entry.HandlerAHex;
                }

                parseOk = true;
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }

            TryExtractDataA(ins, bytes, out dataASlice, out dataAOff, out dataALen, out _, out handlerA, out relocTags, out vec4Outliers);

            int diffBytes = 0, cyanDiffs = 0;
            if (dataASlice != null && baseline.DataASlice.Length > 0)
            {
                int len = Math.Min(dataASlice.Length, baseline.DataASlice.Length);
                for (int i = 0; i < len; i++)
                {
                    if (dataASlice[i] == baseline.DataASlice[i])
                        continue;
                    diffBytes++;
                    if (i is >= MagicDllKeThResRelocAnalyzer.CyanVec4RelStart and <= MagicDllKeThResRelocAnalyzer.CyanVec4RelEnd)
                        cyanDiffs++;
                }
            }

            bool sameLayout = hasKeThRes
                && string.Equals(dataARva, baseline.PrimaryDataARva, StringComparison.OrdinalIgnoreCase)
                && ParseRva(handlerA) == baseline.HandlerARva
                && diffBytes == 0;

            return new CorpusRow(
                magicId,
                family,
                ins.Sha256,
                hasKeThRes,
                keCount,
                opcodeCount,
                dataARva,
                dataBRva,
                handlerA,
                dataAOff,
                dataALen,
                dataASlice != null ? Sha256Hex(dataASlice) : null,
                diffBytes,
                cyanDiffs,
                relocTags,
                sameLayout,
                parseOk,
                parseError,
                vec4Outliers);
        }

        static void TryExtractDataA(
            MagicDllInspection ins,
            byte[] bytes,
            out byte[]? slice,
            out int fileOffset,
            out int length,
            out string? dataARva,
            out string? handlerA,
            out int relocTagCount,
            out List<Vec4Outlier> vec4Outliers)
        {
            slice = null;
            fileOffset = 0;
            length = 0;
            dataARva = null;
            handlerA = null;
            relocTagCount = 0;
            vec4Outliers = new List<Vec4Outlier>();

            try
            {
                MagicDllKeThResPppPatch.AnalyzeResult a = MagicDllKeThResPppPatch.Analyze(ins, includeDataSection: false);
                if (a.DataA == null)
                    return;

                fileOffset = a.DataA.FileOffset;
                length = Math.Min(a.DataA.Length, DataAScanSize);
                if (fileOffset < 0 || fileOffset >= bytes.Length)
                    return;

                length = Math.Min(length, bytes.Length - fileOffset);
                slice = new byte[length];
                Array.Copy(bytes, fileOffset, slice, 0, length);
                dataARva = $"0x{a.DataA.ImageVa:X}";

                var parse = MagicDllKeThResParser.Parse(ins, ps3MagicRoot: null, maxPhyreMatches: 1);
                if (parse.PrimaryBoltResource != null)
                    handlerA = parse.PrimaryBoltResource.Entry.HandlerAHex;

                MagicDllKeThResRelocAnalyzer.AnalyzeResult reloc = MagicDllKeThResRelocAnalyzer.Analyze(ins);
                relocTagCount = reloc.Tags.Count;
                vec4Outliers = FindVec4Outliers(slice, reloc);
            }
            catch
            {
                // not Family D PPP
            }
        }

        static List<Vec4Outlier> FindVec4Outliers(byte[] dataA, MagicDllKeThResRelocAnalyzer.AnalyzeResult reloc)
        {
            var hits = new List<Vec4Outlier>();
            var relocRefs = new HashSet<int>(
                reloc.Tags.Where(t => t.DataARelOffset.HasValue).Select(t => t.DataARelOffset!.Value));

            // Thundaga baseline cyan-ish reference (post-reloc strip)
            const float refR = 1.0f, refG = 0.3f, refB = 0.5f;

            for (int off = 0; off + 16 <= dataA.Length; off += 4)
            {
                float r = BitConverter.ToSingle(dataA, off);
                float g = BitConverter.ToSingle(dataA, off + 4);
                float b = BitConverter.ToSingle(dataA, off + 8);
                float a = BitConverter.ToSingle(dataA, off + 12);
                if (!LooksLikeColorVec4(r, g, b, a))
                    continue;

                bool relocReferenced = relocRefs.Contains(off);
                float delta = Math.Abs(r - refR) + Math.Abs(g - refG) + Math.Abs(b - refB);
                if (delta < 0.15f && a >= 0.5f && a <= 1.2f)
                    continue; // same as Thundaga default

                string note = relocReferenced ? "reloc_ref" : "unreferenced";
                if (off is >= MagicDllKeThResRelocAnalyzer.CyanVec4RelStart and <= MagicDllKeThResRelocAnalyzer.CyanVec4RelEnd)
                    note += ";cyan_strip";

                hits.Add(new Vec4Outlier(off, r, g, b, a, note));
                if (hits.Count >= 12)
                    break;
            }

            return hits;
        }

        static bool LooksLikeColorVec4(float r, float g, float b, float a) =>
            r is >= -0.05f and <= 1.6f
            && g is >= -0.05f and <= 1.6f
            && b is >= -0.05f and <= 1.6f
            && a is >= -0.05f and <= 1.6f
            && (r + g + b) > 0.05f;

        static uint? ParseRva(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return null;
            string s = hex.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s[2..];
            return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v : null;
        }

        static CorpusSummary BuildSummary(IReadOnlyList<CorpusRow> rows, BaselineSnapshot baseline) =>
            new(
                rows.Count,
                rows.Count(r => r.Family == "D_EgoTasklist"),
                rows.Count(r => r.HasKeThRes),
                rows.Count(r => r.SameKeThResLayoutAsBaseline),
                rows.Count(r => r.DataADiffBytes > 0),
                rows.Count(r => r.Vec4Outliers.Count > 0),
                baseline.MagicId,
                rows.Where(r => r.DataADiffBytes > 0).OrderByDescending(r => r.DataADiffBytes).Take(32).Select(r => r.MagicId).ToList());

        static string BuildMarkdown(IReadOnlyList<CorpusRow> rows, CorpusSummary s, BaselineSnapshot baseline)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Magic DLL Wave 5 — KeThRes corpus (offline)");
            sb.AppendLine();
            sb.AppendLine($"**Baseline:** `magic_{baseline.MagicId:D4}` dataA sha `{baseline.DataASha256[..16]}…`");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|--------|------:|");
            sb.AppendLine($"| DLLs scanned | {s.TotalDlls} |");
            sb.AppendLine($"| Family D | {s.FamilyDCount} |");
            sb.AppendLine($"| Has KeThRes | {s.KeThResCount} |");
            sb.AppendLine($"| Same layout as baseline | {s.SameLayoutCount} |");
            sb.AppendLine($"| dataA byte diff > 0 | {s.DataADiffCount} |");
            sb.AppendLine($"| vec4 outlier DLLs | {s.Vec4OutlierDllCount} |");
            sb.AppendLine();
            sb.AppendLine("## Top dataA diffs vs baseline");
            sb.AppendLine();
            sb.AppendLine("| magic_id | family | diff_bytes | cyan_strip_diff | vec4_hits | reloc_tags |");
            sb.AppendLine("|----------|--------|------------|-----------------|----------:|-----------:|");
            foreach (CorpusRow r in rows.Where(x => x.DataADiffBytes > 0).OrderByDescending(x => x.DataADiffBytes).Take(40))
                sb.AppendLine($"| {r.MagicId:D4} | {r.Family} | {r.DataADiffBytes} | {r.CyanStripDiffBytes} | {r.Vec4Outliers.Count} | {r.RelocTagCount} |");

            sb.AppendLine();
            sb.AppendLine("## Vec4 outliers (non-Thundaga-default)");
            sb.AppendLine();
            foreach (CorpusRow r in rows.Where(x => x.Vec4Outliers.Count > 0).Take(25))
            {
                sb.AppendLine($"### magic_{r.MagicId:D4}");
                foreach (Vec4Outlier v in r.Vec4Outliers.Take(6))
                    sb.AppendLine($"- `@+0x{v.DataARel:X}` ({v.Note}): `{v.R:F3},{v.G:F3},{v.B:F3},{v.A:F3}`");
            }

            return sb.ToString();
        }

        static string Sha256Hex(byte[] data)
        {
            byte[] hash = SHA256.HashData(data);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        static void WriteJson<T>(string dir, string name, T value) =>
            File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(value, JsonOpts), new UTF8Encoding(false));

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
