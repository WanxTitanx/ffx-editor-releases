using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using FFXProjectEditor.Modules.BattleKernel.Commands;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;

using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor;

public partial class KernelCommands_Control : UserControl, IRestorableModule
{
    private readonly KernelCommands_DataModel DataModel;
    private DateTime _suppressSelectionFxUntilUtc = DateTime.UtcNow.AddMilliseconds(300);
    private DateTime _suppressReferenceSelectionFxUntilUtc = DateTime.UtcNow.AddMilliseconds(300);
    public event Action<int>? OpenMonsterRequested;
    public event Action<int>? OpenMagicEffectRequested;

    public KernelCommands_Control(CommandFile_enum commandFileType)
    {
        DataModel = new KernelCommands_DataModel(commandFileType);
        this.DataContext = DataModel;
        InitializeComponent();
        ConfigureCapabilityBadge();
        AttachedToVisualTree += (_, _) =>
        {
            _suppressSelectionFxUntilUtc = DateTime.UtcNow.AddMilliseconds(280);
            _suppressReferenceSelectionFxUntilUtc = DateTime.UtcNow.AddMilliseconds(280);
        };
    }

    private void ConfigureCapabilityBadge()
    {
        CommandCapabilityBadge.SetCapability(new FFXProjectEditor.Core.CapabilityDescriptor
        {
            Id = "ability-command",
            Domain = "BattleKernel",
            Title = "Battle Commands & Ability Editor",
            Description = Strings.U_Kc_Description,
            Mode = FFXProjectEditor.Core.CapabilityMode.OfflineWriter,
            Evidence = FFXProjectEditor.Core.EvidenceLevel.Production,
            Platforms = new[] { FFXProjectEditor.Core.Platform.PC },
            RequiredDependencies = Array.Empty<string>(),
            OptionalDependencies = Array.Empty<string>(),
            Risks = Array.Empty<string>(),
            AllowedOperations = new[] { FFXProjectEditor.Core.AllowedOperation.Read, FFXProjectEditor.Core.AllowedOperation.Edit },
            ProhibitedOperations = Array.Empty<FFXProjectEditor.Core.AllowedOperation>(),
            Preconditions = Array.Empty<string>(),
            DocumentationLinks = Array.Empty<string>(),
            OwnerAgent = "Jarvis"
        });
    }

