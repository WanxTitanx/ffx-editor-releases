using FFXProjectEditor.FfxLib.Ai;
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
    /// Wave 4 deep corpus: unlinked DLL taxonomy, data/phyre inventory, kernel row expansion,
    /// slot4 phase patterns, overlay-missing edge cases.
    /// </summary>
    internal static class MagicDllDeepCorpusWave4Rt2
    {
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
        const string DefaultWave2Dir = @"work\magic_dll_logical_decompile_wave2";
        const string DefaultPs3Root = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string DefaultFfxExe = @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\FFX.exe";

        public static int Run(string[] args)
        {
            try
            {
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string wave2Dir = ArgValue(args, "--wave2") ?? DefaultWave2Dir;
                string ps3Root = ArgValue(args, "--ps3-root") ?? DefaultPs3Root;
                string ffxExe = ArgValue(args, "--ffx-exe") ?? DefaultFfxExe;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                wave2Dir = Path.IsPathRooted(wave2Dir) ? wave2Dir : Path.Combine(repoRoot, wave2Dir);
                string wave4Dir = Path.Combine(wave2Dir, "wave4");
                string hexraysDir = Path.Combine(wave2Dir, "hexrays_output");

                Console.WriteLine("=== Magic DLL Deep Corpus — Wave 4 ===");
                Console.WriteLine($"magic root : {magicRoot}");
                Console.WriteLine($"wave2 dir  : {wave2Dir}");
                Console.WriteLine($"ffx.exe    : {ffxExe} ({(File.Exists(ffxExe) ? "ok" : "missing")})");

                Dictionary<int, SummaryRow> summary = LoadSummary(Path.Combine(wave2Dir, "wave2_full_corpus_summary.csv"));
                Dictionary<int, List<CatalogUsage>> catalogIndex = BuildFullCatalogIndex();
                List<DataSlotInventoryRow> dataInventory = BuildDataSlotInventory(magicRoot, repoRoot, summary);
                List<KernelRowExpansion> kernelRows = BuildKernelExpansions(catalogIndex);
                Slot4PhaseReport slot4 = AnalyzeSlot4PhasePatterns(hexraysDir);
                VmExeAnchorDoc vmAnchors = BuildVmExeAnchors(ffxExe);

                Directory.CreateDirectory(wave4Dir);
                WriteJson(wave4Dir, "wave4_data_phyre_inventory.json", dataInventory);
                WriteJson(wave4Dir, "wave4_kernel_row_expansion.json", kernelRows);
                WriteJson(wave4Dir, "wave4_slot4_phase_patterns.json", slot4);
                WriteJson(wave4Dir, "wave4_vm_exe_anchors.json", vmAnchors);
                WriteMarkdown(wave4Dir, dataInventory, kernelRows, slot4, vmAnchors, catalogIndex.Count);

                Console.WriteLine($"data/phyre rows   : {dataInventory.Count}");
                Console.WriteLine($"kernel expansions : {kernelRows.Count}");
                Console.WriteLine($"slot4 phase files : {slot4.FilesWithPhaseGate}");
                Console.WriteLine($"catalog anim ids  : {catalogIndex.Count}");
                Console.WriteLine($"output            : {wave4Dir}");
                Console.WriteLine("Regenerating attribution taxonomy (orphan catalog)...");
                int orphanRc = MagicDllOrphanCatalogRt2.Run(args);
                if (orphanRc != 0)
                    return orphanRc;
                Console.WriteLine("VERDICT: PASS - wave4 deep corpus artifacts ready");
                Console.WriteLine("NEXT: run scripts/magic_dll_hexrays_host_profile.py for per-DLL API matrix");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        static void WriteJson<T>(string dir, string name, T value) =>
            File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(value, JsonOpts), new UTF8Encoding(false));

        static Dictionary<int, List<CatalogUsage>> BuildFullCatalogIndex()
        {
            Dictionary<int, List<CatalogUsage>> index = new();
            foreach (AiCommandMetadataEntry entry in AiCommandMetadataCatalog.Entries)
            {
                IReadOnlyList<int> animIds = MagicDllMoveAnimParser.EnumerateMagicIds(entry.RawProperties);
                if (animIds.Count == 0)
                    continue;
                int a1 = animIds[0];
                int a2 = animIds.Count > 1 ? animIds[1] : animIds[0];
                CatalogUsage usage = new(
                    entry.DisplayName,
                    entry.SourceFile,
                    entry.Operand,
                    a1,
                    a2,
                    entry.Power,
                    entry.HitCount,
                    entry.ElementText,
                    entry.RawProperties);
                Add(index, a1, usage);
                foreach (int extraId in animIds.Skip(1))
                {
                    if (extraId != a1)
                        Add(index, extraId, usage);
                }
            }

            return index;
        }

        static void Add(Dictionary<int, List<CatalogUsage>> index, int id, CatalogUsage usage)
        {
            if (!index.TryGetValue(id, out List<CatalogUsage>? list))
            {
                list = [];
                index[id] = list;
            }

            if (!list.Any(u => u.Operand == usage.Operand && u.SourceFile == usage.SourceFile))
                list.Add(usage);
        }

        static List<DataSlotInventoryRow> BuildDataSlotInventory(
            string magicRoot,
            string repoRoot,
            Dictionary<int, SummaryRow> summary)
        {
            List<DataSlotInventoryRow> rows = [];
            foreach (SummaryRow row in summary.Values.OrderBy(r => r.MagicId))
            {
                string dllPath = Path.Combine(magicRoot, $"magic_{row.MagicId:D4}.dll");
                if (!File.Exists(dllPath))
                    continue;
                MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                MagicDllLogicalDecompileResult decoded = MagicDllLogicalDecompiler.Decompile(inspection);
                int dataSlots = decoded.Slots.Count(s => s.Kind.Equals("data", StringComparison.OrdinalIgnoreCase));
                int codeSlots = decoded.Slots.Count(s => s.Kind.Equals("code", StringComparison.OrdinalIgnoreCase));
                int nullSlots = decoded.Slots.Count(s => s.Kind.Equals("null", StringComparison.OrdinalIgnoreCase));
                string[] ppp = decoded.EngineStrings
                    .Where(s => s.StartsWith("ppp", StringComparison.OrdinalIgnoreCase))
                    .Take(12)
                    .ToArray();
                rows.Add(new DataSlotInventoryRow(
                    row.MagicId,
                    row.Family,
                    row.SlotKind,
                    codeSlots,
                    dataSlots,
                    nullSlots,
                    ppp,
                    decoded.EngineStrings.Any(s => s.Contains("Phyre", StringComparison.OrdinalIgnoreCase)),
                    ppp.Any(s => s.Contains("Accele", StringComparison.OrdinalIgnoreCase)),
                    ppp.Any(s => s.Contains("Color", StringComparison.OrdinalIgnoreCase) || s.Contains("Col", StringComparison.OrdinalIgnoreCase)),
                    EditingGuidanceForFamily(row.Family)));
            }

            return rows;
        }

        static string EditingGuidanceForFamily(string family) => family switch
        {
            _ when family.StartsWith("A_", StringComparison.Ordinal) => "Scale/color floats in DLL; slot4 phase gate RT2",
            _ when family.StartsWith("B_", StringComparison.Ordinal) => "VM timeline + PS3; avoid blind DLL vec4",
            _ when family.StartsWith("C_", StringComparison.Ordinal) => "PS3 textures preferred; bulk vec4 crashes",
            _ when family.StartsWith("D_", StringComparison.Ordinal) => "Ego tasklist; PS3 + scalar candidates only",
            _ => "RT2 required",
        };

        static List<KernelRowExpansion> BuildKernelExpansions(Dictionary<int, List<CatalogUsage>> catalog)
        {
            List<KernelRowExpansion> rows = [];
            foreach (KeyValuePair<int, List<CatalogUsage>> kv in catalog.OrderBy(k => k.Key))
            {
                foreach (CatalogUsage u in kv.Value.Take(4))
                {
                    rows.Add(new KernelRowExpansion(
                        kv.Key,
                        u.DisplayName,
                        u.SourceFile,
                        u.Operand,
                        u.Anim1,
                        u.Anim2,
                        u.Power,
                        u.HitCount,
                        u.ElementText,
                        ExtractRawSnippet(u.RawProperties, 280)));
                }
            }

            return rows;
        }

        static Slot4PhaseReport AnalyzeSlot4PhasePatterns(string hexraysDir)
        {
            int total = 0;
            int withPhase = 0;
            int withInterpreter = 0;
            List<string> samples = [];
            if (!Directory.Exists(hexraysDir))
                return new Slot4PhaseReport(0, 0, 0, samples);

            foreach (string file in Directory.EnumerateFiles(hexraysDir, "magic_*_slot04.c"))
            {
                total++;
                string text = File.ReadAllText(file);
                bool phase = text.Contains("phaseGate", StringComparison.Ordinal)
                    || text.Contains("host+900", StringComparison.Ordinal)
                    || text.Contains("host+904", StringComparison.Ordinal);
                bool interp = text.Contains("runPhaseRecordInterpreter", StringComparison.Ordinal);
                if (phase)
                    withPhase++;
                if (interp)
                    withInterpreter++;
                if (phase && samples.Count < 8)
                    samples.Add(Path.GetFileName(file));
            }

            return new Slot4PhaseReport(total, withPhase, withInterpreter, samples);
        }

        static VmExeAnchorDoc BuildVmExeAnchors(string ffxExe) =>
            new(
                File.Exists(ffxExe) ? ffxExe : "(missing)",
                File.Exists(ffxExe) ? new FileInfo(ffxExe).Length : 0,
                [
                    new("0x80CD60", "FFX_Magic_RunRuntimeRootPhase_structural", "Core VM interpreter — host+2864 in DLLs"),
                    new("0x80BEA0", "FFX_Magic_RunAuxRuntimeRootPass_structural", "Side-pass — host+2884"),
                    new("0x817200", "FFX_Magic_MaterializeRuntimeRoot_structural", "Root materialize — host+2860"),
                    new("0xC48EC8", "g_FFX_MagicOpcodeTable_core", "256 core opcode handlers"),
                    new("0xC492C8", "g_FFX_MagicPostProcTable", "12 post-proc handlers"),
                    new("0xC48E78", "g_FFX_MagicSidePassTable", "20 side-pass handlers"),
                ],
                "Overlay opcodes (>=0x100) dispatch via DLL root+80→+124 table; correlate with wave4_host_api_profiles.json usesVmInterpreter=true",
                "docs/reverse/FFX_MAGIC_TIMELINE_VM_OPCODE_TABLES_2026-06-14.md");

        static void WriteMarkdown(
            string wave4Dir,
            List<DataSlotInventoryRow> data,
            List<KernelRowExpansion> kernel,
            Slot4PhaseReport slot4,
            VmExeAnchorDoc vm,
            int catalogIds)
        {
            StringBuilder sb = new();
            sb.AppendLine("# Magic DLL Deep Corpus — Wave 4");
            sb.AppendLine();
            sb.AppendLine($"- Catalog anim IDs: **{catalogIds}**");
            sb.AppendLine($"- Attribution taxonomy: see `wave4_unlinked_dll_taxonomy.json` (orphan catalog)");
            sb.AppendLine($"- Data/PPP inventory rows: **{data.Count}**");
            sb.AppendLine($"- Kernel row expansions: **{kernel.Count}**");
            sb.AppendLine($"- slot04 files: **{slot4.TotalSlot4Files}** ({slot4.FilesWithPhaseGate} with phaseGate pattern)");
            sb.AppendLine($"- FFX.exe: `{vm.ExePath}`");
            sb.AppendLine();
            sb.AppendLine("## RT2 queue (human)");
            sb.AppendLine("- Family C/D clones: PS3 texture path only");
            sb.AppendLine("- Family A: slot4 phase + draw tick patches");
            sb.AppendLine("- Family B: confirm VM phase in-game after kernel row edit");
            sb.AppendLine();
            sb.AppendLine("See JSON artifacts in this folder.");
            File.WriteAllText(Path.Combine(wave4Dir, "WAVE4_DEEP_CORPUS.md"), sb.ToString(), Encoding.UTF8);
        }

        static Dictionary<int, SummaryRow> LoadSummary(string path)
        {
            Dictionary<int, SummaryRow> map = new();
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                string[] p = line.Split(',');
                if (p.Length < 10)
                    continue;
                int mid = int.Parse(p[0], CultureInfo.InvariantCulture);
                map[mid] = new SummaryRow(mid, p[2], p[3]);
            }

            return map;
        }

        static string ExtractRawSnippet(string raw, int max) =>
            raw.Length <= max ? raw : raw[..max] + "…";

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        sealed record SummaryRow(int MagicId, string Family, string SlotKind);

        sealed record CatalogUsage(
            string DisplayName,
            string SourceFile,
            ushort Operand,
            int Anim1,
            int Anim2,
            int? Power,
            int? HitCount,
            string ElementText,
            string RawProperties);

        sealed record DataSlotInventoryRow(
            int MagicId,
            string Family,
            string SlotKindSignature,
            int CodeSlots,
            int DataSlots,
            int NullSlots,
            string[] PppStrings,
            bool MentionsPhyre,
            bool HasAccelString,
            bool HasColorString,
            string EditingGuidance);

        sealed record KernelRowExpansion(
            int MagicId,
            string DisplayName,
            string SourceFile,
            ushort Operand,
            int Anim1,
            int Anim2,
            int? Power,
            int? HitCount,
            string ElementText,
            string RawSnippet);

        sealed record Slot4PhaseReport(int TotalSlot4Files, int FilesWithPhaseGate, int FilesWithVmInterpreter, IReadOnlyList<string> SampleFiles);

        sealed record VmExeAnchor(string Address, string Symbol, string Notes);

        sealed record VmExeAnchorDoc(
            string ExePath,
            long ExeSizeBytes,
            IReadOnlyList<VmExeAnchor> Anchors,
            string CorrelationNote,
            string SourceDoc);
    }
}
