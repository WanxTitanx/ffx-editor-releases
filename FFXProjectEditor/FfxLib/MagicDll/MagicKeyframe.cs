using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>A native slot record: Q12 timestamp followed by that handler's operands.</summary>
public sealed class MagicKeyframe
{
    private sealed class ReadContext
    {
        public readonly Dictionary<(int Start, ushort Stride, ushort Count), IReadOnlyList<MagicKeyframe>> Sequences = new();
        public long Bytes;
        public int Frames;
    }
    private static readonly ConditionalWeakTable<byte[], ReadContext> Contexts = new();
    public int Index { get; }
    public int RecordOffset { get; }
    public uint Tick { get; }
    public double? Frame => Tick < 0x70000000u ? Tick / 4096.0 : null;
    public byte[] Record { get; }
    public string RecordSha256 { get; }

    private MagicKeyframe(int index, int offset, byte[] record)
    {
        Index = index;
        RecordOffset = offset;
        Record = record;
        Tick = BitConverter.ToUInt32(record);
        RecordSha256 = MagicSlot.ComputeSha256Hex(record);
    }

    internal static IReadOnlyList<MagicKeyframe> Read(
        byte[] data, int start, ushort stride, ushort count, out string? error)
    {
        error = null;
        // Allocation limit, not a claimed native maximum. Larger or malformed sequences
        // remain inspectable through the existing first-record/raw surface.
        if (stride < 4 || stride % 4 != 0 || count == 0 || count > 4096 || start < 0 ||
            (long)start + (long)stride * count + 4 > data.Length)
        {
            error = "Invalid or oversized PPP keyframe range.";
            return Array.Empty<MagicKeyframe>();
        }
        int end = checked(start + stride * count);
        if (BitConverter.ToUInt32(data, end) != 0xFFFFF000u)
        {
            error = "PPP keyframe terminator does not match the declared count.";
            return Array.Empty<MagicKeyframe>();
        }
        // Many programs alias the same native sequence. Copy/hash it once per input
        // snapshot, and bound the aggregate decode rather than only each slot's range.
        var context = Contexts.GetValue(data, _ => new ReadContext());
        lock (context)
        {
            var key = (start, stride, count);
            if (context.Sequences.TryGetValue(key, out var cached))
            {
                for (int index = 0; index < count; index++)
                    if (!data.AsSpan(start + stride * index, stride).SequenceEqual(cached[index].Record))
                    {
                        error = "PPP keyframe input snapshot changed.";
                        return Array.Empty<MagicKeyframe>();
                    }
                return cached;
            }
            long bytes = (long)stride * count;
            if (context.Bytes + bytes > Math.Min(64L * 1024 * 1024, 2L * data.Length) ||
                context.Frames + count > 262144)
            {
                error = "PPP keyframe decode budget exceeded.";
                return Array.Empty<MagicKeyframe>();
            }
            var frames = new MagicKeyframe[count];
            for (int index = 0; index < count; index++)
            {
                int at = start + stride * index;
                frames[index] = new MagicKeyframe(index, at, data.AsSpan(at, stride).ToArray());
            }
            context.Bytes += bytes;
            context.Frames += count;
            context.Sequences.Add(key, frames);
            return frames;
        }
    }
}
