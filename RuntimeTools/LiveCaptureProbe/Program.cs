using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Binarysharp.MSharp;

// ---------------------------------------------------------------------------
// READ-ONLY live verification probe for the LiveBattleLab natural-capture path.
//
// Mirrors LiveBattleLab_DataModel read semantics VERBATIM (same offsets, same
// isRelative flags). It performs NO Write and NO Execute against FFX.
//
//   (no args)        -> single snapshot of the current tick
//   watch [seconds]  -> read-only poll @250ms; prints baseline + only the rows
//                       whose dispatch/target fields change (action/move/seck/
//                       tlist/ScrChunks/ScrData). ctb/hp are shown for context
//                       but do NOT trigger a line (they churn every tick).
// ---------------------------------------------------------------------------

namespace LiveCaptureProbe
{
    internal static class Program
    {
        const int ADDR_BATTLE_ACTIVE = 0xD2A8E0;
        const int ADDR_BATTLE_TRIGGER = 0xD2A8E2;
        const int ADDR_BATTLE_ENCOUNTER_INDEX = 0xD2C259;
        const int ADDR_BATTLE_NAME = 0xD2C25A;
        const int POINTER_BATTLE_ENEMY_LIST = 0xD34460;
        const int SIZE_BATTLE_CHR_ENTRY = 0xF90;
        const int MONSTER_LIST_COUNT = 11;

        const int OFF_ID = 0x00E;
        const int OFF_MOVE_TARGET = 0x416;
        const int OFF_SECK_TARGET = 0x438;
        const int OFF_MAX_HP = 0x594;
        const int OFF_CURRENT_HP = 0x6E4;
        const int OFF_CURRENT_CTB = 0x6EC;
        const int OFF_IN_BATTLE = 0xDC8;
        const int OFF_TARGET_LIST = 0xDCA;
        const int OFF_EXIST_FLAG = 0xDD2;
        const int OFF_ACTION = 0xDD6;
        const int OFF_PTR_SCRIPT_CHUNKS = 0xF78;
        const int OFF_PTR_SCRIPT_DATA = 0xF7C;

        sealed class ActorState
        {
            public short Raw;
            public byte Action;
            public byte MoveTarget;
            public byte Seck;
            public byte TargetList;
            public int Chunks;
            public int Data;
            public int CurHp;
            public int MaxHp;
            public int Ctb;

            // Fields that define a "dispatch/target change" (ctb/hp excluded: they churn).
            public string TriggerKey => $"{Raw:X4}|{Action:X2}|{MoveTarget:X2}|{Seck:X2}|{TargetList:X2}|{Chunks:X8}|{Data:X8}";
        }

        static int Main(string[] args)
        {
            Process? proc = Process.GetProcessesByName("FFX").FirstOrDefault();
            if (proc is null)
            {
                Console.WriteLine("FFX nao encontrado.");
                return 1;
            }

            Console.WriteLine($"FFX PID {proc.Id}");

            MemorySharp mem;
            try
            {
                mem = new MemorySharp(proc);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Attach falhou (provavel privilegio): {ex.GetType().Name}: {ex.Message}");
                return 2;
            }

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "single";
            int seconds = 60;
            if (args.Length > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) && s > 0)
            {
                seconds = s;
            }

            return mode switch
            {
                "watch" => Watch(mem, seconds),
                "turnwatch" => TurnWatch(mem, seconds),
                _ => SingleShot(mem)
            };
        }

