using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Valida os campos decodificados do header do program (RE 2026-08-02):
    /// +16/+20 = curvas de animação (rel. section), +36 = (flags<<16)|seq_id,
    /// +4 = key (índice ×32 na tabela de descritores do host).
    /// </summary>
    public class MagicProgramHeaderTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;

        [Fact]
        public void ProgramHeader_0021_CurvesAndSeqId()
        {
            var parser = new MagicDllParser();
            MagicDllFile dll = parser.Parse(Path.Combine(CorpusDir, "magic_0021.dll"));

            var programs = dll.Roots.SelectMany(r => r.Programs).ToList();
            Assert.True(programs.Count >= 15, $"esperado >= 15 programs, veio {programs.Count}");

            // Program 0: key=0x254, curva1=0x1A000, curva2=0x14000, seq=0 (RE offline).
            MagicProgram p0 = programs[0];
            Assert.Equal(0x254u, p0.Key);
            Assert.Equal(0x1A000, p0.Curve1Rel);
            Assert.Equal(0x14000, p0.Curve2Rel);
            Assert.Equal(0, p0.SeqId);

            // Flags36 = u16 alto do +36 (0x0A/0x0D/0x0B... — tipo do program; varia).
            // O 0x7000 constante é o +24/+28 (NÃO o +36).
            Assert.All(programs.Take(6), p => Assert.InRange(p.Flags36, (ushort)1, (ushort)0x20));

            // SeqId sequencial (0..N-1) nos primeiros.
            for (int i = 0; i < 6; i++)
                Assert.Equal(i, programs[i].SeqId);
        }

        [Fact]
        public void ProgramHeader_CurveData_IsU8Samples()
        {
            // As curvas do PC são samples u8 diretos por frame (RE 2026-08-02) — o
            // primeiro program tem curva2 em section+0x14000 = bytes crescentes.
            var parser = new MagicDllParser();
            MagicDllFile dll = parser.Parse(Path.Combine(CorpusDir, "magic_0021.dll"));
            MagicProgram p0 = dll.Roots[0].Programs[0];

            byte[] file = dll.FileBytes;
            int sectionAbs = p0.SectionAbs;
            int dataPtr = dll.DataSectionRawPtr;
            int curveAbs = dataPtr + sectionAbs + p0.Curve2Rel;

            // Os primeiros samples devem ser crescentes (06 07 07 08 09 0A...).
            byte[] samples = new byte[16];
            Array.Copy(file, curveAbs, samples, 0, 16);
            Assert.True(samples[1] >= samples[0], "curva deveria ser crescente (samples u8)");
            Assert.True(samples[5] >= samples[0], "curva deveria crescer ao longo dos frames");
        }
    }
}
