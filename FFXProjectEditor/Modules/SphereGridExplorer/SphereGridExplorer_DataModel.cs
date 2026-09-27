using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Controls.SphereGrid;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.SphereGrid;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;
using FFXProjectEditor.Modules.Common;
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

namespace FFXProjectEditor.Modules.SphereGridExplorer
{
    internal partial class SphereGridExplorer_DataModel : ObservableObject
    {
        readonly List<SphereGridExplorerRecord> loadedRecords = new();
        readonly List<SphereGridSphereTypeEditorRow> sphereTypeRows = new();
        readonly List<SphereGridNodeTypeEditorRow> nodeTypeRows = new();
        readonly Dictionary<SphereGridSourceKind, List<SphereGridLayoutNodeEditorRow>> layoutNodeRowsBySource = new();
        readonly Dictionary<int, SphereGridSphereTypeEntry> sphereTypeEntriesByIndex = new();
        readonly Dictionary<int, SphereGridNodeTypeEntry> nodeTypeEntriesByIndex = new();
        readonly Dictionary<int, string> commandNames = new();

        /// <summary>command.bin category nibble OR-ed into a LearnedMove id (engine grant gate requires it).</summary>
        const int CommandCategoryNibble = 0x3000;
        readonly Dictionary<int, SphereGridSphereTypeState> sphereTypeBaseline = new();
        readonly Dictionary<int, SphereGridNodeTypeState> nodeTypeBaseline = new();
        readonly Dictionary<SphereGridSourceKind, Dictionary<int, SphereGridLayoutNodeState>> layoutBaselineBySource = new();
        readonly Dictionary<int, SphereGridNodeVisualInfo> previewNodeVisuals = new();

        SphereGridSphereTypeTable? sphereTypes;
        SphereGridNodeTypeTable? nodeTypes;
        SphereGridLayoutFile? originalGrid;
        SphereGridLayoutFile? standardGrid;
        SphereGridLayoutFile? expertGrid;
        string? pendingPreferredRecordId;
        bool isApplyingEditorState;
        bool suppressOptionPropagation;

        public ObservableCollection<SphereGridSourceRow> LoadedSources { get; } = new();
        public ObservableCollection<SphereGridExplorerRecord> DisplayedRecords { get; } = new();
        public ObservableCollection<SphereGridNamedOption> ActionOptions { get; } = new();
        public ObservableCollection<SphereGridNamedOption> RangeOptions { get; } = new();
        public ObservableCollection<SphereGridNamedOption> LearnedMoveOptions { get; } = new();
        public ObservableCollection<SphereGridNamedOption> SphereRequirementOptions { get; } = new();
        public ObservableCollection<SphereGridNamedOption> NodeTypeOptions { get; } = new();
        public ObservableCollection<SphereGridNamedOption> PreviewVisualModeOptions { get; } = new();
        public ObservableCollection<SphereGridPreviewPresetOption> PreviewPresetOptions { get; } = new();
        public ObservableCollection<SphereGridDiffRow> PendingDiffRows { get; } = new();

        [ObservableProperty] private ByteSnapshotEditorSession editSession = null!;
        [ObservableProperty] private SphereGridSourceRow? selectedSource;
        [ObservableProperty] private SphereGridExplorerRecord? selectedRecord;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Load a project root to inspect sphere-grid data.";
        [ObservableProperty] private string selectedSourceSummary = "Sphere types, node types, and grid layouts will land here once a workspace is loaded.";
        [ObservableProperty] private string selectedRecordSummary = "Select a sphere-grid source and then an entry to inspect the decoded structure.";
        [ObservableProperty] private SphereGridLayoutFile? selectedLayoutPreview;
        [ObservableProperty] private bool isLayoutPreviewVisible;
        [ObservableProperty] private int selectedLayoutHighlightedNodeIndex = -1;
        [ObservableProperty] private string selectedLayoutPreviewSummary = "Layout preview appears when you pick Original, Standard, or Expert.";
        [ObservableProperty] private SphereGridPreviewBackgroundKind selectedLayoutBackgroundKind = SphereGridPreviewBackgroundKind.None;
        [ObservableProperty] private SphereGridPreviewVisualMode selectedPreviewVisualMode = SphereGridPreviewVisualMode.HybridGame;
        [ObservableProperty] private SphereGridNamedOption? selectedPreviewVisualModeOption;
        [ObservableProperty] private SphereGridPreviewPresetOption? selectedPreviewPresetOption;
        [ObservableProperty] private double previewZoomFactor = 0.92;
        [ObservableProperty] private SphereGridSphereTypeEditorRow? selectedSphereTypeRow;
        [ObservableProperty] private SphereGridNodeTypeEditorRow? selectedNodeTypeRow;
        [ObservableProperty] private SphereGridLayoutNodeEditorRow? selectedLayoutNodeRow;
        [ObservableProperty] private SphereGridNamedOption? selectedActionOption;
        [ObservableProperty] private SphereGridNamedOption? selectedRangeOption;
        [ObservableProperty] private SphereGridNamedOption? selectedLearnedMoveOption;
        [ObservableProperty] private SphereGridNamedOption? selectedSphereRequirementOption;
        [ObservableProperty] private SphereGridNamedOption? selectedLayoutNodeTypeOption;
        [ObservableProperty] private string selectedEditorSummary = "Select a sphere-grid record to edit proven fields.";
        [ObservableProperty] private string pendingDiffSummary = "No pending sphere-grid edits.";
        [ObservableProperty] private string selectedPreviewPresetSummary = "Pick a visual preset to quickly switch between readability and nostalgia.";

        public bool IsSphereTypeEditorVisible => SelectedSphereTypeRow != null;
        public bool IsNodeTypeEditorVisible => SelectedNodeTypeRow != null;
        public bool IsLayoutNodeEditorVisible => SelectedLayoutNodeRow != null;
        public bool HasPendingDiffRows => PendingDiffRows.Count > 0;
        public bool HasLoadedSphereGrid => LoadedSources.Count > 0;
        public string SelectedNodeEffectAppearanceSummary => SelectedNodeTypeRow == null
            ? string.Empty
            : $"{SelectedNodeTypeRow.NodeEffectBitfield:X4}h · {SelectedNodeTypeRow.AppearanceType:X4}h";
        public IReadOnlyDictionary<int, SphereGridNodeVisualInfo> PreviewNodeVisuals => previewNodeVisuals;
        public double PreviewCanvasSize => 520 * PreviewZoomFactor;
        public string PreviewZoomSummary => $"{PreviewZoomFactor:P0}".Replace(" ", string.Empty);

        public SphereGridExplorer_DataModel()
        {
            SeedStaticOptions();
            ReloadSources();
            EditSession = new ByteSnapshotEditorSession(
                BuildEditorStateSnapshot,
                RestoreEditorStateSnapshot,
                PersistEditorState,
                "sphere-grid",
                BuildEditorStateSnapshot());
            UpdatePendingDiff();
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter(SelectedRecord?.RecordId);
        }

        partial void OnSelectedSourceChanged(SphereGridSourceRow? value)
        {
            if (value == null)
            {
                DisplayedRecords.Clear();
                SelectedRecord = null;
                SelectedSourceSummary = "Select a sphere-grid source to inspect it.";
                SelectedLayoutPreview = null;
                IsLayoutPreviewVisible = false;
                SelectedLayoutBackgroundKind = SphereGridPreviewBackgroundKind.None;
                SelectedLayoutPreviewSummary = "Layout preview appears when you pick Original, Standard, or Expert.";
                SelectedLayoutHighlightedNodeIndex = -1;
                return;
            }

            UpdateLayoutPreview(value);
            RebuildSelectedSourceRecords(pendingPreferredRecordId);
            pendingPreferredRecordId = null;
        }

        partial void OnSelectedRecordChanged(SphereGridExplorerRecord? value)
        {
            SelectedRecordSummary = value == null
                ? "Select a sphere-grid entry to inspect it."
                : value.ContextSummary;

            SelectedLayoutHighlightedNodeIndex = value?.PreviewNodeIndex ?? -1;
            BindEditorSelectionFromRecord(value);
        }

        partial void OnSelectedSphereTypeRowChanged(SphereGridSphereTypeEditorRow? value)
        {
            OnPropertyChanged(nameof(IsSphereTypeEditorVisible));
        }

        partial void OnSelectedNodeTypeRowChanged(SphereGridNodeTypeEditorRow? value)
        {
            OnPropertyChanged(nameof(IsNodeTypeEditorVisible));
            OnPropertyChanged(nameof(SelectedNodeEffectAppearanceSummary));
        }

        [ObservableProperty] private AtlasEvidenceInfo? selectedNodeEvidence;

        partial void OnSelectedLayoutNodeRowChanged(SphereGridLayoutNodeEditorRow? value)
        {
            OnPropertyChanged(nameof(IsLayoutNodeEditorVisible));
            SelectedNodeEvidence = BuildSelectedNodeEvidence(value);
        }

        // Read-only Spira Data Atlas evidence for the selected layout node, when a stable (layout, nodeIndex)
        // crosslink exists. Returns null otherwise so the badge strip hides itself — no guessing, no false badges.
        AtlasEvidenceInfo? BuildSelectedNodeEvidence(SphereGridLayoutNodeEditorRow? node)
        {
            if (node == null || SelectedSource == null)
                return null;

            string? layout = SelectedSource.SourceKind switch
            {
                SphereGridSourceKind.OriginalGrid => "OSG",
                SphereGridSourceKind.StandardGrid => "SSG",
                SphereGridSourceKind.ExpertGrid => "ESG",
                _ => null,
            };
            if (layout == null)
                return null;

            return SpiraDataAtlasCatalog.TryGetSphereGridNode(layout, node.Index.ToString(), out SpiraDataAtlasDetailEntry? detail) && detail != null
                ? AtlasEvidenceInfo.ForDetail(detail)
                : null;
        }

