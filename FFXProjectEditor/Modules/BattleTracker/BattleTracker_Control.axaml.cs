using Avalonia;
using Avalonia.Controls;
using FFXProjectEditor.Modules.BattleTracker;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class BattleTracker_Control : UserControl, IRestorableModule
{
    BattleTracker_DataModel DataModel;
    public BattleTracker_Control()
    {
        InitializeComponent();
        DataModel = new BattleTracker_DataModel(FrameAlly, FrameEnemy);
        this.DataContext = DataModel;
    }

    private void Button_Read(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.ReadInfo();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DataModel.StartReading(); // Start the timer when UserControl is loaded
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DataModel.StopReading(); // Stop the timer when UserControl is unloaded
    }

    private void Button_LoadIngame(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DataModel.LoadIngame();
    }
}