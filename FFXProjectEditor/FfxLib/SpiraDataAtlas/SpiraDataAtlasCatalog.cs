using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Customization;

namespace FFXProjectEditor.FfxLib.SpiraDataAtlas
{
    public enum SpiraDataAtlasEntryKind
    {
        Dataset,
        Crosslink,
        UnknownHealth,
        EvidencePolicy,
    }

    public enum SpiraDataAtlasDetailKind
    {
        ParsedFile,
        DatasetLayer,
        Monster,
        Command,
        GearRewardShop,
        ItemShopCommand,
        MixCombination,
        BlitzballGrowthEvent,
        BlitzballEventRef,
        BlitzballPrizeRef,
        BlitzballPrizeGuardrail,
        PcAeonStats,
        PcAeonFineStat,
        SphereGridHealth,
        SphereGridNode,
        MonsterPresence,
        CommandEligibility,
        GearNameModel,
        UnknownHealth,
        AtelCallShape,
        AtelBranchShape,
        AtelFieldWriteShape,
        AtelWorker,
        AeonGrowthRecipe,
        PlayerGrowthStat,
    }

    public sealed record SpiraDataAtlasEntry(
        string Id,
        SpiraDataAtlasEntryKind Kind,
        string Title,
        string Summary,
        string Domain,
        int RowCount,
        string Evidence,
        string WriterPolicy,
        string Tags,
        string SourcePath)
    {
        public string SearchBlob => string.Join(" ", Id, Kind, Title, Summary, Domain, RowCount, Evidence, WriterPolicy, Tags, SourcePath);
        public bool IsBlocked =>
            WriterPolicy.Contains("blocked", StringComparison.OrdinalIgnoreCase)
            || Evidence.Contains("blocked", StringComparison.OrdinalIgnoreCase);
    }

    public sealed record SpiraCommandCrosslink(
        string OperandHex,
        string DisplayName,
        string Category,
        int CommandSiteCount,
        int MonsterSiteCount,
        int BattleSiteCount,
        int EventSiteCount,
        string SemanticKinds,
        string SiteExamples,
        string SinEligibility,
        string EvidenceHealth);

    public sealed record SpiraDataAtlasDetailEntry(
        string Id,
        SpiraDataAtlasDetailKind Kind,
        string Title,
        string Summary,
        string Detail,
        string Domain,
        string Evidence,
        string WriterPolicy,
        string Tags,
        string SourcePath)
    {
        public string SearchBlob => string.Join(" ", Id, Kind, Title, Summary, Detail, Domain, Evidence, WriterPolicy, Tags, SourcePath);
        public bool IsBlocked =>
            Kind == SpiraDataAtlasDetailKind.UnknownHealth
            || WriterPolicy.Contains("blocked", StringComparison.OrdinalIgnoreCase)
            || Evidence.Contains("blocked", StringComparison.OrdinalIgnoreCase);
    }

    // Read-only atlas bridge for BIBLE OF SPIRA and adjacent editor surfaces.
    // It never writes game files. It consumes small manifests/crosslinks when present in a dev repo and keeps a
    // compiled fallback so release builds still have honest evidence summaries.
    public static class SpiraDataAtlasCatalog
    {
        public const string DisplayName = "Spira Data Atlas";

        static readonly Lazy<IReadOnlyList<SpiraDataAtlasEntry>> _all = new(BuildEntries);
        static readonly Lazy<IReadOnlyList<SpiraDataAtlasDetailEntry>> _details = new(BuildDetails);
        static readonly Lazy<IReadOnlyDictionary<string, SpiraCommandCrosslink>> _commandCrosslinks =
            new(LoadCommandCrosslinks);
        static readonly Lazy<IReadOnlyDictionary<string, ushort>> _shopGearSlotValues =
            new(LoadShopGearSlotValues);
        static readonly Lazy<IReadOnlyDictionary<string, ushort>> _shopItemSlotValues =
            new(LoadShopItemSlotValues);
        static readonly Lazy<IReadOnlyDictionary<string, ushort>> _mixResultValues =
            new(LoadMixResultValues);
        // Compiled, byte-grounded value map for the Aeon-growth value-guard (entry index -> raw Result/Item).
        // Sourced from the compiled SumGrowAeonRecipeData table, so it resolves in ANY build (no work/ dependency).
        static readonly Lazy<IReadOnlyDictionary<int, (ushort Result, ushort Item)>> _aeonGrowthValues =
            new(() => SumGrowAeonRecipeData.Rows.ToDictionary(r => r.Index, r => (r.Result, r.Item)));
        // Compiled, byte-grounded value map for the PC/Aeon player-growth value-guard (Jarvis-TIDUS).
        // Key = (slot index, "save:<field>" | "rom:<field>") -> raw value, sourced from the compiled
        // PlayerGrowthData table (ply_save.bin + ply_rom.bin, new_uspc), so it resolves in ANY build with no
        // work/ dependency. Same raw value-space the PlayerGrowthEditor edits, so a mismatch (after the user
        // edits the field) breaks the match and hides the badge, exactly like the Shop/Aeon value-guards.
        static readonly Lazy<IReadOnlyDictionary<(int Index, string Key), int>> _playerGrowthValues =
            new(BuildPlayerGrowthValueMap);

        // prepare.bin mix items are addressed in the Items game-index space (0x2000 + table index), the same value
        // MixTable_File.ResolveItemGameIndex produces and the corpus mix_key encodes (mix:0x{item1}+0x{item2}).
        const int MixItemGameIndexBase = 0x2000;

        public static IReadOnlyList<SpiraDataAtlasEntry> All => _all.Value;
        public static IReadOnlyList<SpiraDataAtlasDetailEntry> Details => _details.Value;

        // Read-only entry point for the Blitzball prize UI (Jarvis-WAKKA): every Atlas detail in the
        // "blitzball-prize" domain — the 164 prize-index resolution rows + 64 script sites + 1 guardrail.
        // No writer, no runtime; dev work/ tree only (empty in a release build without work/).
        static readonly Lazy<IReadOnlyList<SpiraDataAtlasDetailEntry>> _blitzballPrizeDetails =
            new(() => Details.Where(d => string.Equals(d.Domain, "blitzball-prize", StringComparison.OrdinalIgnoreCase)).ToList());
        public static IReadOnlyList<SpiraDataAtlasDetailEntry> BlitzballPrizeDetails => _blitzballPrizeDetails.Value;

        public static IReadOnlyList<SpiraDataAtlasEntry> Search(string? query, int limit = 24)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0)
                return All.Take(limit).ToList();

