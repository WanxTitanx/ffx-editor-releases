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

namespace FFXProjectEditor.Modules.AlBhedDictionaryEditor
{
    public enum AlBhedDictionaryLocale
    {
        UsLatin,
        JpKana,
    }

    internal partial class AlBhedDictionaryEditor_DataModel : ObservableObject
    {
        AlBhedDictionary_File? loadedFile;
        Dictionary<char, byte> charEncoder = new();
        Dictionary<byte, char> activeDecoder = FfxEncoding.UsDecoder;
        string activePath = string.Empty;

        public ObservableCollection<AlBhedRow> LoadedRows { get; } = new();
        public ObservableCollection<AlBhedRow> DisplayedRows { get; } = new();
        public AlBhedDictionaryLocale[] LocaleOptions { get; } = Enum.GetValues<AlBhedDictionaryLocale>();

        [ObservableProperty] private AlBhedDictionaryLocale selectedLocale = AlBhedDictionaryLocale.UsLatin;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Loading albheddic.bin...";
        [ObservableProperty] private string scopeSummary =
            "Safe scope: edit mapped glyph and group bucket on existing rows. Source bytes and table shape stay fixed.";
        [ObservableProperty] private string selectedSummary = "Select a mapping row to edit the Al Bhed substitution.";
        [ObservableProperty] private string sourcePathLabel = string.Empty;
        [ObservableProperty] private string validationSummary = string.Empty;
        [ObservableProperty] private bool hasValidationErrors;
        [ObservableProperty] private AlBhedRow? selectedRow;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;

        public bool CanSave => EditSession?.HasPendingChanges == true && !HasValidationErrors;

