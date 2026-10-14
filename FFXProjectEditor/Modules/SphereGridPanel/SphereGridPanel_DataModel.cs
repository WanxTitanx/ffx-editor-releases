using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.SphereGrid;
using FFXProjectEditor.Modules.SphereGridExplorer;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Encoding;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.SphereGridPanel
{
    internal partial class SphereGridPanel_DataModel : ObservableObject
    {
        readonly List<SphereGridNodeTypeEditorRow> _rows = new();
        readonly Dictionary<int, string> _commandNames = new();
        readonly Dictionary<int, string> _jpCommandNames = new();
        SphereGridNodeTypeTable? _table;

        public ObservableCollection<SphereGridPanelRowView> Rows { get; } = new();

        [ObservableProperty] private string statusSummary = "Carregue um projeto e clique Refresh.";
        [ObservableProperty] private string headerSummary = "";
        [ObservableProperty] private string pathSummary = "";
        [ObservableProperty] private string ensureCommandIdText = "321";
        [ObservableProperty] private bool hasLoadedPanel;
        [ObservableProperty] private bool hasSelectedEditor;
        [ObservableProperty] private SphereGridPanelRowView? selectedRow;
        [ObservableProperty] private SphereGridNodeTypeEditorRow? selectedEditorRow;
        [ObservableProperty] private SphereGridNamedOption? selectedSphereRequirementOption;

        public string SelectedEffectAppearanceSummary => SelectedEditorRow == null
            ? string.Empty
            : $"{SelectedEditorRow.NodeEffectBitfield:X4}h · {SelectedEditorRow.AppearanceType:X4}h";

        bool suppressSphereRequirementPropagation;

        public ObservableCollection<SphereGridNamedOption> SphereRequirementOptions { get; } = new();

        public SphereGridPanel_DataModel()
        {
            SeedSphereRequirementOptions();
        }

        void SeedSphereRequirementOptions()
        {
            SphereRequirementOptions.Clear();
            foreach ((int value, string label) in SphereGridNodeSphereRequirement.GetDropdownOptions())
                SphereRequirementOptions.Add(new SphereGridNamedOption { Value = value, Label = label });
        }

        partial void OnSelectedRowChanged(SphereGridPanelRowView? value)
        {
            SelectedEditorRow = value == null
                ? null
                : _rows.FirstOrDefault(row => row.Index == value.Index);
            HasSelectedEditor = SelectedEditorRow != null;
            SyncSphereRequirementFromRow();
            OnPropertyChanged(nameof(SelectedEffectAppearanceSummary));
        }

        partial void OnSelectedEditorRowChanged(SphereGridNodeTypeEditorRow? value)
        {
            HasSelectedEditor = value != null;
            SyncSphereRequirementFromRow();
            OnPropertyChanged(nameof(SelectedEffectAppearanceSummary));
        }

        partial void OnSelectedSphereRequirementOptionChanged(SphereGridNamedOption? value)
        {
            if (suppressSphereRequirementPropagation || value == null || SelectedEditorRow == null)
                return;

            if (!SphereGridNodeSphereRequirement.TryGetApplyResult(
                    (SphereGridNodeSphereRequirementKind)value.Value,
                    out SphereGridNodeSphereRequirementApplyResult apply))
            {
                return;
            }

            SelectedEditorRow.NodeEffectBitfield = apply.NodeEffectBitfield;
            SelectedEditorRow.AppearanceType = apply.AppearanceType;
            if (apply.ClearLearnedMove)
                SelectedEditorRow.LearnedMove = 0;
            if (apply.ClearIncreaseAmount)
                SelectedEditorRow.IncreaseAmount = 0;

            RefreshSelectedRowView();
            OnPropertyChanged(nameof(SelectedEffectAppearanceSummary));
        }

        void SyncSphereRequirementFromRow()
        {
            suppressSphereRequirementPropagation = true;
            try
            {
                if (SelectedEditorRow == null)
                {
                    SelectedSphereRequirementOption = null;
                    return;
                }

                int kind = (int)SphereGridNodeSphereRequirement.DetectKind(
                    SelectedEditorRow.NodeEffectBitfield,
                    SelectedEditorRow.AppearanceType);
                SelectedSphereRequirementOption = SphereRequirementOptions.FirstOrDefault(option => option.Value == kind)
                    ?? SphereRequirementOptions.FirstOrDefault(option => option.Value == (int)SphereGridNodeSphereRequirementKind.Custom);
            }
            finally
            {
                suppressSphereRequirementPropagation = false;
            }
        }

        void RefreshSelectedRowView()
        {
            if (SelectedEditorRow == null)
                return;

            int index = SelectedEditorRow.Index;
            int listIndex = Rows.ToList().FindIndex(row => row.Index == index);
            if (listIndex < 0)
                return;

            Rows[listIndex] = ToView(SelectedEditorRow);
            if (SelectedRow?.Index == index)
                SelectedRow = Rows[listIndex];
        }

        public void Refresh()
        {
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                HasLoadedPanel = false;
                StatusSummary = "Abra um projeto FFX primeiro.";
                return;
            }

            LoadCommandNames();
            LoadPanelTable();
        }

        void LoadPanelTable()
        {
            Rows.Clear();
            _rows.Clear();
            _table = null;
            HasLoadedPanel = false;

            string jpPath = Project_Service.Instance.Path_KernelPanel;
            string usPath = Project_Service.Instance.Path_KernelPanelUs;
            if (!File.Exists(jpPath) && !File.Exists(usPath))
            {
                StatusSummary = "panel.bin not found in the project kernel.";
                PathSummary = jpPath;
                return;
            }

            try
            {
                _table = SphereGrid_File.ReadNodeTypes(jpPath, File.Exists(usPath) ? usPath : null);
                foreach (SphereGridNodeTypeEntry entry in _table.Entries.OrderBy(entry => entry.Index))
                {
                    SphereGridNodeTypeEditorRow row = new()
                    {
                        Index = entry.Index,
                        JpName = entry.Name.JpText,
                        UsName = entry.Name.UsText,
                        JpSimplifiedName = entry.SimplifiedName.JpText,
                        UsSimplifiedName = entry.SimplifiedName.UsText,
                        JpDescription = entry.Description.JpText,
                        UsDescription = entry.Description.UsText,
                        JpSimplifiedDescription = entry.SimplifiedDescription.JpText,
                        UsSimplifiedDescription = entry.SimplifiedDescription.UsText,
                        NodeEffectBitfield = entry.NodeEffectBitfield,
                        LearnedMove = entry.LearnedMove,
                        IncreaseAmount = entry.IncreaseAmount,
                        AppearanceType = entry.AppearanceType,
                    };
                    _rows.Add(row);
                    Rows.Add(ToView(row));
                }

                int slotsLeft = SphereGridPanelGrowWriter.MaxPanelContentIndex - _table.Header.MaxIndex;
                HeaderSummary =
                    string.Format(Strings.U_Sgp_SlotsSummary, _table.Header.MinIndex.ToString("X2"), _table.Header.MaxIndex.ToString("X2"), _table.Entries.Count, Math.Max(0, slotsLeft));
                PathSummary = BuildPathLabel(jpPath, usPath);
                HasLoadedPanel = true;
                StatusSummary = string.Format(Strings.U_Sgp_Loaded, _table.Entries.Count);
            }
            catch (Exception ex)
            {
                StatusSummary = Strings.U_Sgp_ReadFailed + ex.Message;
            }
        }

        public void Save()
        {
            if (_table == null)
            {
                StatusSummary = "Nothing loaded.";
                return;
            }

            try
            {
                SphereGrid_File.WriteNodeTypes(_table, _rows.Select(ToWriteModel).ToList());
                LoadPanelTable();
                StatusSummary = Strings.U_Sgp_Written;
            }
            catch (Exception ex)
            {
                StatusSummary = Strings.U_Sgp_SaveFailed + ex.Message;
            }
        }

        public void RestoreFromBackup()
        {
            if (!TryGetProjectPanelPaths(out string jpPath, out string? usPath))
                return;

            if (!RestoreLocaleFromBackup(jpPath))
            {
                StatusSummary = string.Format(Strings.U_Sgp_BackupNotFound, jpPath);
                return;
            }

            if (!string.IsNullOrWhiteSpace(usPath))
                RestoreLocaleFromBackup(usPath);

            LoadPanelTable();
            StatusSummary = Strings.U_Sgp_Restored;
        }

        public void RestoreVanilla()
        {
            if (!TryGetProjectPanelPaths(out string jpPath, out string? usPath))
                return;

            if (!TryResolveVanillaPanelPaths(out string srcJp, out string? srcUs))
            {
                StatusSummary = Strings.U_Sgp_VanillaNotFound;
                return;
            }

            SphereGridPanelGrowWriter.BackupPanelFilePublic(jpPath);
            if (!string.IsNullOrWhiteSpace(usPath))
                SphereGridPanelGrowWriter.BackupPanelFilePublic(usPath!);

            File.Copy(srcJp, jpPath, overwrite: true);
            if (srcUs != null && File.Exists(srcUs) && !string.IsNullOrWhiteSpace(usPath))
                File.Copy(srcUs, usPath!, overwrite: true);

            LoadPanelTable();
            StatusSummary = "panel.bin restaurado do vanilla master.";
        }

        public void PopulateClones()
        {
            if (!TryGetProjectPanelPaths(out string jpPath, out string? usPath))
                return;

            if (_commandNames.Count == 0)
                LoadCommandNames();

            try
            {
                SphereGridPanelGrowReport report = SphereGridPanelGrowWriter.PopulateMissingCommandNodeTypes(
                    jpPath,
                    usPath,
                    _commandNames,
                    _jpCommandNames.Count > 0 ? _jpCommandNames : null,
                    minCommandId: SphereGridPanelGrowWriter.DefaultBulkMinCommandId,
                    preferHighCommandIds: true);

                LoadPanelTable();
                StatusSummary = report.Summary;
            }
            catch (Exception ex)
            {
                StatusSummary = Strings.U_Sgp_PopulateFailed + ex.Message;
            }
        }

        public void EnsureSelectedCommand()
        {
            if (!int.TryParse((EnsureCommandIdText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cmdId))
            {
                StatusSummary = Strings.U_Sgp_InvalidCommandId;
                return;
            }

            if (!TryGetProjectPanelPaths(out string jpPath, out string? usPath))
                return;

            if (_commandNames.Count == 0)
                LoadCommandNames();

            string usName = _commandNames.TryGetValue(cmdId, out string? name) ? name : $"Command {cmdId}";
            _jpCommandNames.TryGetValue(cmdId, out string? jpName);

            try
            {
                int index = SphereGridPanelGrowWriter.EnsureNodeTypeForCommand(jpPath, usPath, cmdId, usName, jpName);
                LoadPanelTable();
                SelectedRow = Rows.FirstOrDefault(row => row.Index == index);
                StatusSummary = string.Format(Strings.U_Sgp_EnsureDone, cmdId, index, (SphereGridPanelGrowWriter.CommandCategoryNibble | cmdId));
            }
            catch (Exception ex)
            {
                StatusSummary = string.Format(Strings.U_Sgp_EnsureFailed, cmdId, ex.Message);
            }
        }

        static bool RestoreLocaleFromBackup(string path)
        {
            string backup = path + ".panelgrow.bak";
            if (!File.Exists(backup))
                return false;

            File.Copy(backup, path, overwrite: true);
            return true;
        }

        bool TryGetProjectPanelPaths(out string jpPath, out string? usPath)
        {
            jpPath = "";
            usPath = null;
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                StatusSummary = Strings.U_Sgp_ProjectNotLoaded;
                return false;
            }

            jpPath = Project_Service.Instance.Path_KernelPanel;
            usPath = File.Exists(Project_Service.Instance.Path_KernelPanelUs)
                ? Project_Service.Instance.Path_KernelPanelUs
                : null;

            if (!File.Exists(jpPath) && usPath == null)
            {
                StatusSummary = "panel.bin absent from the project.";
                return false;
            }

            return true;
        }

        static bool TryResolveVanillaPanelPaths(out string jpPath, out string? usPath)
        {
            jpPath = string.Empty;
            usPath = null;
            string? root = ResolvePortableMasterRoot("jppc", "battle", "kernel", "panel.bin");
            if (root == null)
                return false;

            jpPath = Path.Combine(root, "jppc", "battle", "kernel", "panel.bin");
            string usCandidate = Path.Combine(root, "new_uspc", "battle", "kernel", "panel.bin");
            usPath = File.Exists(usCandidate) ? usCandidate : null;
            return File.Exists(jpPath);
        }

        static string? ResolvePortableMasterRoot(params string[] requiredRelativeSegments)
        {
            var candidates = new List<string?>
            {
                Environment.GetEnvironmentVariable("FFX_VANILLA_MASTER"),
                PortablePathResolver.MasterRoot,
            };

            string? ffxPs2 = PortablePathResolver.FfxPs2Root;
            if (!string.IsNullOrWhiteSpace(ffxPs2))
                candidates.Insert(1, Path.Combine(ffxPs2, "ffx", "master"));

            foreach (string? candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                try
                {
                    string full = Path.GetFullPath(candidate);
                    string required = full;
                    foreach (string segment in requiredRelativeSegments)
                        required = Path.Combine(required, segment);
                    if (File.Exists(required))
                        return full;
                }
                catch
                {
                    // Invalid/inaccessible configuration is treated as an unavailable reference root.
                }
            }
            return null;
        }

        void LoadCommandNames()
        {
            _commandNames.Clear();
            _jpCommandNames.Clear();

            LoadNamesFromPath(ResolveCommandPath(true), FfxEncoding.UsDecoder, _commandNames);
            LoadNamesFromPath(ResolveCommandPath(false), FfxEncoding.JpDecoder, _jpCommandNames);
        }

        static void LoadNamesFromPath(string? path, Dictionary<byte, char> decoder, Dictionary<int, string> target)
        {
            if (path == null || !File.Exists(path))
                return;

            List<Ability_Command> commands = Ability_Command.ReadList(File.ReadAllBytes(path), hasExtraInfo: true);
            for (int i = 0; i < commands.Count; i++)
            {
                string name = FfxEncoding.DecodeScript(commands[i].NameScriptBytes).GetString(decoder);
                target[i] = string.IsNullOrWhiteSpace(name) ? "-" : name;
            }
        }

        static string? ResolveCommandPath(bool usLocale)
        {
            Project_Service proj = Project_Service.Instance;
            if (proj.IsProjectLoaded)
            {
                string path = usLocale ? proj.Path_KernelCommandUs : proj.Path_KernelCommand;
                if (File.Exists(path))
                    return path;
            }

            string? baseDir = ResolvePortableMasterRoot(
                usLocale ? "new_uspc" : "jppc", "battle", "kernel", "command.bin");
            if (baseDir == null)
                return null;
            string file = usLocale ? "new_uspc" : "jppc";
            string candidate = Path.Combine(baseDir, file, "battle", "kernel", "command.bin");
            return File.Exists(candidate) ? candidate : null;
        }

        static SphereGridPanelRowView ToView(SphereGridNodeTypeEditorRow row)
        {
            string name = !string.IsNullOrWhiteSpace(row.UsName) ? row.UsName : row.JpName;
            int cmdId = row.LearnedMove & 0xFFF;
            string cmdLabel = row.LearnedMove >= 0x3000
                ? $"CMD #{cmdId:D3}"
                : (row.LearnedMove == 0 ? "stat/other" : $"move {row.LearnedMove:X4}h");

            return new SphereGridPanelRowView
            {
                Index = row.Index,
                DisplayIndex = $"{row.Index:X2}h",
                Name = string.IsNullOrWhiteSpace(name) ? $"NodeType {row.Index:X2}h" : name,
                LearnedMoveLabel = $"{row.LearnedMove:X4}h",
                CommandLabel = cmdLabel,
                IncreaseAmount = row.IncreaseAmount,
                SphereLabel = SphereGridNodeSphereRequirement.FormatShortLabel(row.NodeEffectBitfield, row.AppearanceType),
            };
        }

        static SphereGridNodeTypeWriteModel ToWriteModel(SphereGridNodeTypeEditorRow row) => new()
        {
            Index = row.Index,
            JpName = row.JpName,
            UsName = row.UsName,
            JpSimplifiedName = row.JpSimplifiedName,
            UsSimplifiedName = row.UsSimplifiedName,
            JpDescription = row.JpDescription,
            UsDescription = row.UsDescription,
            JpSimplifiedDescription = row.JpSimplifiedDescription,
            UsSimplifiedDescription = row.UsSimplifiedDescription,
            LearnedMove = row.LearnedMove,
            IncreaseAmount = row.IncreaseAmount,
            NodeEffectBitfield = row.NodeEffectBitfield,
            AppearanceType = row.AppearanceType,
        };

        static string BuildPathLabel(string jpPath, string usPath) =>
            File.Exists(usPath) ? $"JP: {jpPath}{Environment.NewLine}US: {usPath}" : $"JP: {jpPath}";
    }

    internal sealed class SphereGridPanelRowView
    {
        public required int Index { get; init; }
        public required string DisplayIndex { get; init; }
        public required string Name { get; init; }
        public required string LearnedMoveLabel { get; init; }
        public required string CommandLabel { get; init; }
        public required ushort IncreaseAmount { get; init; }
        public required string SphereLabel { get; init; }

        public string Summary => $"{DisplayIndex} · {SphereLabel} · {Name} · {LearnedMoveLabel} · {CommandLabel} · +{IncreaseAmount}";
    }
}
