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

namespace FFXProjectEditor.Modules.BattleTextExplorer
{
    // Editable surface for btl_txt.bin (battle text). Mirrors the Weapon Name Explorer safe-writer
    // pattern: each meaningful entry word is a row; the writer is append-only (BtlTextTable_File) so the
    // original overlapping pool is never disturbed. Control bytes survive via the reversible lossless
    // codec, so any string — including ones with <Cn> control tokens — is editable and re-encodable.
    internal partial class BattleTextExplorer_DataModel : ObservableObject
    {
        const int MinScriptBytesForRow = 2; // skip 1-byte markers/empties; they stay read-only and preserved

        readonly List<BattleTextRow> loadedRows = new();
        readonly Dictionary<string, BattleTextSourceState> sourceStates = new(StringComparer.OrdinalIgnoreCase);
        BattleTextSourceState? currentState;

        public ObservableCollection<BattleTextSourceRow> LoadedSources { get; } = new();
        public ObservableCollection<BattleTextRow> DisplayedRows { get; } = new();

        [ObservableProperty] private BattleTextSourceRow? selectedSource;
        [ObservableProperty] private BattleTextRow? selectedRow;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Load a project root to inspect battle text tables.";
        [ObservableProperty] private string selectedSourceSummary = "btl_txt.bin holds battle text lines (4 word slots per index). Control codes show as reversible <Cn> tokens.";
        [ObservableProperty] private string selectedRowSummary = "Select a line to inspect and edit its text.";
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;
        [ObservableProperty] private bool isSafeWriterEnabled;
        [ObservableProperty] private string validationSummary = "Battle-text writer validation is idle until a table is loaded.";
        [ObservableProperty] private bool hasValidationErrors;
        [ObservableProperty] private string editorScopeSummary = "Battle-text authoring is read-only until a table loads.";

        public bool CanSaveCurrentSource => IsSafeWriterEnabled && EditSession?.HasPendingChanges == true && !HasValidationErrors;
        public bool CanDiscardCurrentSource => IsSafeWriterEnabled && EditSession?.DiffersFromOriginal == true;
        public bool CanUndoCurrentSource => IsSafeWriterEnabled && EditSession?.CanUndo == true;
        public bool IsSelectedRowReadOnly => !IsSafeWriterEnabled;

        public BattleTextExplorer_DataModel()
        {
            ReloadSources();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        partial void OnSelectedSourceChanged(BattleTextSourceRow? value)
        {
            if (value == null)
            {
                DisplayedRows.Clear();
                loadedRows.Clear();
                SelectedRow = null;
                currentState = null;
                SetActiveEditSession(null);
                IsSafeWriterEnabled = false;
                EditorScopeSummary = "Battle-text authoring is read-only until a table loads.";
                RefreshDiagnostics();
                return;
            }

            LoadSelectedSource(value);
        }

        partial void OnSelectedRowChanged(BattleTextRow? value)
        {
            SelectedRowSummary = value == null
                ? "Select a line to inspect and edit its text."
                : $"{value.RowLabel} · {value.Summary}";
        }

        partial void OnIsSafeWriterEnabledChanged(bool value) => OnPropertyChanged(nameof(IsSelectedRowReadOnly));

        public void RefreshFromDisk()
        {
            foreach (BattleTextSourceState state in sourceStates.Values)
                state.Session?.Dispose();

            sourceStates.Clear();
            currentState = null;
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
            loadedRows.Clear();
            DisplayedRows.Clear();
            SelectedRow = null;
            IsSafeWriterEnabled = false;
            HasValidationErrors = false;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                return;
            }

            string usPath = Path.Combine(Project_Service.Instance.Path_KernelUs, "btl_txt.bin");
            if (File.Exists(usPath))
            {
                LoadedSources.Add(new BattleTextSourceRow
                {
                    DisplayName = "US Battle Text",
                    RelativePath = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, usPath),
                    AbsolutePath = usPath,
                    Decoder = FfxEncoding.UsDecoder,
                    Summary = "new_uspc/battle/kernel/btl_txt.bin"
                });
            }

            // JP source — uses the JpDecoder so its 2-byte font-bank glyphs (cracked 2026-06-06) surface as
            // readable <FTCX:n>/<K:n>/<F2/F3/F5:n> tokens via the lossless codec, instead of <MISS>/<C..>.
            string jpPath = Path.Combine(Project_Service.Instance.Path_Kernel, "btl_txt.bin");
            if (File.Exists(jpPath))
            {
                LoadedSources.Add(new BattleTextSourceRow
                {
                    DisplayName = "JP Battle Text",
                    RelativePath = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, jpPath),
                    AbsolutePath = jpPath,
                    Decoder = FfxEncoding.JpDecoder,
                    Summary = "jppc/battle/kernel/btl_txt.bin (kanji = <FTCX:n>/<K:n>/<F2/F3/F5:n> glyph refs)"
                });
            }

