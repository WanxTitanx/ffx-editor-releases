using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Player;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves PlayerKernel_File.WriteSave/WriteRom are byte-faithful slot-only writers
    // (no-edit Read -> Write == original). Run: FFXProjectEditor.exe --player-rt0 [ply_save.bin] [ply_rom.bin]
    internal static class PlayerRt0
    {
        public static int Run(string savePath, string romPath)
        {
            Console.WriteLine("=== PlayerKernel RT0 (no-edit Read->Write byte-identity) ===");
            int rc = 0;
            rc |= Check("ply_save.bin", savePath, b => PlayerKernel_File.WriteSave(PlayerKernel_File.ReadSave(b)));
            rc |= Check("ply_rom.bin", romPath, b => PlayerKernel_File.WriteRom(PlayerKernel_File.ReadRom(b)));
            Console.WriteLine(rc == 0
                ? "VERDICT: PASS - PlayerKernel save/rom no-edit writes are byte-identical (slot-only)."
                : "VERDICT: DRIFT - see lines above.");
            return rc;
        }

        static int Check(string label, string path, Func<byte[], byte[]> roundtrip)
        {
            if (!File.Exists(path)) { Console.WriteLine($"{label,-13}: NOT FOUND ({path})"); return 2; }
            byte[] orig = File.ReadAllBytes(path);
            byte[] re;
            try { re = roundtrip(orig); }
            catch (Exception ex) { Console.WriteLine($"{label,-13}: THREW {ex.Message}"); return 2; }

            if (re.Length == orig.Length && re.AsSpan().SequenceEqual(orig))
            {
                Console.WriteLine($"{label,-13}: PASS ({orig.Length} bytes byte-identical)");
                return 0;
            }

            int d = 0, n = Math.Min(orig.Length, re.Length);
            while (d < n && orig[d] == re[d]) d++;
            Console.WriteLine($"{label,-13}: DRIFT @0x{d:X} (len {orig.Length}/{re.Length})");
            return 1;
        }
    }
}
