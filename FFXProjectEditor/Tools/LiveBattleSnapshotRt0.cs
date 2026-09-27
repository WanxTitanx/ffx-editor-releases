using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Binarysharp.MSharp;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.Modules.LiveBattleLab;
using Xe.BinaryMapper;

namespace FFXProjectEditor.Tools
{
    /// <summary>Headless live battle routing snapshot (same fields as Live Battle Lab UI).</summary>
    internal static class LiveBattleSnapshotRt0
    {
        public static int Run(string[] args)
        {
            Console.WriteLine("=== Live Battle Snapshot (headless) ===");

            Process? proc = Process.GetProcessesByName("FFX").FirstOrDefault();
            if (proc == null)
            {
                Console.WriteLine("FAIL: FFX.exe not running.");
                return 2;
            }

            Console.WriteLine($"process : FFX.exe pid={proc.Id}");

            MemorySharp mem;
            try
            {
                mem = new MemorySharp(proc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: attach — {ex.Message}");
                return 2;
            }

            IntPtr R(int rva) => mem.MakeAbsolute((IntPtr)rva);

            byte battleActive = mem.Read<byte>(R(MemoryMap.ADDR_BATTLE_ACTIVE));
            byte trigger = mem.Read<byte>(R(MemoryMap.ADDR_BATTLE_TRIGGER));
            byte encounterIndex = mem.Read<byte>(R(MemoryMap.ADDR_BATTLE_ENCOUNTER_INDEX));
            string battleName = mem.ReadString(R(MemoryMap.ADDR_BATTLE_NAME), Encoding.UTF8, false, 13).Trim('\0', ' ');

            byte[] btlBytes = mem.Read<byte>(R(MemoryMap.ADDR_BTL), 0x2200);
            MemoryBtl? battleState = null;
            if (btlBytes is { Length: > 0 })
            {
                using MemoryStream stream = new(btlBytes);
                battleState = BinaryMapping.ReadObject<MemoryBtl>(stream);
            }

            bool inBattle = battleActive == 1;
            Console.WriteLine($"inBattle: {inBattle} (ADDR_BATTLE_ACTIVE=0x{MemoryMap.ADDR_BATTLE_ACTIVE:X})");
            Console.WriteLine($"battleName: {(string.IsNullOrWhiteSpace(battleName) ? "-" : battleName)}");
            Console.WriteLine($"encounterIndex/formation byte @0xD2C259: {encounterIndex} (0x{encounterIndex:X2})");
            Console.WriteLine($"trigger: 0x{trigger:X2}");

            if (battleState == null)
            {
                Console.WriteLine("FAIL: could not decode MemoryBtl @ ADDR_BTL.");
                return 1;
            }

            string fieldName = LiveBattleLab_DataModel.DecodeFieldName(battleState.field_name);
            string guessedBattleId = fieldName == "-"
                ? "-"
                : $"{fieldName}_{battleState.formation_idx:00}";

            Console.WriteLine($"field_name: {fieldName}");
            Console.WriteLine($"field_idx: {battleState.field_idx}");
            Console.WriteLine($"group_idx: {battleState.group_idx}");
            Console.WriteLine($"formation_idx: {battleState.formation_idx}");
            Console.WriteLine($"battlefield_id: 0x{battleState.battlefield_id:X4}");
            Console.WriteLine($"battle_state: 0x{battleState.battle_state:X2}");
            Console.WriteLine($"encounter_type: 0x{battleState.encounter_type:X2}");
            Console.WriteLine($"guessed_battleId: {guessedBattleId}");
            Console.WriteLine($"FGF: field={battleState.field_idx} group={battleState.group_idx} formation={battleState.formation_idx}");

            if (args.Any(a => a == "--json"))
            {
                string json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    inBattle,
                    battleName,
                    encounterIndex,
                    trigger,
                    fieldName,
                    fieldIdx = battleState.field_idx,
                    groupIdx = battleState.group_idx,
                    formationIdx = battleState.formation_idx,
                    battlefieldId = battleState.battlefield_id,
                    guessedBattleId,
                    fgf = $"{battleState.field_idx}/{battleState.group_idx}/{battleState.formation_idx}"
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
            }

            if (!inBattle)
            {
                Console.WriteLine("WARN: not in active battle — values may be stale shell/parked BTL.");
            }

            Console.WriteLine("VERDICT: OK");
            return 0;
        }
    }
}
