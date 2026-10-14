using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace FFXProjectEditor;

public partial class MonsterAiAdvancedFluxThreshold_Window : Window
{
    readonly MonsterAiEditor_DataModel dataModel;
    readonly AiAdvancedFluxThresholdEditorVm editorVm;

    public MonsterAiAdvancedFluxThreshold_Window()
        : this(
            new MonsterAiEditor_DataModel(),
            new AiAdvancedFluxThresholdEvidenceVm(
                "priv0018",
                "store @0x0000",
                "PUSHII 4 -> CALL readChrProperty -> DIV -> POPVAR priv0018",
                "Primeira releitura posterior em 0x0000.",
                "Janela curta do mesmo bloco tambem mostra: Protect.",
                null))
    {
    }

    internal MonsterAiAdvancedFluxThreshold_Window(
        MonsterAiEditor_DataModel dataModel,
        AiAdvancedFluxThresholdEvidenceVm row)
    {
        this.dataModel = dataModel;
        editorVm = new AiAdvancedFluxThresholdEditorVm(dataModel, row);
        DataContext = editorVm;
        InitializeComponent();
        Closed += (_, _) => editorVm.Detach();
    }

    private void Button_Apply(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedFluxThresholdEdit(editorVm.Row))
        {
            AudioStudio_Service.Instance.PlayConfirm();
            Close();
            return;
        }

        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Close(object? sender, RoutedEventArgs e) => Close();
}

internal sealed partial class AiAdvancedFluxThresholdEditorVm : ObservableObject
{
    readonly MonsterAiEditor_DataModel dataModel;

    public AiAdvancedFluxThresholdEditorVm(
        MonsterAiEditor_DataModel dataModel,
        AiAdvancedFluxThresholdEvidenceVm row)
    {
        this.dataModel = dataModel;
        Row = row;
        Row.PropertyChanged += OnRowPropertyChanged;
        dataModel.PropertyChanged += OnDataModelPropertyChanged;
    }

    public AiAdvancedFluxThresholdEvidenceVm Row { get; }
    public string VariableName => Row.VariableName;
    public string RoleLabel => Row.RoleLabel;
    public string SiteSummary => Row.SiteSummary;
    public string CurrentThresholdSummary => Row.CurrentThresholdSummary;
    public string WriterSummary => Row.WriterSummary;
    public string RawFormulaSummary => Row.RawFormulaSummary;
    public string ComparisonSummary => Row.ComparisonSummary;
    public string FollowUpSummary => Row.FollowUpSummary;
    public bool CanEditRawThreshold => Row.CanEditRawThreshold;
    public bool HasNumeratorEditor => Row.HasNumeratorEditor;
    public string AdvancedFluxThresholdApplySummary => dataModel.AdvancedFluxThresholdApplySummary;

    public string DenominatorText
    {
        get => Row.DenominatorText;
        set
        {
            if (Row.DenominatorText == value)
                return;
            Row.DenominatorText = value;
            OnPropertyChanged();
        }
    }

    public string NumeratorText
    {
        get => Row.NumeratorText;
        set
        {
            if (Row.NumeratorText == value)
                return;
            Row.NumeratorText = value;
            OnPropertyChanged();
        }
    }

    public void Detach()
    {
        Row.PropertyChanged -= OnRowPropertyChanged;
        dataModel.PropertyChanged -= OnDataModelPropertyChanged;
    }

    void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AiAdvancedFluxThresholdEvidenceVm.DenominatorText):
                OnPropertyChanged(nameof(DenominatorText));
                break;
            case nameof(AiAdvancedFluxThresholdEvidenceVm.NumeratorText):
                OnPropertyChanged(nameof(NumeratorText));
                break;
        }
    }

    void OnDataModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonsterAiEditor_DataModel.AdvancedFluxThresholdApplySummary))
            OnPropertyChanged(nameof(AdvancedFluxThresholdApplySummary));
    }
}
