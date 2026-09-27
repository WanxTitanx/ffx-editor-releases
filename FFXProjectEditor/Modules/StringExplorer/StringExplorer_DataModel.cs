using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using FFXProjectEditor.Files;
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

namespace FFXProjectEditor.Modules.StringExplorer
{
    internal partial class StringExplorer_DataModel : ObservableObject
    {
        static readonly IBrush ProvenBadgeBrush = new SolidColorBrush(Color.FromRgb(28, 79, 58));
        static readonly IBrush SafeWriterBadgeBrush = new SolidColorBrush(Color.FromRgb(34, 106, 74));
        static readonly IBrush ReadOnlyBadgeBrush = new SolidColorBrush(Color.FromRgb(125, 80, 30));
        static readonly IBrush HeuristicBadgeBrush = new SolidColorBrush(Color.FromRgb(117, 48, 44));
        static readonly IBrush NeutralBadgeBrush = new SolidColorBrush(Color.FromRgb(35, 56, 72));

        readonly List<StringExplorerRecord> loadedRecords = [];
        readonly Dictionary<string, MonsterLocalizationSourceState> monsterLocalizationStates = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, NameDescriptionSourceState> nameDescriptionStates = new(StringComparer.OrdinalIgnoreCase);

        ByteSnapshotEditorSession? subscribedEditSession;
        MonsterLocalizationSourceState? currentMonsterLocalizationState;
        NameDescriptionSourceState? currentNameDescriptionState;

        internal enum StringSourceKind
        {
            NameDescriptionTextFile,
            BattleTextFile,
            AlBhedDictionaryFile,
            PointerScriptTableFile,
            LegacyMenuMainResourceFile,
            FieldStringFile,
            MonsterLocalizationFile,
            UnsupportedTextFile
        }

        public ObservableCollection<StringSourceRow> LoadedSources { get; } = [];
        public ObservableCollection<StringExplorerRecord> DisplayedRecords { get; } = [];
        public ObservableCollection<StringExplorerDiffRow> PendingDiffRows { get; } = [];

        [ObservableProperty] private StringSourceRow? selectedSource;
        [ObservableProperty] private StringExplorerRecord? selectedRecord;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Load a project root to explore proven text tables, read-only text families, and safe text writers.";
        [ObservableProperty] private string selectedSourceSummary = "READ_STRING_FILE and READ_MONSTER_LOCALIZATIONS live here with explicit proof labels for read-only versus safe-write sources.";
        [ObservableProperty] private string selectedRecordSummary = "Select a string entry to inspect decoded text, simplified variants, and proof notes.";
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;
        [ObservableProperty] private bool isSafeWriterEnabled;
        [ObservableProperty] private string editorScopeSummary = "Curated text explorer. Safe writing only unlocks after serializer, rebuild path, and round-trip validation are proven.";
        [ObservableProperty] private string validationSummary = "No validation issues.";
        [ObservableProperty] private bool hasValidationErrors;
        [ObservableProperty] private string pendingDiffSummary = "No pending text changes.";
        [ObservableProperty] private string selectedSourceFamilyLabel = "No family selected.";
        [ObservableProperty] private string selectedSourceTrustLabel = "NO SOURCE";
        [ObservableProperty] private IBrush selectedSourceTrustBrush = NeutralBadgeBrush;
        [ObservableProperty] private string selectedSourceWriteLabel = "IDLE";
        [ObservableProperty] private IBrush selectedSourceWriteBrush = NeutralBadgeBrush;
        [ObservableProperty] private string selectedSourceProofSummary = "Select a source to inspect proof notes and production scope.";
        [ObservableProperty] private string selectedSourceRiskSummary = "No source selected.";
        [ObservableProperty] private string selectedSourceDecisionSummary = "No production decision loaded.";

        public bool HasPendingDiffs => PendingDiffRows.Count > 0;
        public bool IsSelectedRecordReadOnly => !IsSafeWriterEnabled;
        public bool ShowReadOnlySourceNotice => SelectedSource != null && !IsSafeWriterEnabled;
        public bool CanSaveCurrentSource => IsSafeWriterEnabled && EditSession?.HasPendingChanges == true && !HasValidationErrors;
        public bool CanDiscardCurrentSource => IsSafeWriterEnabled && EditSession?.HasPendingChanges == true;
        public bool CanUndoCurrentSource => IsSafeWriterEnabled && EditSession?.CanUndo == true;
        public bool CanOpenSelectedMonster => SelectedRecord?.IsMonsterLocalizationRecord == true;
        public string SelectedMonsterJumpSummary => SelectedRecord?.IsMonsterLocalizationRecord == true
            ? $"Jump straight to monster m{SelectedRecord.MonsterIndex:D3} in Monster Editor."
            : "No linked monster target is available for this text record.";
        public string SelectedMonsterJumpButtonLabel => SelectedRecord?.IsMonsterLocalizationRecord == true
            ? $"Open Monster m{SelectedRecord.MonsterIndex:D3}"
            : "Open Monster";

