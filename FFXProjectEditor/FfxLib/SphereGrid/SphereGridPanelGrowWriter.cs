using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.SphereGrid
{
    /// <summary>
    /// Grows <c>panel.bin</c> with new node-type rows for commands that lack a LearnedMove mapping.
    /// </summary>
    public static class SphereGridPanelGrowWriter
    {
        public const int CommandCategoryNibble = 0x3000;
        /// <summary>Party-wide / OD commands — rarely need new panel rows.</summary>
        public const int DefaultMinCommandId = 96;
        /// <summary>Default bulk scope for Spira Reforge: appended clone rows only.</summary>
        public const int DefaultBulkMinCommandId = 320;
        public const int MaxPanelContentIndex = SphereGridLayoutBuilder.EmptyContent - 1; // 0xFE

        public static int EnsureNodeTypeForCommand(
            string jpPanelPath,
            string? usPanelPath,
            int commandId,
            string usCommandName,
            string? jpCommandName = null,
            bool backup = true)
        {
            if (commandId < 0 || commandId > 0xFFF)
                throw new ArgumentOutOfRangeException(nameof(commandId), "command id must fit in 12 bits.");

            ushort learnedMove = unchecked((ushort)(CommandCategoryNibble | commandId));
            SphereGridNodeTypeTable table = SphereGrid_File.ReadNodeTypes(jpPanelPath, usPanelPath);
            SphereGridNodeTypeEntry? existing = table.Entries.FirstOrDefault(entry => entry.LearnedMove == learnedMove);
            if (existing != null)
                return existing.Index;

            if (backup)
            {
                BackupPanelFile(jpPanelPath);
                if (!string.IsNullOrWhiteSpace(usPanelPath))
                    BackupPanelFile(usPanelPath!);
            }

            string jpName = string.IsNullOrWhiteSpace(jpCommandName) ? usCommandName : jpCommandName!;
            return SphereGrid_File.AppendNodeTypeForLearnedMove(table, learnedMove, jpName, usCommandName);
        }

        public static SphereGridPanelGrowReport PopulateMissingCommandNodeTypes(
            string jpPanelPath,
            string? usPanelPath,
            IReadOnlyDictionary<int, string> usCommandNames,
            IReadOnlyDictionary<int, string>? jpCommandNames = null,
            int minCommandId = DefaultMinCommandId,
            int? maxCommandId = null,
            bool preferHighCommandIds = true,
            bool backup = true)
        {
            ArgumentNullException.ThrowIfNull(usCommandNames);

            SphereGridPanelGrowReport report = new();
            if (backup)
            {
                BackupPanelFile(jpPanelPath);
                if (!string.IsNullOrWhiteSpace(usPanelPath))
                    BackupPanelFile(usPanelPath!);
            }

            HashSet<ushort> mappedLearnedMoves = SphereGrid_File.ReadNodeTypes(jpPanelPath, usPanelPath)
                .Entries
                .Where(entry => entry.LearnedMove != 0)
                .Select(entry => entry.LearnedMove)
                .ToHashSet();

            IEnumerable<(int commandId, string usName)> candidates = usCommandNames
                .Where(pair => pair.Key >= minCommandId && (!maxCommandId.HasValue || pair.Key <= maxCommandId.Value))
                .Select(pair => (pair.Key, pair.Value));

            candidates = preferHighCommandIds
                ? candidates.OrderByDescending(pair => pair.commandId)
                : candidates.OrderBy(pair => pair.commandId);

            foreach ((int commandId, string usName) in candidates)
            {
                ushort learnedMove = unchecked((ushort)(CommandCategoryNibble | commandId));
                if (mappedLearnedMoves.Contains(learnedMove))
                {
                    report.AlreadyMapped++;
                    continue;
                }

                try
                {
                    string? jpName = null;
                    jpCommandNames?.TryGetValue(commandId, out jpName);
                    int newIndex = EnsureNodeTypeForCommand(
                        jpPanelPath,
                        usPanelPath,
                        commandId,
                        usName,
                        jpName,
                        backup: false);
                    mappedLearnedMoves.Add(learnedMove);
                    report.Appended++;
                    report.AppendedCommandIds.Add(commandId);
                    report.AppendedContentIndices[commandId] = newIndex;
                }
                catch (Exception ex) when (IsPanelFullError(ex))
                {
                    report.PanelFull = true;
                    report.SkippedBecauseFull++;
                    break;
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"#{commandId:D3} ({usName}): {ex.Message}");
                }
            }

            return report;
        }

        static bool IsPanelFullError(Exception ex) =>
            ex.Message.Contains("cannot grow past index", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("0xFF is reserved", StringComparison.OrdinalIgnoreCase);

        static void BackupPanelFile(string path)
        {
            if (!File.Exists(path))
                return;

            string backupPath = path + ".panelgrow.bak";
            File.Copy(path, backupPath, overwrite: true);
        }

        /// <summary>Public wrapper for UI restore/backup flows.</summary>
        public static void BackupPanelFilePublic(string path) => BackupPanelFile(path);
    }

    public sealed class SphereGridPanelGrowReport
    {
        public int AlreadyMapped { get; set; }
        public int Appended { get; set; }
        public int SkippedBecauseFull { get; set; }
        public bool PanelFull { get; set; }
        public List<int> AppendedCommandIds { get; } = new();
        public Dictionary<int, int> AppendedContentIndices { get; } = new();
        public List<string> Errors { get; } = new();

        public string Summary
        {
            get
            {
                string baseMsg = Errors.Count == 0
                    ? string.Format(Strings.U_Bb_PanelGrowSummary, Appended, AlreadyMapped)
                    : string.Format(Strings.U_Bb_PanelGrowSummaryErr, Appended, AlreadyMapped, Errors.Count);

                if (PanelFull)
                {
                    baseMsg +=
                        string.Format(Strings.U_Bb_PanelFull, SphereGridPanelGrowWriter.MaxPanelContentIndex);
                }

                return baseMsg;
            }
        }
    }

    readonly record struct SphereGridNodeTypeAppendText(
        string Name,
        string SimplifiedName,
        string Description,
        string SimplifiedDescription);
}
