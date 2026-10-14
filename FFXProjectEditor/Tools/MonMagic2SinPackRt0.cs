using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;

namespace FFXProjectEditor.Tools
{
    internal static class MonMagic2SinPackRt0
    {
        public static int Run(string inputPath, string outputDir)
        {
            Console.WriteLine("=== MonMagic2 Sin Pack RT0 (5 new monster skills) ===");
            Console.WriteLine($"input  : {inputPath}");
            Console.WriteLine($"output : {outputDir}");

            if (!File.Exists(inputPath))
            {
                Console.WriteLine("monmagic2 file NOT FOUND");
                return 2;
            }

            byte[] original = File.ReadAllBytes(inputPath);
            EntryListFile elf = EntryListFile.Unpack(original);
            Console.WriteLine($"current entries: {elf.Header.RealEntryCount} (entry size 0x{elf.Header.EntrySize:X})");

            MonsterMagicGrowResult[] results = new MonsterMagicGrowResult[5];
            byte[] current = original;

            (string label, Func<byte[], bool, int, MonsterMagicGrowResult> append)[] steps =
            {
                ("Frost-Flood Weave", MonsterMagicGrowWriter.AppendFrostFloodWeave),
                ("Shoreline Break",   MonsterMagicGrowWriter.AppendShorelineBreak),
                ("Sin Salve",         MonsterMagicGrowWriter.AppendSinSalve),
                ("Mist Chorus",       MonsterMagicGrowWriter.AppendMistChorus),
                ("Counter March",     MonsterMagicGrowWriter.AppendCounterMarch),
            };

            for (int i = 0; i < steps.Length; i++)
            {
                try
                {
                    results[i] = steps[i].append(current, true, -1);
                    Console.WriteLine($"  [{i}] {steps[i].label,-20} id={results[i].NewId} operand=0x{results[i].Operand:X4} pass={results[i].Pass} len={results[i].OriginalLength}->{results[i].GrownLength}");
                    if (!results[i].Pass)
                    {
                        Console.WriteLine($"  [{i}] FAILED: rows={results[i].ExistingRecordPayloadPreserved} text={results[i].ExistingTextPoolPrefixPreserved} count={results[i].RereadCountOk} textOk={results[i].RereadNewTextOk} write={results[i].PreserveWriteOk}");
                        return 1;
                    }
                    current = results[i].GrownBytes;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [{i}] {steps[i].label} THREW: {ex.GetType().Name}: {ex.Message}");
                    return 2;
                }
            }

            Directory.CreateDirectory(outputDir);
            string outFile = Path.Combine(outputDir, Path.GetFileName(inputPath));
            File.WriteAllBytes(outFile, current);
            Console.WriteLine($"wrote: {outFile} ({current.Length} bytes, {results[^1].NewEntryCount} entries)");

            int fail = results.Count(r => !r.Pass);
            Console.WriteLine(fail == 0
                ? "VERDICT: PASS - 5 new monmagic2 skills grown + round-trip byte-faithful."
                : $"VERDICT: FAIL - {fail} skill(s) failed round-trip.");
            return fail == 0 ? 0 : 1;
        }
    }
}
