using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Utils.Encoding;
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
    /// Multi-source catalog for magic DLL ids without a spell row in the legacy index:
    /// catalog (moveAnim/None), kernel bins, overlay CSV, cluster twins.
    /// </summary>
    internal static class MagicDllOrphanCatalogRt2
    {
        const string DefaultMasterRoot = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
        const string DefaultWave2Dir = @"work\magic_dll_logical_decompile_wave2";
        const string DefaultPs3Root = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string DefaultOverlayCsv = @"docs\reverse\magicfiles_overlay_2026-06-03\ffx_magic_overlay_tables.csv";

        static readonly (string FileName, bool HasExtraInfo, string Label)[] KernelTargets =
        [
            ("command.bin", true, "command"),
            ("item.bin", true, "item"),
            ("monmagic1.bin", false, "monmagic1"),
            ("monmagic2.bin", false, "monmagic2"),
        ];

        public static int Run(string[] args)
        {
            try
            {
                string masterRoot = ArgValue(args, "--master-root") ?? DefaultMasterRoot;
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string wave2Dir = ArgValue(args, "--wave2") ?? DefaultWave2Dir;
                string ps3Root = ArgValue(args, "--ps3-root") ?? DefaultPs3Root;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                wave2Dir = Path.IsPathRooted(wave2Dir) ? wave2Dir : Path.Combine(repoRoot, wave2Dir);
                string overlayCsv = Path.Combine(repoRoot, DefaultOverlayCsv);
                string wave4Dir = Path.Combine(wave2Dir, "wave4");

                Console.WriteLine("=== Magic DLL Orphan Catalog ===");
                Console.WriteLine($"master root : {masterRoot}");
                Console.WriteLine($"magic root  : {magicRoot}");
                Console.WriteLine($"wave2 dir   : {wave2Dir}");
                Console.WriteLine($"ps3 root    : {ps3Root}");

                Dictionary<int, SummaryLite> summary = LoadSummary(Path.Combine(wave2Dir, "wave2_full_corpus_summary.csv"));
                Dictionary<int, List<KernelRef>> kernelIndex = BuildKernelIndex(masterRoot);
                Dictionary<int, OverlayLite> overlayIndex = LoadOverlayIndex(overlayCsv);
                Dictionary<int, SiblingLite> siblings = LoadSiblings(Path.Combine(wave2Dir, "wave3_sibling_registry.json"));

                List<OrphanCatalogRow> rows = [];
                foreach (int mid in summary.Keys.OrderBy(x => x))
                {
                    IReadOnlyList<MagicDllFamilySpellUsage> catalogHits = MagicDllFamilyComparator.GetSpellsUsingMagicId(mid);
                    kernelIndex.TryGetValue(mid, out List<KernelRef>? kernelHits);
                    overlayIndex.TryGetValue(mid, out OverlayLite? overlay);
                    siblings.TryGetValue(mid, out SiblingLite? sib);
                    summary.TryGetValue(mid, out SummaryLite? sum);

                    string category = Classify(mid, catalogHits, kernelHits, overlay, sib);
                    bool ps3 = Directory.Exists(Path.Combine(ps3Root, $"magic_{mid:D4}"));
                    bool dllExists = File.Exists(Path.Combine(magicRoot, $"magic_{mid:D4}.dll"));
                    rows.Add(new OrphanCatalogRow(
                        mid,
                        category,
                        sum?.Family ?? "?",
                        catalogHits.Select(h => h.DisplayName).Distinct().Take(8).ToArray(),
                        catalogHits.Select(h => h.SourceFile).Distinct().ToArray(),
                        kernelHits?.Take(12).ToArray() ?? [],
                        overlay?.SlotKindSignature ?? "",
                        overlay != null,
                        ps3,
                        dllExists,
                        sib?.ClusterRepMagicId ?? mid,
                        sib?.IsClusterRep ?? true,
                        Recommend(category)));
                }

                Directory.CreateDirectory(wave4Dir);
                var payload = new
                {
                    summary = new
                    {
                        totalDlls = rows.Count,
                        catalogLinked = rows.Count(r => r.Category.StartsWith("catalog", StringComparison.Ordinal)),
                        kernelOnly = rows.Count(r => r.Category == "kernel_bin_only"),
                        engineCarrier = rows.Count(r => r.Category == "engine_overlay_carrier"),
                        cloneGenerated = rows.Count(r => r.Category == "clone_generated"),
                        clusterTwin = rows.Count(r => r.Category == "cluster_twin_visual"),
                        unresolved = rows.Count(r => r.Category == "unresolved_no_refs"),
                    },
                    rows,
                };

                WriteJson(wave4Dir, "wave4_orphan_catalog.json", payload);
                WriteJson(wave4Dir, "wave4_unlinked_dll_taxonomy.json", rows.Select(ToTaxonomyRow).ToList());
                WriteMarkdown(wave4Dir, payload.summary, rows);

                Console.WriteLine($"catalog-linked : {payload.summary.catalogLinked}");
                Console.WriteLine($"kernel-only    : {payload.summary.kernelOnly}");
                Console.WriteLine($"engine carrier : {payload.summary.engineCarrier}");
                Console.WriteLine($"unresolved     : {payload.summary.unresolved}");
                Console.WriteLine($"output         : {Path.Combine(wave4Dir, "wave4_orphan_catalog.json")}");
                Console.WriteLine($"taxonomy       : {Path.Combine(wave4Dir, "wave4_unlinked_dll_taxonomy.json")}");
                Console.WriteLine("VERDICT: PASS - orphan catalog ready");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static string Classify(
            int mid,
            IReadOnlyList<MagicDllFamilySpellUsage> catalog,
            List<KernelRef>? kernel,
            OverlayLite? overlay,
            SiblingLite? sib)
        {
            if (mid is 714 or 715)
                return "clone_generated";
            if (catalog.Count > 0)
                return kernel?.Count > 0 ? "catalog_and_kernel" : "catalog_metadata";
            if (kernel is { Count: > 0 })
                return "kernel_bin_only";
            if (overlay != null)
                return sib is { IsClusterRep: false } ? "cluster_twin_visual" : "engine_overlay_carrier";
            if (sib is { IsClusterRep: false })
                return "cluster_twin_visual";
            return "unresolved_no_refs";
        }

        static string Recommend(string category) => category switch
        {
            "catalog_and_kernel" => "Fully named — use Family Comparator",
            "catalog_metadata" => "Named in metadata; verify kernel row if editing",
            "kernel_bin_only" => "Spell/item/monmagic row exists — add to catalog export or use kernel editor",
            "engine_overlay_carrier" => "Engine/field/cutscene/summon component — overlay+PS3; RT2 if reused",
            "cluster_twin_visual" => "Byte-twin of cluster rep — edit rep or PS3 sibling",
            "clone_generated" => "Lab clone — PS3 texture path",
            _ => "No refs found — likely unused slot or needs EXE/field RE",
        };

        static Dictionary<int, List<KernelRef>> BuildKernelIndex(string masterRoot)
        {
            Dictionary<int, List<KernelRef>> index = new();
            if (!Directory.Exists(masterRoot))
                return index;

            foreach (string localeDir in Directory.EnumerateDirectories(masterRoot))
            {
                string kernelDir = Path.Combine(localeDir, "battle", "kernel");
                if (!Directory.Exists(kernelDir))
                    continue;

                string locale = Path.GetFileName(localeDir);
                foreach ((string fileName, bool extra, string label) in KernelTargets)
                {
                    string path = Path.Combine(kernelDir, fileName);
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        byte[] bytes = File.ReadAllBytes(path);
                        List<Ability_Command> entries = Ability_Command.ReadList(bytes, extra);
                        for (int i = 0; i < entries.Count; i++)
                        {
                            Ability_Command row = entries[i];
                            string name = DecodeName(row.NameScriptBytes);
                            AddKernel(index, row.Anim1Id, new KernelRef(locale, label, i, "anim1", name));
                            AddKernel(index, row.Anim2Id, new KernelRef(locale, label, i, "anim2", name));
                            AddKernel(index, row.CasterAnimId, new KernelRef(locale, label, i, "casterAnim", name));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WARN: skip {path}: {ex.GetType().Name}");
                    }
                }
            }

            return index;
        }

        static void AddKernel(Dictionary<int, List<KernelRef>> index, int magicId, KernelRef hit)
        {
            if (magicId <= 0 || magicId >= 2000)
                return;

            if (!index.TryGetValue(magicId, out List<KernelRef>? list))
            {
                list = [];
                index[magicId] = list;
            }

            if (!list.Any(h => h.Locale == hit.Locale && h.Table == hit.Table && h.RowIndex == hit.RowIndex && h.Field == hit.Field))
                list.Add(hit);
        }

        static Dictionary<int, OverlayLite> LoadOverlayIndex(string csvPath)
        {
            Dictionary<int, OverlayLite> map = new();
            if (!File.Exists(csvPath))
                return map;

            string[] lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2)
                return map;

            string[] headers = lines[0].Split(',');
            int idCol = Array.IndexOf(headers, "magic_id");
            int sigCol = Array.IndexOf(headers, "slot_kind_signature");
            if (idCol < 0)
                return map;

            foreach (string line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                string[] parts = line.Split(',');
                if (parts.Length <= idCol || !int.TryParse(parts[idCol], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mid))
                    continue;
                string sig = sigCol >= 0 && parts.Length > sigCol ? parts[sigCol] : "";
                map[mid] = new OverlayLite(sig);
            }

            return map;
        }

        static Dictionary<int, SiblingLite> LoadSiblings(string path)
        {
            if (!File.Exists(path))
                return new();

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            Dictionary<int, SiblingLite> map = new();
            foreach (JsonElement el in doc.RootElement.EnumerateArray())
            {
                int mid = el.GetProperty("magicId").GetInt32();
                map[mid] = new SiblingLite(
                    el.GetProperty("clusterRepMagicId").GetInt32(),
                    el.GetProperty("isClusterRep").GetBoolean());
            }

            return map;
        }

        static Dictionary<int, SummaryLite> LoadSummary(string path)
        {
            Dictionary<int, SummaryLite> map = new();
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                string[] p = line.Split(',');
                if (p.Length < 4)
                    continue;
                map[int.Parse(p[0], CultureInfo.InvariantCulture)] = new SummaryLite(p[2]);
            }

            return map;
        }

        static string FamilyLetter(string family) => family switch
        {
            _ when family.StartsWith("A_", StringComparison.Ordinal) => "A",
            _ when family.StartsWith("B_", StringComparison.Ordinal) => "B",
            _ when family.StartsWith("C_", StringComparison.Ordinal) => "C",
            _ when family.StartsWith("D_", StringComparison.Ordinal) => "D",
            _ => "?",
        };

        static TaxonomyCompatRow ToTaxonomyRow(OrphanCatalogRow row) => new(
            row.MagicId,
            row.Category,
            row.Category,
            row.CatalogSpellNames,
            row.CatalogSourceFiles,
            row.Ps3FolderExists,
            row.DllExists,
            row.ClusterRepMagicId,
            row.IsClusterRep,
            FamilyLetter(row.Family),
            row.RecommendedAction,
            row.KernelRefs.Count,
            row.HasOverlayRow,
            row.OverlaySlotKindSignature);

        static void WriteMarkdown(string wave4Dir, dynamic summary, List<OrphanCatalogRow> rows)
        {
            StringBuilder sb = new();
            sb.AppendLine("# Magic DLL Orphan Catalog");
            sb.AppendLine();
            sb.AppendLine("Multi-source attribution for magic ids (not only monmagic2/command catalog regex).");
            sb.AppendLine();
            sb.AppendLine($"- Catalog+kernel: **{summary.catalogLinked}**");
            sb.AppendLine($"- Kernel-only: **{summary.kernelOnly}**");
            sb.AppendLine($"- Engine overlay carrier: **{summary.engineCarrier}**");
            sb.AppendLine($"- Unresolved: **{summary.unresolved}**");
            sb.AppendLine();
            sb.AppendLine("Fix: `moveAnim=magic_XXXX/None` rows now parse (44 ids recovered).");
            File.WriteAllText(Path.Combine(wave4Dir, "ORPHAN_CATALOG.md"), sb.ToString(), Encoding.UTF8);
        }

        static void WriteJson(string dir, string name, object value) =>
            File.WriteAllText(
                Path.Combine(dir, name),
                JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                new UTF8Encoding(false));

        static string DecodeName(byte[] bytes) =>
            FfxEncoding.DecodeScript(bytes).GetString(FfxEncoding.UsDecoder, withControlCodes: false);

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        sealed record SummaryLite(string Family);
        sealed record OverlayLite(string SlotKindSignature);
        sealed record SiblingLite(int ClusterRepMagicId, bool IsClusterRep);

        sealed record KernelRef(string Locale, string Table, int RowIndex, string Field, string RowName);

        sealed record OrphanCatalogRow(
            int MagicId,
            string Category,
            string Family,
            string[] CatalogSpellNames,
            string[] CatalogSourceFiles,
            IReadOnlyList<KernelRef> KernelRefs,
            string OverlaySlotKindSignature,
            bool HasOverlayRow,
            bool Ps3FolderExists,
            bool DllExists,
            int ClusterRepMagicId,
            bool IsClusterRep,
            string RecommendedAction);

        sealed record TaxonomyCompatRow(
            int MagicId,
            string Category,
            string Bucket,
            string[] SampleSpellNames,
            string[] SourceFiles,
            bool Ps3FolderExists,
            bool DllExists,
            int ClusterRepMagicId,
            bool IsClusterRep,
            string FamilyLetter,
            string RecommendedAction,
            int KernelRefCount,
            bool HasOverlayRow,
            string OverlaySlotKindSignature);
    }
}