        partial void OnSelectedPreviewVisualModeChanged(SphereGridPreviewVisualMode value)
        {
            selectedPreviewVisualModeOption = PreviewVisualModeOptions.FirstOrDefault(option => option.Value == (int)value);
            OnPropertyChanged(nameof(SelectedPreviewVisualModeOption));
            UpdateLayoutPreviewSummary();
            UpdatePreviewPresetSummary();
        }

        partial void OnSelectedPreviewVisualModeOptionChanged(SphereGridNamedOption? value)
        {
            if (value == null)
                return;

            SphereGridPreviewVisualMode mode = (SphereGridPreviewVisualMode)value.Value;
            if (SelectedPreviewVisualMode != mode)
                SelectedPreviewVisualMode = mode;
        }

        partial void OnPreviewZoomFactorChanged(double value)
        {
            OnPropertyChanged(nameof(PreviewCanvasSize));
            OnPropertyChanged(nameof(PreviewZoomSummary));
            UpdatePreviewPresetSummary();
        }

        partial void OnSelectedPreviewPresetOptionChanged(SphereGridPreviewPresetOption? value)
        {
            if (value == null)
            {
                UpdatePreviewPresetSummary();
                return;
            }

            if (SelectedPreviewVisualMode != value.VisualMode)
                SelectedPreviewVisualMode = value.VisualMode;

            SetPreviewZoom(value.ZoomFactor);
            UpdatePreviewPresetSummary();
        }

        partial void OnSelectedActionOptionChanged(SphereGridNamedOption? value)
        {
            if (suppressOptionPropagation || value == null || SelectedSphereTypeRow == null)
                return;

            if (SelectedSphereTypeRow.Behavior != value.Value)
                SelectedSphereTypeRow.Behavior = unchecked((ushort)value.Value);
        }

        partial void OnSelectedRangeOptionChanged(SphereGridNamedOption? value)
        {
            if (suppressOptionPropagation || value == null || SelectedSphereTypeRow == null)
                return;

            if (SelectedSphereTypeRow.Range != value.Value)
                SelectedSphereTypeRow.Range = unchecked((byte)value.Value);
        }

        partial void OnSelectedLearnedMoveOptionChanged(SphereGridNamedOption? value)
        {
            if (suppressOptionPropagation || value == null || SelectedNodeTypeRow == null)
                return;

            if (SelectedNodeTypeRow.LearnedMove != value.Value)
                SelectedNodeTypeRow.LearnedMove = unchecked((ushort)value.Value);
        }

        partial void OnSelectedSphereRequirementOptionChanged(SphereGridNamedOption? value)
        {
            if (suppressOptionPropagation || value == null || SelectedNodeTypeRow == null)
                return;

            if (!SphereGridNodeSphereRequirement.TryGetApplyResult(
                    (SphereGridNodeSphereRequirementKind)value.Value,
                    out SphereGridNodeSphereRequirementApplyResult apply))
            {
                return;
            }

            SelectedNodeTypeRow.NodeEffectBitfield = apply.NodeEffectBitfield;
            SelectedNodeTypeRow.AppearanceType = apply.AppearanceType;
            if (apply.ClearLearnedMove)
                SelectedNodeTypeRow.LearnedMove = 0;
            if (apply.ClearIncreaseAmount)
                SelectedNodeTypeRow.IncreaseAmount = 0;

            OnPropertyChanged(nameof(SelectedNodeEffectAppearanceSummary));
        }

        partial void OnSelectedLayoutNodeTypeOptionChanged(SphereGridNamedOption? value)
        {
            if (suppressOptionPropagation || value == null || SelectedLayoutNodeRow == null)
                return;

            if (SelectedLayoutNodeRow.ContentIndex != value.Value)
                SelectedLayoutNodeRow.ContentIndex = value.Value;
        }

        public void RefreshFromDisk()
        {
            ReloadSources(SelectedSource?.Id, SelectedRecord?.RecordId);
        }

        public void Save() => EditSession.Save();
        public void Undo() => EditSession.Undo();
        public void Discard() => EditSession.Discard();

        public void ZoomInPreview() => SetPreviewZoom(PreviewZoomFactor + 0.2);
        public void ZoomOutPreview() => SetPreviewZoom(PreviewZoomFactor - 0.2);
        public void ResetPreviewZoom() => SetPreviewZoom(SelectedPreviewPresetOption?.ZoomFactor ?? 1.0);

        public void SelectPreviewNode(int nodeIndex)
        {
            if (SelectedSource == null || !IsLayoutSource(SelectedSource.SourceKind))
                return;

            SphereGridExplorerRecord? record = DisplayedRecords.FirstOrDefault(row => row.RecordIndex == nodeIndex);
            if (record == null)
                return;

            SelectedRecord = record;
        }

        void SeedStaticOptions()
        {
            ActionOptions.Clear();
            // 0x0000 is the third legitimate sphere action (~40% of spheres in the corpus have no on-touch
            // activator/mutator effect), not an unknown value. Surface it as "None / Passive" instead of a guessed
            // Unknown. Honest: this is a partial display label grounded in the offline corpus (Jarvis-AURON deep
            // scout), not a runtime-proved effect name.
            ActionOptions.Add(new SphereGridNamedOption { Value = 0x0000, Label = "0000h · None / Passive" });
            ActionOptions.Add(new SphereGridNamedOption { Value = 0x0001, Label = "0001h · Activator" });
            ActionOptions.Add(new SphereGridNamedOption { Value = 0x0002, Label = "0002h · Mutator" });

            RangeOptions.Clear();
            // 0x00 range = no reach band (None), the third legitimate value, not unknown (same corpus evidence).
            RangeOptions.Add(new SphereGridNamedOption { Value = 0x00, Label = "00h · None" });
            RangeOptions.Add(new SphereGridNamedOption { Value = 0x01, Label = "01h · Short Range" });
            RangeOptions.Add(new SphereGridNamedOption { Value = 0x20, Label = "20h · Long Range" });

            SphereRequirementOptions.Clear();
            foreach ((int optionValue, string label) in SphereGridNodeSphereRequirement.GetDropdownOptions())
                SphereRequirementOptions.Add(new SphereGridNamedOption { Value = optionValue, Label = label });

            PreviewVisualModeOptions.Clear();
            PreviewVisualModeOptions.Add(new SphereGridNamedOption { Value = (int)SphereGridPreviewVisualMode.HybridGame, Label = "Almost In-Game" });
            PreviewVisualModeOptions.Add(new SphereGridNamedOption { Value = (int)SphereGridPreviewVisualMode.DebugColors, Label = "Colors Only" });
            PreviewVisualModeOptions.Add(new SphereGridNamedOption { Value = (int)SphereGridPreviewVisualMode.LabelOverlay, Label = "Node Labels" });
            PreviewVisualModeOptions.Add(new SphereGridNamedOption { Value = (int)SphereGridPreviewVisualMode.BackgroundOnly, Label = "Background Only" });
            SelectedPreviewVisualModeOption = PreviewVisualModeOptions.FirstOrDefault();

            PreviewPresetOptions.Clear();
            PreviewPresetOptions.Add(new SphereGridPreviewPresetOption
            {
                Label = "Near In-Game",
                Summary = "Pushes the board into a tighter in-game camera, leans harder on native art, and centers the selected zone like the real Sphere Grid.",
                VisualMode = SphereGridPreviewVisualMode.HybridGame,
                ZoomFactor = 0.88
            });
            PreviewPresetOptions.Add(new SphereGridPreviewPresetOption
            {
                Label = "Authoring Focus",
                Summary = "Pushes labels and clarity first for payload editing without drowning in the backdrop.",
                VisualMode = SphereGridPreviewVisualMode.LabelOverlay,
                ZoomFactor = 1.08
            });
            PreviewPresetOptions.Add(new SphereGridPreviewPresetOption
            {
                Label = "Dense Grid",
                Summary = "Separates crowded clusters with stronger visual contrast for debugging packed regions.",
                VisualMode = SphereGridPreviewVisualMode.DebugColors,
                ZoomFactor = 1.18
            });
            PreviewPresetOptions.Add(new SphereGridPreviewPresetOption
            {
                Label = "Backdrop Study",
                Summary = "Lets the original sphere-grid art breathe so you can compare layout against the native board.",
                VisualMode = SphereGridPreviewVisualMode.BackgroundOnly,
                ZoomFactor = 0.84
            });
            PreviewPresetOptions.Add(new SphereGridPreviewPresetOption
            {
                Label = "Close Inspection",
                Summary = "A tighter zoom for local surgery when a cluster is packed to hell.",
                VisualMode = SphereGridPreviewVisualMode.HybridGame,
                ZoomFactor = 1.32
            });
            SelectedPreviewPresetOption = PreviewPresetOptions.FirstOrDefault();
        }

        void ReloadSources(string? preferredSourceId = null, string? preferredRecordId = null)
        {
            UnsubscribeEditorGraph();
            LoadedSources.Clear();
            DisplayedRecords.Clear();
            loadedRecords.Clear();
            sphereTypeRows.Clear();
            nodeTypeRows.Clear();
            layoutNodeRowsBySource.Clear();
            sphereTypeEntriesByIndex.Clear();
            nodeTypeEntriesByIndex.Clear();
            previewNodeVisuals.Clear();
            sphereTypeBaseline.Clear();
            nodeTypeBaseline.Clear();
            layoutBaselineBySource.Clear();
            commandNames.Clear();
            sphereTypes = null;
            nodeTypes = null;
            originalGrid = null;
            standardGrid = null;
            expertGrid = null;
            SelectedRecord = null;
            SelectedSphereTypeRow = null;
            SelectedNodeTypeRow = null;
            SelectedLayoutNodeRow = null;
            SelectedLayoutPreview = null;
            IsLayoutPreviewVisible = false;
            SelectedLayoutBackgroundKind = SphereGridPreviewBackgroundKind.None;
            SelectedLayoutPreviewSummary = "Layout preview appears when you pick Original, Standard, or Expert.";
            SelectedLayoutHighlightedNodeIndex = -1;
            pendingPreferredRecordId = preferredRecordId;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                UpdatePendingDiff();
                return;
            }

