using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Gera os targets U1 do efeito-clone (magic_0098 = Death): slots das famílias
    /// T4-ready (SclMove/Scale/AngAccele/Angle) com RecordOffset e SHA — prontos para
    /// o runbook T4 (PPP_C3_CLONE_T4_RUNBOOK_20260802.md). Relatório em stdout.
    /// </summary>
    public class MagicDllCloneTargetsTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;

        [Fact]
        public void CloneTargets_0098_ReportsU1Slots()
        {
            string? fpDir = TestDataPaths.DatOvRoot;
            var parser = System.IO.Directory.Exists(fpDir)
                ? new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.Load(), FpDirectoryPath = fpDir })
                : new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.Load() });

            MagicDllFile dll = parser.Parse(Path.Combine(CorpusDir, "magic_0098.dll"));
            string[] families = { "pppSclMove", "pppScale", "pppAngAccele", "pppAngle" };

            var targets = dll.Roots
                .SelectMany(r => r.Programs)
                .SelectMany(p => p.Slots)
                .Where(s => s.OpcodeName != null && families.Contains(s.OpcodeName))
                .OrderBy(s => s.RecordOffset)
                .ToList();

            Console.WriteLine($"Targets U1 do 0098 (clone): {targets.Count}");
            foreach (var s in targets.Take(20))
            {
                Console.WriteLine(
                    $"  {s.OpcodeName,-14} record=0x{s.RecordOffset:X} handler={s.HandlerTableIndex,3} slot=0x{s.SlotAbs:X} sha={s.RecordSha256[..16]}…");
            }

            Assert.True(targets.Count >= 50, $"esperado >= 50 slots U1 no 0098, veio {targets.Count}");
        }
    }
}
