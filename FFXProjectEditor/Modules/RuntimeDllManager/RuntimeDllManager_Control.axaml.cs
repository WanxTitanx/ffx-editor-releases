using Avalonia.Controls;
using Avalonia.Threading;
using FFXProjectEditor.Modules.RuntimeDllManager;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;
using System;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class RuntimeDllManager_Control : UserControl, IRestorableModule
{
    private readonly RuntimeDllManager_DataModel DataModel;
    private readonly DispatcherTimer _refreshTimer;

    public RuntimeDllManager_Control()
    {
        DataModel = new RuntimeDllManager_DataModel();
        DataContext = DataModel;
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
        Loaded += Control_Loaded;
        Unloaded += Control_Unloaded;
    }

    private void Control_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.RefreshStatus();
        _refreshTimer.Start();
    }

    private void Control_Unloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _refreshTimer.Stop();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        DataModel.RefreshStatus();
    }

    private void Button_DetectGameRoot(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.DetectGameRoot();
        DataModel.RefreshStatus();
    }

    // Explicit game-folder pick — the validated root is shared app-wide and persisted
    // (Project_Service.SetGameRoot), so this textbox no longer lives session-local only.
    private async void Button_BrowseGameRoot(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await AvaloniaDialog_Util.OpenFolderDialog(this, Strings.U_GameFolderPickerTitle);
        if (folders.Count == 0)
            return;
        if (!Project_Service.Instance.SetGameRoot(folders[0]))
        {
            await AvaloniaDialog_Util.ShowMessageAsync(this,
                Strings.U_GameInvalidFolderTitle,
                string.Format(Strings.U_GameInvalidFolderMessage, folders[0]));
            return;
        }
        DataModel.GameRoot = Project_Service.GameRootOverride ?? folders[0];
        DataModel.RefreshStatus();
    }

    private void Button_OpenTargetFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.OpenTargetFolder();
        DataModel.RefreshStatus();
    }

    private void Button_LaunchGame(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.LaunchGame();
    }

    private void Button_Refresh(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.RefreshStatus();
    }

    private void Button_ToggleDll(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: RuntimeDllItem item })
        {
            DataModel.ToggleDll(item);
        }
    }
}
