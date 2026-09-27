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
    public ObservableCollection<AiAdvancedNativeConditionRow> AdvancedNativeConditions { get; } = new();
    public string[] AdvancedNativeOperators { get; } = Enum.GetValues<AiVarCompareOperator>()
        .Select(AiVarConditionBuilder.OperatorLabel).ToArray();
    [ObservableProperty] private AiAdvancedNativeConditionRow? selectedAdvancedNativeCondition;
    [ObservableProperty] private string advancedNativeConditionValue = string.Empty;
    [ObservableProperty] private string advancedNativeConditionOperator = "==";
    [ObservableProperty] private string advancedNativeConditionStatus = string.Empty;
    public bool HasAdvancedNativeCondition => SelectedAdvancedNativeCondition != null;
    public bool HasAdvancedNativeConditions => AdvancedNativeConditions.Count != 0;
    public string AdvancedNativeConditionCoverage => string.Format(Strings.AiAdvancedNativeConditionCount, AdvancedNativeConditions.Count);

    partial void OnSelectedAdvancedNativeConditionChanged(AiAdvancedNativeConditionRow? value)
    {
        AdvancedNativeConditionValue = value?.Source.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        AdvancedNativeConditionOperator = value == null ? "==" : AiVarConditionBuilder.OperatorLabel(value.Source.Operator);
        AdvancedNativeConditionStatus = string.Empty;
        OnPropertyChanged(nameof(HasAdvancedNativeCondition));
    }

    void RefreshAdvancedNativeConditions()
    {
        int? offset = SelectedAdvancedNativeCondition?.Source.Offset;
        AdvancedNativeConditions.Clear();
        if (selectedScript != null)
            foreach (var descriptor in AiNativeConditionWriter.Detect(selectedScript))
                AdvancedNativeConditions.Add(new AiAdvancedNativeConditionRow(descriptor));
        SelectedAdvancedNativeCondition = AdvancedNativeConditions.FirstOrDefault(row => row.Source.Offset == offset)
            ?? AdvancedNativeConditions.FirstOrDefault();
        OnPropertyChanged(nameof(HasAdvancedNativeConditions));
        OnPropertyChanged(nameof(AdvancedNativeConditionCoverage));
        OnPropertyChanged(nameof(CanEditSelectedAdvancedNativeCondition));
    }

    public bool ApplyAdvancedNativeCondition()
    {
        if (selectedScript != null && HasPendingAuthoringEdits)
        {
            AdvancedNativeConditionStatus = Strings.AiAdvancedPendingEdits;
            return false;
        }
        if (selectedScript == null || selectedPath == null || SelectedAdvancedNativeCondition == null)
            return false;
        if (!short.TryParse(AdvancedNativeConditionValue.Trim(), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out short value)
            || !AiVarConditionBuilder.TryParseOperator(AdvancedNativeConditionOperator, out var comparison))
        {
            AdvancedNativeConditionStatus = Strings.AiAdvancedNativeConditionInvalid;
            return false;
        }
        if (!AiNativeConditionWriter.TryApply(selectedScript, SelectedAdvancedNativeCondition.Source, comparison, value,
                out byte[]? output, out string error) || output == null
            || !AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(output, selectedScript,
                selectedScript.OriginalAiFileBytes.Length, out _, out error))
        {
            AdvancedNativeConditionStatus = error;
            return false;
        }
        try
        {
            AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, output);
        }
        catch (Exception ex)
        {
            AdvancedNativeConditionStatus = ex.Message;
            return false;
        }
        bool reloaded = ReloadSelectedFromDisk();
        RefreshPhaseRotationAudit();
        AdvancedNativeConditionStatus = reloaded ? Strings.AiAdvancedNativeConditionSaved : Strings.AiAdvancedNativeConditionReload;
        return reloaded;
    }
}

internal sealed record AiAdvancedNativeConditionRow(AiNativeConditionDescriptor Source)
{
    public string Summary => $"{Source.VariableName} {AiVarConditionBuilder.OperatorLabel(Source.Operator)} {Source.Value}";
    public string Location => string.Format(Strings.AiAdvancedNativeConditionLocation, Source.Offset, Source.WorkerIndex, Source.BranchTargetOffset, Source.BranchWhenTrue ? Strings.AiAdvancedBranchTrue : Strings.AiAdvancedBranchFalse);
}