            string[] terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return All
                .Select(e => new { Entry = e, Score = Score(e, terms) })
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.Kind)
                .ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(x => x.Entry)
                .ToList();
        }

        public static bool TryGetCommandCrosslink(string operandHex, out SpiraCommandCrosslink? crosslink) =>
            _commandCrosslinks.Value.TryGetValue(NormalizeHex(operandHex, 4), out crosslink);

        public static IReadOnlyList<SpiraDataAtlasDetailEntry> SearchDetails(string? query, int limit = 24)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0)
                return Details.Take(limit).ToList();

            string[] terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Details
                .Select(e => new { Entry = e, Score = Score(e, terms) })
                .Where(x => x.Score >= 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Entry.Kind)
                .ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(x => x.Entry)
                .ToList();
        }

        public static bool TryGetDetail(string id, out SpiraDataAtlasDetailEntry? detail) =>
            (detail = _details.Value.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))) != null;

        public static bool TryGetMonsterDetail(string monsterId, out SpiraDataAtlasDetailEntry? detail)
        {
            string id = NormalizeMonsterId(monsterId);
            if (id.Length == 0)
            {
                detail = null;
                return false;
            }

            return TryGetDetail($"atlas:monster:{id}", out detail);
        }

        // Tiny read-only accessor for the Sphere Grid module badges: maps a (layout, nodeIndex) pair to its
        // sphere-grid-node detail without the caller needing to know the id shape or SanitizeId.
        public static bool TryGetSphereGridNode(string layout, string nodeIndex, out SpiraDataAtlasDetailEntry? detail)
        {
            detail = null;
            if (string.IsNullOrWhiteSpace(layout) || string.IsNullOrWhiteSpace(nodeIndex))
                return false;

            return TryGetDetail($"atlas:sphere-grid-node:{layout.ToLowerInvariant()}:{SanitizeId(nodeIndex)}", out detail);
        }

        // Tiny read-only accessor for a future Customization/Aeon badge: maps a sum_grow.bin recipe (entry index)
        // to its Aeon-growth detail, value-guarded like the Shop accessors. sum_grow.bin is Aeon-ONLY and the
        // backing table is a compiled, byte-grounded copy (work/-independent), so this resolves in any build. The
        // guard compares the editor's CURRENT raw Result + Item for that recipe against the corpus values; editing
        // the recipe to a different ability/item (or passing an out-of-range index) breaks the match and hides the
        // strip, exactly like the Shop value-guards. Read-only — never a writer, never PC/Sphere (no such rows).
        public static bool TryGetAeonGrowthRecipe(int entryIndex, int currentResultRaw, int currentItemRaw, out SpiraDataAtlasDetailEntry? detail)
        {
            detail = null;
            if (entryIndex < 0)
                return false;

            if (!_aeonGrowthValues.Value.TryGetValue(entryIndex, out (ushort Result, ushort Item) corpus))
                return false;

            if (corpus.Result != currentResultRaw || corpus.Item != currentItemRaw)
                return false;

            return TryGetDetail($"atlas:aeon-growth:{entryIndex}", out detail) && detail != null;
        }

        // Tiny read-only accessor for a future PlayerGrowth/PC-stats badge (Jarvis-TIDUS): maps a player slot
        // field (source "ply_save" field "basehp", or source "ply_rom" field "hpcoefa", etc.) to its player-growth
        // detail, value-guarded like the Aeon/Shop accessors. The compiled PlayerGrowthData table is byte-grounded
        // from ply_save.bin + ply_rom.bin (new_uspc, the region the editor loads), so it resolves in ANY build with
        // no work/ dependency. The guard compares the editor's CURRENT raw value for that field against the compiled
        // value; editing the field (or passing an unknown slot/source/field) breaks the match and hides the strip.
        // Read-only - never a writer, never the PlayerGrowth save path.
        public static bool TryGetPlayerGrowthStat(string source, int characterIndex, string field, int currentRawValue, out SpiraDataAtlasDetailEntry? detail)
        {
            detail = null;
            if (characterIndex < 0 || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(field))
                return false;

            if (!TryNormalizePlayerGrowthSource(source, out string src))
                return false;

            string key = $"{src}:{field.Trim().ToLowerInvariant()}";
            if (!_playerGrowthValues.Value.TryGetValue((characterIndex, key), out int corpusRaw))
                return false;

            if (corpusRaw != currentRawValue)
                return false;

            return TryGetDetail($"atlas:player-stat:{characterIndex}", out detail) && detail != null;
        }

        static bool TryNormalizePlayerGrowthSource(string source, out string normalized)
        {
            switch (source.Trim().ToLowerInvariant())
            {
                case "save":
                case "ply_save":
                case "ply_save.bin":
                    normalized = "save";
                    return true;
                case "rom":
                case "ply_rom":
                case "ply_rom.bin":
                    normalized = "rom";
                    return true;
                default:
                    normalized = "";
                    return false;
            }
        }

        // Builds the (slot index, "save:<field>" | "rom:<field>") -> raw value map from the compiled, byte-grounded
        // PlayerGrowthData table. Field names mirror the editor's PlayerKernelRow members (lowercased). Read-only.
        static IReadOnlyDictionary<(int Index, string Key), int> BuildPlayerGrowthValueMap()
        {
            Dictionary<(int, string), int> map = new();
            foreach (PlayerGrowthRow r in PlayerGrowthData.Rows)
            {
                // ply_save base stats + max (the stat anchors the editor shows).
                map[(r.Index, "save:basehp")] = r.BaseHp;
                map[(r.Index, "save:basemp")] = r.BaseMp;
                map[(r.Index, "save:basestr")] = r.BaseStr;
                map[(r.Index, "save:basedef")] = r.BaseDef;
                map[(r.Index, "save:basemag")] = r.BaseMag;
                map[(r.Index, "save:basemdf")] = r.BaseMdf;
                map[(r.Index, "save:baseagi")] = r.BaseAgi;
                map[(r.Index, "save:baselck")] = r.BaseLck;
                map[(r.Index, "save:baseeva")] = r.BaseEva;
                map[(r.Index, "save:baseacc")] = r.BaseAcc;
                map[(r.Index, "save:maxhp")] = r.MaxHp;
                map[(r.Index, "save:maxmp")] = r.MaxMp;
                // ply_rom growth ROM: AP-requirement curve + per-stat auto-growth coefficients.
                map[(r.Index, "rom:apreqa")] = r.ApReqA;
                map[(r.Index, "rom:apreqb")] = r.ApReqB;
                map[(r.Index, "rom:apreqc")] = r.ApReqC;
                map[(r.Index, "rom:apreqmax")] = r.ApReqMax;
                map[(r.Index, "rom:hpcoefa")] = r.HpA;
                map[(r.Index, "rom:hpcoefb")] = r.HpB;
                map[(r.Index, "rom:mpcoefa")] = r.MpA;
                map[(r.Index, "rom:mpcoefb")] = r.MpB;
                map[(r.Index, "rom:strcoefa")] = r.StrA;
                map[(r.Index, "rom:strcoefb")] = r.StrB;
                map[(r.Index, "rom:defcoefa")] = r.DefA;
                map[(r.Index, "rom:defcoefb")] = r.DefB;
                map[(r.Index, "rom:magcoefa")] = r.MagA;
                map[(r.Index, "rom:magcoefb")] = r.MagB;
                map[(r.Index, "rom:mdfcoefa")] = r.MdfA;
                map[(r.Index, "rom:mdfcoefb")] = r.MdfB;
                map[(r.Index, "rom:agicoefa")] = r.AgiA;
                map[(r.Index, "rom:agicoefb")] = r.AgiB;
                map[(r.Index, "rom:evacoefa")] = r.EvaA;
                map[(r.Index, "rom:evacoefb")] = r.EvaB;
                map[(r.Index, "rom:acccoefa")] = r.AccA;
                map[(r.Index, "rom:acccoefb")] = r.AccB;
            }
            return map;
        }

        // Tiny read-only accessor for the Treasure module badges: maps a gear-chest treasure row (takara Kind=0x05)
        // to its gear reward/shop detail. The treasure-buki-get crosslink keys source_key as "treasure:0x{index:X4}"
        // (proved 1:1 against takara.bin, 0 mismatches across all 82 gear rows), so the id is reconstructible from the
        // editor's row index with no guessing. expectedBukiGetRow keeps the badge honest on an editable module: if the
        // chest is repointed to a different buki_get row (or retyped away from gear), the recorded corpus buki_get no
        // longer matches and the lookup fails, so the strip hides instead of showing a stale reward.
        public static bool TryGetTreasureGear(int treasureIndex, int expectedBukiGetRow, out SpiraDataAtlasDetailEntry? detail)
        {
            detail = null;
            if (treasureIndex < 0)
                return false;

            if (!TryGetDetail($"atlas:gear:{SanitizeId($"treasure:0x{treasureIndex:X4}")}", out detail) || detail == null)
                return false;

            // The corpus buki_get row is recorded in the detail text as "buki_get=N". When it is readable it must match
            // the editor's current gear target; otherwise the badge would describe a reward the chest no longer grants.
            if (TryParseCorpusBukiGet(detail.Detail, out int corpusBukiGet) && corpusBukiGet != expectedBukiGetRow)
            {
                detail = null;
                return false;
            }

            return true;
        }

        static bool TryParseCorpusBukiGet(string detailText, out int bukiGet)
        {
            bukiGet = -1;
            if (string.IsNullOrEmpty(detailText))
                return false;

            const string marker = "buki_get=";
            int start = detailText.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return false;

            start += marker.Length;
            int end = start;
            while (end < detailText.Length && char.IsDigit(detailText[end]))
                end++;

            return end > start && int.TryParse(detailText.AsSpan(start, end - start), out bukiGet);
        }

        // Tiny read-only accessor for the Shop module gear-slot badges. The gear-shop crosslink keys each slot as
        // "gear-shop:0x{bank:X2}:slot:{slot}" (bank == editor ShopEntryRow.EntryIndex, slot == editor SlotIndex), and
        // the parser corpus records that slot's on-disk catalog index in gear_reward_shop_crosslink.csv. The editor's
        // ShopSlotEntry.RawValue is the identical on-disk little-endian ushort (arms_shop.bin reuses a 0-based catalog
        // where index 0 is a reserved null entry, so the first sellable gear is index 1 = on-disk value 0x01). The
        // value-guard is therefore a same-space integer compare: show the badge only while the slot's CURRENT RawValue
        // still equals the corpus value (and is non-empty). Editing the slot to different gear breaks the match and
        // hides the strip, exactly like the Treasure value-guard. This is a parser-corpus + reader-semantics evidence
        // path (read-only) — never a runtime/byte-grounded proof.
        public static bool TryGetShopGearSlot(int bank, int slot, int currentRawValue, out SpiraDataAtlasDetailEntry? detail)
        {
            detail = null;
            if (bank < 0 || slot < 0 || currentRawValue == 0)
                return false;

            // One SanitizeId feeds both the value-dict lookup and the atlas:gear id, so they can never disagree.
            // bank:X2 must be 2-padded to match the corpus key ("0x00".."0x2E"); SanitizeId lowercases, so hex case
            // is irrelevant, but an unpadded single-digit bank would sanitize to a different id and miss.
            string sanitizedId = SanitizeId($"gear-shop:0x{bank:X2}:slot:{slot}");
            if (!_shopGearSlotValues.Value.TryGetValue(sanitizedId, out ushort corpusValue) || corpusValue != currentRawValue)
                return false;

            return TryGetDetail($"atlas:gear:{sanitizedId}", out detail) && detail != null;
        }

        // Tiny read-only accessor for the Shop module item-slot badges — the mirror of TryGetShopGearSlot. The
        // item-shop crosslink keys each slot as "item-shop:0x{bank:X2}:slot:{slot}", and item_shop_command_crosslink.csv
        // records that slot's raw on-disk value (the encoded Items game-index, 0x2xxx). That value is byte-grounded 1:1 against
        // item_shop.bin and is the same little-endian ushort the editor reads as ShopSlotEntry.RawValue, so the
        // value-guard is a same-space integer compare: show the badge only while the slot's CURRENT RawValue still
        // equals the corpus value (and is non-empty). Editing the slot to a different item hides the strip. Read-only.
        public static bool TryGetShopItemSlot(int bank, int slot, int currentRawValue, out SpiraDataAtlasDetailEntry? detail)
        {
            detail = null;
            if (bank < 0 || slot < 0 || currentRawValue == 0)
                return false;

            string sanitizedId = SanitizeId($"item-shop:0x{bank:X2}:slot:{slot}");
            if (!_shopItemSlotValues.Value.TryGetValue(sanitizedId, out ushort corpusValue) || corpusValue != currentRawValue)
                return false;

            return TryGetDetail($"atlas:item-shop:{sanitizedId}", out detail) && detail != null;
        }

        // Tiny read-only accessor for the Mix Table module badges. prepare.bin stores the 112x112 mix matrix
        // lower-triangular: each unordered item pair {a,b} has its non-zero result at the canonical cell
        // origin=max(a,b), partner=min(a,b), and the mirror cell is empty (0). mix_combinations.csv is the
        // parser-canonical normalization of that table (6,328 rows = 112*113/2, 0 duplicate keys), keyed
        // "mix:0x{0x2000+max}+0x{0x2000+min}" with item1>=item2, so the id is reconstructible from the editor's
        // (origin, partner) indices with no guessing. Both the editor reader (MixTable_File, byte-identical per
        // MixTableRt0) and the FFXDataParser corpus read the same little-endian result word, so currentRawResult
        // (MixResultRow.RawResult) is a same-space integer compare against the recorded corpus result_hex. The
        // guard keeps the badge honest on an editable module: re-pointing the outcome to a different item breaks
        // the match and the strip hides. An empty cell (raw 0 — including every upper-triangle mirror) short-
        // circuits. This is a parser-corpus value-guard (read-only); the in-game mix effect is never proved here.
        public static bool TryGetMixCombination(int originIndex, int partnerIndex, int currentRawResult, out SpiraDataAtlasDetailEntry? detail)
        {
            detail = null;
            if (originIndex < 0 || partnerIndex < 0 || currentRawResult == 0)
                return false;

            int item1 = MixItemGameIndexBase + Math.Max(originIndex, partnerIndex);
            int item2 = MixItemGameIndexBase + Math.Min(originIndex, partnerIndex);

            // One SanitizeId feeds both the value-dict lookup and the atlas:mix id, so they can never disagree.
            string sanitizedId = SanitizeId($"mix:0x{item1:X4}+0x{item2:X4}");
            if (!_mixResultValues.Value.TryGetValue(sanitizedId, out ushort corpusResult) || corpusResult != currentRawResult)
                return false;

            return TryGetDetail($"atlas:mix:{sanitizedId}", out detail) && detail != null;
        }

        static IReadOnlyList<SpiraDataAtlasEntry> BuildEntries()
        {
            Dictionary<string, SpiraDataAtlasEntry> entries = BuiltInEntries()
                .ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_master_2026-06-08",
                         "spira_data_atlas_master_manifest.csv"))
            {
                string dataset = Get(row, "dataset");
                if (dataset.Length == 0)
                    continue;

                string stage = Get(row, "stage");
                string domain = Get(row, "domain");
                string lane = Get(row, "atlas_lane");
                string notes = Get(row, "notes");
                entries[$"atlas:master:{SanitizeId(stage)}:{SanitizeId(dataset)}"] = new SpiraDataAtlasEntry(
                    $"atlas:master:{SanitizeId(stage)}:{SanitizeId(dataset)}",
                    SpiraDataAtlasEntryKind.Dataset,
                    $"{stage}: {dataset}",
                    $"{lane}. {notes} Editor surface: {Get(row, "editor_surface")}",
                    domain,
                    ParseInt(Get(row, "rows")),
                    Get(row, "evidence"),
                    Get(row, "writer_policy"),
                    $"master manifest dataset layer {stage} {dataset} {domain} {lane} {Get(row, "priority")}",
                    Get(row, "csv_file"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "spira_data_atlas_step0c_manifest.csv"))
            {
                string dataset = Get(row, "dataset");
                if (dataset.Length == 0)
                    continue;

                int rows = ParseInt(Get(row, "rows"));
                string notes = Get(row, "notes");
                entries[$"atlas:step0c:{dataset}"] = new SpiraDataAtlasEntry(
                    $"atlas:step0c:{dataset}",
                    SpiraDataAtlasEntryKind.Crosslink,
                    $"Step0-C crosslink: {dataset}",
                    notes.Length == 0 ? "Read-only crosslink dataset generated from the Spira Data Atlas pass." : notes,
                    GuessDomain(dataset),
                    rows,
                    Get(row, "evidence"),
                    Get(row, "writer_policy"),
                    $"step0c crosslink bible {dataset}",
                    Get(row, "csv_file"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_unknown_health_2026-06-08",
                         "atlas_evidence_health_summary.csv"))
            {
                string kind = Get(row, "unknown_kind");
                string domain = Get(row, "domain");
                if (kind.Length == 0)
                    continue;

                entries[$"atlas:health:{domain}:{kind}"] = new SpiraDataAtlasEntry(
                    $"atlas:health:{domain}:{kind}",
                    SpiraDataAtlasEntryKind.UnknownHealth,
                    $"Atlas health: {kind}",
                    $"{Get(row, "count")} finding(s) in {domain}. Keep this as a badge/guardrail before UI or SIN promotion.",
                    domain,
                    ParseInt(Get(row, "count")),
                    Get(row, "evidence_badge"),
                    Get(row, "writer_policy"),
                    $"unknown health evidence bible {kind} {domain}",
                    "work/spira_data_atlas_unknown_health_2026-06-08/atlas_evidence_health_summary.csv");
            }

            return entries.Values
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.Domain, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static IReadOnlyList<SpiraDataAtlasDetailEntry> BuildDetails()
        {
            List<SpiraDataAtlasDetailEntry> details = new();

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v6_p0_extras_2026-06-08",
                         "parser_target_text_file_inventory.csv"))
            {
                string relativePath = Get(row, "relative_path");
                if (relativePath.Length == 0)
                    continue;

                string family = Get(row, "family");
                string stem = Get(row, "file_stem");
                string rawDomain = relativePath.StartsWith("battle", StringComparison.OrdinalIgnoreCase)
                    ? "battle-target-text"
                    : relativePath.StartsWith("event", StringComparison.OrdinalIgnoreCase)
                        ? "event-target-text"
                        : "parser-target-text";
                string summary =
                    $"{relativePath}: raw FFXDataParser target/text file, family {family}, {Get(row, "bytes")} byte(s). " +
                    "This is source evidence for v7 granular ATEL and v8 semantic indexes.";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:parsed-file:{SanitizeId(relativePath)}",
                    SpiraDataAtlasDetailKind.ParsedFile,
                    $"{stem} ({family})",
                    summary,
                    $"Raw parsed file: work/ffxdataparser_fullrun_2026-06-08-v4/target/text/{relativePath}",
                    rawDomain,
                    Get(row, "evidence"),
                    Get(row, "writer_policy"),
                    $"raw parsed target text {family} {stem} {relativePath} v7 v8 atel",
                    "work/step0_consolidated_v6_p0_extras_2026-06-08/parser_target_text_file_inventory.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_master_2026-06-08",
                         "spira_data_atlas_master_manifest.csv"))
            {
                string dataset = Get(row, "dataset");
                if (dataset.Length == 0)
                    continue;

                string stage = Get(row, "stage");
                string domain = Get(row, "domain");
                string lane = Get(row, "atlas_lane");
                string csvFile = Get(row, "csv_file");
                string summary =
                    $"{stage}/{dataset}: {Get(row, "rows")} row(s), domain {domain}, priority {Get(row, "priority")}. {Get(row, "notes")}";
                string detail =
                    $"Atlas lane: {lane}\nEditor surface: {Get(row, "editor_surface")}\nCSV: {csvFile}\nSHA256: {Get(row, "csv_sha256")}";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:dataset:{SanitizeId(stage)}:{SanitizeId(dataset)}",
                    SpiraDataAtlasDetailKind.DatasetLayer,
                    $"{dataset} ({stage})",
                    summary,
                    detail,
                    domain,
                    Get(row, "evidence"),
                    Get(row, "writer_policy"),
                    $"master dataset layer {stage} {dataset} {domain} {lane}",
                    "work/spira_data_atlas_master_2026-06-08/spira_data_atlas_master_manifest.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v8_atel_semantic_2026-06-08",
                         "atel_call_site_summary.csv"))
            {
                string callId = Get(row, "call_id_hex");
                string callName = Get(row, "call_name");
                if (callId.Length == 0 || callName.Length == 0)
                    continue;

                string sourceKind = Get(row, "source_kind");
                string callNamespace = Get(row, "call_namespace");
                string count = Get(row, "count");

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:atel-call-shape:{SanitizeId(sourceKind)}:{callId.ToLowerInvariant()}:{SanitizeId(callName)}",
                    SpiraDataAtlasDetailKind.AtelCallShape,
                    $"{callName} (0x{callId})",
                    $"{sourceKind} ATEL call-shape summary: {count} site(s), namespace {callNamespace}.",
                    $"Call ID: 0x{callId}\nNamespace: {callNamespace}\nSource kind: {sourceKind}\nSites: {count}",
                    "atel-call-shape",
                    "ffxdataparser-target-text-corpus;presence-index;metadata-only",
                    "read-only;not-writer-authority",
                    $"fields by call-shape workers branches atel call summary {callNamespace} {callName} {callId} {sourceKind}",
                    "work/step0_consolidated_v8_atel_semantic_2026-06-08/atel_call_site_summary.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v8_atel_semantic_2026-06-08",
                         "atel_command_operand_summary.csv"))
            {
                string operand = Get(row, "command_hex");
                string commandName = Get(row, "command_name");
                if (operand.Length == 0 || commandName.Length == 0)
                    continue;

                string sourceKind = Get(row, "source_kind");
                string semanticKind = Get(row, "semantic_kind");
                string callId = Get(row, "call_id_hex");
                string count = Get(row, "count");

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:atel-command-sites:{SanitizeId(sourceKind)}:{SanitizeId(operand)}:{SanitizeId(callId)}",
                    SpiraDataAtlasDetailKind.Command,
                    $"{commandName} ({operand}) ATEL sites",
                    $"{sourceKind} ATEL command operand summary: {count} site(s), call 0x{callId}, semantic {semanticKind}.",
                    $"Operand: {operand}\nCommand: {commandName}\nCall ID: 0x{callId}\nSemantic kind: {semanticKind}\nSites: {count}",
                    "atel-command-sites",
                    "ffxdataparser-target-text-corpus;presence-index;metadata-only;semantic-candidate",
                    "read-only;not-writer-authority;sin-candidate-queue-only",
                    $"command AiCommandMetadata command sites SIN eligibility atel operand {operand} {commandName} {semanticKind}",
                    "work/step0_consolidated_v8_atel_semantic_2026-06-08/atel_command_operand_summary.csv"));
            }

            Dictionary<string, BranchShapeAccumulator> branchShapes = new(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v8_atel_semantic_2026-06-08",
                         "atel_branch_semantic_catalog.csv"))
            {
                string sourceKind = Get(row, "source_kind");
                string branchKind = Get(row, "branch_kind");
                string primarySemantic = Get(row, "primary_semantic");
                string semanticTags = Get(row, "semantic_tags");
                if (sourceKind.Length == 0 || branchKind.Length == 0 || primarySemantic.Length == 0)
                    continue;

                string key = $"{sourceKind}|{branchKind}|{primarySemantic}|{semanticTags}";
                if (!branchShapes.TryGetValue(key, out BranchShapeAccumulator? shape))
                {
                    shape = new BranchShapeAccumulator(sourceKind, branchKind, primarySemantic, semanticTags);
                    branchShapes[key] = shape;
                }

                shape.Count++;
                shape.SourceKeys.Add(Get(row, "source_key"));
                shape.Functions.Add(Get(row, "current_function"));
                AddEvidence(shape.EvidenceBadges, Get(row, "evidence_badge"));
                if (shape.Example.Length == 0)
                {
                    shape.Example =
                        $"{Get(row, "relative_path")}:{Get(row, "line_number")} {Get(row, "current_function")} " +
                        $"@0x{Get(row, "offset_hex")} -> {Get(row, "target_label")} 0x{Get(row, "target_offset_hex")}";
                }
            }

            foreach (BranchShapeAccumulator shape in branchShapes.Values)
            {
                string sourceExamples = string.Join(", ", shape.SourceKeys
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .Take(6));
                string functionExamples = string.Join(", ", shape.Functions
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .Take(6));
                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:atel-branch-shape:{SanitizeId(shape.SourceKind)}:{SanitizeId(shape.BranchKind)}:{SanitizeId(shape.PrimarySemantic)}:{SanitizeId(shape.SemanticTags)}",
                    SpiraDataAtlasDetailKind.AtelBranchShape,
                    $"{shape.SourceKind} {shape.BranchKind}: {shape.PrimarySemantic}",
                    $"ATEL branch/control-flow aggregate: {shape.Count} row(s), tags {shape.SemanticTags}.",
                    $"Source examples: {sourceExamples}\nFunction examples: {functionExamples}\nExample: {shape.Example}",
                    "atel-branch",
                    CombineEvidence(shape.EvidenceBadges.Append("branch-shape-aggregate")),
                    "read-only;not-writer-authority;branch-authoring-requires-AiScriptLab-and-AEON",
                    $"branches atel control-flow condition jump check switch run await {shape.SourceKind} {shape.BranchKind} {shape.PrimarySemantic} {shape.SemanticTags}",
                    "work/step0_consolidated_v8_atel_semantic_2026-06-08/atel_branch_semantic_catalog.csv"));
            }

            Dictionary<string, FieldWriteShapeAccumulator> fieldShapes = new(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v8_atel_semantic_2026-06-08",
                         "atel_field_write_semantic_catalog.csv"))
            {
                string callId = Get(row, "set_call_id_hex");
                string fieldHex = Get(row, "field_hex");
                string fieldName = Get(row, "field_name");
                string semanticKind = Get(row, "semantic_kind");
                string fieldCategory = Get(row, "field_category");
                if (callId.Length == 0 || fieldHex.Length == 0 || semanticKind.Length == 0)
                    continue;

                string key = $"{callId}|{fieldHex}|{fieldName}|{semanticKind}|{fieldCategory}";
                if (!fieldShapes.TryGetValue(key, out FieldWriteShapeAccumulator? shape))
                {
                    shape = new FieldWriteShapeAccumulator(callId, fieldHex, fieldName, semanticKind, fieldCategory);
                    fieldShapes[key] = shape;
                }

                shape.Count++;
                shape.SourceKinds.Add(Get(row, "source_kind"));
                AddEvidence(shape.EvidenceBadges, Get(row, "evidence_badge"));
                shape.RiskNote = FirstNonEmpty(shape.RiskNote, Get(row, "risk_note"));
                if (shape.Example.Length == 0)
                {
                    shape.Example =
                        $"{Get(row, "relative_path")}:{Get(row, "line_number")} {Get(row, "current_function")} @0x{Get(row, "offset_hex")}";
                }
            }

            foreach (FieldWriteShapeAccumulator shape in fieldShapes.Values)
            {
                string displayName = FirstNonEmpty(shape.FieldName, $"field_{shape.FieldHex}");
                string sourceKinds = string.Join(",", shape.SourceKinds.Where(s => !string.IsNullOrWhiteSpace(s)).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:atel-field-shape:{shape.CallIdHex.ToLowerInvariant()}:{shape.FieldHex.ToLowerInvariant()}:{SanitizeId(shape.SemanticKind)}",
                    SpiraDataAtlasDetailKind.AtelFieldWriteShape,
                    $"{displayName} via 0x{shape.CallIdHex}",
                    $"Field write call-shape aggregate: {shape.Count} row(s), field 0x{shape.FieldHex}, semantic {shape.SemanticKind}, category {shape.FieldCategory}.",
                    $"Call ID: 0x{shape.CallIdHex}\nField: {displayName} / 0x{shape.FieldHex}\nSource kinds: {sourceKinds}\nRisk: {shape.RiskNote}\nExample: {shape.Example}",
                    "atel-field-shape",
                    CombineEvidence(shape.EvidenceBadges.Append("field-shape-aggregate")),
                    "read-only;not-writer-authority;field-namespace-requires-call-context",
                    $"fields by call-shape field write atel {displayName} {shape.CallIdHex} {shape.FieldHex} {shape.SemanticKind} {shape.FieldCategory}",
                    "work/step0_consolidated_v8_atel_semantic_2026-06-08/atel_field_write_semantic_catalog.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v7_atel_granular_2026-06-08",
                         "atel_worker_catalog.csv"))
            {
                string sourceKey = Get(row, "source_key");
                string workerToken = Get(row, "worker_token");
                if (sourceKey.Length == 0 || workerToken.Length == 0)
                    continue;

                string sourceKind = Get(row, "source_kind");
                string typeText = Get(row, "type_text");
                string purpose = Get(row, "purpose_slot");
                string relativePath = Get(row, "relative_path");
                string summary =
                    $"{sourceKind} {sourceKey} {workerToken}: {typeText}, purpose {purpose}, " +
                    $"{Get(row, "functions_count")} function(s), {Get(row, "jumps_count")} jump(s).";
                string detail =
                    $"Path: {relativePath}:{Get(row, "line_number")}\n" +
                    $"Domain: {Get(row, "worker_domain")}; type 0x{Get(row, "type_hex")}; purpose 0x{Get(row, "purpose_slot_hex")}.\n" +
                    $"Private data addr/len: 0x{Get(row, "private_addr_hex")}/0x{Get(row, "private_len_hex")}.";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:atel-worker:{SanitizeId(sourceKind)}:{SanitizeId(sourceKey)}:{SanitizeId(workerToken)}",
                    SpiraDataAtlasDetailKind.AtelWorker,
                    $"{sourceKey} {workerToken} worker",
                    summary,
                    detail,
                    "atel-worker",
                    Get(row, "provenance_labels"),
                    "read-only;metadata-only;not-writer-authority",
                    $"workers atel ai patterns {sourceKind} {sourceKey} {workerToken} {typeText} {purpose} {relativePath}",
                    "work/step0_consolidated_v7_atel_granular_2026-06-08/atel_worker_catalog.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "monster_ai_battle_encounter_crosslink.csv"))
            {
                string monsterId = NormalizeMonsterId(Get(row, "monster_id"));
                if (monsterId.Length == 0)
                    continue;

                string name = FirstNonEmpty(Get(row, "canonical_name"), Get(row, "localized_name"), Get(row, "parser_name"), monsterId);
                string battleExamples = Get(row, "battle_examples");
                string patterns = TrimJsonishList(Get(row, "ai_patterns"));
                string summary =
                    $"Monster {monsterId} / actor 0x{Get(row, "actor_hex")}: HP {Get(row, "hp")}, AP {Get(row, "ap_normal")}/{Get(row, "ap_overkill")}, " +
                    $"{Get(row, "ai_worker_count")} worker(s), {Get(row, "ai_command_count")} command site(s), " +
                    $"{Get(row, "battle_count")} battle appearance(s), {Get(row, "encounter_formation_refs")} encounter formation ref(s).";
                if (battleExamples.Length > 0)
                    summary += $" Examples: {battleExamples}.";

                string detail =
                    $"Names: canonical={Get(row, "canonical_name")}; parser={Get(row, "parser_name")}; localized={Get(row, "localized_name")}.\n" +
                    $"AI: calls={Get(row, "ai_call_count")}; fields={Get(row, "ai_field_count")}; targets={Get(row, "ai_target_count")}; patterns={patterns}.\n" +
                    $"Abilities: {FirstNonEmpty(Get(row, "ability_examples"), "none")}.\n" +
                    $"Rewards: drop {Get(row, "primary_item_drop")}; steal {Get(row, "steal_item")}; gear {Get(row, "gear_drop")}.\n" +
                    $"Sensor/scan text: sensor={Get(row, "sensor_text_present")}; scan={Get(row, "scan_text_present")}.";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:monster:{monsterId}",
                    SpiraDataAtlasDetailKind.Monster,
                    $"{name} ({monsterId})",
                    summary,
                    detail,
                    "monster-corpus",
                    CombineEvidence(Get(row, "evidence_badge"), Get(row, "evidence_health")),
                    Get(row, "writer_policy"),
                    $"monster where-appears ai battle encounter {monsterId} {name} {patterns}",
                    "work/spira_data_atlas_step0c_crosslinks_2026-06-08/monster_ai_battle_encounter_crosslink.csv"));
            }

            Dictionary<string, EncounterFormationAccumulator> encounterByFormation = BuildEncounterFormationLookup();
            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v6_p0_extras_2026-06-08",
                         "battle_formation_monsters_catalog.csv"))
            {
                string monsterId = NormalizeMonsterId(Get(row, "monster_id"));
                string battleKey = Get(row, "battle_key");
                string slot = Get(row, "monster_slot_dec");
                if (monsterId.Length == 0 || battleKey.Length == 0 || slot.Length == 0)
                    continue;

                encounterByFormation.TryGetValue(battleKey, out EncounterFormationAccumulator? encounter);
                string name = FirstNonEmpty(Get(row, "monster_name"), monsterId);
                string encounterSummary = encounter == null
                    ? "No encounter table reference joined in the current atlas output."
                    : $"{encounter.ReferenceCount} encounter ref(s): {encounter.ExamplesText}.";
                string detail =
                    $"Monster: {monsterId} / actor 0x{Get(row, "actor_hex")}\n" +
                    $"Formation: {battleKey}; monster slot {slot} (0x{Get(row, "monster_slot_hex")}).\n" +
                    $"Encounter refs: {encounterSummary}\n" +
                    $"Raw: {Get(row, "raw_line")}";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:monster-presence:{monsterId}:{SanitizeId(battleKey)}:slot-{SanitizeId(slot)}",
                    SpiraDataAtlasDetailKind.MonsterPresence,
                    $"{name} in {battleKey} slot {slot}",
                    $"{monsterId} appears in battle formation {battleKey} slot {slot}. {encounterSummary}",
                    detail,
                    "monster-presence",
                    Get(row, "evidence"),
                    Get(row, "writer_policy"),
                    $"monster where appears granular formation slot encounter {monsterId} {name} {battleKey}",
                    "work/step0_consolidated_v6_p0_extras_2026-06-08/battle_formation_monsters_catalog.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "command_ai_site_crosslink.csv"))
            {
                string operand = Get(row, "operand_hex");
                string displayName = Get(row, "display_name");
                if (operand.Length == 0 || displayName.Length == 0)
                    continue;

                string status = Get(row, "ai_operand_status");
                string eligibility = Get(row, "sin_eligibility");
                string summary =
                    $"{displayName} {operand}: {Get(row, "category")} / {Get(row, "role_text")}; " +
                    $"{Get(row, "command_site_count")} ATEL site(s), eligibility {eligibility}.";
                string detail =
                    $"Description: {Get(row, "description")}\n" +
                    $"Formula: {Get(row, "formula")} {Get(row, "formula_hex")}; power {Get(row, "power")}; MP {Get(row, "mp_cost")}.\n" +
                    $"Target/status/element: {Get(row, "target_text")} / {Get(row, "status_text")} / {Get(row, "element_text")}.\n" +
                    $"AI operand status: {status}\n" +
                    $"Monster/Battle/Event sites: {Get(row, "monster_site_count")}/{Get(row, "battle_site_count")}/{Get(row, "event_site_count")}.\n" +
                    $"Calls: {Get(row, "call_ids")}; semantics: {Get(row, "semantic_kinds")}; examples: {Get(row, "site_examples")}.";

                string policy = Get(row, "writer_policy");
                if (status.Contains("metadata-only", StringComparison.OrdinalIgnoreCase))
                    policy = CombineEvidence(policy, "metadata-only-not-sin-payload");

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:command-eligibility:{SanitizeId(operand)}",
                    SpiraDataAtlasDetailKind.CommandEligibility,
                    $"{displayName} ({operand}) SIN eligibility",
                    summary,
                    detail,
                    "sin-eligibility",
                    CombineEvidence(Get(row, "evidence_badge"), Get(row, "evidence_health"), status, eligibility),
                    policy,
                    $"command AiCommandMetadata SIN eligibility command sites payload {operand} {displayName} {status} {eligibility}",
                    "work/spira_data_atlas_step0c_crosslinks_2026-06-08/command_ai_site_crosslink.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "gear_reward_shop_crosslink.csv"))
            {
                string sourceKey = Get(row, "source_key");
                if (sourceKey.Length == 0)
                    continue;

                string owner = Get(row, "owner_name");
                string gearKind = Get(row, "gear_kind");
                string title = $"{owner} {gearKind} reward/shop {sourceKey}".Trim();
                string summary =
                    $"{sourceKey}: {owner} {gearKind}, formula {Get(row, "formula")}, power {Get(row, "power")}, crit {Get(row, "crit_percent")}%, " +
                    $"{Get(row, "slot_count")} slot(s), abilities {FirstNonEmpty(Get(row, "ability_slots"), "none")}.";
                string detail =
                    $"buki_get={Get(row, "buki_get_id")}; treasure_index={Get(row, "treasure_index")}.\n" +
                    $"Name candidates ({Get(row, "name_candidate_count")}): {Get(row, "name_examples")}";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:gear:{SanitizeId(sourceKey)}",
                    SpiraDataAtlasDetailKind.GearRewardShop,
                    title,
                    summary,
                    detail,
                    "gear",
                    CombineEvidence(Get(row, "evidence_badge"), Get(row, "evidence_health")),
                    Get(row, "writer_policy"),
                    $"gear treasure shop reward weapon name auto-abilities {owner} {gearKind} {sourceKey}",
                    "work/spira_data_atlas_step0c_crosslinks_2026-06-08/gear_reward_shop_crosslink.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v6_p0_extras_2026-06-08",
                         "weapon_name_catalog.csv"))
            {
                string rowIndex = Get(row, "name_row_index_dec");
                string owner = Get(row, "owner_name");
                if (rowIndex.Length == 0 || owner.Length == 0)
                    continue;

                string weaponName = FirstNonEmpty(Get(row, "weapon_name"), "(blank weapon name)");
                string model = Get(row, "model_path");
                string summary = $"{owner} name row {rowIndex}: {weaponName}; model {model} / 0x{Get(row, "model_hex")}.";
                string detail =
                    $"w_name row: {rowIndex} (0x{Get(row, "name_row_index_hex")}), owner code {Get(row, "owner_code")}.\n" +
                    $"Row offset: 0x{Get(row, "row_offset_hex")}; model path: {model}; model hex: 0x{Get(row, "model_hex")}.\n" +
                    "This is exact for w_name.bin row/model, but it does not choose the final generated gear name for a treasure/shop payload by itself.";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:gear-name-model:{SanitizeId(owner)}:{SanitizeId(rowIndex)}",
                    SpiraDataAtlasDetailKind.GearNameModel,
                    $"{owner}: {weaponName}",
                    summary,
                    detail,
                    "gear-name-model",
                    Get(row, "evidence"),
                    $"{Get(row, "writer_policy")};read-only;not-reward-name-authority",
                    $"weapon name model gear w_name {owner} {weaponName} {model}",
                    "work/step0_consolidated_v6_p0_extras_2026-06-08/weapon_name_catalog.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "item_shop_command_crosslink.csv"))
            {
                string key = Get(row, "shop_slot_key");
                if (key.Length == 0)
                    continue;

                string itemName = FirstNonEmpty(Get(row, "display_text"), key);
                string operand = Get(row, "item_operand_hex");
                string commandName = Get(row, "command_metadata_name");
                string summary = operand.Length == 0
                    ? $"{key}: {itemName}; item command metadata not joined for this row."
                    : $"{key}: {itemName} -> {operand} {commandName}; role {Get(row, "role_text")}.";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:item-shop:{SanitizeId(key)}",
                    SpiraDataAtlasDetailKind.ItemShopCommand,
                    $"{itemName} shop slot",
                    summary,
                    $"Shop slot key: {key}",
                    "shop",
                    CombineEvidence(Get(row, "evidence_badge"), Get(row, "evidence_health")),
                    Get(row, "writer_policy"),
                    $"item shop command metadata {itemName} {operand} {commandName}",
                    "work/spira_data_atlas_step0c_crosslinks_2026-06-08/item_shop_command_crosslink.csv"));
            }

            // Mix combinations (Jarvis-JECHT): the prepare.bin 112x112 result matrix is stored lower-triangular
            // (origin index O carries non-zero results only for partner slots 0..O), so each unordered item pair
            // {a,b} lives at exactly one canonical cell origin=max, partner=min. mix_combinations.csv is the
            // parser-canonical normalization of that table: 6,328 rows = 112*113/2, 0 duplicate keys, keyed
            // mix:0x{item1}+0x{item2} with item1>=item2 (the same Items game indices the editor shows, 0x2000+index).
            // result_hex is the raw little-endian result word the editor reads as MixResultRow.RawResult, so the
            // value-guard accessor (TryGetMixCombination) is a same-space integer compare. proved-candidate: stable
            // key + offline value-guard. The in-game mix effect is never claimed (RT2-pending). Dev work/ tree only
            // (no compiled fallback), like the Sphere Grid node detail: empty in a release build.
            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v5",
                         "mix_combinations.csv"))
            {
                string mixKey = Get(row, "mix_key");
                string resultHex = Get(row, "result_hex");
                if (mixKey.Length == 0 || resultHex.Length == 0)
                    continue;

                string item1Name = FirstNonEmpty(Get(row, "item1_name"), Get(row, "item1_hex"));
                string item2Name = FirstNonEmpty(Get(row, "item2_name"), Get(row, "item2_hex"));
                string resultName = FirstNonEmpty(Get(row, "result_name"), resultHex);
                string summary =
                    $"{item1Name} ({Get(row, "item1_hex")}) + {item2Name} ({Get(row, "item2_hex")}) = {resultName} ({resultHex}).";
                string detail =
                    $"Mix key: {mixKey}\nResult: {resultName} / {resultHex}\nPair order: {Get(row, "pair_order")}\n" +
                    $"Source: {Get(row, "source_path")}:{Get(row, "source_line")}";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:mix:{SanitizeId(mixKey)}",
                    SpiraDataAtlasDetailKind.MixCombination,
                    $"{item1Name} + {item2Name} = {resultName}",
                    summary,
                    detail,
                    "mix",
                    CombineEvidence(Get(row, "provenance_labels"), "parser-corpus", "proved-candidate", "read-only"),
                    "read-only;not-writer-authority;mix-effect-RT2-pending",
                    $"mix combination prepare.bin recipe {item1Name} {item2Name} {resultName} {mixKey}",
                    "work/step0_consolidated_v5/mix_combinations.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v5",
                         "blitzball_growth_event_refs.csv"))
            {
                string eventId = Get(row, "event_id");
                string variable = Get(row, "variable_hex");
                string stat = Get(row, "stat");
                if (eventId.Length == 0 || variable.Length == 0 || stat.Length == 0)
                    continue;

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:blitzball-event-ref:{SanitizeId(eventId)}:{SanitizeId(stat)}:{SanitizeId(variable)}",
                    SpiraDataAtlasDetailKind.BlitzballEventRef,
                    $"{eventId} Blitzball {stat} ref {variable}",
                    $"Blitzball event script {eventId} references growth stat {stat} through variable {variable}.",
                    $"Event key: {Get(row, "event_key")}\nSource: {Get(row, "source_path")}:{Get(row, "source_line")}",
                    "blitzball-event-ref",
                    Get(row, "provenance_labels"),
                    "read-only;Blitzball writer not implied",
                    $"blitzball event refs growth stat prize-pending {eventId} {stat} {variable}",
                    "work/step0_consolidated_v5/blitzball_growth_event_refs.csv"));
            }

            // Blitzball prize references: normalized read-only dataset (Jarvis-WAKKA).
            // Replaces the old raw bltz0200/0201 text scrape with the prize-index
            // resolution table (the stable key): prize+220 -> takara reward is an
            // offline triple-evidence rule = proved-candidate (Fahrenheit blitz_prize.cs
            // + bltz0201 obtainTreasure + bltz0200/0201 Treasure-Label display, over
            // RT0-proved takara). Tech = partial, overdrive = metadata-only. The
            // per-event prize VALUE is a runtime save-data variable, so script sites
            // stay blocked. Dev work/ tree only (no compiled fallback), like Sphere Grid.
            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_blitzball_prize_ref_2026-06-09",
                         "blitzball_prize_ref_catalog.csv"))
            {
                string prizeDec = Get(row, "prize_index_dec");
                string prizeHex = Get(row, "prize_index_hex");
                string kind = Get(row, "prize_ref_kind");
                if (prizeDec.Length == 0 || kind.Length == 0)
                    continue;

                string rewardName = Get(row, "candidate_reward_name");
                string rewardId = Get(row, "candidate_reward_id");
                string rewardDomain = Get(row, "candidate_reward_domain");
                string transform = Get(row, "transform");
                string takaraDec = Get(row, "takara_index_dec");

                (string evidence, string policy, string label) = kind switch
                {
                    "treasure" => (
                        "parser-corpus;event-atel-text;takara-rt0;proved-candidate;read-only",
                        "read-only;rule-offline-proved-candidate;per-event-prize-blocked-runtime;Blitzball writer not implied",
                        "proved-candidate"),
                    "tech" => (
                        "parser-corpus;fahrenheit-blitz_tech;partial;read-only",
                        "read-only;tech-name-metadata-only;Blitzball writer not implied",
                        "partial"),
                    "overdrive" => (
                        "metadata-only;fahrenheit-blitz_prize-comment;read-only",
                        "read-only;do-not-promote;Blitzball writer not implied",
                        "metadata-only"),
                    _ => (
                        "parser-corpus;read-only",
                        "read-only;Blitzball writer not implied",
                        "parser-corpus"),
                };

                string title = kind switch
                {
                    "treasure" => $"Blitzball prize {prizeHex} -> {rewardName}",
                    "tech" => $"Blitzball prize {prizeHex} -> tech {rewardName}",
                    "overdrive" => $"Blitzball prize {prizeHex} -> overdrive {rewardName}",
                    _ => $"Blitzball prize {prizeHex}",
                };
                string summary = kind == "treasure"
                    ? $"Prize index {prizeDec} ({prizeHex}) maps to takara {takaraDec} -> {rewardName} via {transform}. Offline rule = {label}; per-event prize value is runtime."
                    : $"Prize index {prizeDec} ({prizeHex}) -> {rewardDomain} {rewardName} via {transform}. {label}.";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:blitzball-prize:{kind}:{SanitizeId(prizeDec)}",
                    SpiraDataAtlasDetailKind.BlitzballPrizeRef,
                    title,
                    summary,
                    $"Reward id: {rewardId}\nTransform: {transform}\nSource: {Get(row, "source_event")} ({Get(row, "source_proof")})\nNotes: {Get(row, "notes")}",
                    "blitzball-prize",
                    evidence,
                    policy,
                    $"blitzball prize ref {kind} {prizeHex} {rewardName} takara obtainTreasure {rewardId}",
                    "work/step0_blitzball_prize_ref_2026-06-09/blitzball_prize_ref_catalog.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_blitzball_prize_ref_2026-06-09",
                         "blitzball_prize_ref_sites.csv"))
            {
                string eventId = Get(row, "event_id");
                string line = Get(row, "source_line");
                string siteKind = Get(row, "prize_ref_kind");
                if (eventId.Length == 0 || line.Length == 0)
                    continue;

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:blitzball-prize-site:{SanitizeId(eventId)}:{SanitizeId(line)}",
                    SpiraDataAtlasDetailKind.BlitzballPrizeRef,
                    $"{eventId} prize site {line} ({siteKind})",
                    $"{Get(row, "event_name")} references {Get(row, "prize_variable")} via {Get(row, "transform")}; per-event reward blocked (runtime save data).",
                    $"Room: {Get(row, "room_or_script")}\nVariable: {Get(row, "prize_variable")} ({Get(row, "prize_domain")}/{Get(row, "prize_scorer")})\nRaw: {Get(row, "text_context")}",
                    "blitzball-prize",
                    "parser-corpus;event-atel-text;blocked;read-only",
                    "read-only;blocked-per-event-runtime;Blitzball writer not implied",
                    $"blitzball prize site {eventId} {siteKind} {Get(row, "prize_variable")} runtime blocked",
                    "work/step0_blitzball_prize_ref_2026-06-09/blitzball_prize_ref_sites.csv"));
            }

            details.Add(new SpiraDataAtlasDetailEntry(
                "atlas:blitzball-prize-refs:guardrail",
                SpiraDataAtlasDetailKind.BlitzballPrizeGuardrail,
                "Blitzball prize refs: rule proved-candidate, per-event blocked",
                "The prize+220 -> takara treasure rule is an offline proved-candidate (Fahrenheit blitz_prize.cs + bltz0201 obtainTreasure + bltz0200/0201 Treasure-Label display, over RT0-proved takara). Tech = partial, overdrive = metadata-only.",
                "Read-only. The SPECIFIC prize a league/tournament awards depends on runtime save data (BlitzballLeague/TournamentPrizeIndex) and is blocked. Never claim RT2/in-game for Blitzball prizes here, and never promote a writer.",
                "blitzball-prize",
                "proved-candidate;metadata-only;blocked;read-only",
                "read-only;blocked-from-writer;blocked-from-SIN;rule-offline-proved-candidate",
                "blitzball prize refs guardrail proved-candidate blocked runtime per-event",
                "work/step0_blitzball_prize_ref_2026-06-09/blitzball_prize_ref_manifest.json"));

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "blitzball_growth_event_crosslink.csv"))
            {
                string key = Get(row, "player_key");
                if (key.Length == 0)
                    continue;

                string summary =
                    $"{key}: HP {Get(row, "hp_formula")}; SP {Get(row, "sp_formula")}; AT {Get(row, "at_formula")}; " +
                    $"event refs by stat {Get(row, "event_ref_counts_by_stat")}.";
                string detail =
                    $"EN {Get(row, "en_formula")}\nPA {Get(row, "pa_formula")}\nSH {Get(row, "sh_formula")}\nBL {Get(row, "bl_formula")}\nCA {Get(row, "ca_formula")}";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:blitzball:{SanitizeId(key)}",
                    SpiraDataAtlasDetailKind.BlitzballGrowthEvent,
                    $"Blitzball growth/event refs {Get(row, "player_id")}",
                    summary,
                    detail,
                    "blitzball",
                    CombineEvidence(Get(row, "evidence_badge"), Get(row, "evidence_health")),
                    Get(row, "writer_policy"),
                    $"blitzball growth event refs player {Get(row, "player_id")}",
                    "work/spira_data_atlas_step0c_crosslinks_2026-06-08/blitzball_growth_event_crosslink.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v6_p0_extras_2026-06-08",
                         "pc_save_stats_catalog.csv"))
            {
                string hex = Get(row, "index_hex");
                string name = Get(row, "character_name");
                if (hex.Length == 0 || name.Length == 0)
                    continue;

                string summary =
                    $"{name} ({hex}) base/current: HP {Get(row, "base_hp")}/{Get(row, "current_hp")}, MP {Get(row, "base_mp")}/{Get(row, "current_mp")}, " +
                    $"STR {Get(row, "base_str")}/{Get(row, "current_str")}, MAG {Get(row, "base_mag")}/{Get(row, "current_mag")}, AGI {Get(row, "base_agi")}/{Get(row, "current_agi")}.";
                string detail =
                    $"Base DEF/MDF/LCK/EVA/ACC: {Get(row, "base_def")}/{Get(row, "base_mdf")}/{Get(row, "base_lck")}/{Get(row, "base_eva")}/{Get(row, "base_acc")}.\n" +
                    $"Current DEF/MDF/LCK/EVA/ACC: {Get(row, "current_def")}/{Get(row, "current_mdf")}/{Get(row, "current_lck")}/{Get(row, "current_eva")}/{Get(row, "current_acc")}.\n" +
                    $"Poison damage {Get(row, "poison_damage_percent")}%; Overdrive {Get(row, "current_overdrive")}/{Get(row, "max_overdrive")} mode {Get(row, "overdrive_mode")} 0x{Get(row, "overdrive_mode_hex")}.\n" +
                    $"Sphere levels current/used: {Get(row, "current_sphere_levels")}/{Get(row, "used_sphere_levels")}.\n" +
                    $"Abilities: {Get(row, "abilities")}.\nAlready learned: {Get(row, "already_learned")}.";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:pc-aeon-fine:{hex.ToLowerInvariant()}",
                    SpiraDataAtlasDetailKind.PcAeonFineStat,
                    $"{name} fine PC/Aeon stats ({hex})",
                    summary,
                    detail,
                    "pc-aeon-fine",
                    Get(row, "evidence"),
                    Get(row, "writer_policy"),
                    $"pc aeon fine stats abilities overdrive sphere levels {name} {hex}",
                    "work/step0_consolidated_v6_p0_extras_2026-06-08/pc_save_stats_catalog.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "pc_aeon_stats_crosslink.csv"))
            {
                string hex = Get(row, "character_index_hex");
                if (hex.Length == 0)
                    continue;

                string name = Get(row, "character_name");
                string summary =
                    $"{name} ({hex}): HP {Get(row, "base_hp")}, MP {Get(row, "base_mp")}, STR {Get(row, "base_str")}, MAG {Get(row, "base_mag")}, " +
                    $"overdrive {Get(row, "current_overdrive")}/{Get(row, "max_overdrive")} mode {Get(row, "overdrive_mode")}.";
                string detail =
                    $"Aeon growth formulas: {Get(row, "aeon_growth_formula_count")}; overdrive requirement rows: {Get(row, "overdrive_requirement_count")}.";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:pc-aeon:{hex.ToLowerInvariant()}",
                    SpiraDataAtlasDetailKind.PcAeonStats,
                    $"PC/Aeon stats: {name} ({hex})",
                    summary,
                    detail,
                    "pc-aeon-stats",
                    CombineEvidence(Get(row, "evidence_badge"), Get(row, "evidence_health")),
                    Get(row, "writer_policy"),
                    $"pc aeon stats overdrive growth {name} {hex}",
                    "work/spira_data_atlas_step0c_crosslinks_2026-06-08/pc_aeon_stats_crosslink.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v6_p0_extras_2026-06-08",
                         "sphere_grid_layout_nodes_catalog.csv"))
            {
                string layout = Get(row, "layout");
                string nodeIndex = Get(row, "node_index");
                if (layout.Length == 0 || nodeIndex.Length == 0)
                    continue;

                string nodeName = Get(row, "node_name");
                string summary =
                    $"{layout} node {nodeIndex}: {nodeName} type 0x{Get(row, "node_type_hex")} at ({Get(row, "x")},{Get(row, "y")}), cluster {Get(row, "cluster_index")}.";
                string detail =
                    $"Unknown6: 0x{Get(row, "unknown6_hex")} / {Get(row, "unknown6_dec")}.\nRaw: {Get(row, "raw_line")}";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:sphere-grid-node:{layout.ToLowerInvariant()}:{SanitizeId(nodeIndex)}",
                    SpiraDataAtlasDetailKind.SphereGridNode,
                    $"{layout} node {nodeIndex}: {nodeName}",
                    summary,
                    detail,
                    "sphere-grid-node",
                    Get(row, "evidence"),
                    Get(row, "writer_policy"),
                    $"sphere grid node layout topology panel {layout} {nodeIndex} {nodeName}",
                    "work/step0_consolidated_v6_p0_extras_2026-06-08/sphere_grid_layout_nodes_catalog.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "sphere_grid_health_crosslink.csv"))
            {
                string layout = Get(row, "layout");
                if (layout.Length == 0)
                    continue;

                string summary =
                    $"{layout}: {Get(row, "node_count")} nodes, {Get(row, "link_count")} links, {Get(row, "unresolved_node_count")} unresolved node type(s).";
                string detail = FirstNonEmpty(Get(row, "unresolved_examples"), "No unresolved node type examples.");

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:sphere-grid:{layout.ToLowerInvariant()}",
                    SpiraDataAtlasDetailKind.SphereGridHealth,
                    $"Sphere Grid health: {layout}",
                    summary,
                    detail,
                    "sphere-grid",
                    CombineEvidence(Get(row, "evidence_badge"), Get(row, "evidence_health")),
                    Get(row, "writer_policy"),
                    $"sphere grid layout topology health {layout}",
                    "work/spira_data_atlas_step0c_crosslinks_2026-06-08/sphere_grid_health_crosslink.csv"));
            }

            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_unknown_health_2026-06-08",
                         "atlas_unknowns_catalog.csv"))
            {
                string key = Get(row, "record_key");
                string kind = Get(row, "unknown_kind");
                string domain = Get(row, "domain");
                if (key.Length == 0 || kind.Length == 0)
                    continue;

                string title = $"{kind}: {FirstNonEmpty(Get(row, "display_name"), key)}";
                string summary = $"{Get(row, "issue")} Recommended: {Get(row, "recommended_action")}";
                string detail =
                    $"Dataset: {Get(row, "dataset")}; record: {key}; source: {Get(row, "source_detail")}.\nRaw: {Get(row, "raw_value")}";

                string id = UniqueDetailId(details, $"atlas:unknown:{SanitizeId(domain)}:{SanitizeId(kind)}:{SanitizeId(key)}");
                details.Add(new SpiraDataAtlasDetailEntry(
                    id,
                    SpiraDataAtlasDetailKind.UnknownHealth,
                    title,
                    summary,
                    detail,
                    domain,
                    Get(row, "evidence_badge"),
                    Get(row, "writer_policy"),
                    $"unknown health evidence {kind} {domain} {key} {Get(row, "display_name")}",
                    "work/spira_data_atlas_unknown_health_2026-06-08/atlas_unknowns_catalog.csv"));
            }

            // Aeon growth/customization recipes from sum_grow.bin — Aeon-ONLY (Target 0x007F in 77/77; no PC rows,
            // no Sphere-Grid rows). COMPILED, byte-grounded table (SumGrowAeonRecipeData), so these exist in ANY
            // build WITHOUT a work/ tree, unlike the CSV-backed details above. Names/labels are computed by the
            // editor's own AeonCustomizationEntry + dictionaries (single source of truth, RT0-proved by
            // --customization-rt0), never re-invented here. Read-only — never a writer, never SIN.
            foreach (SumGrowAeonRecipeRow r in SumGrowAeonRecipeData.Rows)
            {
                AeonCustomizationEntry entry = new()
                {
                    Index = r.Index,
                    Target = r.Target,
                    Result = r.Result,
                    Item = r.Item,
                    PrimaryValue = r.Primary,
                    SecondaryValue = r.Secondary,
                };

                bool isStat = entry.IsStatRecipe;
                string kindLabel = isStat ? "stat" : "ability";
                string resultName = isStat
                    ? CustomizationNaming_Util.ResolveStatLabel(entry.Result)
                    : CustomizationNaming_Util.ResolveGameLabel(entry.Result);
                string itemName = CustomizationNaming_Util.ResolveGameLabel(entry.Item);

                string title = isStat
                    ? $"Aeon growth: {resultName} +{entry.PrimaryValue} (via {itemName})"
                    : $"Aeon growth: teach {resultName} (via {entry.PrimaryValue}x {itemName})";
                string summary = $"{entry.Summary} - sum_grow.bin entry {r.Index} (0x{r.Index:X2}).";
                string detail =
                    $"Target: {entry.TargetLabel} (0x{entry.Target:X4})\n" +
                    $"Result: {entry.ResultLabel} - raw 0x{entry.Result:X4} (category {entry.ResultCategory}, index {entry.ResultIndex})\n" +
                    $"Item: {itemName} - raw 0x{entry.Item:X4} (category {entry.ItemCategory}, index {entry.ItemIndex})\n" +
                    $"Primary={entry.PrimaryValue}, Secondary={entry.SecondaryValue} ({kindLabel} recipe).\n" +
                    (isStat
                        ? "Cost semantics: partial - editor renders \"Primary x item\"; in-game cost is 1 item/application (Primary is the per-application stat increment, Secondary=1 is the recipe flag)."
                        : "Cost: Primary x item (Secondary=0).");

                string evidence = "rt0-structure;editor-dict-resolved;parser-corpus-coherent;locale-invariant;read-only;proved-candidate";
                if (isStat)
                    evidence += ";cost-semantics-partial";
                string policy = isStat
                    ? "read-only;Aeon stat growth;cost-semantics-partial;writer not implied"
                    : "read-only;Aeon ability teach;writer not implied";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:aeon-growth:{r.Index}",
                    SpiraDataAtlasDetailKind.AeonGrowthRecipe,
                    title,
                    summary,
                    detail,
                    "aeon-growth",
                    evidence,
                    policy,
                    $"aeon growth sum_grow recipe {kindLabel} {resultName} {itemName} entry {r.Index} 0x{r.Index:X2}",
                    "FFXProjectEditor/FfxLib/SpiraDataAtlas/SumGrowAeonRecipeData.cs (compiled from sum_grow.bin)"));
            }

            // PC/Aeon player growth + stats from ply_save.bin + ply_rom.bin (Jarvis-TIDUS). 20 slots: 0-6 PC,
            // 7 guest (Seymour), 8-17 Aeons, 18-19 unmapped. COMPILED, byte-grounded table (PlayerGrowthData), so
            // these exist in ANY build WITHOUT a work/ tree. REGION-SPECIFIC (new_uspc, the region the editor loads)
            // - ply_save/ply_rom are NOT locale-invariant, unlike sum_grow.bin. Honesty: the per-stat auto-growth
            // coefficients are Aeon-only (PCs grow stats via the Sphere Grid); for PC slots the only growth knob in
            // this ROM is the AP-requirement curve. Names resolve at runtime via Character_Enum. Read-only.
            foreach (PlayerGrowthRow r in PlayerGrowthData.Rows)
            {
                bool isPc = r.Index <= 6;
                bool isGuest = r.Index == 7;
                bool isAeon = r.Index >= 8 && r.Index <= 17;
                bool isUnmapped = r.Index >= 18;
                string label = Enum.IsDefined(typeof(FFXProjectEditor.FfxLib.Dictionaries.Character_Enum), (sbyte)r.Index)
                    ? ((FFXProjectEditor.FfxLib.Dictionaries.Character_Enum)(sbyte)r.Index).ToString()
                    : $"Unknown Slot {r.Index:D2}";
                string slotKind = isPc ? "PC" : isGuest ? "guest" : isAeon ? "Aeon" : "unmapped";
                string apCurve = $"AP req {r.ApReqA}A/{r.ApReqB}B/{r.ApReqC}C (max {r.ApReqMax})";

                string title = $"Player growth: {label} (#{r.Index:D2}, {slotKind})";
                string summary = isAeon || isUnmapped
                    ? $"{label}: Aeon auto-growth coefficients HP {r.HpA}/{r.HpB}, STR {r.StrA}/{r.StrB}, MAG {r.MagA}/{r.MagB}; {apCurve}."
                    : $"{label}: base HP/MP {r.BaseHp}/{r.BaseMp}, STR {r.BaseStr}, MAG {r.BaseMag}, AGI {r.BaseAgi}; {apCurve}.";

                string detail =
                    $"Slot {r.Index} (0x{r.Index:X2}) - {slotKind}. Source region: {PlayerGrowthData.SourceRegion} (ply_save.bin + ply_rom.bin).\n" +
                    $"ply_save base stats: HP {r.BaseHp}, MP {r.BaseMp}, STR {r.BaseStr}, DEF {r.BaseDef}, MAG {r.BaseMag}, MDF {r.BaseMdf}, AGI {r.BaseAgi}, LCK {r.BaseLck}, EVA {r.BaseEva}, ACC {r.BaseAcc}.\n" +
                    $"ply_save snapshot (kernel defaults): MaxHP/MaxMP {r.MaxHp}/{r.MaxMp}, overdrive mode {r.OdMode}, sphere levels {r.SphAvail}/{r.SphUsed} avail/used.\n" +
                    $"ply_rom AP curve: A={r.ApReqA}, B={r.ApReqB}, C={r.ApReqC}, max={r.ApReqMax} (in-game AP-per-level formula RT2-pending).\n" +
                    $"ply_rom stat-growth coefficients (A/B): HP {r.HpA}/{r.HpB}, MP {r.MpA}/{r.MpB}, STR {r.StrA}/{r.StrB}, DEF {r.DefA}/{r.DefB}, MAG {r.MagA}/{r.MagB}, MDF {r.MdfA}/{r.MdfB}, AGI {r.AgiA}/{r.AgiB}, EVA {r.EvaA}/{r.EvaB}, ACC {r.AccA}/{r.AccB}.\n" +
                    (isPc || isGuest
                        ? "PC/guest slot: stat-growth coefficients are zero - PCs grow stats via the Sphere Grid, not these coefficients; the AP-requirement curve is the only growth knob in this ROM."
                        : isAeon
                            ? "Aeon slot: stat-growth coefficients drive auto-leveling. This is a DIFFERENT axis from sum_grow.bin (Aeon customization recipes); growth formula RT2-pending."
                            : "Unmapped slot (no Character_Enum mapping): raw bytes preserved, treated as metadata-only.");

                string evidence = isUnmapped
                    ? "rt0-structure;parser-corpus;byte-grounded;region-uspc-not-locale-invariant;read-only;metadata-only;unmapped-slot"
                    : isAeon
                        ? "rt0-structure;parser-corpus;byte-grounded;region-uspc-not-locale-invariant;read-only;proved-candidate;aeon-auto-growth-coef;growth-formula-rt2-pending"
                        : "rt0-structure;parser-corpus;byte-grounded;region-uspc-not-locale-invariant;read-only;proved-candidate;pc-growth-ap-curve;stat-coef-aeon-only;ap-formula-rt2-pending";

                string policy = "read-only;PlayerGrowth writer is a separate module;writer not implied;never SIN";

                details.Add(new SpiraDataAtlasDetailEntry(
                    $"atlas:player-stat:{r.Index}",
                    SpiraDataAtlasDetailKind.PlayerGrowthStat,
                    title,
                    summary,
                    detail,
                    "player-growth",
                    evidence,
                    policy,
                    $"player growth stats pc aeon {label} slot {r.Index} 0x{r.Index:X2} {slotKind} ap curve coefficient ply_save ply_rom",
                    "FFXProjectEditor/FfxLib/SpiraDataAtlas/PlayerGrowthData.cs (compiled from ply_save.bin + ply_rom.bin, new_uspc)"));
            }

            return details
                .OrderBy(d => d.Kind)
                .ThenBy(d => d.Domain, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static IReadOnlyDictionary<string, SpiraCommandCrosslink> LoadCommandCrosslinks()
        {
            Dictionary<string, SpiraCommandCrosslink> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "command_ai_site_crosslink.csv"))
            {
                string operand = NormalizeHex(Get(row, "operand_hex"), 4);
                if (operand.Length == 0)
                    continue;

                map[operand] = new SpiraCommandCrosslink(
                    operand,
                    Get(row, "display_name"),
                    Get(row, "category"),
                    ParseInt(Get(row, "command_site_count")),
                    ParseInt(Get(row, "monster_site_count")),
                    ParseInt(Get(row, "battle_site_count")),
                    ParseInt(Get(row, "event_site_count")),
                    Get(row, "semantic_kinds"),
                    Get(row, "site_examples"),
                    Get(row, "sin_eligibility"),
                    Get(row, "evidence_health"));
            }

            return map.Count > 0 ? map : BuiltInCommandCrosslinks();
        }

        // Read-only corpus slot-value index for the gear-shop value-guard. SINGLE SOURCE: reads slot_value_hex from the
        // SAME csv (gear_reward_shop_crosslink.csv, keyed by source_key) that builds the atlas:gear gear-shop detail, so
        // the value and the detail can never drift (mirror of the item-shop consolidation). The crosslink also holds the
        // 82 treasure-buki-get rows, which carry slot_value_hex = "" and are skipped here, leaving only the gear-shop
        // slots. slot_value_hex is the raw little-endian word the editor reads (the shop_arms.bin catalog index), so the
        // guard is a same-space integer compare. No compiled fallback (dev work/ tree only, like the Sphere Grid node
        // detail): in a release build this is empty and the badge simply never shows.
        static IReadOnlyDictionary<string, ushort> LoadShopGearSlotValues()
        {
            Dictionary<string, ushort> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "gear_reward_shop_crosslink.csv"))
            {
                string key = Get(row, "source_key");
                string norm = NormalizeHex(Get(row, "slot_value_hex"), 4);
                if (key.Length == 0 || norm.Length == 0)
                    continue;

                map[SanitizeId(key)] = Convert.ToUInt16(norm[2..], 16);
            }

            return map;
        }

        // Read-only corpus slot-value index for the item-shop value-guard. SINGLE SOURCE: reads slot_value_hex from the
        // SAME csv (item_shop_command_crosslink.csv) that builds the atlas:item-shop detail, so the value and the detail
        // can never drift. slot_value_hex is the raw little-endian word the editor reads (the encoded Items game-index,
        // 0x2xxx), byte-grounded 1:1 against item_shop.bin. No compiled fallback (dev work/ tree only, like the Sphere
        // Grid node detail): in a release build this is empty and the item badge simply never shows.
        static IReadOnlyDictionary<string, ushort> LoadShopItemSlotValues()
        {
            Dictionary<string, ushort> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "spira_data_atlas_step0c_crosslinks_2026-06-08",
                         "item_shop_command_crosslink.csv"))
            {
                string key = Get(row, "shop_slot_key");
                string norm = NormalizeHex(Get(row, "slot_value_hex"), 4);
                if (key.Length == 0 || norm.Length == 0)
                    continue;

                map[SanitizeId(key)] = Convert.ToUInt16(norm[2..], 16);
            }

            return map;
        }

        // Read-only corpus result-value index for the Mix value-guard. SINGLE SOURCE: reads result_hex from the
        // SAME csv (mix_combinations.csv) that builds the atlas:mix detail, so the value and the detail can never
        // drift (mirror of the shop consolidations). result_hex is the raw little-endian result word the editor reads
        // as MixResultRow.RawResult, so the guard is a same-space integer compare. No compiled fallback (dev work/
        // tree only, like the Sphere Grid node detail): in a release build this is empty and the mix badge never shows.
        static IReadOnlyDictionary<string, ushort> LoadMixResultValues()
        {
            Dictionary<string, ushort> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v5",
                         "mix_combinations.csv"))
            {
                string key = Get(row, "mix_key");
                string norm = NormalizeHex(Get(row, "result_hex"), 4);
                if (key.Length == 0 || norm.Length == 0)
                    continue;

                map[SanitizeId(key)] = Convert.ToUInt16(norm[2..], 16);
            }

            return map;
        }

        static Dictionary<string, EncounterFormationAccumulator> BuildEncounterFormationLookup()
        {
            Dictionary<string, EncounterFormationAccumulator> byFormation = new(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, string> row in ReadCsvFromKnownOutput(
                         "step0_consolidated_v6_p0_extras_2026-06-08",
                         "encounter_formation_catalog.csv"))
            {
                string formation = Get(row, "formation");
                if (formation.Length == 0)
                    continue;

                if (!byFormation.TryGetValue(formation, out EncounterFormationAccumulator? acc))
                {
                    acc = new EncounterFormationAccumulator();
                    byFormation[formation] = acc;
                }

                acc.ReferenceCount++;
                string example =
                    $"table {Get(row, "table_index_hex")}/group {Get(row, "group_index")}/formation {Get(row, "formation_index")}" +
                    $" battlefield {Get(row, "battlefield")} danger {Get(row, "danger")}";
                if (!string.IsNullOrWhiteSpace(Get(row, "weight")))
                    example += $" weight {Get(row, "weight")}/{Get(row, "denominator")}";
                acc.Examples.Add(example);
            }

            return byFormation;
        }

        static IReadOnlyDictionary<string, SpiraCommandCrosslink> BuiltInCommandCrosslinks()
        {
            SpiraCommandCrosslink[] rows =
            {
                new("0x4000", "Attack", "MonsterMagic1", 77, 77, 0, 0, "perform-command", "monster:m001:0101; monster:m002:0101", "eligible-after-template-gates", "ok"),
                new("0x3047", "Thundara", "Character", 46, 46, 0, 0, "perform-command", "monster command-site summary", "eligible-after-template-gates", "ok"),
                new("0x3003", "Escape", "Character", 254, 8, 246, 0, "perform-command", "mixed battle/monster command sites", "eligible-after-template-gates", "ok"),
            };
            return rows.ToDictionary(r => r.OperandHex, StringComparer.OrdinalIgnoreCase);
        }

        static IEnumerable<SpiraDataAtlasEntry> BuiltInEntries()
        {
            yield return new SpiraDataAtlasEntry(
                "atlas:master",
                SpiraDataAtlasEntryKind.Dataset,
                "Spira Data Atlas master",
                "54 datasets/layers and 4,762,824 rows from FFXDataParser, editor dictionaries, locale layer, ATEL granular pass and semantic pass.",
                "atlas",
                54,
                "parser-corpus;metadata-only;presence-index",
                "read-only atlas; no writer promotion from this dataset alone",
                "bible atlas master parser-corpus",
                "work/spira_data_atlas_master_2026-06-08/spira_data_atlas_master_manifest.csv");

            yield return new SpiraDataAtlasEntry(
                "atlas:command-sites",
                SpiraDataAtlasEntryKind.Crosslink,
                "Command -> metadata -> ATEL sites",
                "979 command/magic rows joined to ATEL command sites. SIN eligibility follows AiCommandMetadata: Character/MonsterMagic categories are payload candidates; Item remains metadata-only.",
                "battle-kernel",
                979,
                "parser-corpus;AiCommandMetadata;step0c-crosslink",
                "read-only; SIN must still pass AiScriptLab, AEON diff, backup and RT2 gates",
                "bible command AiCommandMetadata SIN",
                "work/spira_data_atlas_step0c_crosslinks_2026-06-08/command_ai_site_crosslink.csv");

            yield return new SpiraDataAtlasEntry(
                "atlas:field-call-shapes",
                SpiraDataAtlasEntryKind.Crosslink,
                "Field writes by call shape",
                "ATEL semantic pass classified 33,167 writeChrProperty actor/field/value writes, 4,150 setStatField no-explicit-actor writes, 2,624 setMotionField writes, 116 btlSetMotionData (0x70A8) explicit-actor motion-data writes and 8 not-a-field-write rows (0x7032 setActorFacingAngle); 0 unknown call-shape rows remain after the SEYMOUR 2026-06-10 audit.",
                "atel",
                40065,
                "semantic-candidate;field-call-context",
                "all call-shapes are now classified (SEYMOUR 2026-06-10); field writes stay read-only, never writer authority",
                "bible fields call-shape guardrail setStatField writeChrProperty",
                "work/step0_consolidated_v8_atel_semantic_2026-06-08/atel_field_write_semantic_catalog.csv");

            yield return new SpiraDataAtlasEntry(
                "atlas:monster-where-appears",
                SpiraDataAtlasEntryKind.Crosslink,
                "Monster -> AI -> battle -> encounter",
                "361 monster rows joined to localization, AI pattern summaries, battle formation appearances and encounter formation references.",
                "monster-corpus",
                361,
                "parser-corpus;metadata-only;step0c-crosslink",
                "read-only; Monster/AI/Encounter writers own edits and gates",
                "bible monster where appears encounter battle ai",
                "work/spira_data_atlas_step0c_crosslinks_2026-06-08/monster_ai_battle_encounter_crosslink.csv");

            yield return new SpiraDataAtlasEntry(
                "atlas:atel-workers-branches",
                SpiraDataAtlasEntryKind.Crosslink,
                "ATEL workers, branches and command sites",
                "Granular ATEL pass indexed 3,283 workers, 405,978 branch/control-flow rows and 2,959 command operand sites across monster, battle and event target text.",
                "atel",
                411220,
                "ffxdataparser-target-text-corpus;presence-index;metadata-only",
                "read-only; branch authoring still goes through AiScriptLab/AEON gates",
                "bible workers branches command-sites atel",
                "work/step0_consolidated_v7_atel_granular_2026-06-08");

            yield return new SpiraDataAtlasEntry(
                "atlas:unknown-health",
                SpiraDataAtlasEntryKind.UnknownHealth,
                "Unknown / Evidence Health pass",
                "75 health findings (the 124 ATEL call-shapes were resolved by the SEYMOUR audit 2026-06-10: 116 motion-data 0x70A8 + 8 not-a-field-write 0x7032): 47 U+FFFD command text rows, 21 Sphere item enum issues, 4 PC/Aeon placeholders, 2 ESG unresolved node types and 1 reserved Unknown46 field.",
                "atlas-health",
                75,
                "atlas-health;read-only",
                "diagnostic only; blocked-from-writer where noted",
                "bible health unknown evidence badges blocked",
                "work/spira_data_atlas_unknown_health_2026-06-08");
        }

        static int Score(SpiraDataAtlasEntry entry, string[] terms)
        {
            int score = 0;
            foreach (string rawTerm in terms)
            {
                string term = rawTerm.Trim();
                if (term.Length == 0) continue;

                int termScore = ScoreTerm(entry, term);
                if (termScore < 0)
                    return -1;
                score += termScore;
            }
            return score;
        }

        static int Score(SpiraDataAtlasDetailEntry entry, string[] terms)
        {
            int score = 0;
            foreach (string rawTerm in terms)
            {
                string term = rawTerm.Trim();
                if (term.Length == 0) continue;

                int termScore = ScoreTerm(entry, term);
                if (termScore < 0)
                    return -1;
                score += termScore;
            }
            return score;
        }

        static int ScoreTerm(SpiraDataAtlasEntry entry, string term)
        {
            if (term.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
            {
                string domain = term["domain:".Length..];
                if (domain.Length == 0)
                    return -1;
                if (entry.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase))
                    return 150;
                if (entry.Domain.Contains(domain, StringComparison.OrdinalIgnoreCase))
                    return 80;
                return -1;
            }
            if (term.StartsWith("kind:", StringComparison.OrdinalIgnoreCase))
            {
                string kind = term["kind:".Length..];
                if (kind.Length == 0)
                    return -1;
                if (entry.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase))
                    return 140;
                return -1;
            }

            if (entry.Id.Equals(term, StringComparison.OrdinalIgnoreCase)) return 160;
            if (entry.Kind.ToString().Equals(term, StringComparison.OrdinalIgnoreCase)) return 120;
            if (entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) return 90;
            if (entry.Domain.Contains(term, StringComparison.OrdinalIgnoreCase)) return 70;
            if (entry.Tags.Contains(term, StringComparison.OrdinalIgnoreCase)) return 65;
            if (entry.Summary.Contains(term, StringComparison.OrdinalIgnoreCase)) return 45;
            if (entry.Evidence.Contains(term, StringComparison.OrdinalIgnoreCase)) return 30;
            if (entry.WriterPolicy.Contains(term, StringComparison.OrdinalIgnoreCase)) return 30;
            if (entry.SearchBlob.Contains(term, StringComparison.OrdinalIgnoreCase)) return 10;
            return -1;
        }

        static int ScoreTerm(SpiraDataAtlasDetailEntry entry, string term)
        {
            if (term.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
            {
                string domain = term["domain:".Length..];
                if (domain.Length == 0)
                    return -1;
                if (entry.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase))
                    return 170;
                if (entry.Domain.Contains(domain, StringComparison.OrdinalIgnoreCase))
                    return 90;
                return -1;
            }
            if (term.StartsWith("kind:", StringComparison.OrdinalIgnoreCase))
            {
                string kind = term["kind:".Length..];
                if (kind.Length == 0)
                    return -1;
                if (entry.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase))
                    return 145;
                return -1;
            }
            if ((term.Equals("worker", StringComparison.OrdinalIgnoreCase) || term.Equals("workers", StringComparison.OrdinalIgnoreCase))
                && entry.Kind == SpiraDataAtlasDetailKind.AtelWorker)
                return 150;
            if ((term.Equals("field-shape", StringComparison.OrdinalIgnoreCase) || term.Equals("fieldshape", StringComparison.OrdinalIgnoreCase))
                && entry.Kind == SpiraDataAtlasDetailKind.AtelFieldWriteShape)
                return 150;
            if ((term.Equals("branch", StringComparison.OrdinalIgnoreCase) || term.Equals("branches", StringComparison.OrdinalIgnoreCase))
                && entry.Kind == SpiraDataAtlasDetailKind.AtelBranchShape)
                return 150;

            if (entry.Id.Equals(term, StringComparison.OrdinalIgnoreCase)) return 180;
            if (entry.Kind.ToString().Equals(term, StringComparison.OrdinalIgnoreCase)) return 125;
            if (entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) return 95;
            if (entry.Domain.Contains(term, StringComparison.OrdinalIgnoreCase)) return 75;
            if (entry.Tags.Contains(term, StringComparison.OrdinalIgnoreCase)) return 70;
            if (entry.Summary.Contains(term, StringComparison.OrdinalIgnoreCase)) return 50;
            if (entry.Detail.Contains(term, StringComparison.OrdinalIgnoreCase)) return 40;
            if (entry.Evidence.Contains(term, StringComparison.OrdinalIgnoreCase)) return 30;
            if (entry.WriterPolicy.Contains(term, StringComparison.OrdinalIgnoreCase)) return 30;
            if (entry.SearchBlob.Contains(term, StringComparison.OrdinalIgnoreCase)) return 10;
            return -1;
        }

        static IEnumerable<Dictionary<string, string>> ReadCsvFromKnownOutput(string outputDirName, string fileName)
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null)
                yield break;

            string path = Path.Combine(repoRoot, "work", outputDirName, fileName);
            if (!File.Exists(path))
                yield break;

            foreach (Dictionary<string, string> row in ReadCsvRows(path))
                yield return row;
        }

        static IEnumerable<Dictionary<string, string>> ReadCsvRows(string path)
        {
            using StreamReader reader = new(path);
            using IEnumerator<string> records = ReadCsvRecords(reader).GetEnumerator();
            if (!records.MoveNext())
                yield break;

            string? headerLine = records.Current;
            if (headerLine == null)
                yield break;

            string[] headers = ParseCsvLine(headerLine).ToArray();
            while (records.MoveNext())
            {
                string? line = records.Current;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] values = ParseCsvLine(line).ToArray();
                Dictionary<string, string> row = new(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Length; i++)
                    row[headers[i]] = i < values.Length ? values[i] : "";
                yield return row;
            }
        }

        static IEnumerable<string> ReadLinesFromWork(string relativePath)
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null)
                yield break;

            string path = Path.Combine(repoRoot, "work", relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                yield break;

            foreach (string line in File.ReadLines(path))
                yield return line;
        }

        static IEnumerable<string> ReadCsvRecords(TextReader reader)
        {
            System.Text.StringBuilder current = new();
            bool quoted = false;

            while (true)
            {
                int next = reader.Read();
                if (next < 0)
                    break;

                char c = (char)next;
                if (c == '"')
                {
                    current.Append(c);
                    if (quoted && reader.Peek() == '"')
                    {
                        current.Append((char)reader.Read());
                        continue;
                    }

                    quoted = !quoted;
                    continue;
                }

                if ((c == '\r' || c == '\n') && !quoted)
                {
                    if (c == '\r' && reader.Peek() == '\n')
                        reader.Read();

                    yield return current.ToString();
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
                yield return current.ToString();
        }

        static IEnumerable<string> ParseCsvLine(string line)
        {
            List<string> values = new();
            bool quoted = false;
            System.Text.StringBuilder current = new();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                    continue;
                }

                if (c == ',' && !quoted)
                {
                    values.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            values.Add(current.ToString());
            return values;
        }

        static string? FindRepoRoot()
        {
            string[] starts =
            {
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
            };

            foreach (string start in starts)
            {
                DirectoryInfo? dir = new(start);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "tools", "ffxdataparser_bridge"))
                        && File.Exists(Path.Combine(dir.FullName, "FFXProjectEditor", "FFXProjectEditor.csproj")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }

            return null;
        }

        static string Get(IReadOnlyDictionary<string, string> row, string key) =>
            row.TryGetValue(key, out string? value) ? value : "";

        sealed class FieldWriteShapeAccumulator
        {
            public FieldWriteShapeAccumulator(string callIdHex, string fieldHex, string fieldName, string semanticKind, string fieldCategory)
            {
                CallIdHex = callIdHex;
                FieldHex = fieldHex;
                FieldName = fieldName;
                SemanticKind = semanticKind;
                FieldCategory = fieldCategory;
            }

            public string CallIdHex { get; }
            public string FieldHex { get; }
            public string FieldName { get; }
            public string SemanticKind { get; }
            public string FieldCategory { get; }
            public HashSet<string> SourceKinds { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> EvidenceBadges { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int Count { get; set; }
            public string RiskNote { get; set; } = "";
            public string Example { get; set; } = "";
        }

        sealed class BranchShapeAccumulator
        {
            public BranchShapeAccumulator(string sourceKind, string branchKind, string primarySemantic, string semanticTags)
            {
                SourceKind = sourceKind;
                BranchKind = branchKind;
                PrimarySemantic = primarySemantic;
                SemanticTags = semanticTags;
            }

            public string SourceKind { get; }
            public string BranchKind { get; }
            public string PrimarySemantic { get; }
            public string SemanticTags { get; }
            public int Count { get; set; }
            public HashSet<string> SourceKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Functions { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> EvidenceBadges { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string Example { get; set; } = "";
        }

        sealed class EncounterFormationAccumulator
        {
            public int ReferenceCount { get; set; }
            public HashSet<string> Examples { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string ExamplesText => string.Join("; ", Examples.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).Take(4));
        }

        static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

        static string CombineEvidence(params string[] values) =>
            string.Join(";", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));

        static string CombineEvidence(IEnumerable<string> values) =>
            string.Join(";", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));

        static void AddEvidence(ISet<string> target, string value)
        {
            foreach (string part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                target.Add(part);
        }

        static string TrimJsonishList(string value) =>
            value.Replace("[", "", StringComparison.Ordinal)
                .Replace("]", "", StringComparison.Ordinal)
                .Replace("\"", "", StringComparison.Ordinal)
                .Trim();

        static string ExtractFirstToken(string text, string prefix, string requiredSuffix)
        {
            foreach (string rawPart in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string part = rawPart.Trim('(', ')', '[', ']', ',', ';', '.', ':');
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && part.Contains(requiredSuffix, StringComparison.OrdinalIgnoreCase))
                    return part;
            }

            return "";
        }

        static int ParseInt(string value) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

        static string GuessDomain(string dataset)
        {
            if (dataset.Contains("monster", StringComparison.OrdinalIgnoreCase)) return "monster-corpus";
            if (dataset.Contains("command", StringComparison.OrdinalIgnoreCase)) return "battle-kernel";
            if (dataset.Contains("gear", StringComparison.OrdinalIgnoreCase) || dataset.Contains("shop", StringComparison.OrdinalIgnoreCase)) return "gear";
            if (dataset.Contains("blitz", StringComparison.OrdinalIgnoreCase)) return "blitzball";
            if (dataset.Contains("pc_", StringComparison.OrdinalIgnoreCase) || dataset.Contains("aeon", StringComparison.OrdinalIgnoreCase)) return "pc-aeon-stats";
            if (dataset.Contains("sphere", StringComparison.OrdinalIgnoreCase)) return "sphere-grid";
            return "atlas";
        }

        static string NormalizeHex(string value, int width)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string text = value.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            text = text.TrimEnd('h', 'H');
            if (text.Length == 0 || text.Any(c => !Uri.IsHexDigit(c)))
                return "";

            return $"0x{text.ToUpperInvariant().PadLeft(width, '0')}";
        }

        static string UniqueDetailId(IReadOnlyCollection<SpiraDataAtlasDetailEntry> details, string baseId)
        {
            if (!details.Any(d => string.Equals(d.Id, baseId, StringComparison.OrdinalIgnoreCase)))
                return baseId;

            int suffix = 2;
            string candidate;
            do
            {
                candidate = $"{baseId}-{suffix}";
                suffix++;
            }
            while (details.Any(d => string.Equals(d.Id, candidate, StringComparison.OrdinalIgnoreCase)));

            return candidate;
        }

        static string NormalizeMonsterId(string value)
        {
            string text = (value ?? "").Trim().ToLowerInvariant();
            if (text.Length == 0)
                return "";

            if (text.StartsWith("monster_", StringComparison.OrdinalIgnoreCase))
                text = text["monster_".Length..];

            if (text.StartsWith("m", StringComparison.OrdinalIgnoreCase))
                text = text[1..];

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                return "";

            return $"m{number:D3}";
        }

        static string SanitizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            System.Text.StringBuilder builder = new();
            foreach (char c in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(c);
                else if (c is ':' or '/' or '\\' or ' ' or '_' or '-')
                    builder.Append('-');
            }

            string sanitized = builder.ToString().Trim('-');
            while (sanitized.Contains("--", StringComparison.Ordinal))
                sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
            return sanitized.Length == 0 ? "unknown" : sanitized;
        }
    }
}
