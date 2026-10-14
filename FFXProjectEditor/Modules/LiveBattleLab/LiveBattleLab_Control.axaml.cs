using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Modules.LiveBattleLab;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;
using System.Collections.Generic;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class LiveBattleLab_Control : UserControl, IRestorableModule
{
    readonly LiveBattleLab_DataModel dataModel;

    public LiveBattleLab_Control()
    {
        dataModel = new LiveBattleLab_DataModel();
        DataContext = dataModel;
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        dataModel.StartRuntimeTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        dataModel.StopRuntimeTimer();
    }

    void PlayActionFx()
    {
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshRuntime();
        PlayActionFx();
    }

    private void Button_UseLiveRouting(object? sender, RoutedEventArgs e)
    {
        dataModel.UseLiveRouting();
        PlayActionFx();
    }

    private void Button_ForceCurrent(object? sender, RoutedEventArgs e)
    {
        dataModel.ForceCurrentBattle();
        PlayActionFx();
    }

    private void Button_ForceBattle(object? sender, RoutedEventArgs e)
    {
        dataModel.ForceBattle();
        PlayActionFx();
    }

    private void Button_RepeatEncounter(object? sender, RoutedEventArgs e)
    {
        dataModel.RepeatEncounter();
        PlayActionFx();
    }

    private void Button_ApplySandbox(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplySandboxChanges();
        PlayActionFx();
    }

    private void Button_RecalculateSandboxPlan(object? sender, RoutedEventArgs e)
    {
        dataModel.RecalculateSandboxPlan();
        PlayActionFx();
    }

    private void Button_PrepareSandboxRoute(object? sender, RoutedEventArgs e)
    {
        dataModel.PrepareSandboxRoute();
        PlayActionFx();
    }

    private void Button_ForceSandbox(object? sender, RoutedEventArgs e)
    {
        dataModel.ForceSandboxBattle();
        PlayActionFx();
    }

    private void Button_ReloadCommands(object? sender, RoutedEventArgs e)
    {
        dataModel.ReloadCommandsIngame();
        PlayActionFx();
    }

    private void Button_ReloadItems(object? sender, RoutedEventArgs e)
    {
        dataModel.ReloadItemsIngame();
        PlayActionFx();
    }

    private void Button_ReloadMonMagic1(object? sender, RoutedEventArgs e)
    {
        dataModel.ReloadMonMagic1Ingame();
        PlayActionFx();
    }

    private void Button_ReloadMonMagic2(object? sender, RoutedEventArgs e)
    {
        dataModel.ReloadMonMagic2Ingame();
        PlayActionFx();
    }

    private void Button_ReloadAutoAbilities(object? sender, RoutedEventArgs e)
    {
        dataModel.ReloadAutoAbilitiesIngame();
        PlayActionFx();
    }

    private void Button_ReloadAeonGrow(object? sender, RoutedEventArgs e)
    {
        dataModel.ReloadAeonGrowIngame();
        PlayActionFx();
    }

    private void Button_ReloadCustomizations(object? sender, RoutedEventArgs e)
    {
        dataModel.ReloadCustomizationsIngame();
        PlayActionFx();
    }

    private void Button_CaptureNaturalSample(object? sender, RoutedEventArgs e)
    {
        dataModel.CaptureNaturalSample();
        PlayActionFx();
    }

    private async void Button_ExportNaturalCapture(object? sender, RoutedEventArgs e)
    {
        string path = await AvaloniaDialog_Util.SaveFileDialog(
            this,
            "Export Natural Capture (Pt47/Pt48)",
            "ffx_natural_capture.csv",
            "csv",
            new List<FilePickerFileType>
            {
                new("CSV") { Patterns = new[] { "*.csv" } }
            });

        if (!string.IsNullOrEmpty(path))
        {
            dataModel.ExportNaturalCaptureCsv(path);
        }

        PlayActionFx();
    }

    private void Button_ClearNaturalCaptures(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearNaturalCaptures();
        PlayActionFx();
    }

    private void Button_ReadDebug(object? sender, RoutedEventArgs e)
    {
        dataModel.ReadDebugFlagsFromRuntime();
        PlayActionFx();
    }

    private void Button_ApplyDebug(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyDebugFlags();
        PlayActionFx();
    }
}
