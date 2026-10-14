using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>Native step or velocity integration, not polynomial growth from repeated impulses.</summary>
public enum MagicU1Model { DoubleLayer, SingleLayer }

public sealed class MagicU1Keyframe
{
    public uint Tick { get; }
    public int RecordOffset { get; }
    public byte[] Window { get; }
    public MagicU1Keyframe(uint tick, int recordOffset, byte[] window)
    {
        Tick = tick;
        RecordOffset = recordOffset;
        Window = (byte[])(window ?? throw new ArgumentNullException(nameof(window))).Clone();
    }
}

/// <summary>One native instruction and its local keyframes. Native parameter offsets identify shared state.</summary>
public sealed class MagicU1SlotInput
{
    public string Opcode { get; }
    public int RecordOffset => Keyframes.Count == 0 ? -1 : Keyframes[0].RecordOffset;
    public byte[] Window => Keyframes.Count == 0 ? Array.Empty<byte>() : Keyframes[0].Window;
    public IReadOnlyList<MagicU1Keyframe> Keyframes { get; }
    public int ProgramOffset { get; }
    public int? DestinationOffset { get; }
    public int? SourceOffset { get; }

    public MagicU1SlotInput(string opcode, int recordOffset, byte[] window)
        : this(opcode, new[] { new MagicU1Keyframe(0, recordOffset, window) }) { }

    public MagicU1SlotInput(string opcode, IReadOnlyList<MagicU1Keyframe> keyframes,
        int programOffset = 0, int? destinationOffset = null, int? sourceOffset = null)
    {
        Opcode = opcode;
        Keyframes = keyframes.Select(k => new MagicU1Keyframe(k.Tick, k.RecordOffset, k.Window)).ToArray();
        ProgramOffset = programOffset;
        DestinationOffset = destinationOffset;
        SourceOffset = sourceOffset;
    }
}

/// <summary>Channel deltas from zero initial state. Angles are raw native integers, not assumed degrees.</summary>
public readonly record struct MagicU1FrameState(double ScaleX, double ScaleY, double ScaleZ,
    double PosX, double AngleYRaw, double PosY = 0, double PosZ = 0, double AngleXRaw = 0, double AngleZRaw = 0)
{
    /// <summary>Compatibility alias; this value has native units, not an established degree conversion.</summary>
    public double AngleYDeg => AngleYRaw;
    public override string ToString() => $"Sx={ScaleX:F2} Sy={ScaleY:F2} Sz={ScaleZ:F2} Px={PosX:F2} AngY(raw)={AngleYRaw:F2}";
}

public sealed class MagicU1SimulationResult
{
    public IReadOnlyList<MagicU1FrameState> Original { get; }
    public IReadOnlyList<MagicU1FrameState> Mutated { get; }
    public double FinalRatioX { get; }
    public int SlotCount { get; }
    public MagicU1SimulationResult(IReadOnlyList<MagicU1FrameState> original,
        IReadOnlyList<MagicU1FrameState> mutated, int slotCount)
    {
        Original = original;
        Mutated = mutated;
        SlotCount = slotCount;
        double baseline = original.Count > 0 ? original[^1].ScaleX : 0;
        FinalRatioX = Math.Abs(baseline) < 1e-9 ? double.NaN : mutated[^1].ScaleX / baseline;
    }
}

/// <summary>
/// Local-frame U1 diagnostics. Honors native slot order, parameter aliases and keyframe impulses.
/// Zero initial channels; no program spawning/lifetime/loop-reset, parent transforms, other handlers
/// or rendering. This is not a reconstruction of an entire effect's runtime state.
/// </summary>
public static class MagicU1Simulator
{
    private const int WindowStart = 16;
    private static readonly HashSet<string> Integrators = new(StringComparer.Ordinal)
    {
        "pppMove", "pppAccele", "pppSclMove", "pppSclAccele", "pppAngMove", "pppAngAccele",
        "pppMoveLoop", "pppSclMoveLoop", "pppAngMoveLoop",
    };
    private static readonly HashSet<string> Steps = new(StringComparer.Ordinal)
    {
        "pppPoint", "pppScale", "pppAngle", "pppPointLoop", "pppScaleLoop", "pppAngleLoop", "pppKeDrct",
    };
    public static MagicU1Model? GetModel(string opcode) => opcode != null && Integrators.Contains(opcode)
        ? MagicU1Model.DoubleLayer : opcode != null && Steps.Contains(opcode) ? MagicU1Model.SingleLayer : null;
    public static bool IsU1(string opcode) => GetModel(opcode) != null;
    public static bool IsAngle(string opcode) => opcode?.StartsWith("pppAng", StringComparison.Ordinal) == true;
    public static int AxisOf(string opcode) => IsAngle(opcode) ? 1 : 0;

