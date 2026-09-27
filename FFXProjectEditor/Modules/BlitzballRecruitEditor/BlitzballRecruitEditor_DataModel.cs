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

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.BlitzballRecruitEditor
{
    // Editor for blitzball RECRUITMENT: change WHICH player is recruited at each field-event location by
    // patching the 1-byte player-id immediate inside that event's ATEL bytecode (signature
    // AE 28 00 14 AE <id> 00 A3). Game-file edit, no EXE patch; the writer (BlitzballRecruit_File) is
    // RT0 isolation-proven (--blitzball-recruit-rt0). One ByteSnapshotEditorSession per event .ebp.
    internal partial class BlitzballRecruitEditor_DataModel : ObservableObject
    {
        string eventObjRoot = string.Empty;
        IReadOnlyList<BlitzballPlayerOption> playerOptions = Array.Empty<BlitzballPlayerOption>();
        readonly Dictionary<string, RecruitEventState> states = new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<RecruitEventRow> Events { get; } = new();
        public ObservableCollection<RecruitSiteRow> Sites { get; } = new();

        [ObservableProperty] private RecruitEventRow? selectedEvent;
        [ObservableProperty] private string loadSummary = "Loading recruitment events...";
        [ObservableProperty] private string scopeSummary = Strings.F2_change_which_player_is_recruited_at_each_01fee71a;
        [ObservableProperty] private string selectedEventSummary = Strings.F2_select_a_recruitment_event_to_edit_who_i_86bd8ee8;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;

        public BlitzballRecruitEditor_DataModel() => Reload();

        public void Save() => EditSession?.Save();
        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();

        public void RefreshFromDisk()
        {
            foreach (RecruitEventState s in states.Values) s.Session?.Dispose();
            states.Clear();
            EditSession = null;
            Reload();
        }

        void Reload()
        {
            Events.Clear();
            Sites.Clear();
            SelectedEvent = null;
            EditSession = null;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                return;
            }

            eventObjRoot = Project_Service.Instance.Path_Event;
            playerOptions = BuildPlayerOptions();

            foreach ((string id, string area) in BlitzballRecruitEvents.Events)
            {
                string path = BlitzballRecruitEvents.EventPath(eventObjRoot, id);
                Events.Add(new RecruitEventRow { EventId = id, Area = area, Path = path, Exists = File.Exists(path) });
            }

            LoadSummary = $"{Events.Count(e => e.Exists)}/{Events.Count} recruitment events found in the workspace.";
            ScopeSummary = "Writable (Lab): changes WHICH player is recruited at each location by patching the 1-byte "
                + "player-id immediate in the field event .ebp (RT0 isolation proven, --blitzball-recruit-rt0). "
                + "Recruitment availability and story-gating stay in the event logic — this only re-points the slot.";
            SelectedEvent = Events.FirstOrDefault(e => e.Exists) ?? Events.FirstOrDefault();
        }

        partial void OnSelectedEventChanged(RecruitEventRow? value)
        {
            Sites.Clear();
            if (value == null || !value.Exists)
            {
                EditSession = null;
                SelectedEventSummary = value == null ? Strings.F2_select_a_recruitment_event_afd54351 : $"{value.EventId}: file not found in the workspace.";
                return;
            }

            try
            {
                RecruitEventState state = GetOrCreate(value);
                foreach (RecruitSiteRow r in state.SiteRows) Sites.Add(r);
                EditSession = state.Session;
                SelectedEventSummary = $"{value.EventId} · {value.Area} — {state.SiteRows.Count} recruit site(s) "
                    + $"(each player usually appears twice = two code branches).";
            }
            catch (Exception ex)
            {
                EditSession = null;
                SelectedEventSummary = $"Failed to read {value.EventId}: {ex.Message}";
            }
        }

        RecruitEventState GetOrCreate(RecruitEventRow row)
        {
            if (states.TryGetValue(row.EventId, out RecruitEventState? existing))
                return existing;

            byte[] orig = File.ReadAllBytes(row.Path);
            Event_File ev = Event_File.Read(row.EventId, orig);
            List<BlitzballRecruitSite> sites = BlitzballRecruit_File.FindSites(ev);

            var state = new RecruitEventState { Row = row, OriginalBytes = orig };
            foreach (BlitzballRecruitSite s in sites)
            {
                var sr = new RecruitSiteRow { ScriptOffset = s.ScriptOffset, PlayerOptions = playerOptions, PlayerId = s.PlayerId };
                sr.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(RecruitSiteRow.PlayerId))
                        state.Session?.NotifyPotentialMutation();
                };
                state.SiteRows.Add(sr);
            }

            state.Session = new ByteSnapshotEditorSession(
                () => BuildEventBytes(state),
                bytes => RestoreEvent(state, bytes),
                bytes => PersistEvent(state, bytes),
                $"recruits · {row.EventId}",
                BuildEventBytes(state))
            {
                RevertWritesToDisk = true
            };
            states[row.EventId] = state;
            return state;
        }

        byte[] BuildEventBytes(RecruitEventState state)
        {
            Event_File ev = Event_File.Read(state.Row.EventId, state.OriginalBytes);
            foreach (RecruitSiteRow sr in state.SiteRows)
                ev.PatchScriptByte(sr.ScriptOffset, sr.PlayerId);
            return ev.Write();
        }

        void RestoreEvent(RecruitEventState state, byte[] bytes)
        {
            Event_File ev = Event_File.Read(state.Row.EventId, bytes);
            List<BlitzballRecruitSite> sites = BlitzballRecruit_File.FindSites(ev);
            for (int i = 0; i < state.SiteRows.Count && i < sites.Count; i++)
                state.SiteRows[i].PlayerId = sites[i].PlayerId;
        }

        void PersistEvent(RecruitEventState state, byte[] bytes)
        {
            File.WriteAllBytes(state.Row.Path, bytes);
            state.OriginalBytes = bytes;
        }

        IReadOnlyList<BlitzballPlayerOption> BuildPlayerOptions()
        {
            IReadOnlyDictionary<int, string> names = TryLoadNames();
            var list = new List<BlitzballPlayerOption>(60);
            for (int p = 0; p < 60; p++)
            {
                string nm = names.TryGetValue(p, out string? n) && !string.IsNullOrWhiteSpace(n)
                    ? n
                    : (p == 0 ? "Tidus (player-named)" : p == 1 ? "Wakka (player-named)" : $"Player {p}");
                list.Add(new BlitzballPlayerOption { Id = (byte)p, Name = nm });
            }
            return list;
        }

        static IReadOnlyDictionary<int, string> TryLoadNames()
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
                for (int p = 0; p < 60; p++)
                {
                    string? nm = BlitzballPlayerNames.TryGetName(dict, p);
                    if (!string.IsNullOrWhiteSpace(nm))
                        map[p] = nm!;
                }
            }
            catch { /* names are a convenience for the dropdown; never block recruit editing */ }
            return map;
        }
    }

    internal sealed class RecruitEventState
    {
        public required RecruitEventRow Row { get; init; }
        public required byte[] OriginalBytes { get; set; }
        public List<RecruitSiteRow> SiteRows { get; } = new();
        public ByteSnapshotEditorSession Session { get; set; } = null!;
    }

    internal sealed class RecruitEventRow
    {
        public required string EventId { get; init; }
        public required string Area { get; init; }
        public required string Path { get; init; }
        public required bool Exists { get; init; }

        public string Header => $"{EventId} · {Area}";
        public string StatusLabel => Exists ? string.Empty : "(not in workspace)";
        public string SearchBlob => $"{EventId} {Area}";
    }

    internal partial class RecruitSiteRow : ObservableObject
    {
        public required int ScriptOffset { get; init; }
        public required IReadOnlyList<BlitzballPlayerOption> PlayerOptions { get; init; }

        [ObservableProperty] private byte playerId;

        public string OffsetLabel => $"@0x{ScriptOffset:X}";
        public BlitzballPlayerOption? SelectedPlayer
        {
            get => PlayerOptions.FirstOrDefault(o => o.Id == PlayerId);
            set { if (value != null) PlayerId = value.Id; }
        }
        public string CurrentLabel => PlayerOptions.FirstOrDefault(o => o.Id == PlayerId)?.Label ?? $"0x{PlayerId:X2}";

        partial void OnPlayerIdChanged(byte value)
        {
            OnPropertyChanged(nameof(SelectedPlayer));
            OnPropertyChanged(nameof(CurrentLabel));
        }
    }
}
