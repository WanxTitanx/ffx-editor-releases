using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using FFXProjectEditor.FfxLib.SphereGrid;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Controls.SphereGrid;

public sealed class SphereGridPreview_Control : Control
{
    static readonly IReadOnlyList<IBrush> NodePalette =
    [
        new SolidColorBrush(Color.Parse("#5CE2FF")),
        new SolidColorBrush(Color.Parse("#7FFFC4")),
        new SolidColorBrush(Color.Parse("#FFD166")),
        new SolidColorBrush(Color.Parse("#F78FB3")),
        new SolidColorBrush(Color.Parse("#A29BFE")),
        new SolidColorBrush(Color.Parse("#74B9FF")),
        new SolidColorBrush(Color.Parse("#55EFC4")),
        new SolidColorBrush(Color.Parse("#FAB1A0"))
    ];

    static readonly Pen LinkPen = new(new SolidColorBrush(Color.Parse("#35586D")), 1.6);
    static readonly Pen SelectedLinkPen = new(new SolidColorBrush(Color.Parse("#7BDAFF")), 2.5);
    static readonly Pen GameLinkGlowPen = new(new SolidColorBrush(Color.Parse("#205D82")), 7.4);
    static readonly Pen GameLinkMidPen = new(new SolidColorBrush(Color.Parse("#37A9E2")), 3.2);
    static readonly Pen GameLinkCorePen = new(new SolidColorBrush(Color.Parse("#9CEFFF")), 1.15);
    static readonly Pen ClusterPen = new(new SolidColorBrush(Color.Parse("#274A5B")), 1.0);
    static readonly Pen AnchorPen = new(new SolidColorBrush(Color.Parse("#B6FFF1")), 1.2);
    static readonly Pen SelectedNodePen = new(new SolidColorBrush(Color.Parse("#FFFFFF")), 2.2);
    static readonly Pen NodePen = new(new SolidColorBrush(Color.Parse("#09131B")), 1.0);
    static readonly Pen LabelNodePen = new(new SolidColorBrush(Color.Parse("#A6C7D9")), 1.1);
    static readonly Pen SelectedHaloPen = new(new SolidColorBrush(Color.Parse("#7BDAFF")), 3.4);
    static readonly IBrush LabelFill = new SolidColorBrush(Color.Parse("#112430"));
    static readonly IBrush SelectedLabelFill = new SolidColorBrush(Color.Parse("#1D4054"));
    static readonly IBrush EmptyNodeFill = new SolidColorBrush(Color.Parse("#4B5B66"));
    static readonly IBrush LabelTextBrush = Brushes.White;
    static readonly Typeface LabelTypeface = new("Inter");
    static readonly Bitmap? IconAtlas = LoadBitmap("avares://FFXProjectEditor/Assets/SphereGrid/icon_atlas.png");
    static readonly IReadOnlyDictionary<SphereGridPreviewBackgroundKind, Bitmap?> Backgrounds = new Dictionary<SphereGridPreviewBackgroundKind, Bitmap?>
    {
        [SphereGridPreviewBackgroundKind.Original] = LoadBitmap("avares://FFXProjectEditor/Assets/SphereGrid/background_original.png"),
        [SphereGridPreviewBackgroundKind.Standard] = LoadBitmap("avares://FFXProjectEditor/Assets/SphereGrid/background_standard.png"),
        [SphereGridPreviewBackgroundKind.Expert] = LoadBitmap("avares://FFXProjectEditor/Assets/SphereGrid/background_expert.png")
    };
    static readonly IReadOnlyDictionary<int, Rect> IconRects = new Dictionary<int, Rect>
    {
        [0x02] = Cell(0, 3), // strength
        [0x04] = Cell(2, 2), // defense
        [0x05] = Cell(2, 3), // magic defense
        [0x0A] = Cell(2, 0), // hp
        [0x0B] = Cell(3, 0), // mp
        [0x0C] = Cell(4, 1), // white magic / support
        [0x0D] = Cell(4, 0), // black magic
        [0x0E] = Cell(5, 1), // special / target
        [0x0F] = Cell(0, 6)  // skill
    };