            LoadCommandNames();

            List<string> loadedDomainNames = new();
            TryLoadSphereTypes(loadedDomainNames);
            TryLoadNodeTypes(loadedDomainNames);
            TryLoadLayout(loadedDomainNames, "original_grid", "Original Sphere Grid", "READ_SPHERE_GRID_LAYOUT", Path.Combine(Project_Service.Instance.Path_Abmap, "dat01.dat"), Path.Combine(Project_Service.Instance.Path_Abmap, "dat09.dat"), SphereGridSourceKind.OriginalGrid, "Original grid layout with clusters, nodes, links, and editable node contents.");
            TryLoadLayout(loadedDomainNames, "standard_grid", "Standard Sphere Grid", "READ_SPHERE_GRID_LAYOUT", Path.Combine(Project_Service.Instance.Path_Abmap, "dat02.dat"), Path.Combine(Project_Service.Instance.Path_Abmap, "dat10.dat"), SphereGridSourceKind.StandardGrid, "Standard grid layout with clusters, nodes, links, and editable node contents.");
            TryLoadLayout(loadedDomainNames, "expert_grid", "Expert Sphere Grid", "READ_SPHERE_GRID_LAYOUT", Path.Combine(Project_Service.Instance.Path_Abmap, "dat03.dat"), Path.Combine(Project_Service.Instance.Path_Abmap, "dat11.dat"), SphereGridSourceKind.ExpertGrid, "Expert grid layout with clusters, nodes, links, and editable node contents.");

            if (LoadedSources.Count == 0)
            {
                LoadSummary = "No sphere-grid files were found in the loaded workspace.";
                UpdatePendingDiff();
                return;
            }

            BuildPreviewVisuals();
            RefreshDynamicOptions();
            CaptureBaselinesFromEditors();
            SubscribeEditorGraph();

            LoadSummary = $"Loaded {LoadedSources.Count} sphere-grid domains: {string.Join(", ", loadedDomainNames)}.";

            SphereGridSourceRow? preferredSource = LoadedSources.FirstOrDefault(row => row.Id == preferredSourceId);
            SelectedSource = preferredSource ?? LoadedSources.FirstOrDefault();

            if (EditSession != null)
                EditSession.ReplaceBaseline(BuildEditorStateSnapshot(), "No pending sphere-grid changes.");

            UpdatePendingDiff();
            OnPropertyChanged(nameof(HasLoadedSphereGrid));
        }

        void TryLoadSphereTypes(List<string> loadedDomainNames)
        {
            string jpPath = Project_Service.Instance.Path_KernelSphere;
            string usPath = Project_Service.Instance.Path_KernelSphereUs;
            if (!File.Exists(jpPath) && !File.Exists(usPath))
                return;

            sphereTypes = SphereGrid_File.ReadSphereTypes(jpPath, usPath);
            foreach (SphereGridSphereTypeEntry entry in sphereTypes.Entries)
            {
                sphereTypeEntriesByIndex[entry.Index] = entry;
                sphereTypeRows.Add(new SphereGridSphereTypeEditorRow
                {
                    Index = entry.Index,
                    JpDescription = entry.Description.JpText,
                    UsDescription = entry.Description.UsText,
                    JpSimplifiedDescription = entry.SimplifiedDescription.JpText,
                    UsSimplifiedDescription = entry.SimplifiedDescription.UsText,
                    Behavior = entry.Behavior,
                    Activates = entry.Activates,
                    Range = entry.Range,
                    SpecialRole = entry.SpecialRole,
                    Reserved0x0E = entry.Reserved0x0E
                });
            }

            LoadedSources.Add(new SphereGridSourceRow
            {
                Id = "sphere_types",
                DisplayName = "Sphere Types",
                RelativePath = BuildOverlayPathLabel(jpPath, usPath),
                ParserModeLabel = "READ_SPHERE_GRID_NODE_TYPES",
                Summary = "Localized sphere descriptions plus proven action/range data from sphere.bin.",
                SourceKind = SphereGridSourceKind.SphereTypes
            });
            loadedDomainNames.Add("sphere types");
        }

        void TryLoadNodeTypes(List<string> loadedDomainNames)
        {
            string jpPath = Project_Service.Instance.Path_KernelPanel;
            string usPath = Project_Service.Instance.Path_KernelPanelUs;
            if (!File.Exists(jpPath) && !File.Exists(usPath))
                return;

            nodeTypes = SphereGrid_File.ReadNodeTypes(jpPath, usPath);
            foreach (SphereGridNodeTypeEntry entry in nodeTypes.Entries)
            {
                nodeTypeEntriesByIndex[entry.Index] = entry;
                nodeTypeRows.Add(new SphereGridNodeTypeEditorRow
                {
                    Index = entry.Index,
                    JpName = entry.Name.JpText,
                    UsName = entry.Name.UsText,
                    JpSimplifiedName = entry.SimplifiedName.JpText,
                    UsSimplifiedName = entry.SimplifiedName.UsText,
                    JpDescription = entry.Description.JpText,
                    UsDescription = entry.Description.UsText,
                    JpSimplifiedDescription = entry.SimplifiedDescription.JpText,
                    UsSimplifiedDescription = entry.SimplifiedDescription.UsText,
                    NodeEffectBitfield = entry.NodeEffectBitfield,
                    LearnedMove = entry.LearnedMove,
                    IncreaseAmount = entry.IncreaseAmount,
                    AppearanceType = entry.AppearanceType
                });
            }

            LoadedSources.Add(new SphereGridSourceRow
            {
                Id = "node_types",
                DisplayName = "Node Types",
                RelativePath = BuildOverlayPathLabel(jpPath, usPath),
                ParserModeLabel = "READ_SPHERE_GRID_NODE_TYPES",
                Summary = "Localized node payloads with proven learned-move and increase editing from panel.bin.",
                SourceKind = SphereGridSourceKind.NodeTypes
            });
            loadedDomainNames.Add("node types");
        }

        void TryLoadLayout(List<string> loadedDomainNames, string id, string displayName, string parserModeLabel, string layoutPath, string contentPath, SphereGridSourceKind sourceKind, string summary)
        {
            if (!File.Exists(layoutPath) || !File.Exists(contentPath))
                return;

            SphereGridLayoutFile layout = SphereGrid_File.ReadLayout(layoutPath, contentPath, displayName);
            switch (sourceKind)
            {
                case SphereGridSourceKind.OriginalGrid:
                    originalGrid = layout;
                    break;
                case SphereGridSourceKind.StandardGrid:
                    standardGrid = layout;
                    break;
                case SphereGridSourceKind.ExpertGrid:
                    expertGrid = layout;
                    break;
            }

            List<SphereGridLayoutNodeEditorRow> rows = new();
            foreach (SphereGridNodeEntry node in layout.Nodes)
            {
                rows.Add(new SphereGridLayoutNodeEditorRow
                {
                    Index = node.Index,
                    PosX = node.PosX,
                    PosY = node.PosY,
                    Cluster = node.Cluster,
                    ConnectedNodeCount = node.ConnectedNodeIndices.Count,
                    RedundantContent = node.RedundantContent,
                    Unknown6 = node.Unknown6,
                    ContentIndex = node.ContentIndex
                });
            }

            layoutNodeRowsBySource[sourceKind] = rows;
            LoadedSources.Add(new SphereGridSourceRow
            {
                Id = id,
                DisplayName = displayName,
                RelativePath = BuildRelativePairLabel(layoutPath, contentPath),
                ParserModeLabel = parserModeLabel,
                Summary = summary,
                SourceKind = sourceKind
            });
            loadedDomainNames.Add(displayName);
        }

        void LoadCommandNames()
        {
            string path = Project_Service.Instance.Path_KernelCommandUs;
            if (!File.Exists(path))
                return;

            List<Ability_Command> commands = Ability_Command.ReadList(File.ReadAllBytes(path), hasExtraInfo: true);
            for (int i = 0; i < commands.Count; i++)
            {
                string name = FfxEncoding.DecodeScript(commands[i].NameScriptBytes).GetString(FfxEncoding.UsDecoder);
                commandNames[i] = string.IsNullOrWhiteSpace(name) ? "-" : name;
            }
        }

        void RebuildSelectedSourceRecords(string? preferredRecordId = null)
        {
            if (SelectedSource == null)
                return;

            loadedRecords.Clear();
            switch (SelectedSource.SourceKind)
            {
                case SphereGridSourceKind.SphereTypes:
                    BuildSphereTypeRecords();
                    break;
                case SphereGridSourceKind.NodeTypes:
                    BuildNodeTypeRecords();
                    break;
                case SphereGridSourceKind.OriginalGrid:
                    BuildLayoutRecords(originalGrid, SphereGridSourceKind.OriginalGrid);
                    break;
                case SphereGridSourceKind.StandardGrid:
                    BuildLayoutRecords(standardGrid, SphereGridSourceKind.StandardGrid);
                    break;
                case SphereGridSourceKind.ExpertGrid:
                    BuildLayoutRecords(expertGrid, SphereGridSourceKind.ExpertGrid);
                    break;
            }

            ApplyFilter(preferredRecordId);
        }

