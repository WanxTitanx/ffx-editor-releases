// ============================================================================
// BattleCompanionActivationWriter — battle-backed companion writer (m213 host + 2 companions)
// PURPOSE : patches the pre-seeded companion slots (1 and 2, under host m213 in slot 0) of a loaded battle,
//           verifying byte-identity inside the slot window and re-encoding raw ids (preserves high nibble).
// WHY     : the "Flux host/companion pair" product lane needs a byte-safe way to swap the two companions of a
//           battle-file backing a m213 activation without disturbing anything outside the formation slots.
// EVIDENCE: HostMonsterId=213; writable monster ids 0..0x0FFF; raw id high nibble (0xF000) preserved.
// MAINT   : only works on a RECOGNIZED activation package and a battle exposing a writable formation; any
//           departure is rejected with a clear error (no silent no-op). Keep raw-id re-encode symmetric.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Battle
{
    public sealed record BattleCompanionActivationWriteRequest(
        string BattleId,
        int Slot1MonsterId,
        int Slot2MonsterId);

    public sealed record BattleCompanionActivationSlotEdit(
        int SlotIndex,
        int OldMonsterId,
        int NewMonsterId,
        ushort OldRawMonsterId,
        ushort NewRawMonsterId);

    public sealed record BattleCompanionActivationWriteResult(
        BattleCompanionActivationWriteRequest Request,
        byte[] EditedBattleBytes,
        IReadOnlyList<BattleCompanionActivationSlotEdit> SlotEdits);

    public static class BattleCompanionActivationWriter
    {
        const int HostMonsterId = 213;

        public static bool TryApplyCompanionFormationPatch(
            Battle_File battle,
            BattleCompanionActivation_File activation,
            BattleCompanionActivationWriteRequest request,
            out BattleCompanionActivationWriteResult? result,
            out string error)
        {
            result = null;
            error = string.Empty;

            ArgumentNullException.ThrowIfNull(battle);
            ArgumentNullException.ThrowIfNull(activation);
            ArgumentNullException.ThrowIfNull(request);

            if (!activation.HasRecognizedPackage)
            {
                error = "The writer m213 only opens in recognized packages, not in sentinel/partial.";
                return false;
            }

            if (!battle.CanWriteFormation || battle.Formation == null)
            {
                error = "The current battle does not expose a writable formation for this battle-backed writer.";
                return false;
            }

            if (!battle.BattleId.Equals(request.BattleId, StringComparison.OrdinalIgnoreCase))
            {
                error = $"A battle carregada ({battle.BattleId}) nao bate com o package pedido ({request.BattleId}).";
                return false;
            }

            IReadOnlyList<Battle_FormationSlot> slots = battle.Formation.Slots;
            if (slots.Count < 3 || slots[0].IsEmpty || slots[0].DictionaryId != HostMonsterId)
            {
                error = "The current formation does not close the host m213 in slot0.";
                return false;
            }

            if (!IsWritableMonsterId(request.Slot1MonsterId) || !IsWritableMonsterId(request.Slot2MonsterId))
            {
                error = "The new companions must be real monster IDs (0..4095).";
                return false;
            }

            ushort[] rawIds = slots
                .Select(slot => slot.IsEmpty ? (ushort)0xFFFF : (ushort)slot.RawMonsterId)
                .ToArray();
            ushort slot1Raw = ReencodeRawMonsterId(rawIds[1], request.Slot1MonsterId);
            ushort slot2Raw = ReencodeRawMonsterId(rawIds[2], request.Slot2MonsterId);

            if (rawIds[1] == slot1Raw && rawIds[2] == slot2Raw)
            {
                error = "Nothing changed in the two preseeded companions of this battle.";
                return false;
            }

            rawIds[1] = slot1Raw;
            rawIds[2] = slot2Raw;
            byte[] editedBattle = battle.WriteWithFormationSlots(rawIds);

            if (!FormationSlotWriter.IsSlotOnly(
                    battle.OriginalBytes,
                    editedBattle,
                    battle.FormationSlotsOffset,
                    Battle_File.FormationSlotsLength))
            {
                error = "The patch m213 left the slot-only window of the formation and was aborted.";
                return false;
            }

            result = new BattleCompanionActivationWriteResult(
                request,
                editedBattle,
                [
                    new BattleCompanionActivationSlotEdit(
                        1,
                        slots[1].DictionaryId,
                        request.Slot1MonsterId,
                        (ushort)slots[1].RawMonsterId,
                        slot1Raw),
                    new BattleCompanionActivationSlotEdit(
                        2,
                        slots[2].DictionaryId,
                        request.Slot2MonsterId,
                        (ushort)slots[2].RawMonsterId,
                        slot2Raw),
                ]);
            return true;
        }

        static bool IsWritableMonsterId(int monsterId) => monsterId >= 0 && monsterId <= 0x0FFF;

        static ushort ReencodeRawMonsterId(ushort currentRawMonsterId, int newMonsterId)
        {
            ushort highBits = (ushort)(currentRawMonsterId & 0xF000);
            return (ushort)(highBits | (newMonsterId & 0x0FFF));
        }
    }
}