        // Fast read-only sampler. Logs SIGNAL (action/move/seck/tlist/ScrPtr edges),
        // HP changes (damage) and CTB resets (turn taken), plus a 3s heartbeat so
        // we can SEE the fight progress and decide if action/target ever move.
        static int TurnWatch(MemorySharp mem, int seconds)
        {
            const int intervalMs = 40;
            int iterations = Math.Max(1, seconds * 1000 / intervalMs);
            Console.WriteLine($"TURNWATCH read-only: {seconds}s @ {intervalMs}ms. SIGNAL=action/move/seck/tlist/ScrPtr · HP=dano · CTB-RESET=turno. Heartbeat 3s.");

            var last = new Dictionary<int, ActorState>();
            int signalEvents = 0, hpEvents = 0, ctbResets = 0;
            double startTick = Stopwatch.GetTimestamp();
            byte lastBattleActive = 2;
            double lastHeartbeat = -999;

            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    byte battleActive = mem.Read<byte>((IntPtr)ADDR_BATTLE_ACTIVE, true);
                    double elapsed = (Stopwatch.GetTimestamp() - startTick) / Stopwatch.Frequency;

                    if (battleActive != lastBattleActive)
                    {
                        Console.WriteLine($"[t={elapsed,6:0.0}s] battle_active={battleActive}");
                        lastBattleActive = battleActive;
                        if (battleActive == 0)
                        {
                            last.Clear();
                        }
                    }

                    if (battleActive == 1)
                    {
                        int listAddress = mem.Read<int>((IntPtr)POINTER_BATTLE_ENEMY_LIST, true);
                        if (listAddress > 0)
                        {
                            var current = new List<(int slot, ActorState a)>();
                            for (int slot = 0; slot < MONSTER_LIST_COUNT; slot++)
                            {
                                ActorState? a = ReadActor(mem, listAddress, slot, includeEmpty: false);
                                if (a != null)
                                {
                                    current.Add((slot, a));
                                }
                            }

                            if (elapsed - lastHeartbeat >= 3.0 && current.Count > 0)
                            {
                                string hb = string.Join("  ", current.Select(c => $"E{c.slot:D2}:act=0x{c.a.Action:X2},ctb={c.a.Ctb},hp={c.a.CurHp}"));
                                Console.WriteLine($"[t={elapsed,6:0.0}s] HB {hb}");
                                lastHeartbeat = elapsed;
                            }

                            foreach ((int slot, ActorState a) in current)
                            {
                                if (!last.TryGetValue(slot, out ActorState? prev))
                                {
                                    Console.WriteLine($"[t={elapsed,6:0.0}s] {FormatFull(slot, a)}");
                                    last[slot] = a;
                                    continue;
                                }

                                var events = new List<string>();
                                if (prev.TriggerKey != a.TriggerKey)
                                {
                                    events.Add("SIGNAL " + FormatDelta(prev, a));
                                    signalEvents++;
                                }

                                if (prev.CurHp != a.CurHp)
                                {
                                    events.Add($"hp {prev.CurHp}->{a.CurHp}");
                                    hpEvents++;
                                }

                                if (a.Ctb - prev.Ctb > 64)
                                {
                                    events.Add($"ctb-reset {prev.Ctb}->{a.Ctb} (acted?)");
                                    ctbResets++;
                                }

                                if (events.Count > 0)
                                {
                                    Console.WriteLine($"[t={elapsed,6:0.0}s] E{slot:D2} " + string.Join(" | ", events));
                                }

                                last[slot] = a; // always update so ctb countdown does not stale-trigger
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Sample falhou: {ex.GetType().Name}: {ex.Message}");
                }

                Thread.Sleep(intervalMs);
            }

            Console.WriteLine($"TURNWATCH end: {signalEvents} signal · {hpEvents} hp-change · {ctbResets} ctb-reset em {seconds}s. READ-ONLY.");
            return 0;
        }

        static int SingleShot(MemorySharp mem)
        {
            try
            {
                PrintBattleHeader(mem, out int listAddress);
                if (listAddress <= 0)
                {
                    Console.WriteLine("Ponteiro de enemy list vazio/zerado.");
                    return 0;
                }

                int decoded = 0;
                for (int slot = 0; slot < MONSTER_LIST_COUNT; slot++)
                {
                    ActorState? a = ReadActor(mem, listAddress, slot, includeEmpty: true);
                    if (a == null)
                    {
                        continue;
                    }

                    Console.WriteLine(FormatFull(slot, a));
                    decoded++;
                }

                Console.WriteLine($"-- {decoded} enemy row(s). READ-ONLY: nenhum byte escrito, nenhuma engine call.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Leitura falhou: {ex.GetType().Name}: {ex.Message}");
                return 3;
            }
        }

        static int Watch(MemorySharp mem, int seconds)
        {
            const int intervalMs = 250;
            int iterations = Math.Max(1, seconds * 1000 / intervalMs);
            Console.WriteLine($"WATCH read-only: {seconds}s @ {intervalMs}ms. Loga so executores (RawId>0) quando action/move/seck/tlist/ScrPtr mudam. ctb/hp sao contexto.");

            var last = new Dictionary<int, ActorState>();
            int transitions = 0;
            double startTick = Stopwatch.GetTimestamp();
            byte lastBattleActive = 2;

            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    byte battleActive = mem.Read<byte>((IntPtr)ADDR_BATTLE_ACTIVE, true);
                    double elapsed = (Stopwatch.GetTimestamp() - startTick) / Stopwatch.Frequency;

                    if (battleActive != lastBattleActive)
                    {
                        Console.WriteLine($"[t={elapsed,6:0.0}s] battle_active={battleActive}");
                        lastBattleActive = battleActive;
                        if (battleActive == 0)
                        {
                            last.Clear();
                        }
                    }

                    if (battleActive == 1)
                    {
                        int listAddress = mem.Read<int>((IntPtr)POINTER_BATTLE_ENEMY_LIST, true);
                        if (listAddress > 0)
                        {
                            for (int slot = 0; slot < MONSTER_LIST_COUNT; slot++)
                            {
                                ActorState? a = ReadActor(mem, listAddress, slot, includeEmpty: false);
                                if (a == null)
                                {
                                    continue;
                                }

                                if (!last.TryGetValue(slot, out ActorState? prev))
                                {
                                    Console.WriteLine($"[t={elapsed,6:0.0}s] {FormatFull(slot, a)}");
                                    last[slot] = a;
                                    continue;
                                }

                                if (prev.TriggerKey != a.TriggerKey)
                                {
                                    Console.WriteLine($"[t={elapsed,6:0.0}s] E{slot:D2} CHANGE {FormatDelta(prev, a)} | ctb={a.Ctb} hp={a.CurHp}/{a.MaxHp}");
                                    transitions++;
                                    last[slot] = a;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Sample falhou: {ex.GetType().Name}: {ex.Message}");
                }

                Thread.Sleep(intervalMs);
            }

            Console.WriteLine($"WATCH end: {transitions} transition(s) em {seconds}s. READ-ONLY.");
            return 0;
        }

        static void PrintBattleHeader(MemorySharp mem, out int listAddress)
        {
            byte battleActive = mem.Read<byte>((IntPtr)ADDR_BATTLE_ACTIVE, true);
            byte encounterIndex = mem.Read<byte>((IntPtr)ADDR_BATTLE_ENCOUNTER_INDEX, true);
            byte trigger = mem.Read<byte>((IntPtr)ADDR_BATTLE_TRIGGER, true);
            string battleName = mem.ReadString((IntPtr)ADDR_BATTLE_NAME, Encoding.UTF8, true, 13).Trim('\0', ' ');

            Console.WriteLine($"battle_active={battleActive} battle_name='{battleName}' encounter={encounterIndex:X2}h trigger={trigger:X2}h");
            if (battleActive != 1)
            {
                Console.WriteLine(">> NAO esta em batalha ativa (battle_active!=1).");
            }

            listAddress = mem.Read<int>((IntPtr)POINTER_BATTLE_ENEMY_LIST, true);
            Console.WriteLine($"enemy_list_ptr=0x{listAddress:X8}");
        }

        static ActorState? ReadActor(MemorySharp mem, int listAddress, int slot, bool includeEmpty)
        {
            int actorAddress = listAddress + slot * SIZE_BATTLE_CHR_ENTRY;
            byte[] b = mem.Read<byte>((IntPtr)actorAddress, SIZE_BATTLE_CHR_ENTRY, false);
            if (b == null || b.Length < SIZE_BATTLE_CHR_ENTRY)
            {
                return null;
            }

            short raw = (short)BitConverter.ToUInt16(b, OFF_ID);
            byte inBattle = b[OFF_IN_BATTLE];
            bool exist = b[OFF_EXIST_FLAG] != 0;
            byte action = b[OFF_ACTION];

            if (includeEmpty)
            {
                if (raw <= 0 && inBattle == 0 && !exist && action == 0)
                {
                    return null;
                }
            }
            else if (raw <= 0)
            {
                // watch mode tracks only real executors (same filter as CaptureNaturalSample)
                return null;
            }

            return new ActorState
            {
                Raw = raw,
                Action = action,
                MoveTarget = b[OFF_MOVE_TARGET],
                Seck = b[OFF_SECK_TARGET],
                TargetList = b[OFF_TARGET_LIST],
                Chunks = BitConverter.ToInt32(b, OFF_PTR_SCRIPT_CHUNKS),
                Data = BitConverter.ToInt32(b, OFF_PTR_SCRIPT_DATA),
                CurHp = BitConverter.ToInt32(b, OFF_CURRENT_HP),
                MaxHp = BitConverter.ToInt32(b, OFF_MAX_HP),
                Ctb = BitConverter.ToInt32(b, OFF_CURRENT_CTB)
            };
        }

        static string FormatFull(int slot, ActorState a)
        {
            return $"E{slot:D2} raw=0x{a.Raw:X4} action=0x{a.Action:X2} hp={a.CurHp}/{a.MaxHp} ctb={a.Ctb} " +
                   $"move=0x{a.MoveTarget:X2} seck=0x{a.Seck:X2} tlist=0x{a.TargetList:X2} " +
                   $"ScrChunks=0x{a.Chunks:X8} ScrData=0x{a.Data:X8}";
        }

        static string FormatDelta(ActorState p, ActorState a)
        {
            var parts = new List<string> { $"raw=0x{a.Raw:X4}" };
            if (p.Action != a.Action) parts.Add($"action 0x{p.Action:X2}->0x{a.Action:X2}");
            if (p.MoveTarget != a.MoveTarget) parts.Add($"move 0x{p.MoveTarget:X2}->0x{a.MoveTarget:X2}");
            if (p.Seck != a.Seck) parts.Add($"seck 0x{p.Seck:X2}->0x{a.Seck:X2}");
            if (p.TargetList != a.TargetList) parts.Add($"tlist 0x{p.TargetList:X2}->0x{a.TargetList:X2}");
            if (p.Chunks != a.Chunks) parts.Add($"ScrChunks 0x{p.Chunks:X8}->0x{a.Chunks:X8}");
            if (p.Data != a.Data) parts.Add($"ScrData 0x{p.Data:X8}->0x{a.Data:X8}");
            return string.Join(" · ", parts);
        }
    }
}