        void BuildSphereTypeRecords()
        {
            if (sphereTypes == null)
                return;

            foreach (SphereGridSphereTypeEditorRow row in sphereTypeRows)
            {
                SphereGridSphereTypeEntry original = sphereTypeEntriesByIndex[row.Index];
                string preferredDescription = EmptyToPlaceholder(PreferredText(row.UsDescription, row.JpDescription));
                string simplified = TextEquals(PreferredText(row.UsDescription, row.JpDescription), PreferredText(row.UsSimplifiedDescription, row.JpSimplifiedDescription))
                    ? "(Shared with regular description)"
                    : EmptyToPlaceholder(PreferredText(row.UsSimplifiedDescription, row.JpSimplifiedDescription));

                loadedRecords.Add(new SphereGridExplorerRecord
                {
                    RecordId = $"sphere:{row.Index}",
                    RecordIndex = row.Index,
                    IndexLabel = $"Type {row.Index:X2}h",
                    Title = BuildSphereTypeTitle(row),
                    Summary = preferredDescription,
                    PrimaryLabel = "Description",
                    PrimaryText = preferredDescription,
                    SecondaryLabel = "Simplified",
                    SecondaryText = simplified,
                    TertiaryLabel = "Behavior",
                    TertiaryText = BuildSphereTypeBehavior(row, original),
                    QuaternaryLabel = "Headers / Locale",
                    QuaternaryText = $"{original.Description.HeaderSummary}{Environment.NewLine}{Environment.NewLine}Simplified:{Environment.NewLine}{original.SimplifiedDescription.HeaderSummary}{Environment.NewLine}{Environment.NewLine}{original.Description.LocaleSummary}",
                    ContextSummary = $"{BuildSphereTypeTitle(row)} · sphere.bin entry {row.Index:X2}h",
                    SearchBlob = $"{row.Index:X2} {row.UsDescription} {row.JpDescription} {row.UsSimplifiedDescription} {row.JpSimplifiedDescription}"
                });
            }

            SelectedSourceSummary = $"READ_SPHERE_GRID_NODE_TYPES · {BuildOverlayPathLabel(sphereTypes.JpPath, sphereTypes.UsPath)} · {sphereTypeRows.Count} sphere types decoded and ready for proven edits.";
        }

        void BuildNodeTypeRecords()
        {
            if (nodeTypes == null)
                return;

            foreach (SphereGridNodeTypeEditorRow row in nodeTypeRows)
            {
                SphereGridNodeTypeEntry original = nodeTypeEntriesByIndex[row.Index];
                string preferredName = PreferredName(row);
                string preferredDescription = EmptyToPlaceholder(PreferredText(row.UsDescription, row.JpDescription));
                string simplifiedName = TextEquals(PreferredText(row.UsName, row.JpName), PreferredText(row.UsSimplifiedName, row.JpSimplifiedName))
                    ? "(Shared with regular name)"
                    : EmptyToPlaceholder(PreferredText(row.UsSimplifiedName, row.JpSimplifiedName));
                string simplifiedDescription = TextEquals(PreferredText(row.UsDescription, row.JpDescription), PreferredText(row.UsSimplifiedDescription, row.JpSimplifiedDescription))
                    ? "(Shared with regular description)"
                    : EmptyToPlaceholder(PreferredText(row.UsSimplifiedDescription, row.JpSimplifiedDescription));

                loadedRecords.Add(new SphereGridExplorerRecord
                {
                    RecordId = $"node:{row.Index}",
                    RecordIndex = row.Index,
                    IndexLabel = $"NodeType {row.Index:X2}h",
                    Title = preferredName,
                    Summary = preferredDescription,
                    PrimaryLabel = "Name / Simplified",
                    PrimaryText = $"{preferredName}{Environment.NewLine}{Environment.NewLine}Simplified: {simplifiedName}",
                    SecondaryLabel = "Description",
                    SecondaryText = $"{preferredDescription}{Environment.NewLine}{Environment.NewLine}Simplified: {simplifiedDescription}",
                    TertiaryLabel = "Payload",
                    TertiaryText = BuildNodeTypePayload(row, original),
                    QuaternaryLabel = "Headers / Locale",
                    QuaternaryText = $"Name:{Environment.NewLine}{original.Name.HeaderSummary}{Environment.NewLine}{Environment.NewLine}Description:{Environment.NewLine}{original.Description.HeaderSummary}{Environment.NewLine}{Environment.NewLine}{original.Name.LocaleSummary}",
                    ContextSummary = $"{preferredName} · panel.bin entry {row.Index:X2}h",
                    SearchBlob = $"{row.Index:X2} {preferredName} {preferredDescription} {row.JpName} {row.UsName} {row.JpDescription} {row.UsDescription}"
                });
            }

            SelectedSourceSummary = $"READ_SPHERE_GRID_NODE_TYPES · {BuildOverlayPathLabel(nodeTypes.JpPath, nodeTypes.UsPath)} · {nodeTypeRows.Count} node types decoded and ready for proven edits.";
        }

        void BuildLayoutRecords(SphereGridLayoutFile? layout, SphereGridSourceKind sourceKind)
        {
            if (layout == null || !layoutNodeRowsBySource.TryGetValue(sourceKind, out List<SphereGridLayoutNodeEditorRow>? rows))
                return;

            foreach (SphereGridLayoutNodeEditorRow row in rows)
            {
                SphereGridNodeTypeEntry? nodeType = ResolveOriginalNodeType(row.ContentIndex);
                string nodeTypeName = ResolveNodeTypeDisplayName(row.ContentIndex);
                string nodeTypeDescription = nodeType == null
                    ? "(Node type not resolved yet.)"
                    : EmptyToPlaceholder(PreferredText(nodeType.PreferredDescription, nodeType.Description.JpText));

                loadedRecords.Add(new SphereGridExplorerRecord
                {
                    RecordId = $"{sourceKind}:{row.Index}",
                    RecordIndex = row.Index,
                    IndexLabel = $"Node {row.Index:X3}h",
                    Title = $"{row.Index:D3} · {nodeTypeName}",
                    Summary = $"Cluster {row.Cluster:D2} · Pos ({row.PosX}, {row.PosY}) · {row.ConnectedNodeCount} links",
                    PrimaryLabel = "Node Type",
                    PrimaryText = $"{nodeTypeName}{Environment.NewLine}{Environment.NewLine}{nodeTypeDescription}",
                    SecondaryLabel = "Placement",
                    SecondaryText = BuildNodePlacement(layout, row),
                    TertiaryLabel = "Links",
                    TertiaryText = BuildNodeLinks(layout, row.Index),
                    QuaternaryLabel = "Raw",
                    QuaternaryText = BuildNodeRaw(row, nodeType),
                    ContextSummary = $"{layout.DisplayName} · node {row.Index:D3} · cluster {row.Cluster:D2}",
                    SearchBlob = $"{layout.DisplayName} {row.Index:D3} {row.Index:X3} {nodeTypeName} {nodeTypeDescription} {row.PosX} {row.PosY} {row.Cluster}",
                    PreviewNodeIndex = row.Index
                });
            }

            SelectedSourceSummary = $"READ_SPHERE_GRID_LAYOUT · {BuildRelativePairLabel(layout.LayoutPath, layout.ContentsPath)} · {layout.ClusterCount} clusters · {layout.NodeCount} nodes · {layout.LinkCount} links. Node content swapping is enabled; topology stays locked.";
        }

