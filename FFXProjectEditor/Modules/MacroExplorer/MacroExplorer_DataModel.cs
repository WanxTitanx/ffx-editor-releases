using CommunityToolkit.Mvvm.ComponentModel;
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
using System.Text.Json;

namespace FFXProjectEditor.Modules.MacroExplorer
{
    internal partial class MacroExplorer_DataModel : ObservableObject
    {
        readonly List<MacroEntryRow> loadedEntries = new();
        readonly Dictionary<string, MacroSourceState> macroStates = new(StringComparer.OrdinalIgnoreCase);
        MacroSourceState? currentMacroState;

        public ObservableCollection<MacroSourceRow> LoadedSources { get; } = new();
        public ObservableCollection<MacroEntryRow> DisplayedEntries { get; } = new();

        [ObservableProperty] private MacroSourceRow? selectedSource;
        [ObservableProperty] private MacroEntryRow? selectedEntry;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Load a project root to inspect macro dictionaries.";
        [ObservableProperty] private string selectedSourceSummary = "READ_MACROS exposes menu text macro dictionaries such as area names, colorized labels, and scripted text fragments.";
        [ObservableProperty] private string selectedEntrySummary = "Select a macro entry to inspect and edit the resolved regular and simplified strings.";
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;
        [ObservableProperty] private bool isSafeWriterEnabled;
        [ObservableProperty] private string validationSummary = "Macro writer validation is idle until a dictionary is loaded.";
        [ObservableProperty] private bool hasValidationErrors;
        [ObservableProperty] private string editorScopeSummary = "Macro authoring is read-only until a dictionary loads.";

        public bool CanSaveCurrentSource => IsSafeWriterEnabled && EditSession?.HasPendingChanges == true && !HasValidationErrors;
        public bool CanDiscardCurrentSource => IsSafeWriterEnabled && EditSession?.HasPendingChanges == true;
        public bool CanUndoCurrentSource => IsSafeWriterEnabled && EditSession?.CanUndo == true;
        public bool IsSelectedEntryReadOnly => !IsSafeWriterEnabled;

        public MacroExplorer_DataModel()
        {
            ReloadSources();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        partial void OnSelectedSourceChanged(MacroSourceRow? value)
        {
            if (value == null)
            {
                DisplayedEntries.Clear();
                loadedEntries.Clear();
                SelectedEntry = null;
                currentMacroState = null;
                SetActiveEditSession(null);
                IsSafeWriterEnabled = false;
                EditorScopeSummary = "Macro authoring is read-only until a dictionary loads.";
                RefreshDiagnostics();
                return;
            }

            LoadSelectedSource(value);
        }

        partial void OnSelectedEntryChanged(MacroEntryRow? value)
        {
            SelectedEntrySummary = value == null
                ? "Select a macro entry to inspect and edit the resolved regular and simplified strings."
                : $"{value.EntryLabel} · {value.Summary}";
        }

        partial void OnIsSafeWriterEnabledChanged(bool value) => OnPropertyChanged(nameof(IsSelectedEntryReadOnly));

        public void RefreshFromDisk()
        {
            foreach (MacroSourceState state in macroStates.Values)
                state.Session?.Dispose();

            macroStates.Clear();
            currentMacroState = null;
            SetActiveEditSession(null);
            ReloadSources();
        }

        public bool SaveCurrentSource()
        {
            if (!ValidateCurrentSourceForSave() || EditSession == null)
                return false;

            EditSession.Save();
            RefreshDiagnostics();
            return true;
        }

        public void UndoCurrentSource()
        {
            EditSession?.Undo();
            RefreshDiagnostics();
        }

        public void DiscardCurrentSource()
        {
            EditSession?.Discard();
            RefreshDiagnostics();
        }

        void SetActiveEditSession(ByteSnapshotEditorSession? session)
        {
            EditSession = session;
            OnPropertyChanged(nameof(CanSaveCurrentSource));
            OnPropertyChanged(nameof(CanDiscardCurrentSource));
            OnPropertyChanged(nameof(CanUndoCurrentSource));
        }

        void ReloadSources()
        {
            LoadedSources.Clear();
            loadedEntries.Clear();
            DisplayedEntries.Clear();
            SelectedEntry = null;
            IsSafeWriterEnabled = false;
            HasValidationErrors = false;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                return;
            }

            string usPath = Project_Service.Instance.Path_MacroDictionaryUs;
            if (File.Exists(usPath))
            {
                LoadedSources.Add(new MacroSourceRow
                {
                    DisplayName = "US Macro Dictionary",
                    RelativePath = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, usPath),
                    AbsolutePath = usPath,
                    Decoder = FfxEncoding.UsDecoder,
                    Summary = "new_uspc/menu/macrodic.dcp"
                });
            }

