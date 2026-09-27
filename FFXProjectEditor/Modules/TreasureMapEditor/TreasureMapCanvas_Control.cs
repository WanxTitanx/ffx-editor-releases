using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using FFXProjectEditor.FfxLib.TreasureMap;
using System;
using System.Collections.Generic;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

// ── TreasureMapCanvas_Control ──────────────────────────────────────────────────────────
// Custom Avalonia Control that renders one GuideMapModel (YNGM triangles) and overlays its
// TreasureChestRow items. It owns its projection (GuideMapProjection.Fit on the real canvas
// bounds) and projects BOTH the geometry and the chests with that SAME projection — fixing
// a latent bug where chests used fixed 900x700 PixelX/Y and drifted from the geometry at
// other canvas sizes.
//   Real chests        -> solid gold circle at their guide x/z.
//   Placeholder chests -> orange-outline circle at the map-bounds center (no real position).
// Supports pan (drag), zoom (wheel/buttons) and CenterOn(chest).
// ──────────────────────────────────────────────────────────────────────────────────────

public sealed class TreasureMapCanvas_Control : Control
{
    private static readonly IBrush MapBg = new SolidColorBrush(Color.Parse("#101820"));
    private static readonly IBrush MapFill = new SolidColorBrush(Color.Parse("#183F4A"));
    private static readonly Pen MapEdge = new(new SolidColorBrush(Color.Parse("#4CB7C5")), 1);
    private static readonly IBrush ChestFill = new SolidColorBrush(Color.Parse("#ffcb55"));
    private static readonly Pen ChestStroke = new(new SolidColorBrush(Color.Parse("#281900")), 2);
    private static readonly IBrush SelFill = new SolidColorBrush(Color.Parse("#FFE066"));
    private static readonly IBrush PlaceholderFill = new SolidColorBrush(Color.Parse("#5A3A1B"));
    private static readonly Pen PlaceholderStroke = new(new SolidColorBrush(Color.Parse("#FF7043")), 2);

