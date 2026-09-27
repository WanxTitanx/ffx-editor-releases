using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.DifficultyDirector;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class DifficultyDirector_Control : UserControl, IRestorableModule
{
    readonly DifficultyDirector_DataModel dataModel;

    public DifficultyDirector_Control()
    {
        dataModel = new DifficultyDirector_DataModel();
        DataContext = dataModel;
        InitializeComponent();
    }

    private void Button_PresetEasy(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPreset(Strings.U_Dd_PresetEasy);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetNormal(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPreset("Normal");
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetHard(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPreset(Strings.U_Dd_PresetHard);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetDarkAeon(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPreset("Dark Aeon");
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetExplorer(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPreset(Strings.U_Dd_PresetExploration);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetHunter(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPreset(Strings.U_Dd_PresetHunter);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ChallengeTrueNightmare(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyChallengeMode("True Nightmare");
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ChallengeSpeedRun(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyChallengeMode("Speed Run Assist");
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ChallengeExplorer(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyChallengeMode("Explorer");
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ChallengeHunter(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyChallengeMode("Hunter");
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ChallengeEqualizer(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyChallengeMode("Equalizer");
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RefreshPreview(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshPreview();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ApplyAll(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyToAll();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RestoreBackups(object? sender, RoutedEventArgs e)
    {
        dataModel.RestoreBackups();
        AudioStudio_Service.Instance.PlayAlternative();
    }
}
