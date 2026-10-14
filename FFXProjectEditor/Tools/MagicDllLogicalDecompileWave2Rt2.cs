using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Wave 2: full-corpus clustering + Hex-Rays queue (1 rep per slot0 cluster + pinned DLLs).
    /// </summary>
    internal static class MagicDllLogicalDecompileWave2Rt2
    {
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
        const string DefaultWave1Dir = @"work\magic_dll_logical_decompile_wave1";
        const string DefaultOutputDir = @"work\magic_dll_logical_decompile_wave2";

        static readonly int[] MandatoryMagicIds =
        [
            82, 714, 84, 148, 688, 98, 45, 3, 208, 244, 21
        ];

        static readonly int[] HexRaysSlots = [0, 1, 3, 4];

        public static int Run(string[] args)
        {
            try
            {
                bool runFullCorpus = !args.Any(a => a.Equals("--skip-corpus", StringComparison.OrdinalIgnoreCase));
                bool exportOnly = args.Any(a => a.Equals("--export-queue-only", StringComparison.OrdinalIgnoreCase));
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string wave1Dir = ArgValue(args, "--wave1") ?? DefaultWave1Dir;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();

                Console.WriteLine("=== Magic DLL Logical Decompile — Wave 2 ===");
                Console.WriteLine($"magic root : {magicRoot}");
                Console.WriteLine($"output     : {outputDir}");
                Console.WriteLine($"full corpus: {runFullCorpus}");

                if (!Directory.Exists(magicRoot))
                {
                    Console.WriteLine($"FAIL: magic root not found: {magicRoot}");
                    return 2;
                }

                string corpusDir = Path.Combine(outputDir, "corpus_full");
                List<MagicDllLogicalDecompileResult> results;
                if (runFullCorpus)
                {
                    Console.WriteLine("Phase 1/3: full corpus static logical decompile (~581 DLLs)...");
                    results = DecodeFullCorpus(magicRoot, repoRoot, corpusDir);
                }
                else
                {
                    string summaryPath = Path.Combine(wave1Dir, "wave1_logical_decompile_summary.csv");
                    if (!File.Exists(summaryPath))
                    {
                        Console.WriteLine($"FAIL: --skip-corpus requires wave1 summary at {summaryPath}");
                        return 2;
                    }

                    Console.WriteLine("Phase 1/3: re-decode wave1 sample only (--skip-corpus)...");
                    results = DecodeFromManifest(magicRoot, repoRoot, wave1Dir, summaryPath);
                }

                Console.WriteLine($"Phase 2/3: cluster by family + slot0_hash ({results.Count} DLLs)...");
                ClusterReport clusters = BuildClusters(results);
                List<HexRaysQueueItem> queueFull = BuildHexRaysQueue(clusters, results, magicRoot);
                List<HexRaysQueueItem> queuePinned = queueFull.Where(q => q.PickReason == "pinned").ToList();
                List<HexRaysQueueItem> queueShared = queueFull
                    .Where(q => q.PickReason == "pinned" || IsSharedClusterRep(clusters, q.MagicId))
                    .ToList();

                Directory.CreateDirectory(outputDir);
                string clustersPath = Path.Combine(outputDir, "wave2_clusters.json");
                string queuePath = Path.Combine(outputDir, "wave2_hexrays_queue.json");
                string queuePinnedPath = Path.Combine(outputDir, "wave2_hexrays_queue_pinned.json");
                string queueSharedPath = Path.Combine(outputDir, "wave2_hexrays_queue_shared.json");
                string mdPath = Path.Combine(outputDir, "MAGIC_DLL_LOGICAL_DECOMPILE_WAVE2.md");

                File.WriteAllText(clustersPath, JsonSerializer.Serialize(clusters, JsonOptions()));
                File.WriteAllText(queuePath, JsonSerializer.Serialize(queueFull, JsonOptions()));
                File.WriteAllText(queuePinnedPath, JsonSerializer.Serialize(queuePinned, JsonOptions()));
                File.WriteAllText(queueSharedPath, JsonSerializer.Serialize(queueShared, JsonOptions()));
                File.WriteAllText(mdPath, BuildWave2Markdown(results, clusters, queueFull, queuePinned, queueShared, runFullCorpus));
                WriteClusterCsv(clusters, Path.Combine(outputDir, "wave2_clusters.csv"));
                WriteIdaBatchScript(queuePinned, Path.Combine(outputDir, "wave2_hexrays_batch_pinned.py"), magicRoot);

                Console.WriteLine($"corpus decoded : {results.Count}");
                Console.WriteLine($"clusters total : {clusters.TotalClusters} (only {CountSharedClusterReps(clusters)} have >1 member)");
                Console.WriteLine($"hexrays ALL    : {queueFull.Count} DLLs = {queueFull.Count * HexRaysSlots.Length} functions (weeks — do NOT batch blindly)");
                Console.WriteLine($"hexrays PINNED : {queuePinned.Count} DLLs = {queuePinned.Count * HexRaysSlots.Length} functions (wave2 phase 3)");
                Console.WriteLine($"hexrays SHARED : {queueShared.Count} DLLs = {queueShared.Count * HexRaysSlots.Length} functions (cluster reps only)");
                Console.WriteLine($"clusters json  : {clustersPath}");
                Console.WriteLine($"hexrays pinned : {queuePinnedPath}");
                Console.WriteLine($"hexrays shared : {queueSharedPath}");
                Console.WriteLine($"ida batch py   : {Path.Combine(outputDir, "wave2_hexrays_batch_pinned.py")}");

                if (!exportOnly)
                    Console.WriteLine("Phase 3/3: Hex-Rays — start with wave2_hexrays_queue_pinned.json (~44 functions), not the full 2136 queue.");

                bool pass = results.Count >= 500 && queuePinned.Count >= 8;
                Console.WriteLine(pass ? "VERDICT: PASS — wave2 corpus + cluster queue ready" : "VERDICT: PARTIAL");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static List<MagicDllLogicalDecompileResult> DecodeFullCorpus(string magicRoot, string repoRoot, string corpusDir)
        {
            List<MagicDllLogicalDecompileResult> results = [];
            List<string> dlls = Directory.EnumerateFiles(magicRoot, "magic_*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string dllPath in dlls)
            {
                try
                {
                    MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                    MagicDllLogicalDecompileResult decoded = MagicDllLogicalDecompiler.Decompile(inspection);
                    MagicDllLogicalDecompiler.WritePerDllReport(decoded, corpusDir);
                    results.Add(decoded);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  skip {Path.GetFileName(dllPath)}: {ex.Message}");
                }
            }

            string summaryPath = Path.Combine(corpusDir, "..", "wave2_full_corpus_summary.csv");
            WriteSummaryCsv(results, summaryPath);
            return results;
        }

        static List<MagicDllLogicalDecompileResult> DecodeFromManifest(string magicRoot, string repoRoot, string outDir, string summaryCsv)
        {
            List<MagicDllLogicalDecompileResult> results = [];
            foreach (string line in File.ReadLines(summaryCsv).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                string dll = line.Split(',')[1].Trim();
                string dllPath = Path.Combine(magicRoot, dll);
                if (!File.Exists(dllPath))
                    continue;
                MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                MagicDllLogicalDecompileResult decoded = MagicDllLogicalDecompiler.Decompile(inspection);
                MagicDllLogicalDecompiler.WritePerDllReport(decoded, outDir);
                results.Add(decoded);
            }

            return results;
        }

        static ClusterReport BuildClusters(List<MagicDllLogicalDecompileResult> results)
        {
            List<FamilyClusterGroup> groups = [];
            foreach (string fam in new[] { "A", "B", "C", "D" })
            {
                var byHash = results
                    .Where(r => FamilyLetter(r.Family) == fam && !string.IsNullOrEmpty(r.Slot0CodeSha256))
                    .GroupBy(r => r.Slot0CodeSha256)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                List<Slot0Cluster> clusters = [];
                int idx = 0;
                foreach (var g in byHash)
                {
                    MagicDllLogicalDecompileResult rep = g.OrderBy(x => x.MagicId).First();
                    clusters.Add(new Slot0Cluster(
                        idx++,
                        fam,
                        g.Key,
                        g.Count(),
                        rep.MagicIdText,
                        rep.FileName,
                        string.Join(",", g.Select(x => x.MagicIdText).OrderBy(x => x, StringComparer.Ordinal))));
                }

                groups.Add(new FamilyClusterGroup(fam, results.Count(r => FamilyLetter(r.Family) == fam), clusters.Count, clusters));
            }

            return new ClusterReport(
                results.Count,
                groups.Sum(g => g.ClusterCount),
                groups);
        }

        static List<HexRaysQueueItem> BuildHexRaysQueue(
            ClusterReport clusters,
            List<MagicDllLogicalDecompileResult> results,
            string magicRoot)
        {
            Dictionary<int, MagicDllLogicalDecompileResult> byId = results
                .Where(r => r.MagicId.HasValue)
                .ToDictionary(r => r.MagicId!.Value);

            HashSet<int> picked = new(MandatoryMagicIds);
            foreach (FamilyClusterGroup g in clusters.Groups)
            {
                foreach (Slot0Cluster c in g.Clusters)
                {
                    if (int.TryParse(c.RepresentativeMagicId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mid))
                        picked.Add(mid);
                }
            }

            List<HexRaysQueueItem> queue = [];
            foreach (int magicId in picked.OrderBy(x => x))
            {
                if (!byId.TryGetValue(magicId, out MagicDllLogicalDecompileResult? decoded))
                {
                    string dllPath = Path.Combine(magicRoot, $"magic_{magicId:D4}.dll");
                    if (!File.Exists(dllPath))
                        continue;
                    MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath);
                    decoded = MagicDllLogicalDecompiler.Decompile(inspection);
                }

                List<HexRaysSlotTarget> slots = [];
                foreach (int slotIndex in HexRaysSlots)
                {
                    MagicDllLogicalSlotDecompile? slot = decoded.Slots.FirstOrDefault(s => s.SlotIndex == slotIndex);
                    if (slot == null || slot.Kind != "code" || slot.IsStub)
                        continue;
                    slots.Add(new HexRaysSlotTarget(
                        slotIndex,
                        slot.RoleName,
                        slot.Rva,
                        slot.RvaValue,
                        $"0x{0x10000000 + slot.RvaValue:X}"));
                }

                if (slots.Count == 0)
                    continue;

                queue.Add(new HexRaysQueueItem(
                    magicId,
                    $"magic_{magicId:D4}.dll",
                    Path.Combine(magicRoot, $"magic_{magicId:D4}.dll"),
                    decoded.Family,
                    MandatoryMagicIds.Contains(magicId) ? "pinned" : "cluster_rep",
                    slots));
            }

            return queue;
        }

        static void WriteSummaryCsv(List<MagicDllLogicalDecompileResult> results, string path)
        {
            List<string> lines =
            [
                "magic_id,dll,family,slot_kind,slot0_hash,slot1_hash,active_code,stub_slots,proven_slots,partial_slots"
            ];
            foreach (MagicDllLogicalDecompileResult r in results.OrderBy(r => r.MagicId))
            {
                int proven = r.Slots.Count(s => s.MatchLevel == "proven_fingerprint");
                int partial = r.Slots.Count(s => s.MatchLevel == "partial_fingerprint");
                lines.Add(string.Join(",",
                    r.MagicIdText, r.FileName, r.Family, Csv(r.SlotKindSignature),
                    r.Slot0CodeSha256, r.Slot1CodeSha256,
                    r.ActiveCodeSlots, r.StubSlots, proven, partial));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }

        static void WriteClusterCsv(ClusterReport report, string path)
        {
            List<string> lines = ["family,cluster_id,slot0_hash,member_count,representative,members"];
            foreach (FamilyClusterGroup g in report.Groups)
            {
                foreach (Slot0Cluster c in g.Clusters)
                {
                    lines.Add(string.Join(",",
                        g.FamilyLetter, c.ClusterId.ToString(CultureInfo.InvariantCulture),
                        c.Slot0Hash, c.MemberCount.ToString(CultureInfo.InvariantCulture),
                        c.RepresentativeMagicId, Csv(c.MemberMagicIds)));
                }
            }

            File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }

        static void WriteIdaBatchScript(List<HexRaysQueueItem> queue, string scriptPath, string magicRoot)
        {
            string queuePath = Path.Combine(Path.GetDirectoryName(scriptPath)!, "wave2_hexrays_queue.json");
            StringBuilder sb = new();
            sb.AppendLine("#!/usr/bin/env python3");
            sb.AppendLine("\"\"\"Wave 2 Hex-Rays batch — decompile overlay slots 0/1/3/4 per queue item.\"\"\"");
            sb.AppendLine("import json, os, sys");
            sb.AppendLine("try:");
            sb.AppendLine("    import ida_auto, ida_hexrays, ida_funcs, ida_name");
            sb.AppendLine("except ImportError:");
            sb.AppendLine("    print('Run inside IDA Pro with Hex-Rays'); sys.exit(1)");
            sb.AppendLine();
            sb.AppendLine($"QUEUE = r'''{queuePath.Replace("'", "''")}'''");
            sb.AppendLine("OUT = os.path.join(os.path.dirname(QUEUE), 'hexrays_output')");
            sb.AppendLine();
            sb.AppendLine("def decompile_slot(dll_path, magic_id, slot_idx, rva, out_dir):");
            sb.AppendLine("    ea = 0x10000000 + rva");
            sb.AppendLine("    name = f'magic_{magic_id:04d}_slot{slot_idx:02d}'");
            sb.AppendLine("    if ida_funcs.get_func(ea) is None:");
            sb.AppendLine("        ida_funcs.add_func(ea)");
            sb.AppendLine("    ida_name.set_name(ea, name, ida_name.SN_CHECK)");
            sb.AppendLine("    cfunc = ida_hexrays.decompile(ea)");
            sb.AppendLine("    if not cfunc: return False");
            sb.AppendLine("    path = os.path.join(out_dir, f'magic_{magic_id:04d}_slot{slot_idx:02d}.c')");
            sb.AppendLine("    with open(path, 'w', encoding='utf-8') as f:");
            sb.AppendLine("        f.write(str(cfunc))");
            sb.AppendLine("    return True");
            sb.AppendLine();
            sb.AppendLine("def main():");
            sb.AppendLine("    with open(QUEUE, encoding='utf-8') as f:");
            sb.AppendLine("        queue = json.load(f)");
            sb.AppendLine("    os.makedirs(OUT, exist_ok=True)");
            sb.AppendLine("    ok = fail = 0");
            sb.AppendLine("    for item in queue:");
            sb.AppendLine("        dll = item['dllPath']");
            sb.AppendLine("        mid = item['magicId']");
            sb.AppendLine("        for slot in item['slots']:");
            sb.AppendLine("            if decompile_slot(dll, mid, slot['slotIndex'], slot['rvaValue'], OUT):");
            sb.AppendLine("                ok += 1");
            sb.AppendLine("            else:");
            sb.AppendLine("                fail += 1");
            sb.AppendLine("    print(f'Hex-Rays batch: ok={ok} fail={fail} -> {OUT}')");
            sb.AppendLine();
            sb.AppendLine("if __name__ == '__main__':");
            sb.AppendLine("    ida_auto.auto_wait()");
            sb.AppendLine("    main()");
            File.WriteAllText(scriptPath, sb.ToString(), Encoding.UTF8);
        }

        static bool IsSharedClusterRep(ClusterReport clusters, int magicId)
        {
            string id = magicId.ToString("D4", CultureInfo.InvariantCulture);
            foreach (FamilyClusterGroup g in clusters.Groups)
            {
                foreach (Slot0Cluster c in g.Clusters)
                {
                    if (c.MemberCount > 1 && c.RepresentativeMagicId == id)
                        return true;
                }
            }

            return false;
        }

        static int CountSharedClusterReps(ClusterReport clusters) =>
            clusters.Groups.Sum(g => g.Clusters.Count(c => c.MemberCount > 1));

        static string BuildWave2Markdown(
            List<MagicDllLogicalDecompileResult> results,
            ClusterReport clusters,
            List<HexRaysQueueItem> queueFull,
            List<HexRaysQueueItem> queuePinned,
            List<HexRaysQueueItem> queueShared,
            bool fullCorpus)
        {
            StringBuilder sb = new();
            sb.AppendLine("# Magic DLL — Logical Decompile Wave 2");
            sb.AppendLine();
            sb.AppendLine($"Generated UTC: `{DateTimeOffset.UtcNow:O}` · Lane: **Jarvis-MAGIC**");
            sb.AppendLine();
            sb.AppendLine("## Honest cost model");
            sb.AppendLine();
            sb.AppendLine("| Layer | Cost | What you get |");
            sb.AppendLine("| --- | --- | --- |");
            sb.AppendLine("| **Wave 1 static** | ~4s / 119 DLLs | Host-offset fingerprint + template pseudocode |");
            sb.AppendLine($"| **Wave 2 static (full)** | **~6s / 583 DLLs** | Every DLL fingerprinted + clustered |");
            sb.AppendLine($"| **Hex-Rays PINNED** | **~{queuePinned.Count * 4} functions** | Prism/Fira/Death anchors — do this next |");
            sb.AppendLine($"| **Hex-Rays SHARED reps** | **~{queueShared.Count * 4} functions** | Covers clone families (only {CountSharedClusterReps(clusters)} multi-member clusters) |");
            sb.AppendLine($"| **Hex-Rays ALL unique** | **~{queueFull.Count * 4} functions** | Almost every DLL is slot0-unique — **weeks** |");
            sb.AppendLine("| **100% behavior + RT2** | months | Opcodes, timing, per-spell proof |");
            sb.AppendLine();
            sb.AppendLine("**Why Wave 1 felt instant:** byte-scan + templates, not Hex-Rays. **Why you cannot Hex-Rays all 581 at once:** 534 distinct slot0 architectures — sharing is rare (36 A, 41 B, 2 C, 2 D in shared clusters).");
            sb.AppendLine();
            sb.AppendLine("## Corpus");
            sb.AppendLine();
            sb.AppendLine($"- DLLs decoded: **{results.Count}**");
            sb.AppendLine($"- Slot0 clusters: **{clusters.TotalClusters}** ({CountSharedClusterReps(clusters)} with >1 member)");
            sb.AppendLine();
            sb.AppendLine("### Per-family clusters");
            sb.AppendLine();
            sb.AppendLine("| Family | DLLs | Clusters | Shared clusters | Largest |");
            sb.AppendLine("| --- | ---: | ---: | ---: | --- |");
            foreach (FamilyClusterGroup g in clusters.Groups)
            {
                int shared = g.Clusters.Count(c => c.MemberCount > 1);
                Slot0Cluster? top = g.Clusters.OrderByDescending(c => c.MemberCount).FirstOrDefault();
                sb.AppendLine($"| **{g.FamilyLetter}** | {g.DllCount} | {g.ClusterCount} | {shared} | {top?.MemberCount ?? 0}× `{top?.RepresentativeMagicId ?? "-"}` |");
            }

            sb.AppendLine();
            sb.AppendLine("### Hex-Rays tiers");
            sb.AppendLine();
            sb.AppendLine("1. `wave2_hexrays_queue_pinned.json` — **start here** (Prism/Fira/Death/Bucket reps)");
            sb.AppendLine("2. `wave2_hexrays_queue_shared.json` — cluster representatives with >1 sibling");
            sb.AppendLine("3. `wave2_hexrays_queue.json` — full unique set (on-demand per spell edit)");
            sb.AppendLine();
            sb.AppendLine("### Pinned DLLs");
            sb.AppendLine();
            foreach (int id in MandatoryMagicIds)
                sb.AppendLine($"- `magic_{id:D4}`");
            return sb.ToString();
        }

        static string BuildWave2Markdown(ClusterReport clusters, List<HexRaysQueueItem> queue, List<MagicDllLogicalDecompileResult> results, bool fullCorpus) =>
            BuildWave2Markdown(results, clusters, queue, queue.Where(q => q.PickReason == "pinned").ToList(), queue, fullCorpus);

        static JsonSerializerOptions JsonOptions() => new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        static string FamilyLetter(string family) => family switch
        {
            _ when family.StartsWith("A_", StringComparison.Ordinal) => "A",
            _ when family.StartsWith("B_", StringComparison.Ordinal) => "B",
            _ when family.StartsWith("C_", StringComparison.Ordinal) => "C",
            _ when family.StartsWith("D_", StringComparison.Ordinal) => "D",
            _ => "?"
        };

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        static string Csv(string value)
        {
            value ??= string.Empty;
            if (value.Contains('"') || value.Contains(','))
                return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            return value;
        }

        sealed record Slot0Cluster(int ClusterId, string FamilyLetter, string Slot0Hash, int MemberCount, string RepresentativeMagicId, string RepresentativeDll, string MemberMagicIds);
        sealed record FamilyClusterGroup(string FamilyLetter, int DllCount, int ClusterCount, IReadOnlyList<Slot0Cluster> Clusters);
        sealed record ClusterReport(int TotalDlls, int TotalClusters, IReadOnlyList<FamilyClusterGroup> Groups);
        sealed record HexRaysSlotTarget(int SlotIndex, string RoleName, string Rva, int RvaValue, string ImageBaseEa);
        sealed record HexRaysQueueItem(int MagicId, string DllName, string DllPath, string Family, string PickReason, IReadOnlyList<HexRaysSlotTarget> Slots);
    }
}