    readonly Dictionary<int, NodeHitRegion> hitRegions = new();
    int hoveredNodeIndex = -1;
    Vector viewPan = default;

    public static readonly StyledProperty<SphereGridLayoutFile?> LayoutProperty =
        AvaloniaProperty.Register<SphereGridPreview_Control, SphereGridLayoutFile?>(nameof(Layout));

    public static readonly StyledProperty<int> HighlightedNodeIndexProperty =
        AvaloniaProperty.Register<SphereGridPreview_Control, int>(nameof(HighlightedNodeIndex), -1);

    public static readonly StyledProperty<IReadOnlyDictionary<int, SphereGridNodeVisualInfo>?> NodeVisualsProperty =
        AvaloniaProperty.Register<SphereGridPreview_Control, IReadOnlyDictionary<int, SphereGridNodeVisualInfo>?>(nameof(NodeVisuals));

    public static readonly StyledProperty<SphereGridPreviewBackgroundKind> BackgroundKindProperty =
        AvaloniaProperty.Register<SphereGridPreview_Control, SphereGridPreviewBackgroundKind>(nameof(BackgroundKind), SphereGridPreviewBackgroundKind.None);

    public static readonly StyledProperty<SphereGridPreviewVisualMode> VisualModeProperty =
        AvaloniaProperty.Register<SphereGridPreview_Control, SphereGridPreviewVisualMode>(nameof(VisualMode), SphereGridPreviewVisualMode.HybridGame);

    static SphereGridPreview_Control()
    {
        FocusableProperty.OverrideDefaultValue<SphereGridPreview_Control>(true);
        AffectsRender<SphereGridPreview_Control>(LayoutProperty, HighlightedNodeIndexProperty, NodeVisualsProperty, BackgroundKindProperty, VisualModeProperty);
    }

    public event EventHandler<SphereGridNodeInvokedEventArgs>? NodeInvoked;

    public SphereGridLayoutFile? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public int HighlightedNodeIndex
    {
        get => GetValue(HighlightedNodeIndexProperty);
        set => SetValue(HighlightedNodeIndexProperty, value);
    }

    public IReadOnlyDictionary<int, SphereGridNodeVisualInfo>? NodeVisuals
    {
        get => GetValue(NodeVisualsProperty);
        set => SetValue(NodeVisualsProperty, value);
    }

    public SphereGridPreviewBackgroundKind BackgroundKind
    {
        get => GetValue(BackgroundKindProperty);
        set => SetValue(BackgroundKindProperty, value);
    }

