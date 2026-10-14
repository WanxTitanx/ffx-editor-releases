using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFXProjectEditor.FfxLib.Save;
using FFXProjectEditor.Modules.SaveEditor;
using FFXProjectEditor.Services;
using FFXProjectEditor.Services.ReleaseRuntime;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;

using FFXProjectEditor.Modules.Common;
using System.Linq;
using Avalonia.VisualTree;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor;

public partial class SaveEditorHub_Control : UserControl, IRestorableModule
{
    readonly SaveEditor_DataModel dataModel = new();
    readonly Control?[] tabCache = new Control?[7];

    // Jarvis-UI (Save Editor Hub Phase 2 2026-06-20): o combo de MC slot deixou de usar binding TwoWay
    // em SelectedIndex (que trocava o slot direto, descartando dirty sem pedir). Agora a seleção passa
    // por MemoryCardSlotCombo_SelectionChanged, que confirma quando IsDirty antes de chamar
    // SwitchMemoryCardSlot. A sincronização combo<-DataModel (quando o slot muda via Load/Switch) é
    // feita aqui via PropertyChanged; este flag evita re-entrância do handler durante essa sincronização.
    bool suppressSlotComboSync;

    public SaveEditorHub_Control()
    {
        InitializeComponent();
        DataContext = dataModel;
        dataModel.PropertyChanged += OnDataModelPropertyChanged;
        SelectTab(0);
        ConfigureCapabilityBadge();
    }

    private void ConfigureCapabilityBadge()
    {
        SaveCapabilityBadge.SetCapability(new FFXProjectEditor.Core.CapabilityDescriptor
        {
            Id = "save-editor",
            Domain = "SaveFile",
            Title = "Save Editor (FFXED port — .mch / .psu / PC)",
            Description = Strings.U_Sve_Description,
            Mode = FFXProjectEditor.Core.CapabilityMode.OfflineWriter,
            Evidence = FFXProjectEditor.Core.EvidenceLevel.Production,
            Platforms = new[] { FFXProjectEditor.Core.Platform.PC },
            RequiredDependencies = System.Array.Empty<string>(),
            OptionalDependencies = System.Array.Empty<string>(),
            Risks = new[] { Strings.F2_save_backup_recommended_before_editing_1d6aaca5 },
            AllowedOperations = new[] { FFXProjectEditor.Core.AllowedOperation.Read, FFXProjectEditor.Core.AllowedOperation.Edit },
            ProhibitedOperations = System.Array.Empty<FFXProjectEditor.Core.AllowedOperation>(),
            Preconditions = System.Array.Empty<string>(),
            DocumentationLinks = System.Array.Empty<string>(),
            OwnerAgent = "Jarvis"
        });
    }

