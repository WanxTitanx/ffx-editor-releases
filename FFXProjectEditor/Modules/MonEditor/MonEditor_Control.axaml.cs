using FFXProjectEditor.Resources;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class MonEditor_Control : UserControl, IRestorableModule
{
    private readonly MonEditor_DataModel DataModel;
    private readonly HashSet<Control> _miniAudioBoundControls = [];
    private Control? _lastMiniHoveredControl;
    private DateTime _lastMiniHoverAtUtc = DateTime.MinValue;

    public MonEditor_Control(Monster_File monFile, string monsterPath, MonEditorSelector_DataModel selectorDM)
    {
        DataModel = new MonEditor_DataModel(monFile, monsterPath, selectorDM);
        this.DataContext = DataModel;
        InitializeComponent();
        ConfigureCapabilityBadge();
        RegisterMiniEditorAudioHandlers();
        AttachedToVisualTree += (_, _) => RegisterMiniEditorAudioHandlers();
        DataModel.PropertyChanged += DataModel_PropertyChanged;
        Loaded += OnModelsPreviewLoaded;
    }

    private void ConfigureCapabilityBadge()
    {
        MonsterCapabilityBadge.SetCapability(new FFXProjectEditor.Core.CapabilityDescriptor
        {
            Id = "monster-stats",
            Domain = "Monster",
            Title = "Monster Editor",
            Description = Strings.U_Mon_Description,
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

    private void OnModelsPreviewLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        NavigateModelPreview();
    }

    private void DataModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonEditor_DataModel.ModelPreviewUrl))
            NavigateModelPreview();
    }

    private void NavigateModelPreview()
    {
        // Non-Windows: no WebView2 embed — the fallback card + browser button own the preview.
        if (!DataModel.IsEmbeddedModelPreviewSupported)
            return;

        string? url = DataModel.ModelPreviewUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            // Clear a previously rendered preview when the current platform/capability cannot
            // produce a URL. The status TextBlock remains visible with the explicit reason.
            Dispatcher.UIThread.Post(
                () => ModelPreviewHost.Navigate("about:blank"),
                DispatcherPriority.Background);
            return;
        }

        Dispatcher.UIThread.Post(() => ModelPreviewHost.Navigate(url), DispatcherPriority.Background);
    }

    private void Button_ApplyVariantMainModel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ApplyVariantMainModelPair();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_OpenModelPreviewBrowser(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.OpenModelPreviewInBrowser();
    }

    private async void Button_ReviewDiff(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PlayEditorOpen();

        string monsterName = "Monster Data";
        string sessionSummary = DataModel.EditSession?.SessionSummary ?? Strings.U_Mon_PendingChangesFallback;
        string behaviorSummary = DataModel.EditSession?.BehaviorSummary ?? Strings.U_Mon_BehaviorFallback;

        var preview = new Core.OperationPreview
        {
            OperationId = "mon-data",
            DisplayName = $"Monster Data ({monsterName})",
            FileCount = 1,
            TotalBytes = 1280,
            OverallRisk = Core.RiskLevel.Safe,
            FilePreviewSummaries = new List<Core.FilePreviewSummary>
            {
                new Core.FilePreviewSummary
                {
                    FileId = monsterName + " (Data)",
                    SourceRelativePath = "mon_data.bin",
                    OutputRelativePath = "mon_data.bin",
                    BeforeHash = "before_snapshot",
                    PredictedAfterHash = "after_snapshot",
                    ByteDiffLines = new[] { Strings.U_Mon_DiffByteLine },
                    DisassemblyDiffLines = Array.Empty<string>(),
                    SemanticDiffLines = new[]
                    {
                        string.Format(Strings.U_Mon_MonsterBullet, monsterName),
                        string.Format(Strings.U_Mon_SummaryBullet, sessionSummary),
                        string.Format(Strings.U_Mon_FieldsBullet, behaviorSummary)
                    },
                    HumanSummary = $"{sessionSummary}: {behaviorSummary}"
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
                Title = Strings.U_Mon_ReviewTitle + monsterName,
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

    private void Button_ClearMirrorTargets(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ClearMirrorTargets();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void RegisterMiniEditorAudioHandlers()
    {
        foreach (Control control in this.GetVisualDescendants().OfType<Control>())
        {
            if (control is CheckBox checkBox)
            {
                if (_miniAudioBoundControls.Add(checkBox))
                {
                    checkBox.PointerEntered += MiniEditorHover;
                    checkBox.Checked += MiniEditorToggleChanged;
                    checkBox.Unchecked += MiniEditorToggleChanged;
                }
            }
            else if (control is TextBox textBox)
            {
                if (_miniAudioBoundControls.Add(textBox))
                {
                    textBox.PointerEntered += MiniEditorHover;
                    textBox.GotFocus += MiniEditorFocus;
                }
            }
        }
    }

    private void MiniEditorHover(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control || !control.IsEnabled || !control.IsVisible)
        {
            return;
        }

        if (ReferenceEquals(_lastMiniHoveredControl, control) &&
            (DateTime.UtcNow - _lastMiniHoverAtUtc).TotalMilliseconds < 90)
        {
            return;
        }

        _lastMiniHoveredControl = control;
        _lastMiniHoverAtUtc = DateTime.UtcNow;
        AudioStudio_Service.Instance.PlayMiniEditorHover();
    }

    private void MiniEditorToggleChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PlayMiniEditorConfirm();
    }

    private void MiniEditorFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PlayMiniEditorConfirm();
    }
}
