using Avalonia.Controls;
using Avalonia.Interactivity;
using System.ComponentModel;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.SphereGridBuilder
{
    // v2 canvas authoring: a custom-drawn SphereGridCanvasView (added to CanvasHost in code-behind) + a thin
    // toolbar/side-panel that delegate to the DataModel. The view and DataModel share one SphereGridLayoutBuilder;
    // the DataModel wires the view's GridChanged/NodeSelected events in AttachView.
    public partial class SphereGridCanvas_Control : UserControl, IRestorableModule
    {
        private readonly SphereGridCanvas_DataModel _dataModel = new();
        private readonly SphereGridCanvasView _view = new();

        public SphereGridCanvas_Control()
        {
            InitializeComponent();
            DataContext = _dataModel;
            CanvasHost.Child = _view;
            _dataModel.AttachView(_view);
            // Jarvis-UI (Sprint D 2026-06-20, OPT-D2): destacar o botão de modo ativo na toolbar. O enum
            // ActiveToolMode já vive no DataModel e é setado em cada SetMode*; aqui só refletimos ele na
            // classe visual toolActive do botão correspondente (sem polling — assina PropertyChanged).
            _dataModel.PropertyChanged += OnDataModelPropertyChanged;
            ApplyToolModeClass(_dataModel.ActiveToolMode);
        }

        void OnDataModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SphereGridCanvas_DataModel.ActiveToolMode))
                ApplyToolModeClass(_dataModel.ActiveToolMode);
        }

        void ApplyToolModeClass(ToolMode mode)
        {
            SetToolActive(ToolBtnSelect, mode == ToolMode.Select);
            SetToolActive(ToolBtnAddNode, mode == ToolMode.AddNode);
            SetToolActive(ToolBtnAddLink, mode == ToolMode.AddLink);
        }

        static void SetToolActive(Button button, bool active)
        {
            bool has = button.Classes.Contains("toolActive");
            if (active && !has) button.Classes.Add("toolActive");
            else if (!active && has) button.Classes.Remove("toolActive");
        }

        private void Button_New(object? sender, RoutedEventArgs e) => _dataModel.NewGrid();
        private void Button_OpenOriginal(object? sender, RoutedEventArgs e) => _dataModel.OpenGrid(SphereGridCanvas_DataModel.GridKind.Original);
        private void Button_OpenStandard(object? sender, RoutedEventArgs e) => _dataModel.OpenGrid(SphereGridCanvas_DataModel.GridKind.Standard);
        private void Button_OpenExpert(object? sender, RoutedEventArgs e) => _dataModel.OpenGrid(SphereGridCanvas_DataModel.GridKind.Expert);

        private void Button_ModeSelect(object? sender, RoutedEventArgs e) => _dataModel.SetModeSelect();
        private void Button_ModeAddNode(object? sender, RoutedEventArgs e) => _dataModel.SetModeAddNode();
        private void Button_ModeAddLink(object? sender, RoutedEventArgs e) => _dataModel.SetModeAddLink();
        private void Button_Fit(object? sender, RoutedEventArgs e) => _dataModel.FitView();

        private void Button_Validate(object? sender, RoutedEventArgs e) => _dataModel.Validate();
        private void Button_SaveCopy(object? sender, RoutedEventArgs e) => _dataModel.SaveCopy();
        private void Button_SaveProject(object? sender, RoutedEventArgs e) => _dataModel.SaveToProject();
        private void Button_Restore(object? sender, RoutedEventArgs e) => _dataModel.RestoreFromReference();

        private void Button_ApplyNode(object? sender, RoutedEventArgs e) => _dataModel.ApplyNode();
        private void Button_PopulatePanel(object? sender, RoutedEventArgs e) => _dataModel.PopulatePanelFromCommands();
        private void Button_RemoveNode(object? sender, RoutedEventArgs e) => _dataModel.RemoveSelectedNode();
        private void Button_AddLinkPanel(object? sender, RoutedEventArgs e) => _dataModel.AddLinkFromPanel();
    }
}
