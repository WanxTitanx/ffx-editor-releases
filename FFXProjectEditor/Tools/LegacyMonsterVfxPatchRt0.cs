using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Changes only Anim1/Anim2 in #250–266. Flan Flood #249 and all gameplay,
    // names, row count and text pool are kept byte-identical.
    internal static class LegacyMonsterVfxPatchRt0
    {
        public static int Run(string inputPath, string outputDir)
        {
            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Missing monmagic2.bin: {inputPath}");
                return 2;
            }
            byte[] originalBytes = File.ReadAllBytes(inputPath);
            EntryListFile original = EntryListFile.Unpack(originalBytes);
            if (original.Header.EntrySize != MonsterMagicGrowWriter.MonMagicEntrySize ||
                original.Header.RealEntryCount is not (268 or 341))
            {
                Console.Error.WriteLine("Legacy VFX patch expects 268-row jppc or 341-row new_uspc monmagic2.");
                return 2;
            }
            var entries = Ability_Command.ReadList(originalBytes, hasExtraInfo: false);
            int rowSize = original.Header.EntrySize;
            int tableOffset = original.Header.EntryTableFileOffset;
            if (tableOffset < 0 || (long)tableOffset + original.FirstFile.Length > originalBytes.Length ||
                !original.FirstFile.AsSpan().SequenceEqual(originalBytes.AsSpan(tableOffset, original.FirstFile.Length)))
            {
                Console.Error.WriteLine("Entry table bounds or source bytes are inconsistent.");
                return 2;
            }
            byte[] result = (byte[])originalBytes.Clone();
            foreach (MonsterMagicGrowWriter.LegacyMonsterVfxSpec spec in MonsterMagicGrowWriter.LegacyMonsterVfxSkills)
            {
                string name = FfxEncoding.DecodeScript(entries[spec.Row].NameScriptBytes)
                    .GetString(FfxEncoding.UsDecoder, withControlCodes: true);
                if (!string.Equals(name, spec.Name, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Row {spec.Row} is '{name}', expected '{spec.Name}'.");
                    return 2;
                }
                int animOffset = checked(tableOffset + spec.Row * rowSize + 16);
                BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(animOffset, 2), spec.Anim1Id);
                BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(animOffset + 2, 2), spec.Anim2Id);
            }
            EntryListFile final = EntryListFile.Unpack(result);
            if (result.Length != originalBytes.Length ||
                final.Header.RealEntryCount != original.Header.RealEntryCount ||
                !original.SecondFile.SequenceEqual(final.SecondFile))
            {
                Console.Error.WriteLine("Legacy VFX patch changed table length, count or text pool.");
                return 1;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                ReadOnlySpan<byte> before = original.FirstFile.AsSpan(i * rowSize, rowSize);
                ReadOnlySpan<byte> after = final.FirstFile.AsSpan(i * rowSize, rowSize);
                bool valid = i is >= 250 and <= 266
                    ? before[..16].SequenceEqual(after[..16]) && before[20..].SequenceEqual(after[20..]) &&
                      !before.Slice(16, 4).SequenceEqual(after.Slice(16, 4))
                    : before.SequenceEqual(after);
                if (!valid)
                {
                    Console.Error.WriteLine($"Unexpected byte change at row {i}.");
                    return 1;
                }
            }
            var reread = Ability_Command.ReadList(result, hasExtraInfo: false);
            if (MonsterMagicGrowWriter.LegacyMonsterVfxSkills.Any(spec =>
                reread[spec.Row].Anim1Id != spec.Anim1Id || reread[spec.Row].Anim2Id != spec.Anim2Id))
            {
                Console.Error.WriteLine("Legacy VFX selectors failed readback.");
                return 1;
            }
            Directory.CreateDirectory(outputDir);
            string output = Path.Combine(outputDir, Path.GetFileName(inputPath));
            File.WriteAllBytes(output, result);
            Console.WriteLine($"PASS: {output}, {entries.Count} rows; only Anim1/Anim2 of #250–266 changed.");
            return 0;
        }
    }
}
