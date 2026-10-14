using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.Tools
{
    internal static class AiCommandMetadataRt0
    {
        public static int Run()
        {
            Console.WriteLine("=== AI command metadata catalog RT0 ===");

            TryBootstrapProjectForRt0();
            KernelMonsterMagicLiveSync.SyncProject();

            IReadOnlyList<AiCommandMetadataEntry> entries;
            try
            {
                entries = AiCommandMetadataCatalog.Entries;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: catalog load threw {ex.GetType().Name}: {ex.Message}");
                return 1;
            }

            int liveMonMagic2Rows = entries.Count(e =>
                e.Category == AiCommandMetadataCategory.MonsterMagic2
                && e.ProvenanceLabels.Contains("live-kernel-sync", StringComparison.Ordinal));
            int expectedMonMagic2Rows = 247 + liveMonMagic2Rows;
            int expectedTotalRows = 979 + liveMonMagic2Rows;
            int expectedSelectorRows = 867 + liveMonMagic2Rows;

            int fail = 0;
            fail += Expect("total rows", expectedTotalRows, entries.Count);
            fail += Expect("distinct operands", expectedTotalRows, entries.Select(e => e.Operand).Distinct().Count());

            fail += ExpectCategory(entries, AiCommandMetadataCategory.Item, 112);
            fail += ExpectCategory(entries, AiCommandMetadataCategory.Character, 320);
            fail += ExpectCategory(entries, AiCommandMetadataCategory.MonsterMagic1, 300);
            fail += ExpectCategory(entries, AiCommandMetadataCategory.MonsterMagic2, expectedMonMagic2Rows);

            int provedAiOperands = entries.Count(e => e.IsKnownAiPerformOperandCategory);
            int metadataOnly = entries.Count(e => !e.IsKnownAiPerformOperandCategory);
            fail += Expect("proved AI perform operands", 867 + liveMonMagic2Rows, provedAiOperands);
            fail += Expect("metadata-only item rows", 112, metadataOnly);

            fail += ExpectEntry(0x3000, AiCommandMetadataCategory.Character, "Attack", shouldBeAiOperand: true);
            fail += ExpectEntry(0x4000, AiCommandMetadataCategory.MonsterMagic1, "Attack", shouldBeAiOperand: true);
            fail += ExpectEntry(0x60AB, AiCommandMetadataCategory.MonsterMagic2, "Multi-Fira", shouldBeAiOperand: true);
            if (liveMonMagic2Rows >= 1)
                fail += ExpectEntry(0x60F7, AiCommandMetadataCategory.MonsterMagic2, "Prism Flare", shouldBeAiOperand: true);
            if (liveMonMagic2Rows >= 2)
                fail += ExpectEntry(0x60F8, AiCommandMetadataCategory.MonsterMagic2, "ThundaFira", shouldBeAiOperand: true);
            if (liveMonMagic2Rows >= 3)
                fail += ExpectEntry(0x60F9, AiCommandMetadataCategory.MonsterMagic2, "Flan Flood", shouldBeAiOperand: true);
            fail += ExpectEntry(0x2007, AiCommandMetadataCategory.Item, "Mega Phoenix", shouldBeAiOperand: false);

            int selectorRows = AiCommandId.AllOptions().Count;
            fail += Expect("legacy AI selector options", expectedSelectorRows, selectorRows);
            if (liveMonMagic2Rows >= 1)
                fail += ExpectOption(0x60F7, "Prism Flare");
            if (liveMonMagic2Rows >= 2)
                fail += ExpectOption(0x60F8, "ThundaFira");
            if (liveMonMagic2Rows >= 3)
                fail += ExpectOption(0x60F9, "Flan Flood");

            Console.WriteLine(fail == 0
                ? "VERDICT: PASS - generated metadata loads, counts match parser output, and item rows stay read-only."
                : $"VERDICT: FAIL - {fail} assertion(s) failed.");
            return fail == 0 ? 0 : 1;
        }

        static int ExpectCategory(IReadOnlyList<AiCommandMetadataEntry> entries, AiCommandMetadataCategory category, int expected)
        {
            int actual = entries.Count(e => e.Category == category);
            return Expect($"{category} rows", expected, actual);
        }

        static int ExpectEntry(ushort operand, AiCommandMetadataCategory category, string name, bool shouldBeAiOperand)
        {
            if (!AiCommandMetadataCatalog.TryGet(operand, out AiCommandMetadataEntry? entry))
            {
                Console.WriteLine($"  0x{operand:X4}: FAIL missing");
                return 1;
            }

            bool ok = entry.Category == category
                && entry.DisplayName == name
                && entry.IsKnownAiPerformOperandCategory == shouldBeAiOperand;
            Console.WriteLine(ok
                ? $"  0x{operand:X4}: PASS {entry.Category} {entry.DisplayName} aiOperand={entry.IsKnownAiPerformOperandCategory}"
                : $"  0x{operand:X4}: FAIL {entry.Category} {entry.DisplayName} aiOperand={entry.IsKnownAiPerformOperandCategory}");
            return ok ? 0 : 1;
        }

        static int ExpectOption(ushort operand, string name)
        {
            AiCommandOption? option = AiCommandId.OptionFor(operand);
            bool ok = option != null && option.Name == name;
            Console.WriteLine(ok
                ? $"  selector {"0x" + operand.ToString("X4"),-24}: PASS {option!.Name}"
                : $"  selector {"0x" + operand.ToString("X4"),-24}: FAIL {(option == null ? "missing" : option.Name)}");
            return ok ? 0 : 1;
        }

        static int Expect(string label, int expected, int actual)
        {
            bool ok = expected == actual;
            Console.WriteLine(ok
                ? $"  {label,-30}: PASS {actual}"
                : $"  {label,-30}: FAIL expected {expected}, got {actual}");
            return ok ? 0 : 1;
        }

        static void TryBootstrapProjectForRt0()
        {
            if (Project_Service.Instance.IsProjectLoaded)
                return;

            string? path = Environment.GetEnvironmentVariable("FFX_MASTER");
            if (string.IsNullOrWhiteSpace(path) || !Project_Service.IsPathValid(path))
            {
                path = @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master";
                if (!Project_Service.IsPathValid(path))
                    path = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
                if (!Project_Service.IsPathValid(path))
                    path = null;
            }

            if (path == null)
            {
                Console.WriteLine("  project bootstrap           : SKIP (no valid master folder; live monmagic sync not exercised)");
                return;
            }

            Project_Service.Instance.LoadProject(path);
            Console.WriteLine($"  project bootstrap           : PASS {path}");
        }
    }
}
