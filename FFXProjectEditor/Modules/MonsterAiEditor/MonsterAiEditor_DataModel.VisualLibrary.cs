using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        public ObservableCollection<BehaviorTemplateVm> BehaviorTemplates { get; } = new();
        public ObservableCollection<BehaviorTemplateVm> DisplayedBehaviorTemplates { get; } = new();

        [ObservableProperty] private string behaviorLibrarySearchText = string.Empty;
        [ObservableProperty] private string behaviorLibraryVisualSummary = Strings.U_Ai_VlIntro;

        partial void OnBehaviorLibrarySearchTextChanged(string value) => ApplyBehaviorTemplateFilter();

        void SeedVisualBehaviorLibrary()
        {
            if (BehaviorTemplates.Count > 0)
            {
                return;
            }

            AiCommandOption fallback = AiCommandId.AllOptions().FirstOrDefault()
                ?? new AiCommandOption(AiCommandCategory.Character, 0, 0x3000, "Command 0");
            AiCommandOption firaga = FindCommand("Firaga", "Fira", "Fire") ?? fallback;
            AiCommandOption cure = FindCommand("Curaga", "Cura", "Cure") ?? fallback;
            AiCommandOption haste = FindCommand("Haste") ?? fallback;

            BehaviorTemplates.Add(new BehaviorTemplateVm(
                "counter",
                Strings.U_Ai_VlRiteRetaliation,
                Strings.U_Ai_VlRiteRetaliationDesc,
                "⚔",
                usesSelectedAction: true,
                hasPrimaryCommand: true,
                hasChance: true,
                primaryLabel: Strings.U_Ai_VlSkill,
                defaultCommand: firaga));

            BehaviorTemplates.Add(new BehaviorTemplateVm(
                "hp-cure",
                Strings.U_Ai_VlRiteSurvival,
                Strings.U_Ai_VlRiteSurvivalDesc,
                "💊",
                hasPrimaryCommand: true,
                hasHpThreshold: true,
                primaryLabel: Strings.U_Ai_VlSpell,
                defaultCommand: cure));

            BehaviorTemplates.Add(new BehaviorTemplateVm(
                "alternate",
                Strings.U_Ai_VlRiteUncertainty,
                Strings.U_Ai_VlRiteUncertaintyDesc,
                "🔄",
                usesSelectedAction: true,
                hasPrimaryCommand: true,
                primaryLabel: Strings.U_Ai_VlSkillB,
                defaultCommand: firaga));

            BehaviorTemplates.Add(new BehaviorTemplateVm(
                "enrage",
                Strings.U_Ai_VlRiteFury,
                Strings.U_Ai_VlRiteFuryDesc,
                "🔥",
                hasPrimaryCommand: true,
                hasHpThreshold: true,
                primaryLabel: Strings.U_Ai_VlCommand,
                defaultCommand: haste));

            BehaviorTemplates.Add(new BehaviorTemplateVm(
                "defensive",
                Strings.U_Ai_VlRiteProtection,
                Strings.U_Ai_VlRiteProtectionDesc,
                "🛡"));

            AiCommandOption delayAttack = FindCommand("Delay Attack", "Delay", "Attack") ?? fallback;
            BehaviorTemplates.Add(new BehaviorTemplateVm(
                "counterattack",
                "CounterAttack",
                Strings.U_Ai_VlCounterAttackDesc,
                "⚔",
                hasPrimaryCommand: true,
                hasTarget: true,
                primaryLabel: Strings.U_Ai_VlSkill,
                defaultCommand: delayAttack));

            ApplyBehaviorTemplateFilter();
        }

        void ApplyBehaviorTemplateFilter()
        {
            DisplayedBehaviorTemplates.Clear();
            string q = (BehaviorLibrarySearchText ?? string.Empty).Trim();
            foreach (BehaviorTemplateVm template in BehaviorTemplates)
            {
                if (q.Length == 0 || template.SearchBlob.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    DisplayedBehaviorTemplates.Add(template);
                }
            }
        }

        public void ApplyBehaviorTemplate(BehaviorTemplateVm? template)
        {
            if (template == null)
            {
                BehaviorLibraryVisualSummary = Strings.U_Ai_VlSelectRite;
                return;
            }

            switch (template.Id)
            {
                case "counter":
                    ApplyCounterTemplate(template);
                    return;
                case "hp-cure":
                    ApplyHpGuardTemplate(template, Strings.U_Ai_VlRiteSurvival);
                    return;
                case "alternate":
                    ApplyAlternateTemplate(template);
                    return;
                case "enrage":
                    ApplyHpGuardTemplate(template, Strings.U_Ai_VlRiteFury);
                    return;
                case "defensive":
                    ApplyPresetDefensive();
                    BehaviorLibraryVisualSummary = PresetSummary;
                    return;
                case "counterattack":
                    ApplyCounterAttackTemplate(template);
                    return;
                default:
                    BehaviorLibraryVisualSummary = string.Format(Strings.U_Ai_VlUnknownRite, template.Title);
                    return;
            }
        }

        void ApplyCounterTemplate(BehaviorTemplateVm template)
        {
            SelectedAutomationAbility = template.SelectedCommand;
            if (!PresetReady())
            {
                BehaviorLibraryVisualSummary = PresetSummary;
                return;
            }

            int k = template.SelectedChance?.K ?? 0;
            if (!TryForcePickedAbility(random: k > 0, k: k))
            {
                BehaviorLibraryVisualSummary = PresetSummary;
                return;
            }

            string chance = k <= 0 ? Strings.U_Ai_AlwaysShort : $"1-in-{k}";
            BehaviorLibraryVisualSummary =
                string.Format(Strings.U_Ai_VlRetaliationApplied, template.SelectedCommand?.Name, chance, PresetSummary);
        }

        void ApplyCounterAttackTemplate(BehaviorTemplateVm template)
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                BehaviorLibraryVisualSummary = Strings.U_Ai_VlNeedAiFileCounter;
                return;
            }

            AiSnippet? snippet = AiSnippetLibrary.ById("guard-counterattack-cmd");
            if (snippet == null)
            {
                BehaviorLibraryVisualSummary = Strings.U_Ai_VlSnippetNotFound;
                return;
            }

            if (!TryResolveOnHitHook(out AiEventHook hook, out string hookError))
            {
                BehaviorLibraryVisualSummary = hookError;
                return;
            }

            AiCommandOption? command = template.SelectedCommand;
            if (command == null)
            {
                BehaviorLibraryVisualSummary = Strings.U_Ai_VlPickCounterSkill;
                return;
            }

            ushort target = template.SelectedTarget?.Operand ?? 0xFFEF;
            byte[] newAi;
            try
            {
                var (guard, action) = snippet.ExpandGuarded(new AiSnippetArgs(command.Operand, 0, target));
                newAi = AiScript_File.AppendGuardedAction(selectedScript, hook.WorkerIndex, hook.EntrypointIndex, guard, action);
            }
            catch (Exception ex)
            {
                BehaviorLibraryVisualSummary = string.Format(Strings.U_Ai_VlCounterAborted, ex.Message);
                return;
            }

            AiValidationReport check = AiValidator.ValidateRebuilt(newAi, selectedScript.OriginalAiFileBytes.Length);
            if (!check.IsValid)
            {
                BehaviorLibraryVisualSummary = string.Format(Strings.U_Ai_VlCounterBlocked, check.Errors.FirstOrDefault()?.Message);
                return;
            }

            if (!SaveNewAi(newAi, out string err))
            {
                BehaviorLibraryVisualSummary = string.Format(Strings.U_Ai_VlCounterAborted, err);
                return;
            }

            string targetLabel = template.SelectedTarget?.Label ?? $"0x{target:X4}";
            BehaviorLibraryVisualSummary =
                string.Format(Strings.U_Ai_VlCounterApplied, command.Name, command.Operand, targetLabel, Path.GetFileName(selectedPath));
        }

        void ApplyAlternateTemplate(BehaviorTemplateVm template)
        {
            SelectedAutomationAbility = template.SelectedCommand;
            if (!PresetReady())
            {
                BehaviorLibraryVisualSummary = PresetSummary;
                return;
            }

            if (!TryForcePickedAbility(random: true, k: 2))
            {
                BehaviorLibraryVisualSummary = PresetSummary;
                return;
            }

            BehaviorLibraryVisualSummary =
                string.Format(Strings.U_Ai_VlUncertaintyApplied, template.SelectedCommand?.Name, PresetSummary);
        }

        void ApplyHpGuardTemplate(BehaviorTemplateVm template, string label)
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                BehaviorLibraryVisualSummary = Strings.U_Ai_VlNeedAiFileHp;
                return;
            }

            AiSnippet? snippet = AiSnippetLibrary.ById("guard-hp-below-pct-force-cmd");
            if (snippet == null)
            {
                BehaviorLibraryVisualSummary = Strings.U_Ai_VlHpSnippetNotFound;
                return;
            }

            if (!TryResolveAuthoringHook(out AiEventHook hook, out string hookLabel, out string hookError))
            {
                BehaviorLibraryVisualSummary = hookError;
                return;
            }

            ushort percent = (ushort)(template.SelectedHpThreshold?.Percent ?? 50);
            AiCommandOption? command = template.SelectedCommand;
            if (command == null)
            {
                BehaviorLibraryVisualSummary = Strings.U_Ai_VlPickRiteCommand;
                return;
            }

            byte[] newAi;
            try
            {
                var (guard, action) = snippet.ExpandGuarded(new AiSnippetArgs(command.Operand, percent, 0));
                newAi = AiScript_File.AppendGuardedAction(selectedScript, hook.WorkerIndex, hook.EntrypointIndex, guard, action);
            }
            catch (Exception ex)
            {
                BehaviorLibraryVisualSummary = string.Format(Strings.U_Ai_VlRiteAborted, label, ex.Message);
                return;
            }

            AiValidationReport check = AiValidator.ValidateRebuilt(newAi, selectedScript.OriginalAiFileBytes.Length);
            if (!check.IsValid)
            {
                BehaviorLibraryVisualSummary = string.Format(Strings.U_Ai_VlRiteBlocked, label, check.Errors.FirstOrDefault()?.Message);
                return;
            }

            if (!SaveNewAi(newAi, out string err))
            {
                BehaviorLibraryVisualSummary = string.Format(Strings.U_Ai_VlRiteAborted, label, err);
                return;
            }

            BehaviorLibraryVisualSummary =
                string.Format(Strings.U_Ai_VlRiteApplied, label, percent, command.Name, hookLabel, Path.GetFileName(selectedPath));
        }

        static AiCommandOption? FindCommand(params string[] needles)
        {
            foreach (string needle in needles)
            {
                AiCommandOption? exact = AiCommandId.AllOptions()
                    .FirstOrDefault(o => string.Equals(o.Name, needle, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                {
                    return exact;
                }
            }

            foreach (string needle in needles)
            {
                AiCommandOption? contains = AiCommandId.AllOptions()
                    .FirstOrDefault(o => o.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
                if (contains != null)
                {
                    return contains;
                }
            }

            return null;
        }
    }

    internal sealed partial class BehaviorTemplateVm : ObservableObject
    {
        static readonly IReadOnlyList<BehaviorChanceOption> ChanceChoices =
        [
            new BehaviorChanceOption(Strings.U_Ai_Always, 0),
            new BehaviorChanceOption(Strings.U_Ai_OneInTwo, 2),
            new BehaviorChanceOption(Strings.U_Ai_OneInThree, 3),
        ];

        static readonly IReadOnlyList<BehaviorPercentOption> HpThresholdChoices =
        [
            new BehaviorPercentOption("25%", 25),
            new BehaviorPercentOption("50%", 50),
            new BehaviorPercentOption("75%", 75),
        ];

        static readonly IReadOnlyList<BehaviorTargetOption> TargetChoices =
        [
            new BehaviorTargetOption("LastAttacker (0xFFEF)", 0xFFEF),
            new BehaviorTargetOption("FrontlineChars (0xFFF2)", 0xFFF2),
            new BehaviorTargetOption("Self (0xFFF3)", 0xFFF3),
        ];

        public BehaviorTemplateVm(
            string id,
            string title,
            string description,
            string icon,
            bool usesSelectedAction = false,
            bool hasPrimaryCommand = false,
            bool hasChance = false,
            bool hasHpThreshold = false,
            bool hasTarget = false,
            string primaryLabel = "Skill",
            AiCommandOption? defaultCommand = null,
            BehaviorTargetOption? defaultTarget = null)
        {
            Id = id;
            Title = title;
            Description = description;
            Icon = icon;
            UsesSelectedAction = usesSelectedAction;
            HasPrimaryCommand = hasPrimaryCommand;
            HasChance = hasChance;
            HasHpThreshold = hasHpThreshold;
            HasTarget = hasTarget;
            PrimaryLabel = primaryLabel;
            SelectedCommand = defaultCommand ?? AiCommandId.AllOptions().FirstOrDefault();
            SelectedChance = ChanceOptions.FirstOrDefault();
            SelectedHpThreshold = HpThresholdOptions.FirstOrDefault(option => option.Percent == 50) ?? HpThresholdOptions.FirstOrDefault();
            SelectedTarget = defaultTarget ?? TargetChoices.FirstOrDefault();
        }

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public string Icon { get; }
        public bool UsesSelectedAction { get; }
        public bool HasPrimaryCommand { get; }
        public bool HasChance { get; }
        public bool HasHpThreshold { get; }
        public bool HasTarget { get; }
        public string PrimaryLabel { get; }
        public IReadOnlyList<AiCommandOption> CommandOptions => AiCommandId.AllOptions();
        public IReadOnlyList<BehaviorChanceOption> ChanceOptions => ChanceChoices;
        public IReadOnlyList<BehaviorPercentOption> HpThresholdOptions => HpThresholdChoices;
        public IReadOnlyList<BehaviorTargetOption> TargetOptions => TargetChoices;
        public string SearchBlob => $"{Title} {Description} {Icon}";
        public string Header => $"{Icon} {Title}";
        public string ActionHint => UsesSelectedAction
            ? Strings.U_Ai_VlUsesSelectedAction
            : Strings.U_Ai_VlNoActionNeeded;

        [ObservableProperty] private bool isExpanded;
        [ObservableProperty] private AiCommandOption? selectedCommand;
        [ObservableProperty] private BehaviorChanceOption? selectedChance;
        [ObservableProperty] private BehaviorPercentOption? selectedHpThreshold;
        [ObservableProperty] private BehaviorTargetOption? selectedTarget;
    }

    internal sealed record BehaviorChanceOption(string Label, int K)
    {
        public override string ToString() => Label;
    }

    internal sealed record BehaviorPercentOption(string Label, int Percent)
    {
        public override string ToString() => Label;
    }

    internal sealed record BehaviorTargetOption(string Label, ushort Operand)
    {
        public override string ToString() => Label;
    }
}
