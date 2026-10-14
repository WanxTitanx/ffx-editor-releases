using System;

namespace FFXProjectEditor.FfxLib.MagicDll;

public enum MagicTimingKind
{
    SectionDuration,
    ProgramStart,
    ProgramDuration,
    LoopStart,
    LoopEnd,
    KeyframeTime,
}

/// <summary>A timing operand issued by one captured document, independent of payload schemas.</summary>
public sealed record MagicTimingField
{
    public MagicTimingKind Kind { get; }
    public int DataOffset { get; }
    public int OwnerOffset { get; }
    public uint OriginalTick { get; }
    internal Guid DocumentId { get; }
    public bool IsEditable => OriginalTick < 0x70000000u && OriginalTick % 4096 == 0;
    public double? Frames => OriginalTick < 0x70000000u ? OriginalTick / 4096.0 : null;

    internal MagicTimingField(MagicTimingKind kind, int dataOffset, int ownerOffset,
        uint tick, Guid documentId)
    {
        Kind = kind;
        DataOffset = dataOffset;
        OwnerOffset = ownerOffset;
        OriginalTick = tick;
        DocumentId = documentId;
    }
}
