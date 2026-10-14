using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System;
using System.Globalization;

namespace FFXProjectEditor.Modules.MonsterAiEditor;

internal partial class MonsterAiEditor_DataModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdvancedSeymourHandoff))]
    private AiAdvancedSeymourHandoffRow? advancedSeymourHandoff;
    [ObservableProperty] private string advancedSeymourHandoffStatus = string.Empty;
    public bool HasAdvancedSeymourHandoff => AdvancedSeymourHandoff != null;

    void RefreshAdvancedSeymourHandoff()
    {
        AdvancedSeymourHandoff = null;
        AdvancedSeymourHandoffStatus = string.Empty;
        if (selectedScript != null && TryGetSelectedMonsterNumber(out int number) && number == 124
            && AiSeymourHandoffThresholdWriter.TryDetect(selectedScript, out var descriptor))
            AdvancedSeymourHandoff = new AiAdvancedSeymourHandoffRow(descriptor!);
    }

    public bool ApplyAdvancedSeymourHandoff(AiAdvancedSeymourHandoffRow? row)
    {
        if (selectedScript == null || selectedPath == null || row == null
            || !ReferenceEquals(row, AdvancedSeymourHandoff)
            || !TryGetSelectedMonsterNumber(out int number) || number != 124)
        {
            AdvancedSeymourHandoffStatus = Strings.AiAdvancedConditionChanged;
            return false;
        }
        if (HasPendingAuthoringEdits)
        {
            AdvancedSeymourHandoffStatus = Strings.AiAdvancedPendingEdits;
            return false;
        }
        if (!int.TryParse(row.First.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int first)
            || !int.TryParse(row.Second.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int second)
            || !int.TryParse(row.Third.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int third)
            || !(6 > first && first > second && second > third && third >= 1))
        {
            AdvancedSeymourHandoffStatus = Strings.AiAdvancedSeymourRange;
            return false;
        }
        if (!AiSeymourHandoffThresholdWriter.TryApply(selectedScript, row.Source, first, second, third,
                out byte[]? output, out string error) || output == null)
        {
            AdvancedSeymourHandoffStatus = error;
            return false;
        }
        try
        {
            AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, output);
        }
        catch (Exception ex)
        {
            AdvancedSeymourHandoffStatus = ex.Message;
            return false;
        }
        bool reloaded = ReloadSelectedFromDisk();
        RefreshPhaseRotationAudit();
        RebuildAdvancedPhaseUnits();
        AdvancedSeymourHandoffStatus = reloaded ? Strings.AiAdvancedNativeConditionSaved : Strings.AiAdvancedNativeConditionReload;
        return reloaded;
    }
}

internal sealed partial class AiAdvancedSeymourHandoffRow : ObservableObject
{
    public AiSeymourHandoffThresholdDescriptor Source { get; }
    [ObservableProperty] private string first;
    [ObservableProperty] private string second;
    [ObservableProperty] private string third;
    public string Location => $"w{Source.WorkerIndex} · 0x{Source.FirstOffset:X4} / 0x{Source.SecondOffset:X4} / 0x{Source.ThirdOffset:X4}";
    public AiAdvancedSeymourHandoffRow(AiSeymourHandoffThresholdDescriptor source)
    {
        Source = source;
        first = source.First.ToString(CultureInfo.InvariantCulture);
        second = source.Second.ToString(CultureInfo.InvariantCulture);
        third = source.Third.ToString(CultureInfo.InvariantCulture);
    }
}
