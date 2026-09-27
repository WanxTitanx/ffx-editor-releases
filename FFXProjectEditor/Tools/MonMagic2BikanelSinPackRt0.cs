using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Creates payload rows only. UNI-010 is a hook hypothesis; none of these rows
    // installs a SIN gate or changes monster AI.
    internal static class MonMagic2BikanelSinPackRt0
    {
        public static int Run(string inputPath, string outputDir)
        {
            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Input monmagic2.bin not found: {inputPath}");
                return 2;
            }

            byte[] current = File.ReadAllBytes(inputPath);
            EntryListFile original = EntryListFile.Unpack(current);
            if (original.Header.EntrySize != MonsterMagicGrowWriter.MonMagicEntrySize ||
                original.Header.RealEntryCount != MonsterMagicGrowWriter.SandMantleCommandId)
            {
                Console.Error.WriteLine("Bikanel SIN pack expects exactly 298 existing 0x5C monmagic2 rows.");
                return 2;
            }

            (string Name, int Id, int DonorId, Func<byte[], bool, int, MonsterMagicGrowResult> Append)[] steps =
            {
                ("Sand Mantle", MonsterMagicGrowWriter.SandMantleCommandId, 274, MonsterMagicGrowWriter.AppendSandMantle),
                ("Sin Siphon", MonsterMagicGrowWriter.SinSiphonCommandId, 259, MonsterMagicGrowWriter.AppendSinSiphon),
                ("Sandstorm", MonsterMagicGrowWriter.SandstormCommandId, 110, MonsterMagicGrowWriter.AppendSandstorm),
                ("Dust Ward", MonsterMagicGrowWriter.DustWardCommandId, 274, MonsterMagicGrowWriter.AppendDustWard),
                ("Hush Wave", MonsterMagicGrowWriter.HushWaveCommandId, 110, MonsterMagicGrowWriter.AppendHushWave),
                ("Silica Shards", MonsterMagicGrowWriter.SilicaShardsCommandId, 269, MonsterMagicGrowWriter.AppendSilicaShards),
            };

            foreach (var step in steps)
            {
                MonsterMagicGrowResult result = step.Append(current, true, step.DonorId);
                if (!result.Pass || result.NewId != step.Id || result.Operand != 0x6000 + step.Id ||
                    !string.Equals(result.Name, step.Name, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Bikanel SIN payload failed: {step.Name} / row {step.Id}.");
                    return 1;
                }
                current = result.GrownBytes;
                Console.WriteLine($"{step.Name}: row {result.NewId}, operand 0x{result.Operand:X4}");
            }

            var finalRows = Ability_Command.ReadList(current, hasExtraInfo: false);
            if (finalRows.Count != 304 ||
                steps.Where((step, index) => !string.Equals(
                    FfxEncoding.DecodeScript(finalRows[298 + index].NameScriptBytes)
                        .GetString(FfxEncoding.UsDecoder, withControlCodes: true),
                    step.Name, StringComparison.Ordinal)).Any())
            {
                Console.Error.WriteLine("Bikanel SIN pack readback failed.");
                return 1;
            }

            Directory.CreateDirectory(outputDir);
            string output = Path.Combine(outputDir, Path.GetFileName(inputPath));
            File.WriteAllBytes(output, current);
            Console.WriteLine($"PASS: {output}, 298 -> 304 rows; existing rows and text pool prefix preserved.");
            return 0;
        }
    }
}