        void ApplyFilter(string? preferredRecordId = null)
        {
            string normalizedFilter = FilterText.Trim();
            DisplayedRecords.Clear();
            foreach (SphereGridExplorerRecord row in loadedRecords)
            {
                if (normalizedFilter.Length == 0 || row.SearchBlob.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
                    DisplayedRecords.Add(row);
            }

            SelectedRecord = DisplayedRecords.FirstOrDefault(row => row.RecordId == preferredRecordId) ?? DisplayedRecords.FirstOrDefault();
        }

        void BindEditorSelectionFromRecord(SphereGridExplorerRecord? record)
        {
            SelectedSphereTypeRow = null;
            SelectedNodeTypeRow = null;
            SelectedLayoutNodeRow = null;

            if (record == null || SelectedSource == null)
            {
                SelectedEditorSummary = "Select a sphere-grid record to edit proven fields.";
                SetSelectedOptions(null, null, null, null);
                return;
            }

            switch (SelectedSource.SourceKind)
            {
                case SphereGridSourceKind.SphereTypes:
                    SelectedSphereTypeRow = sphereTypeRows.FirstOrDefault(row => row.Index == record.RecordIndex);
                    SelectedEditorSummary = "Editing sphere descriptions plus the proven action/range fields. Activation bitfield, special role, and zero-only bytes stay locked for now.";
                    SetSelectedOptions(SelectedSphereTypeRow?.Behavior, SelectedSphereTypeRow?.Range, null, null);
                    break;
                case SphereGridSourceKind.NodeTypes:
                    SelectedNodeTypeRow = nodeTypeRows.FirstOrDefault(row => row.Index == record.RecordIndex);
                    SelectedEditorSummary = "Editing node text, learned move, stat bump, and required sphere (NodeEffectBitfield + AppearanceType). Layout topology stays read-only.";
                    SetSelectedOptions(null, null, SelectedNodeTypeRow?.LearnedMove, null, SelectedNodeTypeRow);
                    break;
                case SphereGridSourceKind.OriginalGrid:
                case SphereGridSourceKind.StandardGrid:
                case SphereGridSourceKind.ExpertGrid:
                    if (layoutNodeRowsBySource.TryGetValue(SelectedSource.SourceKind, out List<SphereGridLayoutNodeEditorRow>? rows))
                    {
                        SelectedLayoutNodeRow = rows.FirstOrDefault(row => row.Index == record.RecordIndex);
                    }
                    SelectedEditorSummary = "Node content swapping is live here. Position, cluster, links, and layout topology remain read-only until Jarvis proves the structure harder.";
                    SetSelectedOptions(null, null, null, SelectedLayoutNodeRow?.ContentIndex, null);
                    break;
            }
        }

        void SetSelectedOptions(ushort? behavior, byte? range, ushort? learnedMove, int? layoutNodeType, SphereGridNodeTypeEditorRow? nodeTypeRow = null)
        {
            suppressOptionPropagation = true;
            try
            {
                SelectedActionOption = behavior.HasValue ? EnsureActionOption(behavior.Value) : null;
                SelectedRangeOption = range.HasValue ? EnsureRangeOption(range.Value) : null;
                SelectedLearnedMoveOption = learnedMove.HasValue ? EnsureLearnedMoveOption(learnedMove.Value) : null;
                SelectedLayoutNodeTypeOption = layoutNodeType.HasValue ? EnsureNodeTypeOption(layoutNodeType.Value) : null;
                SelectedSphereRequirementOption = nodeTypeRow == null
                    ? null
                    : EnsureSphereRequirementOption(nodeTypeRow.NodeEffectBitfield, nodeTypeRow.AppearanceType);
                OnPropertyChanged(nameof(SelectedNodeEffectAppearanceSummary));
            }
            finally
            {
                suppressOptionPropagation = false;
            }
        }

        void UpdateLayoutPreview(SphereGridSourceRow source)
        {
            SphereGridLayoutFile? layout = source.SourceKind switch
            {
                SphereGridSourceKind.OriginalGrid => originalGrid,
                SphereGridSourceKind.StandardGrid => standardGrid,
                SphereGridSourceKind.ExpertGrid => expertGrid,
                _ => null
            };

            SelectedLayoutPreview = layout;
            IsLayoutPreviewVisible = layout != null;
            SelectedLayoutHighlightedNodeIndex = -1;
            SelectedLayoutBackgroundKind = source.SourceKind switch
            {
                SphereGridSourceKind.OriginalGrid => SphereGridPreviewBackgroundKind.Original,
                SphereGridSourceKind.StandardGrid => SphereGridPreviewBackgroundKind.Standard,
                SphereGridSourceKind.ExpertGrid => SphereGridPreviewBackgroundKind.Expert,
                _ => SphereGridPreviewBackgroundKind.None
            };
            UpdateLayoutPreviewSummary();
        }

        void UpdateLayoutPreviewSummary()
        {
            if (SelectedLayoutPreview == null)
            {
                SelectedLayoutPreviewSummary = "Layout preview appears when you pick Original, Standard, or Expert.";
                return;
            }

            string modeLabel = SelectedPreviewVisualMode switch
            {
                SphereGridPreviewVisualMode.HybridGame => "almost in-game",
                SphereGridPreviewVisualMode.DebugColors => "debug colors",
                SphereGridPreviewVisualMode.LabelOverlay => "label overlay",
                SphereGridPreviewVisualMode.BackgroundOnly => "background only",
                _ => "preview"
            };

            SelectedLayoutPreviewSummary =
                $"{SelectedLayoutPreview.DisplayName} · {SelectedLayoutPreview.ClusterCount} clusters · {SelectedLayoutPreview.NodeCount} nodes · {SelectedLayoutPreview.LinkCount} links. " +
                $"Click a node to focus it, use zoom for close work, and switch visual mode between {modeLabel} and the other game/debug views.";
        }

        void UpdatePreviewPresetSummary()
        {
            string modeLabel = SelectedPreviewVisualMode switch
            {
                SphereGridPreviewVisualMode.HybridGame => "Almost In-Game",
                SphereGridPreviewVisualMode.DebugColors => "Colors Only",
                SphereGridPreviewVisualMode.LabelOverlay => "Node Labels",
                SphereGridPreviewVisualMode.BackgroundOnly => "Background Only",
                _ => "Preview"
            };

            if (SelectedPreviewPresetOption == null)
            {
                SelectedPreviewPresetSummary = $"Custom view · {modeLabel} · {PreviewZoomSummary}.";
                return;
            }

            SelectedPreviewPresetSummary =
                $"{SelectedPreviewPresetOption.Label} · {SelectedPreviewPresetOption.Summary} Current: {modeLabel} at {PreviewZoomSummary}.";
        }

        void SubscribeEditorGraph()
        {
            foreach (SphereGridSphereTypeEditorRow row in sphereTypeRows)
                row.PropertyChanged += EditorRowChanged;

            foreach (SphereGridNodeTypeEditorRow row in nodeTypeRows)
                row.PropertyChanged += EditorRowChanged;

            foreach (List<SphereGridLayoutNodeEditorRow> rows in layoutNodeRowsBySource.Values)
            {
                foreach (SphereGridLayoutNodeEditorRow row in rows)
                    row.PropertyChanged += EditorRowChanged;
            }
        }

        void UnsubscribeEditorGraph()
        {
            foreach (SphereGridSphereTypeEditorRow row in sphereTypeRows)
                row.PropertyChanged -= EditorRowChanged;

            foreach (SphereGridNodeTypeEditorRow row in nodeTypeRows)
                row.PropertyChanged -= EditorRowChanged;

            foreach (List<SphereGridLayoutNodeEditorRow> rows in layoutNodeRowsBySource.Values)
            {
                foreach (SphereGridLayoutNodeEditorRow row in rows)
                    row.PropertyChanged -= EditorRowChanged;
            }
        }

        void EditorRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (isApplyingEditorState)
                return;

            RefreshDynamicOptions();
            RebuildSelectedSourceRecords(SelectedRecord?.RecordId);
            UpdatePendingDiff();
            EditSession.NotifyPotentialMutation();
        }

        void RefreshDynamicOptions()
        {
            BuildPreviewVisuals();
            RefreshLearnedMoveOptions();
            RefreshNodeTypeOptions();
            SetSelectedOptions(
                SelectedSphereTypeRow?.Behavior,
                SelectedSphereTypeRow?.Range,
                SelectedNodeTypeRow?.LearnedMove,
                SelectedLayoutNodeRow?.ContentIndex,
                SelectedNodeTypeRow);
        }

        void RefreshLearnedMoveOptions()
        {
            int? previousValue = SelectedLearnedMoveOption?.Value;
            LearnedMoveOptions.Clear();
            LearnedMoveOptions.Add(new SphereGridNamedOption { Value = 0, Label = "0000h · No learned move" });

            foreach ((int index, string name) in commandNames.OrderBy(pair => pair.Key))
            {
                // On-disk LearnedMove is the ENCODED command id (0x3000 | id): the engine passes it
                // straight to FFX_GrantCommandToCharacter, which requires the 0x3000 category nibble
                // (id 0x140 alone is rejected). Empirically Armor Break = 0x3012 in panel.bin.
                // RE: docs/reverse/FFX_NUL_WARD_TEACH_SURFACE_RE_VERDICT_2026-06-16.md §G.
                int encoded = CommandCategoryNibble | index;
                LearnedMoveOptions.Add(new SphereGridNamedOption
                {
                    Value = encoded,
                    Label = $"{encoded:X4}h · {name}"
                });
            }

            if (previousValue.HasValue)
                SelectedLearnedMoveOption = EnsureLearnedMoveOption(previousValue.Value);
        }

        void RefreshNodeTypeOptions()
        {
            int? previousValue = SelectedLayoutNodeTypeOption?.Value;
            NodeTypeOptions.Clear();

            foreach (SphereGridNodeTypeEditorRow row in nodeTypeRows.OrderBy(row => row.Index))
            {
                NodeTypeOptions.Add(new SphereGridNamedOption
                {
                    Value = row.Index,
                    Label = $"{row.Index:X2}h · {PreferredName(row)}"
                });
            }

            if (previousValue.HasValue)
                SelectedLayoutNodeTypeOption = EnsureNodeTypeOption(previousValue.Value);
        }

        SphereGridNamedOption EnsureActionOption(int value)
        {
            SphereGridNamedOption? option = ActionOptions.FirstOrDefault(item => item.Value == value);
            if (option != null)
                return option;

            option = new SphereGridNamedOption { Value = value, Label = $"{value:X4}h · Unknown" };
            ActionOptions.Add(option);
            return option;
        }

        SphereGridNamedOption EnsureRangeOption(int value)
        {
            SphereGridNamedOption? option = RangeOptions.FirstOrDefault(item => item.Value == value);
            if (option != null)
                return option;

            option = new SphereGridNamedOption { Value = value, Label = $"{value:X2}h · Unknown" };
            RangeOptions.Add(option);
            return option;
        }

        SphereGridNamedOption EnsureLearnedMoveOption(int value)
        {
            SphereGridNamedOption? option = LearnedMoveOptions.FirstOrDefault(item => item.Value == value);
            if (option != null)
                return option;

            string label;
            if (value == 0)
                label = "0000h · No learned move";
            else if (commandNames.TryGetValue(value & 0xFFF, out string? named) && !string.IsNullOrWhiteSpace(named) && named != "-")
                label = $"{value:X4}h · {named}";
            else
                label = $"{value:X4}h · Command {value & 0xFFF:D3}";

            option = new SphereGridNamedOption { Value = value, Label = label };
            LearnedMoveOptions.Add(option);
            return option;
        }

        SphereGridNamedOption EnsureSphereRequirementOption(ushort effect, ushort appearance)
        {
            int kind = (int)SphereGridNodeSphereRequirement.DetectKind(effect, appearance);
            SphereGridNamedOption? option = SphereRequirementOptions.FirstOrDefault(item => item.Value == kind);
            if (option != null)
                return option;

            option = new SphereGridNamedOption
            {
                Value = kind,
                Label = SphereGridNodeSphereRequirement.FormatShortLabel(effect, appearance)
            };
            SphereRequirementOptions.Add(option);
            return option;
        }

