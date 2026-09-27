using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.ThunderPlains;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.ThunderPlainsEditor
{
    internal partial class ThunderPlainsEditor_DataModel : ObservableObject
    {
        const string EventRelativePath = @"jppc\event\obj\ka";
        const string PresetsFolderName = "FFXProjectEditor";
        const string PresetsFileName = "thunder_plains_presets.json";

        public ObservableCollection<ThresholdRow> Thresholds { get; } = [];
        public ObservableCollection<ThresholdRow> ConsecutiveThresholds { get; } = [];
        public ObservableCollection<ThresholdRow> TotalThresholds { get; } = [];
        public ObservableCollection<ThresholdRow> SecondaryThresholds { get; } = [];
        public ObservableCollection<DivergenceRow> Divergences { get; } = [];
        public ObservableCollection<CustomPreset> CustomPresets { get; } = [];

        [ObservableProperty] private string loadSummary = "Lightning Dodge thresholds not loaded.";
        [ObservableProperty] private string filePathSummary = "";
        [ObservableProperty] private bool hasChanges;
        [ObservableProperty] private bool canUndo;
        [ObservableProperty] private bool canRedo;
        [ObservableProperty] private bool hasDivergences;
        [ObservableProperty] private string divergenceSummary = "";
        [ObservableProperty] private ThresholdRow? selectedThreshold;

        string? kami0000Path;
        string? kami0300Path;
        byte[]? originalKami0000;
        byte[]? originalKami0300;
        byte[]? loadedKami0000;
        byte[]? loadedKami0300;

        readonly Stack<Dictionary<string, int>> undoStack = new();
        readonly Stack<Dictionary<string, int>> redoStack = new();

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public ThunderPlainsEditor_DataModel()
        {
            LoadCustomPresets();
            RefreshFromDisk();
        }

        public void RefreshFromDisk()
        {
            ClearEditState();

            string? projectPath = Project_Service.Instance.ProjectPath;
            string? ffxPs2Root = Project_Service.Instance.Path_FfxPs2Root;

            kami0000Path = ThunderPlainsLightning_File.ResolvePath(
                $@"{EventRelativePath}\kami0000\kami0000.ebp", projectPath, ffxPs2Root);
            kami0300Path = ThunderPlainsLightning_File.ResolvePath(
                $@"{EventRelativePath}\kami0300\kami0300.ebp", projectPath, ffxPs2Root);

            if (kami0000Path == null || kami0300Path == null)
            {
                LoadSummary = $"File not found. k0000={(kami0000Path ?? "MISSING")} k0300={(kami0300Path ?? "MISSING")}. "
                            + "Expected in workspace or PS2 extracted data.";
                FilePathSummary = "";
                return;
            }

            try
            {
                loadedKami0000 = File.ReadAllBytes(kami0000Path);
                loadedKami0300 = File.ReadAllBytes(kami0300Path);
            }
            catch (Exception ex)
            {
                CrashLog.Write("ThunderPlainsEditor.RefreshFromDisk: failed to read EBP files", ex);
                LoadSummary = $"Failed to read EBP files: {ex.Message}";
                return;
            }

            originalKami0000 = (byte[])loadedKami0000.Clone();
            originalKami0300 = (byte[])loadedKami0300.Clone();

            ThresholdReadResult? read = ThunderPlainsLightning_File.ReadThresholds(projectPath, ffxPs2Root);
            if (read == null)
            {
                LoadSummary = "Failed to read threshold values from EBP files.";
                return;
            }

            PopulateRows(read);
            PopulateDivergences(read);

            int patternFailures = read.Rows.Count(r => !r.PatternValidKami0000 || !r.PatternValidKami0300);
            string patternNote = patternFailures > 0
                ? $"  ⚠ {patternFailures} threshold(s) failed ATEL pattern validation."
                : "";
            LoadSummary = $"Loaded {Thresholds.Count} thresholds from "
                        + $"{Path.GetFileName(kami0000Path)} & {Path.GetFileName(kami0300Path)}.{patternNote}";
            FilePathSummary = $"{kami0000Path}\n{kami0300Path}";
        }

        void ClearEditState()
        {
            Thresholds.Clear();
            ConsecutiveThresholds.Clear();
            TotalThresholds.Clear();
            SecondaryThresholds.Clear();
            Divergences.Clear();
            undoStack.Clear();
            redoStack.Clear();
            HasChanges = false;
            CanUndo = false;
            CanRedo = false;
            HasDivergences = false;
            DivergenceSummary = "";
        }

        void PopulateRows(ThresholdReadResult read)
        {
            foreach (ThresholdReadRow r in read.Rows)
            {
                ThresholdDef def = ThunderPlainsLightning_File.KnownThresholds
                    .First(t => t.Id == r.Id);

                bool isConsec = !r.Id.StartsWith("total_", StringComparison.Ordinal);
                bool showInTotalGroup = r.Id.StartsWith("total_", StringComparison.Ordinal)
                                        && !def.IsSecondaryOccurrence;

                var row = new ThresholdRow
                {
                    ThresholdId = def.Id,
                    Label = def.Reward,
                    BitFlag = def.BitFlag,
                    CurrentValue = r.DisplayValue,
                    DefaultValue = def.DefaultValue,
                    IsConsecutive = isConsec,
                    IsSecondaryOccurrence = def.IsSecondaryOccurrence,
                    RewardType = isConsec ? "Consecutive" : "Total Bolts",
                    Kami0000Offset = def.Kami0000Offset,
                    Kami0300Offset = def.Kami0300Offset,
                    PatternValidKami0000 = r.PatternValidKami0000,
                    PatternValidKami0300 = r.PatternValidKami0300,
                    OriginalValue = r.DisplayValue,
                };
                row.Owner = this;
                Thresholds.Add(row);
                if (isConsec)
                    ConsecutiveThresholds.Add(row);
                else if (def.IsSecondaryOccurrence)
                    SecondaryThresholds.Add(row);
                else
                    TotalThresholds.Add(row);
            }
        }

        void PopulateDivergences(ThresholdReadResult read)
        {
            Divergences.Clear();
            foreach (ThresholdReadRow r in read.Rows.Where(x => x.Divergent))
            {
                Divergences.Add(new DivergenceRow
                {
                    ThresholdId = r.Id,
                    Label = r.Reward,
                    Kami0000Value = r.Kami0000Value,
                    Kami0300Value = r.Kami0300Value,
                });
            }
            HasDivergences = Divergences.Count > 0;
            DivergenceSummary = HasDivergences
                ? $"⚠ {Divergences.Count} threshold(s) diverge between kami0000 and kami0300 — Save will sync both files to the edited value."
                : "";
        }

        internal void PushUndoFromRow(int preEditValue, string thresholdId)
        {
            var snapshot = Thresholds.ToDictionary(t => t.ThresholdId, t => t.CurrentValue);
            if (snapshot.ContainsKey(thresholdId))
                snapshot[thresholdId] = preEditValue;

            undoStack.Push(snapshot);
            redoStack.Clear();
            CanUndo = true;
            CanRedo = false;
            HasChanges = true;
        }

        public void Undo()
        {
            if (undoStack.Count == 0) return;
            var current = Thresholds.ToDictionary(t => t.ThresholdId, t => t.CurrentValue);
            redoStack.Push(current);

            var prev = undoStack.Pop();
            ApplySnapshot(prev);
            CanUndo = undoStack.Count > 0;
            CanRedo = true;
            HasChanges = true;
        }

        public void Redo()
        {
            if (redoStack.Count == 0) return;
            var current = Thresholds.ToDictionary(t => t.ThresholdId, t => t.CurrentValue);
            undoStack.Push(current);

            var next = redoStack.Pop();
            ApplySnapshot(next);
            CanRedo = redoStack.Count > 0;
            CanUndo = true;
        }

        void ApplySnapshot(IReadOnlyDictionary<string, int> snapshot)
        {
            foreach (ThresholdRow row in Thresholds)
            {
                if (snapshot.TryGetValue(row.ThresholdId, out int v))
                    row.SilentSet(v);
            }
        }

        public void MarkChanged()
        {
            HasChanges = true;
        }

        public ThresholdApplyResult? ComputeSaveDiff()
        {
            if (originalKami0000 == null || originalKami0300 == null)
                return null;

            var newValues = Thresholds.ToDictionary(t => t.ThresholdId, t => t.CurrentValue);
            return ThunderPlainsLightning_File.ApplyThresholds(
                originalKami0000, originalKami0300, newValues);
        }

        public string BuildSavePreview(ThresholdApplyResult result)
        {
            if (!result.IsSuccess || result.Changes.Count == 0)
                return result.Error ?? Strings.F2_no_changes_to_apply_9d87118e;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Will modify {result.BytesChanged} byte(s) across "
                        + $"{(result.Changes.Any(c => c.FileTag == "kami0000") ? "kami0000 " : "")}"
                        + $"{(result.Changes.Any(c => c.FileTag == "kami0300") ? "kami0300" : "")}.");
            sb.AppendLine();
            foreach (var grp in result.Changes.GroupBy(c => c.ThresholdId))
            {
                var first = grp.First();
                sb.AppendLine($"• {first.ThresholdId}: {first.OldValue} → {first.NewValue}");
                foreach (var c in grp)
                    sb.AppendLine($"    [{c.FileTag} @ 0x{c.Offset:X4}]");
            }
            sb.AppendLine();
            sb.AppendLine(Strings.F2_a_timestamped_backup_will_be_created_nex_3569aed0);
            return sb.ToString();
        }

        public SaveOutcome Save()
        {
            if (kami0000Path == null || kami0300Path == null)
                return SaveOutcome.Fail(Strings.F2_no_ebp_files_loaded_eaa73b2e);

            if (originalKami0000 == null || originalKami0300 == null)
                return SaveOutcome.Fail("Original EBP buffers not available.");

            var newValues = Thresholds.ToDictionary(t => t.ThresholdId, t => t.CurrentValue);

            ThresholdApplyResult? result;
            try
            {
                result = ThunderPlainsLightning_File.ApplyThresholds(
                    originalKami0000, originalKami0300, newValues);
            }
            catch (Exception ex)
            {
                CrashLog.Write("ThunderPlainsEditor.Save: ApplyThresholds threw", ex);
                return SaveOutcome.Fail($"Internal error: {ex.Message}");
            }

            if (result is { IsSuccess: false })
            {
                CrashLog.Write("ThunderPlainsEditor.Save: validation failed",
                    new InvalidOperationException(result.Error));
                return SaveOutcome.Fail(result.Error ?? "Validation failed.");
            }
            if (result == null)
            {
                undoStack.Clear();
                redoStack.Clear();
                CanUndo = false;
                CanRedo = false;
                HasChanges = false;
                return SaveOutcome.Skipped(Strings.F2_no_changes_detected_305b7062);
            }

            try
            {
                WriteWithTimestampedBackup(kami0000Path, result.DataKami0000!);
                WriteWithTimestampedBackup(kami0300Path, result.DataKami0300!);

                originalKami0000 = (byte[])result.DataKami0000!.Clone();
                originalKami0300 = (byte[])result.DataKami0300!.Clone();
            }
            catch (Exception ex)
            {
                CrashLog.Write("ThunderPlainsEditor.Save: write failed", ex);
                return SaveOutcome.Fail($"File write failed: {ex.Message}");
            }

            foreach (ThresholdRow row in Thresholds)
                row.OriginalValue = row.CurrentValue;

            undoStack.Clear();
            redoStack.Clear();
            CanUndo = false;
            CanRedo = false;
            HasChanges = false;

            return SaveOutcome.Ok(result.BytesChanged, result.Changes);
        }

        static void WriteWithTimestampedBackup(string targetPath, byte[] bytes)
        {
            string permanentBak = targetPath + ".bak";
            if (!File.Exists(permanentBak))
            {
                File.Copy(targetPath, permanentBak, overwrite: false);
            }
            string timestamped = targetPath + ".bak." + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Copy(targetPath, timestamped, overwrite: true); }
            catch { }

            File.WriteAllBytes(targetPath, bytes);

            PruneRollingBackups(targetPath);
        }

        static void PruneRollingBackups(string targetPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(targetPath) ?? "";
                string fileBase = Path.GetFileName(targetPath);
                var rolling = Directory.GetFiles(dir, fileBase + ".bak.*")
                    .OrderByDescending(f => f)
                    .Skip(10)
                    .ToArray();
                foreach (string old in rolling)
                {
                    try { File.Delete(old); } catch { }
                }
            }
            catch { }
        }

        public void ResetToVanilla()
        {
            foreach (ThresholdRow row in Thresholds)
                row.CurrentValue = row.DefaultValue;
            HasChanges = true;
        }

        public void ApplyPreset(string presetName)
        {
            foreach (ThresholdRow row in Thresholds)
            {
                if (row.IsSecondaryOccurrence)
                    continue;

                row.CurrentValue = presetName switch
                {
                    "vanilla" => row.DefaultValue,
                    "half"    => ClampMax(row.DefaultValue / 2),
                    "easy"    => ClampMax(EasyScale(row.DefaultValue)),
                    "extreme" => ClampMax(row.DefaultValue * 2),
                    _         => row.CurrentValue,
                };
            }

            SyncSecondaryOccurrences();
            HasChanges = true;
        }

        static int EasyScale(int v) =>
            v <= 50 ? Math.Max(1, v / 5)
          : v <= 100 ? Math.Max(1, v / 4)
          : Math.Max(1, v / 3);

        static int ClampMax(int v) =>
            v < ThunderPlainsLightning_File.MIN_THRESHOLD ? ThunderPlainsLightning_File.MIN_THRESHOLD
          : v > ThunderPlainsLightning_File.MAX_THRESHOLD ? ThunderPlainsLightning_File.MAX_THRESHOLD
          : v;

        void SyncSecondaryOccurrences()
        {
            var byId = Thresholds.ToDictionary(t => t.ThresholdId, t => t.CurrentValue);
            foreach (ThresholdRow row in Thresholds.Where(r => r.IsSecondaryOccurrence))
            {
                string primaryId = row.ThresholdId switch
                {
                    "total_30b" => "total_30",
                    "total_80b" => "total_80",
                    _ => row.ThresholdId,
                };
                if (byId.TryGetValue(primaryId, out int primaryValue))
                    row.CurrentValue = primaryValue;
            }
        }

        static string CustomPresetsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PresetsFolderName, PresetsFileName);

        void LoadCustomPresets()
        {
            CustomPresets.Clear();
            try
            {
                string path = CustomPresetsPath;
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<CustomPreset>>(json, JsonOpts);
                if (list == null) return;
                foreach (var p in list)
                    CustomPresets.Add(p);
            }
            catch (Exception ex)
            {
                CrashLog.Write("ThunderPlainsEditor.LoadCustomPresets failed", ex);
            }
        }

        public void SaveCurrentAsCustomPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var preset = new CustomPreset
            {
                Name = name.Trim(),
                CreatedAt = DateTime.Now,
                Values = Thresholds
                    .Where(t => !t.IsSecondaryOccurrence)
                    .ToDictionary(t => t.ThresholdId, t => t.CurrentValue),
            };
            for (int i = 0; i < CustomPresets.Count; i++)
            {
                if (string.Equals(CustomPresets[i].Name, preset.Name, StringComparison.OrdinalIgnoreCase))
                {
                    CustomPresets[i] = preset;
                    PersistCustomPresets();
                    return;
                }
            }
            CustomPresets.Add(preset);
            PersistCustomPresets();
        }

        public void ApplyCustomPreset(string name)
        {
            CustomPreset? preset = CustomPresets.FirstOrDefault(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (preset == null) return;

            foreach (ThresholdRow row in Thresholds)
            {
                if (row.IsSecondaryOccurrence) continue;
                if (preset.Values.TryGetValue(row.ThresholdId, out int v))
                    row.CurrentValue = ClampMax(v);
            }
            SyncSecondaryOccurrences();
            HasChanges = true;
        }

        public void DeleteCustomPreset(string name)
        {
            for (int i = 0; i < CustomPresets.Count; i++)
            {
                if (string.Equals(CustomPresets[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    CustomPresets.RemoveAt(i);
                    PersistCustomPresets();
                    return;
                }
            }
        }

        void PersistCustomPresets()
        {
            try
            {
                string path = CustomPresetsPath;
                string? dir = Path.GetDirectoryName(path);
                if (dir != null) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(CustomPresets.ToList(), JsonOpts);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                CrashLog.Write("ThunderPlainsEditor.PersistCustomPresets failed", ex);
            }
        }
    }

    internal partial class ThresholdRow : ObservableObject
    {
        public ThunderPlainsEditor_DataModel? Owner;

        [ObservableProperty] private string thresholdId = "";
        [ObservableProperty] private string label = "";
        [ObservableProperty] private string bitFlag = "";
        [ObservableProperty] private int currentValue;
        [ObservableProperty] private int defaultValue;
        [ObservableProperty] private int originalValue;
        [ObservableProperty] private bool isConsecutive;
        [ObservableProperty] private bool isSecondaryOccurrence;
        [ObservableProperty] private string rewardType = "";
        [ObservableProperty] private int kami0000Offset;
        [ObservableProperty] private int kami0300Offset;
        [ObservableProperty] private bool patternValidKami0000 = true;
        [ObservableProperty] private bool patternValidKami0300 = true;

        public bool IsDirty => CurrentValue != OriginalValue;

        partial void OnCurrentValueChanged(int value)
        {
            if (Owner == null) return;
            int preEdit = OriginalValue;
            Owner.PushUndoFromRow(preEdit, ThresholdId);
            OnPropertyChanged(nameof(IsDirty));
            Owner.MarkChanged();
        }

        internal void SilentSet(int value)
        {
            var savedOwner = Owner;
            Owner = null;
            CurrentValue = value;
            Owner = savedOwner;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    internal sealed class DivergenceRow
    {
        public string ThresholdId { get; init; } = "";
        public string Label { get; init; } = "";
        public int Kami0000Value { get; init; }
        public int Kami0300Value { get; init; }
    }

    public sealed class CustomPreset
    {
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public Dictionary<string, int> Values { get; set; } = new();
    }

    public readonly record struct SaveOutcome(
        SaveOutcomeKind Kind,
        int BytesChanged,
        string Message,
        IReadOnlyList<ThresholdChangeRecord> Changes)
    {
        public static readonly SaveOutcome Default = new(SaveOutcomeKind.Skipped, 0, "", Array.Empty<ThresholdChangeRecord>());

        public static SaveOutcome Ok(int bytes, IReadOnlyList<ThresholdChangeRecord> changes) =>
            new(SaveOutcomeKind.Ok, bytes, $"Wrote {bytes} byte(s).", changes);

        public static SaveOutcome Skipped(string reason) =>
            new(SaveOutcomeKind.Skipped, 0, reason, Array.Empty<ThresholdChangeRecord>());

        public static SaveOutcome Fail(string error) =>
            new(SaveOutcomeKind.Error, 0, error, Array.Empty<ThresholdChangeRecord>());
    }

    public enum SaveOutcomeKind { Ok, Skipped, Error }
}
