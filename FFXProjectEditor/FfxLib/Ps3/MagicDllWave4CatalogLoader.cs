using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Lazy loader for offline wave4 orphan/taxonomy JSON produced by
    /// <c>--magicdll-orphan-catalog</c> / <c>--magicdll-deep-corpus-wave4</c>.
    /// </summary>
    public static class MagicDllWave4CatalogLoader
    {
        static Dictionary<int, MagicDllWave4Attribution>? cache;
        static string? loadedFrom;
        static string? loadError;

        public static string AvailabilitySummary
        {
            get
            {
                EnsureLoaded();
                if (loadError != null)
                    return $"Wave4 catalog unavailable: {loadError}";
                if (cache == null || cache.Count == 0)
                    return "Wave4 catalog not found. Run --magicdll-orphan-catalog after wave4 deep corpus.";
                return $"Wave4 catalog loaded ({cache.Count} rows) from {loadedFrom}";
            }
        }

        public static int LoadedCount
        {
            get
            {
                EnsureLoaded();
                return cache?.Count ?? 0;
            }
        }

        public static bool TryGet(int magicId, out MagicDllWave4Attribution attribution)
        {
            EnsureLoaded();
            if (cache != null && cache.TryGetValue(magicId, out MagicDllWave4Attribution? row) && row != null)
            {
                attribution = row;
                return true;
            }

            attribution = MagicDllWave4Attribution.Empty(magicId);
            return false;
        }

        public static void Invalidate() => cache = null;

        static void EnsureLoaded()
        {
            if (cache != null)
                return;

            cache = new Dictionary<int, MagicDllWave4Attribution>();
            string? path = ResolveCatalogPath();
            if (path == null)
            {
                loadError = "wave4_orphan_catalog.json not found under work/magic_dll_logical_decompile_wave2/wave4";
                return;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("rows", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
                {
                    loadError = "wave4_orphan_catalog.json missing rows[]";
                    return;
                }

                foreach (JsonElement row in rows.EnumerateArray())
                {
                    if (!row.TryGetProperty("magicId", out JsonElement idEl) || !idEl.TryGetInt32(out int magicId))
                        continue;

                    string category = ReadString(row, "category") ?? "unknown";
                    string family = ReadString(row, "family") ?? "-";
                    string recommended = ReadString(row, "recommendedAction") ?? "-";
                    string overlaySig = ReadString(row, "overlaySlotKindSignature") ?? "-";
                    bool isClusterRep = row.TryGetProperty("isClusterRep", out JsonElement repEl) && repEl.ValueKind == JsonValueKind.True;
                    int? clusterRep = row.TryGetProperty("clusterRepMagicId", out JsonElement clusterEl) && clusterEl.TryGetInt32(out int clusterId)
                        ? clusterId
                        : null;

                    List<string> spellNames = [];
                    if (row.TryGetProperty("catalogSpellNames", out JsonElement spells) && spells.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement spell in spells.EnumerateArray())
                        {
                            string? name = spell.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                                spellNames.Add(name);
                        }
                    }

                    List<MagicDllWave4KernelRef> kernelRefs = [];
                    if (row.TryGetProperty("kernelRefs", out JsonElement kernelEl) && kernelEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement kref in kernelEl.EnumerateArray())
                        {
                            kernelRefs.Add(new MagicDllWave4KernelRef(
                                ReadString(kref, "locale") ?? "-",
                                ReadString(kref, "table") ?? "-",
                                kref.TryGetProperty("rowIndex", out JsonElement rowIndex) && rowIndex.TryGetInt32(out int ri) ? ri : -1,
                                ReadString(kref, "field") ?? "-",
                                ReadString(kref, "rowName") ?? "-"));
                        }
                    }

                    cache[magicId] = MagicDllWave4Attribution.Create(
                        magicId,
                        category,
                        family,
                        recommended,
                        overlaySig,
                        spellNames,
                        kernelRefs,
                        isClusterRep,
                        clusterRep);
                }

                loadedFrom = path;
                loadError = null;
            }
            catch (Exception ex)
            {
                cache = new Dictionary<int, MagicDllWave4Attribution>();
                loadError = ex.Message;
            }
        }

        static string? ResolveCatalogPath()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                if (string.IsNullOrWhiteSpace(start))
                    continue;

                DirectoryInfo? dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "work", "magic_dll_logical_decompile_wave2", "wave4", "wave4_orphan_catalog.json");
                    if (File.Exists(candidate))
                        return candidate;
                    dir = dir.Parent;
                }
            }

            return null;
        }

        static string? ReadString(JsonElement element, string name)
            => element.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;
    }

    public sealed record MagicDllWave4KernelRef(string Locale, string Table, int RowIndex, string Field, string RowName)
    {
        public string Summary => $"{Locale}/{Table}:{RowIndex} {Field}={RowName}";
    }

    public sealed class MagicDllWave4Attribution
    {
        public int MagicId { get; }
        public string Category { get; }
        public string Family { get; }
        public string RecommendedAction { get; }
        public string OverlaySlotKindSignature { get; }
        public IReadOnlyList<string> CatalogSpellNames { get; }
        public IReadOnlyList<MagicDllWave4KernelRef> KernelRefs { get; }
        public int KernelRefCount => KernelRefs.Count;
        public bool IsClusterRep { get; }
        public int? ClusterRepMagicId { get; }

        public string CategoryBadge => Category switch
        {
            "catalog_and_kernel" => "catalog+kernel",
            "engine_overlay_carrier" => "VM carrier",
            "cluster_twin_visual" => "visual twin",
            "clone_generated" => "clone slot",
            _ => Category
        };

        public string EditingGuidance => Category switch
        {
            "engine_overlay_carrier" =>
                "VM interpreter carrier: timing and phase live in overlay callback slots. Prefer Host Context / logical decompile over bulk vec4 patches.",
            "cluster_twin_visual" =>
                "Byte-identical or overlay-twin DLL: compare PS3 textures and spell rows before color/timing edits.",
            "clone_generated" =>
                "Generated clone overlay slot: verify source DLL and PS3 folder before shipping patches.",
            "catalog_and_kernel" when Family is "C_RootSelfGovernedParam" or "D_EgoTasklist" =>
                "Family C/D: visual color = PS3 phyre; blue-dominant vec4 in .data = possible cast→hit TIMING (RT2 0718 Flan Flood). Prefer phyre recolor — bulk vec4 can advance damage or crash.",
            "catalog_and_kernel" =>
                "Named spell linkage proved: Family Comparator + PS3 Magic are the safe authoring surfaces.",
            _ =>
                "Offline taxonomy only — confirm with in-game RT2 before production patches."
        };

        public string Rt2Priority => Category switch
        {
            "engine_overlay_carrier" => "high — VM opcode / callback semantics",
            "cluster_twin_visual" => "medium — visual diff vs cluster rep",
            "clone_generated" => "medium — clone fidelity",
            _ when KernelRefCount == 0 => "Low — no kernel references.",
            _ => "standard — spell gameplay confirm"
        };

        public string CatalogSpellsSummary =>
            CatalogSpellNames.Count == 0
                ? "-"
                : string.Join(", ", CatalogSpellNames.Take(8)) + (CatalogSpellNames.Count > 8 ? $" (+{CatalogSpellNames.Count - 8})" : string.Empty);

        public string KernelRefSummary =>
            KernelRefCount == 0
                ? "No kernel references indexed."
                : $"{KernelRefCount} kernel ref(s); top: {string.Join("; ", KernelRefs.Take(3).Select(r => r.Summary))}";

        public bool HasData => Category != "unknown";

        internal static MagicDllWave4Attribution Create(
            int magicId,
            string category,
            string family,
            string recommendedAction,
            string overlaySlotKindSignature,
            IReadOnlyList<string> catalogSpellNames,
            IReadOnlyList<MagicDllWave4KernelRef> kernelRefs,
            bool isClusterRep,
            int? clusterRepMagicId) =>
            new(magicId, category, family, recommendedAction, overlaySlotKindSignature, catalogSpellNames, kernelRefs, isClusterRep, clusterRepMagicId);

        MagicDllWave4Attribution(
            int magicId,
            string category,
            string family,
            string recommendedAction,
            string overlaySlotKindSignature,
            IReadOnlyList<string> catalogSpellNames,
            IReadOnlyList<MagicDllWave4KernelRef> kernelRefs,
            bool isClusterRep,
            int? clusterRepMagicId)
        {
            MagicId = magicId;
            Category = category;
            Family = family;
            RecommendedAction = recommendedAction;
            OverlaySlotKindSignature = overlaySlotKindSignature;
            CatalogSpellNames = catalogSpellNames;
            KernelRefs = kernelRefs;
            IsClusterRep = isClusterRep;
            ClusterRepMagicId = clusterRepMagicId;
        }

        public static MagicDllWave4Attribution Empty(int magicId) =>
            Create(magicId, "unknown", "-", "-", "-", [], [], false, null);
    }
}
