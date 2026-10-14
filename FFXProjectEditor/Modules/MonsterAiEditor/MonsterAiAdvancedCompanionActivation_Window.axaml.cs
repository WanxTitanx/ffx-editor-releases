using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace FFXProjectEditor;

public partial class MonsterAiAdvancedCompanionActivation_Window : Window
{
    readonly MonsterAiEditor_DataModel dataModel;
    readonly AiAdvancedCompanionActivationEditorVm editorVm;

    public MonsterAiAdvancedCompanionActivation_Window()
        : this(
            new MonsterAiEditor_DataModel(),
            BuildDesignTimePackage())
    {
    }

    internal MonsterAiAdvancedCompanionActivation_Window(
        MonsterAiEditor_DataModel dataModel,
        AiAdvancedCompanionActivationPackageVm package)
    {
        this.dataModel = dataModel;
        editorVm = new AiAdvancedCompanionActivationEditorVm(dataModel, package);
        DataContext = editorVm;
        InitializeComponent();
        Closed += (_, _) => editorVm.Detach();
    }

    private void Button_Apply(object? sender, RoutedEventArgs e)
    {
        if (editorVm.SelectedSlot1Monster == null || editorVm.SelectedSlot2Monster == null)
        {
            AudioStudio_Service.Instance.PlayAlternative();
            return;
        }

        if (dataModel.ApplyAdvancedCompanionActivationEdit(
                editorVm.Package,
                editorVm.SelectedSlot1Monster.MonsterId,
                editorVm.SelectedSlot2Monster.MonsterId))
        {
            AudioStudio_Service.Instance.PlayConfirm();
            Close();
            return;
        }

        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Close(object? sender, RoutedEventArgs e) => Close();

    static AiAdvancedCompanionActivationPackageVm BuildDesignTimePackage() =>
        new(new BattleCompanionActivation_File
        {
            BattleId = "maca03_20",
            BattleTokenLabel = "maca03_20 [0x014D0014]",
            FormationSummary = "Formation: slot0=m213, slot1=m012, slot2=m004",
            ActivationCoverageSummary = Strings.U_Ai_CmpCoverage,
            HumanSummary = Strings.U_Ai_CmpHumanSummary,
            Rows =
            [
                new BattleCompanionActivationRow
                {
                    SlotLabel = "Monster#01 [0x0015] / slot1",
                    TargetOperand = 0x0015,
                    FormationSlotIndex = 1,
                    MonsterId = 12,
                    MonsterLabel = "m012 - Design-time companion",
                    HostActionLabel = "0x408A [Summon]",
                    HostActionOffset = 0x0450,
                    HiddenForSelectedBattle = true,
                    HiddenBattleIdsLabel = "maca03_20 [0x014D0014]",
                    EvidenceLabel = "host 0x408A @ 0x450 · companion Hidden @ 0x188",
                },
                new BattleCompanionActivationRow
                {
                    SlotLabel = "Monster#02 [0x0016] / slot2",
                    TargetOperand = 0x0016,
                    FormationSlotIndex = 2,
                    MonsterId = 4,
                    MonsterLabel = "m004 - Design-time companion",
                    HostActionLabel = "0x408A [Summon]",
                    HostActionOffset = 0x0462,
                    HiddenForSelectedBattle = true,
                    HiddenBattleIdsLabel = "maca03_20 [0x014D0014]",
                    EvidenceLabel = "host 0x408A @ 0x462 · companion Hidden @ 0x19C",
                },
            ],
            Notes =
            [
                Strings.U_Ai_CmpDesignTimeStub,
            ],
            ProofLane = "design-time",
            WriterPolicy = Strings.U_Ai_M213WriterPolicy,
            HostUsesBtlSetAppear = true,
            FormationHasPreseededCompanions = true,
            SelectedBattleTokenKnown = true,
            SelectedBattleHasHostSummonPair = true,
            SpawnFromZeroNotSupported = true,
            HostTreatsPeerSlotsAsGameplayCompanions = true,
            PeerGameplaySummary = Strings.U_Ai_CmpHostFollowUp,
        });
}

internal sealed partial class AiAdvancedCompanionActivationEditorVm : ObservableObject
{
    readonly MonsterAiEditor_DataModel dataModel;

