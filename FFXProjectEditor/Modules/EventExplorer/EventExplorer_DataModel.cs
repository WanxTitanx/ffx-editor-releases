using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Event;
using FFXProjectEditor.Resources;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.Modules.EventExplorer
{
    internal partial class EventExplorer_DataModel : ObservableObject
    {
        public ObservableCollection<EventRow> LoadedEvents { get; } = new();
        public ObservableCollection<EventRow> DisplayedEvents { get; } = new();
        public ObservableCollection<EventChunkRow> SelectedChunks { get; } = new();
        public ObservableCollection<EventTextRow> SelectedTextRows { get; } = new();

        [ObservableProperty] private EventRow? selectedEvent;
        [ObservableProperty] private string filterText = string.Empty;

        public ObservableCollection<DisassemblyInstruction> DisassemblyLines { get; } = new();
        [ObservableProperty] private DisassemblyInstruction? selectedInstruction;
        [ObservableProperty] private string selectedInstructionDetail = string.Empty;
        [ObservableProperty] private bool canPatchOperand;
        [ObservableProperty] private string patchOperandHex = string.Empty;
        [ObservableProperty] private string patchStatus = string.Empty;
        [ObservableProperty] private string growChunkHex = string.Empty;
        [ObservableProperty] private string growStatus = string.Empty;
        public ObservableCollection<object> GlossaryDisplayItems { get; } = new();
        [ObservableProperty] private string glossaryFilter = string.Empty;
        [ObservableProperty] private object? selectedGlossaryItem;
        [ObservableProperty] private string selectedGlossaryDetail = string.Empty;

        [ObservableProperty] private string loadSummary = "Load a project root to browse event files.";
        [ObservableProperty] private string selectedEventSummary = "Select an event to inspect its chunk layout, decoded text tables, and script preview.";
        [ObservableProperty] private string selectedEventScope = "Edit the JP/EN dialogue strings and Save: the EV01 container is re-packed byte-faithfully (--event-rt0 proven), preserving the ATEL script / Unknown 2 / FTCX chunks verbatim.";
        [ObservableProperty] private string scriptPreview = "No event selected.";
        [ObservableProperty] private string scriptStatus = EventScriptCorpus_Service.StatusSummary;
        [ObservableProperty] private string saveStatus = string.Empty;
        [ObservableProperty] private bool canSave;

        // Dialogue-row filters: word search (text content) + per-language toggles (default English-only to avoid the
        // wall of Japanese). The full set lives in allTextRows; SelectedTextRows is the filtered view.
        [ObservableProperty] private string textFilter = string.Empty;
        [ObservableProperty] private bool showEnglish = true;
        [ObservableProperty] private bool showJapanese;

        readonly List<EventTextRow> allTextRows = new();

        // The currently loaded event (its text-table entries are what the editable rows mutate) + its file row.
        Event_File? loadedEvent;
        EventRow? loadedRow;

        partial void OnTextFilterChanged(string value) => ApplyTextFilter();
        partial void OnShowEnglishChanged(bool value) => ApplyTextFilter();
        partial void OnShowJapaneseChanged(bool value) => ApplyTextFilter();

        public EventExplorer_DataModel()
        {
            ReloadEvents();
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedEventChanged(EventRow? value)
        {
            if (value == null)
            {
                SelectedChunks.Clear();
                SelectedTextRows.Clear();
                allTextRows.Clear();
                loadedEvent = null;
                loadedRow = null;
                CanSave = false;
                SaveStatus = string.Empty;
                ScriptPreview = "No event selected.";
                SelectedEventSummary = "Select an event to inspect its chunk layout, decoded text tables, and script preview.";
                return;
            }

            LoadSelectedEvent(value);
        }

        public void RefreshFromDisk()
        {
            ReloadEvents();
        }

        void ReloadEvents()
        {
            LoadedEvents.Clear();
            DisplayedEvents.Clear();
            SelectedChunks.Clear();
            SelectedTextRows.Clear();
            allTextRows.Clear();
            SelectedEvent = null;
            loadedEvent = null;
            loadedRow = null;
            CanSave = false;
            SaveStatus = string.Empty;
            ScriptPreview = "No event selected.";
            ScriptStatus = EventScriptCorpus_Service.StatusSummary;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                return;
            }

            if (!Directory.Exists(Project_Service.Instance.Path_Event))
            {
                LoadSummary = "jppc/event/obj not found in the loaded workspace.";
                return;
            }

            foreach (string path in Directory.GetFiles(Project_Service.Instance.Path_Event, "*.ebp", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string eventId = Path.GetFileNameWithoutExtension(path);
                string relativePath = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, path);
                FileInfo fileInfo = new(path);

                LoadedEvents.Add(new EventRow
                {
                    EventId = eventId,
                    RelativePath = relativePath,
                    AbsolutePath = path,
                    FileSize = fileInfo.Length
                });
            }

            ApplyFilter();
            SelectedEvent = DisplayedEvents.FirstOrDefault();
            LoadSummary = $"Indexed {LoadedEvents.Count} event files from jppc/event/obj.";
        }

        void ApplyFilter()
        {
            string normalizedFilter = FilterText.Trim();
            EventRow? previousSelection = SelectedEvent;

            DisplayedEvents.Clear();
            foreach (EventRow row in LoadedEvents)
            {
                if (normalizedFilter.Length == 0 || row.SearchBlob.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
                {
                    DisplayedEvents.Add(row);
                }
            }

            if (previousSelection != null && DisplayedEvents.Contains(previousSelection))
            {
                SelectedEvent = previousSelection;
            }
            else if (!DisplayedEvents.Contains(SelectedEvent))
            {
                SelectedEvent = DisplayedEvents.FirstOrDefault();
            }
        }

        void LoadSelectedEvent(EventRow row)
        {
            SaveStatus = string.Empty;
            try
            {
                byte[] bytes = File.ReadAllBytes(row.AbsolutePath);
                Event_File eventFile = Event_File.Read(row.EventId, bytes);
                loadedEvent = eventFile;
                loadedRow = row;

                SelectedChunks.Clear();
                foreach (BinaryChunk chunk in eventFile.Chunks)
                {
                    SelectedChunks.Add(new EventChunkRow
                    {
                        IndexLabel = $"#{chunk.Index}",
                        Label = chunk.Label,
                        OffsetLabel = chunk.Offset > 0 ? $"{chunk.Offset:X8}h" : "-",
                        LengthLabel = chunk.Length.ToString("N0") + " bytes",
                        RangeLabel = chunk.RangeLabel
                    });
                }

                // Editable dialogue rows from BOTH text tables (whichever are present + parseable). Each row writes
                // back to the live TextTable_Entry, so Save -> Event_File.Write picks the edits up. English first so
                // the default English-only view isn't buried under the Japanese strings; ApplyTextFilter renders.
                allTextRows.Clear();
                AddTextRows("US", eventFile.EnglishTable);
                AddTextRows("JP", eventFile.JapaneseTable);
                ApplyTextFilter();

                int editable = allTextRows.Count;
                CanSave = editable > 0;

                int presentTextChunks = (eventFile.Chunks.Count > Event_File.ChunkJapaneseText && eventFile.Chunks[Event_File.ChunkJapaneseText].IsPresent ? 1 : 0)
                                      + (eventFile.Chunks.Count > Event_File.ChunkEnglishText && eventFile.Chunks[Event_File.ChunkEnglishText].IsPresent ? 1 : 0);
                string locked = presentTextChunks > 0 && editable == 0
                    ? " (text chunk present but uses null-pointer entries — preserved verbatim, not yet editable)"
                    : string.Empty;

                string basePreview = EventScriptCorpus_Service.GetScriptPreview(row.EventId)
                    ?? $"No decompiled parser corpus block found for {row.EventId}.{Environment.NewLine}{Environment.NewLine}Fallback structural summary:{Environment.NewLine}{eventFile.BuildChunkSummary()}";

                // Append ATEL disassembly when chunk0 (script) is present
                DisassemblyLines.Clear();
                var atelChunk = eventFile.Chunks.Count > 0 && eventFile.Chunks[0].IsPresent
                    ? eventFile.Chunks[0]
                    : null;
                if (atelChunk != null && atelChunk.Bytes != null && atelChunk.Bytes.Length > 0)
                {
                    try
                    {
                        var structured = EventAtelDisassembler.DisassembleAtelChunk(atelChunk.Bytes);
                        var codeRange = EventAtelDisassembler.CodeRange(atelChunk.Bytes);
                        foreach (var d in structured)
                            DisassemblyLines.Add(d);
                        string disasm = string.Join(Environment.NewLine, structured.Select(d => d.FormattedLine));
                        basePreview += Environment.NewLine + Environment.NewLine +
                            $"=== ATEL Disassembly (0x{codeRange.Length:X} bytes, {structured.Count} instr) ===" +
                            Environment.NewLine + disasm;
                    }
                    catch (Exception ex)
                    {
                        basePreview += Environment.NewLine + Environment.NewLine +
                            string.Format(Strings.U_EventAtelDisassemblyUnavailable, ex.Message);
                    }
                }
                ScriptPreview = basePreview;
                SelectedInstruction = null;
                PatchStatus = string.Empty;
                if (GlossaryDisplayItems.Count == 0) LoadGlossary();
                SelectedEventSummary = $"{row.EventId} · {row.RelativePath} · {eventFile.BuildChunkSummary()} · {editable} editable strings{locked}";
            }
            catch (Exception ex)
            {
                SelectedChunks.Clear();
                SelectedTextRows.Clear();
                allTextRows.Clear();
                loadedEvent = null;
                loadedRow = null;
                CanSave = false;
                ScriptPreview = $"Failed to read {row.EventId}:{Environment.NewLine}{ex.Message}";
                SelectedEventSummary = $"Failed to inspect {row.EventId}.";
            }
        }

        void AddTextRows(string locale, TextTable_File? table)
        {
            if (table == null)
                return;

            foreach (TextTable_Entry entry in table.Entries)
                allTextRows.Add(new EventTextRow(locale, entry));
        }

        void LoadGlossary()
        {
            GlossaryDisplayItems.Clear();
            string needle = GlossaryFilter.Trim();
            foreach (var kv in EventCallGlossary.Entries)
            {
                int fs = kv.Key >> 12;
                if (needle.Length > 0 &&
                    !kv.Value.Contains(needle, StringComparison.OrdinalIgnoreCase) &&
                    !$"0x{kv.Key:X4}".Contains(needle, StringComparison.OrdinalIgnoreCase))
                    continue;
                GlossaryDisplayItems.Add(new GlossaryEntry { CallId = kv.Key, Name = kv.Value });
            }
        }

        partial void OnGlossaryFilterChanged(string value) => LoadGlossary();

        partial void OnSelectedGlossaryItemChanged(object? value)
        {
            if (value is GlossaryEntry entry)
                SelectedGlossaryDetail = $"Call ID: 0x{entry.CallId:X4}\nFuncspace: {entry.FuncspaceLabel}\nName: {entry.Name}";
            else
                SelectedGlossaryDetail = string.Empty;
        }

        partial void OnSelectedInstructionChanged(DisassemblyInstruction? instr)
        {
            if (instr == null)
            {
                SelectedInstructionDetail = string.Empty;
                CanPatchOperand = false;
                PatchOperandHex = string.Empty;
                PatchStatus = string.Empty;
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"Offset: 0x{instr.Offset:X4}");
            sb.AppendLine($"Raw: 0x{instr.Raw:X2}");
            sb.AppendLine($"Mnemonic: {instr.Mnemonic}");
            sb.AppendLine($"Length: {instr.Length} bytes");
            if (instr.HasOperand)
                sb.AppendLine($"Operand: 0x{instr.Operand:X4}");
            if (instr.IsNativeCall)
            {
                sb.AppendLine($"Glossary: {instr.GlossaryName}");
                sb.AppendLine($"Funcspace: {instr.FuncspaceName} ({instr.FuncspaceId})");
                sb.AppendLine($"Call Index: 0x{instr.CallIndex:X3}");
                CanPatchOperand = true;
                PatchOperandHex = $"0x{instr.Operand:X4}";
            }
            else
            {
                CanPatchOperand = false;
                PatchOperandHex = string.Empty;
                PatchStatus = string.Empty;
            }
            SelectedInstructionDetail = sb.ToString();
        }

        public void ApplyPatchedOperand()
        {
            if (loadedEvent == null || SelectedInstruction == null || !SelectedInstruction.IsNativeCall)
            {
                PatchStatus = "No instruction selected for patch.";
                return;
            }
            if (!int.TryParse(PatchOperandHex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out int newOperand))
            {
                PatchStatus = "Invalid hex value.";
                return;
            }
            if (newOperand < 0 || newOperand > 0xFFFF)
            {
                PatchStatus = "Operand must be 0x0000-0xFFFF.";
                return;
            }

            try
            {
                int off = SelectedInstruction.Offset;
                loadedEvent.PatchNativeCallOperand(off, (ushort)newOperand);
                var freshDisasm = EventAtelDisassembler.DisassembleAtelChunk(loadedEvent.ScriptChunkOverride!);
                DisassemblyLines.Clear();
                foreach (var d in freshDisasm)
                    DisassemblyLines.Add(d);
                SelectedInstruction = freshDisasm.FirstOrDefault(d => d.Offset == off);

                PatchStatus = $"Patched operand → 0x{newOperand:X4}. Save to persist.";
            }
            catch (Exception ex)
            {
                PatchStatus = $"Patch failed: {ex.Message}";
            }
        }

        // Render SelectedTextRows from allTextRows applying the language toggles + the word/content search.
        void ApplyTextFilter()
        {
            SelectedTextRows.Clear();
            string needle = TextFilter.Trim();
            foreach (EventTextRow row in allTextRows)
            {
                bool localeOk = (row.Locale == "US" && ShowEnglish) || (row.Locale == "JP" && ShowJapanese);
                if (!localeOk)
                    continue;
                if (needle.Length > 0
                    && !(row.RegularText.Contains(needle, StringComparison.OrdinalIgnoreCase)
                         || row.SimplifiedText.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                    continue;
                SelectedTextRows.Add(row);
            }
        }

        public void SaveSelectedEvent()
        {
            if (loadedEvent == null || loadedRow == null)
            {
                SaveStatus = "Nothing loaded to save.";
                return;
            }

            try
            {
                byte[] bytes = loadedEvent.Write();
                File.WriteAllBytes(loadedRow.AbsolutePath, bytes);

                // Re-read so the in-memory model + rows reflect the saved bytes (offsets re-resolved, edits canonical).
                EventRow row = loadedRow;
                LoadSelectedEvent(row);
                SaveStatus = $"Saved {row.EventId} · {bytes.Length:N0} bytes ({row.RelativePath}).";
            }
            catch (Exception ex)
            {
                SaveStatus = $"Save failed: {ex.Message}";
            }
        }

        internal sealed class EventRow
        {
            public required string EventId { get; init; }
            public required string RelativePath { get; init; }
            public required string AbsolutePath { get; init; }
            public required long FileSize { get; init; }

            public string Summary => $"{RelativePath} · {FileSize:N0} bytes";
            public string SearchBlob => $"{EventId} {RelativePath}";
        }

        internal sealed class EventChunkRow
        {
            public required string IndexLabel { get; init; }
            public required string Label { get; init; }
            public required string OffsetLabel { get; init; }
            public required string LengthLabel { get; init; }
            public required string RangeLabel { get; init; }
        }

        // Editable dialogue row: two-way bound to a live TextTable_Entry (regular + simplified text). Mutations
        // flow straight into the entry, so Event_File.Write re-emits the edited text chunk on Save.
        internal sealed partial class EventTextRow : ObservableObject
        {
            readonly TextTable_Entry entry;

            public EventTextRow(string locale, TextTable_Entry entry)
            {
                Locale = locale;
                this.entry = entry;
            }

            public string Locale { get; }
            public string IndexLabel => entry.IndexLabel;
            public string Summary => entry.FlagsSummary;
            public bool HasDistinctSimplified => entry.HasDistinctSimplified;

            public string RegularText
            {
                get => entry.RegularText;
                set { if (!string.Equals(entry.RegularText, value, StringComparison.Ordinal)) { entry.RegularText = value; OnPropertyChanged(); } }
            }

            public string SimplifiedText
            {
                get => entry.SimplifiedText;
                set { if (!string.Equals(entry.SimplifiedText, value, StringComparison.Ordinal)) { entry.SimplifiedText = value; OnPropertyChanged(); } }
            }
        }

        internal sealed class GlossaryEntry
        {
            public required int CallId { get; init; }
            public required string Name { get; init; }
            public int FuncspaceId => CallId >> 12;
            public string FuncspaceLabel => FuncspaceId switch
            {
                0 => "Common", 1 => "Math", 4 => "SgEvent", 5 => "ChEvent",
                6 => "Camera", 7 => "Battle", 8 => "Map", 9 => "Mount",
                0xB => "Movie", 0xC => "Debug", 0xD => "AbiMap",
                _ => $"?FS{FuncspaceId}"
            };
            public string CallIdLabel => $"0x{CallId:X4}";
            public string ShortLabel => $"{FuncspaceLabel}@{CallIdLabel}";
        }
    }
}
