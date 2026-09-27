using System;
using System.IO;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Adds payloads and visual selectors only. Native-action sequencing, target
    // reuse, area assignment and SIN gates still belong to the AI authoring lane.
    internal static class MonMagic2CalmCavernSinPackRt0
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
                original.Header.RealEntryCount != MonsterMagicGrowWriter.CalmCavernSinFirstCommandId)
            {
                Console.Error.WriteLine("Calm/Cavern SIN pack expects exactly 304 existing 0x5C monmagic2 rows.");
                return 2;
            }

            foreach (MonsterMagicGrowWriter.CalmCavernSinSkillSpec spec in MonsterMagicGrowWriter.CalmCavernSinSkills)
            {
                MonsterMagicGrowResult result = MonsterMagicGrowWriter.AppendCalmCavernSinSkill(current, spec.CommandId);
                if (!result.Pass || result.NewId != spec.CommandId || result.Operand != 0x6000 + spec.CommandId ||
                    !string.Equals(result.Name, spec.Name, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Calm/Cavern SIN payload failed: {spec.Name} / row {spec.CommandId}.");
                    return 1;
                }
                current = result.GrownBytes;
                Console.WriteLine($"{spec.UniId} {spec.Name}: row {result.NewId}, operand 0x{result.Operand:X4}");
            }

            var finalRows = Ability_Command.ReadList(current, hasExtraInfo: false);
            if (finalRows.Count != MonsterMagicGrowWriter.CalmCavernSinLastCommandId + 1)
            {
                Console.Error.WriteLine("Calm/Cavern SIN final row count is wrong.");
                return 1;
            }
            foreach (MonsterMagicGrowWriter.CalmCavernSinSkillSpec spec in MonsterMagicGrowWriter.CalmCavernSinSkills)
            {
                Ability_Command row = finalRows[spec.CommandId];
                string name = FfxEncoding.DecodeScript(row.NameScriptBytes)
                    .GetString(FfxEncoding.UsDecoder, withControlCodes: true);
                if (!string.Equals(name, spec.Name, StringComparison.Ordinal) ||
                    row.Anim1Id != spec.Anim1Id || row.Anim2Id != spec.Anim2Id)
                {
                    Console.Error.WriteLine($"Calm/Cavern SIN readback mismatch at row {spec.CommandId}.");
                    return 1;
                }
            }

            Directory.CreateDirectory(outputDir);
            string output = Path.Combine(outputDir, Path.GetFileName(inputPath));
            File.WriteAllBytes(output, current);
            Console.WriteLine($"PASS: {output}, 304 -> {finalRows.Count} rows; old rows and text pool prefix preserved.");
            return 0;
        }
    }
}