    public static MagicU1SimulationResult Run(IReadOnlyList<MagicU1SlotInput> slots, int frames = 60,
        int? mutateRecordOffset = null, double mutateScale = 1, bool wrapAngles = false)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (frames is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(frames));
        if (!double.IsFinite(mutateScale)) throw new ArgumentOutOfRangeException(nameof(mutateScale));
        var selected = slots.Where(s => IsU1(s.Opcode) && s.Keyframes.Count != 0).ToArray();
        var original = Simulate(selected, frames, null, 1, wrapAngles);
        var mutated = mutateRecordOffset == null ? original : Simulate(selected, frames, mutateRecordOffset, mutateScale, wrapAngles);
        return new MagicU1SimulationResult(original, mutated, selected.Length);
    }

    private readonly record struct Node(int Program, long Offset);
    private sealed record Instruction(MagicU1SlotInput Input, Node Destination, Node Source);

    private static MagicU1FrameState[] Simulate(MagicU1SlotInput[] slots, int frames,
        int? mutation, double factor, bool wrapAngles)
    {
        var state = new Dictionary<Node, uint>();
        var positions = new HashSet<Node>();
        var scales = new HashSet<Node>();
        var angles = new HashSet<Node>();
        var instructions = new List<Instruction>();
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            Node NodeFor(int? offset, int local) => new(slot.ProgramOffset,
                offset is >= 0 ? offset.Value : long.MinValue + i * 32L + local * 16);
            var destination = NodeFor(slot.DestinationOffset, 0);
            var source = NodeFor(slot.SourceOffset, 1);
            instructions.Add(new Instruction(slot, destination, source));
            if (slot.Opcode is "pppPoint" or "pppPointLoop" or "pppMove" or "pppMoveLoop" or "pppKeDrct")
                positions.Add(destination);
            else if (slot.Opcode is "pppScale" or "pppScaleLoop" or "pppSclMove" or "pppSclMoveLoop")
                scales.Add(destination);
            else if (slot.Opcode is "pppAngle" or "pppAngleLoop" or "pppAngMove" or "pppAngMoveLoop")
                angles.Add(destination);
        }
        Node Component(Node node, int component) => node with { Offset = node.Offset + component * 4 };
        uint Get(Node node, int component) => state.GetValueOrDefault(Component(node, component));
        void Set(Node node, int component, uint value) => state[Component(node, component)] = value;
        var cursor = new int[instructions.Count];
        var result = new MagicU1FrameState[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            uint tick = (uint)frame * 4096;
            for (int index = 0; index < instructions.Count; index++)
            {
                var instruction = instructions[index];
                var input = instruction.Input;
                var keys = input.Keyframes;
                // The native cursor advances on the next key's exact Q12 timestamp.
                if (cursor[index] + 1 < keys.Count && keys[cursor[index] + 1].Tick == tick) cursor[index]++;
                var key = keys[cursor[index]];
                bool impulse = key.Tick == tick;
                bool integrate = Integrators.Contains(input.Opcode);
                bool integer = IsAngle(input.Opcode);
                int components = input.Opcode.EndsWith("Loop", StringComparison.Ordinal) || input.Opcode == "pppKeDrct" ? 3 : 4;
                if (impulse)
                {
                    for (int component = 0; component < components; component++)
                    {
                        uint raw = key.Window.Length >= component * 4 + 4 ? BitConverter.ToUInt32(key.Window, component * 4) : 0;
                        if (key.RecordOffset == mutation) raw = Scale(raw, factor, integer);
                        Node target = integrate ? instruction.Source : instruction.Destination;
                        Set(target, component, input.Opcode == "pppKeDrct" ? raw : Add(Get(target, component), raw, integer));
                    }
                }
                if (integrate)
                    for (int component = 0; component < components; component++)
                        Set(instruction.Destination, component, Add(Get(instruction.Destination, component), Get(instruction.Source, component), integer));
            }
            double Read(IEnumerable<Node> nodes, int component, bool integer = false) =>
                nodes.Sum(n => integer ? (double)unchecked((int)Get(n, component)) : BitConverter.Int32BitsToSingle(unchecked((int)Get(n, component))));
            double Angle(int component) { double value = Read(angles, component, true); return wrapAngles ? WrapDegrees(value) : value; }
            result[frame] = new MagicU1FrameState(Read(scales, 0), Read(scales, 1), Read(scales, 2),
                Read(positions, 0), Angle(1), Read(positions, 1), Read(positions, 2), Angle(0), Angle(2));
        }
        return result;
    }

    private static uint Add(uint left, uint right, bool integer) => integer ? unchecked(left + right)
        : unchecked((uint)BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle((int)left) + BitConverter.Int32BitsToSingle((int)right)));

    private static uint Scale(uint raw, double factor, bool integer)
    {
        if (!integer) return unchecked((uint)BitConverter.SingleToInt32Bits((float)(BitConverter.Int32BitsToSingle((int)raw) * factor)));
        double value = Math.Truncate(unchecked((int)raw) * factor) % 4294967296.0;
        return unchecked((uint)(long)value);
    }

    /// <summary>Explicit legacy numeric projection only; native angle units are not assumed to be degrees.</summary>
    public static double WrapDegrees(double degrees) => ((degrees % 360) + 360) % 360;

    public static IReadOnlyList<MagicU1SlotInput> CollectU1Slots(MagicDllFile file)
    {
        var result = new List<MagicU1SlotInput>();
        if (file == null) return result;
        foreach (var root in file.Roots)
        foreach (var program in root.Programs)
        foreach (var slot in program.Slots)
        {
            if (slot.OpcodeName is not string opcode || !IsU1(opcode) || slot.Keyframes.Count == 0) continue;
            int parameterCount = Integrators.Contains(opcode) ? 2 : 1;
            long at = (long)program.SectionAbs + slot.SecondaryCallbackRel;
            if (at < 0 || at > file.Data.Length - parameterCount * 4) continue;
            var keys = slot.Keyframes.Where(k => k.Record.Length >= WindowStart + 16)
                .Select(k => new MagicU1Keyframe(k.Tick, k.RecordOffset, k.Record.AsSpan(WindowStart, 16).ToArray())).ToArray();
            if (keys.Length != slot.Keyframes.Count) continue;
            int destination = BitConverter.ToInt32(file.Data, (int)at);
            int? source = parameterCount == 2 ? BitConverter.ToInt32(file.Data, (int)at + 4) : null;
            result.Add(new MagicU1SlotInput(opcode, keys, program.ProgramAbs, destination, source));
        }
        return result;
    }
}
