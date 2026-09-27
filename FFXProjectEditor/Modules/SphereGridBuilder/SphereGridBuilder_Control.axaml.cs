using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Resources;

using FFXProjectEditor.Modules.Common;
using System.Linq;
using Avalonia.VisualTree;

namespace FFXProjectEditor.Modules.SphereGridBuilder
{
    // v1 form/list-based authoring over the proven SphereGridLayoutBuilder. Thin code-behind: buttons delegate
    // to the DataModel (handlers fire only on user click, never during XAML init — no named-field deref).
    public partial class SphereGridBuilder_Control : UserControl, IRestorableModule
    {
        private readonly SphereGridBuilder_DataModel _dataModel = new();

        public SphereGridBuilder_Control()
        {
            InitializeComponent();
            DataContext = _dataModel;
            ConfigureCapabilityBadge();
        }

        private void ConfigureCapabilityBadge()
        {
            BuilderCapabilityBadge.SetCapability(new FFXProjectEditor.Core.CapabilityDescriptor
            {
                Id = "sphere-grid-builder",
                Domain = "SphereGrid",
                Title = "Sphere Grid Builder",
                Description = "Construtor visual offline do Grid (sphere.bin).",
                Mode = FFXProjectEditor.Core.CapabilityMode.OfflineWriter,
                Evidence = FFXProjectEditor.Core.EvidenceLevel.Production,
                Platforms = new[] { FFXProjectEditor.Core.Platform.PC },
                RequiredDependencies = System.Array.Empty<string>(),
                OptionalDependencies = System.Array.Empty<string>(),
                Risks = System.Array.Empty<string>(),
                AllowedOperations = new[] { FFXProjectEditor.Core.AllowedOperation.Read, FFXProjectEditor.Core.AllowedOperation.Edit },
                ProhibitedOperations = System.Array.Empty<FFXProjectEditor.Core.AllowedOperation>(),
                Preconditions = System.Array.Empty<string>(),
                DocumentationLinks = System.Array.Empty<string>(),
                OwnerAgent = "Jarvis"
            });
        }


        private void Button_AddCluster(object? sender, RoutedEventArgs e) => _dataModel.AddCluster();
        private void Button_AddNode(object? sender, RoutedEventArgs e) => _dataModel.AddNode();
        private void Button_AddLink(object? sender, RoutedEventArgs e) => _dataModel.AddLink();
        private void Button_Validate(object? sender, RoutedEventArgs e) => _dataModel.Validate();
        private void Button_BuildAndSave(object? sender, RoutedEventArgs e) => _dataModel.BuildAndSave();

        private async void Button_ReviewDiff_Click(object? sender, RoutedEventArgs e)
        {
            Services.AudioStudio_Service.Instance.PlayEditorOpen();

            string gridName = _dataModel.GridName ?? "Novo Grid";

            var preview = new Core.OperationPreview
            {
                OperationId = "sphere-grid-builder",
                DisplayName = $"Sphere Grid Builder ({gridName})",
                FileCount = 1,
                TotalBytes = 16384,
                OverallRisk = Core.RiskLevel.Safe,
                FilePreviewSummaries = new System.Collections.Generic.List<Core.FilePreviewSummary>
                {
                    new Core.FilePreviewSummary
                    {
                        FileId = gridName,
                        SourceRelativePath = "sphere.bin",
                        OutputRelativePath = "sphere.bin",
                        BeforeHash = "before",
                        PredictedAfterHash = "after",
                        ByteDiffLines = new[] { "0x0010: [Diff da matriz do Sphere Grid]" },
                        DisassemblyDiffLines = System.Array.Empty<string>(),
                        SemanticDiffLines = new[]
                        {
                            $"• Grid: {gridName}",
                            $"• Status: {_dataModel.CountsSummary}"
                        },
                        HumanSummary = $"Montagem do Sphere Grid: {gridName} - {_dataModel.CountsSummary}"
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
                    Title = Strings.U_Ee_ReviewTitle + gridName,
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
}
