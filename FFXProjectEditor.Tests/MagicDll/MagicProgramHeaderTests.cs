using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Validates serialized Q12 timing against the actual native/Noclip consumers.
    /// The former test interpreted timing values as pointers into unrelated bytes.
    /// </summary>
    public class MagicProgramHeaderTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;

        [Fact]
        public void ProgramHeader_0021_StartLifetimeAndSeqId()
        {
            var parser = new MagicDllParser();
            MagicDllFile dll = parser.Parse(Path.Combine(CorpusDir, "magic_0021.dll"));

            var programs = dll.Roots.SelectMany(r => r.Programs).ToList();
            Assert.True(programs.Count >= 15, $"esperado >= 15 programs, veio {programs.Count}");

            // Raw values retained for binary compatibility; they mean 26 and 20 frames.
            MagicProgram p0 = programs[0];
            Assert.Equal(0x254u, p0.Key);
            Assert.Equal(0x1A000, p0.Curve1Rel);
            Assert.Equal(0x14000, p0.Curve2Rel);
            Assert.Equal(26.0,p0.StartFrame);
            Assert.Equal(20.0,p0.LifetimeFrames);
            Assert.Equal(0, p0.SeqId);

            Assert.All(programs.Take(6), p => Assert.Equal(p.SlotCount,p.Flags36));

            // SeqId sequencial (0..N-1) nos primeiros.
            for (int i = 0; i < 6; i++)
                Assert.Equal(i, programs[i].SeqId);
        }

        [Fact]
        public void ProgramHeader_TimingMatchesOwnHeaderAndLeavesLoopSentinelsIntact()
        {
            var parser = new MagicDllParser();
            MagicDllFile dll = parser.Parse(Path.Combine(CorpusDir, "magic_0021.dll"));
            MagicProgram p0 = dll.Roots[0].Programs[0];

            Assert.Equal(BitConverter.ToUInt32(dll.Data,p0.ProgramAbs+16),p0.StartTick);
            Assert.Equal(BitConverter.ToUInt32(dll.Data,p0.ProgramAbs+20),p0.LifetimeTick);
            Assert.Equal(0x70000000u,BitConverter.ToUInt32(dll.Data,p0.ProgramAbs+24));
            Assert.Equal(0x70000000u,BitConverter.ToUInt32(dll.Data,p0.ProgramAbs+28));
            Assert.Equal(dll.FileBytes,dll.Serialize());
        }
    }
}