        public AlBhedDictionaryEditor_DataModel()
        {
            LoadFromDisk();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        partial void OnSelectedLocaleChanged(AlBhedDictionaryLocale value) => LoadFromDisk();

        partial void OnSelectedRowChanged(AlBhedRow? value)
        {
            SelectedSummary = value == null
                ? "Select a mapping row to edit the Al Bhed substitution."
                : value.IsPadding
                    ? "Padding row — preserved verbatim; not editable."
                    : $"{value.Title} · group {value.GroupIndex} · {value.SourceCode:X2}h -> {value.MappedCode:X2}h";
        }

        partial void OnEditSessionChanged(ByteSnapshotEditorSession? value)
        {
            if (value != null)
                value.PropertyChanged += EditSessionPropertyChanged;
            NotifySaveState();
        }

        void EditSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ByteSnapshotEditorSession.HasPendingChanges)
                or nameof(ByteSnapshotEditorSession.CanUndo))
            {
                NotifySaveState();
            }
        }

        public void RefreshFromDisk() => LoadFromDisk();

        public void Save()
        {
            if (!ValidateCurrentFile(out string? error))
            {
                HasValidationErrors = true;
                ValidationSummary = error ?? "Validation failed.";
                NotifySaveState();
                return;
            }

            EditSession?.Save();
            HasValidationErrors = false;
            ValidationSummary = "Saved. Round-trip Read->Write validated against the active locale decoder.";
            NotifySaveState();
        }

        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();

        void NotifySaveState()
        {
            OnPropertyChanged(nameof(CanSave));
        }

        void LoadFromDisk()
        {
            EditSession?.Dispose();
            EditSession = null;
            loadedFile = null;
            HasValidationErrors = false;
            ValidationSummary = string.Empty;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                ClearRows("Project root not loaded.");
                SourcePathLabel = string.Empty;
                return;
            }

            activePath = ResolvePath(SelectedLocale);
            activeDecoder = SelectedLocale == AlBhedDictionaryLocale.JpKana
                ? FfxEncoding.JpDecoder
                : FfxEncoding.UsDecoder;
            charEncoder = BuildCharEncoder(activeDecoder);
            SourcePathLabel = activePath;

            if (!File.Exists(activePath))
            {
                ClearRows($"albheddic.bin not found: {activePath}");
                return;
            }

            byte[] bytes = File.ReadAllBytes(activePath);
            loadedFile = AlBhedDictionary_File.Read(bytes, activeDecoder);
            LoadRows(loadedFile, SelectedRow?.Index);

            EditSession = new ByteSnapshotEditorSession(
                BuildFile,
                RestoreFromBytes,
                PersistBytes,
                "Al Bhed dictionary",
                BuildFile());

            HasValidationErrors = false;
            ValidationSummary = string.Empty;
            NotifySaveState();
        }

        static string ResolvePath(AlBhedDictionaryLocale locale)
        {
            return locale switch
            {
                AlBhedDictionaryLocale.JpKana => Path.Combine(Project_Service.Instance.Path_Menu, "albheddic.bin"),
                _ => Path.Combine(Project_Service.Instance.Path_MenuUs, "albheddic.bin"),
            };
        }

        static Dictionary<char, byte> BuildCharEncoder(Dictionary<byte, char> decoder)
        {
            Dictionary<char, byte> encoder = new();
            foreach (KeyValuePair<byte, char> pair in decoder)
                encoder.TryAdd(pair.Value, pair.Key);

            return encoder;
        }

        void ClearRows(string message)
        {
            foreach (AlBhedRow row in LoadedRows)
                row.PropertyChanged -= RowChanged;

            LoadedRows.Clear();
            DisplayedRows.Clear();
            SelectedRow = null;
            LoadSummary = message;
        }

        void LoadRows(AlBhedDictionary_File file, int? preserveSelectionIndex)
        {
            foreach (AlBhedRow row in LoadedRows)
                row.PropertyChanged -= RowChanged;

            LoadedRows.Clear();
            DisplayedRows.Clear();

            foreach (AlBhedDictionary_Entry entry in file.Entries)
            {
                AlBhedRow row = AlBhedRow.Wrap(entry, charEncoder);
                row.PropertyChanged += RowChanged;
                LoadedRows.Add(row);
            }

            ApplyFilter();
            SelectedRow = preserveSelectionIndex.HasValue
                ? LoadedRows.FirstOrDefault(row => row.Index == preserveSelectionIndex.Value)
                : LoadedRows.FirstOrDefault(row => !row.IsPadding) ?? LoadedRows.FirstOrDefault();

            LoadSummary =
                $"{file.VariantLabel} · {file.ActiveEntryCount} active / {file.PaddingEntryCount} padding · {activePath}";
        }

        void ApplyFilter()
        {
            DisplayedRows.Clear();
            string normalized = FilterText.Trim();

            foreach (AlBhedRow row in LoadedRows)
            {
                if (normalized.Length == 0 || row.SearchBlob.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    DisplayedRows.Add(row);
            }

            if (SelectedRow != null && !DisplayedRows.Contains(SelectedRow))
                SelectedRow = DisplayedRows.FirstOrDefault();
        }

        byte[] BuildFile()
        {
            if (loadedFile == null)
                return Array.Empty<byte>();

            AlBhedDictionary_File rebuilt = new()
            {
                OriginalBytes = loadedFile.OriginalBytes,
                FileSize = loadedFile.FileSize,
                EntryLength = loadedFile.EntryLength,
                VariantLabel = loadedFile.VariantLabel,
                Entries = LoadedRows.Select(row => row.ToEntry(activeDecoder)).ToList()
            };

            byte[] bytes = AlBhedDictionary_File.Write(rebuilt);
            AlBhedDictionary_File.Read(bytes, activeDecoder);
            return bytes;
        }

        bool ValidateCurrentFile(out string? error)
        {
            try
            {
                BuildFile();
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        void RestoreFromBytes(byte[] bytes)
        {
            loadedFile = AlBhedDictionary_File.Read(bytes, activeDecoder);
            LoadRows(loadedFile, SelectedRow?.Index);
            HasValidationErrors = false;
            ValidationSummary = string.Empty;
            NotifySaveState();
        }

        void PersistBytes(byte[] bytes)
        {
            File.WriteAllBytes(activePath, bytes);
            loadedFile = AlBhedDictionary_File.Read(bytes, activeDecoder);
            LoadRows(loadedFile, SelectedRow?.Index);
        }

        void RowChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is AlBhedRow row)
            {
                if (!row.IsPadding && e.PropertyName is nameof(AlBhedRow.MappedCharText) or nameof(AlBhedRow.GroupIndex))
                {
                    if (!row.TryApplyMappedChar(charEncoder, out string? mappedError))
                    {
                        HasValidationErrors = true;
                        ValidationSummary = mappedError ?? "Mapped character is not valid for this locale decoder.";
                    }
                    else if (row.GroupIndex is < 0 or > 25)
                    {
                        HasValidationErrors = true;
                        ValidationSummary = $"Group index must stay within 0..25 (row {row.IndexLabel}).";
                    }
                    else
                    {
                        HasValidationErrors = false;
                        ValidationSummary = string.Empty;
                    }
                }
            }

            EditSession?.NotifyPotentialMutation();
            NotifySaveState();

            if (sender is AlBhedRow changed && ReferenceEquals(changed, SelectedRow))
            {
                SelectedSummary = changed.IsPadding
                    ? "Padding row — preserved verbatim; not editable."
                    : $"{changed.Title} · group {changed.GroupIndex} · {changed.SourceCode:X2}h -> {changed.MappedCode:X2}h";
            }
        }

        internal partial class AlBhedRow : ObservableObject
        {
            [ObservableProperty] private string mappedCharText = string.Empty;
            [ObservableProperty] private int groupIndex;

            public required int Index { get; init; }
            public required byte[] RawBytes { get; init; }
            public required byte SourceCode { get; init; }
            public required char SourceGlyph { get; init; }
            public required bool IsPadding { get; init; }
            public byte MappedCode { get; private set; }

            public char MappedGlyph { get; private set; }
            public string IndexLabel => $"Map {Index:X2}h";
            public string Title => IsPadding ? "(Padding)" : $"{SourceGlyph} -> {MappedGlyph}";
            public string Summary =>
                IsPadding
                    ? "Trailing zero padding row."
                    : $"Source {SourceCode:X2}h ('{SourceGlyph}') -> mapped {MappedCode:X2}h ('{MappedGlyph}') · group {GroupIndex}.";
            public string SearchBlob =>
                IsPadding
                    ? $"padding {Index:X2}"
                    : $"{Index:X2} {SourceCode:X2} {MappedCode:X2} {GroupIndex} {SourceGlyph} {MappedGlyph} {Summary}";
            public bool IsEditable => !IsPadding;

            public static AlBhedRow Wrap(AlBhedDictionary_Entry entry, Dictionary<char, byte> encoder)
            {
                AlBhedRow row = new()
                {
                    Index = entry.Index,
                    RawBytes = entry.RawBytes.ToArray(),
                    SourceCode = entry.SourceCode,
                    SourceGlyph = entry.SourceGlyph,
                    IsPadding = entry.IsPadding,
                    GroupIndex = entry.GroupIndex,
                    MappedCharText = entry.IsPadding ? string.Empty : entry.MappedGlyph.ToString()
                };
                row.SetMapped(entry.MappedCode, entry.MappedGlyph);
                return row;
            }

            public AlBhedDictionary_Entry ToEntry(Dictionary<byte, char> decoder)
            {
                if (!TryApplyMappedChar(BuildCharEncoder(decoder), out string? error))
                    throw new InvalidOperationException(error ?? "Mapped character is invalid.");

                return new AlBhedDictionary_Entry
                {
                    Index = Index,
                    RawBytes = RawBytes.ToArray(),
                    SourceCode = SourceCode,
                    SourceGlyph = SourceGlyph,
                    MappedCode = MappedCode,
                    MappedGlyph = MappedGlyph,
                    GroupIndex = GroupIndex,
                    IsPadding = IsPadding
                };
            }

            public bool TryApplyMappedChar(Dictionary<char, byte> encoder, out string? error)
            {
                if (IsPadding)
                {
                    error = null;
                    return true;
                }

                string trimmed = MappedCharText.Trim();
                if (trimmed.Length == 0)
                {
                    error = $"{IndexLabel}: mapped character cannot be empty.";
                    return false;
                }

                char mappedChar = trimmed[0];
                if (!encoder.TryGetValue(mappedChar, out byte mappedCode))
                {
                    error = $"{IndexLabel}: '{mappedChar}' is not encodable for the active locale decoder.";
                    return false;
                }

                SetMapped(mappedCode, mappedChar);
                error = null;
                return true;
            }

            void SetMapped(byte mappedCode, char mappedGlyph)
            {
                MappedCode = mappedCode;
                MappedGlyph = mappedGlyph;
                OnPropertyChanged(nameof(MappedCode));
                OnPropertyChanged(nameof(MappedGlyph));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(SearchBlob));
            }

            partial void OnGroupIndexChanged(int value)
            {
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(SearchBlob));
            }

            partial void OnMappedCharTextChanged(string value)
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(SearchBlob));
            }

            static Dictionary<char, byte> BuildCharEncoder(Dictionary<byte, char> decoder)
            {
                Dictionary<char, byte> encoder = new();
                foreach (KeyValuePair<byte, char> pair in decoder)
                    encoder.TryAdd(pair.Value, pair.Key);

                return encoder;
            }
        }
    }
}
