using Avalonia.Controls;
using FFXProjectEditor.Modules.DebugMenu;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class DebugMenu_Control : UserControl, IRestorableModule
{
    DebugMenu_DataModel DataModel;
    public int SwitchSize { get; set; } = 500;
    public DebugMenu_Control()
    {
        DataModel = new DebugMenu_DataModel();
        this.DataContext = DataModel;
        InitializeComponent();
    }

    private void Toggle_ControlOn(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        //DataModel.EnableControl(true);
        DataModel.WriteBtlDebug();
    }

    private void Toggle_ControlOff(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        //DataModel.EnableControl(false);
        DataModel.WriteBtlDebug();
    }

    private void ToggleSwitch_Write(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.WriteBtlDebug();
    }

    private void Button_Apply(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.WriteBtlDebug();
    }

    private void Button_WinMatch(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.WinBlitzballGame();
    }
    private void Button_WinHalf(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.WinBlitzballGame(false);
    }

    private void Button_SetBlitzballWins(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.WriteBliztballWins();
    }

    private void Button_WinChocoboRace(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.WinChocoboRace();
    }
    private void Button_ResetChocoboTimer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ResetChocoboTimer();
    }

    private void Button_WriteOverdriveData(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.WriteOverdriveData();
    }

    private void Button_SetOtherData(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.WriteOtherData();
    }

    private void Button_YojimboMin(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ApplyYojimboCompatibilityPreset(DebugMenu_DataModel.YojimboCompatibilityMinValue);
    }

    private void Button_YojimboDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ApplyYojimboCompatibilityPreset(DebugMenu_DataModel.YojimboCompatibilityHdDefaultValue);
    }

    private void Button_YojimboMax(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ApplyYojimboCompatibilityPreset(DebugMenu_DataModel.YojimboCompatibilityMaxValue);
    }

    private void Button_LightningReward30(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ApplyLightningCombinedPreset(DebugMenu_DataModel.LightningCombinedRewardEther);
    }

    private void Button_LightningReward80(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ApplyLightningCombinedPreset(DebugMenu_DataModel.LightningCombinedRewardElixir);
    }

    private void Button_LightningRecord200(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ApplyLightningRecordPreset(DebugMenu_DataModel.LightningRecordRewardSigil);
    }

    private void Button_LightningReset(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ResetLightningCounters();
    }

    private void Button_SetTempleSeals(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.WriteTempleSeals();
    }

    private void Button_SetAlbhedPrimers(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.WriteAlbhedCharacters();
    }
}