        SphereGridNamedOption EnsureNodeTypeOption(int value)
        {
            SphereGridNamedOption? option = NodeTypeOptions.FirstOrDefault(item => item.Value == value);
            if (option != null)
                return option;

            option = new SphereGridNamedOption { Value = value, Label = $"{value:X2}h · {ResolveNodeTypeDisplayName(value)}" };
            NodeTypeOptions.Add(option);
            return option;
        }

        byte[] BuildEditorStateSnapshot()
        {
            SphereGridEditorStateSnapshot snapshot = new()
            {
                SphereTypes = sphereTypeRows.Select(CreateSphereTypeState).ToList(),
                NodeTypes = nodeTypeRows.Select(CreateNodeTypeState).ToList(),
                OriginalGridNodes = GetLayoutRows(SphereGridSourceKind.OriginalGrid).Select(CreateLayoutNodeState).ToList(),
                StandardGridNodes = GetLayoutRows(SphereGridSourceKind.StandardGrid).Select(CreateLayoutNodeState).ToList(),
                ExpertGridNodes = GetLayoutRows(SphereGridSourceKind.ExpertGrid).Select(CreateLayoutNodeState).ToList()
            };
            return JsonSerializer.SerializeToUtf8Bytes(snapshot);
        }

        void RestoreEditorStateSnapshot(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return;

            SphereGridEditorStateSnapshot? snapshot = JsonSerializer.Deserialize<SphereGridEditorStateSnapshot>(bytes);
            if (snapshot == null)
                return;

            isApplyingEditorState = true;
            try
            {
                ApplySphereTypeStates(snapshot.SphereTypes);
                ApplyNodeTypeStates(snapshot.NodeTypes);
                ApplyLayoutNodeStates(SphereGridSourceKind.OriginalGrid, snapshot.OriginalGridNodes);
                ApplyLayoutNodeStates(SphereGridSourceKind.StandardGrid, snapshot.StandardGridNodes);
                ApplyLayoutNodeStates(SphereGridSourceKind.ExpertGrid, snapshot.ExpertGridNodes);
            }
            finally
            {
                isApplyingEditorState = false;
            }

            RefreshDynamicOptions();
            RebuildSelectedSourceRecords(SelectedRecord?.RecordId);
            UpdatePendingDiff();
        }

        void PersistEditorState(byte[] bytes)
        {
            if (sphereTypes != null)
                SphereGrid_File.WriteSphereTypes(sphereTypes, sphereTypeRows.Select(ToWriteModel).ToList());

            if (nodeTypes != null)
                SphereGrid_File.WriteNodeTypes(nodeTypes, nodeTypeRows.Select(ToWriteModel).ToList());

            if (originalGrid != null)
                SphereGrid_File.WriteLayoutNodeContents(originalGrid, GetLayoutRows(SphereGridSourceKind.OriginalGrid).Select(ToWriteModel).ToList());

            if (standardGrid != null)
                SphereGrid_File.WriteLayoutNodeContents(standardGrid, GetLayoutRows(SphereGridSourceKind.StandardGrid).Select(ToWriteModel).ToList());

            if (expertGrid != null)
                SphereGrid_File.WriteLayoutNodeContents(expertGrid, GetLayoutRows(SphereGridSourceKind.ExpertGrid).Select(ToWriteModel).ToList());

            CaptureBaselinesFromEditors();
            RefreshDynamicOptions();
            RebuildSelectedSourceRecords(SelectedRecord?.RecordId);
            UpdatePendingDiff();
        }

        void ApplySphereTypeStates(IReadOnlyList<SphereGridSphereTypeState> states)
        {
            Dictionary<int, SphereGridSphereTypeState> stateMap = states.ToDictionary(state => state.Index);
            foreach (SphereGridSphereTypeEditorRow row in sphereTypeRows)
            {
                if (!stateMap.TryGetValue(row.Index, out SphereGridSphereTypeState? state))
                    continue;

                row.JpDescription = state.JpDescription;
                row.UsDescription = state.UsDescription;
                row.JpSimplifiedDescription = state.JpSimplifiedDescription;
                row.UsSimplifiedDescription = state.UsSimplifiedDescription;
                row.Behavior = state.Behavior;
                row.Range = state.Range;
            }
        }

        void ApplyNodeTypeStates(IReadOnlyList<SphereGridNodeTypeState> states)
        {
            Dictionary<int, SphereGridNodeTypeState> stateMap = states.ToDictionary(state => state.Index);
            foreach (SphereGridNodeTypeEditorRow row in nodeTypeRows)
            {
                if (!stateMap.TryGetValue(row.Index, out SphereGridNodeTypeState? state))
                    continue;

                row.JpName = state.JpName;
                row.UsName = state.UsName;
                row.JpSimplifiedName = state.JpSimplifiedName;
                row.UsSimplifiedName = state.UsSimplifiedName;
                row.JpDescription = state.JpDescription;
                row.UsDescription = state.UsDescription;
                row.JpSimplifiedDescription = state.JpSimplifiedDescription;
                row.UsSimplifiedDescription = state.UsSimplifiedDescription;
                row.LearnedMove = state.LearnedMove;
                row.IncreaseAmount = state.IncreaseAmount;
                row.NodeEffectBitfield = state.NodeEffectBitfield;
                row.AppearanceType = state.AppearanceType;
            }
        }

        void ApplyLayoutNodeStates(SphereGridSourceKind sourceKind, IReadOnlyList<SphereGridLayoutNodeState> states)
        {
            if (!layoutNodeRowsBySource.TryGetValue(sourceKind, out List<SphereGridLayoutNodeEditorRow>? rows))
                return;

            Dictionary<int, SphereGridLayoutNodeState> stateMap = states.ToDictionary(state => state.Index);
            foreach (SphereGridLayoutNodeEditorRow row in rows)
            {
                if (!stateMap.TryGetValue(row.Index, out SphereGridLayoutNodeState? state))
                    continue;

                row.ContentIndex = state.ContentIndex;
            }
        }

        void CaptureBaselinesFromEditors()
        {
            sphereTypeBaseline.Clear();
            foreach (SphereGridSphereTypeEditorRow row in sphereTypeRows)
                sphereTypeBaseline[row.Index] = CreateSphereTypeState(row);

            nodeTypeBaseline.Clear();
            foreach (SphereGridNodeTypeEditorRow row in nodeTypeRows)
                nodeTypeBaseline[row.Index] = CreateNodeTypeState(row);

            layoutBaselineBySource.Clear();
            foreach ((SphereGridSourceKind sourceKind, List<SphereGridLayoutNodeEditorRow> rows) in layoutNodeRowsBySource)
            {
                layoutBaselineBySource[sourceKind] = rows.ToDictionary(row => row.Index, CreateLayoutNodeState);
            }
        }

        void UpdatePendingDiff()
        {
            PendingDiffRows.Clear();

            foreach (SphereGridSphereTypeEditorRow row in sphereTypeRows)
            {
                if (!sphereTypeBaseline.TryGetValue(row.Index, out SphereGridSphereTypeState? baseline))
                    continue;

                AddTextDiff($"Sphere Type {row.Index:X2}h", "US Description", baseline.UsDescription, row.UsDescription);
                AddTextDiff($"Sphere Type {row.Index:X2}h", "JP Description", baseline.JpDescription, row.JpDescription);
                AddTextDiff($"Sphere Type {row.Index:X2}h", "US Simplified", baseline.UsSimplifiedDescription, row.UsSimplifiedDescription);
                AddTextDiff($"Sphere Type {row.Index:X2}h", "JP Simplified", baseline.JpSimplifiedDescription, row.JpSimplifiedDescription);
                AddValueDiff($"Sphere Type {row.Index:X2}h", "Action", baseline.Behavior, row.Behavior, value => $"{value:X4}h");
                AddValueDiff($"Sphere Type {row.Index:X2}h", "Range", baseline.Range, row.Range, value => $"{value:X2}h");
            }

            foreach (SphereGridNodeTypeEditorRow row in nodeTypeRows)
            {
                if (!nodeTypeBaseline.TryGetValue(row.Index, out SphereGridNodeTypeState? baseline))
                    continue;

                AddTextDiff($"Node Type {row.Index:X2}h", "US Name", baseline.UsName, row.UsName);
                AddTextDiff($"Node Type {row.Index:X2}h", "JP Name", baseline.JpName, row.JpName);
                AddTextDiff($"Node Type {row.Index:X2}h", "US Simplified Name", baseline.UsSimplifiedName, row.UsSimplifiedName);
                AddTextDiff($"Node Type {row.Index:X2}h", "JP Simplified Name", baseline.JpSimplifiedName, row.JpSimplifiedName);
                AddTextDiff($"Node Type {row.Index:X2}h", "US Description", baseline.UsDescription, row.UsDescription);
                AddTextDiff($"Node Type {row.Index:X2}h", "JP Description", baseline.JpDescription, row.JpDescription);
                AddTextDiff($"Node Type {row.Index:X2}h", "US Simplified Description", baseline.UsSimplifiedDescription, row.UsSimplifiedDescription);
                AddTextDiff($"Node Type {row.Index:X2}h", "JP Simplified Description", baseline.JpSimplifiedDescription, row.JpSimplifiedDescription);
                AddValueDiff($"Node Type {row.Index:X2}h", "Learned Move", baseline.LearnedMove, row.LearnedMove, value => value == 0 ? "0000h · No learned move" : ResolveCommandName(value));
                AddValueDiff($"Node Type {row.Index:X2}h", "Increase Amount", baseline.IncreaseAmount, row.IncreaseAmount, value => $"{value:X4}h ({value})");
                AddValueDiff($"Node Type {row.Index:X2}h", "Effect Bitfield", baseline.NodeEffectBitfield, row.NodeEffectBitfield, value => $"{value:X4}h");
                AddValueDiff($"Node Type {row.Index:X2}h", "Appearance Type", baseline.AppearanceType, row.AppearanceType, value => $"{value:X4}h");
            }

            foreach ((SphereGridSourceKind sourceKind, Dictionary<int, SphereGridLayoutNodeState> baselineMap) in layoutBaselineBySource)
            {
                foreach (SphereGridLayoutNodeEditorRow row in GetLayoutRows(sourceKind))
                {
                    if (!baselineMap.TryGetValue(row.Index, out SphereGridLayoutNodeState? baseline))
                        continue;

                    AddValueDiff($"{GetSourceKindLabel(sourceKind)} Node {row.Index:D3}", "Node Type", baseline.ContentIndex, row.ContentIndex, ResolveNodeTypeDisplayName);
                }
            }

            PendingDiffSummary = PendingDiffRows.Count == 0
                ? "No pending sphere-grid edits."
                : $"{PendingDiffRows.Count} staged sphere-grid change(s) ready for diff review before save.";
            OnPropertyChanged(nameof(HasPendingDiffRows));
        }

