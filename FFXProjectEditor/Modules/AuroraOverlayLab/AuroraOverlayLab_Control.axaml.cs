using Avalonia.Controls;
using FFXProjectEditor.Modules.AuroraOverlayLab;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class AuroraOverlayLab_Control : UserControl, IRestorableModule
{
    private readonly AuroraOverlayLab_DataModel DataModel;

    public AuroraOverlayLab_Control()
    {
        DataModel = new AuroraOverlayLab_DataModel();
        DataContext = DataModel;
        InitializeComponent();
    }

    private void Button_DetectGameRoot(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.DetectGameRoot();
        DataModel.LoadExistingConfig();
        DataModel.RefreshStatus();
    }

    private void Button_LoadConfig(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.LoadExistingConfig();
    }

    private void Button_OpenConfigFolder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.OpenConfigFolder();
        DataModel.RefreshStatus();
    }

    private void Button_OpenLog(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.OpenTempLog();
    }

    private void Button_LaunchGame(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.LaunchGame();
    }

    private void Button_ApplyConfig(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ApplyConfig();
    }

    private void Button_DisableOverlay(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.DisableOverlay();
    }

    private void Button_Refresh(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.RefreshStatus();
    }
}