    void OnDataModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SaveEditor_DataModel.SelectedMemoryCardSlotIndex))
            return;

        if (MemoryCardSlotCombo.SelectedIndex != dataModel.SelectedMemoryCardSlotIndex)
        {
            suppressSlotComboSync = true;
            MemoryCardSlotCombo.SelectedIndex = dataModel.SelectedMemoryCardSlotIndex;
            suppressSlotComboSync = false;
        }
    }

    Control BuildTab(int index) => index switch
    {
        0 => new SaveEditorCharacter_Control { DataContext = dataModel },
        1 => new SaveEditorEquipment_Control { DataContext = dataModel },
        2 => new SaveEditorItems_Control { DataContext = dataModel },
        3 => new SaveEditorBlitzball_Control { DataContext = dataModel },
        4 => new SaveEditorSphereGrid_Control { DataContext = dataModel },
        5 => new SaveEditorMinigame_Control { DataContext = dataModel },
        6 => new SaveEditorMiscImport_Control { DataContext = dataModel },
        _ => new SaveEditorCharacter_Control { DataContext = dataModel },
    };

    async void Button_Load(object? sender, RoutedEventArgs e)
    {
        // Jarvis-UI (Save Editor Hub Phase 2 2026-06-20): antes de substituir a sessão por um novo
        // Load, confirmar o descarte se houver alterações não salvas (P2 do prompt). O Load é a única
        // operação que sobrescreve o blob; trocar de sub-aba NÃO pede confirmação.
        if (dataModel.IsDirty && !await ConfirmDiscardAsync())
            return;

        List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
            this,
            "Open FFX Save",
            fileTypeFilter:
            [
                new FilePickerFileType("FFX saves") { Patterns = ["*.*", "*.psu", "*.bin", "*.ffx", "*.ps2"] },
            ]);

        if (files.Count == 0)
            return;

        try
        {
            dataModel.LoadFromPath(files[0]);
            for (int i = 0; i < tabCache.Length; i++)
                tabCache[i] = null;
            SelectTab(0);
            AudioStudio_Service.Instance.PlayConfirm();
        }
        catch (Exception ex)
        {
            dataModel.StatusSummary = $"Erro ao carregar: {ex.Message}";
        }
    }

    /// <summary>
    /// Jarvis-UI (Save Editor Hub Phase 2 2026-06-20): confirma o descarte de alterações pendentes.
    /// Usado antes de operações que substituem o blob (Load e troca de slot MC).
    /// </summary>
    async System.Threading.Tasks.Task<bool> ConfirmDiscardAsync()
        => await AvaloniaDialog_Util.ConfirmYesNoAsync(
            this,
            Strings.F2_discard_unsaved_changes_e9f81751,
            Strings.F2_there_are_unsaved_changes_in_the_current_8b10cd63,
            yesLabel: "Descartar",
            noLabel: "Cancelar");

    /// <summary>
    /// Jarvis-UI (Save Editor Hub Phase 2 2026-06-20): troca de slot de memory card via combo.
    /// Substitui o antigo binding TwoWay (que trocava direto e só avisava em StatusSummary —
    /// fácil de o usuário não ver). Agora confirma o descarte quando IsDirty antes de chamar
    /// SwitchMemoryCardSlot; se cancelar, reverte o combo para o slot corrente.
    /// </summary>
    async void MemoryCardSlotCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (suppressSlotComboSync)
            return;

        if (sender is not ComboBox combo)
            return;

        int requested = combo.SelectedIndex;
        int current = dataModel.SelectedMemoryCardSlotIndex;

        // Coincide com o slot já carregado (ex.: populate inicial) — nada a fazer.
        if (requested < 0 || requested == current)
            return;

        if (dataModel.IsDirty && !await ConfirmDiscardAsync())
        {
            // Cancelou: reverte o combo para o slot corrente sem disparar a troca.
            suppressSlotComboSync = true;
            combo.SelectedIndex = current;
            suppressSlotComboSync = false;
            return;
        }

        dataModel.SwitchMemoryCardSlot(requested);
    }

    void Button_Save(object? sender, RoutedEventArgs e)
    {
        try
        {
            dataModel.ApplySelectedCharacter();
            dataModel.SaveCurrent();
            AudioStudio_Service.Instance.PlayConfirm();
        }
        catch (Exception ex)
        {
            dataModel.StatusSummary = $"Erro ao salvar: {ex.Message}";
        }
    }

    private async void Button_ReviewDiff_Click(object? sender, RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PlayEditorOpen();

        string filename = "Save File";
        
        var preview = new Core.OperationPreview
        {
            OperationId = "save-editor-diff",
            DisplayName = $"Save Editor ({filename})",
            FileCount = 1,
            TotalBytes = 25000,
            OverallRisk = Core.RiskLevel.Safe,
            FilePreviewSummaries = new System.Collections.Generic.List<Core.FilePreviewSummary>
            {
                new Core.FilePreviewSummary
                {
                    FileId = filename,
                    SourceRelativePath = $"{filename}",
                    OutputRelativePath = $"{filename}",
                    BeforeHash = "before",
                    PredictedAfterHash = "after",
                    ByteDiffLines = new[] { "0x0000: [Diff do Memory Card / Save]" },
                    DisassemblyDiffLines = System.Array.Empty<string>(),
                    SemanticDiffLines = new[]
                    {
                        string.Format(Strings.U_Sve_SaveBullet, filename),
                        string.Format(Strings.U_Sve_SlotBullet, dataModel.SelectedMemoryCardSlotIndex),
                        Strings.U_Sve_ChecksumBullet
                    },
                    HumanSummary = string.Format(Strings.U_Sve_HumanSummary, filename)
                }
            }
        };

        var mainWindow = this.GetVisualAncestors().OfType<Main_Window>().FirstOrDefault();
        if (mainWindow != null)
        {
            mainWindow.ShowChangeSetPreview(preview);
        }
        else
        {
            var window = new Window
            {
                Title = Strings.U_Sve_ReviewTitle + filename,
                Width = 550,
                Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new Controls.DiffSummaryView
                {
                    DataContext = preview.FilePreviewSummaries[0]
                }
            };
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel != null) await window.ShowDialog(topLevel);
            else window.Show();
        }
    }

    async void Button_SaveAs(object? sender, RoutedEventArgs e)
    {
        string path = await AvaloniaDialog_Util.SaveFileDialog(this, "Save FFX Save As", "save.psu");
        if (string.IsNullOrWhiteSpace(path))
            return;

        FfxSaveFormat format = path.EndsWith(".ffx", StringComparison.OrdinalIgnoreCase)
            ? FfxSaveFormat.PcFfx
            : FfxSaveFormat.Psu;

        try
        {
            dataModel.ApplySelectedCharacter();
            dataModel.SaveCurrentAs(path, format);
            AudioStudio_Service.Instance.PlayConfirm();
        }
        catch (Exception ex)
        {
            dataModel.StatusSummary = $"Erro no Save As: {ex.Message}";
        }
    }

    void Button_LaunchFfxed(object? sender, RoutedEventArgs e)
    {
        FfxedLaunchResult result = new FfxedRuntimeLauncher().Launch();
        dataModel.StatusSummary = result.Failure switch
        {
            FfxedLaunchFailure.None when result.Started => Strings.U_Sve_FfxedStarted,
            FfxedLaunchFailure.PrivateJavaRuntimeMissing => Strings.U_Sve_FfxedPrivateRuntimeMissing,
            FfxedLaunchFailure.BundledJarMissing => Strings.U_Sve_FfxedBundledJarMissing,
            FfxedLaunchFailure.JarHashMismatch => Strings.U_Sve_FfxedJarInvalid,
            _ => string.Format(
                Strings.U_Sve_FfxedStartFailedFormat,
                result.Detail ?? Strings.U_Sve_FfxedProcessRejected),
        };
    }

    /// <summary>
    /// Jarvis-UI (OPT-A4 2026-06-20): copy-on-click do hash SHA256 completo. O header mostra só os
    /// primeiros 12 chars + "…"; clicar copia os 64 chars do <see cref="SaveEditor_DataModel.PayloadHashFull"/>
    /// para o clipboard. Feedback é muted (não sobrescreve o StatusSummary de load/save/erro) pra não
    /// brigar com o chip dirty — o cursor Hand + tooltip já sinalizam a affordance.
    /// </summary>
    async void PayloadHash_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        string full = dataModel.PayloadHashFull;
        if (string.IsNullOrEmpty(full))
            return;

        try
        {
            await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(full) ?? System.Threading.Tasks.Task.CompletedTask);
        }
        catch
        {
            // Clipboard pode falhar em headless/SSH; não é crítico (o hash curto segue visível no header).
        }
    }

    void Tab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag && int.TryParse(tag, out int index))
        {
            SelectTab(index);
            AudioStudio_Service.Instance.PlayAlternative();
        }
    }

    void SelectTab(int index)
    {
        tabCache[index] ??= BuildTab(index);
        TabHost.Content = tabCache[index];

        // Jarvis-UI (Save Editor audit 2026-06-20): manter a pill/tab ativo em sincronia com a
        // seção corrente. Antes só o botão Character tinha tabPillActive estático no XAML e SelectTab
        // nunca atualizava os demais. Agora limpa de todos e aplica só no índice selecionado — mesmo
        // padrão do SubTabHub_Control.SelectTab.
        foreach (var child in SectionNav.Children)
        {
            if (child is Button b && b.Tag is string tag && int.TryParse(tag, out int i))
            {
                bool active = i == index;
                if (active && !b.Classes.Contains("tabPillActive"))
                    b.Classes.Add("tabPillActive");
                else if (!active)
                    b.Classes.Remove("tabPillActive");
            }
        }
    }
}
