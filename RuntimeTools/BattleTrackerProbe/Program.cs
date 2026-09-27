using System;
using System.Diagnostics;
using System.Linq;
using Binarysharp.MSharp;

namespace BattleTrackerProbe
{
    // Replicates the editor's EXACT read path (Binarysharp.MSharp, isRelative=true for RVAs,
    // isRelative=false for the heap-resident chr array) to prove the editor's read layer works
    // against the live x86 FFX.exe from an x64 host. Read-only.
    internal static class Program
    {
        const int ADDR_BATTLE_ACTIVE = 0xD2A8E0;
        const int ADDR_ENCOUNTER_INDEX = 0xD2C259;
        const int ADDR_BATTLE_NAME = 0xD2C25A;
        const int POINTER_ENEMY_LIST = 0xD34460;
        const int POINTER_PLAYER_LIST = 0xD334CC;
        const int ADDR_FORMATION_SLOTS = 0xD2C895;
        const int STRIDE = 0xF90;

        static int Main()
        {
            var proc = Process.GetProcessesByName("FFX").FirstOrDefault();
            if (proc == null) { Console.WriteLine("FFX not found"); return 2; }
            Console.WriteLine($"FFX pid={proc.Id} base=0x{proc.MainModule.BaseAddress.ToInt64():X} size=0x{proc.MainModule.ModuleMemorySize:X}");

            MemorySharp m;
            try { m = new MemorySharp(proc); }
            catch (Exception ex) { Console.WriteLine($"MemorySharp ctor THREW: {ex.GetType().Name}: {ex.Message}"); return 3; }
            Console.WriteLine($"MemorySharp ctor OK, IsRunning={m.IsRunning}");

            byte active;
            try { active = m.Read<byte>((IntPtr)ADDR_BATTLE_ACTIVE, true); }
            catch (Exception ex) { Console.WriteLine($"Read<byte>(BATTLE_ACTIVE, isRelative:true) THREW: {ex.GetType().Name}: {ex.Message}"); return 4; }
            Console.WriteLine($"BATTLE_ACTIVE = {active}  (1 = in battle)");
            Console.WriteLine($"ENCOUNTER_INDEX = {m.Read<byte>((IntPtr)ADDR_ENCOUNTER_INDEX, true)}");
            Console.WriteLine($"BATTLE_NAME = '{m.ReadString((IntPtr)ADDR_BATTLE_NAME, System.Text.Encoding.ASCII, true, 13)}'");

            int enemyPtr = m.Read<int>((IntPtr)POINTER_ENEMY_LIST, true);
            int playerPtr = m.Read<int>((IntPtr)POINTER_PLAYER_LIST, true);
            Console.WriteLine($"ENEMY_LIST  -> 0x{enemyPtr:X}");
            Console.WriteLine($"PLAYER_LIST -> 0x{playerPtr:X}");
            Console.Write("FORMATION slots: ");
            for (int i = 0; i < 3; i++) Console.Write($"{m.Read<sbyte>((IntPtr)(ADDR_FORMATION_SLOTS + i), true)} ");
            Console.WriteLine();

            Console.WriteLine("=== ENEMIES (11) ===");
            for (int k = 0; k < 11; k++)
            {
                int c = enemyPtr + k * STRIDE;
                byte[] chr = m.Read<byte>((IntPtr)c, STRIDE, false); // absolute heap read
                ushort id = BitConverter.ToUInt16(chr, 0xE);
                byte inb = chr[0xDC8];
                int hp = BitConverter.ToInt32(chr, 0x5D0);
                int mhp = BitConverter.ToInt32(chr, 0x594);
                Console.WriteLine($"  [{k,2}] id=0x{id:X4} dictId={id - 0x1000,5} InBattle={inb} HP={hp}/{mhp}");
            }
            return active == 1 ? 0 : 1;
        }
    }
}
