using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves the field-string TextTable_File.Write (rebuild + shared-string dedup) is
    // byte-faithful (no-edit Read -> Write == original), which unlocks the deliberately-locked field-string
    // write family. Run: FFXProjectEditor.exe --textstr-rt0 [help_txt.bin]
    internal static class TextStringRt0
    {
        public static int Run(string path)
        {
            Console.WriteLine("=== TextTable_File (field-string) RT0 (no-edit Read->Write byte-identity) ===");
            Console.WriteLine($"file : {path}");
            if (!File.Exists(path)) { Console.WriteLine("NOT FOUND"); return 2; }

            byte[] orig = File.ReadAllBytes(path);
            var decoder = FfxEncoding.JpDecoder; // jppc help_txt.bin

            TextTable_File table;
            try { table = TextTable_File.Read(orig, decoder); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            byte[] re = table.Write(decoder);
            bool ok = re.Length == orig.Length && re.AsSpan().SequenceEqual(orig);
            Console.WriteLine($"strings      : {table.EntryCount}");
            Console.WriteLine($"len orig / re: {orig.Length} / {re.Length}");

            if (!ok)
            {
                int d = 0, n = Math.Min(orig.Length, re.Length);
                while (d < n && orig[d] == re[d]) d++;
                Console.WriteLine($"VERDICT: DRIFT @0x{d:X} (length {(orig.Length == re.Length ? "same" : "differs")})");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - field-string no-edit save is byte-identical (unlocks the locked write family).");
            return 0;
        }
    }
}
