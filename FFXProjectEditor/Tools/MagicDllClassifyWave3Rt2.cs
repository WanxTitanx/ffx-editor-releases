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
    /// Wave 3 classification: sibling registry, sub-family fingerprints, spell linkage,
    /// clone-overlay-missing (714/715), VM bridge, secondary Hex-Rays queue (slots 2,5,6,7+).
    /// </summary>
    internal static class MagicDllClassifyWave3Rt2
    {
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
        const string DefaultWave2Dir = @"work\magic_dll_logical_decompile_wave2";
        const string DefaultPs3Root = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";

        static readonly int[] PrimaryHexRaysSlots = [0, 1, 3, 4];
        static readonly int[] CloneOverlayMissingIds = [714, 715];

        public static int Run(string[] args)
        {
            try
            {
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string wave2Dir = ArgValue(args, "--wave2") ?? DefaultWave2Dir;
                string ps3Root = ArgValue(args, "--ps3-root") ?? DefaultPs3Root;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                wave2Dir = Path.IsPathRooted(wave2Dir) ? wave2Dir : Path.Combine(repoRoot, wave2Dir);

                Console.WriteLine("=== Magic DLL Classify — Wave 3 ===");
                Console.WriteLine($"magic root : {magicRoot}");
                Console.WriteLine($"wave2 dir  : {wave2Dir}");
                Console.WriteLine($"ps3 root   : {ps3Root}");

                if (!Directory.Exists(magicRoot))
                {
                    Console.WriteLine($"FAIL: magic root not found: {magicRoot}");
                    return 2;
                }

                string clustersPath = Path.Combine(wave2Dir, "wave2_clusters.json");
                string summaryPath = Path.Combine(wave2Dir, "wave2_full_corpus_summary.csv");
                string queuePath = Path.Combine(wave2Dir, "wave2_hexrays_queue.json");
                if (!File.Exists(clustersPath) || !File.Exists(summaryPath))
                {
                    Console.WriteLine("FAIL: run --magicdll-logical-decompile-wave2 first");
                    return 2;
                }

                Dictionary<int, SummaryRow> summary = LoadSummary(summaryPath);
                List<SiblingEntry> siblings = BuildSiblingRegistry(clustersPath, summary);
                List<SubfamilyCluster> subfamilies = BuildSubfamilyClusters(summary, siblings);
                List<SpellLinkageEntry> linkage = BuildSpellLinkage(summary.Keys, magicRoot, ps3Root);
                List<CloneOverlayMissingEntry> clones = BuildCloneOverlayMissing(magicRoot, repoRoot, ps3Root);
                VmDllBridgeDoc vmBridge = BuildVmBridge();
                List<HexRaysQueueItemWave3> secondaryQueue = BuildSecondaryHexRaysQueue(
                    queuePath, magicRoot, repoRoot, summary);

                string outDir = wave2Dir;
                string siblingPath = Path.Combine(outDir, "wave3_sibling_registry.json");
                string subfamilyPath = Path.Combine(outDir, "wave3_subfamily_clusters.json");
                string linkagePath = Path.Combine(outDir, "wave3_spell_linkage.json");
                string clonePath = Path.Combine(outDir, "wave3_clone_overlay_missing.json");
                string vmPath = Path.Combine(outDir, "wave3_vm_dll_bridge.json");
                string secondaryPath = Path.Combine(outDir, "wave3_hexrays_queue_secondary.json");

                File.WriteAllText(siblingPath, JsonSerializer.Serialize(siblings, JsonOpts), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(subfamilyPath, JsonSerializer.Serialize(subfamilies, JsonOpts), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(linkagePath, JsonSerializer.Serialize(linkage, JsonOpts), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(clonePath, JsonSerializer.Serialize(clones, JsonOpts), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(vmPath, JsonSerializer.Serialize(vmBridge, JsonOpts), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(secondaryPath, JsonSerializer.Serialize(secondaryQueue, JsonOpts), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                WriteWave3Markdown(outDir, siblings, subfamilies, linkage, clones, vmBridge, secondaryQueue);

                Console.WriteLine($"siblings      : {siblings.Count} entries -> {siblingPath}");
                Console.WriteLine($"subfamilies   : {subfamilies.Count} clusters -> {subfamilyPath}");
                Console.WriteLine($"spell linkage : {linkage.Count} DLLs -> {linkagePath}");
                Console.WriteLine($"clone missing : {clones.Count} -> {clonePath}");
                Console.WriteLine($"vm bridge     : {vmPath}");
                Console.WriteLine($"secondary q   : {secondaryQueue.Count} DLLs, {secondaryQueue.Sum(q => q.Slots.Count)} slots -> {secondaryPath}");
                Console.WriteLine("VERDICT: PASS - wave3 classification artifacts ready");
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

        static List<SiblingEntry> BuildSiblingRegistry(string clustersPath, Dictionary<int, SummaryRow> summary)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(clustersPath));
            List<SiblingEntry> entries = [];
            foreach (JsonElement group in doc.RootElement.GetProperty("groups").EnumerateArray())
            {
                string fam = group.GetProperty("familyLetter").GetString() ?? "?";
                foreach (JsonElement c in group.GetProperty("clusters").EnumerateArray())
                {
                    int clusterId = c.GetProperty("clusterId").GetInt32();
                    string repText = c.GetProperty("representativeMagicId").GetString() ?? "0";
                    int rep = int.Parse(repText, CultureInfo.InvariantCulture);
                    string membersCsv = c.GetProperty("memberMagicIds").GetString() ?? repText;
                    int[] members = membersCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => int.Parse(s, CultureInfo.InvariantCulture))
                        .OrderBy(x => x)
                        .ToArray();
                    string slot0 = c.GetProperty("slot0Hash").GetString() ?? string.Empty;

                    foreach (int mid in members)
                    {
                        summary.TryGetValue(mid, out SummaryRow? row);
                        entries.Add(new SiblingEntry(
                            mid,
                            rep,
                            fam,
                            clusterId,
                            slot0,
                            members.Length,
                            mid == rep,
                            members,
                            row?.Family ?? string.Empty,
                            row?.Slot0Hash ?? string.Empty,
                            row?.Slot1Hash ?? string.Empty));
                    }
                }
            }

            return entries.OrderBy(e => e.MagicId).ToList();
        }

        static List<SubfamilyCluster> BuildSubfamilyClusters(
            Dictionary<int, SummaryRow> summary,
            List<SiblingEntry> siblings)
        {
            var groups = summary.Values
                .Where(r => !string.IsNullOrEmpty(r.Slot0Hash))
                .GroupBy(r => $"{FamilyLetter(r.Family)}|{r.Slot0Hash}|{r.Slot1Hash}")
                .OrderByDescending(g => g.Count());

            List<SubfamilyCluster> clusters = [];
            int id = 0;
            foreach (var g in groups)
            {
                SummaryRow rep = g.OrderBy(r => r.MagicId).First();
                string[] parts = g.Key.Split('|');
                string fam = parts[0];
                string hostSig = BuildHostSignature(rep.MagicId, siblings);
                clusters.Add(new SubfamilyCluster(
                    id++,
                    fam,
                    rep.Slot0Hash,
                    rep.Slot1Hash,
                    g.Key,
                    g.Count(),
                    rep.MagicId,
                    g.Select(r => r.MagicId).OrderBy(x => x).ToArray(),
                    ClassifySubfamilyLabel(fam, rep, hostSig),
                    hostSig));
            }

            return clusters;
        }

        static string BuildHostSignature(int magicId, List<SiblingEntry> siblings)
        {
            SiblingEntry? s = siblings.FirstOrDefault(x => x.MagicId == magicId);
            return s?.FamilyEnum ?? string.Empty;
        }

        static string ClassifySubfamilyLabel(string fam, SummaryRow rep, string hostSig)
        {
            int proven = rep.ProvenSlots;
            int active = rep.ActiveCodeSlots;
            return fam switch
            {
                "A" when proven >= 3 => "A_draw_tick_heavy",
                "A" => "A_particle_light",
                "B" when active >= 5 => "B_interpreter_full",
                "B" => "B_root_materialize",
                "C" => "C_ego_ppp_params",
                "D" => "D_tasklist_fsm",
                _ => "unknown_subfamily"
            };
        }

        static List<SpellLinkageEntry> BuildSpellLinkage(IEnumerable<int> magicIds, string magicRoot, string ps3Root)
        {
            List<SpellLinkageEntry> list = [];
            foreach (int mid in magicIds.OrderBy(x => x))
            {
                IReadOnlyList<MagicDllFamilySpellUsage> spells = MagicDllFamilyComparator.GetSpellsUsingMagicId(mid);
                string dllPath = Path.Combine(magicRoot, $"magic_{mid:D4}.dll");
                string dllHash = File.Exists(dllPath) ? Sha256Prefix(dllPath) : string.Empty;
                string ps3Folder = Path.Combine(ps3Root, $"magic_{mid:D4}");
                bool ps3Exists = Directory.Exists(ps3Folder);
                int textureCount = 0;
                string? sampleTextureHash = null;
                if (ps3Exists)
                {
                    List<string> textures = Directory.EnumerateFiles(ps3Folder, "*.dds.phyre", SearchOption.AllDirectories)
                        .Take(8)
                        .ToList();
                    textureCount = Directory.EnumerateFiles(ps3Folder, "*.dds.phyre", SearchOption.AllDirectories).Count();
                    if (textures.Count > 0)
                        sampleTextureHash = Sha256Prefix(textures[0]);
                }

                list.Add(new SpellLinkageEntry(
                    mid,
                    spells.Select(s => new SpellLinkRow(
                        s.DisplayName,
                        s.SourceFile,
                        s.OperandHex,
                        s.Anim1,
                        s.Anim2,
                        s.MoveAnimSummary,
                        s.GameplaySummary)).ToList(),
                    dllHash,
                    ps3Exists,
                    ps3Folder,
                    textureCount,
                    sampleTextureHash));
            }

            return list;
        }

        static List<CloneOverlayMissingEntry> BuildCloneOverlayMissing(string magicRoot, string repoRoot, string ps3Root)
        {
            List<CloneOverlayMissingEntry> list = [];
            foreach (int mid in CloneOverlayMissingIds)
            {
                string dllPath = Path.Combine(magicRoot, $"magic_{mid:D4}.dll");
                MagicDllLogicalDecompileResult? decoded = null;
                if (File.Exists(dllPath))
                {
                    MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                    decoded = MagicDllLogicalDecompiler.Decompile(inspection);
                }

                IReadOnlyList<MagicDllFamilySpellUsage> spells = MagicDllFamilyComparator.GetSpellsUsingMagicId(mid);
                string ps3Folder = Path.Combine(ps3Root, $"magic_{mid:D4}");
                bool hasPppStrings = decoded?.EngineStrings.Any(s =>
                    s.Contains("ppp", StringComparison.OrdinalIgnoreCase)) ?? false;

                list.Add(new CloneOverlayMissingEntry(
                    mid,
                    "CloneOverlayMissing",
                    "C_like_strings",
                    decoded?.Family ?? "Unknown",
                    decoded?.SlotKindSignature ?? string.Empty,
                    decoded?.ActiveCodeSlots ?? 0,
                    decoded?.EngineStrings.Take(16).ToArray() ?? [],
                    hasPppStrings,
                    spells.Select(s => s.DisplayName).Distinct().Take(8).ToArray(),
                    Directory.Exists(ps3Folder),
                    File.Exists(dllPath) ? Sha256Prefix(dllPath) : string.Empty,
                    "Visual edit via PS3 textures only; overlay CSV row missing — do not bulk vec4 patch Family C/D clones"));
            }

            return list;
        }

        static VmDllBridgeDoc BuildVmBridge() =>
            new(
                "FFX.exe magic VM ↔ magic DLL overlay bridge",
                "docs/reverse/FFX_MAGIC_VM_OPCODE_CLUSTERS_2026-06-14.md",
                [
                    new VmBridgeLink(
                        "B_RootRecordInterpreter",
                        "host+2864(runPhaseRecordInterpreter)",
                        "sub_80CD60",
                        "VM CLUSTER 1-6 — timeline opcode dispatch",
                        "Overlay slots 3/4 call interpreter; record lifecycle + branch/transform/matrix clusters"),
                    new VmBridgeLink(
                        "B_RootRecordInterpreter",
                        "host+2884(runPhase1SidePass)",
                        "sub_80BEA0",
                        "VM side-pass cluster",
                        "Phase-1 auxiliary pass before/after main interpreter"),
                    new VmBridgeLink(
                        "B_RootRecordInterpreter",
                        "host+2860(materializeRuntimeRoot)",
                        "sub_817200",
                        "VM CLUSTER 5 — cursor/root write",
                        "Slot 0 materializes 1MB root consumed by VM"),
                    new VmBridgeLink(
                        "A_ParticleSelfContained",
                        "host+1000(drawOrSubmit)",
                        "sub_7EB* draw path",
                        "VM CLUSTER 6 — DRAW/SUBMIT",
                        "Self-contained pool; no full interpreter — direct host draw API"),
                    new VmBridgeLink(
                        "C_RootSelfGovernedParam",
                        "host+2844(parseEgoPppResource)",
                        "PPP string bootstrap",
                        "VM-adjacent — ego params, not full B interpreter",
                        "pppAccele/pppColor strings; patch via PS3 or scalar candidates only"),
                ]);

        static List<HexRaysQueueItemWave3> BuildSecondaryHexRaysQueue(
            string primaryQueuePath,
            string magicRoot,
            string repoRoot,
            Dictionary<int, SummaryRow> summary)
        {
            if (!File.Exists(primaryQueuePath))
                return [];

            List<PrimaryQueueRow> primary = JsonSerializer.Deserialize<List<PrimaryQueueRow>>(
                File.ReadAllText(primaryQueuePath), JsonOpts) ?? [];
            List<HexRaysQueueItemWave3> queue = [];

            foreach (PrimaryQueueRow item in primary)
            {
                int magicId = item.MagicId;
                string dllPath = Path.Combine(magicRoot, item.DllName);
                if (!File.Exists(dllPath))
                    continue;

                MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                MagicDllLogicalDecompileResult decoded = MagicDllLogicalDecompiler.Decompile(inspection);
                List<HexRaysSlotTargetWave3> slots = [];

                foreach (MagicDllLogicalSlotDecompile slot in decoded.Slots)
                {
                    if (PrimaryHexRaysSlots.Contains(slot.SlotIndex))
                        continue;
                    if (!string.Equals(slot.Kind, "code", StringComparison.OrdinalIgnoreCase) || slot.IsStub)
                        continue;
                    if (slot.RvaValue <= 0)
                        continue;

                    slots.Add(new HexRaysSlotTargetWave3(
                        slot.SlotIndex,
                        slot.RoleName,
                        slot.Rva,
                        slot.RvaValue,
                        $"0x{0x10000000 + slot.RvaValue:X}"));
                }

                if (slots.Count == 0)
                    continue;

                summary.TryGetValue(magicId, out SummaryRow? row);
                queue.Add(new HexRaysQueueItemWave3(
                    magicId,
                    item.DllName,
                    dllPath,
                    decoded.Family,
                    "secondary_slots",
                    row?.Slot0Hash ?? string.Empty,
                    slots.OrderBy(s => s.SlotIndex).ToList()));
            }

            return queue.OrderBy(q => q.MagicId).ToList();
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
                map[mid] = new SummaryRow(
                    mid,
                    p[1],
                    p[2],
                    p[3],
                    p[4],
                    p[5],
                    int.Parse(p[6], CultureInfo.InvariantCulture),
                    int.Parse(p[7], CultureInfo.InvariantCulture),
                    int.Parse(p[8], CultureInfo.InvariantCulture),
                    int.Parse(p[9], CultureInfo.InvariantCulture));
            }

            return map;
        }

        static void WriteWave3Markdown(
            string outDir,
            List<SiblingEntry> siblings,
            List<SubfamilyCluster> subfamilies,
            List<SpellLinkageEntry> linkage,
            List<CloneOverlayMissingEntry> clones,
            VmDllBridgeDoc vm,
            List<HexRaysQueueItemWave3> secondary)
        {
            int twinCount = siblings.Count(s => !s.IsClusterRep);
            int linkageWithSpells = linkage.Count(l => l.Spells.Count > 0);
            int linkageWithPs3 = linkage.Count(l => l.Ps3FolderExists);
            int secSlots = secondary.Sum(q => q.Slots.Count);

            StringBuilder sb = new();
            sb.AppendLine("# Magic DLL Wave 3 Classification");
            sb.AppendLine();
            sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC · Lane Jarvis-MAGIC");
            sb.AppendLine();
            sb.AppendLine("| Artifact | Count |");
            sb.AppendLine("|----------|------:|");
            sb.AppendLine($"| Sibling registry entries | {siblings.Count} |");
            sb.AppendLine($"| Non-rep cluster siblings | {twinCount} |");
            sb.AppendLine($"| Sub-family clusters (slot0+slot1) | {subfamilies.Count} |");
            sb.AppendLine($"| Spell linkage rows | {linkage.Count} |");
            sb.AppendLine($"| DLLs with command row | {linkageWithSpells} |");
            sb.AppendLine($"| DLLs with PS3 folder | {linkageWithPs3} |");
            sb.AppendLine($"| Clone overlay missing | {clones.Count} |");
            sb.AppendLine($"| Secondary Hex-Rays DLLs | {secondary.Count} |");
            sb.AppendLine($"| Secondary Hex-Rays slots | {secSlots} |");
            sb.AppendLine();
            sb.AppendLine("## Commands");
            sb.AppendLine();
            sb.AppendLine("```powershell");
            sb.AppendLine("FFXProjectEditor --magicdll-classify-wave3");
            sb.AppendLine("python scripts/magic_dll_hexrays_batch.py --tier secondary --remaining-only --resume");
            sb.AppendLine("```");

            File.WriteAllText(Path.Combine(outDir, "WAVE3_CLASSIFICATION.md"), sb.ToString(), Encoding.UTF8);
        }

        static string FamilyLetter(string familyEnum) => familyEnum switch
        {
            _ when familyEnum.StartsWith("A_", StringComparison.Ordinal) => "A",
            _ when familyEnum.StartsWith("B_", StringComparison.Ordinal) => "B",
            _ when familyEnum.StartsWith("C_", StringComparison.Ordinal) => "C",
            _ when familyEnum.StartsWith("D_", StringComparison.Ordinal) => "D",
            _ => "?"
        };

        static string Sha256Prefix(string path)
        {
            byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
            return Convert.ToHexString(hash).ToLowerInvariant()[..16];
        }

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        sealed record SummaryRow(
            int MagicId,
            string Dll,
            string Family,
            string SlotKind,
            string Slot0Hash,
            string Slot1Hash,
            int ActiveCodeSlots,
            int StubSlots,
            int ProvenSlots,
            int PartialSlots);

        sealed record SiblingEntry(
            int MagicId,
            int ClusterRepMagicId,
            string FamilyLetter,
            int ClusterId,
            string Slot0Hash,
            int ClusterMemberCount,
            bool IsClusterRep,
            int[] ClusterMemberIds,
            string FamilyEnum,
            string Slot0CodeHash,
            string Slot1CodeHash);

        sealed record SubfamilyCluster(
            int SubfamilyId,
            string FamilyLetter,
            string Slot0Hash,
            string Slot1Hash,
            string FingerprintKey,
            int MemberCount,
            int RepresentativeMagicId,
            int[] MemberMagicIds,
            string SubfamilyLabel,
            string Notes);

        sealed record SpellLinkRow(
            string DisplayName,
            string SourceFile,
            string OperandHex,
            int Anim1,
            int Anim2,
            string MoveAnimSummary,
            string GameplaySummary);

        sealed record SpellLinkageEntry(
            int MagicId,
            IReadOnlyList<SpellLinkRow> Spells,
            string DllSha256Prefix,
            bool Ps3FolderExists,
            string Ps3FolderPath,
            int Ps3TextureCount,
            string? SampleTextureSha256Prefix);

        sealed record CloneOverlayMissingEntry(
            int MagicId,
            string Classification,
            string StringFamilyHint,
            string LogicalFamily,
            string SlotKindSignature,
            int ActiveCodeSlots,
            string[] EngineStrings,
            bool HasPppStrings,
            string[] SampleSpellNames,
            bool Ps3FolderExists,
            string DllSha256Prefix,
            string EditingGuidance);

        sealed record VmBridgeLink(
            string DllFamily,
            string HostSlot,
            string ExeSymbol,
            string VmCluster,
            string Notes);

        sealed record VmDllBridgeDoc(
            string Title,
            string SourceDoc,
            IReadOnlyList<VmBridgeLink> Links);

        sealed record HexRaysSlotTargetWave3(
            int SlotIndex,
            string RoleName,
            string Rva,
            int RvaValue,
            string ImageBaseEa);

        sealed record HexRaysQueueItemWave3(
            int MagicId,
            string DllName,
            string DllPath,
            string Family,
            string PickReason,
            string Slot0Hash,
            IReadOnlyList<HexRaysSlotTargetWave3> Slots);

        sealed class PrimaryQueueRow
        {
            public int MagicId { get; set; }
            public string DllName { get; set; } = string.Empty;
            public string DllPath { get; set; } = string.Empty;
            public string Family { get; set; } = string.Empty;
            public string PickReason { get; set; } = string.Empty;
            public List<PrimarySlotRow> Slots { get; set; } = [];
        }

        sealed class PrimarySlotRow
        {
            public int SlotIndex { get; set; }
            public string RoleName { get; set; } = string.Empty;
            public string Rva { get; set; } = string.Empty;
            public int RvaValue { get; set; }
            public string ImageBaseEa { get; set; } = string.Empty;
        }
    }
}
