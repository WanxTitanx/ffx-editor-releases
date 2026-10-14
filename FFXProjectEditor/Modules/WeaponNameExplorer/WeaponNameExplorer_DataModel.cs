using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.FfxLib.WeaponNames;
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

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.WeaponNameExplorer
{
    // Editable surface for w_name.bin (weapon/armor name table). Mirrors MacroExplorer's safe-writer
    // pattern: each (entry, character) name pair is a row; the writer rebuilds the string pool + offset
    // table via the proven WeaponNameTable_File.Write while preserving keys, model words and the final
    // word verbatim. Unchanged names reuse their original script bytes, so they never re-encode.
    internal partial class WeaponNameExplorer_DataModel : ObservableObject
    {
        readonly List<WeaponNameRow> loadedRows = new();
        readonly Dictionary<string, WeaponNameSourceState> sourceStates = new(StringComparer.OrdinalIgnoreCase);
        WeaponNameSourceState? currentState;

        public ObservableCollection<WeaponNameSourceRow> LoadedSources { get; } = new();
        public ObservableCollection<WeaponNameRow> DisplayedRows { get; } = new();

        [ObservableProperty] private WeaponNameSourceRow? selectedSource;
        [ObservableProperty] private WeaponNameRow? selectedRow;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = Strings.F2_load_a_project_root_to_inspect_weapon_ar_00fa267e;
        [ObservableProperty] private string selectedSourceSummary = "w_name.bin holds the per-character weapon/armor name strings (regular + simplified) plus the equipment model word for each name id.";
        [ObservableProperty] private string selectedRowSummary = Strings.F2_select_a_name_to_inspect_and_edit_its_re_1aced893;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;
        [ObservableProperty] private bool isSafeWriterEnabled;
        [ObservableProperty] private string validationSummary = "Weapon-name writer validation is idle until a table is loaded.";
        [ObservableProperty] private bool hasValidationErrors;
        [ObservableProperty] private string editorScopeSummary = "Weapon-name authoring is read-only until a table loads.";

        public bool CanSaveCurrentSource => IsSafeWriterEnabled && EditSession?.HasPendingChanges == true && !HasValidationErrors;
        // Discard reverts to the original on disk, so it stays available whenever the file differs from
        // how it was first loaded — including after a Save.
        public bool CanDiscardCurrentSource => IsSafeWriterEnabled && EditSession?.DiffersFromOriginal == true;
        public bool CanUndoCurrentSource => IsSafeWriterEnabled && EditSession?.CanUndo == true;
        public bool IsSelectedRowReadOnly => !IsSafeWriterEnabled;

        public WeaponNameExplorer_DataModel()
        {
            ReloadSources();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        partial void OnSelectedSourceChanged(WeaponNameSourceRow? value)
        {
            if (value == null)
            {
                DisplayedRows.Clear();
                loadedRows.Clear();
                SelectedRow = null;
                currentState = null;
                SetActiveEditSession(null);
                IsSafeWriterEnabled = false;
                EditorScopeSummary = "Weapon-name authoring is read-only until a table loads.";
                RefreshDiagnostics();
                return;
            }

            LoadSelectedSource(value);
        }

        partial void OnSelectedRowChanged(WeaponNameRow? value)
        {
            SelectedRowSummary = value == null
                ? Strings.F2_select_a_name_to_inspect_and_edit_its_re_1aced893
                : $"{value.RowLabel} · {value.Summary}";
        }

        partial void OnIsSafeWriterEnabledChanged(bool value) => OnPropertyChanged(nameof(IsSelectedRowReadOnly));

        public void RefreshFromDisk()
        {
            foreach (WeaponNameSourceState state in sourceStates.Values)
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

            string usPath = Project_Service.Instance.Path_KernelWeaponNamesUs;
            if (File.Exists(usPath))
            {
                LoadedSources.Add(new WeaponNameSourceRow
                {
                    DisplayName = "US Weapon Names",
                    RelativePath = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, usPath),
                    AbsolutePath = usPath,
                    Decoder = FfxEncoding.UsDecoder,
                    Summary = "new_uspc/battle/kernel/w_name.bin"
                });
            }

            LoadSummary = $"Prepared {LoadedSources.Count} weapon-name source(s).";
            SelectedSource = LoadedSources.FirstOrDefault();
        }

        void LoadSelectedSource(WeaponNameSourceRow source)
        {
            try
            {
                WeaponNameSourceState state = GetOrCreateState(source);
                currentState = state;

                loadedRows.Clear();
                foreach (WeaponNameRow row in state.Records)
                    loadedRows.Add(row);

                SetActiveEditSession(state.Session);
                IsSafeWriterEnabled = true;
                EditorScopeSummary = "Weapon-name authoring is live: Regular and Simplified strings with save, undo, discard, validation, and structural round-trip checks. Keys, model words and the final word are preserved verbatim; the string pool + offset table rebuild automatically.";
                SelectedSourceSummary = $"{source.RelativePath} · {state.Records.Count} name cells across {state.File.EntryCount} ids. Safe write rebuilds the string pool and recomputes offsets; unchanged names keep their original bytes.";
                LoadSummary = $"Loaded {state.Records.Count} weapon-name cells from {source.DisplayName}.";

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
            WeaponNameRow? previousSelection = SelectedRow;

            DisplayedRows.Clear();
            foreach (WeaponNameRow row in loadedRows)
            {
                if (normalizedFilter.Length == 0 || row.SearchBlob.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
                    DisplayedRows.Add(row);
            }

            SelectedRow = previousSelection != null && DisplayedRows.Contains(previousSelection)
                ? previousSelection
                : DisplayedRows.FirstOrDefault();
        }

        WeaponNameSourceState GetOrCreateState(WeaponNameSourceRow source)
        {
            if (sourceStates.TryGetValue(source.AbsolutePath, out WeaponNameSourceState? existing))
                return existing;

            byte[] bytes = File.ReadAllBytes(source.AbsolutePath);
            WeaponNameTable_File file = WeaponNameTable_File.Read(bytes, source.Decoder);
            ObservableCollection<WeaponNameRow> records = BuildRecords(file);

            WeaponNameSourceState state = new()
            {
                Source = source,
                Decoder = source.Decoder,
                File = file,
                Records = records
            };

            foreach (WeaponNameRow row in records)
                row.PropertyChanged += (_, e) => RecordChanged(state, row, e);

            state.Session = new ByteSnapshotEditorSession(
                () => CaptureSnapshot(state),
                snapshot => RestoreSnapshot(state, snapshot),
                snapshot => PersistSnapshot(state, snapshot),
                source.DisplayName.ToLowerInvariant(),
                CaptureSnapshot(state))
            {
                // In-place file editor: Undo/Discard also rewrite the file so disk always matches the
                // screen, and Discard restores w_name.bin to how it was when the project loaded.
                RevertWritesToDisk = true
            };

            // The session marks pending/undo state on a debounce timer; mirror those changes onto the
            // DataModel's Can* commands so Save/Undo/Discard re-enable after the user edits again.
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

        static ObservableCollection<WeaponNameRow> BuildRecords(WeaponNameTable_File file)
        {
            ObservableCollection<WeaponNameRow> records = [];

            foreach (WeaponNameEntry entry in file.Entries)
            {
                for (int slot = 0; slot < entry.RegularNames.Count; slot++)
                {
                    WeaponNameTextRef regular = entry.RegularNames[slot];
                    WeaponNameTextRef simplified = entry.SimplifiedNames[slot];
                    WeaponNameModelRef model = entry.Models[slot];

                    WeaponNameRow row = new()
                    {
                        EntryIndex = entry.Index,
                        Slot = slot,
                        EntryLabel = entry.IndexLabel,
                        CharacterCode = regular.CharacterCode,
                        CharacterName = regular.CharacterName,
                        ModelLabel = model.DisplayLabel,
                        ModelRawHex = $"{model.RawValue:X4}h",
                        RegularText = regular.Text,
                        SimplifiedText = simplified.Text
                    };

                    row.RefreshPresentation();
                    records.Add(row);
                }
            }

            return records;
        }

        void RecordChanged(WeaponNameSourceState state, WeaponNameRow row, PropertyChangedEventArgs e)
        {
            if (state.SuppressRecordTracking)
                return;

            if (e.PropertyName is nameof(WeaponNameRow.RegularText) or nameof(WeaponNameRow.SimplifiedText))
            {
                row.RefreshPresentation();
                state.Session?.NotifyPotentialMutation();

                if (ReferenceEquals(currentState, state))
                    RefreshDiagnostics();
            }
        }

        static byte[] CaptureSnapshot(WeaponNameSourceState state)
        {
            WeaponNameSnapshotRow[] rows = state.Records
                .Select(r => new WeaponNameSnapshotRow
                {
                    EntryIndex = r.EntryIndex,
                    Slot = r.Slot,
                    Regular = r.RegularText,
                    Simplified = r.SimplifiedText
                })
                .ToArray();

            return JsonSerializer.SerializeToUtf8Bytes(rows);
        }

        void RestoreSnapshot(WeaponNameSourceState state, byte[] snapshotBytes)
        {
            WeaponNameSnapshotRow[]? rows = JsonSerializer.Deserialize<WeaponNameSnapshotRow[]>(snapshotBytes);
            if (rows == null)
                return;

            Dictionary<(int, int), WeaponNameSnapshotRow> map = rows.ToDictionary(r => (r.EntryIndex, r.Slot));

            state.SuppressRecordTracking = true;
            try
            {
                foreach (WeaponNameRow row in state.Records)
                {
                    if (!map.TryGetValue((row.EntryIndex, row.Slot), out WeaponNameSnapshotRow? snap))
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

            if (ReferenceEquals(currentState, state))
            {
                // A revert can change the text the active filter was matching on; if that empties the
                // list, drop the filter so the (restored) names stay visible instead of a blank list.
                if (FilterText.Trim().Length > 0)
                {
                    ApplyFilter();
                    if (DisplayedRows.Count == 0 && loadedRows.Count > 0)
                        FilterText = string.Empty; // OnFilterTextChanged re-applies and shows all rows
                }
                else
                {
                    ApplyFilter();
                }

                RefreshDiagnostics();
            }
        }

        void PersistSnapshot(WeaponNameSourceState state, byte[] snapshotBytes)
        {
            byte[] fileBytes = BuildFileBytes(state, snapshotBytes);
            File.WriteAllBytes(state.Source.AbsolutePath, fileBytes);
            state.File = WeaponNameTable_File.Read(fileBytes, state.Decoder);

            if (ReferenceEquals(currentState, state))
                RefreshDiagnostics();
        }

        static byte[] BuildFileBytes(WeaponNameSourceState state, byte[] snapshotBytes)
        {
            WeaponNameSnapshotRow[]? rows = JsonSerializer.Deserialize<WeaponNameSnapshotRow[]>(snapshotBytes);
            if (rows == null)
                throw new InvalidDataException("Weapon-name snapshot could not be read.");

            Dictionary<(int, int), WeaponNameSnapshotRow> map = rows.ToDictionary(r => (r.EntryIndex, r.Slot));

            WeaponNameTable_File file = WeaponNameTable_File.Read(File.ReadAllBytes(state.Source.AbsolutePath), state.Decoder);
            foreach (WeaponNameEntry entry in file.Entries)
            {
                for (int slot = 0; slot < entry.RegularNames.Count; slot++)
                {
                    if (!map.TryGetValue((entry.Index, slot), out WeaponNameSnapshotRow? snap))
                        continue;

                    entry.RegularNames[slot].Text = TextBinary_Util.NormalizeText(snap.Regular);
                    entry.SimplifiedNames[slot].Text = TextBinary_Util.NormalizeText(snap.Simplified);
                }
            }

            byte[] rebuilt = file.Write(state.Decoder);
            VerifyRoundTrip(map, WeaponNameTable_File.Read(rebuilt, state.Decoder));
            return rebuilt;
        }

        static void VerifyRoundTrip(Dictionary<(int, int), WeaponNameSnapshotRow> map, WeaponNameTable_File rebuilt)
        {
            foreach (WeaponNameEntry entry in rebuilt.Entries)
            {
                for (int slot = 0; slot < entry.RegularNames.Count; slot++)
                {
                    if (!map.TryGetValue((entry.Index, slot), out WeaponNameSnapshotRow? snap))
                        continue;

                    EnsureRoundTripField($"{entry.IndexLabel} {entry.RegularNames[slot].CharacterName} Regular",
                        TextBinary_Util.NormalizeText(snap.Regular), TextBinary_Util.NormalizeText(entry.RegularNames[slot].Text));
                    EnsureRoundTripField($"{entry.IndexLabel} {entry.SimplifiedNames[slot].CharacterName} Simplified",
                        TextBinary_Util.NormalizeText(snap.Simplified), TextBinary_Util.NormalizeText(entry.SimplifiedNames[slot].Text));
                }
            }
        }

        static void EnsureRoundTripField(string label, string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidDataException($"Weapon-name rebuild round-trip mismatch on {label}.");
        }

        bool ValidateCurrentSourceForSave()
        {
            if (currentState == null)
                return false;

            WeaponNameSourceState state = currentState;

            if (!ValidateRecords(state, out List<string> issues))
            {
                HasValidationErrors = true;
                ValidationSummary = issues.FirstOrDefault() ?? "Weapon-name validation failed.";
                OnPropertyChanged(nameof(CanSaveCurrentSource));
                return false;
            }

            try
            {
                BuildFileBytes(state, CaptureSnapshot(state));
                HasValidationErrors = false;
                ValidationSummary = "Validation clean. Supported FFX charset/tags are writable here; unchanged names keep their original bytes and offsets rebuild automatically.";
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

        bool ValidateRecords(WeaponNameSourceState state, out List<string> issues)
        {
            issues = [];
            Dictionary<(int, int), (byte[] reg, byte[] simp)> baseline = BuildBaselineMap(state.File);

            foreach (WeaponNameRow row in state.Records)
            {
                baseline.TryGetValue((row.EntryIndex, row.Slot), out (byte[] reg, byte[] simp) b);
                ValidateField(row.RowLabel, "Regular", row.RegularText, b.reg, state.Decoder, issues);
                ValidateField(row.RowLabel, "Simplified", row.SimplifiedText, b.simp, state.Decoder, issues);
            }

            return issues.Count == 0;
        }

        static Dictionary<(int, int), (byte[] reg, byte[] simp)> BuildBaselineMap(WeaponNameTable_File file)
        {
            Dictionary<(int, int), (byte[] reg, byte[] simp)> map = new();
            foreach (WeaponNameEntry entry in file.Entries)
                for (int slot = 0; slot < entry.RegularNames.Count; slot++)
                    map[(entry.Index, slot)] = (entry.RegularNames[slot].ScriptBytes, entry.SimplifiedNames[slot].ScriptBytes);
            return map;
        }

        static void ValidateField(string label, string field, string? currentText, byte[]? baselineBytes, Dictionary<byte, char> decoder, List<string> issues)
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
            if (currentState == null)
            {
                HasValidationErrors = false;
                ValidationSummary = "Weapon-name writer validation is idle until a table is loaded.";
            }
            else if (ValidateRecords(currentState, out List<string> issues))
            {
                HasValidationErrors = false;
                ValidationSummary = "Validation clean. Supported FFX charset/tags are writable here; unchanged names keep their original bytes and offsets rebuild automatically.";
            }
            else
            {
                HasValidationErrors = true;
                ValidationSummary = issues.FirstOrDefault() ?? "Weapon-name validation failed.";
            }

            OnPropertyChanged(nameof(CanSaveCurrentSource));
            OnPropertyChanged(nameof(CanDiscardCurrentSource));
            OnPropertyChanged(nameof(CanUndoCurrentSource));
            OnPropertyChanged(nameof(IsSelectedRowReadOnly));
        }
    }

    internal sealed class WeaponNameSourceRow
    {
        public required string DisplayName { get; init; }
        public required string RelativePath { get; init; }
        public required string AbsolutePath { get; init; }
        public required string Summary { get; init; }
        public required Dictionary<byte, char> Decoder { get; init; }
    }

    internal sealed class WeaponNameSourceState
    {
        public required WeaponNameSourceRow Source { get; init; }
        public required Dictionary<byte, char> Decoder { get; init; }
        public required WeaponNameTable_File File { get; set; }
        public required ObservableCollection<WeaponNameRow> Records { get; init; }
        public ByteSnapshotEditorSession Session { get; set; } = null!;
        public bool SuppressRecordTracking { get; set; }
    }

    internal sealed class WeaponNameSnapshotRow
    {
        public int EntryIndex { get; set; }
        public int Slot { get; set; }
        public string Regular { get; set; } = string.Empty;
        public string Simplified { get; set; } = string.Empty;
    }

    internal partial class WeaponNameRow : ObservableObject
    {
        public required int EntryIndex { get; init; }
        public required int Slot { get; init; }
        public required string EntryLabel { get; init; }
        public required string CharacterCode { get; init; }
        public required string CharacterName { get; init; }
        public required string ModelLabel { get; init; }
        public required string ModelRawHex { get; init; }

        [ObservableProperty] private string regularText = string.Empty;
        [ObservableProperty] private string simplifiedText = string.Empty;
        [ObservableProperty] private string summary = string.Empty;
        [ObservableProperty] private string searchBlob = string.Empty;

        public string RowLabel => $"{EntryLabel} · {CharacterName}";
        public bool HasDistinctSimplified => !string.Equals(RegularText, SimplifiedText, StringComparison.Ordinal);

        public void RefreshPresentation()
        {
            string regular = (RegularText ?? string.Empty).Replace("\n", " ").Trim();
            string preview = regular.Length == 0 ? "(Empty)" : (regular.Length > 60 ? regular[..60] + "…" : regular);
            Summary = HasDistinctSimplified ? $"{preview} · split simplified" : preview;
            SearchBlob = $"{EntryIndex:X2} {CharacterName} {CharacterCode} {ModelRawHex} {RegularText} {SimplifiedText}";
        }
    }
}
