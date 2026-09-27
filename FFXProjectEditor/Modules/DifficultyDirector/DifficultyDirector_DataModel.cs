using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.DifficultyDirector
{
    internal partial class DifficultyDirector_DataModel : ObservableObject
    {
        const string BackupSuffix = ".difficulty.bak";

        DifficultyChallengeMode activeChallengeMode = DifficultyChallengeMode.None;
        bool suppressPreviewRefresh;

        public ObservableCollection<DifficultyPreviewRow> PreviewRows { get; } = new();
        public ObservableCollection<DifficultyDangerPreviewRow> DangerPreviewRows { get; } = new();

        [ObservableProperty] private double hpScale = 100;
        [ObservableProperty] private double mpScale = 100;
        [ObservableProperty] private double strengthScale = 100;
        [ObservableProperty] private double magicScale = 100;
        [ObservableProperty] private double defenseScale = 100;
        [ObservableProperty] private double otherStatsScale = 100;
        [ObservableProperty] private double dangerScale = 100;
        [ObservableProperty] private bool applyDangerWithPreset = true;
        [ObservableProperty] private string selectedPreset = "Normal";
        [ObservableProperty] private string statusMessage = Strings.F2_load_a_master_workspace_to_preview_the_d_975b4123;
        [ObservableProperty] private string monsterCountLabel = "-";
        [ObservableProperty] private string encounterCountLabel = "-";
        [ObservableProperty] private string challengeModeSummary = Strings.F2_no_active_challenge_mode_presets_edit_mo_85c49e13;

        public DifficultyDirector_DataModel()
        {
            RefreshPreview();
        }

        partial void OnHpScaleChanged(double value) => RefreshPreviewUnlessSuppressed();
        partial void OnMpScaleChanged(double value) => RefreshPreviewUnlessSuppressed();
        partial void OnStrengthScaleChanged(double value) => RefreshPreviewUnlessSuppressed();
        partial void OnMagicScaleChanged(double value) => RefreshPreviewUnlessSuppressed();
        partial void OnDefenseScaleChanged(double value) => RefreshPreviewUnlessSuppressed();
        partial void OnOtherStatsScaleChanged(double value) => RefreshPreviewUnlessSuppressed();
        partial void OnDangerScaleChanged(double value) => RefreshPreviewUnlessSuppressed();

        public void ApplyPreset(string preset)
        {
            activeChallengeMode = DifficultyChallengeMode.None;
            SelectedPreset = preset;

            if (preset == Strings.U_Dd_PresetEasy)
            {
                ChallengeModeSummary = Strings.U_Dd_SafePreset;
                SetScales(60, 70, 50);
            }
            else if (preset == Strings.U_Dd_PresetHard)
            {
                ChallengeModeSummary = Strings.U_Dd_HardCampaign;
                SetScales(150, 130, 150);
            }
            else if (preset == Strings.U_Dd_PresetDarkAeon)
            {
                ChallengeModeSummary = Strings.U_Dd_DarkAeon;
                SetScales(300, 200, 200);
            }
            else if (preset == Strings.U_Dd_PresetExploration)
            {
                ChallengeModeSummary = Strings.U_Dd_Exploration;
                SetScales(100, 100, 0);
            }
            else if (preset == Strings.U_Dd_PresetHunter)
            {
                ChallengeModeSummary = Strings.U_Dd_Hunting;
                SetScales(100, 130, 255);
            }
            else
            {
                SelectedPreset = Strings.U_Dd_PresetNormal;
                ChallengeModeSummary = Strings.F2_vanilla_preset_keeps_monsters_and_encoun_2d1f6a0d;
                SetScales(100, 100, 100);
            }
        }

        public void ApplyChallengeMode(string mode)
        {
            if (mode == Strings.U_Dd_PresetTrueNightmare)
            {
                activeChallengeMode = DifficultyChallengeMode.TrueNightmare;
                SelectedPreset = Strings.U_Dd_PresetTrueNightmare;
                ChallengeModeSummary = Strings.F2_challenge_mode_offline_hp_500_stats_300__21a70217;
                SetScales(500, 300, 255);
            }
            else if (mode == Strings.U_Dd_PresetSpeedRun)
            {
                activeChallengeMode = DifficultyChallengeMode.SpeedRunAssist;
                SelectedPreset = Strings.U_Dd_PresetSpeedRun;
                ChallengeModeSummary = Strings.U_Dd_SpeedRun;
                SetScales(50, 100, 0);
            }
            else if (mode == Strings.U_Dd_PresetExplorer)
            {
                activeChallengeMode = DifficultyChallengeMode.Explorer;
                SelectedPreset = Strings.U_Dd_PresetExplorer;
                ChallengeModeSummary = Strings.U_Dd_Explorer;
                SetScales(100, 100, 0);
            }
            else if (mode == Strings.U_Dd_PresetHunter)
            {
                activeChallengeMode = DifficultyChallengeMode.Hunter;
                SelectedPreset = Strings.U_Dd_PresetHunter;
                ChallengeModeSummary = Strings.U_Dd_ChallengeMode;
                SetScales(100, 130, 255);
            }
            else if (mode == Strings.U_Dd_PresetEqualizer)
            {
                activeChallengeMode = DifficultyChallengeMode.Equalizer;
                SelectedPreset = Strings.U_Dd_PresetEqualizer;
                ChallengeModeSummary = Strings.F2_challenge_mode_offline_normalizes_hp_mp__66cdcc41;
                SetScales(100, 100, 100);
            }
            else
            {
                ApplyPreset(Strings.U_Dd_PresetNormal);
            }
        }

        void SetScales(double hp, double stats, double danger)
        {
            suppressPreviewRefresh = true;
            try
            {
                HpScale = hp;
                MpScale = stats;
                StrengthScale = stats;
                MagicScale = stats;
                DefenseScale = stats;
                OtherStatsScale = stats;
                DangerScale = danger;
            }
            finally
            {
                suppressPreviewRefresh = false;
            }

            RefreshPreview();
        }

        public void RefreshPreview()
        {
            PreviewRows.Clear();
            DangerPreviewRows.Clear();

            bool hasMonsters = Project_Service.Instance.IsProjectLoaded && Directory.Exists(Project_Service.Instance.Path_Mon);
            if (!hasMonsters)
            {
                MonsterCountLabel = "-";
                StatusMessage = Strings.U_Dd_MasterNotLoaded;
            }
            else
            {
                RefreshMonsterPreview();
            }

            RefreshEncounterPreview();
        }

        public void ApplyToAll()
        {
            if (!Project_Service.Instance.IsProjectLoaded || !Directory.Exists(Project_Service.Instance.Path_Mon))
            {
                StatusMessage = Strings.U_Dd_ApplyAborted;
                return;
            }

            DifficultyAggregate? equalizerTarget = activeChallengeMode == DifficultyChallengeMode.Equalizer
                ? ComputeDifficultyAggregate()
                : null;

            int written = 0;
            int skipped = 0;
            foreach (string path in EnumerateMonsterFiles())
            {
                try
                {
                    Monster_File monster = Monster_File.Read(File.ReadAllBytes(path));
                    Monster_StatSheet stats = monster.StatSheetFile;
                    if (stats == null)
                    {
                        skipped++;
                        continue;
                    }

                    string backup = BackupPath(path);
                    if (!File.Exists(backup))
                    {
                        File.Copy(path, backup, overwrite: false);
                    }

                    if (equalizerTarget != null)
                    {
                        ApplyEqualizer(stats, equalizerTarget);
                    }
                    else
                    {
                        ApplyScale(stats);
                    }

                    File.WriteAllBytes(path, monster.Write());
                    written++;
                }
                catch
                {
                    skipped++;
                }
            }

            string dangerNote = ApplyDangerWithPreset
                ? ApplyEncounterDangerScale().Message
                : Strings.U_Dd_DangerNotApplied;

            RefreshPreview();
            StatusMessage = string.Format(Strings.U_Dd_Applied, written, dangerNote, BackupSuffix) +
                            (skipped > 0 ? string.Format(Strings.U_Dd_Skipped, skipped) : "");
        }

        public void RestoreBackups()
        {
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                StatusMessage = Strings.U_Dd_RestoreAborted;
                return;
            }

            int restoredMonsters = 0;
            if (Directory.Exists(Project_Service.Instance.Path_Mon))
            {
                foreach (string backup in Directory.EnumerateFiles(Project_Service.Instance.Path_Mon, "*" + BackupSuffix, SearchOption.AllDirectories))
                {
                    string target = backup[..^BackupSuffix.Length];
                    if (File.Exists(target))
                    {
                        File.Copy(backup, target, overwrite: true);
                        restoredMonsters++;
                    }
                }
            }

            int restoredEncounterTables = 0;
            string encounterPath = EncounterTablePath();
            string encounterBackup = BackupPath(encounterPath);
            if (File.Exists(encounterPath) && File.Exists(encounterBackup))
            {
                File.Copy(encounterBackup, encounterPath, overwrite: true);
                restoredEncounterTables = 1;
            }

            RefreshPreview();
            if (restoredMonsters == 0 && restoredEncounterTables == 0)
            {
                StatusMessage = string.Format(Strings.U_Dd_NoBackup, BackupSuffix);
            }
            else
            {
                StatusMessage = string.Format(Strings.U_Dd_Restored, restoredMonsters, restoredEncounterTables, BackupSuffix);
            }
        }

        void RefreshPreviewUnlessSuppressed()
        {
            if (!suppressPreviewRefresh)
            {
                RefreshPreview();
            }
        }

        void RefreshMonsterPreview()
        {
            DifficultyAggregate? equalizerTarget = activeChallengeMode == DifficultyChallengeMode.Equalizer
                ? ComputeDifficultyAggregate()
                : null;

            int scanned = 0;
            int skipped = 0;
            foreach (string path in EnumerateMonsterFiles())
            {
                try
                {
                    Monster_File monster = Monster_File.Read(File.ReadAllBytes(path));
                    Monster_StatSheet stats = monster.StatSheetFile;
                    if (stats == null)
                    {
                        skipped++;
                        continue;
                    }

                    scanned++;
                    uint afterHp = equalizerTarget?.Hp ?? ScaleUint(stats.Hp, HpScale);
                    uint afterMp = equalizerTarget?.Mp ?? ScaleUint(stats.Mp, MpScale);
                    byte afterStrength = equalizerTarget?.Strength ?? ScaleByte(stats.Strength, StrengthScale);
                    byte afterDefense = equalizerTarget?.Defense ?? ScaleByte(stats.Defense, DefenseScale);

                    PreviewRows.Add(new DifficultyPreviewRow(
                        MonsterLabelFromPath(path),
                        stats.Hp,
                        afterHp,
                        stats.Mp,
                        afterMp,
                        stats.Strength,
                        afterStrength,
                        stats.Defense,
                        afterDefense));
                }
                catch
                {
                    skipped++;
                }
            }

            List<DifficultyPreviewRow> top = PreviewRows
                .OrderByDescending(row => Math.Abs((long)row.AfterHp - row.BeforeHp))
                .ThenBy(row => row.MonsterLabel)
                .Take(5)
                .ToList();
            PreviewRows.Clear();
            foreach (DifficultyPreviewRow row in top)
            {
                PreviewRows.Add(row);
            }

            MonsterCountLabel = string.Format(Strings.U_Dd_MonstersFound, scanned) + (skipped > 0 ? string.Format(Strings.U_Dd_SkippedShort, skipped) : "");
            string modeText = activeChallengeMode == DifficultyChallengeMode.Equalizer ? Strings.U_Dd_EqualizerMode : $"HP {HpScale:0}% · stats {AverageStatScale():0}%";
            StatusMessage = string.Format(Strings.U_Dd_PreviewReady, SelectedPreset, modeText, DangerScale);
        }

        void RefreshEncounterPreview()
        {
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                EncounterCountLabel = "-";
                return;
            }

            string encounterPath = EncounterTablePath();
            if (!File.Exists(encounterPath))
            {
                EncounterCountLabel = Strings.U_Dd_BtlNotFound;
                return;
            }

            try
            {
                EncounterTable_File encounterTable = EncounterTable_File.Read(File.ReadAllBytes(encounterPath));
                int groupCount = 0;
                List<DifficultyDangerPreviewRow> rows = new();
                foreach (EncounterTable_Entry table in encounterTable.Tables)
                {
                    foreach (EncounterTable_Group group in table.Groups)
                    {
                        groupCount++;
                        int before = ClampByteValue(group.Danger);
                        int after = ScaleDanger(before);
                        rows.Add(new DifficultyDangerPreviewRow(
                            EncounterGroupLabel(table, group),
                            before,
                            after));
                    }
                }

                foreach (DifficultyDangerPreviewRow row in rows
                    .OrderByDescending(row => Math.Abs(row.AfterDanger - row.BeforeDanger))
                    .ThenBy(row => row.EncounterLabel)
                    .Take(5))
                {
                    DangerPreviewRows.Add(row);
                }

                EncounterCountLabel = $"{encounterTable.Tables.Count} tabelas · {groupCount} grupos";
            }
            catch (Exception ex)
            {
                EncounterCountLabel = string.Format(Strings.U_Dd_BtlInvalid, ex.Message);
            }
        }

        DangerApplyResult ApplyEncounterDangerScale()
        {
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                return new DangerApplyResult(0, 0, Strings.U_Dd_DangerNotAppliedNoMaster);
            }

            string encounterPath = EncounterTablePath();
            if (!File.Exists(encounterPath))
            {
                return new DangerApplyResult(0, 0, Strings.U_Dd_DangerNotAppliedNoBtl);
            }

            try
            {
                EncounterTable_File encounterTable = EncounterTable_File.Read(File.ReadAllBytes(encounterPath));
                int scanned = 0;
                int changed = 0;
                foreach (EncounterTable_Entry table in encounterTable.Tables)
                {
                    foreach (EncounterTable_Group group in table.Groups)
                    {
                        scanned++;
                        int after = ScaleDanger(group.Danger);
                        if (after != group.Danger)
                        {
                            group.Danger = after;
                            changed++;
                        }
                    }
                }

                if (changed > 0)
                {
                    string backup = BackupPath(encounterPath);
                    if (!File.Exists(backup))
                    {
                        File.Copy(encounterPath, backup, overwrite: false);
                    }

                    File.WriteAllBytes(encounterPath, encounterTable.Write());
                }

                string message = changed == 0
                    ? string.Format(Strings.U_Dd_DangerReadNoChange, scanned)
                    : string.Format(Strings.U_Dd_DangerApplied, changed, scanned);
                return new DangerApplyResult(scanned, changed, message);
            }
            catch (Exception ex)
            {
                return new DangerApplyResult(0, 0, string.Format(Strings.U_Dd_DangerNotAppliedEx, ex.Message));
            }
        }

        void ApplyScale(Monster_StatSheet stats)
        {
            stats.Hp = ScaleUint(stats.Hp, HpScale);
            stats.Mp = ScaleUint(stats.Mp, MpScale);
            stats.HpOverkill = ScaleUint(stats.HpOverkill, HpScale);
            stats.Strength = ScaleByte(stats.Strength, StrengthScale);
            stats.Magic = ScaleByte(stats.Magic, MagicScale);
            stats.Defense = ScaleByte(stats.Defense, DefenseScale);
            stats.MagicDefense = ScaleByte(stats.MagicDefense, DefenseScale);
            stats.Agility = ScaleByte(stats.Agility, OtherStatsScale);
            stats.Luck = ScaleByte(stats.Luck, OtherStatsScale);
            stats.Evasion = ScaleByte(stats.Evasion, OtherStatsScale);
            stats.Accuracy = ScaleByte(stats.Accuracy, OtherStatsScale);
        }

        static void ApplyEqualizer(Monster_StatSheet stats, DifficultyAggregate target)
        {
            stats.Hp = target.Hp;
            stats.Mp = target.Mp;
            stats.HpOverkill = target.HpOverkill;
            stats.Strength = target.Strength;
            stats.Magic = target.Magic;
            stats.Defense = target.Defense;
            stats.MagicDefense = target.MagicDefense;
            stats.Agility = target.Agility;
            stats.Luck = target.Luck;
            stats.Evasion = target.Evasion;
            stats.Accuracy = target.Accuracy;
        }

        DifficultyAggregate? ComputeDifficultyAggregate()
        {
            int count = 0;
            double hp = 0;
            double mp = 0;
            double hpOverkill = 0;
            double strength = 0;
            double magic = 0;
            double defense = 0;
            double magicDefense = 0;
            double agility = 0;
            double luck = 0;
            double evasion = 0;
            double accuracy = 0;

            foreach (string path in EnumerateMonsterFiles())
            {
                try
                {
                    Monster_File monster = Monster_File.Read(File.ReadAllBytes(path));
                    Monster_StatSheet stats = monster.StatSheetFile;
                    if (stats == null)
                    {
                        continue;
                    }

                    count++;
                    hp += stats.Hp;
                    mp += stats.Mp;
                    hpOverkill += stats.HpOverkill;
                    strength += stats.Strength;
                    magic += stats.Magic;
                    defense += stats.Defense;
                    magicDefense += stats.MagicDefense;
                    agility += stats.Agility;
                    luck += stats.Luck;
                    evasion += stats.Evasion;
                    accuracy += stats.Accuracy;
                }
                catch
                {
                    // Preview/apply should stay resilient when a custom m### is malformed.
                }
            }

            if (count == 0)
            {
                return null;
            }

            return new DifficultyAggregate(
                AverageUint(hp, count),
                AverageUint(mp, count),
                AverageUint(hpOverkill, count),
                AverageByte(strength, count),
                AverageByte(magic, count),
                AverageByte(defense, count),
                AverageByte(magicDefense, count),
                AverageByte(agility, count),
                AverageByte(luck, count),
                AverageByte(evasion, count),
                AverageByte(accuracy, count));
        }

        double AverageStatScale() =>
            (MpScale + StrengthScale + MagicScale + DefenseScale + OtherStatsScale) / 5.0;

        int ScaleDanger(int value)
        {
            int danger = ClampByteValue(value);
            if (DangerScale <= 0)
            {
                return 0;
            }

            if (DangerScale >= 255)
            {
                return 255;
            }

            double scaled = Math.Round(danger * (DangerScale / 100.0), MidpointRounding.AwayFromZero);
            return ClampByteValue((int)scaled);
        }

        static uint ScaleUint(uint value, double percent)
        {
            double scaled = Math.Round(value * (percent / 100.0), MidpointRounding.AwayFromZero);
            if (scaled < 0) return 0;
            if (scaled > uint.MaxValue) return uint.MaxValue;
            return (uint)scaled;
        }

        static byte ScaleByte(byte value, double percent)
        {
            double scaled = Math.Round(value * (percent / 100.0), MidpointRounding.AwayFromZero);
            if (scaled < 0) return 0;
            if (scaled > byte.MaxValue) return byte.MaxValue;
            return (byte)scaled;
        }

        static uint AverageUint(double total, int count)
        {
            double average = Math.Round(total / count, MidpointRounding.AwayFromZero);
            if (average < 0) return 0;
            if (average > uint.MaxValue) return uint.MaxValue;
            return (uint)average;
        }

        static byte AverageByte(double total, int count)
        {
            double average = Math.Round(total / count, MidpointRounding.AwayFromZero);
            if (average < 0) return 0;
            if (average > byte.MaxValue) return byte.MaxValue;
            return (byte)average;
        }

        static int ClampByteValue(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }

        static string BackupPath(string path) => path + BackupSuffix;

        static string EncounterTablePath() => Project_Service.Instance.Path_KernelEncounterTable;

        static bool IsMonsterFileName(string name) =>
            name.Length == 4 && (name[0] == 'm' || name[0] == 'M') && name.Skip(1).All(char.IsDigit);

        static string MonsterLabelFromPath(string path)
        {
            string id = Path.GetFileNameWithoutExtension(path);
            string numberText = id.Length > 1 ? id[1..] : "";
            if (short.TryParse(numberText, out short number) &&
                Monster_Dictionary.Instance.TryGetValue(number, out string? name))
            {
                return $"{id} · {name}";
            }

            return id;
        }

        static string EncounterGroupLabel(EncounterTable_Entry table, EncounterTable_Group group)
        {
            string map = string.IsNullOrWhiteSpace(table.Map)
                ? $"table {table.TableIndex:000}"
                : table.Map.TrimEnd('\0');
            return $"{map} · T{table.TableIndex:000}/G{group.GroupIndex:00}";
        }

        static IEnumerable<string> EnumerateMonsterFiles()
        {
            return Directory.EnumerateFiles(Project_Service.Instance.Path_Mon, "m*.bin", SearchOption.AllDirectories)
                .Where(path => IsMonsterFileName(Path.GetFileNameWithoutExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }
    }

    internal enum DifficultyChallengeMode
    {
        None,
        TrueNightmare,
        SpeedRunAssist,
        Explorer,
        Hunter,
        Equalizer
    }

    internal sealed record DifficultyAggregate(
        uint Hp,
        uint Mp,
        uint HpOverkill,
        byte Strength,
        byte Magic,
        byte Defense,
        byte MagicDefense,
        byte Agility,
        byte Luck,
        byte Evasion,
        byte Accuracy);

    internal sealed record DangerApplyResult(int GroupsScanned, int GroupsChanged, string Message);

    internal sealed record DifficultyPreviewRow(
        string MonsterLabel,
        uint BeforeHp,
        uint AfterHp,
        uint BeforeMp,
        uint AfterMp,
        byte BeforeStrength,
        byte AfterStrength,
        byte BeforeDefense,
        byte AfterDefense)
    {
        public string HpLabel => $"{BeforeHp:N0} -> {AfterHp:N0}";
        public string MpLabel => $"{BeforeMp:N0} -> {AfterMp:N0}";
        public string StrengthLabel => $"{BeforeStrength} -> {AfterStrength}";
        public string DefenseLabel => $"{BeforeDefense} -> {AfterDefense}";
    }

    internal sealed record DifficultyDangerPreviewRow(
        string EncounterLabel,
        int BeforeDanger,
        int AfterDanger)
    {
        public string DangerLabel => $"{BeforeDanger} -> {AfterDanger}";
        public string DeltaLabel => AfterDanger == BeforeDanger ? Strings.U_Dd_NoChange : $"{AfterDanger - BeforeDanger:+#;-#;0}";
    }
}
