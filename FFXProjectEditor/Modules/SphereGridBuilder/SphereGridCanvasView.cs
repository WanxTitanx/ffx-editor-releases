using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using FFXProjectEditor.Controls.SphereGrid;
using FFXProjectEditor.FfxLib.SphereGrid;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.SphereGridBuilder
{
    // v2 VISUAL canvas over the proven SphereGridLayoutBuilder. Custom-drawn (Render override + manual pointer
    // handling) rather than an ItemsControl of 800+ templated shapes: a shipped grid is ~828 nodes + ~848 links,
    // so binding+templating every primitive would be heavy and make per-node drag awkward. The view owns a
    // SphereGridLayoutBuilder (FromExisting for an edit, or empty for a New grid) and mutates it directly through
    // the lib's editing API (MoveNode/AddLink/AddNode), then raises GridChanged so the DataModel refreshes.
    //
    // World coords are the file's signed PosX/PosY (short, large range); a scale+offset transform maps them to
    // screen, with wheel zoom (about the cursor) and drag-pan. Node hit-radius is screen-constant so nodes stay
    // clickable at any zoom.
    internal sealed class SphereGridCanvasView : Control
    {
        public enum Mode { Select, AddLink, AddNode }

        // --- editable model ---
        private SphereGridLayoutBuilder? _builder;
        public SphereGridLayoutBuilder? Builder => _builder;

        public Mode CurrentMode { get; set; } = Mode.Select;

        // The cluster a freshly-added node is assigned to (set from the side panel; 0 by default).
        public ushort ActiveCluster { get; set; }

        // Snap placement/drag to a grid step = the vanilla node spacing (measured min link/node gap ≈ 43 units).
        // The FFX engine BREAKS the grid if nodes are placed too close together (proven in-game), so snapping
        // keeps authored grids at the game's "real size". 0 disables snapping. Driven by the DataModel's toggle.
        public int SnapStep { get; set; } = 43;

        // content index -> short type name (e.g. "Strength +1"), from panel.bin; set by the DataModel. Used to
        // label the selected node (and all filled nodes when ShowLabels is on + zoomed in).
        public IReadOnlyDictionary<int, string>? NodeTypeNames { get; set; }
        // content index -> fill brush by category (HP green, MP blue, lock gray-red...), from the DataModel.
        public IReadOnlyDictionary<int, IBrush>? NodeBrushes { get; set; }
        // content index -> node visual (lock level / short text label / empty) for the lock "L{n}" + AGI/ACC badges.
        public IReadOnlyDictionary<int, SphereGridNodeVisualInfo>? NodeVisuals { get; set; }
        // content index -> game-icons.net vector glyph (CC BY) by category; crisp at any zoom (no atlas cropping).
        public IReadOnlyDictionary<int, Geometry>? NodeIcons { get; set; }
        public bool ShowIcons { get; set; } = true;
        public bool ShowLabels { get; set; }

        private int? _hoverNode;                 // node under the cursor (hover highlight)
        private DispatcherTimer? _pulseTimer;    // drives the selected-node glow pulse
        private double _pulsePhase;

        public int? SelectedNode { get; private set; }

        // Raised after any topology mutation (drag/add/link) so the DataModel can refresh counts + panel.
        public event Action? GridChanged;
        // Raised when the selected node changes (index or null) so the side panel can follow.
        public event Action<int?>? NodeSelected;
        // Raised with a short human note (e.g. "link added 12<->40") for the status line.
        public event Action<string>? Status;
        // Raised on every selection/drag with a LIVE break-risk message (or null = safe) — drives the auto anti-xablau banner.
        public event Action<string?>? LiveRisk;

        // --- view transform (world -> screen) ---
        private double _scale = 1.0;
        private double _offX;
        private double _offY;
        private bool _fitPending = true;

        // --- interaction state ---
        private bool _panning;
        private bool _draggingNode;
        private Point _lastPointer;
        private int? _linkFirst; // first node picked in AddLink mode

        // box (rubber-band) multi-select: Shift+drag on empty in Select mode. Delete removes the whole group.
        private bool _boxSelecting;
        private Point _boxStart, _boxCurrent;
        private readonly HashSet<int> _multiSelect = new();
        private bool _selectedRisky; // the selected node currently breaks an engine rule (too close / link crossing)

        // node footprint in WORLD units — the on-screen radius scales with zoom (zoom in = bigger), clamped so it
        // never vanishes nor explodes. Fixes "the more I zoom in, the smaller they look".
        private const double NodeWorldRadius = 30.0;
        private const double MinNodeRadius = 6.0, MaxNodeRadius = 48.0;
        private double CurrentNodeRadius() => Math.Clamp(NodeWorldRadius * _scale, MinNodeRadius, MaxNodeRadius);
        private const double HitSlack = 3.0;

        private static readonly IBrush BgBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x1B));
        private static readonly IBrush NodeFilled = new SolidColorBrush(Color.FromRgb(0x4F, 0xB9, 0xD9));
        private static readonly IBrush NodeEmpty = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x4A));
        private static readonly IPen NodeStroke = new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0xB0, 0xC0)), 1.2);
        private static readonly IPen SelStroke = new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0xC9, 0x3A)), 3.0);
        private static readonly IPen LinkPen = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x66, 0x74)), 1.8);
        private static readonly IPen CurvePen = new Pen(new SolidColorBrush(Color.FromRgb(0x6E, 0x5A, 0x8C)), 1.8);
        private static readonly IPen LinkFirstPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0xC9, 0x3A)), 2.0);
        private static readonly IBrush ClusterFill = new SolidColorBrush(Color.FromArgb(0x22, 0x4F, 0x6B, 0x8A));
        private static readonly IPen ClusterStroke = new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x4F, 0x6B, 0x8A)), 1);
        private static readonly IPen HighlightLinkPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0xC9, 0x3A)), 3.0);
        private static readonly IPen NeighborStroke = new Pen(new SolidColorBrush(Color.FromRgb(0xC9, 0xD8, 0x5A)), 2.0);
        private static readonly IBrush LatticeBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x6A, 0x7A, 0x8A));
        private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xEC));
        private static readonly Typeface LabelTypeface = new("Segoe UI");
        private const double LabelFontSize = 11.0; // era 10 inline no DrawLabel
        private static readonly IPen MultiStroke = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x7A, 0xC9)), 2.2);
        private static readonly IPen RiskStroke = new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0x3A, 0x3A)), 3.6);
        private static readonly IPen HoverStroke = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), 2.0);
        private static readonly IBrush PulseBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0xE8, 0xC9, 0x3A));
        private static readonly IPen NodeEdgePen = new Pen(new SolidColorBrush(Color.FromRgb(0x09, 0x13, 0x1B)), 1.6);
        private static readonly IBrush LabelTextBrush = Brushes.White;
        private static readonly IBrush IconBrush = new SolidColorBrush(Color.FromArgb(0xF5, 0xF6, 0xF9, 0xFC));
        private static readonly Typeface BadgeTypeface = new("Inter");
        private static readonly IBrush BoxFill = new SolidColorBrush(Color.FromArgb(0x22, 0xE0, 0x7A, 0xC9));
        private static readonly IPen BoxPen = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0xE0, 0x7A, 0xC9)), 1) { DashStyle = DashStyle.Dash };

        public SphereGridCanvasView()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        public void SetBuilder(SphereGridLayoutBuilder builder)
        {
            _builder = builder;
            SelectedNode = null;
            _linkFirst = null;
            _multiSelect.Clear();
            ActiveCluster = 0;
            _fitPending = true;
            NodeSelected?.Invoke(null);
            InvalidateVisual();
        }

        /// <summary>
        /// Provides the content index (panel.bin node type) for nodes created in AddNode mode.
        /// Set by the DataModel from the node-type picker; null or a negative result = empty node (0xFF).
        /// </summary>
        public Func<int>? NewNodeContentProvider { get; set; }


        public void SelectNode(int? index)
        {
            SelectedNode = index;
            NodeSelected?.Invoke(index);
            EvaluateSelectedRisk();
            EnsurePulse(index != null);
            InvalidateVisual();
        }

        // run a light ~30fps timer only while a node is selected, to pulse its glow ring
        private void EnsurePulse(bool on)
        {
            if (on)
            {
                _pulseTimer ??= CreatePulseTimer();
                if (!_pulseTimer.IsEnabled) _pulseTimer.Start();
            }
            else _pulseTimer?.Stop();
        }

        private DispatcherTimer CreatePulseTimer()
        {
            DispatcherTimer t = new() { Interval = TimeSpan.FromMilliseconds(33) };
            t.Tick += (_, _) => { _pulsePhase += 0.16; InvalidateVisual(); };
            return t;
        }

        // LIVE "anti-xablau": cheap check of just the SELECTED node (proximity O(n) + its incident links' crossings),
        // run on select + every drag frame. Marks the node red and raises a message so the UI warns in real time.
        private void EvaluateSelectedRisk()
        {
            string? msg = null;
            if (_builder != null && SelectedNode is int s && s < _builder.NodeCount)
            {
                IReadOnlyList<SphereGridNodeEntry> nodes = _builder.Nodes;
                SphereGridNodeEntry sn = nodes[s];
                int near = -1; double nd2 = double.MaxValue;
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (i == s) continue;
                    double dx = sn.PosX - nodes[i].PosX, dy = sn.PosY - nodes[i].PosY;
                    double d2 = dx * dx + dy * dy;
                    if (d2 < nd2) { nd2 = d2; near = i; }
                }
                if (near >= 0 && nd2 < 40.0 * 40.0)
                    msg = string.Format(Strings.U_Sgb_RiskTooClose, near, Math.Sqrt(nd2));

                if (msg == null)
                {
                    IReadOnlyList<SphereGridLinkEntry> links = _builder.Links;
                    for (int li = 0; li < links.Count && msg == null; li++)
                    {
                        SphereGridLinkEntry l = links[li];
                        if ((l.Node1 != s && l.Node2 != s) || l.Node1 >= nodes.Count || l.Node2 >= nodes.Count) continue;
                        int ax = nodes[l.Node1].PosX, ay = nodes[l.Node1].PosY, bx = nodes[l.Node2].PosX, by = nodes[l.Node2].PosY;
                        for (int lj = 0; lj < links.Count; lj++)
                        {
                            if (lj == li) continue;
                            SphereGridLinkEntry m = links[lj];
                            if (m.Node1 >= nodes.Count || m.Node2 >= nodes.Count) continue;
                            if (l.Node1 == m.Node1 || l.Node1 == m.Node2 || l.Node2 == m.Node1 || l.Node2 == m.Node2) continue;
                            int cx = nodes[m.Node1].PosX, cy = nodes[m.Node1].PosY, dx = nodes[m.Node2].PosX, dy = nodes[m.Node2].PosY;
                            if (ProperCross(ax, ay, bx, by, cx, cy, dx, dy)) { msg = string.Format(Strings.U_Sgb_RiskCrossing, li, lj); break; }
                        }
                    }
                }
            }
            _selectedRisky = msg != null;
            LiveRisk?.Invoke(msg);
        }

        private static int Orient(int ax, int ay, int bx, int by, int cx, int cy)
        { long v = (long)(bx - ax) * (cy - ay) - (long)(by - ay) * (cx - ax); return v > 0 ? 1 : (v < 0 ? -1 : 0); }

        private static bool ProperCross(int ax, int ay, int bx, int by, int cx, int cy, int dx, int dy)
        {
            int d1 = Orient(cx, cy, dx, dy, ax, ay), d2 = Orient(cx, cy, dx, dy, bx, by);
            int d3 = Orient(ax, ay, bx, by, cx, cy), d4 = Orient(ax, ay, bx, by, dx, dy);
            return d1 != 0 && d2 != 0 && d3 != 0 && d4 != 0 && d1 != d2 && d3 != d4;
        }

        public void FitToContent()
        {
            _fitPending = true;
            InvalidateVisual();
        }

        // ---------- transform ----------
        private Point W2S(double wx, double wy) => new(wx * _scale + _offX, wy * _scale + _offY);
        private Point S2W(Point s) => new((s.X - _offX) / _scale, (s.Y - _offY) / _scale);

        private void DoFit()
        {
            _fitPending = false;
            if (_builder == null || _builder.NodeCount == 0)
            {
                _scale = 0.25; _offX = Bounds.Width / 2; _offY = Bounds.Height / 2;
                return;
            }

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (SphereGridNodeEntry n in _builder.Nodes)
            {
                minX = Math.Min(minX, n.PosX); maxX = Math.Max(maxX, n.PosX);
                minY = Math.Min(minY, n.PosY); maxY = Math.Max(maxY, n.PosY);
            }
            double w = Math.Max(1, maxX - minX), h = Math.Max(1, maxY - minY);
            double vw = Math.Max(50, Bounds.Width - 60), vh = Math.Max(50, Bounds.Height - 60);
            double fit = Math.Min(vw / w, vh / h);
            if (!double.IsFinite(fit) || fit <= 0) fit = 0.25;
            // a shipped grid would cram 800+ nodes into nothing at pure fit — start zoomed in to a READABLE scale
            // (nodes ~12px, vanilla spacing ~77u -> ~42px apart) and let the user pan; tiny grids just fit.
            _scale = Math.Max(fit, 0.55);
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            _offX = Bounds.Width / 2 - cx * _scale;
            _offY = Bounds.Height / 2 - cy * _scale;
        }

        // ---------- render ----------
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(BgBrush, new Rect(Bounds.Size));
            if (_fitPending) DoFit();
            if (_builder == null) return;

            DrawLattice(context);

            IReadOnlyList<SphereGridNodeEntry> nodes = _builder.Nodes;
            IReadOnlyList<SphereGridLinkEntry> links = _builder.Links;

            // the selected node's incident links + neighbour nodes (drawn highlighted)
            HashSet<int> incidentLinks = new();
            HashSet<int> neighbourNodes = new();
            if (SelectedNode is int s)
            {
                for (int li = 0; li < links.Count; li++)
                {
                    SphereGridLinkEntry l = links[li];
                    if (l.Node1 == s) { incidentLinks.Add(li); neighbourNodes.Add(l.Node2); }
                    else if (l.Node2 == s) { incidentLinks.Add(li); neighbourNodes.Add(l.Node1); }
                }
            }

            // clusters (faint regions) — radiusType&3 gives 0..3, scaled to a modest world radius.
            foreach (SphereGridClusterEntry c in _builder.Clusters)
            {
                double r = (12 + (c.RadiusType & 3) * 10) * _scale;
                if (r < 2) continue;
                Point p = W2S(c.PosX, c.PosY);
                context.DrawEllipse(ClusterFill, ClusterStroke, p, r, r);
            }

            // links (straight, or quadratic curve through the anchor node); the selection's links glow
            for (int li = 0; li < links.Count; li++)
            {
                SphereGridLinkEntry l = links[li];
                if (l.Node1 >= nodes.Count || l.Node2 >= nodes.Count) continue;
                Point a = W2S(nodes[l.Node1].PosX, nodes[l.Node1].PosY);
                Point b = W2S(nodes[l.Node2].PosX, nodes[l.Node2].PosY);
                bool hot = incidentLinks.Contains(li);
                if (l.AnchorNode != 0xFFFF && l.AnchorNode < nodes.Count)
                {
                    Point ctrl = W2S(nodes[l.AnchorNode].PosX, nodes[l.AnchorNode].PosY);
                    var geo = new StreamGeometry();
                    using (StreamGeometryContext g = geo.Open())
                    {
                        g.BeginFigure(a, false);
                        g.QuadraticBezierTo(ctrl, b);
                        g.EndFigure(false);
                    }
                    context.DrawGeometry(null, hot ? HighlightLinkPen : CurvePen, geo);
                }
                else
                {
                    context.DrawLine(hot ? HighlightLinkPen : LinkPen, a, b);
                }
            }

            // nodes — authentic coloured orb + atlas icon / short badge (Explorer look). LOD: tiny dots when zoomed
            // far out (overview) so the whole grid stays fast/legible; full orbs when zoomed in.
            double rad = CurrentNodeRadius();
            bool glyphs = ShowIcons && rad >= 9; // only draw icons/badges once the orb is big enough to read them
            bool labelAll = ShowLabels && _scale > 0.7;
            for (int i = 0; i < nodes.Count; i++)
            {
                SphereGridNodeEntry n = nodes[i];
                Point p = W2S(n.PosX, n.PosY);
                if (p.X < -rad * 2 || p.Y < -rad * 2 || p.X > Bounds.Width + rad * 2 || p.Y > Bounds.Height + rad * 2) continue; // cull
                bool empty = n.ContentIndex == SphereGridLayoutBuilder.EmptyContent;
                bool sel = i == SelectedNode;
                IBrush fill = empty ? NodeEmpty
                            : (NodeBrushes != null && NodeBrushes.TryGetValue(n.ContentIndex, out IBrush? b) ? b : NodeFilled);

                if (sel)
                {
                    double pulse = (Math.Sin(_pulsePhase) + 1) / 2;
                    context.DrawEllipse(PulseBrush, null, p, rad + 3 + pulse * 4, rad + 3 + pulse * 4);
                }

                IPen stroke = sel ? (_selectedRisky ? RiskStroke : SelStroke)
                            : _multiSelect.Contains(i) ? MultiStroke
                            : neighbourNodes.Contains(i) ? NeighborStroke
                            : (i == _linkFirst ? LinkFirstPen : NodeEdgePen);
                context.DrawEllipse(fill, stroke, p, rad, rad);

                // glyph (scales with the orb): lock level "L{n}", else a crisp game-icon vector, else a short text badge.
                if (glyphs && !empty)
                {
                    SphereGridNodeVisualInfo? vis = null;
                    NodeVisuals?.TryGetValue(n.ContentIndex, out vis);
                    if (vis != null && vis.IsLock)
                    {
                        DrawBadge(context, $"L{vis.LockLevel}", p, rad * 0.95);
                    }
                    else if (NodeIcons != null && NodeIcons.TryGetValue(n.ContentIndex, out Geometry? icon))
                    {
                        double ico = rad * 1.7, sc = ico / 512.0;
                        using (context.PushTransform(Matrix.CreateScale(sc, sc) * Matrix.CreateTranslation(p.X - ico / 2, p.Y - ico / 2)))
                            context.DrawGeometry(IconBrush, null, icon);
                    }
                    else if (vis != null && !string.IsNullOrEmpty(vis.ShortLabel) && vis.ShortLabel != "--")
                    {
                        DrawBadge(context, vis.ShortLabel, p, rad * (vis.ShortLabel.Length <= 3 ? 0.66 : 0.52));
                    }
                }

                if (i == _hoverNode && !sel)
                    context.DrawEllipse(null, HoverStroke, p, rad + 2, rad + 2);

                if ((sel || (labelAll && !empty)) && NodeTypeNames != null
                    && NodeTypeNames.TryGetValue(n.ContentIndex, out string? name) && !string.IsNullOrEmpty(name))
                    DrawLabel(context, new Point(p.X, p.Y + rad + 2), name);
            }

            if (_boxSelecting)
            {
                double bx = Math.Min(_boxStart.X, _boxCurrent.X), by = Math.Min(_boxStart.Y, _boxCurrent.Y);
                Rect box = new(bx, by, Math.Abs(_boxStart.X - _boxCurrent.X), Math.Abs(_boxStart.Y - _boxCurrent.Y));
                context.FillRectangle(BoxFill, box);
                context.DrawRectangle(null, BoxPen, box);
            }
        }

        // Faint reference dots at the snap-grid step, so the author can see where nodes encaixam (vanilla ~43).
        private void DrawLattice(DrawingContext context)
        {
            if (SnapStep <= 0) return;
            if (SnapStep * _scale < 8) return; // too dense to be useful
            Point tl = S2W(new Point(0, 0)), br = S2W(new Point(Bounds.Width, Bounds.Height));
            int x0 = (int)(Math.Floor(tl.X / SnapStep) * SnapStep), x1 = (int)(Math.Ceiling(br.X / SnapStep) * SnapStep);
            int y0 = (int)(Math.Floor(tl.Y / SnapStep) * SnapStep), y1 = (int)(Math.Ceiling(br.Y / SnapStep) * SnapStep);
            long cols = (long)(x1 - x0) / SnapStep + 1, rows = (long)(y1 - y0) / SnapStep + 1;
            if (cols * rows > 4000) return; // guard against a huge dot field
            for (int wx = x0; wx <= x1; wx += SnapStep)
                for (int wy = y0; wy <= y1; wy += SnapStep)
                {
                    Point p = W2S(wx, wy);
                    context.FillRectangle(LatticeBrush, new Rect(p.X - 0.75, p.Y - 0.75, 1.5, 1.5));
                }
        }

        private void DrawLabel(DrawingContext context, Point p, string text)
        {
            FormattedText ft = new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, LabelFontSize, LabelBrush);
            context.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y));
        }

        // centred glyph text inside a node (short badge like AGI / STR / L3), mirroring the Explorer preview.
        private void DrawBadge(DrawingContext context, string text, Point center, double fontSize)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            TextLayout tl = new(text, typeface: BadgeTypeface, fontSize: fontSize, foreground: LabelTextBrush, textAlignment: TextAlignment.Center);
            tl.Draw(context, new Point(center.X - tl.Width / 2, center.Y - tl.Height / 2 - 0.5));
        }

        // ---------- hit testing ----------
        private int? NodeAt(Point screen)
        {
            if (_builder == null) return null;
            var nodes = _builder.Nodes;
            double best = CurrentNodeRadius() + HitSlack;
            best *= best;
            int? hit = null;
            // iterate in reverse so the topmost-drawn node wins
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                Point p = W2S(nodes[i].PosX, nodes[i].PosY);
                double dx = p.X - screen.X, dy = p.Y - screen.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 <= best) { hit = i; break; }
            }
            return hit;
        }

        // ---------- pointer ----------
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            if (_builder == null) return;
            Point sp = e.GetPosition(this);
            _lastPointer = sp;
            PointerPoint pp = e.GetCurrentPoint(this);
            bool left = pp.Properties.IsLeftButtonPressed;
            bool right = pp.Properties.IsRightButtonPressed || pp.Properties.IsMiddleButtonPressed;

            int? node = NodeAt(sp);

            if (right)
            {
                _panning = true;
                e.Pointer.Capture(this);
                return;
            }

            if (!left) return;

            switch (CurrentMode)
            {
                case Mode.AddLink:
                    if (node is int nl)
                    {
                        if (_linkFirst is int first && first != nl)
                        {
                            try
                            {
                                _builder.AddLink((ushort)first, (ushort)nl);
                                Status?.Invoke(string.Format(Strings.U_Sgb_LinkAdded, first, nl));
                                GridChanged?.Invoke();
                            }
                            catch (Exception ex) { Status?.Invoke(string.Format(Strings.U_Sgb_LinkFailed, ex.Message)); }
                            _linkFirst = null;
                        }
                        else
                        {
                            _linkFirst = nl;
                            Status?.Invoke(string.Format(Strings.U_Sgb_LinkFirstNode, nl));
                        }
                        InvalidateVisual();
                    }
                    else { _linkFirst = null; InvalidateVisual(); }
                    break;

                case Mode.AddNode:
                    if (node == null)
                    {
                        try
                        {
                            Point w = S2W(sp);
                            short wx = SnapShort(w.X), wy = SnapShort(w.Y);
                            int content = NewNodeContentProvider?.Invoke() ?? SphereGridLayoutBuilder.EmptyContent;
                            if (content < 0)
                            {
                                Status?.Invoke(Strings.F2_choose_a_valid_node_type_before_adding_s_a61beaff);
                                break;
                            }
                            int idx = _builder.AddNodeNear(wx, wy, content);
                            ushort cluster = _builder.Nodes[idx].Cluster;
                            ushort u6 = _builder.Nodes[idx].Unknown6;
                            Status?.Invoke(string.Format(Strings.U_Sgb_NodeAdded, idx, wx, wy, cluster, u6));
                            SelectNode(idx);
                            GridChanged?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            FFXProjectEditor.Utils.CrashLog.Write("SphereGridCanvas.AddNode", ex);
                            Status?.Invoke(string.Format(Strings.U_Sgb_AddNodeFailed, ex.Message));
                        }
                    }
                    else SelectNode(node);
                    break;

                default: // Select
                    if (node is int ns)
                    {
                        SelectNode(ns);
                        _draggingNode = true;
                        e.Pointer.Capture(this);
                    }
                    else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        _boxSelecting = true; _boxStart = sp; _boxCurrent = sp;
                        e.Pointer.Capture(this);
                        InvalidateVisual();
                    }
                    else
                    {
                        _panning = true;
                        e.Pointer.Capture(this);
                    }
                    break;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_builder == null) return;
            Point sp = e.GetPosition(this);
            double ddx = sp.X - _lastPointer.X, ddy = sp.Y - _lastPointer.Y;

            if (_boxSelecting)
            {
                _boxCurrent = sp;
                InvalidateVisual();
                return;
            }

            if (_draggingNode && SelectedNode is int idx)
            {
                Point w = S2W(sp);
                _builder.MoveNode(idx, SnapShort(w.X), SnapShort(w.Y));
                _lastPointer = sp;
                GridChanged?.Invoke();
                EvaluateSelectedRisk();
                InvalidateVisual();
            }
            else if (_panning)
            {
                _offX += ddx; _offY += ddy;
                _lastPointer = sp;
                InvalidateVisual();
            }
            else
            {
                int? h = NodeAt(sp); // hover highlight
                if (h != _hoverNode) { _hoverNode = h; InvalidateVisual(); }
            }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_hoverNode != null) { _hoverNode = null; InvalidateVisual(); }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_boxSelecting)
            {
                _boxSelecting = false;
                e.Pointer.Capture(null);
                SelectNodesInBox(_boxStart, _boxCurrent);
                InvalidateVisual();
                return;
            }
            _panning = false;
            _draggingNode = false;
            e.Pointer.Capture(null);
        }

        private void SelectNodesInBox(Point a, Point b)
        {
            if (_builder == null) return;
            double minx = Math.Min(a.X, b.X), maxx = Math.Max(a.X, b.X);
            double miny = Math.Min(a.Y, b.Y), maxy = Math.Max(a.Y, b.Y);
            _multiSelect.Clear();
            IReadOnlyList<SphereGridNodeEntry> nodes = _builder.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Point p = W2S(nodes[i].PosX, nodes[i].PosY);
                if (p.X >= minx && p.X <= maxx && p.Y >= miny && p.Y <= maxy) _multiSelect.Add(i);
            }
            Status?.Invoke(_multiSelect.Count == 0
                ? Strings.U_Sgb_MultiSelectEmpty
                : string.Format(Strings.U_Sgb_MultiSelectCount, _multiSelect.Count));
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            Point sp = e.GetPosition(this);
            Point wBefore = S2W(sp);
            double factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
            double next = _scale * factor;
            next = Math.Clamp(next, 0.01, 50);
            _scale = next;
            // keep the world point under the cursor fixed
            _offX = sp.X - wBefore.X * _scale;
            _offY = sp.Y - wBefore.Y * _scale;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_builder == null) return;
            if (e.Key != Key.Delete && e.Key != Key.Back) return;

            if (_multiSelect.Count > 0)
            {
                int n = _multiSelect.Count;
                // delete in DESCENDING index order so each RemoveNode's reindex doesn't shift the not-yet-deleted ones
                foreach (int idx in _multiSelect.OrderByDescending(x => x))
                    try { _builder.RemoveNode(idx); } catch { /* skip already-invalid */ }
                _multiSelect.Clear();
                Status?.Invoke(string.Format(Strings.U_Sgb_NodesRemoved, n));
                SelectNode(null);
                GridChanged?.Invoke();
                e.Handled = true;
                return;
            }

            if (SelectedNode is int single)
            {
                try
                {
                    _builder.RemoveNode(single);
                    Status?.Invoke(string.Format(Strings.U_Sgb_NodeRemoved, single));
                    SelectNode(null);
                    GridChanged?.Invoke();
                }
                catch (Exception ex) { Status?.Invoke(Strings.U_Sgb_RemoveFailed + ex.Message); }
                e.Handled = true;
            }
        }

        private short SnapShort(double v)
        {
            if (SnapStep > 0) v = Math.Round(v / SnapStep) * SnapStep;
            return ClampShort(v);
        }

        private static short ClampShort(double v)
        {
            if (v < short.MinValue) return short.MinValue;
            if (v > short.MaxValue) return short.MaxValue;
            return (short)Math.Round(v);
        }
    }
}
