using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.CustomBossCreator
{
    internal partial class CustomBossCreator_DataModel : ObservableObject
    {
        const string AiModeInherit = "inherit";
        const string AiModeCopy = "copy";
        const string LootModeInherit = "inherit";
        const string LootModeCopy = "copy";

        // ---- Source monster picker ----
        readonly List<MonsterPickEntry> allSourceMonsters = new();
        bool suppressBossDiffRefresh;
        public ObservableCollection<MonsterPickEntry> SourceMonsters { get; } = new();
        [ObservableProperty][NotifyPropertyChangedFor(nameof(CanCreate))] private MonsterPickEntry? selectedSource;
        [ObservableProperty] private string sourceFilter = string.Empty;

        // ---- AI source ----
        readonly List<MonsterPickEntry> allAiSourceMonsters = new();
        public ObservableCollection<AiSourceModeOption> AiSourceModes { get; } = new();
        public ObservableCollection<MonsterPickEntry> AiSourceMonsters { get; } = new();
        [ObservableProperty][NotifyPropertyChangedFor(nameof(IsCopyAiMode))] private AiSourceModeOption? selectedAiSourceMode;
        [ObservableProperty] private MonsterPickEntry? selectedAiSource;
        [ObservableProperty] private string aiSourceFilter = string.Empty;
        [ObservableProperty] private string aiSourceSummary = Strings.U_CBC_AiInherit;

        // ---- Loot source ----
        readonly List<MonsterPickEntry> allLootSourceMonsters = new();
        public ObservableCollection<LootSourceModeOption> LootSourceModes { get; } = new();
        public ObservableCollection<MonsterPickEntry> LootSourceMonsters { get; } = new();
        [ObservableProperty][NotifyPropertyChangedFor(nameof(IsCopyLootMode))] private LootSourceModeOption? selectedLootSourceMode;
        [ObservableProperty] private MonsterPickEntry? selectedLootSource;
        [ObservableProperty] private string lootSourceFilter = string.Empty;
        [ObservableProperty] private string lootSourceSummary = Strings.U_CBC_LootInherit;

        // ---- Target ID ----
        [ObservableProperty][NotifyPropertyChangedFor(nameof(CanCreate))][NotifyPropertyChangedFor(nameof(OutputPathPreview))]
        private int targetId = 347;
        [ObservableProperty][NotifyPropertyChangedFor(nameof(CanCreate))]
        private string targetIdError = string.Empty;

        // ---- Display name (shown in editor UI + Monster_Dictionary) ----
        [ObservableProperty][NotifyPropertyChangedFor(nameof(CanCreate))]
        private string displayName = "Custom Boss";

        // ---- Stats (nullable = inherit from source) ----
        [ObservableProperty] private string hpText = string.Empty;
        [ObservableProperty] private string mpText = string.Empty;
        [ObservableProperty] private string hpOverkillText = string.Empty;
        [ObservableProperty] private string strengthText = string.Empty;
        [ObservableProperty] private string defenseText = string.Empty;
        [ObservableProperty] private string magicText = string.Empty;
        [ObservableProperty] private string magicDefenseText = string.Empty;
        [ObservableProperty] private string agilityText = string.Empty;
        [ObservableProperty] private string luckText = string.Empty;
        [ObservableProperty] private string evasionText = string.Empty;
        [ObservableProperty] private string accuracyText = string.Empty;
        [ObservableProperty] private string modelIdText = string.Empty;

        // ---- Simple rewards (nullable = inherit from source) ----
        [ObservableProperty] private string rewardGilText = string.Empty;
        [ObservableProperty] private string rewardApText = string.Empty;
        [ObservableProperty] private string rewardApOverkillText = string.Empty;

        // ---- Advanced rewards light (nullable = inherit from selected loot source) ----
        [ObservableProperty] private string drop1ChanceText = string.Empty;
        [ObservableProperty] private string drop1IdText = string.Empty;
        [ObservableProperty] private string drop1RareIdText = string.Empty;
        [ObservableProperty] private string drop1CountText = string.Empty;
        [ObservableProperty] private string drop1RareCountText = string.Empty;
        [ObservableProperty] private string stealChanceText = string.Empty;
        [ObservableProperty] private string stealIdText = string.Empty;
        [ObservableProperty] private string stealRareIdText = string.Empty;
        [ObservableProperty] private string stealCountText = string.Empty;
        [ObservableProperty] private string stealRareCountText = string.Empty;
        [ObservableProperty] private string bribeIdText = string.Empty;
        [ObservableProperty] private string bribeCountText = string.Empty;

        // ---- Status ----
        [ObservableProperty] private string bossDiffPreview = Strings.F2_preview_select_a_base_monster_to_see_wha_66541d3d;
        [ObservableProperty] private string statusMessage = Strings.U_CBC_SelectBaseMonster;
        [ObservableProperty] private bool createSuccess;

        // ---- Output preview ----
        public string OutputPathPreview
        {
            get
            {
                if (!Project_Service.Instance.IsProjectLoaded) return Strings.U_CBC_ProjectNotLoaded;
                try { return Project_Service.Instance.GetPathMon(TargetId); }
                catch { return Strings.U_CBC_InvalidId; }
            }
        }

        public bool CanCreate =>
            SelectedSource != null &&
            !string.IsNullOrWhiteSpace(DisplayName) &&
            string.IsNullOrEmpty(TargetIdError) &&
            Project_Service.Instance.IsProjectLoaded;

        public bool IsCopyAiMode => SelectedAiSourceMode?.Key == AiModeCopy;
        public bool IsCopyLootMode => SelectedLootSourceMode?.Key == LootModeCopy;

        public CustomBossCreator_DataModel()
        {
            AiSourceModes.Add(new AiSourceModeOption(AiModeInherit, Strings.U_CBC_InheritAiBase));
            AiSourceModes.Add(new AiSourceModeOption(AiModeCopy, Strings.F2_copy_ai_from_another_m_4bb10dd3));
            SelectedAiSourceMode = AiSourceModes[0];
            LootSourceModes.Add(new LootSourceModeOption(LootModeInherit, Strings.U_CBC_InheritLootBase));
            LootSourceModes.Add(new LootSourceModeOption(LootModeCopy, Strings.F2_copy_loot_from_another_m_8159ca42));
            SelectedLootSourceMode = LootSourceModes[0];
            LoadSourceMonsters();
        }

        // ------------------------------------------------------------------
        // Source list
        // ------------------------------------------------------------------

        void LoadSourceMonsters()
        {
            SourceMonsters.Clear();
            AiSourceMonsters.Clear();
            LootSourceMonsters.Clear();
            allSourceMonsters.Clear();
            allAiSourceMonsters.Clear();
            allLootSourceMonsters.Clear();

            if (!Project_Service.Instance.IsProjectLoaded) return;

            string monRoot = Project_Service.Instance.Path_Mon;
            if (!Directory.Exists(monRoot)) return;

            Regex rx = new(@"^_m(\d+)$");
            foreach (int id in Directory.GetDirectories(monRoot)
                .Select(Path.GetFileName)
                .Where(n => n != null && rx.IsMatch(n))
                .Select(n => int.Parse(rx.Match(n!).Groups[1].Value))
                .OrderBy(i => i))
            {
                string name = Monster_Dictionary.Instance.TryGetValue((short)id, out string? n) ? n : "<Custom>";
                MonsterPickEntry entry = new(id, name);
                allSourceMonsters.Add(entry);
                allAiSourceMonsters.Add(entry);
                allLootSourceMonsters.Add(entry);
            }

            RefillFiltered(SourceMonsters, allSourceMonsters, SourceFilter);
            RefillFiltered(AiSourceMonsters, allAiSourceMonsters, AiSourceFilter);
            RefillFiltered(LootSourceMonsters, allLootSourceMonsters, LootSourceFilter);

            // Auto-select first
            if (SourceMonsters.Count > 0)
            {
                SelectedSource = SourceMonsters[0];
                SelectedAiSource = SourceMonsters[0];
                SelectedLootSource = SourceMonsters[0];
            }

            SuggestFreeTargetId();
            RefreshAiSourceSummary();
            RefreshLootSourceSummary();
            RefreshBossDiffPreview();
        }

        partial void OnSourceFilterChanged(string value) => ApplySourceFilter();

        void ApplySourceFilter()
        {
            RefillFiltered(SourceMonsters, allSourceMonsters, SourceFilter);
        }

        partial void OnSelectedSourceChanged(MonsterPickEntry? value)
        {
            if (value == null) return;
            LoadStatsFromSource(value.Id);
            if (!IsCopyAiMode)
            {
                SelectedAiSource = value;
            }
            if (!IsCopyLootMode)
            {
                SelectedLootSource = value;
            }
            RefreshAiSourceSummary();
            RefreshLootSourceSummary();
            RefreshBossDiffPreview();
        }

        partial void OnSelectedAiSourceModeChanged(AiSourceModeOption? value)
        {
            OnPropertyChanged(nameof(IsCopyAiMode));
            if (!IsCopyAiMode && SelectedSource != null)
            {
                SelectedAiSource = SelectedSource;
            }
            RefreshAiSourceSummary();
            RefreshBossDiffPreview();
        }

        partial void OnSelectedAiSourceChanged(MonsterPickEntry? value)
        {
            RefreshAiSourceSummary();
            RefreshBossDiffPreview();
        }

        partial void OnAiSourceFilterChanged(string value)
        {
            RefillFiltered(AiSourceMonsters, allAiSourceMonsters, AiSourceFilter);
        }

        partial void OnSelectedLootSourceModeChanged(LootSourceModeOption? value)
        {
            OnPropertyChanged(nameof(IsCopyLootMode));
            if (!IsCopyLootMode && SelectedSource != null)
            {
                SelectedLootSource = SelectedSource;
                LoadRewardsFromLootSource(SelectedSource);
            }
            else if (IsCopyLootMode && SelectedLootSource != null)
            {
                LoadRewardsFromLootSource(SelectedLootSource);
            }
            RefreshLootSourceSummary();
            RefreshBossDiffPreview();
        }

        partial void OnSelectedLootSourceChanged(MonsterPickEntry? value)
        {
            if (value != null && IsCopyLootMode)
            {
                LoadRewardsFromLootSource(value);
            }
            RefreshLootSourceSummary();
            RefreshBossDiffPreview();
        }

        partial void OnLootSourceFilterChanged(string value)
        {
            RefillFiltered(LootSourceMonsters, allLootSourceMonsters, LootSourceFilter);
        }

        void LoadStatsFromSource(int sourceId)
        {
            if (!Project_Service.Instance.IsProjectLoaded) return;

            string path = Project_Service.Instance.GetPathMon(sourceId);
            if (!File.Exists(path)) return;

            suppressBossDiffRefresh = true;
            try
            {
                Monster_File mf = Monster_File.Read(File.ReadAllBytes(path));
                Monster_StatSheet ss = mf.StatSheetFile;
                HpText = ss.Hp.ToString();
                MpText = ss.Mp.ToString();
                HpOverkillText = ss.HpOverkill.ToString();
                StrengthText = ss.Strength.ToString();
                DefenseText = ss.Defense.ToString();
                MagicText = ss.Magic.ToString();
                MagicDefenseText = ss.MagicDefense.ToString();
                AgilityText = ss.Agility.ToString();
                LuckText = ss.Luck.ToString();
                EvasionText = ss.Evasion.ToString();
                AccuracyText = ss.Accuracy.ToString();
                ModelIdText = ss.ModelId.ToString();
                if (mf.LootFile != null)
                {
                    RewardGilText = mf.LootFile.Gil.ToString();
                    RewardApText = mf.LootFile.Ap.ToString();
                    RewardApOverkillText = mf.LootFile.ApOverkill.ToString();
                }
                else
                {
                    RewardGilText = string.Empty;
                    RewardApText = string.Empty;
                    RewardApOverkillText = string.Empty;
                }
                StatusMessage = string.Format(Strings.U_CBC_HeaderBaseEdit, sourceId, SelectedSource?.Name ?? "?");
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(Strings.U_CBC_ReadBaseError, sourceId, ex.Message);
            }
            finally
            {
                suppressBossDiffRefresh = false;
                RefreshBossDiffPreview();
            }
        }

        void LoadRewardsFromLootSource(MonsterPickEntry source)
        {
            if (!Project_Service.Instance.IsProjectLoaded) return;

            string path = Project_Service.Instance.GetPathMon(source.Id);
            if (!File.Exists(path)) return;

            bool oldSuppress = suppressBossDiffRefresh;
            suppressBossDiffRefresh = true;
            try
            {
                Monster_File mf = Monster_File.Read(File.ReadAllBytes(path));
                if (mf.LootFile == null)
                {
                    RewardGilText = string.Empty;
                    RewardApText = string.Empty;
                    RewardApOverkillText = string.Empty;
                    return;
                }

                RewardGilText = mf.LootFile.Gil.ToString();
                RewardApText = mf.LootFile.Ap.ToString();
                RewardApOverkillText = mf.LootFile.ApOverkill.ToString();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erro ao ler loot de m{source.Id:D3}: {ex.Message}";
            }
            finally
            {
                suppressBossDiffRefresh = oldSuppress;
                RefreshBossDiffPreview();
            }
        }

        partial void OnDisplayNameChanged(string value) => RefreshBossDiffPreview();
        partial void OnHpTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnMpTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnHpOverkillTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnStrengthTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnDefenseTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnMagicTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnMagicDefenseTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnAgilityTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnLuckTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnEvasionTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnAccuracyTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnModelIdTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnRewardGilTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnRewardApTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnRewardApOverkillTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnDrop1ChanceTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnDrop1IdTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnDrop1RareIdTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnDrop1CountTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnDrop1RareCountTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnStealChanceTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnStealIdTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnStealRareIdTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnStealCountTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnStealRareCountTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnBribeIdTextChanged(string value) => RefreshBossDiffPreview();
        partial void OnBribeCountTextChanged(string value) => RefreshBossDiffPreview();

        void RefreshBossDiffPreview()
        {
            if (suppressBossDiffRefresh) return;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                BossDiffPreview = Strings.F2_preview_load_a_project_to_compare_the_bo_37b62cf7;
                return;
            }

            if (SelectedSource == null)
            {
                BossDiffPreview = Strings.U_CBC_PreviewNoMonster;
                return;
            }

            try
            {
                string srcPath = Project_Service.Instance.GetPathMon(SelectedSource.Id);
                if (!File.Exists(srcPath))
                {
                    BossDiffPreview = string.Format(Strings.U_CBC_PreviewBaseMissing, srcPath);
                    return;
                }

                Monster_File sourceMonster = Monster_File.Read(File.ReadAllBytes(srcPath));
                Monster_StatSheet baseStats = sourceMonster.StatSheetFile;
                Monster_Loot? rewardBaseLoot = sourceMonster.LootFile;
                CustomBossConfig cfg = BuildCurrentConfig();

                string display = string.IsNullOrWhiteSpace(DisplayName) ? $"Custom_{TargetId}" : DisplayName.Trim();
                List<string> changes = new();
                List<string> invalid = new();
                string lootLine = string.Format(Strings.U_CBC_FinalLootInherit, SelectedSource.Id, SelectedSource.Name);
                if (IsCopyLootMode)
                {
                    if (SelectedLootSource == null)
                    {
                        rewardBaseLoot = null;
                        lootLine = Strings.F2_final_loot_copy_mode_selected_but_no_sou_0058b5d2;
                    }
                    else
                    {
                        lootLine = string.Format(Strings.U_CBC_FinalLootCopyBlock, SelectedLootSource.Id, SelectedLootSource.Name);
                        string lootPath = Project_Service.Instance.GetPathMon(SelectedLootSource.Id);
                        if (File.Exists(lootPath))
                        {
                            Monster_File lootMonster = Monster_File.Read(File.ReadAllBytes(lootPath));
                            rewardBaseLoot = lootMonster.LootFile;
                            if (rewardBaseLoot == null)
                            {
                                lootLine = string.Format(Strings.U_CBC_LootNoLootFile, SelectedLootSource.Id, SelectedLootSource.Name);
                            }
                        }
                        else
                        {
                            rewardBaseLoot = null;
                            lootLine = string.Format(Strings.U_CBC_LootFileMissing, SelectedLootSource.Id, SelectedLootSource.Name);
                        }

                        if (SelectedLootSource.Id != SelectedSource.Id)
                        {
                            changes.Add(string.Format(Strings.U_CBC_LootFileCopy, SelectedLootSource.Id, SelectedLootSource.Name));
                        }
                    }
                }

                AddDiff(changes, "HP", baseStats.Hp, cfg.Hp);
                AddDiff(changes, "MP", baseStats.Mp, cfg.Mp);
                AddDiff(changes, "HP Overkill", baseStats.HpOverkill, cfg.HpOverkill);
                AddDiff(changes, Strings.U_CBC_StatStrength, baseStats.Strength, cfg.Strength);
                AddDiff(changes, Strings.U_CBC_StatDefense, baseStats.Defense, cfg.Defense);
                AddDiff(changes, Strings.U_CBC_StatMagic, baseStats.Magic, cfg.Magic);
                AddDiff(changes, Strings.U_CBC_StatMagicDefense, baseStats.MagicDefense, cfg.MagicDefense);
                AddDiff(changes, Strings.U_CBC_StatAgility, baseStats.Agility, cfg.Agility);
                AddDiff(changes, Strings.U_CBC_StatLuck, baseStats.Luck, cfg.Luck);
                AddDiff(changes, Strings.U_CBC_StatEvasion, baseStats.Evasion, cfg.Evasion);
                AddDiff(changes, Strings.U_CBC_StatAccuracy, baseStats.Accuracy, cfg.Accuracy);
                AddDiff(changes, "Model ID", baseStats.ModelId, cfg.ModelId);
                if (rewardBaseLoot != null)
                {
                    AddDiff(changes, "Gil", rewardBaseLoot.Gil, cfg.RewardGil);
                    AddDiff(changes, "AP", rewardBaseLoot.Ap, cfg.RewardAp);
                    AddDiff(changes, "AP Overkill", rewardBaseLoot.ApOverkill, cfg.RewardApOverkill);
                    AddDiff(changes, "Drop chance raw", rewardBaseLoot.Drop1Chance, cfg.Drop1Chance);
                    AddLootItemDiff(changes, Strings.U_CBC_DropCommon, rewardBaseLoot.Drop1Id, rewardBaseLoot.Drop1Count, cfg.Drop1Id, cfg.Drop1Count);
                    AddLootItemDiff(changes, Strings.U_CBC_DropRare, rewardBaseLoot.Drop1RareId, rewardBaseLoot.Drop1RareCount, cfg.Drop1RareId, cfg.Drop1RareCount);
                    AddDiff(changes, "Steal chance raw", rewardBaseLoot.StealChance, cfg.StealChance);
                    AddLootItemDiff(changes, Strings.U_CBC_StealCommon, rewardBaseLoot.StealId, rewardBaseLoot.StealCount, cfg.StealId, cfg.StealCount);
                    AddLootItemDiff(changes, Strings.U_CBC_StealRare, rewardBaseLoot.StealRareId, rewardBaseLoot.StealRareCount, cfg.StealRareId, cfg.StealRareCount);
                    AddLootItemDiff(changes, "Bribe", rewardBaseLoot.BribeId, rewardBaseLoot.BribeCount, cfg.BribeId, cfg.BribeCount);
                }
                else if (cfg.RewardGil.HasValue || cfg.RewardAp.HasValue || cfg.RewardApOverkill.HasValue || HasAdvancedLootOverride(cfg))
                {
                    changes.Add(Strings.U_CBC_LootOverridesNone);
                }

                AddInvalid(invalid, "HP", HpText, cfg.Hp);
                AddInvalid(invalid, "MP", MpText, cfg.Mp);
                AddInvalid(invalid, "HP Overkill", HpOverkillText, cfg.HpOverkill);
                AddInvalid(invalid, Strings.U_CBC_StatStrength, StrengthText, cfg.Strength);
                AddInvalid(invalid, Strings.U_CBC_StatDefense, DefenseText, cfg.Defense);
                AddInvalid(invalid, Strings.U_CBC_StatMagic, MagicText, cfg.Magic);
                AddInvalid(invalid, Strings.U_CBC_StatMagicDefense, MagicDefenseText, cfg.MagicDefense);
                AddInvalid(invalid, Strings.U_CBC_StatAgility, AgilityText, cfg.Agility);
                AddInvalid(invalid, Strings.U_CBC_StatLuck, LuckText, cfg.Luck);
                AddInvalid(invalid, Strings.U_CBC_StatEvasion, EvasionText, cfg.Evasion);
                AddInvalid(invalid, Strings.U_CBC_StatAccuracy, AccuracyText, cfg.Accuracy);
                AddInvalid(invalid, "Model ID", ModelIdText, cfg.ModelId);
                AddInvalid(invalid, "Gil", RewardGilText, cfg.RewardGil);
                AddInvalid(invalid, "AP", RewardApText, cfg.RewardAp);
                AddInvalid(invalid, "AP Overkill", RewardApOverkillText, cfg.RewardApOverkill);
                AddInvalid(invalid, "Drop chance raw", Drop1ChanceText, cfg.Drop1Chance);
                AddInvalid(invalid, Strings.U_CBC_DropCommonId, Drop1IdText, cfg.Drop1Id);
                AddInvalid(invalid, Strings.U_CBC_DropRareId, Drop1RareIdText, cfg.Drop1RareId);
                AddInvalid(invalid, Strings.U_CBC_DropCommonQty, Drop1CountText, cfg.Drop1Count);
                AddInvalid(invalid, Strings.U_CBC_DropRareQty, Drop1RareCountText, cfg.Drop1RareCount);
                AddInvalid(invalid, "Steal chance raw", StealChanceText, cfg.StealChance);
                AddInvalid(invalid, Strings.U_CBC_StealCommonId, StealIdText, cfg.StealId);
                AddInvalid(invalid, Strings.U_CBC_StealRareId, StealRareIdText, cfg.StealRareId);
                AddInvalid(invalid, Strings.U_CBC_StealCommonQty, StealCountText, cfg.StealCount);
                AddInvalid(invalid, Strings.U_CBC_StealRareQty, StealRareCountText, cfg.StealRareCount);
                AddInvalid(invalid, "Bribe ID", BribeIdText, cfg.BribeId);
                AddInvalid(invalid, Strings.U_CBC_BribeQty, BribeCountText, cfg.BribeCount);

                string aiLine = IsCopyAiMode && SelectedAiSource != null
                    ? string.Format(Strings.U_CBC_AiFinalCopy, SelectedAiSource.Id, SelectedAiSource.Name)
                    : string.Format(Strings.U_CBC_AiFinalInherit, SelectedSource.Id, SelectedSource.Name);

                List<string> lines = new()
                {
                    string.Format(Strings.U_CBC_Destination, SelectedSource.Id, SelectedSource.Name, TargetId, display),
                    aiLine,
                    lootLine
                };

                if (changes.Count == 0)
                {
                    lines.Add(Strings.F2_stats_rewards_no_numeric_difference_from_16ddf4fe);
                }
                else
                {
                    lines.Add(Strings.U_CBC_Changes);
                    lines.AddRange(changes.Select(change => $"  • {change}"));
                }

                if (invalid.Count > 0)
                {
                    lines.Add(Strings.U_CBC_InvalidInherit);
                    lines.AddRange(invalid.Select(item => $"  • {item}"));
                }

                if (!string.IsNullOrWhiteSpace(TargetIdError))
                {
                    lines.Add(string.Format(Strings.U_CBC_Warning, TargetIdError));
                }

                BossDiffPreview = string.Join('\n', lines);
            }
            catch (Exception ex)
            {
                BossDiffPreview = string.Format(Strings.U_CBC_PreviewUnavailable, ex.Message);
            }
        }

        static void AddDiff<T>(List<string> changes, string label, T baseValue, T? overrideValue)
            where T : struct, IEquatable<T>
        {
            if (!overrideValue.HasValue) return;
            if (!baseValue.Equals(overrideValue.Value))
            {
                changes.Add($"{label}: {baseValue} → {overrideValue.Value}");
            }
        }

        static void AddInvalid<T>(List<string> invalid, string label, string text, T? parsed)
            where T : struct
        {
            if (!string.IsNullOrWhiteSpace(text) && !parsed.HasValue)
            {
                invalid.Add($"{label} = \"{text}\"");
            }
        }

        static void AddLootItemDiff(List<string> changes, string label, ushort baseId, byte baseCount, ushort? overrideId, byte? overrideCount)
        {
            if (!overrideId.HasValue && !overrideCount.HasValue) return;

            ushort finalId = overrideId ?? baseId;
            byte finalCount = overrideCount ?? baseCount;
            if (finalId != baseId || finalCount != baseCount)
            {
                changes.Add($"{label}: {DescribeItemStack(baseId, baseCount)} → {DescribeItemStack(finalId, finalCount)}");
            }
        }

        static bool HasAdvancedLootOverride(CustomBossConfig cfg)
            => cfg.Drop1Chance.HasValue ||
               cfg.Drop1Id.HasValue ||
               cfg.Drop1RareId.HasValue ||
               cfg.Drop1Count.HasValue ||
               cfg.Drop1RareCount.HasValue ||
               cfg.StealChance.HasValue ||
               cfg.StealId.HasValue ||
               cfg.StealRareId.HasValue ||
               cfg.StealCount.HasValue ||
               cfg.StealRareCount.HasValue ||
               cfg.BribeId.HasValue ||
               cfg.BribeCount.HasValue;

        // ------------------------------------------------------------------
        // Target ID validation
        // ------------------------------------------------------------------

        partial void OnTargetIdChanged(int value)
        {
            ValidateTargetId();
            OnPropertyChanged(nameof(OutputPathPreview));
            RefreshBossDiffPreview();
        }

        void ValidateTargetId()
        {
            if (TargetId < 0 || TargetId > 999)
            {
                TargetIdError = Strings.F2_id_must_be_between_0_and_999_6da07ebc;
                return;
            }
            if (!Project_Service.Instance.IsProjectLoaded) { TargetIdError = string.Empty; return; }

            string path = Project_Service.Instance.GetPathMon(TargetId);
            TargetIdError = File.Exists(path)
                ? string.Format(Strings.U_CBC_TargetExists, TargetId)
                : string.Empty;
        }

        void SuggestFreeTargetId()
        {
            if (!Project_Service.Instance.IsProjectLoaded) return;

            // Find first free slot above the dictionary max (346)
            for (int id = 347; id <= 999; id++)
            {
                string path = Project_Service.Instance.GetPathMon(id);
                if (!File.Exists(path))
                {
                    TargetId = id;
                    return;
                }
            }
        }

        // ------------------------------------------------------------------
        // Create command
        // ------------------------------------------------------------------

        [RelayCommand]
        void ApplyBossPreset(string? presetKey)
        {
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                StatusMessage = Strings.F2_load_a_project_before_applying_preset_d9788637;
                return;
            }

            if (SelectedSource == null)
            {
                StatusMessage = Strings.F2_select_a_base_monster_before_applying_pr_035db485;
                return;
            }

            string key = (presetKey ?? "").Trim().ToLowerInvariant();
            suppressBossDiffRefresh = true;
            try
            {
                if (key == "inherit")
                {
                    ClearNumericOverrides();
                    StatusMessage = Strings.F2_preset_applied_inherit_all_numeric_field_a4a6db86;
                    return;
                }

                string srcPath = Project_Service.Instance.GetPathMon(SelectedSource.Id);
                if (!File.Exists(srcPath))
                {
                    throw new FileNotFoundException(string.Format(Strings.U_CBC_BaseFileMissing, srcPath));
                }

                Monster_File sourceMonster = Monster_File.Read(File.ReadAllBytes(srcPath));
                Monster_StatSheet ss = sourceMonster.StatSheetFile;
                Monster_Loot? loot = sourceMonster.LootFile;
                string presetLabel;

                switch (key)
                {
                    case "dark-lite":
                        presetLabel = "Dark Lite";
                        ApplyScaledStats(ss, hp: 4.0, mp: 2.0, overkill: 4.0, power: 1.65, defense: 1.65, speed: 1.35, luck: 1.35, accuracy: 1.25);
                        ApplyScaledRewards(loot, gil: 2.0, ap: 3.0, apOverkill: 3.0);
                        break;
                    case "tank":
                        presetLabel = Strings.U_CBC_PresetTank;
                        ApplyScaledStats(ss, hp: 3.0, mp: 1.25, overkill: 3.0, power: 1.15, defense: 2.0, speed: 0.8, luck: 1.0, accuracy: 1.0);
                        ApplyScaledRewards(loot, gil: 1.5, ap: 1.6, apOverkill: 1.6);
                        break;
                    case "glass":
                        presetLabel = Strings.U_CBC_PresetCannon;
                        ApplyScaledStats(ss, hp: 1.35, mp: 1.6, overkill: 1.35, power: 2.25, defense: 0.75, speed: 1.35, luck: 1.2, accuracy: 1.25);
                        ApplyScaledRewards(loot, gil: 1.75, ap: 2.0, apOverkill: 2.0);
                        break;
                    case "speed":
                        presetLabel = Strings.U_CBC_PresetFast;
                        ApplyScaledStats(ss, hp: 1.7, mp: 1.3, overkill: 1.7, power: 1.3, defense: 1.0, speed: 2.0, luck: 1.2, accuracy: 1.25);
                        ApplyScaledRewards(loot, gil: 1.3, ap: 1.5, apOverkill: 1.5);
                        break;
                    default:
                        StatusMessage = string.Format(Strings.U_CBC_PresetUnknown, presetKey);
                        return;
                }

                StatusMessage = string.Format(Strings.U_CBC_PresetApplied, presetLabel);
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(Strings.U_CBC_PresetApplyError, ex.Message);
            }
            finally
            {
                suppressBossDiffRefresh = false;
                RefreshBossDiffPreview();
            }
        }

        [RelayCommand]
        void CreateBoss()
        {
            if (!CanCreate) return;

            CreateSuccess = false;
            MonsterPickEntry source = SelectedSource!;

            try
            {
                string srcPath = Project_Service.Instance.GetPathMon(source.Id);
                if (!File.Exists(srcPath))
                    throw new FileNotFoundException(string.Format(Strings.U_CBC_SourceFileMissing, srcPath));

                Monster_File sourceMf = Monster_File.Read(File.ReadAllBytes(srcPath));

                CustomBossConfig cfg = BuildCurrentConfig();

                Monster_File cloned = MonsterCloner.Clone(sourceMf, cfg);
                string aiNote = ApplyAiSource(cloned, source);
                string lootNote = ApplyLootSource(cloned, source, cfg);

                // Write to disk
                string destPath = Project_Service.Instance.GetPathMon(TargetId);
                string? destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);

                File.WriteAllBytes(destPath, cloned.Write());

                // Register in Monster_Dictionary at runtime so FormationEditor shows the name
                string regName = string.IsNullOrWhiteSpace(DisplayName) ? $"Custom_{TargetId}" : DisplayName.Trim();
                Monster_Dictionary.Instance[(short)TargetId] = regName;
                string recipeNote = TryWriteBossRecipe(destPath, source, regName, cfg, aiNote, lootNote);

                CreateSuccess = true;
                StatusMessage =
                    $"✅ m{TargetId:D3}.bin criado com sucesso!\n" +
                    string.Format(Strings.U_CBC_NextStepsHeader, source.Id, source.Name, TargetId, regName) +
                    $"{aiNote}\n" +
                    $"{lootNote}\n" +
                    $"{recipeNote}\n" +
                    $"Caminho: {destPath}\n\n" +
                    Strings.U_CBC_NextSteps +
                    Strings.U_CBC_Step1 +
                    "  2. (Optional) Open the Monster AI Editor → customize the AI\n" +
                    "  3. (Optional) Open the Monster Editor → refine in-game name, drops, steal and gear";
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(Strings.U_CBC_CreateFailed, ex.Message);
            }
        }

        public void ExportBossRecipe(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                if (SelectedSource == null)
                {
                    throw new InvalidOperationException(Strings.F2_select_a_base_monster_before_exporting_a_2f9b1900);
                }

                CustomBossConfig cfg = BuildCurrentConfig();
                string display = string.IsNullOrWhiteSpace(DisplayName) ? $"Custom_{TargetId}" : DisplayName.Trim();
                string targetPath = Project_Service.Instance.IsProjectLoaded ? Project_Service.Instance.GetPathMon(TargetId) : $"m{TargetId:D3}.bin";
                string aiNote = BuildAiSourceNote(SelectedSource);
                string lootNote = BuildLootSourceNote(SelectedSource);
                CustomBossRecipeSidecar recipe = BuildBossRecipe(targetPath, SelectedSource, display, cfg, aiNote, lootNote);

                File.WriteAllText(path, JsonSerializer.Serialize(recipe, RecipeJsonOptions()));
                StatusMessage =
                    Strings.U_CBC_RecipeExported +
                    string.Format(Strings.U_CBC_RecipeFile, path) +
                    string.Format(Strings.U_CBC_NextStepsHeader, SelectedSource.Id, SelectedSource.Name, TargetId, display) +
                    $"{aiNote}\n" +
                    $"{lootNote}";
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(Strings.U_CBC_RecipeExportFail, ex.Message);
            }
        }

        public void ImportBossRecipe(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                string json = File.ReadAllText(path);
                CustomBossRecipeSidecar? recipe = JsonSerializer.Deserialize<CustomBossRecipeSidecar>(json, RecipeJsonOptions());
                if (recipe == null)
                {
                    throw new InvalidOperationException(Strings.F2_file_bossrecipe_json_is_empty_or_invalid_fffae379);
                }

                if (!string.IsNullOrWhiteSpace(recipe.Schema) && recipe.Schema != "ffx.bossrecipe.v1")
                {
                    throw new InvalidOperationException(string.Format(Strings.U_CBC_UnsupportedSchema, recipe.Schema));
                }

                if (recipe.SchemaVersion != 1)
                {
                    throw new InvalidOperationException(string.Format(Strings.U_CBC_UnsupportedVersion, recipe.SchemaVersion));
                }

                MonsterPickEntry source = FindMonsterPick(recipe.SourceMonsterId)
                    ?? throw new InvalidOperationException(string.Format(Strings.U_CBC_BaseMonsterMissing, recipe.SourceMonsterId));

                SelectedSource = source;
                TargetId = recipe.TargetMonsterId;
                DisplayName = string.IsNullOrWhiteSpace(recipe.DisplayName) ? $"Custom_{TargetId}" : recipe.DisplayName.Trim();

                ApplyAiSourceFromRecipe(recipe.AiSource, source);
                ApplyLootSourceFromRecipe(recipe.LootSource, source);
                ApplyOverridesToForm(recipe.Overrides);
                ValidateTargetId();
                RefreshAiSourceSummary();
                RefreshLootSourceSummary();
                RefreshBossDiffPreview();

                string targetWarning = string.IsNullOrWhiteSpace(TargetIdError) ? "" : "\n" + string.Format(Strings.U_CBC_Warning, TargetIdError);
                StatusMessage =
                    Strings.U_CBC_RecipeImported +
                    string.Format(Strings.U_CBC_RecipeFile, path) +
                    string.Format(Strings.U_CBC_NextStepsHeader, source.Id, source.Name, TargetId, DisplayName) + targetWarning + "\n" +
                    Strings.F2_review_the_fields_and_click_create_monst_d7e24958;
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(Strings.U_CBC_RecipeImportFail, ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        void ClearNumericOverrides()
        {
            HpText = string.Empty;
            MpText = string.Empty;
            HpOverkillText = string.Empty;
            StrengthText = string.Empty;
            DefenseText = string.Empty;
            MagicText = string.Empty;
            MagicDefenseText = string.Empty;
            AgilityText = string.Empty;
            LuckText = string.Empty;
            EvasionText = string.Empty;
            AccuracyText = string.Empty;
            ModelIdText = string.Empty;
            RewardGilText = string.Empty;
            RewardApText = string.Empty;
            RewardApOverkillText = string.Empty;
            Drop1ChanceText = string.Empty;
            Drop1IdText = string.Empty;
            Drop1RareIdText = string.Empty;
            Drop1CountText = string.Empty;
            Drop1RareCountText = string.Empty;
            StealChanceText = string.Empty;
            StealIdText = string.Empty;
            StealRareIdText = string.Empty;
            StealCountText = string.Empty;
            StealRareCountText = string.Empty;
            BribeIdText = string.Empty;
            BribeCountText = string.Empty;
        }

        void ApplyScaledStats(
            Monster_StatSheet ss,
            double hp,
            double mp,
            double overkill,
            double power,
            double defense,
            double speed,
            double luck,
            double accuracy)
        {
            HpText = ScaleUInt(ss.Hp, hp).ToString();
            MpText = ScaleUInt(ss.Mp, mp).ToString();
            HpOverkillText = ScaleUInt(ss.HpOverkill, overkill).ToString();
            StrengthText = ScaleByte(ss.Strength, power).ToString();
            DefenseText = ScaleByte(ss.Defense, defense).ToString();
            MagicText = ScaleByte(ss.Magic, power).ToString();
            MagicDefenseText = ScaleByte(ss.MagicDefense, defense).ToString();
            AgilityText = ScaleByte(ss.Agility, speed).ToString();
            LuckText = ScaleByte(ss.Luck, luck).ToString();
            EvasionText = ScaleByte(ss.Evasion, speed).ToString();
            AccuracyText = ScaleByte(ss.Accuracy, accuracy).ToString();
            ModelIdText = ss.ModelId.ToString();
        }

        void ApplyScaledRewards(Monster_Loot? loot, double gil, double ap, double apOverkill)
        {
            if (loot == null)
            {
                RewardGilText = string.Empty;
                RewardApText = string.Empty;
                RewardApOverkillText = string.Empty;
                return;
            }

            RewardGilText = ScaleReward(loot.Gil, gil).ToString();
            RewardApText = ScaleReward(loot.Ap, ap).ToString();
            RewardApOverkillText = ScaleReward(loot.ApOverkill, apOverkill).ToString();
        }

        static uint ScaleUInt(uint value, double factor)
        {
            double scaled = Math.Round(value * factor);
            return (uint)Math.Clamp(scaled, 1d, uint.MaxValue);
        }

        static byte ScaleByte(byte value, double factor)
        {
            int scaled = (int)Math.Round(value * factor);
            return (byte)Math.Clamp(scaled, 0, byte.MaxValue);
        }

        static ushort ScaleReward(ushort value, double factor)
        {
            double scaled = Math.Round(value * factor);
            return (ushort)Math.Clamp(scaled, 0d, (double)ushort.MaxValue);
        }

        CustomBossConfig BuildCurrentConfig() => new()
        {
            MonsterId = (short)TargetId,
            Hp = ParseUint(HpText),
            Mp = ParseUint(MpText),
            HpOverkill = ParseUint(HpOverkillText),
            Strength = ParseByte(StrengthText),
            Defense = ParseByte(DefenseText),
            Magic = ParseByte(MagicText),
            MagicDefense = ParseByte(MagicDefenseText),
            Agility = ParseByte(AgilityText),
            Luck = ParseByte(LuckText),
            Evasion = ParseByte(EvasionText),
            Accuracy = ParseByte(AccuracyText),
            ModelId = ParseShort(ModelIdText),
            RewardGil = ParseRewardUshort(RewardGilText),
            RewardAp = ParseRewardUshort(RewardApText),
            RewardApOverkill = ParseRewardUshort(RewardApOverkillText),
            Drop1Chance = ParseByte(Drop1ChanceText),
            Drop1Id = ParseUshort(Drop1IdText),
            Drop1RareId = ParseUshort(Drop1RareIdText),
            Drop1Count = ParseByte(Drop1CountText),
            Drop1RareCount = ParseByte(Drop1RareCountText),
            StealChance = ParseByte(StealChanceText),
            StealId = ParseUshort(StealIdText),
            StealRareId = ParseUshort(StealRareIdText),
            StealCount = ParseByte(StealCountText),
            StealRareCount = ParseByte(StealRareCountText),
            BribeId = ParseUshort(BribeIdText),
            BribeCount = ParseByte(BribeCountText),
            DisplayNameEncoded = DisplayName.Trim(),
        };

        string ApplyAiSource(Monster_File cloned, MonsterPickEntry source)
        {
            if (!IsCopyAiMode)
            {
                return string.Format(Strings.U_CBC_AiUsedInherit, source.Id, source.Name);
            }

            (MonsterPickEntry aiEntry, Monster_File aiMonster, AiScriptFile script) = ReadValidatedAiSource();
            cloned.AiFile = aiMonster.AiFile.ToArray();
            return string.Format(Strings.U_CBC_AiUsedCopiedLabel, aiEntry.Id, aiEntry.Name, AiTechnicalLabel(script));
        }

        string BuildAiSourceNote(MonsterPickEntry source)
        {
            if (!IsCopyAiMode)
            {
                return string.Format(Strings.U_CBC_AiUsedInherit, source.Id, source.Name);
            }

            (MonsterPickEntry aiEntry, _, AiScriptFile script) = ReadValidatedAiSource();
            return string.Format(Strings.U_CBC_AiUsedCopiedLabel, aiEntry.Id, aiEntry.Name, AiTechnicalLabel(script));
        }

        string ApplyLootSource(Monster_File cloned, MonsterPickEntry source, CustomBossConfig cfg)
        {
            if (!IsCopyLootMode)
            {
                MonsterCloner.ApplyLootOverrides(cloned.LootFile, cfg);
                return string.Format(Strings.U_CBC_LootUsedInherit, source.Id, source.Name);
            }

            (MonsterPickEntry lootEntry, _, Monster_Loot loot) = ReadValidatedLootSource();
            cloned.LootFile = MonsterCloner.CloneLoot(loot);
            MonsterCloner.ApplyLootOverrides(cloned.LootFile, cfg);
            return string.Format(Strings.U_CBC_LootUsedCopiedLabel, lootEntry.Id, lootEntry.Name, LootTechnicalLabel(cloned.LootFile));
        }

        string BuildLootSourceNote(MonsterPickEntry source)
        {
            if (!IsCopyLootMode)
            {
                return string.Format(Strings.U_CBC_LootUsedInherit, source.Id, source.Name);
            }

            (MonsterPickEntry lootEntry, _, Monster_Loot loot) = ReadValidatedLootSource();
            return string.Format(Strings.U_CBC_LootUsedCopiedLabel, lootEntry.Id, lootEntry.Name, LootTechnicalLabel(loot));
        }

        (MonsterPickEntry Entry, Monster_File Monster, AiScriptFile Script) ReadValidatedAiSource()
        {
            if (SelectedAiSource == null)
            {
                throw new InvalidOperationException(Strings.F2_copy_ai_mode_selected_but_no_ai_source_m_ca72e782);
            }

            string aiPath = Project_Service.Instance.GetPathMon(SelectedAiSource.Id);
            if (!File.Exists(aiPath))
            {
                throw new FileNotFoundException(string.Format(Strings.U_CBC_AiSourceFileMissing, aiPath));
            }

            Monster_File aiMonster = Monster_File.Read(File.ReadAllBytes(aiPath));
            if (aiMonster.AiFile == null || aiMonster.AiFile.Length == 0)
            {
                throw new InvalidOperationException(string.Format(Strings.U_CBC_AiNoFile, SelectedAiSource.Id));
            }

            AiScriptFile script = AiScript_File.Read(aiMonster.AiFile);
            AiValidationReport report = AiValidator.Validate(script);
            if (!report.IsValid)
            {
                string first = report.Errors.FirstOrDefault()?.Message ?? Strings.U_CBC_StructuralFailureNoDetail;
                throw new InvalidOperationException(string.Format(Strings.U_CBC_AiValidationFailed, SelectedAiSource.Id, first));
            }

            return (SelectedAiSource, aiMonster, script);
        }

        (MonsterPickEntry Entry, Monster_File Monster, Monster_Loot Loot) ReadValidatedLootSource()
        {
            if (SelectedLootSource == null)
            {
                throw new InvalidOperationException(Strings.F2_copy_loot_mode_selected_but_no_loot_sour_2091d74b);
            }

            string lootPath = Project_Service.Instance.GetPathMon(SelectedLootSource.Id);
            if (!File.Exists(lootPath))
            {
                throw new FileNotFoundException(string.Format(Strings.U_CBC_LootSourceFileMissing, lootPath));
            }

            Monster_File lootMonster = Monster_File.Read(File.ReadAllBytes(lootPath));
            if (lootMonster.LootFile == null)
            {
                throw new InvalidOperationException(string.Format(Strings.U_CBC_LootNoFile, SelectedLootSource.Id));
            }

            return (SelectedLootSource, lootMonster, lootMonster.LootFile);
        }

        void RefreshAiSourceSummary()
        {
            MonsterPickEntry? entry = IsCopyAiMode ? SelectedAiSource : SelectedSource;
            if (entry == null)
            {
                AiSourceSummary = IsCopyAiMode
                    ? Strings.F2_ai_choose_a_m_source_to_copy_the_entire_5df953bc
                    : Strings.U_CBC_AiSelectBase;
                return;
            }

            string mode = IsCopyAiMode ? Strings.U_CBC_CopyFullProfile : Strings.U_CBC_InheritBase;
            string path = Project_Service.Instance.IsProjectLoaded ? Project_Service.Instance.GetPathMon(entry.Id) : "";
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AiSourceSummary = string.Format(Strings.U_CBC_AiFilePending, mode, entry.Id, entry.Name);
                return;
            }

            try
            {
                Monster_File monster = Monster_File.Read(File.ReadAllBytes(path));
                if (monster.AiFile == null || monster.AiFile.Length == 0)
                {
                    AiSourceSummary = string.Format(Strings.U_CBC_AiNoRealFile, mode, entry.Id, entry.Name);
                    return;
                }

                AiScriptFile script = AiScript_File.Read(monster.AiFile);
                AiValidationReport report = AiValidator.Validate(script);
                string validation = report.IsValid ? Strings.U_CBC_Validated : Strings.U_CBC_WarningLabel;
                AiSourceSummary = $"AI: {mode} m{entry.Id:D3} {entry.Name} · {AiTechnicalLabel(script)} · {validation} structural.";
            }
            catch (Exception ex)
            {
                AiSourceSummary = string.Format(Strings.U_CBC_AiReadFailed, entry.Id, entry.Name, ex.Message);
            }
        }

        static string AiTechnicalLabel(AiScriptFile script) =>
            $"AiFile 0x{script.OriginalAiFileBytes.Length:X} · {script.Workers.Count} workers · {script.Instructions.Count} instr.";

        void RefreshLootSourceSummary()
        {
            MonsterPickEntry? entry = IsCopyLootMode ? SelectedLootSource : SelectedSource;
            if (entry == null)
            {
                LootSourceSummary = IsCopyLootMode
                    ? Strings.U_CBC_LootChooseSource
                    : Strings.U_CBC_LootSelectBase;
                return;
            }

            string mode = IsCopyLootMode ? Strings.U_CBC_CopyFullBlock : Strings.U_CBC_InheritBase;
            string path = Project_Service.Instance.IsProjectLoaded ? Project_Service.Instance.GetPathMon(entry.Id) : "";
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                LootSourceSummary = string.Format(Strings.U_CBC_LootFilePending, mode, entry.Id, entry.Name);
                return;
            }

            try
            {
                Monster_File monster = Monster_File.Read(File.ReadAllBytes(path));
                if (monster.LootFile == null)
                {
                    LootSourceSummary = string.Format(Strings.U_CBC_LootNoRealFile, mode, entry.Id, entry.Name);
                    return;
                }

                LootSourceSummary = $"Loot: {mode} m{entry.Id:D3} {entry.Name} · {LootTechnicalLabel(monster.LootFile)}.";
            }
            catch (Exception ex)
            {
                LootSourceSummary = string.Format(Strings.U_CBC_LootReadFailed, entry.Id, entry.Name, ex.Message);
            }
        }

        static string LootTechnicalLabel(Monster_Loot loot)
            => $"Gil {loot.Gil} · AP {loot.Ap}/{loot.ApOverkill} · drop {DescribeItemStack(loot.Drop1Id, loot.Drop1Count)} / rare {DescribeItemStack(loot.Drop1RareId, loot.Drop1RareCount)} · steal {DescribeItemStack(loot.StealId, loot.StealCount)} · bribe {DescribeItemStack(loot.BribeId, loot.BribeCount)} · chance raw D{loot.Drop1Chance}/S{loot.StealChance}/G{loot.GearChance}";

        static string DescribeItemStack(ushort itemId, byte count)
        {
            if (count == 0)
            {
                return Strings.F2_none_71f8e797;
            }

            string name = Item_Dictionary.Instance.TryGetValue(itemId, out string? itemName)
                ? itemName
                : $"item {itemId}";
            return $"{name} x{count}";
        }

        CustomBossRecipeSidecar BuildBossRecipe(string targetMonsterFile, MonsterPickEntry source, string displayName, CustomBossConfig cfg, string aiNote, string lootNote)
        {
            MonsterPickEntry aiEntry = IsCopyAiMode && SelectedAiSource != null ? SelectedAiSource : source;
            MonsterPickEntry lootEntry = IsCopyLootMode && SelectedLootSource != null ? SelectedLootSource : source;
            return new CustomBossRecipeSidecar
            {
                Schema = "ffx.bossrecipe.v1",
                SchemaVersion = 1,
                CreatedUtc = DateTimeOffset.UtcNow.ToString("O"),
                TargetMonsterId = TargetId,
                TargetMonsterFile = targetMonsterFile,
                DisplayName = displayName,
                SourceMonsterId = source.Id,
                SourceMonsterName = source.Name,
                AiSource = new CustomBossRecipeAiSource
                {
                    Mode = IsCopyAiMode ? "copy-monster-ai" : "inherit-base-ai",
                    MonsterId = aiEntry.Id,
                    MonsterName = aiEntry.Name,
                    Note = aiNote
                },
                LootSource = new CustomBossRecipeLootSource
                {
                    Mode = IsCopyLootMode ? "copy-monster-loot" : "inherit-base-loot",
                    MonsterId = lootEntry.Id,
                    MonsterName = lootEntry.Name,
                    Note = lootNote
                },
                Overrides = BuildRecipeOverrides(cfg),
                ProofStatus = "offline-writer: Monster_File.Write + optional AiFile whole-profile copy + optional LootFile whole-section copy + optional LootFile AP/Gil + Drop1/Steal/Bribe light overrides; placement/RT2 still requires Formation Editor and in-game validation"
            };
        }

        string TryWriteBossRecipe(string destPath, MonsterPickEntry source, string displayName, CustomBossConfig cfg, string aiNote, string lootNote)
        {
            try
            {
                string? dir = Path.GetDirectoryName(destPath);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    return Strings.U_CBC_RecipeOutputInvalid;
                }

                CustomBossRecipeSidecar recipe = BuildBossRecipe(destPath, source, displayName, cfg, aiNote, lootNote);

                string recipePath = Path.Combine(dir, $"m{TargetId:D3}.bossrecipe.json");
                File.WriteAllText(recipePath, JsonSerializer.Serialize(recipe, RecipeJsonOptions()));
                return string.Format(Strings.U_CBC_RecipePath, recipePath);
            }
            catch (Exception ex)
            {
                return string.Format(Strings.U_CBC_RecipeWriteFailed, ex.Message);
            }
        }

        void ApplyAiSourceFromRecipe(CustomBossRecipeAiSource? aiSource, MonsterPickEntry source)
        {
            if (aiSource?.Mode == "copy-monster-ai")
            {
                MonsterPickEntry aiEntry = FindMonsterPick(aiSource.MonsterId)
                    ?? throw new InvalidOperationException(string.Format(Strings.U_CBC_AiSourceMissing, aiSource.MonsterId));
                SelectedAiSourceMode = AiSourceModes.First(o => o.Key == AiModeCopy);
                SelectedAiSource = aiEntry;
                return;
            }

            SelectedAiSourceMode = AiSourceModes.First(o => o.Key == AiModeInherit);
            SelectedAiSource = source;
        }

        void ApplyLootSourceFromRecipe(CustomBossRecipeLootSource? lootSource, MonsterPickEntry source)
        {
            if (lootSource?.Mode == "copy-monster-loot")
            {
                MonsterPickEntry lootEntry = FindMonsterPick(lootSource.MonsterId)
                    ?? throw new InvalidOperationException(string.Format(Strings.U_CBC_LootSourceMissing, lootSource.MonsterId));
                SelectedLootSourceMode = LootSourceModes.First(o => o.Key == LootModeCopy);
                SelectedLootSource = lootEntry;
                return;
            }

            SelectedLootSourceMode = LootSourceModes.First(o => o.Key == LootModeInherit);
            SelectedLootSource = source;
        }

        void ApplyOverridesToForm(Dictionary<string, string> overrides)
        {
            Dictionary<string, string> values = overrides == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);

            HpText = OverrideOrBlank(values, "Hp");
            MpText = OverrideOrBlank(values, "Mp");
            HpOverkillText = OverrideOrBlank(values, "HpOverkill");
            StrengthText = OverrideOrBlank(values, "Strength");
            DefenseText = OverrideOrBlank(values, "Defense");
            MagicText = OverrideOrBlank(values, "Magic");
            MagicDefenseText = OverrideOrBlank(values, "MagicDefense");
            AgilityText = OverrideOrBlank(values, "Agility");
            LuckText = OverrideOrBlank(values, "Luck");
            EvasionText = OverrideOrBlank(values, "Evasion");
            AccuracyText = OverrideOrBlank(values, "Accuracy");
            ModelIdText = OverrideOrBlank(values, "ModelId");
            RewardGilText = OverrideOrBlank(values, "RewardGil");
            RewardApText = OverrideOrBlank(values, "RewardAp");
            RewardApOverkillText = OverrideOrBlank(values, "RewardApOverkill");
            Drop1ChanceText = OverrideOrBlank(values, "Drop1Chance");
            Drop1IdText = OverrideOrBlank(values, "Drop1Id");
            Drop1RareIdText = OverrideOrBlank(values, "Drop1RareId");
            Drop1CountText = OverrideOrBlank(values, "Drop1Count");
            Drop1RareCountText = OverrideOrBlank(values, "Drop1RareCount");
            StealChanceText = OverrideOrBlank(values, "StealChance");
            StealIdText = OverrideOrBlank(values, "StealId");
            StealRareIdText = OverrideOrBlank(values, "StealRareId");
            StealCountText = OverrideOrBlank(values, "StealCount");
            StealRareCountText = OverrideOrBlank(values, "StealRareCount");
            BribeIdText = OverrideOrBlank(values, "BribeId");
            BribeCountText = OverrideOrBlank(values, "BribeCount");
        }

        MonsterPickEntry? FindMonsterPick(int id)
            => allSourceMonsters.FirstOrDefault(entry => entry.Id == id);

        static string OverrideOrBlank(Dictionary<string, string> overrides, string key)
            => overrides.TryGetValue(key, out string? value) ? value : string.Empty;

        static JsonSerializerOptions RecipeJsonOptions() => new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };

        static Dictionary<string, string> BuildRecipeOverrides(CustomBossConfig cfg)
        {
            Dictionary<string, string> overrides = new();
            Add("MonsterId", cfg.MonsterId);
            Add("Hp", cfg.Hp);
            Add("Mp", cfg.Mp);
            Add("HpOverkill", cfg.HpOverkill);
            Add("Strength", cfg.Strength);
            Add("Defense", cfg.Defense);
            Add("Magic", cfg.Magic);
            Add("MagicDefense", cfg.MagicDefense);
            Add("Agility", cfg.Agility);
            Add("Luck", cfg.Luck);
            Add("Evasion", cfg.Evasion);
            Add("Accuracy", cfg.Accuracy);
            Add("ModelId", cfg.ModelId);
            Add("RewardGil", cfg.RewardGil);
            Add("RewardAp", cfg.RewardAp);
            Add("RewardApOverkill", cfg.RewardApOverkill);
            Add("Drop1Chance", cfg.Drop1Chance);
            Add("Drop1Id", cfg.Drop1Id);
            Add("Drop1RareId", cfg.Drop1RareId);
            Add("Drop1Count", cfg.Drop1Count);
            Add("Drop1RareCount", cfg.Drop1RareCount);
            Add("StealChance", cfg.StealChance);
            Add("StealId", cfg.StealId);
            Add("StealRareId", cfg.StealRareId);
            Add("StealCount", cfg.StealCount);
            Add("StealRareCount", cfg.StealRareCount);
            Add("BribeId", cfg.BribeId);
            Add("BribeCount", cfg.BribeCount);
            if (!string.IsNullOrWhiteSpace(cfg.DisplayNameEncoded))
            {
                overrides["DisplayName"] = cfg.DisplayNameEncoded.Trim();
            }
            return overrides;

            void Add<T>(string key, T? value) where T : struct
            {
                if (value.HasValue)
                {
                    overrides[key] = value.Value.ToString() ?? "";
                }
            }
        }

        static void RefillFiltered(ObservableCollection<MonsterPickEntry> target, IEnumerable<MonsterPickEntry> source, string filter)
        {
            target.Clear();
            string f = filter?.Trim() ?? string.Empty;
            IEnumerable<MonsterPickEntry> rows = source;
            if (!string.IsNullOrWhiteSpace(f))
            {
                rows = rows.Where(entry =>
                    entry.Label.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    entry.Id.ToString("D3").Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    entry.Id.ToString().Contains(f, StringComparison.OrdinalIgnoreCase));
            }

            foreach (MonsterPickEntry entry in rows)
            {
                target.Add(entry);
            }
        }

        static uint? ParseUint(string s)
            => uint.TryParse(s, out uint v) ? v : null;

        static byte? ParseByte(string s)
            => byte.TryParse(s, out byte v) ? v : null;

        static ushort? ParseUshort(string s)
            => ushort.TryParse(s, out ushort v) ? v : null;

        static short? ParseShort(string s)
            => short.TryParse(s, out short v) ? v : null;

        static ushort? ParseRewardUshort(string s)
            => ushort.TryParse(s, out ushort v) ? v : null;
    }

    internal sealed class AiSourceModeOption
    {
        public string Key { get; }
        public string Label { get; }

        public AiSourceModeOption(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public override string ToString() => Label;
    }

    internal sealed class LootSourceModeOption
    {
        public string Key { get; }
        public string Label { get; }

        public LootSourceModeOption(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public override string ToString() => Label;
    }

    internal sealed class CustomBossRecipeSidecar
    {
        public string Schema { get; set; } = "ffx.bossrecipe.v1";
        public int SchemaVersion { get; set; }
        public string CreatedUtc { get; set; } = string.Empty;
        public int TargetMonsterId { get; set; }
        public string TargetMonsterFile { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int SourceMonsterId { get; set; }
        public string SourceMonsterName { get; set; } = string.Empty;
        public CustomBossRecipeAiSource AiSource { get; set; } = new();
        public CustomBossRecipeLootSource LootSource { get; set; } = new();
        public Dictionary<string, string> Overrides { get; set; } = new();
        public string ProofStatus { get; set; } = string.Empty;
    }

    internal sealed class CustomBossRecipeAiSource
    {
        public string Mode { get; set; } = string.Empty;
        public int MonsterId { get; set; }
        public string MonsterName { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    internal sealed class CustomBossRecipeLootSource
    {
        public string Mode { get; set; } = string.Empty;
        public int MonsterId { get; set; }
        public string MonsterName { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    internal sealed class MonsterPickEntry
    {
        public int Id { get; }
        public string Name { get; }
        public string Label => $"m{Id:D3} — {Name}";

        public MonsterPickEntry(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Label;
    }
}
