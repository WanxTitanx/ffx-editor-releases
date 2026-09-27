using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FFXProjectEditor.Controls.SphereGrid;
using FFXProjectEditor.Modules.SphereGridExplorer;
using FFXProjectEditor.Services;
using System;
using FFXProjectEditor.Resources;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class SphereGridExplorer_Control : UserControl, IRestorableModule
{
    readonly SphereGridExplorer_DataModel dataModel;
    SphereGridPreview_Control? previewSurface;
    bool isPreviewPanning;
    bool previewPressStartedOnNode;
    Point previewPanAnchor;
    Vector previewPanStart;

    public SphereGridExplorer_Control()
    {
        dataModel = new SphereGridExplorer_DataModel();
        DataContext = dataModel;
        InitializeComponent();

        previewSurface = this.FindControl<SphereGridPreview_Control>("PreviewSurface");
        if (previewSurface != null)
        {
            previewSurface.NodeInvoked += Preview_NodeInvoked;
            previewSurface.PointerWheelChanged += PreviewSurface_PointerWheelChanged;
            previewSurface.PointerPressed += PreviewSurface_PointerPressed;
            previewSurface.PointerMoved += PreviewSurface_PointerMoved;
            previewSurface.PointerReleased += PreviewSurface_PointerReleased;
            previewSurface.PointerCaptureLost += PreviewSurface_PointerCaptureLost;
        }
        ConfigureCapabilityBadge();
    }

    private void ConfigureCapabilityBadge()
    {
        SphereGridCapabilityBadge.SetCapability(new FFXProjectEditor.Core.CapabilityDescriptor
        {
            Id = "sphere-grid",
            Domain = "SphereGrid",
            Title = "Sphere Grid Editor (sphere.bin)",
            Description = Strings.U_Sge_Description,
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

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshFromDisk();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

    private void Button_ReviewDiff_Click(object? sender, RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PlayEditorOpen();
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Main_Window mainWindow)
        {
            var preview = new Core.OperationPreview
            {
                OperationId = "sphere-grid-preview",
                DisplayName = "Sphere Grid (sphere.bin)",
                FileCount = 1,
                TotalBytes = 4096,
                OverallRisk = Core.RiskLevel.Safe,
                FilePreviewSummaries = new System.Collections.Generic.List<Core.FilePreviewSummary>
                {
                    new Core.FilePreviewSummary
                    {
                        FileId = "sphere.bin",
                        SourceRelativePath = "battle/kernel/sphere.bin",
                        OutputRelativePath = "battle/kernel/sphere.bin",
                        BeforeHash = "N/A",
                        PredictedAfterHash = "N/A",
                        ByteDiffLines = new System.Collections.Generic.List<string> { "Offset 0x0000..0x1000 [Modificado]" },
                        DisassemblyDiffLines = new System.Collections.Generic.List<string> { dataModel.EditSession?.BehaviorSummary ?? Strings.U_Sge_BehaviorFallback },
                        SemanticDiffLines = new System.Collections.Generic.List<string> { dataModel.EditSession?.SessionSummary ?? Strings.U_Sge_SessionFallback },
                        HumanSummary = "Hardcoded preview adapted to V2"
                    }
                }
            };
            mainWindow.ShowChangeSetPreview(preview);
        }
    }

    private void Button_Undo(object? sender, RoutedEventArgs e)
    {
        dataModel.Undo();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Discard(object? sender, RoutedEventArgs e)
    {
        dataModel.Discard();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Save(object? sender, RoutedEventArgs e)
    {
        dataModel.Save();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PreviewZoomIn(object? sender, RoutedEventArgs e)
    {
        dataModel.ZoomInPreview();
        AudioStudio_Service.Instance.PlaySoftNavigation();
    }

    private void Button_PreviewZoomOut(object? sender, RoutedEventArgs e)
    {
        dataModel.ZoomOutPreview();
        AudioStudio_Service.Instance.PlaySoftNavigation();
    }

    private void Button_PreviewReset(object? sender, RoutedEventArgs e)
    {
        dataModel.ResetPreviewZoom();
        previewSurface?.ResetViewPan();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Preview_NodeInvoked(object? sender, SphereGridNodeInvokedEventArgs e)
    {
        previewSurface?.ResetViewPan();
        dataModel.SelectPreviewNode(e.NodeIndex);
        AudioStudio_Service.Instance.PlaySoftNavigation();
    }

    private void PreviewSurface_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y > 0)
            dataModel.ZoomInPreview();
        else if (e.Delta.Y < 0)
            dataModel.ZoomOutPreview();
        else
            return;

        AudioStudio_Service.Instance.PlaySoftNavigation();
        e.Handled = true;
    }

    private void PreviewSurface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (previewSurface == null)
            return;

        if (e.Handled)
            return;

        PointerPointProperties properties = e.GetCurrentPoint(previewSurface).Properties;
        previewPressStartedOnNode = previewSurface.TryHitNode(e.GetPosition(previewSurface), out _);

        bool shouldPan = properties.IsRightButtonPressed ||
                         properties.IsMiddleButtonPressed ||
                         (properties.IsLeftButtonPressed && !previewPressStartedOnNode);

        if (!shouldPan)
            return;

        isPreviewPanning = true;
        previewPanAnchor = e.GetPosition(previewSurface);
        previewPanStart = previewSurface.GetViewPan();
        e.Pointer.Capture(previewSurface);
        e.Handled = true;
    }

    private void PreviewSurface_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!isPreviewPanning || previewSurface == null)
            return;

        Point point = e.GetPosition(previewSurface);
        Vector delta = point - previewPanAnchor;
        previewSurface.SetViewPan(new Vector(previewPanStart.X + delta.X, previewPanStart.Y + delta.Y));
        e.Handled = true;
    }

    private void PreviewSurface_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!isPreviewPanning || previewSurface == null)
            return;

        isPreviewPanning = false;
        previewPressStartedOnNode = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void PreviewSurface_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        isPreviewPanning = false;
        previewPressStartedOnNode = false;
    }
}
