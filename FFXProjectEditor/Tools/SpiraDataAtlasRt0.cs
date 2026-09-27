using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;

namespace FFXProjectEditor.Tools
{
    internal static class SpiraDataAtlasRt0
    {
        public static int Run()
        {
            Console.WriteLine("=== Spira Data Atlas read-only provider RT0 ===");

            int fail = 0;
            try
            {
                IReadOnlyList<SpiraDataAtlasEntry> entries = SpiraDataAtlasCatalog.All;
                IReadOnlyList<SpiraDataAtlasDetailEntry> details = SpiraDataAtlasCatalog.Details;

                fail += ExpectAtLeast("atlas entries", 60, entries.Count);
                fail += ExpectAtLeast("master manifest layers", 54, entries.Count(e => e.Id.StartsWith("atlas:master:", StringComparison.OrdinalIgnoreCase)));
                fail += Expect("detail entries", 21192, details.Count);
                fail += Expect("unique detail ids", details.Count, details.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                fail += Expect("raw parsed files", 1601, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.ParsedFile));
                fail += Expect("battle target/text files", 1224, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.ParsedFile && d.Domain == "battle-target-text"));
                fail += Expect("event target/text files", 377, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.ParsedFile && d.Domain == "event-target-text"));
                fail += ExpectAtLeast("dataset detail layers", 54, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.DatasetLayer));
                fail += ExpectAtLeast("ATEL call-shapes", 615, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.AtelCallShape));
                fail += Expect("ATEL branch-shapes", 89, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.AtelBranchShape));
                fail += Expect("ATEL field-shapes", 202, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.AtelFieldWriteShape));
                // SEYMOUR call-shape audit (2026-06-10): the 124 unknown call-shapes are fully classified —
                // 116 btlSetMotionData (0x70A8) explicit-actor motion-data writes + 8 not-a-field-write (0x7032
                // setActorFacingAngle). 0 field-shape aggregates may carry the unknown-call-shape semantic.
                fail += Expect("ATEL unknown call-shapes (resolved)", 0,
                    details.Count(d => d.Kind == SpiraDataAtlasDetailKind.AtelFieldWriteShape
                        && d.Summary.Contains("field-write-unknown-call-shape", StringComparison.OrdinalIgnoreCase)));
                fail += ExpectSearch("ATEL 0x70A8 motion-data field-shape present",
                    details.Any(d => d.Kind == SpiraDataAtlasDetailKind.AtelFieldWriteShape
                        && d.Id.StartsWith("atlas:atel-field-shape:70a8:", StringComparison.OrdinalIgnoreCase)
                        && d.Summary.Contains("set-motion-data-actor-field-value", StringComparison.OrdinalIgnoreCase)));
                fail += Expect("ATEL workers", 3283, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.AtelWorker));
                fail += Expect("ATEL command site summaries", 656, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.Command && d.Domain == "atel-command-sites"));
                fail += Expect("monster presence rows", 1604, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.MonsterPresence));
                fail += Expect("SIN command eligibility rows", 979, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.CommandEligibility));
                fail += Expect("gear name/model rows", 1190, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.GearNameModel));
                fail += Expect("Blitzball event refs", 312, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.BlitzballEventRef));
                fail += Expect("Blitzball prize refs", 228, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.BlitzballPrizeRef));
                fail += Expect("Blitzball prize guardrail", 1, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.BlitzballPrizeGuardrail));
                fail += Expect("Blitzball treasure prize rows", 101,
                    details.Count(d => d.Kind == SpiraDataAtlasDetailKind.BlitzballPrizeRef
                        && d.Id.StartsWith("atlas:blitzball-prize:treasure:", StringComparison.OrdinalIgnoreCase)));
                fail += Expect("Blitzball tech prize rows", 60,
                    details.Count(d => d.Kind == SpiraDataAtlasDetailKind.BlitzballPrizeRef
                        && d.Id.StartsWith("atlas:blitzball-prize:tech:", StringComparison.OrdinalIgnoreCase)));
                fail += Expect("Blitzball overdrive prize rows", 3,
                    details.Count(d => d.Kind == SpiraDataAtlasDetailKind.BlitzballPrizeRef
                        && d.Id.StartsWith("atlas:blitzball-prize:overdrive:", StringComparison.OrdinalIgnoreCase)));
                fail += Expect("Blitzball prize sites", 64,
                    details.Count(d => d.Kind == SpiraDataAtlasDetailKind.BlitzballPrizeRef
                        && d.Id.StartsWith("atlas:blitzball-prize-site:", StringComparison.OrdinalIgnoreCase)));
                // mapping proof: prize 0 -> takara 220 (Hi-Potion), prize 100 -> takara 320 (Phoenix Down)
                fail += ExpectSearch("Blitzball prize 0 -> takara 220 Hi-Potion",
                    details.Any(d => d.Id == "atlas:blitzball-prize:treasure:0"
                        && d.Summary.Contains("takara 220", StringComparison.OrdinalIgnoreCase)
                        && d.Summary.Contains("Hi-Potion", StringComparison.OrdinalIgnoreCase)));
                fail += ExpectSearch("Blitzball prize 100 -> takara 320 Phoenix Down",
                    details.Any(d => d.Id == "atlas:blitzball-prize:treasure:100"
                        && d.Summary.Contains("takara 320", StringComparison.OrdinalIgnoreCase)
                        && d.Summary.Contains("Phoenix Down", StringComparison.OrdinalIgnoreCase)));
                // honesty: treasure = proved-candidate (never bare proved / RT2); overdrive = metadata-only; sites = blocked
                fail += ExpectSearch("Blitzball treasure proved-candidate (not RT2)",
                    details.Any(d => d.Id == "atlas:blitzball-prize:treasure:0"
                        && d.Evidence.Contains("proved-candidate", StringComparison.OrdinalIgnoreCase)
                        && !d.Evidence.Contains("RT2", StringComparison.OrdinalIgnoreCase)));
                fail += ExpectSearch("Blitzball overdrive metadata-only",
                    details.Any(d => d.Id.StartsWith("atlas:blitzball-prize:overdrive:", StringComparison.OrdinalIgnoreCase)
                        && d.Evidence.Contains("metadata-only", StringComparison.OrdinalIgnoreCase)));
                fail += ExpectSearch("Blitzball prize site blocked",
                    details.Any(d => d.Id.StartsWith("atlas:blitzball-prize-site:", StringComparison.OrdinalIgnoreCase) && d.IsBlocked));
                // Blitzball prize UI accessor: 164 catalog + 64 sites + 1 guardrail = 229 domain:blitzball-prize details.
                fail += Expect("Blitzball prize UI accessor rows", 229, SpiraDataAtlasCatalog.BlitzballPrizeDetails.Count);
                fail += ExpectSearch("Blitzball prize UI accessor domain",
                    SpiraDataAtlasCatalog.BlitzballPrizeDetails.All(d => d.Domain == "blitzball-prize"));
                // Mix detail kind (Jarvis-JECHT): prepare.bin lower-triangular 112x112 matrix normalized to 6,328
                // unordered pairs (112*113/2, 0 duplicate keys), keyed mix:0x{max}+0x{min} in the 0x2000 Items space.
                fail += Expect("Mix combination rows", 6328, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.MixCombination));
                fail += Expect("PC/Aeon fine rows", 20, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.PcAeonFineStat));
                fail += Expect("Sphere Grid node rows", 2493, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.SphereGridNode));
                fail += Expect("Unknown health rows", 75, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.UnknownHealth));
                // Aeon growth recipes (Jarvis-BRASKA): sum_grow.bin, Aeon-ONLY, 77 = 67 ability + 10 stat (compiled, work/-independent).
                fail += Expect("Aeon growth recipe rows", 77, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.AeonGrowthRecipe));
                fail += Expect("Aeon growth stat recipes (cost-partial)", 10,
                    details.Count(d => d.Kind == SpiraDataAtlasDetailKind.AeonGrowthRecipe && d.Evidence.Contains("cost-semantics-partial", StringComparison.OrdinalIgnoreCase)));
                fail += Expect("Aeon growth ability recipes", 67,
                    details.Count(d => d.Kind == SpiraDataAtlasDetailKind.AeonGrowthRecipe && !d.Evidence.Contains("cost-semantics-partial", StringComparison.OrdinalIgnoreCase)));
                // Aeon-only honesty: every aeon-growth row is Target 0x007F; PC and Sphere-Grid kinds never appear in this domain.
                fail += ExpectSearch("Aeon growth is Aeon-only (no PC rows)",
                    details.Where(d => d.Kind == SpiraDataAtlasDetailKind.AeonGrowthRecipe).All(d => d.Domain == "aeon-growth" && d.Detail.Contains("0x007F")));
                fail += ExpectSearch("Aeon growth domain has no PC fine-stat rows",
                    details.Count(d => d.Domain == "aeon-growth" && d.Kind == SpiraDataAtlasDetailKind.PcAeonFineStat) == 0);
                fail += ExpectSearch("Aeon growth domain has no Sphere Grid rows",
                    details.Count(d => d.Domain == "aeon-growth" && d.Kind == SpiraDataAtlasDetailKind.SphereGridNode) == 0);

                // PC/Aeon player growth (Jarvis-TIDUS): ply_save.bin + ply_rom.bin, 20 slots compiled byte-grounded
                // (work/-independent), region new_uspc. 7 PC + 1 guest (share pc-growth-ap-curve) + 10 Aeon + 2 unmapped.
                fail += Expect("Player growth rows", 20, details.Count(d => d.Kind == SpiraDataAtlasDetailKind.PlayerGrowthStat));
                fail += Expect("Player growth domain rows", 20, details.Count(d => d.Domain == "player-growth" && d.Kind == SpiraDataAtlasDetailKind.PlayerGrowthStat));
                fail += Expect("Player growth PC/guest slots (ap-curve, stat-coef Aeon-only)", 8,
                    details.Count(d => d.Kind == SpiraDataAtlasDetailKind.PlayerGrowthStat && d.Evidence.Contains("pc-growth-ap-curve", StringComparison.OrdinalIgnoreCase)));
                fail += Expect("Player growth Aeon slots (auto-growth coef)", 10,
                    details.Count(d => d.Kind == SpiraDataAtlasDetailKind.PlayerGrowthStat && d.Evidence.Contains("aeon-auto-growth-coef", StringComparison.OrdinalIgnoreCase)));
                fail += Expect("Player growth unmapped slots (metadata-only)", 2,
                    details.Count(d => d.Kind == SpiraDataAtlasDetailKind.PlayerGrowthStat && d.Evidence.Contains("unmapped-slot", StringComparison.OrdinalIgnoreCase)));
                // Honesty: PC slots carry zero stat-growth coefficients (PCs grow via the Sphere Grid); Aeon slots do not.
                fail += ExpectSearch("Player growth PC stat coefficients are zero",
                    PlayerGrowthData.Rows.Where(r => r.Index <= 6).All(r => (r.HpA | r.HpB | r.StrA | r.MagA | r.DefA | r.AgiA) == 0));
                fail += ExpectSearch("Player growth Aeon stat coefficients are non-zero",
                    PlayerGrowthData.Rows.Where(r => r.Index >= 8 && r.Index <= 17).All(r => (r.HpA | r.HpB | r.StrA | r.MagA) != 0));
                // Region honesty: compiled table is new_uspc (NOT locale-invariant, unlike sum_grow.bin).
                fail += ExpectSearch("Player growth region is new_uspc", PlayerGrowthData.SourceRegion == "new_uspc");

                fail += ExpectSearch("master search atel-granular", SpiraDataAtlasCatalog.Search("atel-granular", 8).Count > 0);
                fail += ExpectSearch("raw parsed search battle/btl", SpiraDataAtlasCatalog.SearchDetails("battle/btl", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.ParsedFile));
                fail += ExpectSearch("semantic candidate search", SpiraDataAtlasCatalog.Search("semantic-candidate", 8).Count > 0);
                fail += ExpectSearch("branch search", SpiraDataAtlasCatalog.SearchDetails("domain:atel-branch", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.AtelBranchShape));
                fail += ExpectSearch("field call-shape search", SpiraDataAtlasCatalog.SearchDetails("domain:atel-field-shape", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.AtelFieldWriteShape));
                fail += ExpectSearch("worker search", SpiraDataAtlasCatalog.SearchDetails("domain:atel-worker", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.AtelWorker));
                fail += ExpectSearch("monster presence search", SpiraDataAtlasCatalog.SearchDetails("domain:monster-presence m337", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.MonsterPresence));
                fail += ExpectSearch("SIN eligibility search", SpiraDataAtlasCatalog.SearchDetails("domain:sin-eligibility Attack", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.CommandEligibility));
                fail += ExpectSearch("gear name/model search", SpiraDataAtlasCatalog.SearchDetails("domain:gear-name-model Brotherhood", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.GearNameModel));
                fail += ExpectSearch("Blitzball prize guardrail search", SpiraDataAtlasCatalog.SearchDetails("domain:blitzball-prize blocked", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.BlitzballPrizeGuardrail));
                fail += ExpectSearch("PC/Aeon fine search", SpiraDataAtlasCatalog.SearchDetails("domain:pc-aeon-fine Tidus", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.PcAeonFineStat));
                fail += ExpectSearch("Sphere Grid node search", SpiraDataAtlasCatalog.SearchDetails("domain:sphere-grid-node Cheer", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.SphereGridNode));

                bool commandCrosslink = SpiraDataAtlasCatalog.TryGetCommandCrosslink("0x4000", out SpiraCommandCrosslink? attack)
                    && attack is { CommandSiteCount: > 0 };
                fail += ExpectSearch("command crosslink 0x4000", commandCrosslink);

                bool monsterDetail = SpiraDataAtlasCatalog.TryGetMonsterDetail("m001", out SpiraDataAtlasDetailEntry? raldo)
                    && raldo is not null
                    && raldo.Title.Contains("Raldo", StringComparison.OrdinalIgnoreCase);
                fail += ExpectSearch("monster detail m001/Raldo", monsterDetail);

                // Treasure module crosslink: gear chest #4 (Yuna Weapon, buki_get 2) resolves to its gear reward detail.
                bool treasureGear = SpiraDataAtlasCatalog.TryGetTreasureGear(4, 2, out SpiraDataAtlasDetailEntry? chest)
                    && chest is { Kind: SpiraDataAtlasDetailKind.GearRewardShop }
                    && chest.Title.Contains("Yuna", StringComparison.OrdinalIgnoreCase);
                fail += ExpectSearch("treasure gear #4/buki_get 2", treasureGear);

                // Value-guard: a chest repointed to a different buki_get row no longer matches the corpus, so it hides.
                bool treasureGuard = !SpiraDataAtlasCatalog.TryGetTreasureGear(4, 99, out _);
                fail += ExpectSearch("treasure gear value-guard", treasureGuard);

                // Non-gear treasure index has no atlas:gear entry, so the accessor returns false (strip stays hidden).
                bool treasureNonGear = !SpiraDataAtlasCatalog.TryGetTreasureGear(0, 0, out _);
                fail += ExpectSearch("treasure non-gear miss", treasureNonGear);

                // Shop gear-slot value-guard: gear-shop slot 0x00/0 (Tidus Weapon, corpus on-disk value 1) resolves.
                bool shopGearGood = SpiraDataAtlasCatalog.TryGetShopGearSlot(0, 0, 1, out SpiraDataAtlasDetailEntry? shopSlot)
                    && shopSlot is { Kind: SpiraDataAtlasDetailKind.GearRewardShop }
                    && shopSlot.Title.Contains("Tidus", StringComparison.OrdinalIgnoreCase);
                fail += ExpectSearch("shop gear slot 0x00/0 value 1", shopGearGood);

                // Value-guard: an edited/modded slot value (999, outside the 1..427 catalog) no longer matches -> hide.
                bool shopGearGuard = !SpiraDataAtlasCatalog.TryGetShopGearSlot(0, 0, 999, out _);
                fail += ExpectSearch("shop gear value-guard mismatch hides", shopGearGuard);

                // Empty slot (raw 0) short-circuits; a bank with no gear-shop corpus rows (bank 2) misses cleanly.
                bool shopGearEmpty = !SpiraDataAtlasCatalog.TryGetShopGearSlot(0, 0, 0, out _);
                bool shopGearNoCorpus = !SpiraDataAtlasCatalog.TryGetShopGearSlot(2, 0, 1, out _);
                fail += ExpectSearch("shop gear empty slot (raw 0) miss", shopGearEmpty);
                fail += ExpectSearch("shop gear bank-without-corpus miss", shopGearNoCorpus);

                // Highest-risk format edge: multi-digit bank hex (46 -> 0x2E) + a >255 value (427 -> 0x1AB ushort).
                bool shopGearMax = SpiraDataAtlasCatalog.TryGetShopGearSlot(46, 6, 427, out _);
                fail += ExpectSearch("shop gear bank 0x2E slot 6 value 427", shopGearMax);

                // Item-shop value-guard (mirror of gear): item-shop slot 0x00/0 = Potion, on-disk value 0x2000.
                bool shopItemGood = SpiraDataAtlasCatalog.TryGetShopItemSlot(0, 0, 0x2000, out SpiraDataAtlasDetailEntry? itemSlot)
                    && itemSlot is { Kind: SpiraDataAtlasDetailKind.ItemShopCommand }
                    && itemSlot.Title.Contains("Potion", StringComparison.OrdinalIgnoreCase);
                fail += ExpectSearch("shop item slot 0x00/0 value 0x2000", shopItemGood);

                // Value-guard: an edited/modded item value (0x9999) no longer matches the corpus -> badge hides.
                bool shopItemGuard = !SpiraDataAtlasCatalog.TryGetShopItemSlot(0, 0, 0x9999, out _);
                fail += ExpectSearch("shop item value-guard mismatch hides", shopItemGuard);

                // Empty slot (raw 0) short-circuits; a (bank,slot) the corpus never filled (bank 0 fills only slots 0..2)
                // misses cleanly, so slot 3 yields no badge even with a plausible Items value.
                bool shopItemEmpty = !SpiraDataAtlasCatalog.TryGetShopItemSlot(0, 0, 0, out _);
                bool shopItemNoCorpus = !SpiraDataAtlasCatalog.TryGetShopItemSlot(0, 3, 0x2000, out _);
                fail += ExpectSearch("shop item empty slot (raw 0) miss", shopItemEmpty);
                fail += ExpectSearch("shop item no-corpus slot miss", shopItemNoCorpus);

                // Mix value-guard (Jarvis-JECHT): Potion(idx 0) + Potion(idx 0) = Ultra Potion (0x30AC), the canonical (0,0) cell.
                bool mixGood = SpiraDataAtlasCatalog.TryGetMixCombination(0, 0, 0x30AC, out SpiraDataAtlasDetailEntry? mixCell)
                    && mixCell is { Kind: SpiraDataAtlasDetailKind.MixCombination }
                    && mixCell.Title.Contains("Ultra Potion", StringComparison.OrdinalIgnoreCase);
                fail += ExpectSearch("mix 0+0 -> Ultra Potion 0x30AC", mixGood);

                // Canonical ordering: the unordered pair {Hi-Potion idx 1, Mega-Potion idx 3} = Ultra Potion (0x30AC)
                // resolves the same id whether the editor selects origin 3/partner 1 or origin 1/partner 3 (max/min).
                bool mixCanonHi = SpiraDataAtlasCatalog.TryGetMixCombination(3, 1, 0x30AC, out SpiraDataAtlasDetailEntry? mixA);
                bool mixCanonLo = SpiraDataAtlasCatalog.TryGetMixCombination(1, 3, 0x30AC, out SpiraDataAtlasDetailEntry? mixB);
                fail += ExpectSearch("mix canonical order (3,1)==(1,3)", mixCanonHi && mixCanonLo && mixA?.Id == mixB?.Id);

                // Value-guard: editing the outcome to a different item (0x9999) no longer matches the corpus -> hide.
                bool mixGuard = !SpiraDataAtlasCatalog.TryGetMixCombination(0, 0, 0x9999, out _);
                fail += ExpectSearch("mix value-guard mismatch hides", mixGuard);

                // Empty cell (raw 0) short-circuits: every upper-triangle mirror cell is 0 in prepare.bin, so it never badges.
                bool mixEmpty = !SpiraDataAtlasCatalog.TryGetMixCombination(0, 0, 0, out _);
                fail += ExpectSearch("mix empty cell (raw 0) miss", mixEmpty);

                // Out-of-table index (partner 200 -> 0x20C8, no such mix key) misses cleanly instead of throwing.
                bool mixNoCorpus = !SpiraDataAtlasCatalog.TryGetMixCombination(0, 200, 0x30AC, out _);
                fail += ExpectSearch("mix out-of-range index miss", mixNoCorpus);

                fail += ExpectSearch("mix search domain", SpiraDataAtlasCatalog.SearchDetails("domain:mix Ultra Potion", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.MixCombination));

                // Aeon growth value-guard (Jarvis-BRASKA): entry 66 = teach Ultima (0x3053) for 99x Supreme Gem (0x202C).
                bool aeonAbil = SpiraDataAtlasCatalog.TryGetAeonGrowthRecipe(66, 0x3053, 0x202C, out SpiraDataAtlasDetailEntry? aeonUlt)
                    && aeonUlt is { Kind: SpiraDataAtlasDetailKind.AeonGrowthRecipe }
                    && aeonUlt.Title.Contains("Ultima", StringComparison.OrdinalIgnoreCase)
                    && aeonUlt.Detail.Contains("Supreme Gem", StringComparison.OrdinalIgnoreCase);
                fail += ExpectSearch("aeon growth 66 -> Ultima / Supreme Gem", aeonAbil);

                // Stat recipe entry 67 = HP +100 via Power Sphere (0x2046); cost-semantics-partial surfaced in evidence.
                bool aeonStat = SpiraDataAtlasCatalog.TryGetAeonGrowthRecipe(67, 0x0000, 0x2046, out SpiraDataAtlasDetailEntry? aeonHp)
                    && aeonHp is { Kind: SpiraDataAtlasDetailKind.AeonGrowthRecipe }
                    && aeonHp.Title.Contains("HP", StringComparison.OrdinalIgnoreCase)
                    && aeonHp.Detail.Contains("Power Sphere", StringComparison.OrdinalIgnoreCase)
                    && aeonHp.Evidence.Contains("cost-semantics-partial", StringComparison.OrdinalIgnoreCase);
                fail += ExpectSearch("aeon growth 67 -> HP +100 / Power Sphere (cost-partial)", aeonStat);

                // Value-guard: editing the recipe's taught ability (0x9999) no longer matches the corpus -> hide.
                bool aeonGuard = !SpiraDataAtlasCatalog.TryGetAeonGrowthRecipe(66, 0x9999, 0x202C, out _);
                fail += ExpectSearch("aeon growth value-guard mismatch hides", aeonGuard);

                // Out-of-range entry index (table is 0..76) misses cleanly instead of throwing.
                bool aeonNoCorpus = !SpiraDataAtlasCatalog.TryGetAeonGrowthRecipe(999, 0, 0, out _);
                fail += ExpectSearch("aeon growth out-of-range index miss", aeonNoCorpus);

                fail += ExpectSearch("aeon growth search domain", SpiraDataAtlasCatalog.SearchDetails("domain:aeon-growth Ultima", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.AeonGrowthRecipe));

                // Player growth value-guard (Jarvis-TIDUS): Tidus (slot 0) base HP = 520 from ply_save.bin.
                bool pgTidus = SpiraDataAtlasCatalog.TryGetPlayerGrowthStat("ply_save", 0, "basehp", 520, out SpiraDataAtlasDetailEntry? pgT)
                    && pgT is { Kind: SpiraDataAtlasDetailKind.PlayerGrowthStat }
                    && pgT.Title.Contains("Tidus", StringComparison.OrdinalIgnoreCase);
                fail += ExpectSearch("player growth slot 0 -> Tidus base HP 520", pgTidus);

                // Tidus AP-requirement curve max = 22000 from ply_rom.bin (the PC growth knob).
                bool pgTidusAp = SpiraDataAtlasCatalog.TryGetPlayerGrowthStat("ply_rom", 0, "apreqmax", 22000, out _);
                fail += ExpectSearch("player growth slot 0 -> ply_rom AP max 22000", pgTidusAp);

                // Valefor (slot 8) HP growth coefficient A = 6 from ply_rom.bin (Aeon auto-growth).
                bool pgValefor = SpiraDataAtlasCatalog.TryGetPlayerGrowthStat("ply_rom", 8, "hpcoefa", 6, out SpiraDataAtlasDetailEntry? pgV)
                    && pgV is { Kind: SpiraDataAtlasDetailKind.PlayerGrowthStat }
                    && pgV.Title.Contains("Valefor", StringComparison.OrdinalIgnoreCase);
                fail += ExpectSearch("player growth slot 8 -> Valefor HP coef 6", pgValefor);

                // Value-guard: editing Tidus base HP away from 520 no longer matches the corpus -> hide.
                bool pgGuard = !SpiraDataAtlasCatalog.TryGetPlayerGrowthStat("ply_save", 0, "basehp", 9999, out _);
                fail += ExpectSearch("player growth value-guard mismatch hides", pgGuard);

                // Out-of-range slot (table is 0..19) and unknown source/field miss cleanly instead of throwing.
                bool pgNoSlot = !SpiraDataAtlasCatalog.TryGetPlayerGrowthStat("ply_save", 999, "basehp", 0, out _);
                fail += ExpectSearch("player growth out-of-range slot miss", pgNoSlot);
                bool pgNoSrc = !SpiraDataAtlasCatalog.TryGetPlayerGrowthStat("bogus", 0, "basehp", 520, out _);
                fail += ExpectSearch("player growth unknown source miss", pgNoSrc);
                bool pgNoField = !SpiraDataAtlasCatalog.TryGetPlayerGrowthStat("ply_save", 0, "nope", 0, out _);
                fail += ExpectSearch("player growth unknown field miss", pgNoField);

                fail += ExpectSearch("player growth search domain", SpiraDataAtlasCatalog.SearchDetails("domain:player-growth Tidus", 8).Any(d => d.Kind == SpiraDataAtlasDetailKind.PlayerGrowthStat));

                IReadOnlyList<AiBibleEntry> bible = AiBibleCatalog.Search("battle/btl", 8);
                fail += ExpectSearch("BIBLE sees raw parsed files", bible.Any(e => e.Id.StartsWith("atlas:parsed-file:", StringComparison.OrdinalIgnoreCase)));
                fail += ExpectSearch("BIBLE corpus overdrive pattern", AiBibleCatalog.ById("pattern:corpus:overdrive") is not null);
                fail += ExpectSearch("BIBLE domain battle text", AiBibleCatalog.Search("domain:battle-target-text", 8).Any(e => e.Domain == "battle-target-text"));
                fail += ExpectSearch("BIBLE domain monster corpus", AiBibleCatalog.Search("domain:monster-corpus", 8).Any(e => e.Domain == "monster-corpus"));
                fail += ExpectSearch("BIBLE domain gear", AiBibleCatalog.Search("domain:gear", 8).Any(e => e.Domain == "gear"));
                fail += ExpectSearch("BIBLE domain branches", AiBibleCatalog.Search("domain:atel-branch", 8).Any(e => e.Domain == "atel-branch"));
                fail += ExpectSearch("BIBLE domain field-shape", AiBibleCatalog.Search("domain:atel-field-shape", 8).Any(e => e.Domain == "atel-field-shape"));
                fail += ExpectSearch("BIBLE domain workers", AiBibleCatalog.Search("domain:atel-worker", 8).Any(e => e.Domain == "atel-worker"));
                fail += ExpectSearch("BIBLE domain monster presence", AiBibleCatalog.Search("domain:monster-presence", 8).Any(e => e.Domain == "monster-presence"));
                fail += ExpectSearch("BIBLE domain SIN eligibility", AiBibleCatalog.Search("domain:sin-eligibility", 8).Any(e => e.Domain == "sin-eligibility"));
                fail += ExpectSearch("BIBLE domain Blitzball prize", AiBibleCatalog.Search("domain:blitzball-prize", 8).Any(e => e.Domain == "blitzball-prize"));
                fail += ExpectSearch("BIBLE domain Sphere Grid node", AiBibleCatalog.Search("domain:sphere-grid-node", 8).Any(e => e.Domain == "sphere-grid-node"));
                fail += ExpectSearch("BIBLE domain mix", AiBibleCatalog.Search("domain:mix", 8).Any(e => e.Domain == "mix"));
                fail += ExpectSearch("BIBLE domain aeon-growth", AiBibleCatalog.Search("domain:aeon-growth", 8).Any(e => e.Domain == "aeon-growth"));
                fail += ExpectSearch("BIBLE domain player-growth", AiBibleCatalog.Search("domain:player-growth", 8).Any(e => e.Domain == "player-growth"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: provider threw {ex.GetType().Name}: {ex.Message}");
                return 1;
            }

            Console.WriteLine(fail == 0
                ? "VERDICT: PASS - Atlas provider sees master layers, raw parsed files, crosslinks and BIBLE search without writer authority."
                : $"VERDICT: FAIL - {fail} assertion(s) failed.");
            return fail == 0 ? 0 : 1;
        }

        static int Expect(string label, int expected, int actual)
        {
            bool ok = expected == actual;
            Console.WriteLine(ok
                ? $"  {label,-30}: PASS {actual}"
                : $"  {label,-30}: FAIL expected {expected}, got {actual}");
            return ok ? 0 : 1;
        }

        static int ExpectAtLeast(string label, int expectedMinimum, int actual)
        {
            bool ok = actual >= expectedMinimum;
            Console.WriteLine(ok
                ? $"  {label,-30}: PASS {actual}"
                : $"  {label,-30}: FAIL expected >= {expectedMinimum}, got {actual}");
            return ok ? 0 : 1;
        }

        static int ExpectSearch(string label, bool ok)
        {
            Console.WriteLine(ok
                ? $"  {label,-30}: PASS"
                : $"  {label,-30}: FAIL");
            return ok ? 0 : 1;
        }
    }
}
