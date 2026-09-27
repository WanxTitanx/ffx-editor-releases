using System;
using System.IO;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Creates skill payloads and selectors only; no SIN AI/gate/ATEL bytes are emitted.
    internal static class MonMagic2GagazetSinPackRt0
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
                original.Header.RealEntryCount != MonsterMagicGrowWriter.GagazetSinFirstCommandId)
            {
                Console.Error.WriteLine("Gagazet SIN pack expects exactly 329 existing 0x5C monmagic2 rows.");
                return 2;
            }
            foreach (MonsterMagicGrowWriter.GagazetSinSkillSpec spec in MonsterMagicGrowWriter.GagazetSinSkills)
            {
                MonsterMagicGrowResult result = MonsterMagicGrowWriter.AppendGagazetSinSkill(current, spec.CommandId);
                if (!result.Pass || result.NewId != spec.CommandId || result.Operand != 0x6000 + spec.CommandId ||
                    !string.Equals(result.Name, spec.Name, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Gagazet SIN payload failed: {spec.Name} / row {spec.CommandId}.");
                    return 1;
                }
                current = result.GrownBytes;
                Console.WriteLine($"{spec.UniId} {spec.Name}: row {result.NewId}, operand 0x{result.Operand:X4}");
            }
            var rows = Ability_Command.ReadList(current, hasExtraInfo: false);
            if (rows.Count != MonsterMagicGrowWriter.GagazetSinLastCommandId + 1)
            {
                Console.Error.WriteLine("Gagazet SIN final row count is wrong.");
                return 1;
            }
            foreach (MonsterMagicGrowWriter.GagazetSinSkillSpec spec in MonsterMagicGrowWriter.GagazetSinSkills)
            {
                Ability_Command row = rows[spec.CommandId];
                string name = FfxEncoding.DecodeScript(row.NameScriptBytes)
                    .GetString(FfxEncoding.UsDecoder, withControlCodes: true);
                if (!string.Equals(name, spec.Name, StringComparison.Ordinal) ||
                    row.Anim1Id != spec.Anim1Id || row.Anim2Id != spec.Anim2Id)
                {
                    Console.Error.WriteLine($"Gagazet SIN readback mismatch at row {spec.CommandId}.");
                    return 1;
                }
            }
            Directory.CreateDirectory(outputDir);
            string output = Path.Combine(outputDir, Path.GetFileName(inputPath));
            File.WriteAllBytes(output, current);
            Console.WriteLine($"PASS: {output}, 329 -> {rows.Count} rows; old rows and text pool prefix preserved.");
            return 0;
        }
    }
}