    public SphereGridPreviewVisualMode VisualMode
    {
        get => GetValue(VisualModeProperty);
        set => SetValue(VisualModeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        hitRegions.Clear();

        Rect frame = new(0, 0, Bounds.Width, Bounds.Height);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0A141C")), frame);
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#1E3645")), 1), frame.Deflate(0.5));

        SphereGridLayoutFile? layout = Layout;
        if (layout == null || layout.Nodes.Count == 0)
            return;

        Rect content = frame.Deflate(18);
        if (content.Width <= 0 || content.Height <= 0)
            return;

        List<Point> worldPoints = new(layout.Nodes.Count + layout.Clusters.Count);
        worldPoints.AddRange(layout.Nodes.Select(node => new Point(node.PosX, node.PosY)));
        worldPoints.AddRange(layout.Clusters.Select(cluster => new Point(cluster.PosX, cluster.PosY)));

        double minX = worldPoints.Min(point => point.X);
        double maxX = worldPoints.Max(point => point.X);
        double minY = worldPoints.Min(point => point.Y);
        double maxY = worldPoints.Max(point => point.Y);

        double worldWidth = Math.Max(1, maxX - minX);
        double worldHeight = Math.Max(1, maxY - minY);
        bool useInGameCamera = VisualMode == SphereGridPreviewVisualMode.HybridGame;
        double fitScale = Math.Min(content.Width / worldWidth, content.Height / worldHeight);
        double scale = fitScale * (useInGameCamera ? 1.42 : 1.0);

        double offsetX;
        double offsetY;
        if (useInGameCamera)
        {
            Point focusWorld = ResolveFocusWorld(layout, minX, maxX, minY, maxY);
            offsetX = content.Center.X - (focusWorld.X - minX) * scale + viewPan.X;
            offsetY = content.Center.Y - (focusWorld.Y - minY) * scale + viewPan.Y;
        }
        else
        {
            offsetX = content.X + (content.Width - worldWidth * scale) / 2 + viewPan.X;
            offsetY = content.Y + (content.Height - worldHeight * scale) / 2 + viewPan.Y;
        }

        Rect worldRect = new(offsetX, offsetY, worldWidth * scale, worldHeight * scale);

        Point Map(short x, short y)
        {
            double px = offsetX + (x - minX) * scale;
            double py = offsetY + (y - minY) * scale;
            return new Point(px, py);
        }

        if (VisualMode is SphereGridPreviewVisualMode.HybridGame or SphereGridPreviewVisualMode.BackgroundOnly)
        {
            DrawBackground(context, worldRect);
        }

        if (VisualMode != SphereGridPreviewVisualMode.BackgroundOnly && VisualMode != SphereGridPreviewVisualMode.HybridGame)
        {
            foreach (SphereGridClusterEntry cluster in layout.Clusters)
            {
                Point center = Map(cluster.PosX, cluster.PosY);
                double radius = Math.Max(14, 18 + cluster.Radius * 8 * Math.Max(0.75, scale / 18));
                context.DrawEllipse(null, ClusterPen, center, radius, radius);
                if (cluster.AltDesign)
                    context.DrawEllipse(null, ClusterPen, center, radius + 7, radius + 7);
            }

            foreach (SphereGridLinkEntry link in layout.Links)
            {
                if (link.Node1 >= layout.Nodes.Count || link.Node2 >= layout.Nodes.Count)
                    continue;

                SphereGridNodeEntry node1 = layout.Nodes[link.Node1];
                SphereGridNodeEntry node2 = layout.Nodes[link.Node2];
                bool touchesHighlighted = HighlightedNodeIndex >= 0 && (node1.Index == HighlightedNodeIndex || node2.Index == HighlightedNodeIndex);
                Point p1 = Map(node1.PosX, node1.PosY);
                Point p2 = Map(node2.PosX, node2.PosY);

                if (VisualMode == SphereGridPreviewVisualMode.HybridGame)
                {
                    DrawGameLink(context, p1, p2, touchesHighlighted);
                }
                else
                {
                    context.DrawLine(touchesHighlighted ? SelectedLinkPen : LinkPen, p1, p2);
                }
            }
        }

        SphereGridNodeEntry? focusedBadgeNode = null;
        Point focusedBadgePoint = default;
        SphereGridNodeVisualInfo? focusedBadgeVisual = null;

        foreach (SphereGridNodeEntry node in layout.Nodes)
        {
            Point point = Map(node.PosX, node.PosY);
            bool isHighlighted = node.Index == HighlightedNodeIndex;
            SphereGridNodeVisualInfo visual = ResolveVisual(node);
            double hitRadius = VisualMode == SphereGridPreviewVisualMode.BackgroundOnly ? 14 : Math.Max(12, 11.5 * Math.Max(1.0, scale / 18));
            hitRegions[node.Index] = new NodeHitRegion(point, hitRadius);

            if (VisualMode == SphereGridPreviewVisualMode.DebugColors)
            {
                DrawDebugNode(context, node, point, visual, isHighlighted);
            }
            else if (VisualMode == SphereGridPreviewVisualMode.LabelOverlay)
            {
                DrawLabelNode(context, point, visual, isHighlighted, drawBackgroundOrb: false);
            }
            else if (VisualMode == SphereGridPreviewVisualMode.HybridGame)
            {
                DrawGameNode(context, point, visual, isHighlighted);
            }
            else if (VisualMode == SphereGridPreviewVisualMode.BackgroundOnly && isHighlighted)
            {
                context.DrawEllipse(null, SelectedHaloPen, point, hitRadius, hitRadius);
            }

            if (isHighlighted)
            {
                focusedBadgeNode = node;
                focusedBadgePoint = point;
                focusedBadgeVisual = visual;
            }

            if (hoveredNodeIndex == node.Index)
            {
                focusedBadgeNode = node;
                focusedBadgePoint = point;
                focusedBadgeVisual = visual;
            }
        }

        if (focusedBadgeNode != null && focusedBadgeVisual != null)
        {
            DrawHighlightBadge(context, focusedBadgePoint, focusedBadgeVisual, focusedBadgeNode.Index, hoveredNodeIndex == focusedBadgeNode.Index);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Layout == null || Layout.Nodes.Count == 0)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Point point = e.GetPosition(this);
        if (!TryHitNode(point, out int nodeIndex))
            return;

        HighlightedNodeIndex = nodeIndex;
        Focus();
        NodeInvoked?.Invoke(this, new SphereGridNodeInvokedEventArgs(nodeIndex));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (Layout == null || Layout.Nodes.Count == 0)
            return;

        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
            return;

        Point point = e.GetPosition(this);
        int nextHovered = TryHitNode(point, out int nodeIndex) ? nodeIndex : -1;
        if (nextHovered == hoveredNodeIndex)
            return;

        hoveredNodeIndex = nextHovered;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (hoveredNodeIndex < 0)
            return;

        hoveredNodeIndex = -1;
        InvalidateVisual();
    }

    public void ResetViewPan()
    {
        viewPan = default;
        InvalidateVisual();
    }

    public void SetViewPan(Vector value)
    {
        viewPan = value;
        InvalidateVisual();
    }

    public Vector GetViewPan() => viewPan;

    Point ResolveFocusWorld(SphereGridLayoutFile layout, double minX, double maxX, double minY, double maxY)
    {
        if (HighlightedNodeIndex >= 0)
        {
            SphereGridNodeEntry? highlighted = layout.Nodes.FirstOrDefault(node => node.Index == HighlightedNodeIndex);
            if (highlighted != null)
                return new Point(highlighted.PosX, highlighted.PosY);
        }

        return new Point((minX + maxX) / 2, (minY + maxY) / 2);
    }

    void DrawBackground(DrawingContext context, Rect worldRect)
    {
        if (!Backgrounds.TryGetValue(BackgroundKind, out Bitmap? bitmap) || bitmap == null)
            return;

        double margin = VisualMode == SphereGridPreviewVisualMode.HybridGame
            ? Math.Min(worldRect.Width, worldRect.Height) * 0.012
            : Math.Min(worldRect.Width, worldRect.Height) * 0.055;
        Rect targetBounds = worldRect.Deflate(margin);
        if (targetBounds.Width <= 1 || targetBounds.Height <= 1)
            targetBounds = worldRect;

        double sourceAspect = bitmap.PixelSize.Width / (double)bitmap.PixelSize.Height;
        double targetAspect = targetBounds.Width / targetBounds.Height;

        Rect target;
        if (sourceAspect > targetAspect)
        {
            double height = targetBounds.Width / sourceAspect;
            target = new Rect(
                targetBounds.X,
                targetBounds.Y + (targetBounds.Height - height) / 2,
                targetBounds.Width,
                height);
        }
        else
        {
            double width = targetBounds.Height * sourceAspect;
            target = new Rect(
                targetBounds.X + (targetBounds.Width - width) / 2,
                targetBounds.Y,
                width,
                targetBounds.Height);
        }

        double opacityValue = VisualMode switch
        {
            SphereGridPreviewVisualMode.BackgroundOnly => 0.9,
            SphereGridPreviewVisualMode.HybridGame => 0.82,
            _ => 0.62
        };
        using var opacity = context.PushOpacity(opacityValue);
        context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), target);
    }

    void DrawGameLink(DrawingContext context, Point p1, Point p2, bool touchesHighlighted)
    {
        if (touchesHighlighted)
        {
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#66D7FF")), 8.4), p1, p2);
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#A4F3FF")), 3.9), p1, p2);
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#FFFFFF")), 1.35), p1, p2);
            return;
        }

        context.DrawLine(GameLinkGlowPen, p1, p2);
        context.DrawLine(GameLinkMidPen, p1, p2);
        context.DrawLine(GameLinkCorePen, p1, p2);
    }

    void DrawDebugNode(DrawingContext context, SphereGridNodeEntry node, Point point, SphereGridNodeVisualInfo visual, bool isHighlighted)
    {
        double radius = isHighlighted ? 7.5 : 5.2;
        IBrush fill = node.ContentIndex <= 0 || node.ContentIndex == 0xFF
            ? EmptyNodeFill
            : visual.IsLock
                ? new SolidColorBrush(Color.Parse("#5B6572"))
                : NodePalette[Math.Abs(node.ContentIndex) % NodePalette.Count];

        context.DrawEllipse(fill, isHighlighted ? SelectedNodePen : NodePen, point, radius, radius);
        if (node.AnchorLinkIndices.Count > 0)
            context.DrawEllipse(null, AnchorPen, point, radius + 4.5, radius + 4.5);
    }

    void DrawGameNode(DrawingContext context, Point point, SphereGridNodeVisualInfo visual, bool isHighlighted)
    {
        if (visual.IsEmpty)
        {
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#080D12")), new Pen(new SolidColorBrush(Color.Parse("#87949C")), 1.5), point, 15.2, 15.2);
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#111B24")), new Pen(new SolidColorBrush(Color.Parse("#33414B")), 1.1), point, 11.3, 11.3);
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#202B34")), 1.1), point, 19.4, 19.4);
            if (isHighlighted)
                context.DrawEllipse(null, SelectedHaloPen, point, 20.8, 20.8);
            return;
        }

        DrawOrbShell(context, point, ResolveOrbStyle(visual), isHighlighted);

        if (TryGetIconRect(visual, out Rect source) && IconAtlas != null)
        {
            Rect dest = new(point.X - 16.5, point.Y - 16.5, 33, 33);
            context.DrawImage(IconAtlas, source, dest);
            if (visual.IsLock)
                DrawCenteredText(context, $"L{visual.LockLevel}", point, 9, Brushes.White);
        }
        else
        {
            DrawLabelNode(context, point, visual, isHighlighted, drawBackgroundOrb: true);
        }

        if (isHighlighted)
            context.DrawEllipse(null, SelectedHaloPen, point, 21, 21);
    }

    void DrawOrbShell(DrawingContext context, Point point, OrbStyle style, bool isHighlighted)
    {
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#081117")), new Pen(new SolidColorBrush(Color.Parse(style.OuterHex)), 1.25), point, 18.3, 18.3);
        context.DrawEllipse(new SolidColorBrush(Color.Parse(style.InnerHex)), new Pen(new SolidColorBrush(Color.Parse(style.MiddleHex)), 1.0), point, 15.7, 15.7);
        context.DrawEllipse(new SolidColorBrush(Color.Parse(style.HighlightHex)), null, new Point(point.X - 4.1, point.Y - 4.1), 5.1, 5.1);
        if (isHighlighted)
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#9EEBFF")), 1.2), point, 18.9, 18.9);
    }

    OrbStyle ResolveOrbStyle(SphereGridNodeVisualInfo visual)
    {
        if (visual.IsLock)
            return new OrbStyle("#7F8D97", "#2C333C", "#A7B4BE", "#DDE6EB");

        string name = visual.DisplayName;
        return visual.AppearanceType switch
        {
            0x02 => new OrbStyle("#B64F4C", "#5C1615", "#D88A84", "#FDE9E6"),
            0x04 => new OrbStyle("#7E8590", "#353A45", "#B6BBC4", "#EEF1F5"),
            0x05 => new OrbStyle("#6152A6", "#241C57", "#9388D0", "#F0EDFF"),
            0x0A => new OrbStyle("#6CB457", "#20471A", "#A8E58D", "#F2FFF0"),
            0x0B => new OrbStyle("#538FC1", "#193A5C", "#8FC4F7", "#EDF8FF"),
            0x0C => new OrbStyle("#C5A33C", "#66521A", "#F6E28C", "#FFF9E6"),
            0x0D => new OrbStyle("#A176C8", "#46206D", "#D9B0FF", "#FFF4FF"),
            0x0E => new OrbStyle("#B14E98", "#5E1B4D", "#EA9EE0", "#FFF3FD"),
            0x0F => new OrbStyle("#8C5AAE", "#3A1E57", "#CFA8F0", "#FCF4FF"),
            _ when name.StartsWith("Strength", StringComparison.OrdinalIgnoreCase) => new OrbStyle("#B64F4C", "#5C1615", "#D88A84", "#FDE9E6"),
            _ when name.StartsWith("Magic Defense", StringComparison.OrdinalIgnoreCase) => new OrbStyle("#6152A6", "#241C57", "#9388D0", "#F0EDFF"),
            _ when name.StartsWith("Magic", StringComparison.OrdinalIgnoreCase) => new OrbStyle("#7E5DB8", "#321956", "#B999F3", "#FBF4FF"),
            _ when name.StartsWith("Defense", StringComparison.OrdinalIgnoreCase) => new OrbStyle("#7E8590", "#353A45", "#B6BBC4", "#EEF1F5"),
            _ when name.StartsWith("Agility", StringComparison.OrdinalIgnoreCase) => new OrbStyle("#4A8A87", "#173F3D", "#8BD1CB", "#EDFFFC"),
            _ when name.StartsWith("Accuracy", StringComparison.OrdinalIgnoreCase) => new OrbStyle("#A88951", "#4D3614", "#E9D3A6", "#FFF8EB"),
            _ when name.StartsWith("Evasion", StringComparison.OrdinalIgnoreCase) => new OrbStyle("#7D8EA8", "#29384B", "#B8CBE6", "#F2F8FF"),
            _ when name.StartsWith("Luck", StringComparison.OrdinalIgnoreCase) => new OrbStyle("#B9A144", "#594A14", "#F1DE8E", "#FFFCEB"),
            _ when name.StartsWith("HP", StringComparison.OrdinalIgnoreCase) => new OrbStyle("#6CB457", "#20471A", "#A8E58D", "#F2FFF0"),
            _ when name.StartsWith("MP", StringComparison.OrdinalIgnoreCase) => new OrbStyle("#538FC1", "#193A5C", "#8FC4F7", "#EDF8FF"),
            _ => new OrbStyle("#7E8590", "#2B3440", "#AFBECA", "#F4F8FB")
        };
    }

    void DrawLabelNode(DrawingContext context, Point point, SphereGridNodeVisualInfo visual, bool isHighlighted, bool drawBackgroundOrb)
    {
        double radius = 13;
        if (drawBackgroundOrb)
        {
            context.DrawEllipse(isHighlighted ? SelectedLabelFill : LabelFill, isHighlighted ? SelectedNodePen : LabelNodePen, point, radius, radius);
        }
        else
        {
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#152A38")), isHighlighted ? SelectedNodePen : LabelNodePen, point, radius, radius);
        }

        DrawCenteredText(context, visual.ShortLabel, point, visual.ShortLabel.Length <= 3 ? 10 : 8, LabelTextBrush);
    }

    void DrawHighlightBadge(DrawingContext context, Point point, SphereGridNodeVisualInfo visual, int nodeIndex, bool isHoverBadge)
    {
        string label = $"{nodeIndex:D3} · {visual.DisplayName}";
        TextLayout layout = CreateTextLayout(label, 13, Brushes.White);
        double paddingX = 10;
        double paddingY = 6;
        Rect badge = new(point.X + 16, point.Y - layout.Height - 18, layout.Width + paddingX * 2, layout.Height + paddingY * 2);

        if (badge.Right > Bounds.Width - 8)
            badge = badge.WithX(point.X - badge.Width - 16);
        if (badge.Y < 8)
            badge = badge.WithY(point.Y + 16);

        IBrush fill = new SolidColorBrush(Color.Parse(isHoverBadge ? "#21465C" : "#193546"));
        Pen border = new(new SolidColorBrush(Color.Parse(isHoverBadge ? "#B7F1FF" : "#7BDAFF")), isHoverBadge ? 1.4 : 1.2);
        context.DrawRectangle(fill, border, badge, 8, 8);
        layout.Draw(context, new Point(badge.X + paddingX, badge.Y + paddingY));
    }

    void DrawCenteredText(DrawingContext context, string text, Point center, double fontSize, IBrush brush)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        TextLayout layout = CreateTextLayout(text, fontSize, brush);
        Point origin = new(center.X - layout.Width / 2, center.Y - layout.Height / 2 - 0.5);
        layout.Draw(context, origin);
    }

    TextLayout CreateTextLayout(string text, double fontSize, IBrush brush)
    {
        return new TextLayout(
            text,
            typeface: LabelTypeface,
            fontSize: fontSize,
            foreground: brush,
            textAlignment: TextAlignment.Center);
    }

    SphereGridNodeVisualInfo ResolveVisual(SphereGridNodeEntry node)
    {
        if (NodeVisuals != null && NodeVisuals.TryGetValue(node.ContentIndex, out SphereGridNodeVisualInfo? visual))
            return visual;

        return new SphereGridNodeVisualInfo
        {
            ContentIndex = node.ContentIndex,
            AppearanceType = 0xFFFF,
            DisplayName = node.ContentIndex <= 0 || node.ContentIndex == 0xFF ? "Empty Node" : $"NodeType {node.ContentIndex:X2}h",
            ShortLabel = node.ContentIndex <= 0 || node.ContentIndex == 0xFF ? "--" : $"N{node.ContentIndex:X2}",
            IsEmpty = node.ContentIndex <= 0 || node.ContentIndex == 0xFF,
            IsLock = false,
            LockLevel = 0
        };
    }

    bool TryGetIconRect(SphereGridNodeVisualInfo visual, out Rect source)
    {
        if (visual.IsEmpty)
        {
            source = default;
            return false;
        }

        if (visual.IsLock)
        {
            source = Cell(4, 4);
            return true;
        }

        return IconRects.TryGetValue(visual.AppearanceType, out source);
    }

    public bool TryHitNode(Point point, out int nodeIndex)
    {
        foreach ((int candidateIndex, NodeHitRegion region) in hitRegions.OrderBy(pair => pair.Value.Radius))
        {
            if (region.Contains(point))
            {
                nodeIndex = candidateIndex;
                return true;
            }
        }

        nodeIndex = -1;
        return false;
    }

    static Rect Cell(int x, int y) => new(x * 64, y * 64, 64, 64);

    static Bitmap? LoadBitmap(string uri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    readonly record struct NodeHitRegion(Point Center, double Radius)
    {
        public bool Contains(Point point)
        {
            double dx = point.X - Center.X;
            double dy = point.Y - Center.Y;
            return dx * dx + dy * dy <= Radius * Radius;
        }
    }

    readonly record struct OrbStyle(string OuterHex, string InnerHex, string MiddleHex, string HighlightHex);
}
