using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FFXProjectEditor.Services;
using System;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

// ── TreasureMapEditor_Control / TreasureMapConfirmWindow ────────────────────────────────
// Code-behind for the editor view. Mirrors DataModel properties to controls and wires the
// button handlers (Save/Discard with a confirmation dialog that supports "Don't show
// again", model paging and canvas zoom/fit/center). Debug-log category "TreasureMap.UI".
// ──────────────────────────────────────────────────────────────────────────────────────
internal sealed class TreasureMapConfirmWindow : Window
{
    private readonly CheckBox _dontShowAgain = new() { Content = "Don\'t show this again" };

    private TreasureMapConfirmWindow(string title, string message, string confirmLabel)
    {
        Title = title; Width = 500; SizeToContent = SizeToContent.Height; CanResize = false;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        var proceed = new Button { Content = confirmLabel, MinWidth = 110 };
        cancel.Click += (_, _) => Close(false);
        proceed.Click += (_, _) => { Close(true); };

        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
                    _dontShowAgain,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8, Children = { cancel, proceed } }
                }
            }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(false); } };
    }

    public static Task<bool> Confirm(Window owner, string title, string message, string confirmLabel, bool isSuppressed)
    {
        if (isSuppressed) return Task.FromResult(true);
        return new TreasureMapConfirmWindow(title, message, confirmLabel).ShowDialog<bool>(owner);
    }

    public bool DontShowAgain => _dontShowAgain.IsChecked == true;
}

public partial class TreasureMapEditor_Control : UserControl
{
    private readonly TreasureMapEditor_DataModel _model;
    public TreasureMapEditor_Control() { _model = new TreasureMapEditor_DataModel(ResolveMasterPath()); DataContext = _model; InitializeComponent(); }
    public TreasureMapEditor_Control(string masterPath) { _model = new TreasureMapEditor_DataModel(masterPath); DataContext = _model; InitializeComponent(); }

    private void FieldSelected(object? s, SelectionChangedEventArgs e) { if (e.AddedItems.Count > 0 && e.AddedItems[0] is TreasureFieldItem f) _model.SelectedField = f; }
    private void ChestSelected(object? s, SelectionChangedEventArgs e) { if (e.AddedItems.Count > 0 && e.AddedItems[0] is TreasureChestRow c) _model.SelectedChest = c; }

    private async void Save_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            var owner = VisualRoot as Window ?? throw new InvalidOperationException("No owner window");
            var result = await TreasureMapConfirmWindow.Confirm(owner,
                "Save Treasure Catalog",
                "Save changes to the treasure catalog (takara.bin)? This modifies the game file.",
                "Save", _model.IsConfirmSuppressed);
            if (!result) return;
            _model.Save();
        }
        catch (Exception ex) { _model.Status = "Save error: " + ex.Message; }
    }

    private async void Discard_Click(object? s, RoutedEventArgs e)
    {
        if (!_model.IsDirty) { _model.Discard(); return; }
        try
        {
            var owner = VisualRoot as Window ?? throw new InvalidOperationException("No owner window");
            var result = await TreasureMapConfirmWindow.Confirm(owner,
                "Discard Changes",
                "Discard all unsaved changes to the treasure catalog?",
                "Discard", _model.IsConfirmSuppressed);
            if (!result) return;
            _model.Discard();
        }
        catch (Exception ex) { _model.Status = "Discard error: " + ex.Message; }
    }

    private void PrevModel_Click(object? s, RoutedEventArgs e) => _model.NextModel(-1);
    private void NextModel_Click(object? s, RoutedEventArgs e) => _model.NextModel(1);
    private void ZoomIn_Click(object? s, RoutedEventArgs e) => MapCanvas.ZoomIn();
    private void ZoomOut_Click(object? s, RoutedEventArgs e) => MapCanvas.ZoomOut();
    private void Fit_Click(object? s, RoutedEventArgs e) => MapCanvas.Fit();
    private void Center_Click(object? s, RoutedEventArgs e) => MapCanvas.CenterOn(_model.SelectedChest);

    private static string ResolveMasterPath() => PortablePathResolver.MasterRoot ?? string.Empty;
}
