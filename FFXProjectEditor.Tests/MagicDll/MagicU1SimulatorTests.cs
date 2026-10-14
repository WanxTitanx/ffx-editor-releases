using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Testes do simulador matemático U1 (port C# de work/ppp_c2/simulate_u1_all.py,
    /// PPP_SIMULATOR_U1_20260802.md). Valida os modelos impulsos/integradores nativos, ângulos int32 e mutações em capturas reais.
    /// </summary>
    public class MagicU1SimulatorTests
    {
        private readonly ITestOutputHelper _output;

        public MagicU1SimulatorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string CorpusDir => TestDataPaths.MagicCorpus;

        private static byte[] Window(float a, float b, float c, float d)
        {
            byte[] w = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(a), 0, w, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(b), 0, w, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(c), 0, w, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(d), 0, w, 12, 4);
            return w;
        }

        private static byte[] WindowInt(int a, int b, int c, int d)
        {
            byte[] w = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(a), 0, w, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(b), 0, w, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(c), 0, w, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(d), 0, w, 12, 4);
            return w;
        }

        [Fact]
        public void GetModel_ClassifiesU1Families()
        {
            Assert.Equal(MagicU1Model.DoubleLayer, MagicU1Simulator.GetModel("pppSclMove"));
            Assert.Equal(MagicU1Model.DoubleLayer, MagicU1Simulator.GetModel("pppAngAccele"));
            Assert.Equal(MagicU1Model.DoubleLayer, MagicU1Simulator.GetModel("pppAccele"));
            Assert.Equal(MagicU1Model.SingleLayer, MagicU1Simulator.GetModel("pppScale"));
            Assert.Equal(MagicU1Model.SingleLayer, MagicU1Simulator.GetModel("pppAngle"));
            Assert.Equal(MagicU1Model.SingleLayer, MagicU1Simulator.GetModel("pppPoint"));
            Assert.Null(MagicU1Simulator.GetModel("pppDrawMdl"));
            Assert.Null(MagicU1Simulator.GetModel("pppColor"));
            Assert.True(MagicU1Simulator.IsU1("pppSclMove"));
            Assert.False(MagicU1Simulator.IsU1("pppKeTh"));

            // Loops provados na varredura Onda 6.10 (MoveLoop 0x75C1A0, SclMoveLoop 0x75C380).
            Assert.Equal(MagicU1Model.DoubleLayer, MagicU1Simulator.GetModel("pppMoveLoop"));
            Assert.Equal(MagicU1Model.DoubleLayer, MagicU1Simulator.GetModel("pppSclMoveLoop"));
            Assert.True(MagicU1Simulator.IsU1("pppMoveLoop"));

            // Loops da varredura Onda 6.16 (PointLoop 0x75C5F0, ScaleLoop 0x75D180).
            Assert.Equal(MagicU1Model.SingleLayer, MagicU1Simulator.GetModel("pppPointLoop"));
            Assert.Equal(MagicU1Model.SingleLayer, MagicU1Simulator.GetModel("pppScaleLoop"));
        }

        [Fact]
        public void Velocity_IntegratesAKeyframeImpulse()
        {
            // Native match adds velocity once at t=0; each frame then integrates that velocity.
            var slots = new[]
            {
                new MagicU1SlotInput("pppSclMove", 0x100, Window(1f, 0f, 0f, 0f)),
            };

            MagicU1SimulationResult r = MagicU1Simulator.Run(slots, frames: 3);

            Assert.Equal(1, r.SlotCount);
            Assert.Equal(3, r.Original.Count);
            Assert.Equal(1.0, r.Original[0].ScaleX, 3);
            Assert.Equal(2.0, r.Original[1].ScaleX, 3);
            Assert.Equal(3.0, r.Original[2].ScaleX, 3);
        }

        [Fact]
        public void Step_AppliesOnlyAtMatchingTime()
        {
            // Native Scale adds the delta only at its timestamp; it does not repeat every frame.
            var slots = new[]
            {
                new MagicU1SlotInput("pppScale", 0x200, Window(2f, 0f, 0f, 0f)),
            };

            MagicU1SimulationResult r = MagicU1Simulator.Run(slots, frames: 4);

            Assert.Equal(2.0, r.Original[0].ScaleX, 3);
            Assert.Equal(2.0, r.Original[1].ScaleX, 3);
            Assert.Equal(2.0, r.Original[2].ScaleX, 3);
            Assert.Equal(2.0, r.Original[3].ScaleX, 3);
        }

        [Fact]
        public void Angle_ReadsInt32_AndWraps360()
        {
            // 0xFFFFFFFB = -5 (int32) no EIXO Y (índice 1 — ângulos usam Y, a mira).
            // pppAngle: single-layer, wrap 360.
            var slots = new[]
            {
                new MagicU1SlotInput("pppAngle", 0x300, WindowInt(0, -5, 0, 0)),
            };

            MagicU1SimulationResult r = MagicU1Simulator.Run(slots, frames: 100, wrapAngles: true);

            // frame 0: -5 → wrap 355°; frame 71: -360 → 0°; frame 100: -500 → 220°.
            Assert.Equal(355.0, r.Original[0].AngleYDeg, 3);
            Assert.Equal(355.0, r.Original[71].AngleYDeg, 3);
            Assert.Equal(355.0, r.Original[99].AngleYDeg, 3);
        }

        [Fact]
        public void Angle_WithoutWrap_PreservesNativeImpulse()
        {
            var slots = new[]
            {
                new MagicU1SlotInput("pppAngle", 0x300, WindowInt(0, -5, 0, 0)),
            };

            MagicU1SimulationResult r = MagicU1Simulator.Run(slots, frames: 4, wrapAngles: false);

            Assert.Equal(-5.0, r.Original[0].AngleYDeg, 3);
            Assert.Equal(-5.0, r.Original[3].AngleYDeg, 3);
        }

        [Fact]
        public void Mutation_OneSlot_DilutesRatio()
        {
            // 4 slots SclMove delta=1; mutar 1 com ×2 → razão final < 2 (diluição 1/N).
            var slots = new[]
            {
                new MagicU1SlotInput("pppSclMove", 0x100, Window(1f, 0f, 0f, 0f)),
                new MagicU1SlotInput("pppSclMove", 0x200, Window(1f, 0f, 0f, 0f)),
                new MagicU1SlotInput("pppSclMove", 0x300, Window(1f, 0f, 0f, 0f)),
                new MagicU1SlotInput("pppSclMove", 0x400, Window(1f, 0f, 0f, 0f)),
            };

            MagicU1SimulationResult r = MagicU1Simulator.Run(slots, frames: 10, mutateRecordOffset: 0x200, mutateScale: 2.0);

            // Razão esperada: (4 + 0.25) / 4 = 1.25 (mutação ×2 num de 4 = +25%).
            Assert.Equal(1.25, r.FinalRatioX, 3);
            Assert.NotSame(r.Original, r.Mutated);
        }

        [Fact]
        public void KeDrct_SetsPositionInsteadOfAccumulating()
        {
            // FFX_Pmcom_MatchAndCopyPosition (0x75E520, Onda 6.12): copia 3x f32 p/ node — SET.
            var slots = new[]
            {
                new MagicU1SlotInput("pppKeDrct", 0x500, Window(3f, 4f, 5f, 0f)),
            };

            MagicU1SimulationResult r = MagicU1Simulator.Run(slots, frames: 3);

            // Posição SET (não acumula): frame 0..2 todos = 3.0.
            Assert.Equal(3.0, r.Original[0].PosX, 3);
            Assert.Equal(3.0, r.Original[1].PosX, 3);
            Assert.Equal(3.0, r.Original[2].PosX, 3);
        }

        [Fact]
        public void Run_RejectsInvalidFrames()
        {
            var slots = new[] { new MagicU1SlotInput("pppScale", 0x100, Window(1f, 0f, 0f, 0f)) };
            Assert.Throws<ArgumentOutOfRangeException>(() => MagicU1Simulator.Run(slots, frames: 0));
        }

        [Fact]
        public void Run_FiltersNonU1Slots()
        {
            var slots = new[]
            {
                new MagicU1SlotInput("pppDrawMdl", 0x100, Window(1f, 0f, 0f, 0f)),
                new MagicU1SlotInput("pppScale", 0x200, Window(1f, 0f, 0f, 0f)),
            };

            MagicU1SimulationResult r = MagicU1Simulator.Run(slots, frames: 2);

            Assert.Equal(1, r.SlotCount); // só pppScale conta
        }

        [Fact]
        public void WrapDegrees_NormalizesToRange()
        {
            Assert.Equal(0.0, MagicU1Simulator.WrapDegrees(360), 3);
            Assert.Equal(350.0, MagicU1Simulator.WrapDegrees(-10), 3);
            Assert.Equal(90.0, MagicU1Simulator.WrapDegrees(450), 3);
            Assert.Equal(0.0, MagicU1Simulator.WrapDegrees(0), 3);
        }

        [Fact]
        public void CollectU1Slots_FromCorpus0098_ReturnsExpectedMagnitude()
        {
            string path = Path.Combine(CorpusDir, "magic_0098.dll");
            Assert.True(File.Exists(path), $"Corpus ausente: {path}");

            var parser = new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.Load() });
            Assert.True(parser.TryParse(path, out MagicDllFile? file, out string error), error);

            var slots = MagicU1Simulator.CollectU1Slots(file!);
            Assert.NotEmpty(slots);
            Assert.All(slots, s => Assert.True(MagicU1Simulator.IsU1(s.Opcode)));
            Assert.All(slots, s => Assert.Equal(16, s.Window.Length));

            // 0098 (Death): contagem de slots U1 via parser C# — ordem de dezenas (doc ~71 do
            // walker Python simplificado; parser C# é a verdade, mas não crava número exato).
            Assert.True(slots.Count >= 50, $"esperado ~71 slots U1 no 0098, achou {slots.Count}");

            // A collected instruction retains every declared keyframe and the native parameter IDs.
            var sourceSlots = file!.Roots.SelectMany(root => root.Programs)
                .SelectMany(program => program.Slots).Where(slot => slot.OpcodeName != null && MagicU1Simulator.IsU1(slot.OpcodeName)).ToArray();
            Assert.Equal(sourceSlots.Sum(slot => slot.Keyframes.Count), slots.Sum(slot => slot.Keyframes.Count));
            Assert.All(slots, slot => Assert.NotNull(slot.DestinationOffset));

        }

        [Fact]
        public void CollectU1Slots_FromCorpus0021_ReturnsNonEmpty()
        {
            string path = Path.Combine(CorpusDir, "magic_0021.dll");
            Assert.True(File.Exists(path), $"Corpus ausente: {path}");

            var parser = new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.Load() });
            Assert.True(parser.TryParse(path, out MagicDllFile? file, out string error), error);

            var slots = MagicU1Simulator.CollectU1Slots(file!);
            Assert.NotEmpty(slots);
            Assert.True(slots.Count >= 30, $"Power Break deveria ter dezenas de slots U1, achou {slots.Count}");
        }

        [Fact]
        public void CapturedMutation_0098_MatchesDirectOperandScaling()
        {
            var parser = new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.LoadEmbedded() });
            string source = Path.Combine(CorpusDir, "magic_0098.dll");
            var file = parser.Parse(source);
            var slots = MagicU1Simulator.CollectU1Slots(file);
            var target = slots.First(slot => slot.Opcode == "pppSclMove").Keyframes[0];
            byte[] mutated = (byte[])file.FileBytes.Clone();
            for (int component = 0; component < 4; component++)
            {
                int at = file.DataSectionRawPtr + target.RecordOffset + 16 + component * 4;
                BitConverter.GetBytes(BitConverter.ToSingle(mutated, at) * 2f).CopyTo(mutated, at);
            }
            Assert.True(parser.TryParseSnapshot(mutated, source, out var changed, out var error), error);
            var expected = MagicU1Simulator.Run(slots, 60, target.RecordOffset, 2);
            var actual = MagicU1Simulator.Run(MagicU1Simulator.CollectU1Slots(changed!), 60);
            Assert.Equal(expected.Mutated.ToArray(), actual.Original.ToArray());
            Assert.Equal(file.FileBytes, File.ReadAllBytes(source));
            Assert.False(expected.Original.SequenceEqual(expected.Mutated));
        }

        [Fact]
        public void Fuzz_TruncatedAndRandomWindows_NeverThrows()
        {
            // Robustez (padrão da lane): janelas truncadas/aleatórias em qualquer combinação
            // de opcodes U1 e fatores não podem lançar — contrato nunca-lança do simulador.
            var rng = new Random(42);
            string[] u1Ops = { "pppSclMove", "pppSclAccele", "pppAccele", "pppMove", "pppAngAccele", "pppScale", "pppAngle", "pppPoint", "pppAngMoveLoop", "pppAngleLoop" };
            string[] nonU1Ops = { "pppDrawMdl", "pppKeTh", "pppColor", "pppMatrixXYZ", null! };

            for (int iter = 0; iter < 200; iter++)
            {
                var slots = new List<MagicU1SlotInput>();
                for (int n = 0; n < rng.Next(0, 8); n++)
                {
                    string op = rng.Next(3) == 0 ? nonU1Ops[rng.Next(nonU1Ops.Length)] : u1Ops[rng.Next(u1Ops.Length)];
                    int len = rng.Next(0, 24); // 0..23 bytes (truncado inclusive)
                    byte[] window = new byte[len];
                    rng.NextBytes(window);
                    slots.Add(new MagicU1SlotInput(op, rng.Next(0x1000), window));
                }

                int frames = rng.Next(1, 90);
                int? target = slots.Count > 0 && rng.Next(2) == 0 ? slots[rng.Next(slots.Count)].RecordOffset : null;
                double factor = rng.NextDouble() * 10.0;

                MagicU1SimulationResult r = MagicU1Simulator.Run(slots, frames, target, factor);
                Assert.Equal(frames, r.Original.Count);
                Assert.Equal(frames, r.Mutated.Count);
                Assert.True(r.SlotCount >= 0);
            }
        }
    }
}