    private void Button_ReviewDiff_Click(object? sender, RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PlayEditorOpen();
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Main_Window mainWindow)
        {
            var preview = new Core.OperationPreview
            {
                OperationId = "ability-command-preview",
                DisplayName = "Battle Commands & Ability Data",
                FileCount = 1,
                TotalBytes = 2048,
                OverallRisk = Core.RiskLevel.Safe,
                FilePreviewSummaries = new System.Collections.Generic.List<Core.FilePreviewSummary>
                {
                    new Core.FilePreviewSummary
                    {
                        FileId = "ability_command.bin",
                        SourceRelativePath = "battle/kernel/ability_command.bin",
                        OutputRelativePath = "battle/kernel/ability_command.bin",
                        BeforeHash = Strings.F2_n_a_08d2e98e,
                        PredictedAfterHash = Strings.F2_n_a_08d2e98e,
                        ByteDiffLines = new System.Collections.Generic.List<string> { "Offset 0x0000..0x0800 [Modificado]" },
                        DisassemblyDiffLines = new System.Collections.Generic.List<string> { DataModel.EditSession?.BehaviorSummary ?? Strings.U_Kc_BehaviorFallback },
                        SemanticDiffLines = new System.Collections.Generic.List<string> { DataModel.EditSession?.SessionSummary ?? Strings.U_Kc_SessionFallback },
                        HumanSummary = Strings.F2_hardcoded_preview_adapted_to_v2_0711926b
                    }
                }
            };
            mainWindow.ShowChangeSetPreview(preview);
        }
    }

    private void Button_Save(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.Save();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_Undo(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.Undo();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Discard(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.Discard();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_LoadIngame(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.LoadInGame();
    }

    private void Filter_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        DataModel.ApplyFilter();
    }

    private void Button_CloneCommand(object? sender, RoutedEventArgs e)
    {
        DataModel.CloneSelectedCommand();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_DeleteCommand(object? sender, RoutedEventArgs e)
    {
        DataModel.DeleteSelectedCommand();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_AddCommand(object? sender, RoutedEventArgs e)
    {
        DataModel.AddCommandFromZero();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void CommandList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Use the event's sender, NOT the named field: during XAML EndInit the ListBox raises
        // SelectionChanged before the generated `CommandList` field is assigned (still null), and the 300ms
        // suppress window can already be expired if loading the command file took >300ms — touching the null
        // field there crashed the whole editor on open (monmagic2). The sender is always valid.
        if (DateTime.UtcNow < _suppressSelectionFxUntilUtc) return;
        if ((sender as ListBox)?.SelectedItem == null) return;

        AudioStudio_Service.Instance.PlayListSelection();
    }

    private void MonsterLinks_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Same init-order guard as CommandList: use the sender, not the named field. Auto-selecting the first
        // command during init populates the references list, which sets MonsterLinksList.SelectedItem and fires
        // this — while the named MonsterLinksList field is still null in EndInit. Touching it there crashed open.
        if (DateTime.UtcNow < _suppressReferenceSelectionFxUntilUtc) return;
        if ((sender as ListBox)?.SelectedItem == null) return;

        AudioStudio_Service.Instance.PlayListSelection();
    }

    private void Button_ViewAnim1Effect(object? sender, RoutedEventArgs e)
    {
        OpenMagicEffect(DataModel.SelectedCommand?.Anim1Id);
    }

    private void Button_ViewAnim2Effect(object? sender, RoutedEventArgs e)
    {
        OpenMagicEffect(DataModel.SelectedCommand?.Anim2Id);
    }

    private void Button_OpenAnim1EffectFolder(object? sender, RoutedEventArgs e)
    {
        DataModel.OpenAnimationEffectFolder(1);
    }

    private void Button_OpenAnim2EffectFolder(object? sender, RoutedEventArgs e)
    {
        DataModel.OpenAnimationEffectFolder(2);
    }

    private void OpenMagicEffect(short? magicId)
    {
        if (!magicId.HasValue || magicId.Value < 0)
        {
            return;
        }

        AudioStudio_Service.Instance.PlayConfirm();
        OpenMagicEffectRequested?.Invoke(magicId.Value);
    }

    private void MonsterLink_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not KernelCommandReferenceIndex_Service.CommandReferenceRow reference)
        {
            return;
        }

        OpenReferenceMonster(reference);
    }

    private void MenuItem_OpenReferenceMonster(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        KernelCommandReferenceIndex_Service.CommandReferenceRow? reference = (sender as MenuItem)?.CommandParameter as KernelCommandReferenceIndex_Service.CommandReferenceRow;
        if (reference == null)
        {
            reference = (sender as Control)?.DataContext as KernelCommandReferenceIndex_Service.CommandReferenceRow;
        }

        if (reference == null)
        {
            return;
        }

        OpenReferenceMonster(reference);
    }

    private void OpenReferenceMonster(KernelCommandReferenceIndex_Service.CommandReferenceRow reference)
    {
        MonsterLinksList.SelectedItem = reference;
        AudioStudio_Service.Instance.PlayConfirm();
        OpenMonsterRequested?.Invoke(reference.MonsterIndex);
    }

    public CommandFile_enum GetCommandFileType() => DataModel.CurrentCommandFileType;

    public int? GetSelectedCommandIndex() => DataModel.SelectedCommand?.Index;

    public string GetCurrentFilterText() => DataModel.FilterText;

    public void RestoreViewState(string? filterText, int? selectedCommandIndex)
    {
        _suppressSelectionFxUntilUtc = DateTime.UtcNow.AddMilliseconds(240);
        _suppressReferenceSelectionFxUntilUtc = DateTime.UtcNow.AddMilliseconds(240);
        DataModel.RestoreViewState(filterText, selectedCommandIndex);
    }

    async void Button_ImportCustomWav(object? sender, RoutedEventArgs e)
    {
        List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
            this,
            "Import replacement WAV",
            false,
            "sample.wav",
            new List<FilePickerFileType>
            {
                new("WAV audio") { Patterns = ["*.wav"] }
            });

        if (files.Count > 0)
            DataModel.SetCustomWavPath(files[0]);
    }

}
