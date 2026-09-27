using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXProjectEditor.Controls.SphereGrid;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.SphereGrid;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Encoding;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.SphereGridBuilder
{
    // v2 canvas DataModel: LOAD a shipped grid (Original/Standard/Expert) via ReadLayout -> FromExisting, OR start
    // a New (from-scratch) builder, then drive the visual SphereGridCanvasView. Editing goes through the lib's
    // proven path (FromExisting + Move/Update/Add/Remove -> Build -> WriteLayout), gated by --spheregrid-edit-rt0.
    // Honest: loading an edited/new TOPOLOGY in-game is unproven (offline byte-safe != the engine accepting it).
    internal partial class SphereGridCanvas_DataModel : ObservableObject
    {
        public enum GridKind { New, Original, Standard, Expert }

        private SphereGridLayoutBuilder _builder = new();
        private SphereGridCanvasView? _view;
        private GridKind _kind = GridKind.New;
        private bool _syncingSel;

        [ObservableProperty] private string sourceLabel = Strings.F2_no_grid_loaded_use_new_or_open_original__fe1e32f6;
        [ObservableProperty] private string countsSummary = Strings.U_Sgb_Counts;
        [ObservableProperty] private string gameCompatibilitySummary = ""; // N/860 · L/1021|934 (game-compatible caps)

        [ObservableProperty] private string modeLabel = Strings.U_Sgb_ModeSelect;
        // Jarvis-UI (Sprint D 2026-06-20, OPT-D2): modo de ferramenta ativo como enum, pra o botão ativo na
        // toolbar receber classe visual (toolActive) sem depender de parse de ModeLabel. Espelha os modos do
        // SphereGridCanvasView.Mode (Select/AddNode/AddLink).
        [ObservableProperty] private ToolMode activeToolMode = ToolMode.Select;
        [ObservableProperty] private string statusSummary = "";
        [ObservableProperty] private string liveRiskWarning = ""; // auto anti-xablau: live break-risk while editing
        [ObservableProperty] private string validationSummary = "";
        [ObservableProperty] private string activeClusterText = "0";
        [ObservableProperty] private bool snapEnabled = true;
        [ObservableProperty] private bool showLabels;
        [ObservableProperty] private bool showIcons = true;
        [ObservableProperty] private string topologySafetySummary = "";
        [ObservableProperty] private bool squareModeEnabled;

        /// <summary>
        /// Opt-in CLEAN no-hook runtime test (RT2): unlocks SaveToProject/SaveSquareToProject with changed
        /// counts (header-driven engine; static LpAbilityMapEngine holds 1024/1024/128; native save 1280/1280).
        /// The old 861/882 crash finding came from the retired v5 hook stack, never from a clean deploy.
        /// Requires a disposable save. See SphereGridDeployPolicy.
        /// </summary>
        [ObservableProperty] private bool allowRuntimeTestDeploy;

        partial void OnAllowRuntimeTestDeployChanged(bool value)
        {
            _builder.AllowRuntimeTestDeploy = value;
            RefreshTopologySafetySummary();
            ValidationSummary = "";
        }

        // Square Mode unlocks topology mutation (add/remove nodes/links) on seeded grids.
        // Without Square Mode, seeded grids are edit-only (move/relink, counts preserved).

        // selected-node side panel
        [ObservableProperty] private bool hasSelection;
        [ObservableProperty] private string selIndexLabel = "—";
        [ObservableProperty] private string selPosX = "";
        [ObservableProperty] private string selPosY = "";
        [ObservableProperty] private string selCluster = "";
        [ObservableProperty] private string selContent = "";

        // links of the selected node (managed from the panel) + the "add link to node #" target box
        public ObservableCollection<NodeLinkRow> SelectedNodeLinks { get; } = new();
        [ObservableProperty] private string addLinkTargetText = "";

        // Friendly node-type picker (content index -> name from panel.bin). The hex SelContent box stays as the
        // advanced/fallback when panel.bin can't be resolved or for a raw index not in the table.
        public ObservableCollection<NodeTypeOption> NodeTypeOptions { get; } = new();
        [ObservableProperty] private NodeTypeOption? selectedNodeTypeOption;
        private bool _nodeTypesLoaded;
        /// <summary>command.bin category nibble OR-ed into panel LearnedMove (engine grant gate).</summary>
        const int CommandCategoryNibble = 0x3000;
        private readonly Dictionary<int, string> _commandNames = new();
        private readonly Dictionary<int, string> _jpCommandNames = new();
        private readonly Dictionary<int, int> _commandIdToContentIndex = new();
        private readonly Dictionary<int, ushort> _contentIndexToLearnedMove = new();
        private readonly Dictionary<int, string> _nodeTypeNames = new(); // content index -> short name (for canvas labels)
        private readonly Dictionary<int, IBrush> _nodeBrushes = new(); // content index -> category fill brush (HP green, etc.)
        private readonly Dictionary<int, SphereGridNodeVisualInfo> _nodeVisuals = new(); // content -> lock level / short badge / empty
        private readonly Dictionary<int, Geometry> _nodeIcons = new(); // content -> game-icons.net vector glyph (crisp at any zoom)
        public ObservableCollection<LegendRow> Legend { get; } = new();
        private int _prevTotal = -1;   // node+link count, to play add/remove sounds on change
        private bool _prevRisky;       // edge-detect the live risk to play the alert once

        public string Banner => Strings.U_Sgb_Caps;

        public void AttachView(SphereGridCanvasView view)
        {
            _view = view;
            _view.GridChanged += OnGridChanged;
            _view.NodeSelected += OnNodeSelected;
            _view.Status += s => StatusSummary = s;
            _view.LiveRisk += m =>
            {
                bool risky = !string.IsNullOrEmpty(m);
                if (risky && !_prevRisky) TryPlay(a => a.PlayAlternative()); // alert once on the rising edge
                _prevRisky = risky;
                LiveRiskWarning = m ?? "";
            };
            _view.SnapStep = SnapEnabled ? 43 : 0;
            EnsureNodeTypesLoaded();
            _view.NodeTypeNames = _nodeTypeNames;
            _view.NodeBrushes = _nodeBrushes;
            _view.NodeVisuals = _nodeVisuals;
            _view.NodeIcons = _nodeIcons;
            _view.ShowLabels = ShowLabels;
            _view.ShowIcons = ShowIcons;
            BuildLegend();
            _view.SetBuilder(_builder);
            RefreshCounts();
            RefreshTopologySafetySummary();
        }

        // ---------- load ----------
        public void NewGrid()
        {
            _builder = new SphereGridLayoutBuilder { DisplayName = "New Sphere Grid" };
            _kind = GridKind.New;
            SourceLabel = Strings.F2_new_grid_from_scratch_add_cluster_s_and__3d41e08d;
            ApplyBuilderToView();
        }

        public void OpenGrid(GridKind kind)
        {
            (string layout, string contents, string display) = kind switch
            {
                GridKind.Original => ("dat01.dat", "dat09.dat", "Original Sphere Grid"),
                GridKind.Standard => ("dat02.dat", "dat10.dat", "Standard Sphere Grid"),
                GridKind.Expert => ("dat03.dat", "dat11.dat", "Expert Sphere Grid"),
                _ => ("", "", ""),
            };
            if (layout.Length == 0) { NewGrid(); return; }

            string? dir = ResolveAbmapDir(layout);
            if (dir == null)
            {
                SourceLabel = string.Format(Strings.U_Sgb_NotFoundCorpus, layout);
                StatusSummary = string.Format(Strings.U_Sgb_OpenFailed, display, layout);
                return;
            }

            string layoutPath = Path.Combine(dir, layout);
            string contentsPath = Path.Combine(dir, contents);
            try
            {
                SphereGridLayoutFile grid = SphereGrid_File.ReadLayout(layoutPath, contentsPath, display);
                _builder = SphereGridLayoutBuilder.FromExisting(grid);
                _kind = kind;
                string dirtyNote = DirtyProjectGridNote(layout);
                SourceLabel = string.Format(Strings.U_Sgb_Opened, display, grid.ClusterCount, grid.NodeCount, grid.LinkCount, dir) +
                              (dirtyNote.Length > 0 ? " " + dirtyNote : "");
                StatusSummary = "";
                ApplyBuilderToView();
            }
            catch (Exception ex)
            {
                SourceLabel = string.Format(Strings.U_Sgb_ReadLayoutFailed, layout, ex.Message);
                StatusSummary = string.Format(Strings.U_Sgb_ReadLayoutError, ex.Message);
            }
        }

        private void ApplyBuilderToView()
        {
            _view?.SetBuilder(_builder);
            if (_view != null) _view.NewNodeContentProvider = ResolveContentForApply;
            _view?.FitToContent();
            HasSelection = false;
            ValidationSummary = "";
            StatusSummary = "";
            _prevTotal = _builder.NodeCount + _builder.LinkCount; // avoid a sound storm on load
            _prevRisky = false;
            LiveRiskWarning = "";
            RefreshCounts();
            RefreshTopologySafetySummary();
        }

        // ---------- mode ----------
        public void SetModeSelect() { if (_view != null) _view.CurrentMode = SphereGridCanvasView.Mode.Select; ModeLabel = Strings.U_Sgb_ModeSelect; ActiveToolMode = ToolMode.Select; }
        public void SetModeAddNode()
        {
            if (_builder.IsSeededFromExisting && !SquareModeEnabled)
            {
                StatusSummary = Strings.U_Sgb_AddNodeSquareMode;
                SetModeSelect();
                return;
            }
            if (_view != null) _view.CurrentMode = SphereGridCanvasView.Mode.AddNode;
            ModeLabel = Strings.F2_mode_node_click_on_empty_area_e9bfc848;
            ActiveToolMode = ToolMode.AddNode;
        }
        public void SetModeAddLink() { if (_view != null) _view.CurrentMode = SphereGridCanvasView.Mode.AddLink; ModeLabel = Strings.U_Sgb_ModeAddLink; ActiveToolMode = ToolMode.AddLink; }
        public void FitView() => _view?.FitToContent();

        partial void OnActiveClusterTextChanged(string value)
        {
            if (_view != null && ushort.TryParse((value ?? "").Trim(), out ushort c)) _view.ActiveCluster = c;
        }

        partial void OnSnapEnabledChanged(bool value)
        {
            if (_view != null) { _view.SnapStep = value ? 43 : 0; _view.InvalidateVisual(); }
        }

        partial void OnShowLabelsChanged(bool value)
        {
            if (_view != null) { _view.ShowLabels = value; _view.InvalidateVisual(); }
        }

        partial void OnShowIconsChanged(bool value)
        {
            if (_view != null) { _view.ShowIcons = value; _view.InvalidateVisual(); }
        }

        // ---------- view callbacks ----------
        private void OnGridChanged()
        {
            int total = _builder.NodeCount + _builder.LinkCount;
            if (_prevTotal >= 0 && total != _prevTotal)
            {
                bool up = total > _prevTotal;
                TryPlay(a => { if (up) a.PlayConfirm(); else a.PlayAlternative(); }); // add vs remove
            }
            _prevTotal = total;

            RefreshCounts();
            RefreshTopologySafetySummary();
            if (_view?.SelectedNode is int idx) PopulateSelection(idx); // drag updates PosX/PosY live
            else { HasSelection = false; SelectedNodeLinks.Clear(); }
        }

        private void OnNodeSelected(int? index)
        {
            if (index is int idx) { PopulateSelection(idx); TryPlay(a => a.PlayListSelection()); }
            else { HasSelection = false; SelIndexLabel = "—"; SelectedNodeLinks.Clear(); }
        }

        private void PopulateSelection(int idx)
        {
            if (idx < 0 || idx >= _builder.NodeCount) { HasSelection = false; SelectedNodeLinks.Clear(); return; }
            SphereGridNodeEntry n = _builder.Nodes[idx];
            _syncingSel = true;
            HasSelection = true;
            SelIndexLabel = $"#{idx}";
            SelPosX = n.PosX.ToString(CultureInfo.InvariantCulture);
            SelPosY = n.PosY.ToString(CultureInfo.InvariantCulture);
            SelCluster = n.Cluster.ToString(CultureInfo.InvariantCulture);
            SelContent = n.ContentIndex == SphereGridLayoutBuilder.EmptyContent ? "FF" : n.ContentIndex.ToString("X2", CultureInfo.InvariantCulture);
            SelectedNodeTypeOption = FindOrAddNodeTypeOption(n.ContentIndex);
            _syncingSel = false;
            RebuildSelectedNodeLinks();
        }

        private void RebuildSelectedNodeLinks()
        {
            SelectedNodeLinks.Clear();
            if (_view?.SelectedNode is not int s) return;
            IReadOnlyList<SphereGridLinkEntry> links = _builder.Links;
            for (int i = 0; i < links.Count; i++)
            {
                SphereGridLinkEntry l = links[i];
                int other = l.Node1 == s ? l.Node2 : (l.Node2 == s ? l.Node1 : -1);
                if (other < 0) continue;
                string anc = l.AnchorNode != 0xFFFF ? $" (curva via {l.AnchorNode})" : "";
                SelectedNodeLinks.Add(new NodeLinkRow { LinkIndex = i, Display = string.Format(Strings.U_Sgb_LinkToNode, other, anc) });
            }
        }

        [RelayCommand]
        private void RemoveLinkByIndex(int linkIndex)
        {
            try
            {
                _builder.RemoveLink(linkIndex);
                StatusSummary = string.Format(Strings.U_Sgb_LinkRemoved, linkIndex);
                RefreshCounts();
                RefreshTopologySafetySummary();
                RebuildSelectedNodeLinks();
                _view?.InvalidateVisual();
            }
            catch (Exception ex) { StatusSummary = Strings.U_Sgb_RemoveLinkFailed + ex.Message; }
        }

        public void AddLinkFromPanel()
        {
            if (_view?.SelectedNode is not int s) { StatusSummary = Strings.F2_no_node_selected_3510e5f5; return; }
            if (!int.TryParse((AddLinkTargetText ?? "").Trim(), out int target) || target < 0 || target >= _builder.NodeCount || target == s)
            { StatusSummary = string.Format(Strings.U_Sgb_InvalidTarget, _builder.NodeCount - 1, s); return; }
            try
            {
                bool isExpert = _kind == GridKind.Expert;
                int linkCap = SphereGridGameCompatibility.GetGameCompatibleLinkLimit(isExpert);
                if (_builder.LinkCount >= linkCap)
                {
                    StatusSummary = string.Format(Strings.U_Sgb_LinkLimitReached, _builder.LinkCount, linkCap);
                    return;
                }
                if (!SphereGridGameCompatibility.EndpointsHaveUsableCapacity(_builder.Links, s, target))
                {
                    StatusSummary = string.Format(Strings.U_Sgb_LinksCap, SphereGridGameCompatibility.MaximumUsableLinksPerNode);
                    return;
                }
                _builder.AddLink((ushort)s, (ushort)target);
                StatusSummary = string.Format(Strings.U_Sgb_LinkCreated, s, target);
                AddLinkTargetText = "";
                RefreshCounts();
                RefreshTopologySafetySummary();
                RebuildSelectedNodeLinks();
                _view?.InvalidateVisual();
            }
            catch (Exception ex) { StatusSummary = Strings.U_Sgb_CreateLinkFailed + ex.Message; }
        }

        // ---------- edit selected node from the side panel ----------
        public void ApplyNode()
        {
            if (_view?.SelectedNode is not int idx) { StatusSummary = Strings.F2_no_node_selected_3510e5f5; return; }
            if (!short.TryParse(SelPosX?.Trim(), out short px) || !short.TryParse(SelPosY?.Trim(), out short py)) { StatusSummary = Strings.U_Sgb_InvalidPos; return; }
            if (!ushort.TryParse(SelCluster?.Trim(), out ushort cluster)) { StatusSummary = Strings.U_Sgb_InvalidCluster; return; }
            if (cluster >= _builder.ClusterCount) { StatusSummary = string.Format(Strings.U_Sgb_ClusterNotExist, cluster, _builder.ClusterCount); return; }
            int content = ResolveContentForApply();
            if (content < 0)
                return;
            try
            {
                _builder.UpdateNode(idx, px, py, cluster, content);
                StatusSummary = string.Format(Strings.U_Sgb_NodeUpdated, idx);
                _view?.InvalidateVisual();
                RefreshCounts();
                RefreshTopologySafetySummary();
            }
            catch (Exception ex) { StatusSummary = Strings.U_Sgb_UpdateFailed + ex.Message; }
        }

        public void RemoveSelectedNode()
        {
            if (_view?.SelectedNode is not int idx) { StatusSummary = Strings.F2_no_node_selected_3510e5f5; return; }
            if (_builder.IsSeededFromExisting && idx < _builder.SeededNodeCount && !SquareModeEnabled)
            {
                StatusSummary = Strings.U_Sgb_RemoveSeededSquare;
                return;
            }
            try
            {
                _builder.RemoveNode(idx);
                _view?.SelectNode(null);
                StatusSummary = string.Format(Strings.U_Sgb_NodeRemoved, idx);
                RefreshCounts();
                RefreshTopologySafetySummary();
                _view?.InvalidateVisual();
            }
            catch (Exception ex) { StatusSummary = Strings.U_Sgb_RemoveFailed + ex.Message; }
        }

        // ---------- validate / save ----------
        public void Validate()
        {
            SphereGridBuildValidation v = _builder.Validate();
            string baseMsg = v.IsValid
                ? string.Format(Strings.U_Sgb_ValidByteSafe, _builder.ClusterCount, _builder.NodeCount, _builder.LinkCount)
                : string.Format(Strings.U_Sgb_ValidateErrors, v.Errors.Count, v.Summary);

            List<string> notes = new();
            if (_builder.HasRuntimeUnprovenTopologyCountChange)
            {
                bool isExpert = _kind == GridKind.Expert;
                if (SphereGridDeployPolicy.HasGameCompatibleCounts(_builder, isExpert))
                    notes.Add(string.Format(Strings.U_Sgb_DeploySafeCaps, SphereGridGameCompatibility.MaximumGameCompatibleNodes, SphereGridGameCompatibility.GetGameCompatibleLinkLimit(isExpert)));
                else
                    notes.Add(string.Format(Strings.U_Sgb_DeployUnsafeCaps, SphereGridGameCompatibility.MaximumGameCompatibleNodes, SphereGridGameCompatibility.GetGameCompatibleLinkLimit(isExpert)));
            }
            else if (_builder.IsSeededFromExisting)
                notes.Add(Strings.F2_safe_transplant_mode_counts_preserved_un_d6fae57d);
            string prox = CheckProximity(); if (prox.Length > 0) notes.Add(prox);
            string cross = CheckCrossings(); if (cross.Length > 0) notes.Add(cross);
            string conn = CheckConnectivity(); if (conn.Length > 0) notes.Add(conn);
            string degree = CheckLinkDegreeAdvisory(); if (degree.Length > 0) notes.Add(degree);

            ValidationSummary = notes.Count == 0
                ? baseMsg + Strings.U_Sgb_NoEngineRisks
                : baseMsg + "  " + string.Join("  ", notes);
        }

        // Advisory: a node with more than 5 connected links displays the extra links but they cannot be
        // used for movement/activation in-game (game-compatible cap). Not a hard block (vanilla grids are
        // NOT re-signed), but surfaced so the user keeps every connection usable.
        private string CheckLinkDegreeAdvisory()
        {
            if (_builder.LinkCount == 0) return "";
            IReadOnlyList<SphereGridNodeEntry> nodes = _builder.Nodes;
            List<int> overloaded = new();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (SphereGridGameCompatibility.LinkDegree(_builder.Links, i) > SphereGridGameCompatibility.MaximumUsableLinksPerNode)
                    overloaded.Add(i);
            }
            return overloaded.Count == 0
                ? ""
                : string.Format(Strings.U_Sgb_OverloadedLinks, overloaded.Count, SphereGridGameCompatibility.MaximumUsableLinksPerNode, string.Join(", #", overloaded.Take(5)), (overloaded.Count > 5 ? "…" : ""));
        }

        // The FFX engine breaks the grid when nodes sit too close together (PROVEN in-game by Halyson). Range/byte
        // validation can't see this, so warn (not block) if any two nodes are closer than the vanilla minimum
        // spacing (~43un; flag below ~40). Advisory — the lib still writes a byte-safe file.
        private string CheckProximity()
        {
            const double minGap = 40.0, minGap2 = minGap * minGap;
            IReadOnlyList<SphereGridNodeEntry> nodes = _builder.Nodes;
            int worstA = -1, worstB = -1; double worst2 = double.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    double dx = nodes[i].PosX - nodes[j].PosX, dy = nodes[i].PosY - nodes[j].PosY;
                    double d2 = dx * dx + dy * dy;
                    if (d2 < worst2) { worst2 = d2; worstA = i; worstB = j; }
                }
            if (worstA < 0 || worst2 >= minGap2) return "";
            return string.Format(Strings.U_Sgb_ProximityRisk, worstA, worstB, Math.Sqrt(worst2), minGap);
        }

        // The engine breaks if links CROSS. Vanilla grids have ZERO straight-segment crossings (measured on dat02),
        // so any crossing in an edited grid is user-introduced = a real risk. Counts proper intersections between
        // NON-adjacent links (links sharing a node just touch at an endpoint, which is not a crossing).
        private string CheckCrossings()
        {
            IReadOnlyList<SphereGridNodeEntry> nodes = _builder.Nodes;
            IReadOnlyList<SphereGridLinkEntry> links = _builder.Links;
            int nc = nodes.Count, count = 0, exA = -1, exB = -1;
            for (int i = 0; i < links.Count; i++)
            {
                SphereGridLinkEntry a = links[i];
                if (a.Node1 >= nc || a.Node2 >= nc) continue;
                int ax = nodes[a.Node1].PosX, ay = nodes[a.Node1].PosY, bx = nodes[a.Node2].PosX, by = nodes[a.Node2].PosY;
                for (int j = i + 1; j < links.Count; j++)
                {
                    SphereGridLinkEntry b = links[j];
                    if (b.Node1 >= nc || b.Node2 >= nc) continue;
                    if (a.Node1 == b.Node1 || a.Node1 == b.Node2 || a.Node2 == b.Node1 || a.Node2 == b.Node2) continue;
                    int cx = nodes[b.Node1].PosX, cy = nodes[b.Node1].PosY, dx2 = nodes[b.Node2].PosX, dy2 = nodes[b.Node2].PosY;
                    if (ProperCross(ax, ay, bx, by, cx, cy, dx2, dy2)) { count++; if (exA < 0) { exA = i; exB = j; } }
                }
            }
            return count == 0 ? "" : string.Format(Strings.U_Sgb_CrossingRisk, count, exA, exB);
        }

        private static int Orient(int ax, int ay, int bx, int by, int cx, int cy)
        { long val = (long)(bx - ax) * (cy - ay) - (long)(by - ay) * (cx - ax); return val > 0 ? 1 : (val < 0 ? -1 : 0); }

        private static bool ProperCross(int ax, int ay, int bx, int by, int cx, int cy, int dx, int dy)
        {
            int d1 = Orient(cx, cy, dx, dy, ax, ay), d2 = Orient(cx, cy, dx, dy, bx, by);
            int d3 = Orient(ax, ay, bx, by, cx, cy), d4 = Orient(ax, ay, bx, by, dx, dy);
            return d1 != 0 && d2 != 0 && d3 != 0 && d4 != 0 && d1 != d2 && d3 != d4;
        }

        // INFO (NOT a crash signal — vanilla Expert ships 25 components / 23 link-less nodes): report disconnection
        // so the author knows an island won't be walkable from the main grid unless they bridge it.
        private string CheckConnectivity()
        {
            int n = _builder.NodeCount;
            if (n == 0) return "";
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            int[] deg = new int[n];
            foreach (SphereGridLinkEntry l in _builder.Links)
            {
                if (l.Node1 >= n || l.Node2 >= n) continue;
                deg[l.Node1]++; deg[l.Node2]++;
                int ra = Find(l.Node1), rb = Find(l.Node2);
                if (ra != rb) parent[ra] = rb;
            }
            HashSet<int> comps = new(); int isolated = 0;
            for (int i = 0; i < n; i++) { comps.Add(Find(i)); if (deg[i] == 0) isolated++; }
            if (comps.Count <= 1 && isolated == 0) return "";
            return string.Format(Strings.U_Sgb_Disconnected, comps.Count, isolated);
        }

        public void SaveCopy()
        {
            SphereGridBuildValidation v = _builder.Validate();
            if (!v.IsValid) { ValidationSummary = $"❌ {v.Summary}"; StatusSummary = Strings.F2_fix_the_errors_validate_before_saving_67d42ae1; return; }
            try
            {
                _builder.DisplayName = string.IsNullOrWhiteSpace(_builder.DisplayName) ? "sphere_grid" : _builder.DisplayName;
                SphereGridLayoutFile grid = _builder.Build();
                string dir = Path.Combine(ResolveOutDir(), "sphere_build");
                Directory.CreateDirectory(dir);
                string baseName = Sanitize(_builder.DisplayName);
                string layoutFile = Path.Combine(dir, baseName + ".dat");
                string contentsFile = Path.Combine(dir, baseName + ".contents.dat");
                File.WriteAllBytes(layoutFile, grid.RawLayoutBytes);
                File.WriteAllBytes(contentsFile, grid.RawContentsBytes);
                string appendNote = _builder.HasRuntimeUnprovenTopologyAppend
                    ? (SphereGridDeployPolicy.HasGameCompatibleCounts(_builder, _kind == GridKind.Expert)
                        ? Strings.U_Sgb_SafeDeployNote
                        : Strings.U_Sgb_UnsafeDeployNote)
                    : "";
                StatusSummary = string.Format(Strings.U_Sgb_CopySaved, layoutFile, grid.RawLayoutBytes.Length, grid.RawContentsBytes.Length, appendNote);
            }
            catch (Exception ex) { StatusSummary = Strings.U_Sgb_SaveCopyFailed + ex.Message; }
        }

        public void SaveToProject()
        {
            if (_kind == GridKind.New) { StatusSummary = Strings.F2_new_grid_has_no_target_in_the_project_us_ba5d44d0; return; }
            if (!Project_Service.Instance.IsProjectLoaded) { StatusSummary = Strings.F2_no_project_loaded_use_save_as_922baf6a; return; }
            SphereGridBuildValidation v = _builder.Validate();
            if (!v.IsValid) { ValidationSummary = $"❌ {v.Summary}"; StatusSummary = Strings.F2_fix_the_errors_validate_before_saving_67d42ae1; return; }
            bool isExpert = _kind == GridKind.Expert;
            if (!SphereGridDeployPolicy.CanDeployToProject(_builder, isExpert))
            {
                ValidationSummary = SphereGridDeployPolicy.BlockReason(_builder, isExpert);
                StatusSummary = Strings.U_Sgb_DeployBlocked;
                return;
            }

            (string layout, string contents) = LayoutContentsFor(_kind);
            try
            {
                SphereGridLayoutFile grid = _builder.Build();
                string dir = Project_Service.Instance.Path_Abmap;
                Directory.CreateDirectory(dir);

                // Atomic save: write to .tmp, re-read verify, File.Move, rollback on failure.
                // File.Move on the same NTFS volume is atomic — either the whole file lands
                // or nothing changes. Originals are held in memory for rollback.
                string layoutPath = Path.Combine(dir, layout);
                string contentsPath = Path.Combine(dir, contents);
                string layoutTmp = layoutPath + ".tmp";
                string contentsTmp = contentsPath + ".tmp";

                // Stage
                File.WriteAllBytes(layoutTmp, grid.RawLayoutBytes);
                File.WriteAllBytes(contentsTmp, grid.RawContentsBytes);

                // Re-read verify
                SphereGrid_File.ReadLayout(layoutTmp, contentsTmp, grid.DisplayName);

                // Hold originals for rollback
                byte[] originalLayout = File.Exists(layoutPath) ? File.ReadAllBytes(layoutPath) : Array.Empty<byte>();
                byte[] originalContents = File.Exists(contentsPath) ? File.ReadAllBytes(contentsPath) : Array.Empty<byte>();

                // Install atomically
                bool layoutInstalled = false;
                bool contentsInstalled = false;
                try
                {
                    File.Move(layoutTmp, layoutPath, overwrite: true);
                    layoutInstalled = true;
                    File.Move(contentsTmp, contentsPath, overwrite: true);
                    contentsInstalled = true;
                }
                catch (Exception installError)
                {
                    try
                    {
                        if (layoutInstalled && originalLayout.Length > 0)
                            File.WriteAllBytes(layoutPath, originalLayout);
                        if (contentsInstalled && originalContents.Length > 0)
                            File.WriteAllBytes(contentsPath, originalContents);
                    }
                    catch (Exception rollbackError)
                    {
                        throw new IOException(
                            Strings.U_Sgb_RollbackFailed +
                            $"Layout: {layoutPath}. Contents: {contentsPath}.",
                            new AggregateException(installError, rollbackError));
                    }
                    throw new IOException(
                        string.Format(Strings.U_Sgb_RestoredAfterFail, installError.Message));
                }
                finally
                {
                    TryDelete(layoutTmp);
                    TryDelete(contentsTmp);
                }

                _builder = SphereGridLayoutBuilder.FromExisting(grid);
                ApplyBuilderToView();
                StatusSummary = string.Format(Strings.U_Sgb_SavedProject, layout, contents, dir);
            }
            catch (Exception ex) { StatusSummary = Strings.U_Sgb_SaveProjectFailed + ex.Message; }
        }

        partial void OnSquareModeEnabledChanged(bool value) => RefreshTopologySafetySummary();

        /// <summary>Recover the project's grid to the pristine VANILLA reference (the extracted original), undoing a bad save.
        /// The extraction reference is read-only and never overwritten by the editor.</summary>
        public void RestoreFromReference()
        {
            if (_kind == GridKind.New) { StatusSummary = Strings.U_Sgb_NewGridNoOriginal; return; }
            if (!Project_Service.Instance.IsProjectLoaded) { StatusSummary = Strings.F2_no_project_loaded_nothing_to_restore_4f2f716c; return; }
            string? refDir = ResolveReferenceAbmapDir();
            if (refDir == null) { StatusSummary = Strings.U_Sgb_OriginalNotFound; return; }
            (string layout, string contents) = LayoutContentsFor(_kind);
            try
            {
                string projDir = Project_Service.Instance.Path_Abmap;
                Directory.CreateDirectory(projDir);
                File.Copy(Path.Combine(refDir, layout), Path.Combine(projDir, layout), overwrite: true);
                File.Copy(Path.Combine(refDir, contents), Path.Combine(projDir, contents), overwrite: true);
                StatusSummary = string.Format(Strings.U_Sgb_Restored, layout, contents);
                OpenGrid(_kind);
            }
            catch (Exception ex) { StatusSummary = Strings.U_Sgb_RestoreFailed + ex.Message; }
        }

        private static (string layout, string contents) LayoutContentsFor(GridKind kind) => kind switch
        {
            GridKind.Original => ("dat01.dat", "dat09.dat"),
            GridKind.Standard => ("dat02.dat", "dat10.dat"),
            GridKind.Expert => ("dat03.dat", "dat11.dat"),
            _ => ("", ""),
        };

        private static string? ResolveReferenceAbmapDir()
        {
            return ResolveReferenceAbmapDir("dat01.dat");
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* harmless; will be overwritten next save */ }
        }

        private void RefreshCounts()
        {
            CountsSummary = string.Format(Strings.U_Sgb_CountsLabel, _builder.ClusterCount, _builder.NodeCount, _builder.LinkCount);
            GameCompatibilitySummary =
                SphereGridGameCompatibility.NodeCapacityText(_builder.NodeCount) +
                "  ·  " + SphereGridGameCompatibility.LinkCapacityText(_builder.LinkCount, _kind == GridKind.Expert);
        }

        private void RefreshTopologySafetySummary()
        {
            if (!_builder.IsSeededFromExisting)
            {
                TopologySafetySummary = Strings.U_Sgb_LabSaveCopy;
                return;
            }

            if (!_builder.HasRuntimeUnprovenTopologyCountChange)
            {
                TopologySafetySummary = Strings.F2_safe_transplant_mode_original_counts_pre_8ec30885;
                return;
            }

            bool isExpert = _kind == GridKind.Expert;
            if (SphereGridDeployPolicy.HasGameCompatibleCounts(_builder, isExpert))
            {
                TopologySafetySummary = Strings.U_Sgb_GameCompatibleTopology +
                    string.Format(Strings.U_Sgb_SafeDeploy, SphereGridGameCompatibility.MaximumGameCompatibleNodes, SphereGridGameCompatibility.GetGameCompatibleLinkLimit(isExpert));
                return;
            }

            TopologySafetySummary = _builder.AllowRuntimeTestDeploy
                ? string.Format(Strings.U_Sgb_Rt2TestDeploy, _builder.SeededClusterCount, _builder.SeededNodeCount, _builder.SeededLinkCount, _builder.ClusterCount, _builder.NodeCount, _builder.LinkCount)
                : string.Format(Strings.U_Sgb_NotGameCompatible, _builder.SeededClusterCount, _builder.SeededNodeCount, _builder.SeededLinkCount, _builder.ClusterCount, _builder.NodeCount, _builder.LinkCount);
        }

        private static void TryPlay(Action<AudioStudio_Service> play)
        {
            try { play(AudioStudio_Service.Instance); } catch { /* audio optional */ }
        }

        private static IBrush BrushOf(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

        // Category color from the node-type name (+ learned-move flag for abilities). Pure heuristic on the US name.
        private static IBrush CategorizeBrush(string name, ushort learnedMove)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("lock")) return BrushOf(0x9A, 0x6A, 0x6A);
            if (learnedMove != 0) return BrushOf(0xE8, 0xD2, 0x4F);     // ability / learned move
            if (n.Contains("hp")) return BrushOf(0x5F, 0xB8, 0x5F);
            if (n.Contains("mp")) return BrushOf(0x5F, 0x8F, 0xD8);
            if (n.Contains("magic def")) return BrushOf(0x5F, 0xC9, 0xB0);
            if (n.Contains("magic")) return BrushOf(0xA0, 0x5F, 0xD8);
            if (n.Contains("strength")) return BrushOf(0xD8, 0x5F, 0x5F);
            if (n.Contains("defense")) return BrushOf(0xD8, 0x9A, 0x4F);
            if (n.Contains("agility")) return BrushOf(0xD8, 0xC9, 0x5F);
            if (n.Contains("accuracy")) return BrushOf(0x5F, 0xD8, 0xD8);
            if (n.Contains("evasion")) return BrushOf(0x9F, 0xD8, 0x5F);
            if (n.Contains("luck")) return BrushOf(0xE8, 0xC9, 0x3A);
            return BrushOf(0x3F, 0xA9, 0xC9); // other
        }

        // Build the node visual exactly like the Explorer (atlas sprite / short badge by AppearanceType), so the
        // Canvas matches the proven SphereGridPreview_Control look using the same icon_atlas.png.
        private static SphereGridNodeVisualInfo CreateVisual(int contentIndex, string displayName, ushort appearanceType)
        {
            int lockLevel = appearanceType switch { 0x12 => 1, 0x11 => 2, 0x00 => 3, 0x10 => 4, _ => 0 };
            return new SphereGridNodeVisualInfo
            {
                ContentIndex = contentIndex,
                AppearanceType = appearanceType,
                DisplayName = displayName,
                ShortLabel = BuildShortLabel(displayName, appearanceType, lockLevel),
                IsEmpty = appearanceType == 0x01,
                IsLock = lockLevel > 0,
                LockLevel = lockLevel,
            };
        }

        private static string BuildShortLabel(string displayName, ushort appearanceType, int lockLevel)
        {
            if (lockLevel > 0) return $"L{lockLevel}";
            if (appearanceType == 0x01) return "--";
            string n = displayName ?? "";
            if (n.StartsWith("HP", StringComparison.OrdinalIgnoreCase)) return "HP";
            if (n.StartsWith("MP", StringComparison.OrdinalIgnoreCase)) return "MP";
            if (n.StartsWith("Strength", StringComparison.OrdinalIgnoreCase)) return "STR";
            if (n.StartsWith("Magic Defense", StringComparison.OrdinalIgnoreCase)) return "MDF";
            if (n.StartsWith("Magic", StringComparison.OrdinalIgnoreCase)) return "MAG";
            if (n.StartsWith("Defense", StringComparison.OrdinalIgnoreCase)) return "DEF";
            if (n.StartsWith("Accuracy", StringComparison.OrdinalIgnoreCase)) return "ACC";
            if (n.StartsWith("Evasion", StringComparison.OrdinalIgnoreCase)) return "EVA";
            if (n.StartsWith("Luck", StringComparison.OrdinalIgnoreCase)) return "LCK";
            if (n.StartsWith("Agility", StringComparison.OrdinalIgnoreCase)) return "AGI";
            return appearanceType switch
            {
                0x0C => "WHT", 0x0D => "BLK", 0x0E => "SPL", 0x0F => "SKL",
                _ => n.Length <= 4 ? n.ToUpperInvariant() : n[..4].ToUpperInvariant()
            };
        }

        private static readonly Dictionary<string, Geometry> _iconGeometryCache = new();
        private static Geometry? ParseIcon(string pathData)
        {
            if (_iconGeometryCache.TryGetValue(pathData, out Geometry? g)) return g;
            try { g = Geometry.Parse(pathData); } catch { g = null; }
            if (g != null) _iconGeometryCache[pathData] = g;
            return g;
        }

        // game-icons.net vector glyph (CC BY) by category from the node name (+ learned-move). null = orb/text-badge only.
        // Locks are drawn as "L{n}" by the view (IsLock), so they don't need an icon here.
        private static Geometry? CategorizeIcon(string name, ushort learnedMove)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (learnedMove != 0) return ParseIcon(SphereGridIcons.Ability);
            if (n.StartsWith("hp")) return ParseIcon(SphereGridIcons.Hp);
            if (n.StartsWith("mp")) return ParseIcon(SphereGridIcons.Mp);
            if (n.StartsWith("strength")) return ParseIcon(SphereGridIcons.Strength);
            if (n.StartsWith("magic defense")) return ParseIcon(SphereGridIcons.Defense);
            if (n.StartsWith("magic")) return ParseIcon(SphereGridIcons.Magic);
            if (n.StartsWith("defense")) return ParseIcon(SphereGridIcons.Defense);
            if (n.StartsWith("agility")) return ParseIcon(SphereGridIcons.Agility);
            if (n.StartsWith("luck")) return ParseIcon(SphereGridIcons.Luck);
            return null;
        }

        private void BuildLegend()
        {
            if (Legend.Count > 0) return;
            (string label, IBrush brush)[] rows =
            {
                ("HP", BrushOf(0x5F,0xB8,0x5F)), ("MP", BrushOf(0x5F,0x8F,0xD8)),
                (Strings.U_Sgb_StatStrength, BrushOf(0xD8,0x5F,0x5F)), (Strings.U_Sgb_StatDefense, BrushOf(0xD8,0x9A,0x4F)),
                (Strings.U_Sgb_LegendMagic, BrushOf(0xA0,0x5F,0xD8)), (Strings.U_Sgb_LegendMagicDef, BrushOf(0x5F,0xC9,0xB0)),
                (Strings.U_Sgb_LegendAgility, BrushOf(0xD8,0xC9,0x5F)), (Strings.U_Sgb_LegendAccuracy, BrushOf(0x5F,0xD8,0xD8)),
                (Strings.U_Sgb_StatEvasion, BrushOf(0x9F,0xD8,0x5F)), (Strings.U_Sgb_StatLuck, BrushOf(0xE8,0xC9,0x3A)),
                (Strings.U_Sgb_StatSkill, BrushOf(0xE8,0xD2,0x4F)), (Strings.U_Sgb_StatLock, BrushOf(0x9A,0x6A,0x6A)),
                (Strings.U_Sgb_LegendEmpty, BrushOf(0x3A,0x40,0x4A)), (Strings.F2_other_6e6a6f20, BrushOf(0x3F,0xA9,0xC9)),
            };
            foreach ((string label, IBrush brush) in rows) Legend.Add(new LegendRow { Label = label, Swatch = brush });
        }

        int ResolveContentForApply()
        {
            if (SelectedNodeTypeOption is { IsSeparator: true })
            {
                StatusSummary = Strings.U_Sgb_PickNodeType;
                return -1;
            }

            if (SelectedNodeTypeOption?.CommandId is int cmdId)
            {
                if (_commandIdToContentIndex.TryGetValue(cmdId, out int mapped))
                    return mapped;

                if (TryGrowPanelForCommand(cmdId, out mapped))
                    return mapped;

                string name = _commandNames.TryGetValue(cmdId, out string? n) ? n : $"#{cmdId}";
                StatusSummary =
                    string.Format(Strings.U_Sgb_NoNodeType, cmdId, name, (CommandCategoryNibble | cmdId));
                return -1;
            }

            if (SelectedNodeTypeOption != null)
                return SelectedNodeTypeOption.Index;

            return ParseContent(SelContent);
        }

        // Keep the advanced hex box in sync when the user picks a node type or command by name.
        partial void OnSelectedNodeTypeOptionChanged(NodeTypeOption? value)
        {
            if (_syncingSel || value == null || value.IsSeparator)
                return;

            if (value.CommandId is int cmdId && _commandIdToContentIndex.TryGetValue(cmdId, out int mapped))
            {
                SelContent = mapped == SphereGridLayoutBuilder.EmptyContent ? "FF" : mapped.ToString("X2", CultureInfo.InvariantCulture);
                return;
            }

            if (value.CommandId is int unresolved)
            {
                StatusSummary = string.Format(Strings.U_Sgb_NodeTypeMissing, value.Display, CommandCategoryNibble | unresolved);
                return;
            }

            SelContent = value.Index == SphereGridLayoutBuilder.EmptyContent ? "FF" : value.Index.ToString("X2", CultureInfo.InvariantCulture);
        }

        // ---------- node-type table (panel.bin) + command.bin teach picker ----------
        private void EnsureNodeTypesLoaded()
        {
            if (_nodeTypesLoaded) return;
            _nodeTypesLoaded = true;
            _commandNames.Clear();
            _commandIdToContentIndex.Clear();
            _contentIndexToLearnedMove.Clear();
            LoadCommandNames();

            try
            {
                (string jp, string? us)? paths = ResolvePanelPaths();
                if (paths != null)
                {
                    SphereGridNodeTypeTable table = SphereGrid_File.ReadNodeTypes(paths.Value.jp, paths.Value.us);
                    foreach (SphereGridNodeTypeEntry e in table.Entries)
                    {
                        string name = !string.IsNullOrWhiteSpace(e.Name.UsText) ? e.Name.UsText
                                    : !string.IsNullOrWhiteSpace(e.Name.JpText) ? e.Name.JpText
                                    : $"NodeType {e.Index:X2}h";
                        _nodeTypeNames[e.Index] = name;
                        _nodeBrushes[e.Index] = CategorizeBrush(name, e.LearnedMove);
                        _nodeVisuals[e.Index] = CreateVisual(e.Index, name, e.AppearanceType);
                        Geometry? gi = CategorizeIcon(name, e.LearnedMove);
                        if (gi != null) _nodeIcons[e.Index] = gi;

                        if (e.LearnedMove != 0)
                        {
                            _contentIndexToLearnedMove[e.Index] = e.LearnedMove;
                            int cmdId = e.LearnedMove & 0xFFF;
                            if (!_commandIdToContentIndex.ContainsKey(cmdId))
                                _commandIdToContentIndex[cmdId] = e.Index;
                        }
                    }
                }
            }
            catch { /* panel optional; command list may still populate */ }

            RebuildNodeTypeOptions();
        }

        void LoadCommandNames()
        {
            _commandNames.Clear();
            _jpCommandNames.Clear();
            LoadCommandNamesFromPath(ResolveCommandBinPath(), FfxEncoding.UsDecoder, _commandNames);
            LoadCommandNamesFromPath(ResolveJpCommandBinPath(), FfxEncoding.JpDecoder, _jpCommandNames);
        }

        static void LoadCommandNamesFromPath(string? path, Dictionary<byte, char> decoder, Dictionary<int, string> target)
        {
            if (path == null || !File.Exists(path))
                return;

            try
            {
                List<Ability_Command> commands = Ability_Command.ReadList(File.ReadAllBytes(path), hasExtraInfo: true);
                for (int i = 0; i < commands.Count; i++)
                {
                    string name = FfxEncoding.DecodeScript(commands[i].NameScriptBytes).GetString(decoder);
                    target[i] = string.IsNullOrWhiteSpace(name) ? "-" : name;
                }
            }
            catch { /* hex fallback remains */ }
        }

        void RebuildNodeTypeOptions()
        {
            int? previousCommand = SelectedNodeTypeOption?.CommandId;
            int? previousIndex = SelectedNodeTypeOption?.Index;

            NodeTypeOptions.Clear();

            List<NodeTypeOption> panelRows = _nodeTypeNames
                .OrderBy(pair => pair.Key)
                .Select(pair => new NodeTypeOption
                {
                    Index = pair.Key,
                    Display = $"{pair.Key:X2}h · {pair.Value}",
                })
                .ToList();

            if (!panelRows.Any(o => o.Index == SphereGridLayoutBuilder.EmptyContent))
            {
                panelRows.Insert(0, new NodeTypeOption
                {
                    Index = SphereGridLayoutBuilder.EmptyContent,
                    Display = Strings.U_Sgb_EmptyType,
                });
            }

            foreach (NodeTypeOption row in panelRows)
                NodeTypeOptions.Add(row);

            if (_commandNames.Count > 0)
            {
                NodeTypeOptions.Add(new NodeTypeOption
                {
                    Index = -1,
                    Display = Strings.U_Sgb_TeachSkill,
                    IsSeparator = true,
                });

                foreach ((int cmdId, string name) in _commandNames.OrderBy(pair => pair.Key))
                {
                    bool mapped = _commandIdToContentIndex.TryGetValue(cmdId, out int contentIdx);
                    NodeTypeOptions.Add(new NodeTypeOption
                    {
                        Index = mapped ? contentIdx : SphereGridLayoutBuilder.EmptyContent,
                        CommandId = cmdId,
                        IsUnresolvedCommand = !mapped,
                        Display = mapped
                            ? $"CMD #{cmdId:D3} · {name}  →  panel {contentIdx:X2}h"
                            : string.Format(Strings.U_Sgb_NoPanelBin, cmdId, name),
                    });
                }
            }

            if (previousCommand.HasValue)
                SelectedNodeTypeOption = NodeTypeOptions.FirstOrDefault(o => o.CommandId == previousCommand.Value)
                    ?? FindOrAddNodeTypeOption(previousIndex ?? SphereGridLayoutBuilder.EmptyContent);
            else if (previousIndex.HasValue)
                SelectedNodeTypeOption = FindOrAddNodeTypeOption(previousIndex.Value);
        }

        static string? ResolveJpCommandBinPath()
        {
            Project_Service proj = Project_Service.Instance;
            if (proj.IsProjectLoaded && File.Exists(proj.Path_KernelCommand))
                return proj.Path_KernelCommand;

            string? baseDir = ResolvePortableMasterRoot("jppc", "battle", "kernel", "command.bin");
            if (baseDir == null)
                return null;
            string jp = Path.Combine(baseDir, "jppc", "battle", "kernel", "command.bin");
            return File.Exists(jp) ? jp : null;
        }

        bool TryGrowPanelForCommand(int cmdId, out int contentIndex)
        {
            contentIndex = -1;
            (string jp, string? us)? paths = ResolvePanelPaths();
            if (paths == null)
            {
                StatusSummary = "panel.bin not found — open a project or point to the extracted kernel.";
                return false;
            }

            string usName = _commandNames.TryGetValue(cmdId, out string? us) ? us : $"Command {cmdId}";
            _jpCommandNames.TryGetValue(cmdId, out string? jpName);

            try
            {
                contentIndex = SphereGridPanelGrowWriter.EnsureNodeTypeForCommand(
                    paths.Value.jp,
                    paths.Value.us,
                    cmdId,
                    usName,
                    jpName);
                ReloadNodeTypesFromDisk();
                string panelHex = contentIndex.ToString("X2", CultureInfo.InvariantCulture);
                StatusSummary = string.Format(Strings.U_Sgb_PanelGrowStatus, cmdId, panelHex, CommandCategoryNibble | cmdId);
                return true;
            }
            catch (Exception ex)
            {
                StatusSummary = string.Format(Strings.U_Sgb_PopulateFailed, cmdId, ex.Message);
                return false;
            }
        }

        void ReloadNodeTypesFromDisk()
        {
            _nodeTypesLoaded = false;
            _nodeTypeNames.Clear();
            _nodeBrushes.Clear();
            _nodeVisuals.Clear();
            _nodeIcons.Clear();
            EnsureNodeTypesLoaded();
        }

        public void PopulatePanelFromCommands()
        {
            (string jp, string? us)? paths = ResolvePanelPaths();
            if (paths == null)
            {
                StatusSummary = Strings.U_Sgb_PanelNotFound;
                return;
            }

            if (_commandNames.Count == 0)
                LoadCommandNames();

            if (_commandNames.Count == 0)
            {
                StatusSummary = "command.bin not found — nothing to map.";
                return;
            }

            try
            {
                SphereGridPanelGrowReport report = SphereGridPanelGrowWriter.PopulateMissingCommandNodeTypes(
                    paths.Value.jp,
                    paths.Value.us,
                    _commandNames,
                    _jpCommandNames.Count > 0 ? _jpCommandNames : null,
                    minCommandId: SphereGridPanelGrowWriter.DefaultBulkMinCommandId,
                    preferHighCommandIds: true);

                ReloadNodeTypesFromDisk();
                StatusSummary = report.Summary;
                if (report.Errors.Count > 0)
                    StatusSummary += " " + string.Join(" · ", report.Errors.Take(3));
            }
            catch (Exception ex)
            {
                StatusSummary = Strings.U_Sgb_PopulatePanelFailed + ex.Message;
            }
        }

        static string? ResolveCommandBinPath()
        {
            Project_Service proj = Project_Service.Instance;
            if (proj.IsProjectLoaded && File.Exists(proj.Path_KernelCommandUs))
                return proj.Path_KernelCommandUs;

            string? baseDir = ResolvePortableMasterRoot("new_uspc", "battle", "kernel", "command.bin");
            if (baseDir == null)
                return null;
            string us = Path.Combine(baseDir, "new_uspc", "battle", "kernel", "command.bin");
            return File.Exists(us) ? us : null;
        }

        private NodeTypeOption FindOrAddNodeTypeOption(int contentIndex)
        {
            NodeTypeOption? found = NodeTypeOptions.FirstOrDefault(o => !o.IsSeparator && o.CommandId == null && o.Index == contentIndex);
            if (found != null) return found;
            NodeTypeOption synth = new()
            {
                Index = contentIndex,
                Display = contentIndex == SphereGridLayoutBuilder.EmptyContent ? Strings.U_Sgb_EmptyType : string.Format(Strings.U_Sgb_SynthNoName, contentIndex),
            };
            NodeTypeOptions.Add(synth);
            return synth;
        }

        private static (string jp, string? us)? ResolvePanelPaths()
        {
            Project_Service proj = Project_Service.Instance;
            if (proj.IsProjectLoaded && File.Exists(proj.Path_KernelPanel))
                return (proj.Path_KernelPanel, File.Exists(proj.Path_KernelPanelUs) ? proj.Path_KernelPanelUs : null);
            string? baseDir = ResolvePortableMasterRoot("jppc", "battle", "kernel", "panel.bin");
            if (baseDir == null)
                return null;
            string jp = Path.Combine(baseDir, "jppc", "battle", "kernel", "panel.bin");
            string us = Path.Combine(baseDir, "new_uspc", "battle", "kernel", "panel.bin");
            if (File.Exists(jp)) return (jp, File.Exists(us) ? us : null);
            return null;
        }

        // ---------- helpers ----------
        private static int ParseContent(string? s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0 || s.Equals("FF", StringComparison.OrdinalIgnoreCase) || s.Equals("empty", StringComparison.OrdinalIgnoreCase))
                return SphereGridLayoutBuilder.EmptyContent;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
            return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v) ? v : SphereGridLayoutBuilder.EmptyContent;
        }

        /// <summary>Resolve abmap dir by checking if the SPECIFIC layout file exists (not just dat01.dat).</summary>
        private static string? ResolveAbmapDir(string layoutFile = "dat01.dat")
        {
            string? reference = ResolveReferenceAbmapDir(layoutFile);
            if (reference != null)
                return reference;
            Project_Service proj = Project_Service.Instance;
            if (proj.IsProjectLoaded && File.Exists(Path.Combine(proj.Path_Abmap, layoutFile)))
                return proj.Path_Abmap; // fallback: corpus ausente nesta máquina
            return null;
        }

        /// <summary>
        /// Resolve a clean/reference abmap from an explicit override or a configured extraction root.
        /// No machine drive is assumed; null lets callers surface their existing actionable status.
        /// </summary>
        private static string? ResolveReferenceAbmapDir(string layoutFile)
        {
            string? direct = Environment.GetEnvironmentVariable("FFX_ABMAP_ROOT");
            if (!string.IsNullOrWhiteSpace(direct))
            {
                try
                {
                    string full = Path.GetFullPath(direct);
                    if (File.Exists(Path.Combine(full, layoutFile)))
                        return full;
                }
                catch
                {
                    // Continue through the configured master/extraction roots.
                }
            }

            string? master = ResolvePortableMasterRoot("jppc", "menu", "abmap", layoutFile);
            if (master == null)
                return null;
            string abmap = Path.Combine(master, "jppc", "menu", "abmap");
            return File.Exists(Path.Combine(abmap, layoutFile)) ? abmap : null;
        }

        private static string? ResolvePortableMasterRoot(params string[] requiredRelativeSegments)
        {
            var candidates = new List<string?>
            {
                Environment.GetEnvironmentVariable("FFX_VANILLA_MASTER"),
                PortablePathResolver.MasterRoot,
            };
            string? ffxPs2 = PortablePathResolver.FfxPs2Root;
            if (!string.IsNullOrWhiteSpace(ffxPs2))
                candidates.Insert(1, Path.Combine(ffxPs2, "ffx", "master"));

            foreach (string? candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                try
                {
                    string full = Path.GetFullPath(candidate);
                    string required = full;
                    foreach (string segment in requiredRelativeSegments)
                        required = Path.Combine(required, segment);
                    if (File.Exists(required))
                        return full;
                }
                catch
                {
                    // Invalid/inaccessible configuration is an unavailable reference root.
                }
            }
            return null;
        }

        // SUPERMD §4.6: se o projeto tem o grid modificado (≠ vanilla), avisa — o autor pode estar sobre dados sujos.
        private static string DirtyProjectGridNote(string layoutFile)
        {
            Project_Service proj = Project_Service.Instance;
            if (!proj.IsProjectLoaded) return "";
            string projPath = Path.Combine(proj.Path_Abmap, layoutFile);
            string? corpusDir = ResolveReferenceAbmapDir(layoutFile);
            if (corpusDir == null || PathsEqual(corpusDir, proj.Path_Abmap)) return "";
            string corpusPath = Path.Combine(corpusDir, layoutFile);
            if (!File.Exists(projPath) || !File.Exists(corpusPath)) return "";
            try
            {
                return File.ReadAllBytes(projPath).SequenceEqual(File.ReadAllBytes(corpusPath))
                    ? ""
                    : "⚠️ the project has this grid modified (≠ vanilla) — loaded from clean corpus; use Save to apply to project.";
            }
            catch { return ""; }
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveOutDir()
        {
            string? proj = Project_Service.Instance.ProjectPath;
            return !string.IsNullOrWhiteSpace(proj) && Directory.Exists(proj) ? proj! : AppContext.BaseDirectory;
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "sphere_grid" : name;
        }
    }

    // One entry of the friendly node-type picker: panel content index and/or a command.bin teach target.
    internal sealed class NodeTypeOption
    {
        public required int Index { get; init; }
        public required string Display { get; init; }
        public int? CommandId { get; init; }
        public bool IsSeparator { get; init; }
        public bool IsUnresolvedCommand { get; init; }
        public override string ToString() => Display;
    }

    // One row of the selected node's link list (panel link manager): the link's table index + a human label.
    internal sealed class NodeLinkRow
    {
        public required int LinkIndex { get; init; }
        public required string Display { get; init; }
    }

    // One row of the node-category color legend (swatch + label).
    internal sealed class LegendRow
    {
        public required string Label { get; init; }
        public required IBrush Swatch { get; init; }
    }

    // Jarvis-UI (Sprint D 2026-06-20, OPT-D2): enum de modo de ferramenta ativo — alimenta a classe visual
    // toolActive no botão correspondente da toolbar do canvas (D2) e os botões podem comparar via binding.
    internal enum ToolMode { Select, AddNode, AddLink }
}