        void BuildPreviewVisuals()
        {
            previewNodeVisuals.Clear();
            foreach (SphereGridNodeTypeEditorRow row in nodeTypeRows)
            {
                previewNodeVisuals[row.Index] = CreatePreviewVisual(row);
            }

            OnPropertyChanged(nameof(PreviewNodeVisuals));
        }

        SphereGridNodeVisualInfo CreatePreviewVisual(SphereGridNodeTypeEditorRow row)
        {
            string displayName = PreferredName(row);
            int lockLevel = row.AppearanceType switch
            {
                0x12 => 1,
                0x11 => 2,
                0x00 => 3,
                0x10 => 4,
                _ => 0
            };

            return new SphereGridNodeVisualInfo
            {
                ContentIndex = row.Index,
                AppearanceType = row.AppearanceType,
                DisplayName = displayName,
                ShortLabel = BuildShortPreviewLabel(displayName, row.AppearanceType, lockLevel),
                IsEmpty = row.AppearanceType == 0x01,
                IsLock = lockLevel > 0,
                LockLevel = lockLevel
            };
        }

        static string BuildShortPreviewLabel(string displayName, ushort appearanceType, int lockLevel)
        {
            if (lockLevel > 0)
                return $"L{lockLevel}";

            if (appearanceType == 0x01)
                return "--";

            if (displayName.StartsWith("HP", StringComparison.OrdinalIgnoreCase))
                return "HP";
            if (displayName.StartsWith("MP", StringComparison.OrdinalIgnoreCase))
                return "MP";
            if (displayName.StartsWith("Strength", StringComparison.OrdinalIgnoreCase))
                return "STR";
            if (displayName.StartsWith("Magic Defense", StringComparison.OrdinalIgnoreCase))
                return "MDF";
            if (displayName.StartsWith("Magic", StringComparison.OrdinalIgnoreCase))
                return "MAG";
            if (displayName.StartsWith("Defense", StringComparison.OrdinalIgnoreCase))
                return "DEF";
            if (displayName.StartsWith("Accuracy", StringComparison.OrdinalIgnoreCase))
                return "ACC";
            if (displayName.StartsWith("Evasion", StringComparison.OrdinalIgnoreCase))
                return "EVA";
            if (displayName.StartsWith("Luck", StringComparison.OrdinalIgnoreCase))
                return "LCK";
            if (displayName.StartsWith("Agility", StringComparison.OrdinalIgnoreCase))
                return "AGI";

            return appearanceType switch
            {
                0x0C => "WHT",
                0x0D => "BLK",
                0x0E => "SPL",
                0x0F => "SKL",
                _ => displayName.Length <= 4 ? displayName.ToUpperInvariant() : displayName[..4].ToUpperInvariant()
            };
        }

        void SetPreviewZoom(double value)
        {
            PreviewZoomFactor = Math.Clamp(value, 0.55, 2.8);
        }

        void AddTextDiff(string scope, string field, string before, string after)
        {
            if (string.Equals((before ?? string.Empty).Trim(), (after ?? string.Empty).Trim(), StringComparison.Ordinal))
                return;

            PendingDiffRows.Add(new SphereGridDiffRow
            {
                Scope = scope,
                Change = $"{field}: \"{TrimForDiff(before)}\" -> \"{TrimForDiff(after)}\""
            });
        }

        void AddValueDiff<T>(string scope, string field, T before, T after, Func<T, string> formatter)
            where T : struct, IEquatable<T>
        {
            if (before.Equals(after))
                return;

            PendingDiffRows.Add(new SphereGridDiffRow
            {
                Scope = scope,
                Change = $"{field}: {formatter(before)} -> {formatter(after)}"
            });
        }

        List<SphereGridLayoutNodeEditorRow> GetLayoutRows(SphereGridSourceKind sourceKind)
        {
            return layoutNodeRowsBySource.TryGetValue(sourceKind, out List<SphereGridLayoutNodeEditorRow>? rows)
                ? rows
                : new List<SphereGridLayoutNodeEditorRow>();
        }

        SphereGridSphereTypeWriteModel ToWriteModel(SphereGridSphereTypeEditorRow row)
        {
            return new SphereGridSphereTypeWriteModel
            {
                Index = row.Index,
                JpDescription = row.JpDescription,
                UsDescription = row.UsDescription,
                JpSimplifiedDescription = row.JpSimplifiedDescription,
                UsSimplifiedDescription = row.UsSimplifiedDescription,
                Behavior = row.Behavior,
                Range = row.Range
            };
        }

        SphereGridNodeTypeWriteModel ToWriteModel(SphereGridNodeTypeEditorRow row)
        {
            return new SphereGridNodeTypeWriteModel
            {
                Index = row.Index,
                JpName = row.JpName,
                UsName = row.UsName,
                JpSimplifiedName = row.JpSimplifiedName,
                UsSimplifiedName = row.UsSimplifiedName,
                JpDescription = row.JpDescription,
                UsDescription = row.UsDescription,
                JpSimplifiedDescription = row.JpSimplifiedDescription,
                UsSimplifiedDescription = row.UsSimplifiedDescription,
                LearnedMove = row.LearnedMove,
                IncreaseAmount = row.IncreaseAmount,
                NodeEffectBitfield = row.NodeEffectBitfield,
                AppearanceType = row.AppearanceType,
            };
        }

        SphereGridLayoutNodeWriteModel ToWriteModel(SphereGridLayoutNodeEditorRow row)
        {
            return new SphereGridLayoutNodeWriteModel
            {
                Index = row.Index,
                ContentIndex = row.ContentIndex
            };
        }

        static SphereGridSphereTypeState CreateSphereTypeState(SphereGridSphereTypeEditorRow row)
        {
            return new SphereGridSphereTypeState
            {
                Index = row.Index,
                JpDescription = row.JpDescription,
                UsDescription = row.UsDescription,
                JpSimplifiedDescription = row.JpSimplifiedDescription,
                UsSimplifiedDescription = row.UsSimplifiedDescription,
                Behavior = row.Behavior,
                Range = row.Range
            };
        }

        static SphereGridNodeTypeState CreateNodeTypeState(SphereGridNodeTypeEditorRow row)
        {
            return new SphereGridNodeTypeState
            {
                Index = row.Index,
                JpName = row.JpName,
                UsName = row.UsName,
                JpSimplifiedName = row.JpSimplifiedName,
                UsSimplifiedName = row.UsSimplifiedName,
                JpDescription = row.JpDescription,
                UsDescription = row.UsDescription,
                JpSimplifiedDescription = row.JpSimplifiedDescription,
                UsSimplifiedDescription = row.UsSimplifiedDescription,
                LearnedMove = row.LearnedMove,
                IncreaseAmount = row.IncreaseAmount,
                NodeEffectBitfield = row.NodeEffectBitfield,
                AppearanceType = row.AppearanceType,
            };
        }

        static SphereGridLayoutNodeState CreateLayoutNodeState(SphereGridLayoutNodeEditorRow row)
        {
            return new SphereGridLayoutNodeState
            {
                Index = row.Index,
                ContentIndex = row.ContentIndex
            };
        }

        SphereGridNodeTypeEntry? ResolveOriginalNodeType(int contentIndex)
        {
            return nodeTypeEntriesByIndex.TryGetValue(contentIndex, out SphereGridNodeTypeEntry? entry)
                ? entry
                : null;
        }

        string BuildSphereTypeTitle(SphereGridSphereTypeEditorRow row)
        {
            string description = PreferredText(row.UsDescription, row.JpDescription);
            return string.IsNullOrWhiteSpace(description)
                ? $"Sphere Type {row.Index:X2}h"
                : $"Type {row.Index:X2}h · {TrimForTitle(description)}";
        }

        string BuildSphereTypeBehavior(SphereGridSphereTypeEditorRow row, SphereGridSphereTypeEntry original)
        {
            return string.Join(Environment.NewLine, new[]
            {
                $"Action: {ActionLabel(row.Behavior)}",
                $"Range: {RangeLabel(row.Range)}",
                $"Activation Bitfield: {original.Activates:X4}h (locked)",
                $"Special Role: {original.SpecialRole:X2}h (locked)",
                $"Always Zero?: {original.Reserved0x0E:X4}h (locked)"
            });
        }

        string BuildNodeTypePayload(SphereGridNodeTypeEditorRow row, SphereGridNodeTypeEntry original)
        {
            string learnedMove = row.LearnedMove == 0
                ? "No learned move"
                : ResolveCommandName(row.LearnedMove);

            return string.Join(Environment.NewLine, new[]
            {
                $"Required Sphere: {SphereGridNodeSphereRequirement.FormatShortLabel(row.NodeEffectBitfield, row.AppearanceType)}",
                $"Learned Move: {learnedMove}",
                $"Increase Amount: {row.IncreaseAmount:X4}h ({row.IncreaseAmount})",
                $"Effect Bitfield: {row.NodeEffectBitfield:X4}h",
                $"Appearance Type: {row.AppearanceType:X4}h"
            });
        }

