using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Battle;

namespace FFXProjectEditor.Modules.AuroraChamber;

internal readonly record struct AuroraProjectEncounterBinding(int EncounterId, int Battlefield, string PreviewSlotBattleId);

/// <summary>
/// HD/regional and project-only formations need not exist in the PS2 viewer's table.
/// Reuse a preview slot from the same area and battlefield; its bytes are replaced
/// by the selected project encounter through the existing owned overlay capability.
/// This is a scene binding, not a claim that the two encounter files are identical.
/// </summary>
internal static class AuroraProjectEncounterResolver
{
    internal static int? ResolveBattlefield(EncounterTable_File projectTable, string battleId)
    {
        string? map = MapKey(battleId);
        if (map == null) return null;
        var entries = projectTable.Tables.Where(x => x.Map.Equals(map, StringComparison.OrdinalIgnoreCase)).ToArray();
        int[] explicitFields = entries.SelectMany(x => x.Groups)
            .Where(g => g.Formations.Any(f => f.BattleId.Equals(battleId, StringComparison.OrdinalIgnoreCase)))
            .Select(g => g.Battlefield).Distinct().ToArray();
        if (explicitFields.Length > 0) return explicitFields.Length == 1 ? explicitFields[0] : null;

        // An unlisted formation can inherit its area's stage only if it is unique.
        // In particular, never guess a Nagi05 stage from its first group.
        int[] areaFields = entries.SelectMany(x => x.Groups).Select(g => g.Battlefield).Distinct().ToArray();
        return areaFields.Length == 1 ? areaFields[0] : null;
    }

    internal static bool TryResolve(string battleId, EncounterTable_File projectTable,
        EncounterTable_File viewerTable, Func<int, bool> previewSlotExists,
        out AuroraProjectEncounterBinding binding)
    {
        binding = default;
        int? battlefield = ResolveBattlefield(projectTable, battleId);
        string? map = MapKey(battleId);
        if (battlefield == null || map == null) return false;
        var candidates = new List<AuroraProjectEncounterBinding>();
        foreach (var entry in viewerTable.Tables.Where(t => t.Map.Equals(map, StringComparison.OrdinalIgnoreCase)))
        {
            int encounterId = entry.FormationOffset;
            foreach (var group in entry.Groups)
            foreach (var formation in group.Formations)
            {
                // The file number advances across every formation, including groups
                // with a different stage. FormationId is not the file number.
                if (group.Battlefield == battlefield && encounterId is >= 0 and <= ushort.MaxValue && previewSlotExists(encounterId))
                    candidates.Add(new(encounterId, battlefield.Value, formation.BattleId));
                encounterId++;
            }
        }
        if (candidates.Count == 0) return false;
        binding = candidates.OrderByDescending(x => x.PreviewSlotBattleId.Equals(battleId, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.EncounterId).First();
        return true;
    }

    private static string? MapKey(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId)) return null;
        int separator = battleId.LastIndexOf('_');
        if (separator != 6 || separator == battleId.Length - 1 ||
            !battleId[..separator].All(char.IsAsciiLetterOrDigit) ||
            !battleId[(separator + 1)..].All(char.IsAsciiDigit)) return null;
        return battleId[..separator];
    }
}
