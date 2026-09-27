using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;

namespace FFXProjectEditor;

public class GameIndex_Template : TemplatedControl
{
    private readonly HashSet<Control> _audioBoundControls = [];
    private Control? _lastHoveredControl;
    private DateTime _lastHoverAtUtc = DateTime.MinValue;
    private DateTime _ignoreSelectionChangesUntilUtc = DateTime.MinValue;

    // REQUIRED to be passed as if it's tried to be loaded in the template the initial value won't show up :(
    public static readonly StyledProperty<List<string>> CategOptionsProperty = AvaloniaProperty.Register<TemplatedControl, List<string>>(nameof(CategOptions), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public List<string> CategOptions
    {
        get => GetValue(CategOptionsProperty);
        set => SetValue(CategOptionsProperty, value);
    }

    // Values
    public static readonly StyledProperty<string> GameIndex_LabelProperty = AvaloniaProperty.Register<TemplatedControl, string>(nameof(GameIndex_Label), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public string GameIndex_Label
    {
        get => GetValue(GameIndex_LabelProperty);
        set => SetValue(GameIndex_LabelProperty, value);
    }
    public static readonly StyledProperty<GameIndex_Wrapper> GameIndex_ObjectProperty = AvaloniaProperty.Register<TemplatedControl, GameIndex_Wrapper>(nameof(GameIndex_Object), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public GameIndex_Wrapper GameIndex_Object
    {
        get => GetValue(GameIndex_ObjectProperty);
        set => SetValue(GameIndex_ObjectProperty, value);
    }

    // Syling
    public static readonly StyledProperty<int> BorderWidthProperty = AvaloniaProperty.Register<TemplatedControl, int>(nameof(BorderWidth), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public int BorderWidth
    {
        get => GetValue(BorderWidthProperty);
        set => SetValue(BorderWidthProperty, value);
    }
    public static readonly StyledProperty<int> ValueBorderWidthProperty = AvaloniaProperty.Register<TemplatedControl, int>(nameof(ValueBorderWidth), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, defaultValue: 100);
    public int ValueBorderWidth
    {
        get => GetValue(ValueBorderWidthProperty);
        set => SetValue(ValueBorderWidthProperty, value);
    }
    public static readonly StyledProperty<bool> IsLabelVisibleProperty = AvaloniaProperty.Register<TemplatedControl, bool>(nameof(IsLabelVisible), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, defaultValue: true);
    public bool IsLabelVisible
    {
        get => GetValue(IsLabelVisibleProperty);
        set => SetValue(IsLabelVisibleProperty, value);
    }
    public static readonly StyledProperty<int> BorderThicknessProperty = AvaloniaProperty.Register<TemplatedControl, int>(nameof(BorderThickness), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay, defaultValue: 1);
    public int BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _ignoreSelectionChangesUntilUtc = DateTime.UtcNow.AddMilliseconds(220);

        BindAudio(e.NameScope.Find<TextBox>("PART_IndexTextBox"));
        BindAudio(e.NameScope.Find<TextBox>("PART_FilterTextBox"));
        BindAudio(e.NameScope.Find<ComboBox>("PART_CategoryComboBox"));
        BindAudio(e.NameScope.Find<ComboBox>("PART_OptionComboBox"));
    }

    private void BindAudio(Control? control)
    {
        if (control == null || !_audioBoundControls.Add(control))
        {
            return;
        }

        control.PointerEntered += InteractivePointerEntered;

        switch (control)
        {
            case TextBox textBox:
                textBox.GotFocus += InteractiveCommit;
                break;
            case ComboBox comboBox:
                comboBox.SelectionChanged += InteractiveSelectionChanged;
                comboBox.GotFocus += InteractiveCommit;
                break;
        }
    }

    private void InteractivePointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control || !control.IsEnabled || !control.IsVisible)
        {
            return;
        }

        if (ReferenceEquals(_lastHoveredControl, control) &&
            (DateTime.UtcNow - _lastHoverAtUtc).TotalMilliseconds < 90)
        {
            return;
        }

        _lastHoveredControl = control;
        _lastHoverAtUtc = DateTime.UtcNow;
        AudioStudio_Service.Instance.PlayMiniEditorHover();
    }

    private void InteractiveSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DateTime.UtcNow < _ignoreSelectionChangesUntilUtc)
        {
            return;
        }

        AudioStudio_Service.Instance.PlayMiniEditorConfirm();
    }

    private void InteractiveCommit(object? sender, RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PlayMiniEditorConfirm();
    }
}
