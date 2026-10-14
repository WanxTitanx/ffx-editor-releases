using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.MagicDll;

public enum MagicPrefixOperandKind { Float, DrawResource }

/// <summary>An operand issued by the explicit native +4 contract. The generic payload
/// writer's R6 policy remains unchanged.</summary>
public sealed class MagicPrefixOperand
{
    public int RecordOffset { get; }
    public int DataOffset => RecordOffset + 4;
    public MagicPrefixOperandKind Kind { get; }
    public string Opcode { get; }
    public uint OriginalBits { get; }
    public bool IsEditable { get; }
    public IReadOnlyList<uint> ResourceChoices { get; }
    public string? Error { get; }
    internal MagicPrefixOperand(int record, MagicPrefixOperandKind kind, string opcode,
        uint bits, bool editable, IReadOnlyList<uint> choices, string? error)
    {
        RecordOffset = record;
        Kind = kind;
        Opcode = opcode;
        OriginalBits = bits;
        IsEditable = editable;
        ResourceChoices = choices;
        Error = error;
    }
}
