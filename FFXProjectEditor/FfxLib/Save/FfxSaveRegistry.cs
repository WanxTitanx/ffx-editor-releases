// ============================================================================
// FfxSaveRegistry — save-field registry (embedded/embedded ffxed_registry.json)
// PURPOSE : loads the data-driven save registry (catalogs, int/bit/combo fields, batch actions, key-item
//           bits, weapon catalogs) and exposes typed lookups by section/range. Single source for the editor.
// WHY     : the save layout is huge and was generated from the FFXED dataset; keeping it as JSON (generated)
//           avoids hand-maintaining hundreds of offsets in C#. Root is lazy-loaded from the embedded resource
//           (or a FfxLib/Save/Generated file on disk as fallback).
// EVIDENCE: FFXED v0.749 registry (generated); section ranges mirror the editor tabs.
// MAINT   : the JSON is the wire format — regenerate through the generator script, never hand-edit offsets
//           here blindly. BatchActionsForSection action-ids are coupled to FfxSaveBatchActions.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXProjectEditor.FfxLib.Save
{
    public sealed class FfxSaveCatalogEntry
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("bytes")]
        public int[] Bytes { get; set; } = Array.Empty<int>();
    }

    public sealed class FfxSaveRegistryField
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("bytes")]
        public int Bytes { get; set; }

        [JsonPropertyName("bit")]
        public int Bit { get; set; }

        [JsonPropertyName("catalog")]
        public string? Catalog { get; set; }
    }

    public sealed class FfxSaveRegistryBatchAction
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("actionId")]
        public int ActionId { get; set; }

        [JsonPropertyName("needsSlot")]
        public bool NeedsSlot { get; set; }
    }

    public sealed class FfxSaveRegistryRoot
    {
        [JsonPropertyName("catalogs")]
        public Dictionary<string, List<FfxSaveCatalogEntry>> Catalogs { get; set; } = new();

        [JsonPropertyName("itemCatalog")]
        public List<FfxSaveCatalogEntry> ItemCatalog { get; set; } = new();

        [JsonPropertyName("intFields")]
        public List<FfxSaveRegistryField> IntFields { get; set; } = new();

        [JsonPropertyName("bitFields")]
        public List<FfxSaveRegistryField> BitFields { get; set; } = new();

        [JsonPropertyName("comboFields")]
        public List<FfxSaveRegistryField> ComboFields { get; set; } = new();

        [JsonPropertyName("batchActions")]
        public List<FfxSaveRegistryBatchAction> BatchActions { get; set; } = new();

        [JsonPropertyName("miscHighlights")]
        public List<FfxSaveRegistryField> MiscHighlights { get; set; } = new();

        [JsonPropertyName("keyItemBits")]
        public List<FfxSaveRegistryField> KeyItemBits { get; set; } = new();

        [JsonPropertyName("weaponCatalogByCharacter")]
        public Dictionary<string, string> WeaponCatalogByCharacter { get; set; } = new();
    }

    public static class FfxSaveRegistry
    {
        static readonly Lazy<FfxSaveRegistryRoot> LazyRoot = new(Load);

        public static FfxSaveRegistryRoot Root => LazyRoot.Value;

        static FfxSaveRegistryRoot Load()
        {
            Assembly asm = typeof(FfxSaveRegistry).Assembly;
            const string resourceName = "FFXProjectEditor.FfxLib.Save.Generated.ffxed_registry.json";
            using Stream? stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                string path = Path.Combine(AppContext.BaseDirectory, "FfxLib", "Save", "Generated", "ffxed_registry.json");
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<FfxSaveRegistryRoot>(File.ReadAllText(path)) ?? new();

                throw new InvalidOperationException("ffxed_registry.json not found (embedded or on disk).");
            }

            return JsonSerializer.Deserialize<FfxSaveRegistryRoot>(stream) ?? new();
        }

        public static IReadOnlyList<FfxSaveCatalogEntry> GetCatalog(string? catalogId)
        {
            if (string.IsNullOrEmpty(catalogId))
                return Array.Empty<FfxSaveCatalogEntry>();

            return Root.Catalogs.TryGetValue(catalogId, out List<FfxSaveCatalogEntry>? list)
                ? list
                : Array.Empty<FfxSaveCatalogEntry>();
        }

        public static IReadOnlyList<FfxSaveCatalogEntry> GetWeaponCatalogForCharacter(int characterIndex)
        {
            string key = characterIndex.ToString();
            if (!Root.WeaponCatalogByCharacter.TryGetValue(key, out string? catalogId))
                catalogId = "weaponPlaceholder";

            return GetCatalog(catalogId);
        }

        public static IReadOnlyList<FfxSaveCatalogEntry> EquipCharacterCatalog =>
            GetCatalog("c0007hArr2");

        public static IReadOnlyList<FfxSaveCatalogEntry> EquippedOnCatalog =>
            GetCatalog("c0007hArr3");

        public static IReadOnlyList<FfxSaveCatalogEntry> AppearanceCatalog =>
            GetCatalog("c0007hArr22");

        public static IReadOnlyList<FfxSaveCatalogEntry> DamageFormulaCatalog =>
            GetCatalog("c0007hArr20");

        public static IReadOnlyList<FfxSaveCatalogEntry> AutoCapacityCatalog =>
            GetCatalog("c0007hArr21");

        public static IReadOnlyList<FfxSaveCatalogEntry> AutoAbilityCatalog =>
            GetCatalog("c0007hArr23");

        public static IEnumerable<FfxSaveRegistryField> FieldsInRange(int min, int maxInclusive) =>
            Root.IntFields.Where(f => f.Offset >= min && f.Offset <= maxInclusive)
                .Concat(Root.ComboFields.Where(f => f.Offset >= min && f.Offset <= maxInclusive))
                .Concat(Root.BitFields.Where(f => f.Offset >= min && f.Offset <= maxInclusive))
                .OrderBy(f => f.Offset)
                .ThenBy(f => f.Label);

        public static IEnumerable<FfxSaveRegistryBatchAction> BatchActionsForSection(FfxSaveSection section) =>
            section switch
            {
                FfxSaveSection.Character => Root.BatchActions.Where(a => a.ActionId is 1 or 4 or 6 or 7 or 8 or 10 or 42 or 43 or 44 or 45 or 54 or 58 or 59),
                FfxSaveSection.Equipment => Root.BatchActions.Where(a => a.ActionId is 46 or 47),
                FfxSaveSection.Items => Root.BatchActions.Where(a => a.ActionId is 0 or 5),
                FfxSaveSection.Blitzball => Root.BatchActions.Where(a => a.ActionId is 3 or 48 or 49 or 50 or 51 or 52),
                FfxSaveSection.SphereGrid => Root.BatchActions.Where(a => a.ActionId is 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 22 or 23 or 32 or 33 or 35 or 36 or 37 or 38),
                FfxSaveSection.Minigame => Root.BatchActions.Where(a => a.ActionId is 9 or 18 or 21 or 24 or 25 or 56 or 57),
                FfxSaveSection.MiscImport => Root.BatchActions.Where(a => a.ActionId is 26 or 27 or 28 or 29 or 30 or 40 or 41 or 55),
                _ => Enumerable.Empty<FfxSaveRegistryBatchAction>(),
            };
    }

    public enum FfxSaveSection
    {
        Character,
        Equipment,
        Items,
        Blitzball,
        SphereGrid,
        Minigame,
        MiscImport,
    }

    public static class FfxSaveSectionRanges
    {
        public static (int Min, int Max) GetRange(FfxSaveSection section) => section switch
        {
            FfxSaveSection.Equipment => (17628, 22027),
            FfxSaveSection.Items => (15696, 16907),
            FfxSaveSection.Blitzball => (3252, 7211),
            FfxSaveSection.SphereGrid => (8748, 12601),
            FfxSaveSection.Minigame => (700, 3300),
            FfxSaveSection.MiscImport => (0, 25847),
            _ => (0, 0),
        };
    }
}
