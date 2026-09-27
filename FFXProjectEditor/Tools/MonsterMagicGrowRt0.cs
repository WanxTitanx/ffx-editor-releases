using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;

namespace FFXProjectEditor.Tools
{
    // Headless first step for true new monster magic authoring. It grows monmagic1/2 by one row,
    // writes the grown file to work/ only, rereads it, and proves existing row/text payloads stayed
    // prefix-identical while the new id becomes addressable as 0x4xxx/0x6xxx for Monster AI.
    // Run via: FFXProjectEditor.exe --monmagic-grow-rt0 [monmagic2.bin] [outputDir]
    internal static class MonsterMagicGrowRt0
    {
        public static int Run(string inputPath, string outputDir)
        {
            Console.WriteLine("=== Monster Magic GROW RT0 (append one real monmagic row) ===");
            Console.WriteLine($"input  : {inputPath}");
            Console.WriteLine($"output : {outputDir}");

            if (!File.Exists(inputPath))
            {
                Console.WriteLine("monmagic file NOT FOUND");
                return 2;
            }

            bool monsterMagic2 = !Path.GetFileName(inputPath).Contains("monmagic1", StringComparison.OrdinalIgnoreCase);
            byte[] original = File.ReadAllBytes(inputPath);

            MonsterMagicGrowResult result;
            try
            {
                result = MonsterMagicGrowWriter.AppendPrismFlare(original, monsterMagic2);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GROW THREW: {ex.GetType().Name}: {ex.Message}");
                return 2;
            }

            Directory.CreateDirectory(outputDir);
            string outFile = Path.Combine(outputDir, Path.GetFileName(inputPath));
            string jsonFile = Path.Combine(outputDir, "monster_magic_grow_rt0.json");
            string runbookFile = Path.Combine(outputDir, "RT2_MONSTER_MAGIC_GROW_RUNBOOK.md");

            File.WriteAllBytes(outFile, result.GrownBytes);
            File.WriteAllText(jsonFile, JsonSerializer.Serialize(new
            {
                source = inputPath,
                output = outFile,
                sourceSha256 = Sha256Hex(result.OriginalBytes),
                outputSha256 = Sha256Hex(result.GrownBytes),
                result.OriginalEntryCount,
                result.NewEntryCount,
                result.DonorId,
                result.NewId,
                operand = $"0x{result.Operand:X4}",
                result.Name,
                result.Description,
                result.OriginalLength,
                result.GrownLength,
                result.ExistingRecordPayloadPreserved,
                result.ExistingTextPoolPrefixPreserved,
                result.RereadCountOk,
                result.RereadNewTextOk,
                result.PreserveWriteOk,
                result.Pass
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(runbookFile, BuildRunbook(inputPath, outFile, result));

            Console.WriteLine($"entries : {result.OriginalEntryCount} -> {result.NewEntryCount}");
            Console.WriteLine($"new id  : {result.NewId} (operand 0x{result.Operand:X4})");
            Console.WriteLine($"name    : {result.Name}");
            Console.WriteLine($"length  : {result.OriginalLength} -> {result.GrownLength} (+{result.GrownLength - result.OriginalLength})");
            Console.WriteLine("checks  : "
                + $"oldRowsPrefix={result.ExistingRecordPayloadPreserved}, "
                + $"oldTextPrefix={result.ExistingTextPoolPrefixPreserved}, "
                + $"rereadCount={result.RereadCountOk}, "
                + $"rereadText={result.RereadNewTextOk}, "
                + $"preserveWrite={result.PreserveWriteOk}");
            Console.WriteLine($"wrote   : {outFile}");
            Console.WriteLine($"json    : {jsonFile}");
            Console.WriteLine($"runbook : {runbookFile}");

            if (!result.Pass)
            {
                Console.WriteLine("VERDICT: FAIL - grown monmagic table did not satisfy the append contract.");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - created a new monmagic entry and staged a game-loadable kernel file in work/.");
            return 0;
        }

        static string BuildRunbook(string sourcePath, string outputPath, MonsterMagicGrowResult result)
        {
            string fileName = Path.GetFileName(outputPath);
            string folder = fileName.Equals("monmagic1.bin", StringComparison.OrdinalIgnoreCase)
                ? "battle/kernel/monmagic1.bin"
                : "battle/kernel/monmagic2.bin";

            return $@"# RT2 Monster Magic Grow Pilot

Status: RT2 pending

Created file: `{outputPath}`
Source file: `{sourcePath}`

New monster magic:

- Name: `{result.Name}`
- New id: `{result.NewId}`
- Monster AI operand: `0x{result.Operand:X4}`
- Donor id: `{result.DonorId}`
- Table count: `{result.OriginalEntryCount} -> {result.NewEntryCount}`
- File length: `{result.OriginalLength} -> {result.GrownLength}`

Offline proof:

- existing record payload prefix preserved: `{result.ExistingRecordPayloadPreserved}`
- existing text pool prefix preserved: `{result.ExistingTextPoolPrefixPreserved}`
- reread count ok: `{result.RereadCountOk}`
- reread new text ok: `{result.RereadNewTextOk}`
- preserve-write after grow: `{result.PreserveWriteOk}`

Manual RT2 steps:

1. Back up the live/extracted `{folder}` target.
2. Put the staged `{fileName}` where the loose-file loader will read `{folder}`.
3. Patch a disposable monster AI to perform `0x{result.Operand:X4}` on turn.
4. Start the encounter and confirm the move name/effect appears.
5. Record PASS/FAIL, then restore the backup immediately.

Do not promote this as runtime-proved until the encounter casts `0x{result.Operand:X4}` in-game.
";
        }

        static string Sha256Hex(byte[] bytes)
        {
            byte[] hash = SHA256.HashData(bytes);
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }
    }
}