        public StringExplorer_DataModel()
        {
            ReloadSources();
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedSourceChanged(StringSourceRow? value)
        {
            if (value == null)
            {
                loadedRecords.Clear();
                DisplayedRecords.Clear();
                SelectedRecord = null;
                SelectedSourceSummary = "Select a curated text source to decode it.";
                ResetSelectedSourcePresentation();
                currentMonsterLocalizationState = null;
                currentNameDescriptionState = null;
                SetActiveEditSession(null);
                IsSafeWriterEnabled = false;
                EditorScopeSummary = "Curated text explorer. Safe writing only unlocks after serializer, rebuild path, and round-trip validation are proven.";
                RefreshCurrentDiagnostics();
                return;
            }

            ApplySelectedSourcePresentation(value);
            LoadSelectedSource(value);
        }

        partial void OnSelectedRecordChanged(StringExplorerRecord? value)
        {
            SelectedRecordSummary = value == null
                ? "Select a string entry to inspect decoded text, simplified variants, and proof notes."
                : $"{value.IndexLabel} · {value.Title}";
            OnPropertyChanged(nameof(CanOpenSelectedMonster));
            OnPropertyChanged(nameof(SelectedMonsterJumpSummary));
            OnPropertyChanged(nameof(SelectedMonsterJumpButtonLabel));
        }

        partial void OnIsSafeWriterEnabledChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowReadOnlySourceNotice));
        }

        public void RefreshFromDisk()
        {
            foreach (MonsterLocalizationSourceState state in monsterLocalizationStates.Values)
                state.Session.Dispose();

            foreach (NameDescriptionSourceState state in nameDescriptionStates.Values)
                state.Session.Dispose();

            monsterLocalizationStates.Clear();
            nameDescriptionStates.Clear();
            currentMonsterLocalizationState = null;
            currentNameDescriptionState = null;
            SetActiveEditSession(null);
            ReloadSources();
        }

        public bool SaveCurrentSource()
        {
            if (!ValidateCurrentSourceForSave() || EditSession == null)
                return false;

            EditSession.Save();
            RefreshCurrentDiagnostics();
            return true;
        }

        public void UndoCurrentSource()
        {
            EditSession?.Undo();
            RefreshCurrentDiagnostics();
        }

        public void DiscardCurrentSource()
        {
            EditSession?.Discard();
            RefreshCurrentDiagnostics();
        }

        void ReloadSources()
        {
            LoadedSources.Clear();
            loadedRecords.Clear();
            DisplayedRecords.Clear();
            PendingDiffRows.Clear();
            SelectedRecord = null;
            IsSafeWriterEnabled = false;
            HasValidationErrors = false;
            ValidationSummary = "No validation issues.";
            PendingDiffSummary = "No pending text changes.";

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                return;
            }

            AddNameDescriptionSource("menu_txt", "Menu Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "menu_txt.bin"));
            AddNameDescriptionSource("config_txt", "Config Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "config_txt.bin"));
            AddNameDescriptionSource("status_txt", "Status Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "status_txt.bin"));
            AddNameDescriptionSource("summon_txt", "Summon Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "summon_txt.bin"));
            AddNameDescriptionSource("arms_txt", "Gear / Arms Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "arms_txt.bin"));
            AddNameDescriptionSource("item_txt", "Item Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "item_txt.bin"));
            AddNameDescriptionSource("mmain_txt", "Main Menu Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "mmain_txt.bin"));
            AddNameDescriptionSource("btlend_txt", "Battle End Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "btlend_txt.bin"));
            AddNameDescriptionSource("build_txt", "Build Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "build_txt.bin"));
            AddNameDescriptionSource("name_txt", "Name Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "name_txt.bin"));
            AddNameDescriptionSource("save_txt", "Save Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "save_txt.bin"));

            AddBattleTextSource("btl_txt", "Battle Text", Path.Combine(Project_Service.Instance.Path_KernelUs, "btl_txt.bin"));

            AddFieldStringSource("help_txt_us", "Help Text (US) [Field String]", Path.Combine(Project_Service.Instance.Path_MenuUs, "help_txt.bin"), FfxEncoding.UsDecoder, "READ_STRING_FILE · FIELD STRING · US", "Proven 8-byte field-string table. Reader is structurally proven, but write path remains locked.");
            AddFieldStringSource("help_txt_jp", "Help Text (JP) [Field String]", Path.Combine(Project_Service.Instance.Path_Menu, "help_txt.bin"), FfxEncoding.JpDecoder, "READ_STRING_FILE · FIELD STRING · JP", "Proven 8-byte field-string table. Reader is structurally proven, but write path remains locked.");

            AddAlBhedDictionarySource("albheddic_us", "Al Bhed Dictionary (US)", Path.Combine(Project_Service.Instance.Path_MenuUs, "albheddic.bin"), FfxEncoding.UsDecoder, "READ_STRING_FILE · AL BHED DICTIONARY · US", "Proven 4-byte Latin Al Bhed mapping table. Read-only by design.");
            AddAlBhedDictionarySource("albheddic_jp", "Al Bhed Dictionary (JP)", Path.Combine(Project_Service.Instance.Path_Menu, "albheddic.bin"), FfxEncoding.JpDecoder, "READ_STRING_FILE · AL BHED DICTIONARY · JP", "Proven 4-byte kana Al Bhed mapping table. Read-only by design.");

            AddPointerScriptTableSource("menu_script_us", "Menu Script (US) [Pointer Script Table]", Path.Combine(Project_Service.Instance.Path_MenuUs, "menu_script.bin"), FfxEncoding.UsDecoder);
            AddPointerScriptTableSource("battle_script_us", "Battle Script (US) [Pointer Script Table]", Path.Combine(Project_Service.Instance.Path_MenuUs, "battle_script.bin"), FfxEncoding.UsDecoder);
            AddPointerScriptTableSource("system_script_us", "System Script (US) [Pointer Script Table]", Path.Combine(Project_Service.Instance.Path_MenuUs, "system_script.bin"), FfxEncoding.UsDecoder);
            AddPointerScriptTableSource("menu_script_jp", "Menu Script (JP) [Pointer Script Table]", Path.Combine(Project_Service.Instance.Path_Menu, "menu_script.bin"), FfxEncoding.JpDecoder);
            AddPointerScriptTableSource("battle_script_jp", "Battle Script (JP) [Pointer Script Table]", Path.Combine(Project_Service.Instance.Path_Menu, "battle_script.bin"), FfxEncoding.JpDecoder);
            AddPointerScriptTableSource("system_script_jp", "System Script (JP) [Pointer Script Table]", Path.Combine(Project_Service.Instance.Path_Menu, "system_script.bin"), FfxEncoding.JpDecoder);

            AddLegacyMenuMainUsSource(Path.Combine(Project_Service.Instance.Path_MenuUs, "menumain.bin"));
            AddLegacyMenuMainResourceSource("menumain_jp", "Legacy MenuMain (JP) [Legacy MenuMain Resource]", Path.Combine(Project_Service.Instance.Path_Menu, "menumain.bin"), "READ_STRING_FILE · LEGACY MENUMAIN · JP");

            AddMonsterLocalizationSource("monster1", "Monster Localizations 1", Project_Service.Instance.Path_KernelMonster1Us);
            AddMonsterLocalizationSource("monster2", "Monster Localizations 2", Project_Service.Instance.Path_KernelMonster2Us);
            AddMonsterLocalizationSource("monster3", "Monster Localizations 3", Project_Service.Instance.Path_KernelMonster3Us);

            LoadSummary = $"Prepared {LoadedSources.Count} curated text sources from the loaded workspace.";
            SelectedSource = LoadedSources.FirstOrDefault();
        }

        void AddNameDescriptionSource(string id, string displayName, string path)
        {
            if (!File.Exists(path))
                return;

            LoadedSources.Add(CreateSourceRow(
                id,
                displayName,
                path,
                "READ_STRING_FILE · NAME/DESCRIPTION",
                "Proven keyed name/description table. Safe writer path enabled for regular + simplified variants with automatic offset rebuild.",
                FfxEncoding.UsDecoder,
                StringSourceKind.NameDescriptionTextFile));
        }

        void AddMonsterLocalizationSource(string id, string displayName, string path)
        {
            if (!File.Exists(path))
                return;

            LoadedSources.Add(CreateSourceRow(
                id,
                displayName,
                path,
                "READ_MONSTER_LOCALIZATIONS",
                "Monster name / sensor / scan localization table. Safe writer path enabled with automatic offset rebuild.",
                FfxEncoding.UsDecoder,
                StringSourceKind.MonsterLocalizationFile));
        }

        void AddBattleTextSource(string id, string displayName, string path)
        {
            if (!File.Exists(path))
                return;

            LoadedSources.Add(CreateSourceRow(
                id,
                displayName,
                path,
                "READ_STRING_FILE · BATTLE TEXT",
                "Dedicated 8-byte battle text table. Reader is structurally proven for the 4-word layout, but decoded text candidates remain heuristic and write path stays locked.",
                FfxEncoding.UsDecoder,
                StringSourceKind.BattleTextFile));
        }

        void AddFieldStringSource(string id, string displayName, string path, Dictionary<byte, char> decoder, string parserModeLabel, string summary)
        {
            if (!File.Exists(path))
                return;

            LoadedSources.Add(CreateSourceRow(
                id,
                displayName,
                path,
                parserModeLabel,
                summary,
                decoder,
                StringSourceKind.FieldStringFile));
        }

        void AddAlBhedDictionarySource(string id, string displayName, string path, Dictionary<byte, char> decoder, string parserModeLabel, string summary)
        {
            if (!File.Exists(path))
                return;

            LoadedSources.Add(CreateSourceRow(
                id,
                displayName,
                path,
                parserModeLabel,
                summary,
                decoder,
                StringSourceKind.AlBhedDictionaryFile));
        }

        void AddPointerScriptTableSource(string id, string displayName, string path, Dictionary<byte, char> decoder)
        {
            if (!File.Exists(path))
                return;

            LoadedSources.Add(CreateSourceRow(
                id,
                displayName,
                path,
                "READ_STRING_FILE · POINTER SCRIPT TABLE",
                "Proven 52-slot pointer-script table. Read-only by design: slot topology is proven, but no writer is exposed.",
                decoder,
                StringSourceKind.PointerScriptTableFile));
        }

        void AddLegacyMenuMainResourceSource(string id, string displayName, string path, string parserModeLabel)
        {
            if (!File.Exists(path))
                return;

            LoadedSources.Add(CreateSourceRow(
                id,
                displayName,
                path,
                parserModeLabel,
                "Proven non-text container classification for legacy menumain. Metadata and layout are surfaced read-only.",
                FfxEncoding.UsDecoder,
                StringSourceKind.LegacyMenuMainResourceFile));
        }

        void AddLegacyMenuMainUsSource(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                LegacyMenuMainResource_File.Read(File.ReadAllBytes(path));

                LoadedSources.Add(CreateSourceRow(
                    "menumain_us",
                    "Legacy MenuMain (US) [Legacy MenuMain Resource]",
                    path,
                    "READ_STRING_FILE · LEGACY MENUMAIN · US",
                    "Proven non-text container classification for legacy menumain. Metadata and layout are surfaced read-only.",
                    FfxEncoding.UsDecoder,
                    StringSourceKind.LegacyMenuMainResourceFile));
            }
            catch (Exception ex)
            {
                AddUnsupportedSource(
                    "menumain_us_diff",
                    "Legacy MenuMain (US) [Different Format]",
                    path,
                    $"US legacy menumain does not match the proven JP legacy-resource container shape yet. Current production parser stays locked here: {ex.Message}");
            }
        }

        void AddUnsupportedSource(string id, string displayName, string path, string summary)
        {
            if (!File.Exists(path))
                return;

            LoadedSources.Add(CreateSourceRow(
                id,
                displayName,
                path,
                "READ_STRING_FILE · DIFFERENT FORMAT",
                summary,
                FfxEncoding.UsDecoder,
                StringSourceKind.UnsupportedTextFile));
        }

        StringSourceRow CreateSourceRow(
            string id,
            string displayName,
            string path,
            string parserModeLabel,
            string summary,
            Dictionary<byte, char> decoder,
            StringSourceKind sourceKind)
        {
            SourcePresentationProfile profile = BuildSourcePresentation(sourceKind, summary);

            return new StringSourceRow
            {
                Id = id,
                DisplayName = displayName,
                RelativePath = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, path),
                AbsolutePath = path,
                ParserModeLabel = parserModeLabel,
                Summary = summary,
                Decoder = decoder,
                SourceKind = sourceKind,
                FamilyLabel = profile.FamilyLabel,
                TrustLabel = profile.TrustLabel,
                TrustBrush = profile.TrustBrush,
                WritePolicyLabel = profile.WritePolicyLabel,
                WritePolicyBrush = profile.WritePolicyBrush,
                ProofSummary = profile.ProofSummary,
                RiskSummary = profile.RiskSummary,
                DecisionSummary = profile.DecisionSummary
            };
        }

        SourcePresentationProfile BuildSourcePresentation(StringSourceKind sourceKind, string summary)
        {
            return sourceKind switch
            {
                StringSourceKind.NameDescriptionTextFile => new SourcePresentationProfile
                {
                    FamilyLabel = "Name / Description",
                    TrustLabel = "PROVEN",
                    TrustBrush = ProvenBadgeBrush,
                    WritePolicyLabel = "SAFE WRITER",
                    WritePolicyBrush = SafeWriterBadgeBrush,
                    ProofSummary = "Proven 4-field keyed text table with serializer, rebuild path, validation, and structural round-trip checks. " + summary,
                    RiskSummary = "Neutral byte identity can drift on rebuild, but model equality must stay intact. Unsupported changed raw-control bytes still block save.",
                    DecisionSummary = "Production write path is intentionally open only for Name, Simplified Name, Description, and Simplified Description."
                },
                StringSourceKind.MonsterLocalizationFile => new SourcePresentationProfile
                {
                    FamilyLabel = "Monster Localizations",
                    TrustLabel = "PROVEN",
                    TrustBrush = ProvenBadgeBrush,
                    WritePolicyLabel = "SAFE WRITER",
                    WritePolicyBrush = SafeWriterBadgeBrush,
                    ProofSummary = "Proven monster localization reader with guarded rebuild and reread checks for the safe text fields. " + summary,
                    RiskSummary = "Only Name, Sensor, and Scan are in scope. Everything outside those fields remains intentionally out of the writer surface.",
                    DecisionSummary = "Production write path is intentionally open only for Name, Sensor, and Scan."
                },
                StringSourceKind.BattleTextFile => new SourcePresentationProfile
                {
                    FamilyLabel = "Battle Text",
                    TrustLabel = "PROVEN",
                    TrustBrush = ProvenBadgeBrush,
                    WritePolicyLabel = "READ ONLY",
                    WritePolicyBrush = ReadOnlyBadgeBrush,
                    ProofSummary = summary,
                    RiskSummary = "Decoded strings and control-prefix semantics still carry heuristics. Reader proof is structural, not serializer proof.",
                    DecisionSummary = "Keep this family visible and inspectable in production, but do not expose any writer."
                },
                StringSourceKind.FieldStringFile => new SourcePresentationProfile
                {
                    FamilyLabel = "Field String",
                    TrustLabel = "PROVEN",
                    TrustBrush = ProvenBadgeBrush,
                    WritePolicyLabel = "READ ONLY",
                    WritePolicyBrush = ReadOnlyBadgeBrush,
                    ProofSummary = summary,
                    RiskSummary = "Decode-only tokens and local raw controls still exist here. Production must not treat the lab serializer evidence as write-safe.",
                    DecisionSummary = "Keep this family visible and read-only in production until control semantics are proven harder."
                },
                StringSourceKind.AlBhedDictionaryFile => new SourcePresentationProfile
                {
                    FamilyLabel = "Al Bhed Dictionary",
                    TrustLabel = "PROVEN",
                    TrustBrush = ProvenBadgeBrush,
                    WritePolicyLabel = "READ ONLY",
                    WritePolicyBrush = ReadOnlyBadgeBrush,
                    ProofSummary = summary,
                    RiskSummary = "This is a mapping-table surface, not a free-form text authoring family. Treating it like ordinary text would be misleading.",
                    DecisionSummary = "Keep the dictionary as a read-only reference surface in production."
                },
                StringSourceKind.PointerScriptTableFile => new SourcePresentationProfile
                {
                    FamilyLabel = "Pointer Script Table",
                    TrustLabel = "PROVEN",
                    TrustBrush = ProvenBadgeBrush,
                    WritePolicyLabel = "READ ONLY",
                    WritePolicyBrush = ReadOnlyBadgeBrush,
                    ProofSummary = summary,
                    RiskSummary = "Slot topology and decoded script blobs are proven enough to inspect, but script semantics and writer safety are not.",
                    DecisionSummary = "Keep pointer-script tables read-only in production."
                },
                StringSourceKind.LegacyMenuMainResourceFile => new SourcePresentationProfile
                {
                    FamilyLabel = "Legacy MenuMain Resource",
                    TrustLabel = "PROVEN",
                    TrustBrush = ProvenBadgeBrush,
                    WritePolicyLabel = "READ ONLY",
                    WritePolicyBrush = ReadOnlyBadgeBrush,
                    ProofSummary = summary,
                    RiskSummary = "This is a proven non-text container classification. It should not be reframed as a text table or writer candidate.",
                    DecisionSummary = "Keep legacy menumain surfaced as read-only container facts only."
                },
                _ => new SourcePresentationProfile
                {
                    FamilyLabel = "Different Format",
                    TrustLabel = "HEURISTIC",
                    TrustBrush = HeuristicBadgeBrush,
                    WritePolicyLabel = "UNSUPPORTED",
                    WritePolicyBrush = HeuristicBadgeBrush,
                    ProofSummary = summary,
                    RiskSummary = "No dedicated parser is proven for this source yet.",
                    DecisionSummary = "Keep this source explicit and blocked until a dedicated reader exists."
                }
            };
        }

        void ResetSelectedSourcePresentation()
        {
            SelectedSourceFamilyLabel = "No family selected.";
            SelectedSourceTrustLabel = "NO SOURCE";
            SelectedSourceTrustBrush = NeutralBadgeBrush;
            SelectedSourceWriteLabel = "IDLE";
            SelectedSourceWriteBrush = NeutralBadgeBrush;
            SelectedSourceProofSummary = "Select a source to inspect proof notes and production scope.";
            SelectedSourceRiskSummary = "No source selected.";
            SelectedSourceDecisionSummary = "No production decision loaded.";
            OnPropertyChanged(nameof(ShowReadOnlySourceNotice));
        }

        void ApplySelectedSourcePresentation(StringSourceRow source)
        {
            SelectedSourceFamilyLabel = source.FamilyLabel;
            SelectedSourceTrustLabel = source.TrustLabel;
            SelectedSourceTrustBrush = source.TrustBrush;
            SelectedSourceWriteLabel = source.WritePolicyLabel;
            SelectedSourceWriteBrush = source.WritePolicyBrush;
            SelectedSourceProofSummary = source.ProofSummary;
            SelectedSourceRiskSummary = source.RiskSummary;
            SelectedSourceDecisionSummary = source.DecisionSummary;
            OnPropertyChanged(nameof(ShowReadOnlySourceNotice));
        }

        void LoadSelectedSource(StringSourceRow source)
        {
            loadedRecords.Clear();
            DisplayedRecords.Clear();

            try
            {
                switch (source.SourceKind)
                {
                    case StringSourceKind.NameDescriptionTextFile:
                        currentMonsterLocalizationState = null;
                        currentNameDescriptionState = GetOrCreateNameDescriptionState(source);
                        SetActiveEditSession(currentNameDescriptionState.Session);
                        IsSafeWriterEnabled = true;
                        EditorScopeSummary = "Name / Description authoring is live here: Name, Simplified Name, Description, and Simplified Description with save, undo, discard, diff tracking, validation, rebuild, and structural round-trip checks.";
                        foreach (StringExplorerRecord record in currentNameDescriptionState.Records)
                            loadedRecords.Add(record);

                        SelectedSourceSummary = $"{source.ParserModeLabel} · {source.RelativePath} · {currentNameDescriptionState.Records.Count} entries. Safe write path rebuilds offsets and shared-string links automatically.";
                        LoadSummary = $"Loaded {currentNameDescriptionState.Records.Count} name/description entries from {source.DisplayName}.";
                        break;

                    case StringSourceKind.BattleTextFile:
                        currentMonsterLocalizationState = null;
                        currentNameDescriptionState = null;
                        SetActiveEditSession(null);
                        IsSafeWriterEnabled = false;
                        EditorScopeSummary = "Battle text stays read-only. The 8-byte entry layout is surfaced honestly, but write path remains locked until control-prefix semantics are proven harder.";
                        LoadBattleTextSource(source);
                        break;

                    case StringSourceKind.FieldStringFile:
                        currentMonsterLocalizationState = null;
                        currentNameDescriptionState = null;
                        SetActiveEditSession(null);
                        IsSafeWriterEnabled = false;
                        EditorScopeSummary = "Field-string tables are structurally proven readers only. Write path remains intentionally locked in production.";
                        LoadFieldStringSource(source);
                        break;

                    case StringSourceKind.AlBhedDictionaryFile:
                        currentMonsterLocalizationState = null;
                        currentNameDescriptionState = null;
                        SetActiveEditSession(null);
                        IsSafeWriterEnabled = false;
                        EditorScopeSummary = "Al Bhed dictionary files are proven mapping-table readers. They stay read-only and are not treated as free-form text writers.";
                        LoadAlBhedDictionarySource(source);
                        break;

                    case StringSourceKind.PointerScriptTableFile:
                        currentMonsterLocalizationState = null;
                        currentNameDescriptionState = null;
                        SetActiveEditSession(null);
                        IsSafeWriterEnabled = false;
                        EditorScopeSummary = "Pointer-script tables are read-only structural proof. Slot topology and decoded scripts are surfaced, but no writer is exposed.";
                        LoadPointerScriptTableSource(source);
                        break;

                    case StringSourceKind.LegacyMenuMainResourceFile:
                        currentMonsterLocalizationState = null;
                        currentNameDescriptionState = null;
                        SetActiveEditSession(null);
                        IsSafeWriterEnabled = false;
                        EditorScopeSummary = "Legacy menumain resource containers are proven non-text surfaces. Container facts and metadata are surfaced read-only only.";
                        LoadLegacyMenuMainResourceSource(source);
                        break;

                    case StringSourceKind.MonsterLocalizationFile:
                        currentNameDescriptionState = null;
                        currentMonsterLocalizationState = GetOrCreateMonsterLocalizationState(source);
                        SetActiveEditSession(currentMonsterLocalizationState.Session);
                        IsSafeWriterEnabled = true;
                        EditorScopeSummary = "Monster localization authoring is live here: Name, Sensor, and Scan with save, undo, discard, diff tracking, validation, rebuild, and structural round-trip checks.";
                        foreach (StringExplorerRecord record in currentMonsterLocalizationState.Records)
                            loadedRecords.Add(record);

                        SelectedSourceSummary = $"{source.ParserModeLabel} · {source.RelativePath} · {currentMonsterLocalizationState.Records.Count} monster localization entries. Safe write path: Name / Sensor / Scan only.";
                        LoadSummary = $"Loaded {currentMonsterLocalizationState.Records.Count} monster localizations from {source.DisplayName}.";
                        break;

                    case StringSourceKind.UnsupportedTextFile:
                        currentMonsterLocalizationState = null;
                        currentNameDescriptionState = null;
                        SetActiveEditSession(null);
                        IsSafeWriterEnabled = false;
                        EditorScopeSummary = "Different-format sources stay read-only until a dedicated parser exists.";
                        LoadUnsupportedSource(source);
                        break;
                }

                ApplyFilter();
                SelectedRecord = DisplayedRecords.FirstOrDefault();
                RefreshCurrentDiagnostics();
            }
            catch (Exception ex)
            {
                DisplayedRecords.Clear();
                SelectedRecord = null;
                LoadSummary = $"Failed to read {source.DisplayName}: {ex.Message}";
                currentMonsterLocalizationState = null;
                currentNameDescriptionState = null;
                SetActiveEditSession(null);
                IsSafeWriterEnabled = false;
                EditorScopeSummary = "Curated text explorer. Safe writing only unlocks after serializer, rebuild path, and round-trip validation are proven.";
                RefreshCurrentDiagnostics();
            }
        }

        void LoadFieldStringSource(StringSourceRow source)
        {
            byte[] bytes = File.ReadAllBytes(source.AbsolutePath);
            TextTable_File textTable = TextTable_File.Read(bytes, source.Decoder);

            foreach (TextTable_Entry entry in textTable.Entries)
            {
                loadedRecords.Add(new StringExplorerRecord
                {
                    IndexLabel = entry.IndexLabel,
                    Title = entry.Preview,
                    Summary = entry.FlagsSummary,
                    PrimaryLabel = "Regular",
                    PrimaryText = string.IsNullOrWhiteSpace(entry.RegularText) ? "(Empty)" : entry.RegularText,
                    SecondaryLabel = "Simplified",
                    SecondaryText = string.IsNullOrWhiteSpace(entry.SimplifiedText) ? "(Empty)" : entry.SimplifiedText,
                    DetailsLabel = "Headers / Proof",
                    DetailsText =
                        $"{entry.FlagsSummary}{Environment.NewLine}{entry.SimplifiedSummary}{Environment.NewLine}{Environment.NewLine}" +
                        "Write Path: intentionally locked. This family is structurally proven for reading, but no production writer is exposed.",
                    SearchBlob = entry.SearchBlob
                });
            }

            SelectedSourceSummary = $"{source.ParserModeLabel} · {source.RelativePath} · {textTable.EntryCount} strings decoded from a {textTable.FileSize:N0}-byte file.";
            LoadSummary = $"Loaded {textTable.EntryCount} strings from {source.DisplayName}.";
        }

        void LoadBattleTextSource(StringSourceRow source)
        {
            byte[] bytes = File.ReadAllBytes(source.AbsolutePath);
            BattleTextTable_File textTable = BattleTextTable_File.Read(bytes, source.Decoder);

            foreach (BattleTextTable_Entry entry in textTable.Entries)
            {
                loadedRecords.Add(new StringExplorerRecord
                {
                    IndexLabel = entry.IndexLabel,
                    Title = entry.PreferredTitle,
                    Summary = TrimForSummary(entry.PreferredSummary),
                    PrimaryLabel = "Candidate A / W0",
                    PrimaryText = string.IsNullOrWhiteSpace(entry.PrimaryText) ? "(Empty)" : entry.PrimaryText,
                    SecondaryLabel = "Candidate B / W1",
                    SecondaryText = string.IsNullOrWhiteSpace(entry.SecondaryText) ? "(Empty)" : entry.SecondaryText,
                    TertiaryLabel = "Candidate C+D / Raw",
                    TertiaryText =
                        $"W2:{Environment.NewLine}{(string.IsNullOrWhiteSpace(entry.TertiaryText) ? "(Empty)" : entry.TertiaryText)}{Environment.NewLine}{Environment.NewLine}" +
                        $"W3:{Environment.NewLine}{(string.IsNullOrWhiteSpace(entry.QuaternaryText) ? "(Empty)" : entry.QuaternaryText)}{Environment.NewLine}{Environment.NewLine}" +
                        $"Raw Words:{Environment.NewLine}{entry.BuildRawSummary()}",
                    SearchBlob = entry.SearchBlob
                });
            }

            SelectedSourceSummary = $"{source.ParserModeLabel} · {source.RelativePath} · {textTable.EntryCount} battle text entries decoded from an 8-byte-entry table. Candidate strings are heuristic, but the raw word layout is surfaced honestly.";
            LoadSummary = $"Loaded {textTable.EntryCount} battle text entries from {source.DisplayName}.";
        }

        void LoadAlBhedDictionarySource(StringSourceRow source)
        {
            byte[] bytes = File.ReadAllBytes(source.AbsolutePath);
            AlBhedDictionary_File file = AlBhedDictionary_File.Read(bytes, source.Decoder);

            foreach (AlBhedDictionary_Entry entry in file.Entries.Where(entry => !entry.IsPadding))
            {
                loadedRecords.Add(new StringExplorerRecord
                {
                    IndexLabel = entry.IndexLabel,
                    Title = entry.Title,
                    Summary = $"Group {entry.GroupIndex} · source {entry.SourceCode:X2}h · mapped {entry.MappedCode:X2}h",
                    PrimaryLabel = "Source Glyph",
                    PrimaryText = entry.SourceGlyph.ToString(),
                    SecondaryLabel = "Mapped Glyph",
                    SecondaryText = entry.MappedGlyph.ToString(),
                    TertiaryLabel = "Group Bucket",
                    TertiaryText = entry.GroupIndex.ToString(),
                    DetailsLabel = "Raw Bytes / Proof",
                    DetailsText =
                        $"{entry.Summary}{Environment.NewLine}{Environment.NewLine}" +
                        $"Family Proof:{Environment.NewLine}{file.Summary}{Environment.NewLine}{Environment.NewLine}" +
                        "Write Path: intentionally locked. This dictionary is structurally proven as a mapping table, but it is not exposed as a free-form text writer.",
                    SearchBlob = entry.SearchBlob
                });
            }

            SelectedSourceSummary = $"{source.ParserModeLabel} · {source.RelativePath} · {file.ActiveEntryCount} proven mapping rows from a {file.FileSize:N0}-byte dictionary. {file.Summary}";
            LoadSummary = $"Loaded {file.ActiveEntryCount} Al Bhed mapping rows from {source.DisplayName}.";
        }

        void LoadPointerScriptTableSource(StringSourceRow source)
        {
            byte[] bytes = File.ReadAllBytes(source.AbsolutePath);
            PointerScriptTable_File file = PointerScriptTable_File.Read(bytes, source.Decoder);

            foreach (PointerScriptTable_Entry entry in file.Entries)
            {
                loadedRecords.Add(new StringExplorerRecord
                {
                    IndexLabel = entry.IndexLabel,
                    Title = entry.Title,
                    Summary = entry.Summary,
                    PrimaryLabel = "Decoded Script",
                    PrimaryText = entry.IsEmpty ? "(Empty Slot)" : (string.IsNullOrWhiteSpace(entry.Text) ? "(Empty Script)" : entry.Text),
                    SecondaryLabel = "Pointer",
                    SecondaryText = entry.IsEmpty ? "0000h (NULL)" : $"{entry.Offset:X4}h",
                    TertiaryLabel = "Slot State",
                    TertiaryText = entry.IsEmpty ? "Empty" : entry.SharedWithIndex.HasValue ? $"Shared with {entry.SharedWithIndex.Value:X2}h" : "Unique offset",
                    DetailsLabel = "Proof / Layout",
                    DetailsText =
                        $"{entry.Summary}{Environment.NewLine}{Environment.NewLine}" +
                        $"Family Proof:{Environment.NewLine}{file.Summary}{Environment.NewLine}{Environment.NewLine}" +
                        "Write Path: intentionally locked. This source is a proven pointer-script table, not a free-form text writer.",
                    SearchBlob = entry.SearchBlob
                });
            }

            SelectedSourceSummary = $"{source.ParserModeLabel} · {source.RelativePath} · {file.EntryCount} script slots from a {file.FileSize:N0}-byte pointer table. {file.Summary}";
            LoadSummary = $"Loaded {file.EntryCount} pointer-script slots from {source.DisplayName}.";
        }

        void LoadLegacyMenuMainResourceSource(StringSourceRow source)
        {
            byte[] bytes = File.ReadAllBytes(source.AbsolutePath);
            LegacyMenuMainResource_File file = LegacyMenuMainResource_File.Read(bytes);

            loadedRecords.Add(new StringExplorerRecord
            {
                IndexLabel = "Metadata",
                Title = file.ResourceName,
                Summary = "Legacy non-text resource metadata.",
                PrimaryLabel = "Author",
                PrimaryText = file.Author,
                SecondaryLabel = "Resource Name",
                SecondaryText = file.ResourceName,
                TertiaryLabel = "Build Timestamp",
                TertiaryText = file.Timestamp,
                DetailsLabel = "Proof / Scope",
                DetailsText =
                    $"{file.Summary}{Environment.NewLine}{Environment.NewLine}" +
                    "This container is structurally proven on the legacy uspc/jppc menumain copies, but it is intentionally not treated as a free-form text table or writer-safe family.",
                SearchBlob = $"{file.Author} {file.ResourceName} {file.Timestamp} legacy menumain non text container"
            });

            loadedRecords.Add(new StringExplorerRecord
            {
                IndexLabel = "Layout",
                Title = "Container Offsets",
                Summary = $"Body {file.BodyOffset:X4}h · Entry {file.EntryOffset:X4}h · Footer {file.SecondaryFooterOffset:X4}h/{file.PrimaryFooterOffset:X4}h",
                PrimaryLabel = "Body Offset",
                PrimaryText = $"{file.BodyOffset:X4}h",
                SecondaryLabel = "Entry Offset",
                SecondaryText = $"{file.EntryOffset:X4}h",
                TertiaryLabel = "Footer Offsets",
                TertiaryText = $"{file.SecondaryFooterOffset:X4}h / {file.PrimaryFooterOffset:X4}h",
                DetailsLabel = "Proof / Scope",
                DetailsText =
                    $"Metadata block {file.MetadataOffset:X4}h · timestamp block {file.TimestampOffset:X4}h · body length {file.BodyLength:X}h.{Environment.NewLine}{Environment.NewLine}" +
                    "Write Path: intentionally locked. This is a proven container classification, not a text serializer.",
                SearchBlob = $"{file.BodyOffset:X4} {file.EntryOffset:X4} {file.SecondaryFooterOffset:X4} {file.PrimaryFooterOffset:X4} legacy menumain layout"
            });

            loadedRecords.Add(new StringExplorerRecord
            {
                IndexLabel = "Footer",
                Title = "Primary Descriptor Offsets",
                Summary = $"{file.PrimaryDescriptorOffsets.Count} proven descriptor pointer(s).",
                PrimaryLabel = "Descriptor Offsets",
                PrimaryText = file.PrimaryDescriptorSummary,
                SecondaryLabel = "Container Summary",
                SecondaryText = file.Summary,
                TertiaryLabel = "Write Path",
                TertiaryText = "Read-only / non-text",
                DetailsLabel = "Proof / Scope",
                DetailsText =
                    $"The primary footer keeps {file.PrimaryDescriptorOffsets.Count} non-zero descriptor offsets followed by trailing zeros, with descriptor 07h echoing entry-1.{Environment.NewLine}{Environment.NewLine}" +
                    "This is enough to classify the file honestly as a legacy resource container instead of pretending it is an unsupported text table.",
                SearchBlob = $"{file.PrimaryDescriptorSummary} {file.Summary} read only non text"
            });

            SelectedSourceSummary = $"{source.ParserModeLabel} · {source.RelativePath} · {file.FileSize:N0}-byte legacy menumain container. {file.Summary}";
            LoadSummary = $"Loaded legacy non-text container facts from {source.DisplayName}.";
        }

        void LoadUnsupportedSource(StringSourceRow source)
        {
            long fileSize = new FileInfo(source.AbsolutePath).Length;

            loadedRecords.Add(new StringExplorerRecord
            {
                IndexLabel = "N/A",
                Title = "Different Format",
                Summary = "This file is populated, but the explorer has not proven its entry structure yet.",
                PrimaryLabel = "Status",
                PrimaryText = "Different-format localized payload detected. This source stays blocked instead of faking a decode result.",
                SecondaryLabel = "What The Editor Knows",
                SecondaryText = $"{source.DisplayName} exists at {source.RelativePath} and is {fileSize:N0} bytes long. It likely contains localized text, but not in a proven entry shape.",
                TertiaryLabel = "Next Step",
                TertiaryText = "Build a dedicated parser for this file instead of pretending it is empty.",
                SearchBlob = $"{source.DisplayName} different format unsupported"
            });

            SelectedSourceSummary = $"{source.ParserModeLabel} · {source.RelativePath} · Different-format localized payload detected. The explorer flags this honestly instead of inventing a fake 0-string decode.";
            LoadSummary = $"{source.DisplayName} is currently marked as Different Format. The file is populated, but this explorer has not proven the correct reader yet.";
        }

        NameDescriptionSourceState GetOrCreateNameDescriptionState(StringSourceRow source)
        {
            if (nameDescriptionStates.TryGetValue(source.AbsolutePath, out NameDescriptionSourceState? existing))
                return existing;

            byte[] bytes = File.ReadAllBytes(source.AbsolutePath);
            NameDescriptionTextTable_File file = NameDescriptionTextTable_File.Read(bytes, source.Decoder);
            ObservableCollection<StringExplorerRecord> records = BuildNameDescriptionRecords(file);

            NameDescriptionSourceState state = new()
            {
                Source = source,
                Decoder = source.Decoder,
                File = file,
                Records = records
            };

            foreach (StringExplorerRecord record in records)
                record.PropertyChanged += (_, e) => NameDescriptionRecordChanged(state, record, e);

            state.Session = new ByteSnapshotEditorSession(
                () => CaptureNameDescriptionSnapshot(state),
                snapshot => RestoreNameDescriptionSnapshot(state, snapshot),
                snapshot => PersistNameDescriptionSnapshot(state, snapshot),
                $"{source.DisplayName.ToLowerInvariant()} text",
                CaptureNameDescriptionSnapshot(state));

            nameDescriptionStates[source.AbsolutePath] = state;
            return state;
        }

        ObservableCollection<StringExplorerRecord> BuildNameDescriptionRecords(NameDescriptionTextTable_File file)
        {
            ObservableCollection<StringExplorerRecord> records = [];

            for (int i = 0; i < file.Entries.Count; i++)
            {
                NameDescriptionTextTable_Entry entry = file.Entries[i];
                StringExplorerRecord record = new()
                {
                    SourceEntryIndex = i,
                    IndexLabel = entry.IndexLabel,
                    PrimaryLabel = "Name",
                    PrimaryText = entry.NameText,
                    SecondaryLabel = "Simplified Name",
                    SecondaryText = entry.SimplifiedNameText,
                    TertiaryLabel = "Description",
                    TertiaryText = entry.DescriptionText,
                    QuaternaryLabel = "Simplified Description",
                    QuaternaryText = entry.SimplifiedDescriptionText,
                    DetailsLabel = "Headers / Proof",
                    DetailsText = BuildNameDescriptionProofSummary(entry)
                };

                RefreshNameDescriptionRecordPresentation(record);
                records.Add(record);
            }

            return records;
        }

        void NameDescriptionRecordChanged(NameDescriptionSourceState state, StringExplorerRecord record, PropertyChangedEventArgs e)
        {
            if (state.SuppressRecordTracking)
                return;

            if (e.PropertyName is nameof(StringExplorerRecord.PrimaryText)
                or nameof(StringExplorerRecord.SecondaryText)
                or nameof(StringExplorerRecord.TertiaryText)
                or nameof(StringExplorerRecord.QuaternaryText))
            {
                RefreshNameDescriptionRecordPresentation(record);
                state.Session.NotifyPotentialMutation();

                if (ReferenceEquals(currentNameDescriptionState, state))
                    RefreshCurrentDiagnostics();
            }
        }

        void RefreshNameDescriptionRecordPresentation(StringExplorerRecord record)
        {
            string name = NormalizeText(record.PrimaryText);
            string simplifiedName = NormalizeText(record.SecondaryText);
            string description = NormalizeText(record.TertiaryText);
            string simplifiedDescription = NormalizeText(record.QuaternaryText);

            List<string> notes = [];
            if (!string.Equals(name, simplifiedName, StringComparison.Ordinal))
                notes.Add("split simplified name");

            if (!string.Equals(description, simplifiedDescription, StringComparison.Ordinal))
                notes.Add("split simplified description");

            record.Title = string.IsNullOrWhiteSpace(name) ? "(Unnamed entry)" : name;
            record.Summary = notes.Count == 0
                ? TrimForSummary(description)
                : $"{TrimForSummary(description)} · {string.Join(", ", notes)}";
            record.SearchBlob = $"{record.IndexLabel} {name} {simplifiedName} {description} {simplifiedDescription} {record.DetailsText}";

            if (SelectedRecord == record)
                SelectedRecordSummary = $"{record.IndexLabel} · {record.Title}";
        }

        byte[] CaptureNameDescriptionSnapshot(NameDescriptionSourceState state)
        {
            NameDescriptionSnapshotRow[] snapshotRows = state.Records
                .Select(record => new NameDescriptionSnapshotRow
                {
                    SourceEntryIndex = record.SourceEntryIndex,
                    Name = record.PrimaryText,
                    SimplifiedName = record.SecondaryText,
                    Description = record.TertiaryText,
                    SimplifiedDescription = record.QuaternaryText
                })
                .ToArray();

            return JsonSerializer.SerializeToUtf8Bytes(snapshotRows);
        }

        void RestoreNameDescriptionSnapshot(NameDescriptionSourceState state, byte[] snapshotBytes)
        {
            NameDescriptionSnapshotRow[]? snapshotRows = JsonSerializer.Deserialize<NameDescriptionSnapshotRow[]>(snapshotBytes);
            if (snapshotRows == null)
                return;

            Dictionary<int, NameDescriptionSnapshotRow> snapshotMap = snapshotRows.ToDictionary(row => row.SourceEntryIndex);

            state.SuppressRecordTracking = true;
            try
            {
                foreach (StringExplorerRecord record in state.Records)
                {
                    if (!snapshotMap.TryGetValue(record.SourceEntryIndex, out NameDescriptionSnapshotRow? snapshot))
                        continue;

                    record.PrimaryText = snapshot.Name;
                    record.SecondaryText = snapshot.SimplifiedName;
                    record.TertiaryText = snapshot.Description;
                    record.QuaternaryText = snapshot.SimplifiedDescription;
                    RefreshNameDescriptionRecordPresentation(record);
                }
            }
            finally
            {
                state.SuppressRecordTracking = false;
            }

            if (ReferenceEquals(currentNameDescriptionState, state))
            {
                ApplyFilter();
                RefreshCurrentDiagnostics();
            }
        }

        void PersistNameDescriptionSnapshot(NameDescriptionSourceState state, byte[] snapshotBytes)
        {
            byte[] fileBytes = BuildNameDescriptionFileBytes(state, snapshotBytes);
            File.WriteAllBytes(state.Source.AbsolutePath, fileBytes);
            state.File = NameDescriptionTextTable_File.Read(fileBytes, state.Decoder);

            if (ReferenceEquals(currentNameDescriptionState, state))
                RefreshCurrentDiagnostics();
        }

        byte[] BuildNameDescriptionFileBytes(NameDescriptionSourceState state, byte[] snapshotBytes)
        {
            NameDescriptionSnapshotRow[]? snapshotRows = JsonSerializer.Deserialize<NameDescriptionSnapshotRow[]>(snapshotBytes);
            if (snapshotRows == null)
                throw new InvalidDataException("Name/description snapshot could not be read.");

            NameDescriptionTextTable_File file = NameDescriptionTextTable_File.Read(File.ReadAllBytes(state.Source.AbsolutePath), state.Decoder);
            Dictionary<int, NameDescriptionSnapshotRow> snapshotMap = snapshotRows.ToDictionary(row => row.SourceEntryIndex);

            for (int i = 0; i < file.Entries.Count; i++)
            {
                if (!snapshotMap.TryGetValue(i, out NameDescriptionSnapshotRow? snapshot))
                    continue;

                file.Entries[i].NameText = NormalizeText(snapshot.Name);
                file.Entries[i].SimplifiedNameText = NormalizeText(snapshot.SimplifiedName);
                file.Entries[i].DescriptionText = NormalizeText(snapshot.Description);
                file.Entries[i].SimplifiedDescriptionText = NormalizeText(snapshot.SimplifiedDescription);
            }

            byte[] rebuiltBytes = file.Write(state.Decoder);
            VerifyNameDescriptionRoundTrip(snapshotMap, NameDescriptionTextTable_File.Read(rebuiltBytes, state.Decoder));
            return rebuiltBytes;
        }

        void VerifyNameDescriptionRoundTrip(Dictionary<int, NameDescriptionSnapshotRow> snapshotMap, NameDescriptionTextTable_File rebuiltFile)
        {
            if (snapshotMap.Count != rebuiltFile.EntryCount)
                throw new InvalidDataException("Name/description rebuild changed the entry count.");

            for (int i = 0; i < rebuiltFile.Entries.Count; i++)
            {
                if (!snapshotMap.TryGetValue(i, out NameDescriptionSnapshotRow? snapshot))
                    throw new InvalidDataException($"Name/description rebuild lost entry {i}.");

                NameDescriptionTextTable_Entry entry = rebuiltFile.Entries[i];
                EnsureRoundTripField($"Index {entry.Index:X2}h Name", snapshot.Name, entry.NameText);
                EnsureRoundTripField($"Index {entry.Index:X2}h Simplified Name", snapshot.SimplifiedName, entry.SimplifiedNameText);
                EnsureRoundTripField($"Index {entry.Index:X2}h Description", snapshot.Description, entry.DescriptionText);
                EnsureRoundTripField($"Index {entry.Index:X2}h Simplified Description", snapshot.SimplifiedDescription, entry.SimplifiedDescriptionText);
            }
        }

        MonsterLocalizationSourceState GetOrCreateMonsterLocalizationState(StringSourceRow source)
        {
            if (monsterLocalizationStates.TryGetValue(source.AbsolutePath, out MonsterLocalizationSourceState? existing))
                return existing;

            byte[] bytes = File.ReadAllBytes(source.AbsolutePath);
            MonX_File file = MonX_File.Read(bytes);
            ObservableCollection<StringExplorerRecord> records = BuildMonsterLocalizationRecords(file);

            MonsterLocalizationSourceState state = new()
            {
                Source = source,
                File = file,
                Records = records
            };

            foreach (StringExplorerRecord record in records)
                record.PropertyChanged += (_, e) => MonsterLocalizationRecordChanged(state, record, e);

            state.Session = new ByteSnapshotEditorSession(
                () => CaptureMonsterLocalizationSnapshot(state),
                snapshot => RestoreMonsterLocalizationSnapshot(state, snapshot),
                snapshot => PersistMonsterLocalizationSnapshot(state, snapshot),
                $"{source.DisplayName.ToLowerInvariant()} text",
                CaptureMonsterLocalizationSnapshot(state));

            monsterLocalizationStates[source.AbsolutePath] = state;
            return state;
        }

        ObservableCollection<StringExplorerRecord> BuildMonsterLocalizationRecords(MonX_File file)
        {
            ObservableCollection<StringExplorerRecord> records = [];

            for (int i = 0; i < file.Entries.Count; i++)
            {
                MonX_File.Entry entry = file.Entries[i];
                int monsterIndex = file.ThisHeader.PreviousFileCount + i;

                StringExplorerRecord record = new()
                {
                    IsMonsterLocalizationRecord = true,
                    MonsterIndex = monsterIndex,
                    SourceEntryIndex = i,
                    IndexLabel = $"m{monsterIndex:D3}",
                    PrimaryLabel = "Name",
                    PrimaryText = string.IsNullOrWhiteSpace(entry.Name) ? string.Empty : entry.Name,
                    SecondaryLabel = "Sensor",
                    SecondaryText = string.IsNullOrWhiteSpace(entry.Sensor) ? string.Empty : entry.Sensor,
                    TertiaryLabel = "Scan",
                    TertiaryText = string.IsNullOrWhiteSpace(entry.Scan) ? string.Empty : entry.Scan,
                    DetailsLabel = "Write Path",
                    DetailsText = "Offsets rebuild automatically on save. Unknown text slots remain locked and untouched."
                };

                RefreshMonsterLocalizationRecordPresentation(record);
                records.Add(record);
            }

            return records;
        }

        void MonsterLocalizationRecordChanged(MonsterLocalizationSourceState state, StringExplorerRecord record, PropertyChangedEventArgs e)
        {
            if (state.SuppressRecordTracking)
                return;

            if (e.PropertyName is nameof(StringExplorerRecord.PrimaryText)
                or nameof(StringExplorerRecord.SecondaryText)
                or nameof(StringExplorerRecord.TertiaryText))
            {
                RefreshMonsterLocalizationRecordPresentation(record);
                state.Session.NotifyPotentialMutation();

                if (ReferenceEquals(currentMonsterLocalizationState, state))
                    RefreshCurrentDiagnostics();
            }
        }

        void RefreshMonsterLocalizationRecordPresentation(StringExplorerRecord record)
        {
            string name = NormalizeText(record.PrimaryText);
            string sensor = NormalizeText(record.SecondaryText);
            string scan = NormalizeText(record.TertiaryText);

            record.Title = string.IsNullOrWhiteSpace(name) ? "(Unnamed monster)" : name;
            record.Summary = $"Sensor: {TrimForSummary(sensor)}";
            record.SearchBlob = $"{record.MonsterIndex:D3} {name} {sensor} {scan}";

            if (SelectedRecord == record)
                SelectedRecordSummary = $"{record.IndexLabel} · {record.Title}";
        }

        byte[] CaptureMonsterLocalizationSnapshot(MonsterLocalizationSourceState state)
        {
            MonsterLocalizationSnapshotRow[] snapshotRows = state.Records
                .Select(record => new MonsterLocalizationSnapshotRow
                {
                    SourceEntryIndex = record.SourceEntryIndex,
                    Name = record.PrimaryText,
                    Sensor = record.SecondaryText,
                    Scan = record.TertiaryText
                })
                .ToArray();

            return JsonSerializer.SerializeToUtf8Bytes(snapshotRows);
        }

        void RestoreMonsterLocalizationSnapshot(MonsterLocalizationSourceState state, byte[] snapshotBytes)
        {
            MonsterLocalizationSnapshotRow[]? snapshotRows = JsonSerializer.Deserialize<MonsterLocalizationSnapshotRow[]>(snapshotBytes);
            if (snapshotRows == null)
                return;

            Dictionary<int, MonsterLocalizationSnapshotRow> snapshotMap = snapshotRows.ToDictionary(row => row.SourceEntryIndex);

            state.SuppressRecordTracking = true;
            try
            {
                foreach (StringExplorerRecord record in state.Records)
                {
                    if (!snapshotMap.TryGetValue(record.SourceEntryIndex, out MonsterLocalizationSnapshotRow? snapshot))
                        continue;

                    record.PrimaryText = snapshot.Name;
                    record.SecondaryText = snapshot.Sensor;
                    record.TertiaryText = snapshot.Scan;
                    RefreshMonsterLocalizationRecordPresentation(record);
                }
            }
            finally
            {
                state.SuppressRecordTracking = false;
            }

            if (ReferenceEquals(currentMonsterLocalizationState, state))
            {
                ApplyFilter();
                RefreshCurrentDiagnostics();
            }
        }

        void PersistMonsterLocalizationSnapshot(MonsterLocalizationSourceState state, byte[] snapshotBytes)
        {
            byte[] fileBytes = BuildMonsterLocalizationFileBytes(state, snapshotBytes);
            File.WriteAllBytes(state.Source.AbsolutePath, fileBytes);
            state.File = MonX_File.Read(fileBytes);

            if (ReferenceEquals(currentMonsterLocalizationState, state))
                RefreshCurrentDiagnostics();
        }

        byte[] BuildMonsterLocalizationFileBytes(MonsterLocalizationSourceState state, byte[] snapshotBytes)
        {
            MonsterLocalizationSnapshotRow[]? snapshotRows = JsonSerializer.Deserialize<MonsterLocalizationSnapshotRow[]>(snapshotBytes);
            if (snapshotRows == null)
                throw new InvalidDataException("Monster localization snapshot could not be read.");

            MonX_File file = MonX_File.Read(File.ReadAllBytes(state.Source.AbsolutePath));
            Dictionary<int, MonsterLocalizationSnapshotRow> snapshotMap = snapshotRows.ToDictionary(row => row.SourceEntryIndex);

            for (int i = 0; i < file.Entries.Count; i++)
            {
                if (!snapshotMap.TryGetValue(i, out MonsterLocalizationSnapshotRow? snapshot))
                    continue;

                file.Entries[i].Name = NormalizeText(snapshot.Name);
                file.Entries[i].Sensor = NormalizeText(snapshot.Sensor);
                file.Entries[i].Scan = NormalizeText(snapshot.Scan);
            }

            byte[] rebuiltBytes = file.Write();
            VerifyMonsterLocalizationRoundTrip(snapshotMap, MonX_File.Read(rebuiltBytes));
            return rebuiltBytes;
        }

        void VerifyMonsterLocalizationRoundTrip(Dictionary<int, MonsterLocalizationSnapshotRow> snapshotMap, MonX_File rebuiltFile)
        {
            if (snapshotMap.Count != rebuiltFile.Entries.Count)
                throw new InvalidDataException("Monster localization rebuild changed the entry count.");

            for (int i = 0; i < rebuiltFile.Entries.Count; i++)
            {
                if (!snapshotMap.TryGetValue(i, out MonsterLocalizationSnapshotRow? snapshot))
                    throw new InvalidDataException($"Monster localization rebuild lost entry {i}.");

                MonX_File.Entry entry = rebuiltFile.Entries[i];
                EnsureRoundTripField($"m{rebuiltFile.ThisHeader.PreviousFileCount + i:D3} Name", snapshot.Name, entry.Name);
                EnsureRoundTripField($"m{rebuiltFile.ThisHeader.PreviousFileCount + i:D3} Sensor", snapshot.Sensor, entry.Sensor);
                EnsureRoundTripField($"m{rebuiltFile.ThisHeader.PreviousFileCount + i:D3} Scan", snapshot.Scan, entry.Scan);
            }
        }

        bool ValidateCurrentSourceForSave()
        {
            if (currentMonsterLocalizationState != null)
                return ValidateMonsterLocalizationForSave(currentMonsterLocalizationState);

            if (currentNameDescriptionState != null)
                return ValidateNameDescriptionForSave(currentNameDescriptionState);

            return false;
        }

        bool ValidateNameDescriptionForSave(NameDescriptionSourceState state)
        {
            if (!ValidateNameDescriptionRecords(state, out List<string> issues))
            {
                HasValidationErrors = true;
                ValidationSummary = issues.FirstOrDefault() ?? "Name/description validation failed.";
                OnPropertyChanged(nameof(CanSaveCurrentSource));
                return false;
            }

            try
            {
                BuildNameDescriptionFileBytes(state, CaptureNameDescriptionSnapshot(state));
                ValidationSummary = "Validation clean. Writable charset/tokens passed, unchanged raw-control bytes stay preserved from the original file, and automatic offset rebuild survived structural round-trip.";
                HasValidationErrors = false;
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

        bool ValidateMonsterLocalizationForSave(MonsterLocalizationSourceState state)
        {
            if (!ValidateMonsterLocalizationRecords(state, out List<string> issues))
            {
                HasValidationErrors = true;
                ValidationSummary = issues.FirstOrDefault() ?? "Monster localization validation failed.";
                OnPropertyChanged(nameof(CanSaveCurrentSource));
                return false;
            }

            try
            {
                BuildMonsterLocalizationFileBytes(state, CaptureMonsterLocalizationSnapshot(state));
                ValidationSummary = "Validation clean. Writable charset/tokens passed, unchanged raw-control bytes stay preserved from the original file, and automatic offset rebuild survived structural round-trip.";
                HasValidationErrors = false;
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

        bool ValidateNameDescriptionRecords(NameDescriptionSourceState state, out List<string> issues)
        {
            issues = [];

            for (int i = 0; i < state.Records.Count; i++)
            {
                StringExplorerRecord record = state.Records[i];
                NameDescriptionTextTable_Entry baseline = state.File.Entries[i];
                ValidateTextField(record.IndexLabel, "Name", record.PrimaryText, baseline.NameScriptBytes, state.Decoder, issues);
                ValidateTextField(record.IndexLabel, "Simplified Name", record.SecondaryText, baseline.SimplifiedNameScriptBytes, state.Decoder, issues);
                ValidateTextField(record.IndexLabel, "Description", record.TertiaryText, baseline.DescriptionScriptBytes, state.Decoder, issues);
                ValidateTextField(record.IndexLabel, "Simplified Description", record.QuaternaryText, baseline.SimplifiedDescriptionScriptBytes, state.Decoder, issues);
            }

            return issues.Count == 0;
        }

        bool ValidateMonsterLocalizationRecords(MonsterLocalizationSourceState state, out List<string> issues)
        {
            issues = [];

            for (int i = 0; i < state.Records.Count; i++)
            {
                StringExplorerRecord record = state.Records[i];
                MonX_File.Entry baseline = state.File.Entries[i];
                ValidateTextField(record.IndexLabel, "Name", record.PrimaryText, baseline.NameScriptBytes, FfxEncoding.UsDecoder, issues);
                ValidateTextField(record.IndexLabel, "Sensor", record.SecondaryText, baseline.SensorScriptBytes, FfxEncoding.UsDecoder, issues);
                ValidateTextField(record.IndexLabel, "Scan", record.TertiaryText, baseline.ScanScriptBytes, FfxEncoding.UsDecoder, issues);
            }

            return issues.Count == 0;
        }

        void RefreshCurrentDiagnostics()
        {
            if (currentMonsterLocalizationState != null)
            {
                RefreshMonsterLocalizationDiagnostics(currentMonsterLocalizationState);
                return;
            }

            if (currentNameDescriptionState != null)
            {
                RefreshNameDescriptionDiagnostics(currentNameDescriptionState);
                return;
            }

            PendingDiffRows.Clear();
            HasValidationErrors = false;
            ValidationSummary = "Text writer validation is idle until a proven writable source is selected.";
            PendingDiffSummary = "No pending text changes.";
            OnPropertyChanged(nameof(HasPendingDiffs));
            OnPropertyChanged(nameof(CanSaveCurrentSource));
            OnPropertyChanged(nameof(CanDiscardCurrentSource));
            OnPropertyChanged(nameof(CanUndoCurrentSource));
            OnPropertyChanged(nameof(IsSelectedRecordReadOnly));
        }

        void RefreshNameDescriptionDiagnostics(NameDescriptionSourceState state)
        {
            PendingDiffRows.Clear();

            if (ValidateNameDescriptionRecords(state, out List<string> issues))
            {
                HasValidationErrors = false;
                ValidationSummary = "Validation clean. Supported FFX control tags and charset are writable here; unchanged unsupported raw-control bytes can stay preserved from the original file, and offsets rebuild automatically.";
            }
            else
            {
                HasValidationErrors = true;
                ValidationSummary = issues.FirstOrDefault() ?? "Name/description validation failed.";
            }

            for (int i = 0; i < state.Records.Count; i++)
            {
                StringExplorerRecord record = state.Records[i];
                NameDescriptionTextTable_Entry baseline = state.File.Entries[i];
                List<string> changedFields = [];

                if (!string.Equals(NormalizeText(record.PrimaryText), NormalizeText(baseline.NameText), StringComparison.Ordinal))
                    changedFields.Add("Name");

                if (!string.Equals(NormalizeText(record.SecondaryText), NormalizeText(baseline.SimplifiedNameText), StringComparison.Ordinal))
                    changedFields.Add("Simplified Name");

                if (!string.Equals(NormalizeText(record.TertiaryText), NormalizeText(baseline.DescriptionText), StringComparison.Ordinal))
                    changedFields.Add("Description");

                if (!string.Equals(NormalizeText(record.QuaternaryText), NormalizeText(baseline.SimplifiedDescriptionText), StringComparison.Ordinal))
                    changedFields.Add("Simplified Description");

                if (changedFields.Count == 0)
                    continue;

                PendingDiffRows.Add(new StringExplorerDiffRow
                {
                    IndexLabel = record.IndexLabel,
                    Title = record.Title,
                    FieldsSummary = string.Join(", ", changedFields)
                });
            }

            PendingDiffSummary = PendingDiffRows.Count == 0
                ? "No pending name/description text changes."
                : $"{PendingDiffRows.Count} name/description entries changed. Offsets and shared-string links will rebuild automatically on save.";

            OnPropertyChanged(nameof(HasPendingDiffs));
            OnPropertyChanged(nameof(CanSaveCurrentSource));
            OnPropertyChanged(nameof(CanDiscardCurrentSource));
            OnPropertyChanged(nameof(CanUndoCurrentSource));
            OnPropertyChanged(nameof(IsSelectedRecordReadOnly));
        }

        void RefreshMonsterLocalizationDiagnostics(MonsterLocalizationSourceState state)
        {
            PendingDiffRows.Clear();

            if (ValidateMonsterLocalizationRecords(state, out List<string> issues))
            {
                HasValidationErrors = false;
                ValidationSummary = "Validation clean. Supported FFX control tags and charset are writable here; unchanged unsupported raw-control bytes can stay preserved from the original file, and offsets rebuild automatically.";
            }
            else
            {
                HasValidationErrors = true;
                ValidationSummary = issues.FirstOrDefault() ?? "Monster localization validation failed.";
            }

            for (int i = 0; i < state.Records.Count; i++)
            {
                StringExplorerRecord record = state.Records[i];
                MonX_File.Entry baseline = state.File.Entries[i];
                List<string> changedFields = [];

                if (!string.Equals(NormalizeText(record.PrimaryText), NormalizeText(baseline.Name), StringComparison.Ordinal))
                    changedFields.Add("Name");

                if (!string.Equals(NormalizeText(record.SecondaryText), NormalizeText(baseline.Sensor), StringComparison.Ordinal))
                    changedFields.Add("Sensor");

                if (!string.Equals(NormalizeText(record.TertiaryText), NormalizeText(baseline.Scan), StringComparison.Ordinal))
                    changedFields.Add("Scan");

                if (changedFields.Count == 0)
                    continue;

                PendingDiffRows.Add(new StringExplorerDiffRow
                {
                    IndexLabel = record.IndexLabel,
                    Title = record.Title,
                    FieldsSummary = string.Join(", ", changedFields)
                });
            }

            PendingDiffSummary = PendingDiffRows.Count == 0
                ? "No pending monster localization changes."
                : $"{PendingDiffRows.Count} monster localization entries changed. Diff is limited to Name / Sensor / Scan.";

            OnPropertyChanged(nameof(HasPendingDiffs));
            OnPropertyChanged(nameof(CanSaveCurrentSource));
            OnPropertyChanged(nameof(CanDiscardCurrentSource));
            OnPropertyChanged(nameof(CanUndoCurrentSource));
            OnPropertyChanged(nameof(IsSelectedRecordReadOnly));
        }

        void ValidateTextField(string indexLabel, string fieldLabel, string? value, byte[]? originalBytes, Dictionary<byte, char> decoder, List<string> issues)
        {
            try
            {
                _ = TextBinary_Util.ResolveTextBytes(value, originalBytes, decoder);
            }
            catch (Exception ex)
            {
                issues.Add($"{indexLabel} {fieldLabel}: {ex.Message}");
            }
        }

        void EnsureRoundTripField(string fieldLabel, string expected, string actual)
        {
            if (!string.Equals(NormalizeText(expected), NormalizeText(actual), StringComparison.Ordinal))
                throw new InvalidDataException($"{fieldLabel} failed rebuild round-trip verification.");
        }

        void SetActiveEditSession(ByteSnapshotEditorSession? session)
        {
            if (subscribedEditSession != null)
                subscribedEditSession.PropertyChanged -= EditSession_PropertyChanged;

            subscribedEditSession = session;
            EditSession = session;

            if (subscribedEditSession != null)
                subscribedEditSession.PropertyChanged += EditSession_PropertyChanged;

            OnPropertyChanged(nameof(CanSaveCurrentSource));
            OnPropertyChanged(nameof(CanDiscardCurrentSource));
            OnPropertyChanged(nameof(CanUndoCurrentSource));
        }

        void EditSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ByteSnapshotEditorSession.HasPendingChanges)
                or nameof(ByteSnapshotEditorSession.CanUndo)
                or nameof(ByteSnapshotEditorSession.SessionSummary))
            {
                OnPropertyChanged(nameof(CanSaveCurrentSource));
                OnPropertyChanged(nameof(CanDiscardCurrentSource));
                OnPropertyChanged(nameof(CanUndoCurrentSource));
                RefreshCurrentDiagnostics();
            }
        }

        void ApplyFilter()
        {
            if (SelectedSource == null)
                return;

            string normalizedFilter = FilterText.Trim();
            StringExplorerRecord? previousSelection = SelectedRecord;

            DisplayedRecords.Clear();
            foreach (StringExplorerRecord row in loadedRecords)
            {
                if (normalizedFilter.Length == 0 || row.SearchBlob.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
                    DisplayedRecords.Add(row);
            }

            if (previousSelection != null && DisplayedRecords.Contains(previousSelection))
            {
                SelectedRecord = previousSelection;
            }
            else
            {
                SelectedRecord = DisplayedRecords.FirstOrDefault();
            }
        }

        static string BuildNameDescriptionProofSummary(NameDescriptionTextTable_Entry entry)
        {
            return
                $"Keys:{Environment.NewLine}" +
                $"Name {entry.NameKey:X4}h{Environment.NewLine}" +
                $"Simplified Name {entry.SimplifiedNameKey:X4}h{Environment.NewLine}" +
                $"Description {entry.DescriptionKey:X4}h{Environment.NewLine}" +
                $"Simplified Description {entry.SimplifiedDescriptionKey:X4}h{Environment.NewLine}{Environment.NewLine}" +
                "Original Offsets:" + Environment.NewLine +
                $"Name {entry.NameOffset:X4}h · Simplified Name {entry.SimplifiedNameOffset:X4}h{Environment.NewLine}" +
                $"Description {entry.DescriptionOffset:X4}h · Simplified Description {entry.SimplifiedDescriptionOffset:X4}h{Environment.NewLine}{Environment.NewLine}" +
                "Write Path: offsets and shared-string links rebuild automatically on save. Manual offset editing stays locked.";
        }

        static string NormalizeText(string? value)
        {
            return TextBinary_Util.NormalizeText(value);
        }

        static string TrimForSummary(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "(Empty)";

            return value.Length > 84 ? value[..84] + "..." : value;
        }

        internal sealed class StringSourceRow
        {
            public required string Id { get; init; }
            public required string DisplayName { get; init; }
            public required string RelativePath { get; init; }
            public required string AbsolutePath { get; init; }
            public required string ParserModeLabel { get; init; }
            public required string Summary { get; init; }
            public required Dictionary<byte, char> Decoder { get; init; }
            public required StringSourceKind SourceKind { get; init; }
            public required string FamilyLabel { get; init; }
            public required string TrustLabel { get; init; }
            public required IBrush TrustBrush { get; init; }
            public required string WritePolicyLabel { get; init; }
            public required IBrush WritePolicyBrush { get; init; }
            public required string ProofSummary { get; init; }
            public required string RiskSummary { get; init; }
            public required string DecisionSummary { get; init; }
        }

        sealed class SourcePresentationProfile
        {
            public required string FamilyLabel { get; init; }
            public required string TrustLabel { get; init; }
            public required IBrush TrustBrush { get; init; }
            public required string WritePolicyLabel { get; init; }
            public required IBrush WritePolicyBrush { get; init; }
            public required string ProofSummary { get; init; }
            public required string RiskSummary { get; init; }
            public required string DecisionSummary { get; init; }
        }

        sealed class MonsterLocalizationSourceState
        {
            public required StringSourceRow Source { get; init; }
            public required MonX_File File { get; set; }
            public required ObservableCollection<StringExplorerRecord> Records { get; init; }
            public ByteSnapshotEditorSession Session { get; set; } = null!;
            public bool SuppressRecordTracking { get; set; }
        }

        sealed class NameDescriptionSourceState
        {
            public required StringSourceRow Source { get; init; }
            public required Dictionary<byte, char> Decoder { get; init; }
            public required NameDescriptionTextTable_File File { get; set; }
            public required ObservableCollection<StringExplorerRecord> Records { get; init; }
            public ByteSnapshotEditorSession Session { get; set; } = null!;
            public bool SuppressRecordTracking { get; set; }
        }

        sealed class MonsterLocalizationSnapshotRow
        {
            public required int SourceEntryIndex { get; init; }
            public required string Name { get; init; }
            public required string Sensor { get; init; }
            public required string Scan { get; init; }
        }

        sealed class NameDescriptionSnapshotRow
        {
            public required int SourceEntryIndex { get; init; }
            public required string Name { get; init; }
            public required string SimplifiedName { get; init; }
            public required string Description { get; init; }
            public required string SimplifiedDescription { get; init; }
        }
    }
}
