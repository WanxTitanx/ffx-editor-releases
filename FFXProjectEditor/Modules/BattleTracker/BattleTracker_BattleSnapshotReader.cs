using FFXProjectEditor.Resources;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.Modules.LiveBattleLab;
using FFXProjectEditor.Services;
using System;
using System.IO;
using System.Linq;
using Xe.BinaryMapper;

namespace FFXProjectEditor.Modules.BattleTracker
{
    // ponytail: read-only BTL decode for Battle Tracker — mirrors LiveBattleLab snapshot tiles without
    // pulling in force-battle / AI probe / encounter-list prelude logic.
    internal readonly struct BattleTracker_BattleSnapshot
    {
        public string EncounterIndexLabel { get; init; }
        public string TriggerLabel { get; init; }
        public string FieldLabel { get; init; }
        public string BattlefieldLabel { get; init; }
        public string GroupLabel { get; init; }
        public string FormationLabel { get; init; }
        public string FrontlineLabel { get; init; }
        public string BacklineLabel { get; init; }
        public string RoutingCursorSummary { get; init; }
        public string BattleStateLabel { get; init; }
        public string EncounterTypeLabel { get; init; }
        public string ScreenTransitionLabel { get; init; }
        public string LastCommandLabel { get; init; }
        public string AmbushStateLabel { get; init; }
        public string BattleTypeLabel { get; init; }
        public string BattleEndTypeLabel { get; init; }

        public static BattleTracker_BattleSnapshot Empty { get; } = new()
        {
            EncounterIndexLabel = "-",
            TriggerLabel = "-",
            FieldLabel = "-",
            BattlefieldLabel = "-",
            GroupLabel = "-",
            FormationLabel = "-",
            FrontlineLabel = "-",
            BacklineLabel = "-",
            RoutingCursorSummary = "-",
            BattleStateLabel = "-",
            EncounterTypeLabel = "-",
            ScreenTransitionLabel = "-",
            LastCommandLabel = "-",
            AmbushStateLabel = "-",
            BattleTypeLabel = "-",
            BattleEndTypeLabel = "-",
        };

        public static BattleTracker_BattleSnapshot Read()
        {
            byte encounterIndex = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ENCOUNTER_INDEX);
            byte trigger = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_TRIGGER);
            string encounterLabel = $"{encounterIndex:D3} ({encounterIndex:X2}h)";
            string triggerLabel = $"{trigger:X2}h";

            MemoryBtl? battleState = TryReadBattleState();
            if (battleState == null)
            {
                return Empty with
                {
                    EncounterIndexLabel = encounterLabel,
                    TriggerLabel = triggerLabel,
                    RoutingCursorSummary = Strings.U_Bt_StructUnavailable,
                };
            }

            string fieldName = LiveBattleLab_DataModel.DecodeFieldName(battleState.field_name);
            return new BattleTracker_BattleSnapshot
            {
                EncounterIndexLabel = encounterLabel,
                TriggerLabel = triggerLabel,
                FieldLabel = $"{fieldName} · {battleState.field_idx:D3}",
                BattlefieldLabel = $"{battleState.battlefield_id:X4}h",
                GroupLabel = $"{battleState.group_idx:D2}",
                FormationLabel = $"{battleState.formation_idx:D2}",
                FrontlineLabel = FormatPartySlots(MemSharp_Service.Instance.Read<sbyte>(MemoryMap.ADDR_BATTLE_FORMATION_SLOTS, 3)),
                BacklineLabel = FormatRuntimeByteArray(battleState.backline),
                RoutingCursorSummary = BuildRoutingCursorSummary(battleState),
                BattleStateLabel = $"{battleState.battle_state:X2}h",
                EncounterTypeLabel = $"{battleState.encounter_type:X2}h",
                ScreenTransitionLabel = $"{battleState.screen_transition:X2}h",
                LastCommandLabel = $"{battleState.last_com:X8}h",
                AmbushStateLabel = $"{battleState.ambush_state:X2}h",
                BattleTypeLabel = $"{battleState.battle_type:X2}h",
                BattleEndTypeLabel = $"{battleState.battle_end_type:X2}h",
            };
        }

        static MemoryBtl? TryReadBattleState()
        {
            byte[] bytes = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BTL, 0x2200);
            if (bytes == null || bytes.Length == 0)
                return null;

            using MemoryStream stream = new(bytes);
            return BinaryMapping.ReadObject<MemoryBtl>(stream);
        }

        static string BuildRoutingCursorSummary(MemoryBtl battleState) =>
            $"cur_field {FormatPointer(battleState.ptr_btl_bin_cur_field)} · cur_encounter {FormatPointer(battleState.ptr_btl_bin_cur_encounter)} · cur_group {FormatPointer(battleState.ptr_btl_bin_cur_group)} · cur_formation {FormatPointer(battleState.ptr_btl_bin_cur_formation)}";

        static string FormatPointer(uint value) => value == 0 ? "-" : $"{value:X8}h";

        static string FormatPartySlots(sbyte[] slots)
        {
            if (slots == null || slots.Length == 0)
                return "-";

            return string.Join(" / ", slots.Select(slot =>
            {
                if (slot < 0)
                    return "(empty)";
                return Character_Dictionary.Instance.ContainsKey(slot)
                    ? Character_Dictionary.Instance[slot]
                    : $"#{slot}";
            }));
        }

        static string FormatRuntimeByteArray(byte[]? values)
        {
            if (values == null || values.Length == 0)
                return "-";
            return string.Join(" ", values.Select(v => $"{v:X2}h"));
        }
    }
}