    public AiAdvancedCompanionActivationEditorVm(
        MonsterAiEditor_DataModel dataModel,
        AiAdvancedCompanionActivationPackageVm package)
    {
        this.dataModel = dataModel;
        Package = package;
        dataModel.PropertyChanged += OnDataModelPropertyChanged;

        foreach (AiAdvancedCompanionMonsterOption option in Monster_Dictionary.Instance
                     .OrderBy(pair => pair.Key)
                     .Select(pair => new AiAdvancedCompanionMonsterOption(
                         pair.Key,
                         $"m{pair.Key:D3} - {pair.Value}")))
        {
            CompanionOptions.Add(option);
        }

        SelectedSlot1Monster = CompanionOptions.FirstOrDefault(option => option.MonsterId == Slot1CurrentMonsterId);
        SelectedSlot2Monster = CompanionOptions.FirstOrDefault(option => option.MonsterId == Slot2CurrentMonsterId);
    }

    public AiAdvancedCompanionActivationPackageVm Package { get; }
    public ObservableCollection<AiAdvancedCompanionMonsterOption> CompanionOptions { get; } = new();
    public string BattleId => Package.BattleId;
    public string BattleTokenLabel => Package.BattleTokenLabel;
    public string FormationSummary => Package.FormationSummary;
    public string CoverageLabel => Package.CoverageLabel;
    public string HumanSummary => Package.HumanSummary;
    public string NotesSummary => Package.NotesSummary;
    public string Guardrail => Package.Guardrail;
    public string WriterPolicy => Package.WriterPolicy;
    public bool CanEditPackage => Package.HasRecognizedPackage;
    public bool ShowReadOnlyWarning => !CanEditPackage;
    public string Slot1CurrentLabel => BuildCurrentSlotLabel(0);
    public string Slot2CurrentLabel => BuildCurrentSlotLabel(1);
    public int Slot1CurrentMonsterId => Package.Rows.OrderBy(row => row.FormationSlotIndex).ElementAtOrDefault(0)?.MonsterId ?? -1;
    public int Slot2CurrentMonsterId => Package.Rows.OrderBy(row => row.FormationSlotIndex).ElementAtOrDefault(1)?.MonsterId ?? -1;
    public string AdvancedCompanionActivationApplySummary => dataModel.AdvancedCompanionActivationApplySummary;

    [ObservableProperty] private AiAdvancedCompanionMonsterOption? selectedSlot1Monster;
    partial void OnSelectedSlot1MonsterChanged(AiAdvancedCompanionMonsterOption? value)
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(PreviewSummary));
    }

    [ObservableProperty] private AiAdvancedCompanionMonsterOption? selectedSlot2Monster;
    partial void OnSelectedSlot2MonsterChanged(AiAdvancedCompanionMonsterOption? value)
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(PreviewSummary));
    }

    public bool CanApply =>
        CanEditPackage
        && SelectedSlot1Monster != null
        && SelectedSlot2Monster != null
        && (SelectedSlot1Monster.MonsterId != Slot1CurrentMonsterId
            || SelectedSlot2Monster.MonsterId != Slot2CurrentMonsterId);

    public string PreviewSummary
    {
        get
        {
            if (!CanEditPackage)
                return Strings.U_Ai_CmpReadOnly;

            if (SelectedSlot1Monster == null || SelectedSlot2Monster == null)
                return Strings.F2_select_the_two_new_companions_to_assembl_72f911a3;

            if (!CanApply)
                return Strings.U_Ai_CmpNoPending;

            return
                $"slot1: {Slot1CurrentLabel} -> {SelectedSlot1Monster.Display}{System.Environment.NewLine}" +
                $"slot2: {Slot2CurrentLabel} -> {SelectedSlot2Monster.Display}";
        }
    }

    public void Detach() => dataModel.PropertyChanged -= OnDataModelPropertyChanged;

    void OnDataModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonsterAiEditor_DataModel.AdvancedCompanionActivationApplySummary))
            OnPropertyChanged(nameof(AdvancedCompanionActivationApplySummary));
    }

    string BuildCurrentSlotLabel(int rowIndex)
    {
        BattleCompanionActivationRow? row = Package.Rows
            .OrderBy(candidate => candidate.FormationSlotIndex)
            .ElementAtOrDefault(rowIndex);
        if (row == null)
            return Strings.U_Ai_CmpSlotMissing;

        return $"{row.MonsterLabel} · {row.HiddenStatusLabel}";
    }
}

internal sealed class AiAdvancedCompanionMonsterOption
{
    public AiAdvancedCompanionMonsterOption(int monsterId, string display)
    {
        MonsterId = monsterId;
        Display = display;
    }

    public int MonsterId { get; }
    public string Display { get; }
    public override string ToString() => Display;
}
