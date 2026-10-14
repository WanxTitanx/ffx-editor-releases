using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.EventExplorer;
using FFXProjectEditor.Services;
using FFXProjectEditor.Resources;

using FFXProjectEditor.Modules.Common;
using System.Linq;
using Avalonia.VisualTree;
namespace FFXProjectEditor;

public partial class EventExplorer_Control : UserControl, IRestorableModule
{
    readonly EventExplorer_DataModel dataModel;

    public EventExplorer_Control()
    {
        dataModel = new EventExplorer_DataModel();
        DataContext = dataModel;
        InitializeComponent();
        ConfigureCapabilityBadge();
    }

    private void ConfigureCapabilityBadge()
    {
        EventCapabilityBadge.SetCapability(new FFXProjectEditor.Core.CapabilityDescriptor
        {
            Id = "event-explorer",
            Domain = "EventScript",
            Title = "Event Explorer (PARSE_EVENT)",
            Description = Strings.U_Ee_Description,
            Mode = FFXProjectEditor.Core.CapabilityMode.ReadOnly,
            Evidence = FFXProjectEditor.Core.EvidenceLevel.Production,
            Platforms = new[] { FFXProjectEditor.Core.Platform.PC },
            RequiredDependencies = System.Array.Empty<string>(),
            OptionalDependencies = System.Array.Empty<string>(),
            Risks = System.Array.Empty<string>(),
            AllowedOperations = new[] { FFXProjectEditor.Core.AllowedOperation.Read },
            ProhibitedOperations = new[] { FFXProjectEditor.Core.AllowedOperation.Edit },
            Preconditions = System.Array.Empty<string>(),
            DocumentationLinks = System.Array.Empty<string>(),
            OwnerAgent = "Jarvis"
        });
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshFromDisk();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

private void Button_Save(object? sender, RoutedEventArgs e)
    {
        dataModel.SaveSelectedEvent();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

    private void Button_ApplyPatch(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPatchedOperand();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

    private async void Button_ReviewDiff_Click(object? sender, RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PlayEditorOpen();

        string eventName = dataModel.SelectedEvent?.EventId ?? Strings.U_Ee_Event;
        
        var preview = new Core.OperationPreview
        {
            OperationId = "event-explorer-diff",
            DisplayName = $"Event Explorer ({eventName})",
            FileCount = 1,
            TotalBytes = 256,
            OverallRisk = Core.RiskLevel.Safe,
            FilePreviewSummaries = new System.Collections.Generic.List<Core.FilePreviewSummary>
            {
                new Core.FilePreviewSummary
                {
                    FileId = eventName,
                    SourceRelativePath = $"{eventName}",
                    OutputRelativePath = $"{eventName}",
                    BeforeHash = "before",
                    PredictedAfterHash = "after",
                    ByteDiffLines = new[] { Strings.U_Ee_DiffLine },
                    DisassemblyDiffLines = System.Array.Empty<string>(),
                    SemanticDiffLines = new[]
                    {
                        string.Format(Strings.U_Ee_EventBullet, eventName),
                        Strings.U_Ee_OperandsChanged
                    },
                    HumanSummary = string.Format(Strings.U_Ee_OperandEdit, eventName)
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
                Title = Strings.U_Ee_ReviewTitle + eventName,
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
}
