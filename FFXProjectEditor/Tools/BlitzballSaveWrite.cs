using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.Tools
{
    // Blitzball save-file prize WRITER (Jarvis-WAKKA, lab — save-proved offsets, in-game checksum unconfirmed).
    // Sets one prize-index u16 in an FFX save at the save-proved offset and writes the result to a NEW file.
    // SAFETY: it NEVER overwrites the input save (output must be a different path), so your real saves are never
    // touched in place — you back up / copy the edited file into a slot yourself, and validate one in-game load.
    //
    // Usage:
    //   --blitz-save-write <input-save> <field> <prize-value> <output-file> [--base 0x40]
    //   field = league0|league1|league2|tournament0|tournament1|tournament2|league-top|tournament-top
    //   prize-value = prize index (0..189): 0..100 treasure (takara=val+220), 101..160 tech, 187..189 overdrive
    internal static class BlitzballSaveWrite
    {
        const int SaveDataInFileDefault = 0x40;

        // field -> SaveData-relative offset (file offset = base + this). u16 LE.
        static readonly Dictionary<string, int> Fields = new(StringComparer.OrdinalIgnoreCase)
        {
            ["league0"] = 0x19FC, ["league1"] = 0x19FE, ["league2"] = 0x1A00,
            ["tournament0"] = 0x1A02, ["tournament1"] = 0x1A04, ["tournament2"] = 0x1A06,
            ["league-top"] = 0x1A08, ["tournament-top"] = 0x1A0A,
        };

        public static int Run(string[] args)
        {
            int baseOff = SaveDataInFileDefault;
            List<string> pos = new();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--base" && i + 1 < args.Length) baseOff = ParseHex(args[++i]);
                else pos.Add(args[i]);
            }

            if (pos.Count < 4)
            {
                Console.WriteLine("usage: --blitz-save-write <input-save> <field> <prize-value> <output-file> [--base 0x40]");
                Console.WriteLine("  field = " + string.Join("|", Fields.Keys));
                Console.WriteLine("  prize-value = 0..189 (0..100 treasure, 101..160 tech, 187..189 overdrive)");
                return 2;
            }

            string input = pos[0];
            string field = pos[1];
            string output = pos[3];

            try
            {
                if (!Fields.TryGetValue(field, out int sdOff))
                {
                    Console.WriteLine($"ERROR: unknown field '{field}'. valid: {string.Join(", ", Fields.Keys)}");
                    return 2;
                }
                if (!int.TryParse(pos[2], out int value) || value < 0 || value > 0xFFFF)
                {
                    Console.WriteLine($"ERROR: prize-value must be 0..65535 (valid prizes 0..189).");
                    return 2;
                }
                if (value > 189 || (value > 160 && value < 187))
                    Console.WriteLine($"  ! WARNING: {value} is outside the known prize ranges (0..160, 187..189); the game may show nothing.");

                // SAFETY: refuse to overwrite the input in place.
                string fullIn = Path.GetFullPath(input);
                string fullOut = Path.GetFullPath(output);
                if (string.Equals(fullIn, fullOut, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("ERROR: output must be a DIFFERENT path from the input (this tool never overwrites the source save).");
                    return 2;
                }

                byte[] data = File.ReadAllBytes(input);
                int fileOff = baseOff + sdOff;
                if (fileOff + 1 >= data.Length)
                {
                    Console.WriteLine($"ERROR: offset 0x{fileOff:X} out of file bounds ({data.Length} bytes).");
                    return 1;
                }

                int oldVal = data[fileOff] | (data[fileOff + 1] << 8);
                data[fileOff] = (byte)(value & 0xFF);
                data[fileOff + 1] = (byte)((value >> 8) & 0xFF);
                File.WriteAllBytes(output, data);

                Console.WriteLine("=== Blitzball save WRITE (lab; writes a NEW file, never the source) ===");
                Console.WriteLine($"input : {input}");
                Console.WriteLine($"output: {output} ({data.Length} bytes)");
                Console.WriteLine($"field : {field} @ file 0x{fileOff:X} (SaveData+0x{sdOff:X})");
                Console.WriteLine($"  {oldVal} (0x{oldVal:X4}) [{BlitzballSaveRead.ResolvePrize(oldVal)}]");
                Console.WriteLine($"  -> {value} (0x{value:X4}) [{BlitzballSaveRead.ResolvePrize(value)}]");
                Console.WriteLine();
                Console.WriteLine("NEXT: back up your real slot, copy the output over it, load ONCE in-game, claim the prize.");
                Console.WriteLine("If it loads and grants the reward, the save is checksum-free for edits (RT2/save-proved).");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static int ParseHex(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return Convert.ToInt32(s, 16);
        }
    }
}
