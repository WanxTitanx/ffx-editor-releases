using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Battle;

public sealed record BattleCompanionRosterEdit(int Slot, int MonsterId);

/// <summary>Replaces occupied formation members. It does not alter a monster's shared AI or activation scripts.</summary>
public static class BattleCompanionRosterWriter
{
    public static byte[] Apply(string battleId, byte[] original, string expectedRevision,
        IReadOnlyList<BattleCompanionRosterEdit> edits)
    {
        if (BattleOpeningWriter.Revision(original) != expectedRevision)
            throw new InvalidOperationException("The encounter changed after its companions were loaded.");
        if (original.Length < 20) throw new InvalidDataException("Truncated encounter header.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(original) - 1;
        int script = BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(4));
        int map = BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(8));
        int formation = BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(12));
        if (count < 3 || count > 64 || script < 4L + (count + 1L) * 4 || map <= script || formation <= map ||
            formation + 28L > original.Length)
            throw new InvalidDataException("Invalid encounter formation bounds.");
        var battle = Battle_File.Read(battleId, original);
        if (!battle.CanWriteFormation || battle.Formation == null)
            throw new InvalidDataException("This encounter has no writable formation.");
        var slots = battle.Formation.Slots;
        var raw = slots.Select(s => (ushort)s.RawMonsterId).ToArray();
        var seen = new HashSet<int>(); var allowed = new HashSet<int>();
        foreach (var edit in edits)
        {
            if (!seen.Add(edit.Slot) || edit.Slot < 0 || edit.Slot >= slots.Count || slots[edit.Slot].IsEmpty)
                throw new InvalidDataException("Only existing, occupied formation slots can be replaced.");
            if (edit.MonsterId < 0 || edit.MonsterId > 999)
                throw new ArgumentOutOfRangeException(nameof(edits), "Monster ID must be between 0 and 999.");
            raw[edit.Slot] = (ushort)((raw[edit.Slot] & 0xF000) | edit.MonsterId);
            int offset = battle.FormationSlotsOffset + edit.Slot * 2;
            allowed.Add(offset); allowed.Add(offset + 1);
        }
        byte[] output = battle.WriteWithFormationSlots(raw);
        if (output.Length != original.Length || original.Where((b, i) => b != output[i] && !allowed.Contains(i)).Any())
            throw new InvalidDataException("Companion edit escaped the requested formation slots.");
        var reread = Battle_File.Read(battleId, output);
        if (reread.Formation == null || edits.Any(e => reread.Formation.Slots[e.Slot].DictionaryId != e.MonsterId))
            throw new InvalidDataException("Companion edit failed readback.");
        return output;
    }
}
