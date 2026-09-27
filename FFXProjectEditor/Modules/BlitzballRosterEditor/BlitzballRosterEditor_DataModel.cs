using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Blitzball;
using FFXProjectEditor.FfxLib.Event;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.BlitzballRosterEditor
{
    // Visual editor for the 60 blitzball players' base STAT-GROWTH curves, which live in the GAME FILE
    // bltz0002.ebp as ATEL eventData variables 0x126..0x12E (HP/SP/AT/EN/PA/SH/BL/CA). Each (player, stat)
    // is 4 floats (a, b, c, growthType); the writer (BlitzballRoster_File, gate --blitzball-roster-rt0) is
    // RT0 byte-identity proven. Names are READ-ONLY here (resolved from macrodic.dcp; edit them in Macro
    // Explorer); learned/equipped techs, level, EXP, cost are save-side (out of this game-file editor's scope).
    internal partial class BlitzballRosterEditor_DataModel : ObservableObject
    {
        byte[] originalBytes = Array.Empty<byte>();
        string bltzPath = string.Empty;

        public ObservableCollection<BlitzballPlayerRow> LoadedPlayers { get; } = new();
        public ObservableCollection<BlitzballPlayerRow> DisplayedPlayers { get; } = new();

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Loading bltz0002.ebp...";
        [ObservableProperty] private string scopeSummary =
            "Edits the 60 players' base stat-growth curves in the game file bltz0002.ebp.";
        [ObservableProperty] private BlitzballPlayerRow? selectedPlayer;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;

        public BlitzballRosterEditor_DataModel() => LoadFromDisk();

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        public void Save() => EditSession?.Save();
        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();
        public void RefreshFromDisk() => LoadFromDisk();

        static string ResolveBltzPath()
            => Path.Combine(Project_Service.Instance.Path_Event, "bl", "bltz0002", "bltz0002.ebp");

        void LoadFromDisk()
        {
            EditSession?.Dispose();
            EditSession = null;
            ClearRows();

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                return;
            }

            bltzPath = ResolveBltzPath();
            if (!File.Exists(bltzPath))
            {
                LoadSummary = $"bltz0002.ebp not found in the loaded workspace ({bltzPath}).";
                return;
            }

            try
            {
                originalBytes = File.ReadAllBytes(bltzPath);
                LoadRowsFromBytes(originalBytes, TryLoadNames(), preserveIndex: SelectedPlayer?.Index);
                EditSession = new ByteSnapshotEditorSession(
                    BuildFile, RestoreFromBytes, PersistBytes, "blitzball roster", BuildFile())
                {
                    RevertWritesToDisk = true
                };
            }
            catch (Exception ex)
            {
                ClearRows();
                LoadSummary = $"Failed to read bltz0002.ebp: {ex.Message}";
            }
        }

        void ClearRows()
        {
            foreach (BlitzballPlayerRow row in LoadedPlayers) row.Unsubscribe(PlayerStatChanged);
            LoadedPlayers.Clear();
            DisplayedPlayers.Clear();
            SelectedPlayer = null;
        }

        void LoadRowsFromBytes(byte[] bytes, IReadOnlyDictionary<int, string> names, int? preserveIndex)
        {
            foreach (BlitzballPlayerRow row in LoadedPlayers) row.Unsubscribe(PlayerStatChanged);
            LoadedPlayers.Clear();
            DisplayedPlayers.Clear();

            Event_File ev = Event_File.Read(BlitzballRoster_File.EventId, bytes);
            BlitzballStatGrowth[,] grid = BlitzballRoster_File.ReadAll(ev);
            for (int p = 0; p < BlitzballRoster_File.PlayerCount; p++)
            {
                names.TryGetValue(p, out string? nm);
                BlitzballPlayerRow row = BlitzballPlayerRow.Wrap(p, nm, grid);
                row.Subscribe(PlayerStatChanged);
                LoadedPlayers.Add(row);
            }

            ApplyFilter();
            SelectedPlayer = (preserveIndex.HasValue
                                ? LoadedPlayers.FirstOrDefault(r => r.Index == preserveIndex.Value)
                                : null)
                             ?? DisplayedPlayers.FirstOrDefault();

            LoadSummary = $"Loaded {LoadedPlayers.Count} blitzball players from bltz0002.ebp (stat-growth ATEL vars 0x126..0x12E).";
            ScopeSummary = "Writable (Lab): edits the 60 players' base stat-growth curves in the GAME FILE bltz0002.ebp "
                + "(RT0 byte-identity proven, --blitzball-roster-rt0). Names are read-only here — edit them in Macro Explorer. "
                + "Techs/level/EXP/cost are save-side, not part of this game-file editor.";
        }

        IReadOnlyDictionary<int, string> TryLoadNames()
        {
            var map = new Dictionary<int, string>();
            try
            {
                string usPath = Project_Service.Instance.Path_MacroDictionaryUs;
                string macroPath = File.Exists(usPath)
                    ? usPath
                    : Path.Combine(Project_Service.Instance.ProjectPath!, "jppc", "menu", "macrodic.dcp");
                if (!File.Exists(macroPath))
                    return map;

                Dictionary<byte, char> decoder = macroPath.Contains("jppc", StringComparison.OrdinalIgnoreCase)
                    ? FfxEncoding.JpDecoder
                    : FfxEncoding.UsDecoder;
                MacroDictionary_File dict = MacroDictionary_File.Read(File.ReadAllBytes(macroPath), decoder);
                for (int p = 0; p < BlitzballRoster_File.PlayerCount; p++)
                {
                    string? nm = BlitzballPlayerNames.TryGetName(dict, p);
                    if (!string.IsNullOrWhiteSpace(nm))
                        map[p] = nm!;
                }
            }
            catch { /* names are a nice-to-have read-only label; never block the roster load */ }
            return map;
        }

        byte[] BuildFile()
        {
            var grid = new BlitzballStatGrowth[BlitzballRoster_File.PlayerCount, BlitzballRoster_File.Stats.Count];
            foreach (BlitzballPlayerRow row in LoadedPlayers)
                for (int s = 0; s < row.Stats.Count; s++)
                    grid[row.Index, s] = row.Stats[s].ToGrowth();

            Event_File ev = Event_File.Read(BlitzballRoster_File.EventId, originalBytes);
            return BlitzballRoster_File.WriteAll(ev, grid);
        }

        void RestoreFromBytes(byte[] bytes) => LoadRowsFromBytes(bytes, TryLoadNames(), SelectedPlayer?.Index);

        void PersistBytes(byte[] bytes)
        {
            File.WriteAllBytes(bltzPath, bytes);
            originalBytes = bytes;
        }

        void PlayerStatChanged(object? sender, PropertyChangedEventArgs e) => EditSession?.NotifyPotentialMutation();

        void ApplyFilter()
        {
            DisplayedPlayers.Clear();
            string filter = FilterText.Trim();
            foreach (BlitzballPlayerRow row in LoadedPlayers)
                if (filter.Length == 0 || row.SearchBlob.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    DisplayedPlayers.Add(row);

            if (SelectedPlayer != null && !DisplayedPlayers.Contains(SelectedPlayer))
                SelectedPlayer = DisplayedPlayers.FirstOrDefault();
        }
    }

    internal sealed class BlitzballPlayerRow
    {
        public required int Index { get; init; }
        public required string Name { get; init; }
        public required IReadOnlyList<BlitzballStatRow> Stats { get; init; }

        public string IndexLabel => $"#{Index:D2}";
        public string DisplayName => !string.IsNullOrWhiteSpace(Name)
            ? Name
            : (Index <= 1 ? "(player-named lead)" : $"Player {Index}");
        public string Header => $"#{Index:D2} · {DisplayName}";
        public string SearchBlob => $"{Index} {Index:D2} {DisplayName}";

        public static BlitzballPlayerRow Wrap(int index, string? name, BlitzballStatGrowth[,] grid)
        {
            var stats = new List<BlitzballStatRow>(BlitzballRoster_File.Stats.Count);
            for (int s = 0; s < BlitzballRoster_File.Stats.Count; s++)
                stats.Add(BlitzballStatRow.Wrap(BlitzballRoster_File.Stats[s].Name, grid[index, s]));
            return new BlitzballPlayerRow { Index = index, Name = name ?? string.Empty, Stats = stats };
        }

        public void Subscribe(PropertyChangedEventHandler handler)
        {
            foreach (BlitzballStatRow st in Stats) st.PropertyChanged += handler;
        }

        public void Unsubscribe(PropertyChangedEventHandler handler)
        {
            foreach (BlitzballStatRow st in Stats) st.PropertyChanged -= handler;
        }
    }

    internal partial class BlitzballStatRow : ObservableObject
    {
        public required string StatName { get; init; }

        [ObservableProperty] private float a;
        [ObservableProperty] private float b;
        [ObservableProperty] private float c;
        [ObservableProperty] private float growthType;

        public static BlitzballStatRow Wrap(string name, BlitzballStatGrowth g)
            => new() { StatName = name, A = g.A, B = g.B, C = g.C, GrowthType = g.GrowthType };

        public BlitzballStatGrowth ToGrowth() => new() { A = A, B = B, C = C, GrowthType = GrowthType };

        // stat(Lv) per growthType (decoded from FFXDataParser blitzballGrowthToString).
        public string AtLv1 => Fmt(ValueAt(1));
        public string AtLv50 => Fmt(ValueAt(50));
        public string AtLv99 => Fmt(ValueAt(99));
        public string Formula => DescribeFormula();

        partial void OnAChanged(float value) => NotifyComputed();
        partial void OnBChanged(float value) => NotifyComputed();
        partial void OnCChanged(float value) => NotifyComputed();
        partial void OnGrowthTypeChanged(float value) => NotifyComputed();

        void NotifyComputed()
        {
            OnPropertyChanged(nameof(AtLv1));
            OnPropertyChanged(nameof(AtLv50));
            OnPropertyChanged(nameof(AtLv99));
            OnPropertyChanged(nameof(Formula));
        }

        double ValueAt(int lv) => GrowthType switch
        {
            -1f => 1,
            1f => A + B * Math.Pow(lv, C),
            2f => A + (B * lv) - (C * (double)lv * lv),
            3f => A + (B * lv) + (C * (double)lv * lv),
            _ => A + (B * lv),
        };

        static string Fmt(double v) => ((long)Math.Round(v)).ToString();

        string DescribeFormula() => GrowthType switch
        {
            -1f => "constant 1",
            1f => $"{A} + {B}·Lv^{C}",
            2f => $"{A} + {B}·Lv − {C}·Lv²",
            3f => $"{A} + {B}·Lv + {C}·Lv²",
            _ => $"{A} + {B}·Lv",
        };
    }
}
