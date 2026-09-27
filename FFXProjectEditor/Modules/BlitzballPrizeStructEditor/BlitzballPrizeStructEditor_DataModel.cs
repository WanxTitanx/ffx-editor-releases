using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Blitzball;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Event;
using FFXProjectEditor.FfxLib.Treasure;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.BlitzballPrizeStructEditor
{
    // Editor for the blitzball PRIZE STRUCTURE in the GAME FILE bltz0200.ebp. Two editable immediate families,
    // both 2-byte ATEL operands (BlitzballPrizeStructure_File, gate --blitzball-prizestruct-rt0, proven
    // byte-identical + mutation-isolated):
    //   * prize-index  (459 sites): which prize each league/tournament placement / random bucket awards
    //                  (reward = takara prize+220, or MacroDict#8 #(prize-100)).
    //   * roll threshold / ODDS (390 sites): the case>=N / case<=M bounds over GetRandomInRange(100) that decide
    //                  HOW LIKELY each bucket is. Widen a bucket [lo..hi] -> higher odds for that prize.
    //
    // PRESENTATION (this view-model): the 459 prize sites are organised into the RE-grounded tree the human asked
    // for — Competition (League / Tournament) -> Award (1st / 2nd / 3rd place + Top Scorer) -> Draw ("Sorteio N",
    // one GetRandomInRange roll switch) -> the prize buckets. A placement repeats across several draws as the
    // prize escalates with league progression; the draw boundary is BlitzballPrizeStructure_File.FindRollSwitchStarts
    // (proven, gated). The ODDS family is NOT folded into the prizes: within a draw the number of prize buckets and
    // the number of roll-bound pairs do NOT match 1:1 (corpus-checked), so claiming "this prize has X% odds" would
    // be invented — the odds stay an honest, separately-editable group. All grouping is a view over the same
    // PrizeStructRow/EditSession instances; no writer/parser/byte logic runs here.
    internal partial class BlitzballPrizeStructEditor_DataModel : ObservableObject
    {
        const int PrizeToTakara = 220;
        const string OddsCategory = "Roll Threshold (odds)";

        byte[] originalBytes = Array.Empty<byte>();
        string ebpPath = string.Empty;
        readonly List<PrizeStructRow> allRows = new();
        IReadOnlyList<Treasure_Entry>? takara;

        // The prize tree (Competition -> Award -> Draw -> rows) and the separate, flat odds group.
        public ObservableCollection<PrizeCompetitionGroup> Competitions { get; } = new();
        public ObservableCollection<PrizeStructRow> Rows { get; } = new(); // flat filtered set (search/back-compat)
        [ObservableProperty] private PrizeStructGroup? oddsGroup;
        [ObservableProperty] private bool hasOdds;

        public ObservableCollection<string> VarFilters { get; } = new(new[]
        {
            "All", "League", "Tournament", OddsCategory,
        });

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string selectedVarFilter = "All";
        [ObservableProperty] private string loadSummary = "Loading bltz0200.ebp...";
        [ObservableProperty] private string scopeSummary =
            "Edits which prize index each placement / random bucket awards, AND the roll odds, in bltz0200.ebp.";
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;

        public BlitzballPrizeStructEditor_DataModel() => LoadFromDisk();

        partial void OnFilterTextChanged(string value) => ApplyFilter();
        partial void OnSelectedVarFilterChanged(string value) => ApplyFilter();

        public void Save() => EditSession?.Save();
        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();
        public void RefreshFromDisk() => LoadFromDisk();

        static string ResolveEbpPath()
            => Path.Combine(Project_Service.Instance.Path_Event, "bl", "bltz0200", "bltz0200.ebp");

        void LoadFromDisk()
        {
            EditSession?.Dispose();
            EditSession = null;
            foreach (PrizeStructRow r in allRows) r.PropertyChanged -= RowChanged;
            allRows.Clear();
            Rows.Clear();
            Competitions.Clear();
            OddsGroup = null;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                return;
            }

            ebpPath = ResolveEbpPath();
            if (!File.Exists(ebpPath))
            {
                LoadSummary = $"bltz0200.ebp not found in the loaded workspace ({ebpPath}).";
                return;
            }

            try
            {
                takara = TryLoadTakara();
                originalBytes = File.ReadAllBytes(ebpPath);
                LoadRowsFromBytes(originalBytes);
                EditSession = new ByteSnapshotEditorSession(
                    BuildFile, RestoreFromBytes, PersistBytes, "blitzball prize structure", BuildFile())
                {
                    RevertWritesToDisk = true
                };
            }
            catch (Exception ex)
            {
                LoadSummary = $"Failed to read bltz0200.ebp: {ex.Message}";
            }
        }

        void LoadRowsFromBytes(byte[] bytes)
        {
            foreach (PrizeStructRow r in allRows) r.PropertyChanged -= RowChanged;
            allRows.Clear();

            Event_File ev = Event_File.Read(BlitzballPrizeStructure_File.EventId, bytes);
            List<BlitzballPrizeSite> prizes = BlitzballPrizeStructure_File.FindSites(ev);
            List<BlitzballRollThresholdSite> thresholds = BlitzballPrizeStructure_File.FindRollThresholds(ev);

            // RE-grounded draw boundaries (GetRandomInRange roll switches). Number the draws 1..K WITHIN each
            // (competition, award) so the label reads "Sorteio N" for that placement.
            List<int> starts = BlitzballPrizeStructure_File.FindRollSwitchStarts(ev);
            var drawNumberByKey = new Dictionary<(BlitzballPrizeVar, int, int), int>();
            foreach (var grp in prizes
                         .GroupBy(s => (s.Var, s.Slot))
                         .Select(g => (g.Key, draws: g.Select(s => BlitzballPrizeStructure_File.DrawStartFor(starts, s.ValueOffset))
                                                       .Distinct().OrderBy(d => d).ToList())))
            {
                for (int i = 0; i < grp.draws.Count; i++)
                    drawNumberByKey[(grp.Key.Var, grp.Key.Slot, grp.draws[i])] = i + 1;
            }

            foreach (BlitzballPrizeSite s in prizes)
            {
                int drawStart = BlitzballPrizeStructure_File.DrawStartFor(starts, s.ValueOffset);
                drawNumberByKey.TryGetValue((s.Var, s.Slot, drawStart), out int drawNo);
                allRows.Add(PrizeStructRow.ForPrize(
                    s, ResolveReward,
                    competition: BlitzballPrizeStructure_File.CompetitionLabel(s.Var),
                    award: BlitzballPrizeStructure_File.AwardLabel(s.Var, s.Slot),
                    drawStart: drawStart,
                    drawNumber: drawNo == 0 ? 1 : drawNo));
            }
            foreach (BlitzballRollThresholdSite t in thresholds)
                allRows.Add(PrizeStructRow.ForThreshold(t));

            allRows.Sort((a, b) => a.ValueOffset.CompareTo(b.ValueOffset));
            foreach (PrizeStructRow r in allRows)
                r.PropertyChanged += RowChanged;

            int league = prizes.Count(s => s.Var == BlitzballPrizeVar.LeagueStandings);
            int tourn = prizes.Count(s => s.Var == BlitzballPrizeVar.TournamentStandings);
            int lts = prizes.Count(s => s.Var == BlitzballPrizeVar.LeagueTopScorer);
            int tts = prizes.Count(s => s.Var == BlitzballPrizeVar.TournamentTopScorer);
            int drawCount = prizes.Select(s => BlitzballPrizeStructure_File.DrawStartFor(starts, s.ValueOffset)).Distinct().Count();
            LoadSummary = $"{prizes.Count} prize sites in {drawCount} draws (League {league} · Tournament {tourn} · "
                + $"L.top-scorer {lts} · T.top-scorer {tts}) + {thresholds.Count} roll-odds sites in bltz0200.ebp.";
            ScopeSummary = "Writable (Lab): edits the constant prize-index immediates (which prize each placement / random "
                + "bucket gives) AND the roll thresholds (case >=N / <=M over GetRandomInRange(100) = the odds) in "
                + "bltz0200.ebp (RT0 isolation proven, --blitzball-prizestruct-rt0; in-game RT2 pending). The reward each "
                + "index gives = takara row (prize+220), editable in the Prize Pool tab. Each placement repeats across "
                + Strings.F2_several_draws_the_prize_escalates_with_l_0734b2c9;
            ApplyFilter();
        }

        void ApplyFilter()
        {
            // Remember open nodes so re-filtering doesn't collapse the user's place.
            var wasOpen = new HashSet<string>();
            foreach (PrizeCompetitionGroup c in Competitions)
            {
                if (c.IsExpanded) wasOpen.Add("C:" + c.Header);
                foreach (PrizeAwardGroup a in c.Awards)
                {
                    if (a.IsExpanded) wasOpen.Add($"A:{c.Header}/{a.Header}");
                    foreach (PrizeDrawGroup d in a.Draws)
                        if (d.IsExpanded) wasOpen.Add($"D:{c.Header}/{a.Header}/{d.Header}");
                }
            }
            bool oddsWasOpen = OddsGroup?.IsExpanded ?? false;

            Rows.Clear();
            Competitions.Clear();
            OddsGroup = null;

            string filter = FilterText.Trim();
            bool hasText = filter.Length > 0;
            string varFilter = SelectedVarFilter;
            bool showPrizes = varFilter is "All" or "League" or "Tournament";
            bool showOdds = varFilter is "All" or OddsCategory;

            bool Match(PrizeStructRow r) => !hasText || r.SearchBlob.Contains(filter, StringComparison.OrdinalIgnoreCase);

            // --- Prize tree: Competition -> Award -> Draw -> rows ---
            if (showPrizes)
            {
                var comps = new Dictionary<string, PrizeCompetitionGroup>();
                var awards = new Dictionary<(string, string), PrizeAwardGroup>();
                var draws = new Dictionary<(string, string, int), PrizeDrawGroup>();

                foreach (PrizeStructRow r in allRows)
                {
                    if (!r.IsPrize) continue;
                    if (varFilter is "League" or "Tournament" && r.Competition != varFilter) continue;
                    if (!Match(r)) continue;

                    Rows.Add(r);

                    if (!comps.TryGetValue(r.Competition, out PrizeCompetitionGroup? comp))
                    {
                        comp = new PrizeCompetitionGroup { Header = r.Competition };
                        comps[r.Competition] = comp;
                        Competitions.Add(comp);
                    }
                    var aKey = (r.Competition, r.AwardLabel);
                    if (!awards.TryGetValue(aKey, out PrizeAwardGroup? award))
                    {
                        award = new PrizeAwardGroup { Header = r.AwardLabel };
                        awards[aKey] = award;
                        comp.Awards.Add(award);
                    }
                    var dKey = (r.Competition, r.AwardLabel, r.DrawNumber);
                    if (!draws.TryGetValue(dKey, out PrizeDrawGroup? draw))
                    {
                        draw = new PrizeDrawGroup { Header = $"Sorteio {r.DrawNumber}" };
                        draws[dKey] = draw;
                        award.Draws.Add(draw);
                    }
                    draw.Rows.Add(r);
                }

                // Fresh load (or no remembered open node) -> open the Competitions by default so the placement
                // structure is visible at once; Awards/Draws stay closed until drilled. Re-filtering still restores
                // exactly the nodes the user had open (wasOpen), so editing doesn't lose their place.
                bool freshLoad = wasOpen.Count == 0;
                foreach (PrizeCompetitionGroup c in Competitions)
                {
                    c.RecomputeCounts();
                    c.IsExpanded = hasText || freshLoad || wasOpen.Contains("C:" + c.Header);
                    foreach (PrizeAwardGroup a in c.Awards)
                    {
                        a.IsExpanded = hasText || wasOpen.Contains($"A:{c.Header}/{a.Header}");
                        foreach (PrizeDrawGroup d in a.Draws)
                        {
                            d.IsExpanded = hasText || wasOpen.Contains($"D:{c.Header}/{a.Header}/{d.Header}");
                            d.ShowLabel = a.Draws.Count > 1;  // only emit "Sorteio N" when the award has more than one draw
                        }
                    }
                }
            }

            // --- Odds: one honest, flat group (NOT paired to a prize, by design) ---
            if (showOdds)
            {
                var odds = new PrizeStructGroup { Header = "Roll Odds (probability · case ≥N / ≤M over roll 0..99)", IsPrize = false };
                foreach (PrizeStructRow r in allRows)
                {
                    if (r.IsPrize || !Match(r)) continue;
                    Rows.Add(r);
                    odds.Rows.Add(r);
                }
                if (odds.Rows.Count > 0)
                {
                    odds.IsExpanded = hasText || oddsWasOpen;
                    OddsGroup = odds;
                }
            }
            HasOdds = OddsGroup != null;
        }

        byte[] BuildFile()
        {
            Event_File ev = Event_File.Read(BlitzballPrizeStructure_File.EventId, originalBytes);
            // both prize-index and threshold edits are 2-byte immediates at the row's offset
            foreach (PrizeStructRow r in allRows)
                ev.PatchScriptUInt16(r.ValueOffset, r.Value);
            return ev.Write();
        }

        void RestoreFromBytes(byte[] bytes)
        {
            Event_File ev = Event_File.Read(BlitzballPrizeStructure_File.EventId, bytes);
            var byOffset = new Dictionary<int, ushort>();
            foreach (BlitzballPrizeSite s in BlitzballPrizeStructure_File.FindSites(ev))
                byOffset[s.ValueOffset] = s.PrizeIndex;
            foreach (BlitzballRollThresholdSite t in BlitzballPrizeStructure_File.FindRollThresholds(ev))
                byOffset[t.ValueOffset] = t.Threshold;

            // The edit session sets suppressTracking during restore, so updating Value here refreshes the UI
            // without staging a spurious pending change.
            foreach (PrizeStructRow r in allRows)
                if (byOffset.TryGetValue(r.ValueOffset, out ushort v))
                    r.Value = v;
        }

        void PersistBytes(byte[] bytes)
        {
            File.WriteAllBytes(ebpPath, bytes);
            originalBytes = bytes;
        }

        void RowChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PrizeStructRow.Value))
                EditSession?.NotifyPotentialMutation();
        }

        IReadOnlyList<Treasure_Entry>? TryLoadTakara()
        {
            try
            {
                string path = Project_Service.Instance.Path_KernelTreasure;
                return File.Exists(path) ? Treasure_File.ReadAll(File.ReadAllBytes(path)) : null;
            }
            catch { return null; }
        }

        // prize index -> human reward. The bltz0201 display uses prizeIndex+220 (Treasure Label) for 1..100
        // and MacroDict#8 #(prizeIndex-100) for >100; 0 = no prize.
        string ResolveReward(ushort p)
        {
            if (p == 0)
                return Strings.F2_no_prize_9852a72f;
            if (p <= 100)
            {
                int takaraIndex = p + PrizeToTakara;
                if (takara != null && takaraIndex < takara.Count)
                    return $"{ResolveTakaraReward(takara[takaraIndex])}  ·  takara {takaraIndex}";
                return $"takara {takaraIndex} (load project for reward name)";
            }
            return $"MacroDict#8 #{p - 100} (special reward)";
        }

        static string ResolveTakaraReward(Treasure_Entry e)
        {
            try
            {
                return e.Kind switch
                {
                    0x00 => $"{e.Quantity * 100} gil",
                    0x02 => $"{(e.Quantity > 1 ? e.Quantity + "x " : string.Empty)}{ResolveGameName(e.ItemId)}",
                    0x0A => ResolveGameName(e.ItemId),
                    _ => $"Kind {e.Kind:X2}h · Type {e.ItemId:X4}h",
                };
            }
            catch { return $"Type {e.ItemId:X4}h"; }
        }

        static string ResolveGameName(ushort rawType)
        {
            try
            {
                byte category = FfxCommon_Util.GetGameCategory(rawType);
                ushort index = FfxCommon_Util.GetGameIndex(rawType);
                string name = FfxCommon_Util.GetGameIndexName(category, index);
                return string.IsNullOrWhiteSpace(name) ? $"#{rawType:X4}" : name;
            }
            catch { return $"#{rawType:X4}"; }
        }
    }

    internal enum PrizeRowKind { PrizeIndex, RollThreshold }

    // --- Presentation-only grouping nodes for the prize tree. All hold references to the SAME PrizeStructRow
    //     instances the editor edits (not copies), so expand/collapse is pure view state. ---

    // Top level: League / Tournament.
    internal partial class PrizeCompetitionGroup : ObservableObject
    {
        [ObservableProperty] private bool isExpanded;
        public string Header { get; init; } = string.Empty;
        public ObservableCollection<PrizeAwardGroup> Awards { get; } = new();
        [ObservableProperty] private string countLabel = string.Empty;

        public void RecomputeCounts()
        {
            int prizes = 0, draws = 0;
            foreach (PrizeAwardGroup a in Awards)
            {
                a.RecomputeCounts();
                prizes += a.PrizeCount;
                draws += a.Draws.Count;
            }
            CountLabel = $"{prizes} prizes · {draws} draws";
        }
    }

    // Middle level: 1st / 2nd / 3rd place + Top Scorer.
    internal partial class PrizeAwardGroup : ObservableObject
    {
        [ObservableProperty] private bool isExpanded;
        public string Header { get; init; } = string.Empty;
        public bool IsPrize => true;
        public ObservableCollection<PrizeDrawGroup> Draws { get; } = new();
        [ObservableProperty] private string countLabel = string.Empty;
        public int PrizeCount { get; private set; }

        public void RecomputeCounts()
        {
            PrizeCount = Draws.Sum(d => d.Rows.Count);
            CountLabel = Draws.Count == 1
                ? $"{PrizeCount} prizes"
                : $"{PrizeCount} prizes · {Draws.Count} draws";
        }
    }

    // Leaf group: one GetRandomInRange roll switch ("Sorteio N").
    internal partial class PrizeDrawGroup : ObservableObject
    {
        [ObservableProperty] private bool isExpanded;
        // Presentation-only: the tree renders the draw inline (no own Expander). The "Sorteio N" label only shows
        // when the award actually has more than one draw — a single-draw award would just be noise.
        [ObservableProperty] private bool showLabel;
        public string Header { get; init; } = string.Empty;
        public bool IsPrize => true;
        public ObservableCollection<PrizeStructRow> Rows { get; } = new();
        public string CountLabel => Rows.Count == 1 ? "1 prize" : $"{Rows.Count} prizes";
    }

    // Flat group, reused for the odds family (kept separate from prizes on purpose).
    internal partial class PrizeStructGroup : ObservableObject
    {
        [ObservableProperty] private bool isExpanded;
        public string Header { get; init; } = string.Empty;
        public bool IsPrize { get; init; }
        public ObservableCollection<PrizeStructRow> Rows { get; } = new();
        public string CountLabel => Rows.Count == 1 ? "1 site" : $"{Rows.Count} sites";
    }

    internal partial class PrizeStructRow : ObservableObject
    {
        readonly Func<ushort, string>? rewardResolver;

        public int ValueOffset { get; private init; }
        public PrizeRowKind Kind { get; private init; }
        public BlitzballPrizeVar Var { get; private init; }
        public int Slot { get; private init; }
        public bool IsUpperBound { get; private init; }

        // Tree placement (prize rows only).
        public string Competition { get; private init; } = string.Empty;
        public string AwardLabel { get; private init; } = string.Empty;
        public int DrawStart { get; private init; } = -1;
        public int DrawNumber { get; private init; }

        [ObservableProperty] private ushort value;

        PrizeStructRow(Func<ushort, string>? rewardResolver) => this.rewardResolver = rewardResolver;

        public static PrizeStructRow ForPrize(
            BlitzballPrizeSite s, Func<ushort, string> rewardResolver,
            string competition, string award, int drawStart, int drawNumber)
            => new(rewardResolver)
            {
                ValueOffset = s.ValueOffset,
                Kind = PrizeRowKind.PrizeIndex,
                Var = s.Var,
                Slot = s.Slot,
                Value = s.PrizeIndex,
                Competition = competition,
                AwardLabel = award,
                DrawStart = drawStart,
                DrawNumber = drawNumber,
            };

        public static PrizeStructRow ForThreshold(BlitzballRollThresholdSite t)
            => new(null)
            {
                ValueOffset = t.ValueOffset,
                Kind = PrizeRowKind.RollThreshold,
                Slot = -1,
                IsUpperBound = t.IsUpperBound,
                Value = t.Threshold,
            };

        public bool IsPrize => Kind == PrizeRowKind.PrizeIndex;
        public string CategoryLabel => IsPrize ? BlitzballPrizeStructure_File.VarLabel(Var) : OddsCategoryLabel;
        const string OddsCategoryLabel = "Roll Threshold (odds)";
        public string OffsetLabel => $"@0x{ValueOffset:X}";
        public string ValueCaption => IsPrize ? "prize idx" : (IsUpperBound ? "roll ≤" : "roll ≥");

        // In the prize tree the placement is already the parent node, so the row leads with the resolved reward.
        public string Header => IsPrize
            ? rewardResolver!(Value)
            : $"Roll threshold ({(IsUpperBound ? "upper ≤" : "lower ≥")})";

        public string Detail => IsPrize
            ? $"prize index {Value}"
            : (IsUpperBound
                ? $"catches rolls ≤ {Value} / 100 · widen = better odds"
                : $"starts at roll ≥ {Value} / 100");

        public string SearchBlob => IsPrize
            ? $"{Competition} {AwardLabel} sorteio {DrawNumber} {Value} 0x{Value:X} {Header}"
            : $"odds {Header} {Value} 0x{Value:X} {Detail}";

        partial void OnValueChanged(ushort value)
        {
            OnPropertyChanged(nameof(Header));
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(SearchBlob));
        }
    }
}