        string BuildNodePlacement(SphereGridLayoutFile layout, SphereGridLayoutNodeEditorRow row)
        {
            SphereGridClusterEntry? cluster = row.Cluster < layout.Clusters.Count
                ? layout.Clusters[row.Cluster]
                : null;

            List<string> lines = new()
            {
                $"Layout: {layout.DisplayName}",
                $"Position: ({row.PosX}, {row.PosY})",
                $"Cluster: {row.Cluster:D2}"
            };

            if (cluster != null)
            {
                lines.Add($"Cluster Center: ({cluster.PosX}, {cluster.PosY})");
                lines.Add($"Cluster Radius Type: {cluster.RadiusType:X4}h (radius {cluster.Radius}, alt design: {(cluster.AltDesign ? "yes" : "no")})");
            }

            return string.Join(Environment.NewLine, lines);
        }

        string BuildNodeLinks(SphereGridLayoutFile layout, int nodeIndex)
        {
            if (nodeIndex < 0 || nodeIndex >= layout.Nodes.Count)
                return "No connected links resolved for this node.";

            SphereGridNodeEntry node = layout.Nodes[nodeIndex];
            if (node.ConnectedNodeIndices.Count == 0)
                return "No connected links resolved for this node.";

            List<string> lines = new()
            {
                $"Connected Nodes ({node.ConnectedNodeIndices.Count}): {string.Join(", ", node.ConnectedNodeIndices.Select(index => index.ToString("D3")))}"
            };

            if (node.AnchorLinkIndices.Count > 0)
                lines.Add($"Anchor For Links: {string.Join(", ", node.AnchorLinkIndices.Select(index => index.ToString("D3")))}");

            foreach (int linkIndex in node.ConnectedLinkIndices.Take(8))
            {
                if (linkIndex >= layout.Links.Count)
                    continue;

                SphereGridLinkEntry link = layout.Links[linkIndex];
                lines.Add($"Link {link.Index:D3}: {link.Node1:D3} <-> {link.Node2:D3} · Anchor {link.AnchorNode:D3}");
            }

            if (node.ConnectedLinkIndices.Count > 8)
                lines.Add($"... {node.ConnectedLinkIndices.Count - 8} more link rows omitted");

            return string.Join(Environment.NewLine, lines);
        }

        string BuildNodeRaw(SphereGridLayoutNodeEditorRow row, SphereGridNodeTypeEntry? nodeType)
        {
            List<string> lines = new()
            {
                $"Content Index: {row.ContentIndex:X2}h ({row.ContentIndex})",
                $"Redundant Content: {row.RedundantContent:X4}h",
                $"Unknown6: {row.Unknown6:X4}h"
            };

            if (nodeType != null)
            {
                lines.Add($"NodeType Appearance: {nodeType.AppearanceType:X4}h");
                lines.Add($"NodeType Effects: {nodeType.NodeEffectBitfield:X4}h");
            }

            return string.Join(Environment.NewLine, lines);
        }

        string ResolveCommandName(int commandIndex)
        {
            // LearnedMove is the encoded id (0x3000 | id); resolve the name from the low 12 bits.
            if (commandNames.TryGetValue(commandIndex & 0xFFF, out string? commandName))
                return $"{commandIndex:X4}h · {commandName}";

            return $"{commandIndex:X4}h · Command {commandIndex & 0xFFF:D3}";
        }

        string ResolveNodeTypeDisplayName(int contentIndex)
        {
            SphereGridNodeTypeEditorRow? row = nodeTypeRows.FirstOrDefault(candidate => candidate.Index == contentIndex);
            return row == null ? $"NodeType {contentIndex:X2}h" : PreferredName(row);
        }

        static string PreferredText(string usText, string jpText)
        {
            return !string.IsNullOrWhiteSpace(usText) ? usText : jpText;
        }

        static string PreferredName(SphereGridNodeTypeEditorRow row)
        {
            string preferred = PreferredText(row.UsName, row.JpName);
            return string.IsNullOrWhiteSpace(preferred) ? $"NodeType {row.Index:X2}h" : preferred;
        }

        static string ActionLabel(ushort behavior)
        {
            return behavior switch
            {
                // 0x0000 = no on-touch effect (the third legitimate corpus value), not unknown. Partial display label.
                0x0000 => "None / Passive",
                0x0001 => "Activator",
                0x0002 => "Modifier",
                _ => $"Unknown ({behavior:X4}h)"
            };
        }

        static string RangeLabel(byte range)
        {
            return range switch
            {
                // 0x00 = no reach band (None), the third legitimate corpus value, not unknown.
                0x00 => "None",
                0x01 => "Short Range",
                0x20 => "Long Range",
                _ => $"Unknown ({range:X2}h)"
            };
        }

        static string BuildOverlayPathLabel(string jpPath, string? usPath)
        {
            string primaryPath = File.Exists(jpPath)
                ? jpPath
                : usPath ?? jpPath;

            string jpRelative = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, primaryPath);
            if (string.IsNullOrWhiteSpace(usPath) || !File.Exists(usPath) || string.Equals(primaryPath, usPath, StringComparison.OrdinalIgnoreCase))
                return jpRelative;

            string usRelative = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, usPath);
            return $"{jpRelative}{Environment.NewLine}{usRelative}";
        }

        static string BuildRelativePairLabel(string firstPath, string secondPath)
        {
            string firstRelative = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, firstPath);
            string secondRelative = Path.GetRelativePath(Project_Service.Instance.ProjectPath!, secondPath);
            return $"{firstRelative}{Environment.NewLine}{secondRelative}";
        }

        static string EmptyToPlaceholder(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(Empty)" : value;
        }

        static bool TextEquals(string left, string right)
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);
        }

        static string TrimForTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "(Empty)";

            string singleLine = value.Replace(Environment.NewLine, " ");
            return singleLine.Length > 52 ? singleLine[..52] + "..." : singleLine;
        }

        static string TrimForDiff(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "(Empty)";

            string singleLine = value.Replace(Environment.NewLine, " / ");
            return singleLine.Length > 84 ? singleLine[..84] + "..." : singleLine;
        }

        static string GetSourceKindLabel(SphereGridSourceKind sourceKind)
        {
            return sourceKind switch
            {
                SphereGridSourceKind.OriginalGrid => "Original Grid",
                SphereGridSourceKind.StandardGrid => "Standard Grid",
                SphereGridSourceKind.ExpertGrid => "Expert Grid",
                _ => sourceKind.ToString()
            };
        }

        static bool IsLayoutSource(SphereGridSourceKind sourceKind)
        {
            return sourceKind is SphereGridSourceKind.OriginalGrid or SphereGridSourceKind.StandardGrid or SphereGridSourceKind.ExpertGrid;
        }

        internal enum SphereGridSourceKind
        {
            SphereTypes,
            NodeTypes,
            OriginalGrid,
            StandardGrid,
            ExpertGrid
        }

        internal sealed class SphereGridSourceRow
        {
            public required string Id { get; init; }
            public required string DisplayName { get; init; }
            public required string RelativePath { get; init; }
            public required string ParserModeLabel { get; init; }
            public required string Summary { get; init; }
            public required SphereGridSourceKind SourceKind { get; init; }
        }

        internal sealed class SphereGridExplorerRecord
        {
            public required string RecordId { get; init; }
            public required int RecordIndex { get; init; }
            public required string IndexLabel { get; init; }
            public required string Title { get; init; }
            public required string Summary { get; init; }
            public required string PrimaryLabel { get; init; }
            public required string PrimaryText { get; init; }
            public required string SecondaryLabel { get; init; }
            public required string SecondaryText { get; init; }
            public required string TertiaryLabel { get; init; }
            public required string TertiaryText { get; init; }
            public required string QuaternaryLabel { get; init; }
            public required string QuaternaryText { get; init; }
            public required string ContextSummary { get; init; }
            public required string SearchBlob { get; init; }
            public int? PreviewNodeIndex { get; init; }
        }

        sealed class SphereGridEditorStateSnapshot
        {
            public List<SphereGridSphereTypeState> SphereTypes { get; set; } = new();
            public List<SphereGridNodeTypeState> NodeTypes { get; set; } = new();
            public List<SphereGridLayoutNodeState> OriginalGridNodes { get; set; } = new();
            public List<SphereGridLayoutNodeState> StandardGridNodes { get; set; } = new();
            public List<SphereGridLayoutNodeState> ExpertGridNodes { get; set; } = new();
        }

        sealed class SphereGridSphereTypeState
        {
            public int Index { get; set; }
            public string JpDescription { get; set; } = string.Empty;
            public string UsDescription { get; set; } = string.Empty;
            public string JpSimplifiedDescription { get; set; } = string.Empty;
            public string UsSimplifiedDescription { get; set; } = string.Empty;
            public ushort Behavior { get; set; }
            public byte Range { get; set; }
        }

        sealed class SphereGridNodeTypeState
        {
            public int Index { get; set; }
            public string JpName { get; set; } = string.Empty;
            public string UsName { get; set; } = string.Empty;
            public string JpSimplifiedName { get; set; } = string.Empty;
            public string UsSimplifiedName { get; set; } = string.Empty;
            public string JpDescription { get; set; } = string.Empty;
            public string UsDescription { get; set; } = string.Empty;
            public string JpSimplifiedDescription { get; set; } = string.Empty;
            public string UsSimplifiedDescription { get; set; } = string.Empty;
            public ushort LearnedMove { get; set; }
            public ushort IncreaseAmount { get; set; }
            public ushort NodeEffectBitfield { get; set; }
            public ushort AppearanceType { get; set; }
        }

        sealed class SphereGridLayoutNodeState
        {
            public int Index { get; set; }
            public int ContentIndex { get; set; }
        }
    }
}
