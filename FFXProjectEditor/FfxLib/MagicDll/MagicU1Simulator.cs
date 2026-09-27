using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Modelo de acumulação por frame de um handler U1 (PPP_SIMULATOR_U1_20260802.md §1).
    /// </summary>
    public enum MagicU1Model
    {
        /// <summary>layerB += delta; layerA += layerB (aceleração/segunda camada).</summary>
        DoubleLayer,

        /// <summary>layerA += delta (acumulação linear).</summary>
        SingleLayer,
    }

    /// <summary>
    /// Entrada de um slot U1 para o simulador: opcode + janela de 16 bytes
    /// (record+0x10..+0x1F — 4 valores f32, ou int32 para ângulos).
    /// </summary>
    public sealed class MagicU1SlotInput
    {
        public string Opcode { get; }

        /// <summary>Offset absoluto do record no .data (identidade da mutação).</summary>
        public int RecordOffset { get; }

        /// <summary>Janela runtime de 16B (record+0x10..+0x1F).</summary>
        public byte[] Window { get; }

        public MagicU1SlotInput(string opcode, int recordOffset, byte[] window)
        {
            Opcode = opcode;
            RecordOffset = recordOffset;
            Window = window;
        }
    }

    /// <summary>Estado agregado do efeito num frame (escala X/Y/Z, posição X, ângulo Y em graus).</summary>
    public readonly record struct MagicU1FrameState(
        double ScaleX, double ScaleY, double ScaleZ,
        double PosX, double AngleYDeg)
    {
        public override string ToString() =>
            $"Sx={ScaleX:F2} Sy={ScaleY:F2} Sz={ScaleZ:F2} Px={PosX:F2} AngY={AngleYDeg:F2}°";
    }

    /// <summary>
    /// Resultado da simulação U1: série antes × depois (overlay) + razão final da escala X.
    /// </summary>
    public sealed class MagicU1SimulationResult
    {
        public IReadOnlyList<MagicU1FrameState> Original { get; }
        public IReadOnlyList<MagicU1FrameState> Mutated { get; }

        /// <summary>Razão final (frame N-1) escala X mutada/original. NaN quando original é 0.</summary>
        public double FinalRatioX { get; }

        /// <summary>Quantidade de slots U1 considerados na simulação.</summary>
        public int SlotCount { get; }

        public MagicU1SimulationResult(
            IReadOnlyList<MagicU1FrameState> original,
            IReadOnlyList<MagicU1FrameState> mutated,
            int slotCount)
        {
            Original = original;
            Mutated = mutated;
            SlotCount = slotCount;
            double baseX = original.Count > 0 ? original[^1].ScaleX : 0;
            double mutX = mutated.Count > 0 ? mutated[^1].ScaleX : 0;
            FinalRatioX = Math.Abs(baseX) < 1e-9 ? double.NaN : mutX / baseX;
        }
    }


    /// <summary>
    /// Simulador matemático da família U1 (prova de conceito "T4 simulado" — valida o que a
    /// mutação faz matematicamente, NÃO o visual completo; simulação ≠ jogo).
    ///
    /// Port de work/ppp_c2/simulate_u1_all.py (2026-08-02, validado no magic_0098/Death):
    ///   - double-layer (SclMove/SclAccele/Accele/Move/AngAccele): layerB += delta; layerA += layerB
    ///   - single-layer (Scale/Angle/Point): layerA += delta
    ///   - loop (AngMoveLoop/AngleLoop): delta reaplicado (mesmo modelo single)
    ///   - eixo principal: X para escala/posição; Y para ângulos (mira — int32 graus)
    ///   - ângulos lidos como int32 (0xFFFFFFFB = -5, NÃO f32 NaN) e WRAPPED em 360°
    ///     (o runtime modula; o modelo linear superestima — usar só para comparar mutações).
    /// </summary>
    public static class MagicU1Simulator
    {
        private const int FramesDefault = 60;
        private const int WindowStart = 0x10; // record+0x10..+0x1F

        private static readonly HashSet<string> DoubleLayer = new(StringComparer.Ordinal)
        {
            "pppSclMove", "pppSclAccele", "pppAccele", "pppMove", "pppAngAccele",
        };

        private static readonly HashSet<string> SingleLayer = new(StringComparer.Ordinal)
        {
            "pppScale", "pppAngle", "pppPoint", "pppAngMoveLoop", "pppAngleLoop",
            "pppKeDrct", // FFX_Pmcom_MatchAndCopyPosition (0x75E520): copia 3x f32 p/ node — SET
        };

        /// <summary>
        /// Loops U1 provados na varredura (Onda 6.10: MoveLoop 0x75C1A0, SclMoveLoop 0x75C380;
        /// Onda 6.16: PointLoop 0x75C5F0, ScaleLoop 0x75D180):
        /// delta reaplicado por frame (mesmo modelo single com acúmulo em camada tripla).
        /// </summary>
        private static readonly HashSet<string> Loops = new(StringComparer.Ordinal)
        {
            "pppMoveLoop", "pppSclMoveLoop", "pppPointLoop", "pppScaleLoop",
        };

        /// <summary>Modelo da família (double/single) ou null quando a família não é U1.</summary>
        public static MagicU1Model? GetModel(string opcode)
        {
            if (DoubleLayer.Contains(opcode))
                return MagicU1Model.DoubleLayer;
            if (SingleLayer.Contains(opcode) || Loops.Contains(opcode))
                return MagicU1Model.SingleLayer;
            return null;
        }

        /// <summary>true quando a família é U1 (participa da simulação).</summary>
        public static bool IsU1(string opcode) => GetModel(opcode) is not null;

        /// <summary>Ângulos (pppAng*) são int32 graus; demais U1 são f32.</summary>
        private static bool IsAngle(string opcode) => opcode.StartsWith("pppAng", StringComparison.Ordinal);

        /// <summary>Eixo principal: Y (1) para ângulos; X (0) para escala/posição.</summary>
        private static int AxisOf(string opcode) => IsAngle(opcode) ? 1 : 0;

        /// <summary>
        /// Roda a simulação por frame (antes e depois da mutação) sobre todos os slots U1.
        /// <paramref name="mutateRecordOffset"/> = record a escalar (null = sem mutação);
        /// <paramref name="mutateScale"/> = fator aplicado aos 4 valores da janela do slot mutado.
        /// </summary>
        public static MagicU1SimulationResult Run(
            IReadOnlyList<MagicU1SlotInput> slots,
            int frames = FramesDefault,
            int? mutateRecordOffset = null,
            double mutateScale = 1.0,
            bool wrapAngles = true)
        {
            if (slots is null)
                throw new ArgumentNullException(nameof(slots));
            if (frames <= 0)
                throw new ArgumentOutOfRangeException(nameof(frames), "frames must be > 0");

            var u1 = new List<MagicU1SlotInput>(slots.Count);
            foreach (MagicU1SlotInput slot in slots)
            {
                if (GetModel(slot.Opcode) is null)
                    continue; // não-U1: fora do escopo do simulador
                u1.Add(slot);
            }

            MagicU1FrameState[] before = Simulate(u1, frames, mutateRecordOffset, mutateScale, wrapAngles);
            MagicU1FrameState[] after = mutateRecordOffset is null
                ? before
                : Simulate(u1, frames, mutateRecordOffset, mutateScale, wrapAngles, mutated: true);

            return new MagicU1SimulationResult(before, after, u1.Count);
        }

        private static MagicU1FrameState[] Simulate(
            IReadOnlyList<MagicU1SlotInput> slots,
            int frames,
            int? mutateRecordOffset,
            double mutateScale,
            bool wrapAngles,
            bool mutated = false)
        {
            var scale = new double[3];
            var pos = new double[3];
            var ang = new double[3];
            var layers = new Dictionary<(string Opcode, int Rec), (double B, double A)>();

            var outFrames = new MagicU1FrameState[frames];
            for (int f = 0; f < frames; f++)
            {
                foreach (MagicU1SlotInput slot in slots)
                {
                    MagicU1Model model = GetModel(slot.Opcode)!.Value;
                    double[] vals = ReadWindow(slot.Window, IsAngle(slot.Opcode));
                    double mult = mutated && slot.RecordOffset == mutateRecordOffset ? mutateScale : 1.0;
                    double[] d = { vals[0] * mult, vals[1] * mult, vals[2] * mult, vals[3] * mult };

                    var key = (slot.Opcode, slot.RecordOffset);
                    int axis = AxisOf(slot.Opcode);
                    double dv = d[axis];
                    double acc;
                    if (model == MagicU1Model.DoubleLayer)
                    {
                        (double b, double a) = layers.TryGetValue(key, out var st) ? st : (0.0, 0.0);
                        b += dv;
                        a += b;
                        layers[key] = (b, a);
                        acc = a;
                    }
                    else
                    {
                        acc = dv;
                    }

                    if (slot.Opcode is "pppSclMove" or "pppSclAccele" or "pppScale")
                    {
                        scale[0] += acc;
                        scale[1] += slot.Opcode == "pppSclMove" ? d[1] : acc;
                        scale[2] += d[2];
                    }
                    else if (slot.Opcode == "pppMove")
                    {
                        pos[0] += acc;
                        pos[1] += d[1];
                        pos[2] += d[2];
                    }
                    else if (slot.Opcode == "pppKeDrct")
                    {
                        // FFX_Pmcom_MatchAndCopyPosition: SET posição (copia 3x f32 p/ node).
                        pos[0] = d[0];
                        pos[1] = d[1];
                        pos[2] = d[2];
                    }
                    else if (IsAngle(slot.Opcode))
                    {
                        ang[axis] += acc;
                    }
                }

                double angleY = wrapAngles ? WrapDegrees(ang[1]) : ang[1];
                outFrames[f] = new MagicU1FrameState(scale[0], scale[1], scale[2], pos[0], angleY);
            }
            return outFrames;
        }

        /// <summary>Lê os 4 valores da janela (record+0x10..+0x1F); ângulos como int32.</summary>
        private static double[] ReadWindow(byte[] window, bool asInt)
        {
            var result = new double[4];
            for (int k = 0; k < 4; k++)
            {
                int off = 4 * k;
                if (off + 4 > window.Length)
                {
                    result[k] = 0;
                    continue;
                }
                result[k] = asInt
                    ? BitConverter.ToInt32(window, off)
                    : BitConverter.ToSingle(window, off);
            }
            return result;
        }

        /// <summary>Wrap de ângulo em [0, 360) — modular 360° como o runtime (fixupAngles).</summary>
        public static double WrapDegrees(double degrees)
        {
            double m = degrees % 360.0;
            return m < 0 ? m + 360.0 : m;
        }

        /// <summary>Extrai os slots U1 do documento parseado (janela record+0x10..+0x1F).</summary>
        public static IReadOnlyList<MagicU1SlotInput> CollectU1Slots(MagicDllFile file)
        {
            var result = new List<MagicU1SlotInput>();
            if (file is null)
                return result;

            foreach (MagicDllRoot root in file.Roots)
            foreach (MagicProgram program in root.Programs)
            foreach (MagicSlot slot in program.Slots)
            {
                if (slot.OpcodeName is null || !IsU1(slot.OpcodeName))
                    continue;
                if (slot.Record.Length < WindowStart + 16)
                    continue;

                var window = new byte[16];
                Array.Copy(slot.Record, WindowStart, window, 0, 16);
                result.Add(new MagicU1SlotInput(slot.OpcodeName, slot.RecordOffset, window));
            }
            return result;
        }
    }
}
