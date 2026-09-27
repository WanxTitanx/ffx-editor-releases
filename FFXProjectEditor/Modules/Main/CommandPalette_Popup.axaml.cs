using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using FFXProjectEditor.Converters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor;

/// <summary>
/// Command Palette (Ctrl+K) — overlay central com fuzzy search sobre os módulos públicos do
/// <see cref="FFXProjectEditor.Modules.Main.ModuleRegistry"/> (Jarvis-UI Fase D §D5, v2.161.0.0).
/// </summary>
public partial class CommandPalette_Popup : UserControl
{
    public sealed class ResultRow
    {
        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public Geometry? IconGeometry { get; init; }
    }

    readonly ObservableCollection<ResultRow> _results = new();
    string _query = string.Empty;
    bool _syncingSelection;

    public event Action<string>? DispatchRequested;
    public event Action? CloseRequested;

    public ReadOnlyObservableCollection<ResultRow> Results { get; }

    public CommandPalette_Popup()
    {
        Results = new ReadOnlyObservableCollection<ResultRow>(_results);
        InitializeComponent();
        DataContext = this;
        BuildAllRows();
        ApplyFilter();
    }

    void BuildAllRows()
    {
        _all = Modules.Main.ModuleRegistry.Public.Select(e => new ResultRow
        {
            Id = e.Id,
            Title = e.LocalizedTitle,
            Description = e.LocalizedDescription,
            IconGeometry = IconKeyToGeometryConverter.ResolveGeometry(e.IconKey),
        }).ToList();
    }

    List<ResultRow> _all = new();

    void ApplyFilter()
    {
        _results.Clear();

        IEnumerable<ResultRow> source = _all;
        if (!string.IsNullOrWhiteSpace(_query))
        {
            string q = _query.Trim();
            source = _all.Where(r =>
                r.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var row in source)
            _results.Add(row);

        EmptyHint.IsVisible = _results.Count == 0;
        SelectIndex(0);
    }

    void SelectIndex(int index)
    {
        if (_results.Count == 0)
        {
            _syncingSelection = true;
            ResultsList.SelectedIndex = -1;
            _syncingSelection = false;
            return;
        }

        index = Math.Clamp(index, 0, _results.Count - 1);
        _syncingSelection = true;
        ResultsList.SelectedIndex = index;
        ResultsList.ScrollIntoView(_results[index]);
        _syncingSelection = false;
    }

    void DispatchSelection()
    {
        if (ResultsList.SelectedItem is ResultRow row)
            DispatchRequested?.Invoke(row.Id);
        else if (_results.Count > 0)
            DispatchRequested?.Invoke(_results[0].Id);
    }

    public void Reset()
    {
        // WHY: the popup instance is cached in the Popup.Child between opens, so rows must be
        // rebuilt here — ModuleRegistry.Public is dynamic (AI feature gate, future flags) and
        // a stale list would dispatch modules that should stay hidden.
        BuildAllRows();
        _query = string.Empty;
        SearchBox.Text = string.Empty;
        ApplyFilter();
        SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text ?? string.Empty;
        ApplyFilter();
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            int next = ResultsList.SelectedIndex < 0 ? 0 : ResultsList.SelectedIndex + 1;
            SelectIndex(next);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            int prev = ResultsList.SelectedIndex <= 0 ? 0 : ResultsList.SelectedIndex - 1;
            SelectIndex(prev);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            DispatchSelection();
            e.Handled = true;
        }
    }

    private void ResultsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || ResultsList.SelectedItem is not ResultRow row)
            return;

        ResultsList.ScrollIntoView(row);
    }

    private void Result_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border { DataContext: ResultRow row })
            return;

        DispatchRequested?.Invoke(row.Id);
        e.Handled = true;
    }
}