            string jpPath = Path.Combine(Project_Service.Instance.ProjectPath!, "jppc", "menu", "macrodic.dcp");
            if (File.Exists(jpPath))
            {
                LoadedSources.Add(new MacroSourceRow
                {
                    DisplayName = "JP Macro Dictionary",
                    RelativePath = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, jpPath),
                    AbsolutePath = jpPath,
                    Decoder = FfxEncoding.JpDecoder,
                    Summary = "jppc/menu/macrodic.dcp"
                });
            }

            LoadSummary = $"Prepared {LoadedSources.Count} macro dictionary sources.";
            SelectedSource = LoadedSources.FirstOrDefault();
        }

        void LoadSelectedSource(MacroSourceRow source)
        {
            try
            {
                MacroSourceState state = GetOrCreateMacroState(source);
                currentMacroState = state;

                loadedEntries.Clear();
                foreach (MacroEntryRow row in state.Records)
                    loadedEntries.Add(row);

                SetActiveEditSession(state.Session);
                IsSafeWriterEnabled = true;
                EditorScopeSummary = "Macro authoring is live: Regular and Simplified strings with save, undo, discard, validation, and structural round-trip checks. Unchanged chunks stay byte-identical; only edited chunks rebuild.";
                SelectedSourceSummary = $"{source.RelativePath} · {state.Records.Count} macro strings. Safe write path rebuilds the chunk string-pool and 16-slot offset table automatically.";
                LoadSummary = $"Loaded {state.Records.Count} macro entries from {source.DisplayName}.";

                ApplyFilter();
                SelectedEntry = DisplayedEntries.FirstOrDefault();
                RefreshDiagnostics();
            }
            catch (Exception ex)
            {
                loadedEntries.Clear();
                DisplayedEntries.Clear();
                SelectedEntry = null;
                currentMacroState = null;
                SetActiveEditSession(null);
                IsSafeWriterEnabled = false;
                LoadSummary = $"Failed to read {source.DisplayName}: {ex.Message}";
                RefreshDiagnostics();
            }
        }

        void ApplyFilter()
        {
            string normalizedFilter = FilterText.Trim();
            MacroEntryRow? previousSelection = SelectedEntry;

            DisplayedEntries.Clear();
            foreach (MacroEntryRow row in loadedEntries)
            {
                if (normalizedFilter.Length == 0 || row.SearchBlob.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
                    DisplayedEntries.Add(row);
            }

            SelectedEntry = previousSelection != null && DisplayedEntries.Contains(previousSelection)
                ? previousSelection
                : DisplayedEntries.FirstOrDefault();
        }

        MacroSourceState GetOrCreateMacroState(MacroSourceRow source)
        {
            if (macroStates.TryGetValue(source.AbsolutePath, out MacroSourceState? existing))
                return existing;

            byte[] bytes = File.ReadAllBytes(source.AbsolutePath);
            MacroDictionary_File file = MacroDictionary_File.Read(bytes, source.Decoder);
            ObservableCollection<MacroEntryRow> records = BuildMacroRecords(file);

            MacroSourceState state = new()
            {
                Source = source,
                Decoder = source.Decoder,
                File = file,
                Records = records
            };

            foreach (MacroEntryRow row in records)
                row.PropertyChanged += (_, e) => MacroRecordChanged(state, row, e);

            state.Session = new ByteSnapshotEditorSession(
                () => CaptureMacroSnapshot(state),
                snapshot => RestoreMacroSnapshot(state, snapshot),
                snapshot => PersistMacroSnapshot(state, snapshot),
                source.DisplayName.ToLowerInvariant(),
                CaptureMacroSnapshot(state));

            // The session marks pending/undo state on a debounce timer; mirror those changes onto the
            // DataModel's Can* commands so Save/Undo/Discard re-enable after the user edits again.
            state.Session.PropertyChanged += (_, e) =>
            {
                if (!ReferenceEquals(currentMacroState, state))
                    return;

                if (e.PropertyName is nameof(ByteSnapshotEditorSession.HasPendingChanges)
                    or nameof(ByteSnapshotEditorSession.CanUndo))
                {
                    OnPropertyChanged(nameof(CanSaveCurrentSource));
                    OnPropertyChanged(nameof(CanDiscardCurrentSource));
                    OnPropertyChanged(nameof(CanUndoCurrentSource));
                }
            };

            macroStates[source.AbsolutePath] = state;
            return state;
        }

        static ObservableCollection<MacroEntryRow> BuildMacroRecords(MacroDictionary_File file)
        {
            ObservableCollection<MacroEntryRow> records = [];

            foreach (MacroDictionary_Chunk chunk in file.Chunks)
            {
                foreach (MacroDictionary_Entry entry in chunk.Entries)
                {
                    MacroEntryRow row = new()
                    {
                        ChunkIndex = entry.ChunkIndex,
                        EntryIndex = entry.EntryIndex,
                        EntryLabel = entry.EntryLabel,
                        MacroIdHex = entry.MacroIdHex,
                        ChunkLabel = chunk.IndexLabel,
                        RegularText = entry.RegularText,
                        SimplifiedText = entry.SimplifiedText
                    };

                    row.RefreshPresentation();
                    records.Add(row);
                }
            }

            return records;
        }

        void MacroRecordChanged(MacroSourceState state, MacroEntryRow row, PropertyChangedEventArgs e)
        {
            if (state.SuppressRecordTracking)
                return;

            if (e.PropertyName is nameof(MacroEntryRow.RegularText) or nameof(MacroEntryRow.SimplifiedText))
            {
                row.RefreshPresentation();
                state.Session?.NotifyPotentialMutation();

                if (ReferenceEquals(currentMacroState, state))
                    RefreshDiagnostics();
            }
        }

        static byte[] CaptureMacroSnapshot(MacroSourceState state)
        {
            MacroSnapshotRow[] rows = state.Records
                .Select(r => new MacroSnapshotRow
                {
                    ChunkIndex = r.ChunkIndex,
                    EntryIndex = r.EntryIndex,
                    Regular = r.RegularText,
                    Simplified = r.SimplifiedText
                })
                .ToArray();

            return JsonSerializer.SerializeToUtf8Bytes(rows);
        }

        void RestoreMacroSnapshot(MacroSourceState state, byte[] snapshotBytes)
        {
            MacroSnapshotRow[]? rows = JsonSerializer.Deserialize<MacroSnapshotRow[]>(snapshotBytes);
            if (rows == null)
                return;

            Dictionary<(int, int), MacroSnapshotRow> map = rows.ToDictionary(r => (r.ChunkIndex, r.EntryIndex));

            state.SuppressRecordTracking = true;
            try
            {
                foreach (MacroEntryRow row in state.Records)
                {
                    if (!map.TryGetValue((row.ChunkIndex, row.EntryIndex), out MacroSnapshotRow? snap))
                        continue;

                    row.RegularText = snap.Regular;
                    row.SimplifiedText = snap.Simplified;
                    row.RefreshPresentation();
                }
            }
            finally
            {
                state.SuppressRecordTracking = false;
            }

            if (ReferenceEquals(currentMacroState, state))
            {
                ApplyFilter();
                RefreshDiagnostics();
            }
        }

        void PersistMacroSnapshot(MacroSourceState state, byte[] snapshotBytes)
        {
            byte[] fileBytes = BuildMacroFileBytes(state, snapshotBytes);
            File.WriteAllBytes(state.Source.AbsolutePath, fileBytes);
            state.File = MacroDictionary_File.Read(fileBytes, state.Decoder);

            if (ReferenceEquals(currentMacroState, state))
                RefreshDiagnostics();
        }

        static byte[] BuildMacroFileBytes(MacroSourceState state, byte[] snapshotBytes)
        {
            MacroSnapshotRow[]? rows = JsonSerializer.Deserialize<MacroSnapshotRow[]>(snapshotBytes);
            if (rows == null)
                throw new InvalidDataException("Macro snapshot could not be read.");

            Dictionary<(int, int), MacroSnapshotRow> map = rows.ToDictionary(r => (r.ChunkIndex, r.EntryIndex));

            MacroDictionary_File file = MacroDictionary_File.Read(File.ReadAllBytes(state.Source.AbsolutePath), state.Decoder);
            foreach (MacroDictionary_Chunk chunk in file.Chunks)
            {
                foreach (MacroDictionary_Entry entry in chunk.Entries)
                {
                    if (!map.TryGetValue((entry.ChunkIndex, entry.EntryIndex), out MacroSnapshotRow? snap))
                        continue;

                    entry.RegularText = TextBinary_Util.NormalizeText(snap.Regular);
                    entry.SimplifiedText = TextBinary_Util.NormalizeText(snap.Simplified);
                }
            }

            byte[] rebuilt = file.Write(state.Decoder);
            VerifyMacroRoundTrip(map, MacroDictionary_File.Read(rebuilt, state.Decoder));
            return rebuilt;
        }

        static void VerifyMacroRoundTrip(Dictionary<(int, int), MacroSnapshotRow> map, MacroDictionary_File rebuilt)
        {
            foreach (MacroDictionary_Chunk chunk in rebuilt.Chunks)
            {
                foreach (MacroDictionary_Entry entry in chunk.Entries)
                {
                    if (!map.TryGetValue((entry.ChunkIndex, entry.EntryIndex), out MacroSnapshotRow? snap))
                        continue;

                    EnsureRoundTripField($"{entry.EntryLabel} Regular", TextBinary_Util.NormalizeText(snap.Regular), TextBinary_Util.NormalizeText(entry.RegularText));
                    EnsureRoundTripField($"{entry.EntryLabel} Simplified", TextBinary_Util.NormalizeText(snap.Simplified), TextBinary_Util.NormalizeText(entry.SimplifiedText));
                }
            }
        }

        static void EnsureRoundTripField(string label, string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidDataException($"Macro rebuild round-trip mismatch on {label}.");
        }

        bool ValidateCurrentSourceForSave()
        {
            if (currentMacroState == null)
                return false;

            MacroSourceState state = currentMacroState;

            if (!ValidateMacroRecords(state, out List<string> issues))
            {
                HasValidationErrors = true;
                ValidationSummary = issues.FirstOrDefault() ?? "Macro validation failed.";
                OnPropertyChanged(nameof(CanSaveCurrentSource));
                return false;
            }

            try
            {
                BuildMacroFileBytes(state, CaptureMacroSnapshot(state));
                HasValidationErrors = false;
                ValidationSummary = "Validation clean. Supported FFX control tags and charset are writable here; unchanged chunks stay byte-identical and the offset table rebuilds automatically.";
                OnPropertyChanged(nameof(CanSaveCurrentSource));
                return true;
            }
            catch (Exception ex)
            {
                HasValidationErrors = true;
                ValidationSummary = $"Save blocked: {ex.Message}";
                OnPropertyChanged(nameof(CanSaveCurrentSource));
                return false;
            }
        }

        bool ValidateMacroRecords(MacroSourceState state, out List<string> issues)
        {
            issues = [];
            Dictionary<(int, int), (byte[] reg, byte[] simp)> baseline = BuildBaselineMap(state.File);

            foreach (MacroEntryRow row in state.Records)
            {
                baseline.TryGetValue((row.ChunkIndex, row.EntryIndex), out (byte[] reg, byte[] simp) b);
                ValidateMacroField(row.EntryLabel, "Regular", row.RegularText, b.reg, state.Decoder, issues);
                ValidateMacroField(row.EntryLabel, "Simplified", row.SimplifiedText, b.simp, state.Decoder, issues);
            }

            return issues.Count == 0;
        }

        static Dictionary<(int, int), (byte[] reg, byte[] simp)> BuildBaselineMap(MacroDictionary_File file)
        {
            Dictionary<(int, int), (byte[] reg, byte[] simp)> map = new();
            foreach (MacroDictionary_Chunk chunk in file.Chunks)
                foreach (MacroDictionary_Entry entry in chunk.Entries)
                    map[(entry.ChunkIndex, entry.EntryIndex)] = (entry.RegularScriptBytes, entry.SimplifiedScriptBytes);
            return map;
        }

        static void ValidateMacroField(string label, string field, string? currentText, byte[]? baselineBytes, Dictionary<byte, char> decoder, List<string> issues)
        {
            string current = TextBinary_Util.NormalizeText(currentText);
            string original = TextBinary_Util.NormalizeText(baselineBytes == null || baselineBytes.Length == 0
                ? string.Empty
                : TextBinary_Util.DecodeScriptToString(baselineBytes, decoder, true));

            // Unchanged text is byte-preserved by the writer, so it never needs re-encode validation.
            if (string.Equals(current, original, StringComparison.Ordinal))
                return;

            TextWriteValidationResult result = TextWriteValidation_Util.ValidateForReencode(current);
            if (!result.CanReencode)
                issues.Add($"{label} {field}: {result.Summary}");
        }

        void RefreshDiagnostics()
        {
            if (currentMacroState == null)
            {
                HasValidationErrors = false;
                ValidationSummary = "Macro writer validation is idle until a dictionary is loaded.";
            }
            else if (ValidateMacroRecords(currentMacroState, out List<string> issues))
            {
                HasValidationErrors = false;
                ValidationSummary = "Validation clean. Supported FFX control tags and charset are writable here; unchanged chunks stay byte-identical and offsets rebuild automatically.";
            }
            else
            {
                HasValidationErrors = true;
                ValidationSummary = issues.FirstOrDefault() ?? "Macro validation failed.";
            }

            OnPropertyChanged(nameof(CanSaveCurrentSource));
            OnPropertyChanged(nameof(CanDiscardCurrentSource));
            OnPropertyChanged(nameof(CanUndoCurrentSource));
            OnPropertyChanged(nameof(IsSelectedEntryReadOnly));
        }
    }

    internal sealed class MacroSourceRow
    {
        public required string DisplayName { get; init; }
        public required string RelativePath { get; init; }
        public required string AbsolutePath { get; init; }
        public required string Summary { get; init; }
        public required Dictionary<byte, char> Decoder { get; init; }
    }

    internal sealed class MacroSourceState
    {
        public required MacroSourceRow Source { get; init; }
        public required Dictionary<byte, char> Decoder { get; init; }
        public required MacroDictionary_File File { get; set; }
        public required ObservableCollection<MacroEntryRow> Records { get; init; }
        public ByteSnapshotEditorSession Session { get; set; } = null!;
        public bool SuppressRecordTracking { get; set; }
    }

    internal sealed class MacroSnapshotRow
    {
        public int ChunkIndex { get; set; }
        public int EntryIndex { get; set; }
        public string Regular { get; set; } = string.Empty;
        public string Simplified { get; set; } = string.Empty;
    }

    internal partial class MacroEntryRow : ObservableObject
    {
        public required int ChunkIndex { get; init; }
        public required int EntryIndex { get; init; }
        public required string EntryLabel { get; init; }
        public required string MacroIdHex { get; init; }
        public required string ChunkLabel { get; init; }

        [ObservableProperty] private string regularText = string.Empty;
        [ObservableProperty] private string simplifiedText = string.Empty;
        [ObservableProperty] private string summary = string.Empty;
        [ObservableProperty] private string searchBlob = string.Empty;

        public bool HasDistinctSimplified => !string.Equals(RegularText, SimplifiedText, StringComparison.Ordinal);

        public void RefreshPresentation()
        {
            string regular = (RegularText ?? string.Empty).Replace("\n", " ").Trim();
            string preview = regular.Length == 0 ? "(Empty)" : (regular.Length > 60 ? regular[..60] + "…" : regular);
            Summary = HasDistinctSimplified ? $"{preview} · split simplified" : preview;
            SearchBlob = $"{MacroIdHex} {RegularText} {SimplifiedText} {ChunkLabel}";
        }
    }
}
