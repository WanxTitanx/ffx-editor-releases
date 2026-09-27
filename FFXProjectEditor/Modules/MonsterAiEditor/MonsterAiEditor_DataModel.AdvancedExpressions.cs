using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor;

internal partial class MonsterAiEditor_DataModel
{
    public ObservableCollection<AiAdvancedExpressionRow> AdvancedExpressions { get; } = new();
    public ObservableCollection<AiAdvancedExpressionLiteralRow> AdvancedExpressionLiterals { get; } = new();
    public ObservableCollection<AiAdvancedExpressionOperatorRow> AdvancedExpressionOperators { get; } = new();
    public ObservableCollection<string> AdvancedExpressionOperatorChoices { get; } = new();
    [ObservableProperty] private AiAdvancedExpressionRow? selectedAdvancedExpression;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyAdvancedExpression))]
    [NotifyPropertyChangedFor(nameof(HasAdvancedExpressionLiteral))]
    private AiAdvancedExpressionLiteralRow? selectedAdvancedExpressionLiteral;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyAdvancedExpression))]
    [NotifyPropertyChangedFor(nameof(HasAdvancedExpressionOperator))]
    private AiAdvancedExpressionOperatorRow? selectedAdvancedExpressionOperator;
    [ObservableProperty] private string advancedExpressionOperatorValue = string.Empty;
    [ObservableProperty] private string advancedExpressionValue = string.Empty;
    [ObservableProperty] private string advancedExpressionStatus = string.Empty;
    public bool HasAdvancedExpressions => AdvancedExpressions.Count > 0;
    public bool HasAdvancedExpressionLiteral => SelectedAdvancedExpressionLiteral != null;
    public bool HasAdvancedExpressionOperator => SelectedAdvancedExpressionOperator != null;
    public bool CanApplyAdvancedExpression => HasAdvancedExpressionLiteral || HasAdvancedExpressionOperator;

    partial void OnSelectedAdvancedExpressionChanged(AiAdvancedExpressionRow? value)
    {
        AdvancedExpressionLiterals.Clear();
        AdvancedExpressionOperators.Clear();
        if (value != null)
        {
            foreach (var literal in value.Source.Literals)
                AdvancedExpressionLiterals.Add(new AiAdvancedExpressionLiteralRow(literal));
            foreach (var operation in value.Source.Operators)
                AdvancedExpressionOperators.Add(new AiAdvancedExpressionOperatorRow(operation));
        }
        SelectedAdvancedExpressionLiteral = AdvancedExpressionLiterals.FirstOrDefault();
        SelectedAdvancedExpressionOperator = AdvancedExpressionOperators.FirstOrDefault();
        AdvancedExpressionStatus = string.Empty;
    }

    partial void OnSelectedAdvancedExpressionLiteralChanged(AiAdvancedExpressionLiteralRow? value) =>
        AdvancedExpressionValue = value?.Source.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    partial void OnSelectedAdvancedExpressionOperatorChanged(AiAdvancedExpressionOperatorRow? value)
    {
        AdvancedExpressionOperatorChoices.Clear();
        if (value != null)
            foreach (byte opcode in AiIntegerExpressionWriter.AllowedOperators(value.Source.Opcode))
                AdvancedExpressionOperatorChoices.Add(AiIntegerExpressionWriter.OperatorLabel(opcode));
        AdvancedExpressionOperatorValue = value == null ? string.Empty : AiIntegerExpressionWriter.OperatorLabel(value.Source.Opcode);
    }

    void RefreshAdvancedExpressions()
    {
        int? sink = SelectedAdvancedExpression?.Source.SinkOffset;
        SelectedAdvancedExpression = null;
        AdvancedExpressions.Clear();
        if (selectedScript != null)
            foreach (var source in AiIntegerExpressionWriter.Detect(selectedScript))
                AdvancedExpressions.Add(new AiAdvancedExpressionRow(source));
        SelectedAdvancedExpression = AdvancedExpressions.FirstOrDefault(r => r.Source.SinkOffset == sink)
            ?? AdvancedExpressions.FirstOrDefault();
        OnPropertyChanged(nameof(HasAdvancedExpressions));
    }

    public bool ApplyAdvancedExpression()
    {
        if (selectedScript == null || selectedPath == null || SelectedAdvancedExpression == null
            || !CanApplyAdvancedExpression
            || !AdvancedExpressions.Contains(SelectedAdvancedExpression)
            || (SelectedAdvancedExpressionLiteral != null && !AdvancedExpressionLiterals.Contains(SelectedAdvancedExpressionLiteral))
            || (SelectedAdvancedExpressionOperator != null && !AdvancedExpressionOperators.Contains(SelectedAdvancedExpressionOperator))) return false;
        if (HasPendingAuthoringEdits)
        {
            AdvancedExpressionStatus = Strings.AiAdvancedPendingEdits;
            return false;
        }
        int? value = null;
        if (SelectedAdvancedExpressionLiteral != null)
        {
            if (!int.TryParse(AdvancedExpressionValue.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number))
            {
                AdvancedExpressionStatus = Strings.AiAdvancedInvalidNumber;
                return false;
            }
            value = number;
        }
        byte? opcode = SelectedAdvancedExpressionOperator == null ? null
            : AiIntegerExpressionWriter.AllowedOperators(SelectedAdvancedExpressionOperator.Source.Opcode)
                .Where(op => AiIntegerExpressionWriter.OperatorLabel(op) == AdvancedExpressionOperatorValue).Select(op => (byte?)op).SingleOrDefault();
        if (SelectedAdvancedExpressionOperator != null && !opcode.HasValue)
        {
            AdvancedExpressionStatus = Strings.AiAdvancedConditionRange;
            return false;
        }
        var request = new AiIntegerExpressionEdit(SelectedAdvancedExpressionLiteral?.Source.Offset, value,
            SelectedAdvancedExpressionOperator?.Source.Offset, opcode);
        if (!AiIntegerExpressionWriter.TryApply(selectedScript, SelectedAdvancedExpression.Source,
                request, out byte[]? output, out string error) || output == null)
        {
            AdvancedExpressionStatus = error;
            return false;
        }
        try
        {
            AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, output);
        }
        catch (Exception ex)
        {
            AdvancedExpressionStatus = ex.Message;
            return false;
        }
        bool reloaded = ReloadSelectedFromDisk();
        RefreshPhaseRotationAudit();
        RebuildAdvancedPhaseUnits();
        AdvancedExpressionStatus = reloaded ? Strings.AiAdvancedNativeConditionSaved : Strings.AiAdvancedNativeConditionReload;
        return reloaded;
    }
}

internal sealed record AiAdvancedExpressionRow(AiIntegerExpressionDescriptor Source)
{
    public string Summary => Source.Expression;
    public string Location => $"w{Source.WorkerIndex} · 0x{Source.StartOffset:X4} → 0x{Source.SinkOffset:X4} · {Source.Destination}";
}

internal sealed record AiAdvancedExpressionLiteralRow(AiIntegerExpressionLiteral Source)
{
    public string Summary => $"0x{Source.Offset:X4} · {Source.Value} [{Source.Minimum}…{Source.Maximum}]";
}

internal sealed record AiAdvancedExpressionOperatorRow(AiIntegerExpressionOperator Source)
{
    public string Summary => $"0x{Source.Offset:X4} · {AiIntegerExpressionWriter.OperatorLabel(Source.Opcode)}";
}