    public static readonly StyledProperty<GuideMapModel?> ModelProperty =
        AvaloniaProperty.Register<TreasureMapCanvas_Control, GuideMapModel?>(nameof(Model));
    public static readonly StyledProperty<IEnumerable<TreasureChestRow>?> ItemsProperty =
        AvaloniaProperty.Register<TreasureMapCanvas_Control, IEnumerable<TreasureChestRow>?>(nameof(Items));
    public static readonly StyledProperty<TreasureChestRow?> SelectedItemProperty =
        AvaloniaProperty.Register<TreasureMapCanvas_Control, TreasureChestRow?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);
    public static readonly DirectProperty<TreasureMapCanvas_Control, int> ZoomPercentProperty =
        AvaloniaProperty.RegisterDirect<TreasureMapCanvas_Control, int>(nameof(ZoomPercent), c => c.ZoomPercent);

    private double _zoom = 1; private Vector _pan; private bool _panning; private Point _last; private int _zoomPct = 100;
    private GuideMapModel? _rModel; private StreamGeometry? _rGeo; private GuideMapProjection? _rProj;
    private double _rW, _rH;

    public GuideMapModel? Model { get => GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public IEnumerable<TreasureChestRow>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public TreasureChestRow? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public int ZoomPercent { get => _zoomPct; private set => SetAndRaise(ZoomPercentProperty, ref _zoomPct, value); }

    static TreasureMapCanvas_Control() { AffectsRender<TreasureMapCanvas_Control>(ModelProperty, ItemsProperty, SelectedItemProperty); }
    public TreasureMapCanvas_Control() { ClipToBounds = true; PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released; PointerWheelChanged += Wheeled; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == ModelProperty || change.Property == BoundsProperty) { ClearCache(); if (change.Property == ModelProperty) Fit(); } }

    public void Fit() { _zoom = 1; _pan = default; ZoomPercent = 100; InvalidateVisual(); }
    public void ZoomIn() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1.2);
    public void ZoomOut() => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), 1 / 1.2);

    public void CenterOn(TreasureChestRow? chest)
    {
        if (Model is null || chest is null) return;
        // Real position when recovered; otherwise center the bounds placeholder so "Center"
        // also works for the majority of chests (Unresolved / runtime-only positions).
        float gx, gz;
        if (chest.Location.GuideX is float rx && chest.Location.GuideZ is float rz) { gx = rx; gz = rz; }
        else if (chest.ShowAsPlaceholder && chest.PlaceholderX is float phx && chest.PlaceholderZ is float phz) { gx = phx; gz = phz; }
        else return;
        EnsureCache(); (float px, float py) = _rProj!.Project(gx, gz);
        _pan = new Vector(-(px - Bounds.Width / 2) * _zoom, -(py - Bounds.Height / 2) * _zoom); InvalidateVisual();
    }
    public override void Render(DrawingContext ctx)
    {
        if (Model is null || Bounds.Width <= 0) { ctx.FillRectangle(MapBg, new Rect(0, 0, Bounds.Width, Bounds.Height)); return; }
        EnsureCache(); ctx.FillRectangle(MapBg, new Rect(0, 0, Bounds.Width, Bounds.Height));
        double cx = Bounds.Width / 2, cy = Bounds.Height / 2;
        using var push = ctx.PushPreTransform(Matrix.CreateTranslation(cx, cy) * Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_pan.X / _zoom - cx, _pan.Y / _zoom - cy));
        ctx.DrawGeometry(MapFill, MapEdge, _rGeo!);
        if (Items is not null)
            foreach (TreasureChestRow ch in Items)
            {
                bool sel = SelectedItem == ch;
                // Real positions joined to the SAME projection as the geometry (fix: was fixed 900x700 PixelX/Y).
                if (ch.Location.GuideX is float gx && ch.Location.GuideZ is float gz)
                {
                    (float px, float py) = _rProj!.Project(gx, gz);
                    double r = sel ? 9 : 7;
                    ctx.DrawEllipse(sel ? SelFill : ChestFill, ChestStroke, new Point(px, py), r, r);
                }
                // Placeholder for chests without a recovered position (center of map bounds).
                else if (ch.ShowAsPlaceholder && ch.PlaceholderX is float phX && ch.PlaceholderZ is float phZ)
                {
                    (float px, float py) = _rProj!.Project(phX, phZ);
                    double r = sel ? 8 : 6;
                    ctx.DrawEllipse(PlaceholderFill, PlaceholderStroke, new Point(px, py), r, r);
                }
            }
    }

    private void Pressed(object? s, PointerPressedEventArgs e) { _panning = true; _last = e.GetPosition(this); e.Pointer.Capture(this); }
    private void Moved(object? s, PointerEventArgs e) { if (!_panning) return; var p = e.GetPosition(this); _pan += p - _last; _last = p; InvalidateVisual(); }
    private void Released(object? s, PointerReleasedEventArgs e) { _panning = false; e.Pointer.Capture(null); }
    private void Wheeled(object? s, PointerWheelEventArgs e) { ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.15 : 1 / 1.15); e.Handled = true; }
    private void ZoomAt(Point a, double f) { double n = Math.Clamp(_zoom * f, .6, 6); double r = n / _zoom; _pan = new Vector(a.X - Bounds.Width / 2 - (a.X - Bounds.Width / 2 - _pan.X) * r, a.Y - Bounds.Height / 2 - (a.Y - Bounds.Height / 2 - _pan.Y) * r); _zoom = n; ZoomPercent = (int)Math.Round(_zoom * 100); InvalidateVisual(); }

    private void EnsureCache()
    {
        if (Model is null || (ReferenceEquals(_rModel, Model) && _rGeo is not null && _rW == Bounds.Width && _rH == Bounds.Height)) return;
        _rModel = Model; _rW = Bounds.Width; _rH = Bounds.Height;
        _rProj = GuideMapProjection.Fit(Model, Math.Max(100, (int)Bounds.Width), Math.Max(100, (int)Bounds.Height));
        var geo = new StreamGeometry();
        using var gctx = geo.Open();
        foreach (GuideMapTriangle t in Model.Triangles)
        { GuideMapVertex a = Model.Vertices[t.A], b = Model.Vertices[t.B], c = Model.Vertices[t.C];
          (float ax, float ay) = _rProj.Project(a.X, a.Z); (float bx, float by) = _rProj.Project(b.X, b.Z); (float cx, float cy) = _rProj.Project(c.X, c.Z);
          gctx.BeginFigure(new Point(ax, ay), true); gctx.LineTo(new Point(bx, by)); gctx.LineTo(new Point(cx, cy)); gctx.EndFigure(true); }
        _rGeo = geo;
    }
    private void ClearCache() { _rModel = null; _rGeo = null; _rProj = null; _rW = _rH = 0; }
}