            LoadSummary = $"Prepared {LoadedSources.Count} battle-text source(s).";
            SelectedSource = LoadedSources.FirstOrDefault();
        }

        void LoadSelectedSource(BattleTextSourceRow source)
        {
            try
            {
                BattleTextSourceState state = GetOrCreateState(source);
                currentState = state;

                loadedRows.Clear();
                foreach (BattleTextRow row in state.Records)
                    loadedRows.Add(row);

                SetActiveEditSession(state.Session);
                IsSafeWriterEnabled = true;
                EditorScopeSummary = "Battle-text authoring is live: edit any line with save, undo, discard, validation, and round-trip checks. The writer is append-only, so the original pool is preserved byte-for-byte and only edited lines are appended.";
                SelectedSourceSummary = $"{source.RelativePath} · {state.Records.Count} editable lines across {state.File.EntryCount} indices. Control codes appear as reversible <Cn> tokens.";
                LoadSummary = $"Loaded {state.Records.Count} battle-text lines from {source.DisplayName}.";

                ApplyFilter();
                SelectedRow = DisplayedRows.FirstOrDefault();
                RefreshDiagnostics();
            }
            catch (Exception ex)
            {
                loadedRows.Clear();
                DisplayedRows.Clear();
                SelectedRow = null;
                currentState = null;
                SetActiveEditSession(null);
                IsSafeWriterEnabled = false;
                LoadSummary = $"Failed to read {source.DisplayName}: {ex.Message}";
                RefreshDiagnostics();
            }
        }

        void ApplyFilter()
        {
            string normalizedFilter = FilterText.Trim();
            BattleTextRow? previousSelection = SelectedRow;

            DisplayedRows.Clear();
            foreach (BattleTextRow row in loadedRows)
            {
                if (normalizedFilter.Length == 0 || row.SearchBlob.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
                    DisplayedRows.Add(row);
            }

            SelectedRow = previousSelection != null && DisplayedRows.Contains(previousSelection)
                ? previousSelection
                : DisplayedRows.FirstOrDefault();
        }

        BattleTextSourceState GetOrCreateState(BattleTextSourceRow source)
        {
            if (sourceStates.TryGetValue(source.AbsolutePath, out BattleTextSourceState? existing))
                return existing;

            byte[] bytes = File.ReadAllBytes(source.AbsolutePath);
            BtlTextTable_File file = BtlTextTable_File.Read(bytes, source.Decoder);
            ObservableCollection<BattleTextRow> records = BuildRecords(file);

            BattleTextSourceState state = new()
            {
                Source = source,
                Decoder = source.Decoder,
                File = file,
                Records = records
            };

            foreach (BattleTextRow row in records)
                row.PropertyChanged += (_, e) => RecordChanged(state, row, e);

            state.Session = new ByteSnapshotEditorSession(
                () => CaptureSnapshot(state),
                snapshot => RestoreSnapshot(state, snapshot),
                snapshot => PersistSnapshot(state, snapshot),
                source.DisplayName.ToLowerInvariant(),
                CaptureSnapshot(state))
            {
                // In-place file editor: Undo/Discard also rewrite the file (Discard restores the original
                // as loaded), so disk always matches the screen.
                RevertWritesToDisk = true
            };

            state.Session.PropertyChanged += (_, e) =>
            {
                if (!ReferenceEquals(currentState, state))
                    return;

                if (e.PropertyName is nameof(ByteSnapshotEditorSession.HasPendingChanges)
                    or nameof(ByteSnapshotEditorSession.DiffersFromOriginal)
                    or nameof(ByteSnapshotEditorSession.CanUndo))
                {
                    OnPropertyChanged(nameof(CanSaveCurrentSource));
                    OnPropertyChanged(nameof(CanDiscardCurrentSource));
                    OnPropertyChanged(nameof(CanUndoCurrentSource));
                }
            };

            sourceStates[source.AbsolutePath] = state;
            return state;
        }

        static ObservableCollection<BattleTextRow> BuildRecords(BtlTextTable_File file)
        {
            ObservableCollection<BattleTextRow> records = [];

            foreach (BtlTextEntry entry in file.Entries)
            {
                foreach (BtlTextRef word in entry.Words)
                {
                    if (word.ScriptBytes.Length < MinScriptBytesForRow)
                        continue; // markers/empties stay read-only and are preserved verbatim on write

                    BattleTextRow row = new()
                    {
                        EntryIndex = entry.Index,
                        Slot = word.Slot,
                        EntryLabel = entry.IndexLabel,
                        SlotLabel = word.SlotLabel,
                        OffsetHex = $"{word.Offset:X4}h",
                        Text = word.Text
                    };

                    row.RefreshPresentation();
                    records.Add(row);
                }
            }

            return records;
        }

        void RecordChanged(BattleTextSourceState state, BattleTextRow row, PropertyChangedEventArgs e)
        {
            if (state.SuppressRecordTracking)
                return;

            if (e.PropertyName == nameof(BattleTextRow.Text))
            {
                row.RefreshPresentation();
                state.Session?.NotifyPotentialMutation();

                if (ReferenceEquals(currentState, state))
                    RefreshDiagnostics();
            }
        }

        static byte[] CaptureSnapshot(BattleTextSourceState state)
        {
            BattleTextSnapshotRow[] rows = state.Records
                .Select(r => new BattleTextSnapshotRow { EntryIndex = r.EntryIndex, Slot = r.Slot, Text = r.Text })
                .ToArray();

            return JsonSerializer.SerializeToUtf8Bytes(rows);
        }

        void RestoreSnapshot(BattleTextSourceState state, byte[] snapshotBytes)
        {
            BattleTextSnapshotRow[]? rows = JsonSerializer.Deserialize<BattleTextSnapshotRow[]>(snapshotBytes);
            if (rows == null)
                return;

            Dictionary<(int, int), BattleTextSnapshotRow> map = rows.ToDictionary(r => (r.EntryIndex, r.Slot));

            state.SuppressRecordTracking = true;
            try
            {
                foreach (BattleTextRow row in state.Records)
                {
                    if (!map.TryGetValue((row.EntryIndex, row.Slot), out BattleTextSnapshotRow? snap))
                        continue;

                    row.Text = snap.Text;
                    row.RefreshPresentation();
                }
            }
            finally
            {
                state.SuppressRecordTracking = false;
            }

            if (ReferenceEquals(currentState, state))
            {
                if (FilterText.Trim().Length > 0)
                {
                    ApplyFilter();
                    if (DisplayedRows.Count == 0 && loadedRows.Count > 0)
                        FilterText = string.Empty;
                }
                else
                {
                    ApplyFilter();
                }

                RefreshDiagnostics();
            }
        }

        void PersistSnapshot(BattleTextSourceState state, byte[] snapshotBytes)
        {
            byte[] fileBytes = BuildFileBytes(state, snapshotBytes);
            File.WriteAllBytes(state.Source.AbsolutePath, fileBytes);
            state.File = BtlTextTable_File.Read(fileBytes, state.Decoder);

            if (ReferenceEquals(currentState, state))
                RefreshDiagnostics();
        }

        static byte[] BuildFileBytes(BattleTextSourceState state, byte[] snapshotBytes)
        {
            BattleTextSnapshotRow[]? rows = JsonSerializer.Deserialize<BattleTextSnapshotRow[]>(snapshotBytes);
            if (rows == null)
                throw new InvalidDataException("Battle-text snapshot could not be read.");

            Dictionary<(int, int), BattleTextSnapshotRow> map = rows.ToDictionary(r => (r.EntryIndex, r.Slot));

            BtlTextTable_File file = BtlTextTable_File.Read(File.ReadAllBytes(state.Source.AbsolutePath), state.Decoder);
            foreach (BtlTextEntry entry in file.Entries)
            {
                foreach (BtlTextRef word in entry.Words)
                {
                    if (map.TryGetValue((entry.Index, word.Slot), out BattleTextSnapshotRow? snap))
                        word.Text = TextBinary_Util.NormalizeText(snap.Text);
                }
            }

            byte[] rebuilt = file.Write(state.Decoder);
            VerifyRoundTrip(map, BtlTextTable_File.Read(rebuilt, state.Decoder));
            return rebuilt;
        }

        static void VerifyRoundTrip(Dictionary<(int, int), BattleTextSnapshotRow> map, BtlTextTable_File rebuilt)
        {
            foreach (BtlTextEntry entry in rebuilt.Entries)
            {
                foreach (BtlTextRef word in entry.Words)
                {
                    if (!map.TryGetValue((entry.Index, word.Slot), out BattleTextSnapshotRow? snap))
                        continue;

                    string expected = TextBinary_Util.NormalizeText(snap.Text);
                    string actual = TextBinary_Util.NormalizeText(word.Text);
                    if (!string.Equals(expected, actual, StringComparison.Ordinal))
                        throw new InvalidDataException($"Battle-text rebuild round-trip mismatch on {entry.IndexLabel} {word.SlotLabel}.");
                }
            }
        }

        bool ValidateCurrentSourceForSave()
        {
            if (currentState == null)
                return false;

            BattleTextSourceState state = currentState;

            if (!ValidateRecords(state, out List<string> issues))
            {
                HasValidationErrors = true;
                ValidationSummary = issues.FirstOrDefault() ?? "Battle-text validation failed.";
                OnPropertyChanged(nameof(CanSaveCurrentSource));
                return false;
            }

            try
            {
                BuildFileBytes(state, CaptureSnapshot(state));
                HasValidationErrors = false;
                ValidationSummary = "Validation clean. Reversible <Cn> control tokens and the FFX charset are writable; unchanged lines keep their original bytes and edits append.";
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

        bool ValidateRecords(BattleTextSourceState state, out List<string> issues)
        {
            issues = [];
            Dictionary<(int, int), byte[]> baseline = BuildBaselineMap(state.File);

            foreach (BattleTextRow row in state.Records)
            {
                baseline.TryGetValue((row.EntryIndex, row.Slot), out byte[]? scriptBytes);
                ValidateField(row.RowLabel, row.Text, scriptBytes, state.Decoder, issues);
            }

            return issues.Count == 0;
        }

        static Dictionary<(int, int), byte[]> BuildBaselineMap(BtlTextTable_File file)
        {
            Dictionary<(int, int), byte[]> map = new();
            foreach (BtlTextEntry entry in file.Entries)
                foreach (BtlTextRef word in entry.Words)
                    map[(entry.Index, word.Slot)] = word.ScriptBytes;
            return map;
        }

        static void ValidateField(string label, string? currentText, byte[]? baselineBytes, Dictionary<byte, char> decoder, List<string> issues)
        {
            string current = TextBinary_Util.NormalizeText(currentText);
            string original = baselineBytes == null || baselineBytes.Length == 0
                ? string.Empty
                : FfxEncoding.DecodeScriptLossless(baselineBytes, decoder);

            // Unchanged text is byte-preserved by the append-only writer, so it never needs re-encode.
            if (string.Equals(current, original, StringComparison.Ordinal))
                return;

            if (!FfxEncoding.TryEncodeScriptLossless(current, decoder, out _, out string? error))
                issues.Add($"{label}: {error}");
        }

        void RefreshDiagnostics()
        {
            if (currentState == null)
            {
                HasValidationErrors = false;
                ValidationSummary = "Battle-text writer validation is idle until a table is loaded.";
            }
            else if (ValidateRecords(currentState, out List<string> issues))
            {
                HasValidationErrors = false;
                ValidationSummary = "Validation clean. Reversible <Cn> control tokens and the FFX charset are writable; unchanged lines keep their original bytes and edits append.";
            }
            else
            {
                HasValidationErrors = true;
                ValidationSummary = issues.FirstOrDefault() ?? "Battle-text validation failed.";
            }

            OnPropertyChanged(nameof(CanSaveCurrentSource));
            OnPropertyChanged(nameof(CanDiscardCurrentSource));
            OnPropertyChanged(nameof(CanUndoCurrentSource));
            OnPropertyChanged(nameof(IsSelectedRowReadOnly));
        }
    }

    internal sealed class BattleTextSourceRow
    {
        public required string DisplayName { get; init; }
        public required string RelativePath { get; init; }
        public required string AbsolutePath { get; init; }
        public required string Summary { get; init; }
        public required Dictionary<byte, char> Decoder { get; init; }
    }

    internal sealed class BattleTextSourceState
    {
        public required BattleTextSourceRow Source { get; init; }
        public required Dictionary<byte, char> Decoder { get; init; }
        public required BtlTextTable_File File { get; set; }
        public required ObservableCollection<BattleTextRow> Records { get; init; }
        public ByteSnapshotEditorSession Session { get; set; } = null!;
        public bool SuppressRecordTracking { get; set; }
    }

    internal sealed class BattleTextSnapshotRow
    {
        public int EntryIndex { get; set; }
        public int Slot { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    internal partial class BattleTextRow : ObservableObject
    {
        public required int EntryIndex { get; init; }
        public required int Slot { get; init; }
        public required string EntryLabel { get; init; }
        public required string SlotLabel { get; init; }
        public required string OffsetHex { get; init; }

        [ObservableProperty] private string text = string.Empty;
        [ObservableProperty] private string summary = string.Empty;
        [ObservableProperty] private string searchBlob = string.Empty;

        public string RowLabel => $"{EntryLabel} · {SlotLabel}";

        public void RefreshPresentation()
        {
            string line = (Text ?? string.Empty).Replace("\n", " ").Trim();
            Summary = line.Length == 0 ? "(Empty)" : (line.Length > 70 ? line[..70] + "…" : line);
            SearchBlob = $"{EntryIndex:X2} {SlotLabel} {OffsetHex} {Text}";
        }
    }
}
