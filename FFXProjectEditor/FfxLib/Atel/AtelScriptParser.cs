using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Atel;

// ── AtelScriptParser (extraído do ZanarkandWorkshop AtelScriptDocument/AtelWorker, 2026-08-06)
// Parses the ATEL worker descriptors and their INT/FLOAT constant pools.
//
// WHY THIS MATTERS (Treasure Map front #1): the ATEL opcode PUSH_FLOAT_ARR (0x2F) reads a
// float from the CURRENT worker's pool - `FloatConstantBits[operand]` - and those floats are
// stored IN THE FILE (not runtime). This is what lets us recover the ~70+ chest positions
// that the old scanner dismissed as "runtime-only".
//
// ATEL static header (0x38 bytes) - validated against the field-event corpus (EV01 chunk 0):
//   +0x00 i32 codeLen           +0x08 i32 creatorOffset
//   +0x0C i32 scriptIdOffset    +0x30 i32 ScriptCodeOffset
//   +0x34 u16 WorkerCount       +0x36 u16 ActorCount
//   +0x38 i32[WorkerCount] worker-offset table
//
// AtelWorker header (WorkerHeaderLength = 0x34 bytes), read at each table entry:
//   +0x00 u16 EventType         +0x02 u16 VariableCount
//   +0x04 u16 IntegerConstantCount · +0x06 u16 FloatConstantCount
//   +0x08 u16 FunctionCount     +0x0A u16 JumpCount
//   +0x10 i32 PrivateDataLength +0x18 i32 IntegerConstantOffset
//   +0x1C i32 FloatConstantOffset   +0x20 i32 FunctionTable
//   +0x24 i32 JumpTable         +0x2C i32 PrivateDataOffset
//   +0x30 i32 SharedDataOffset
//
// NOTE (2026-08-06): the field-event scanner historically used workerCount@0x36 while the
// ZW battle parser uses 0x34/0x36. Both are supported by exposing the raw words; callers must
// validate against the real corpus and prefer the proven value for the file family they parse.
// ──────────────────────────────────────────────────────────────────────────────────────

/// <summary>Parsed ATEL worker descriptor (header + constant pools).</summary>
public sealed class AtelWorkerInfo
{
    public required int Index { get; init; }
    public required int HeaderOffset { get; init; }
    public required ushort EventType { get; init; }
    public required ushort VariableCount { get; init; }
    public required ushort IntegerConstantCount { get; init; }
    public required ushort FloatConstantCount { get; init; }
    public required ushort FunctionCount { get; init; }
    public required ushort JumpCount { get; init; }
    public required int PrivateDataLength { get; init; }
    public required int IntegerConstantOffset { get; init; }
    public required int FloatConstantOffset { get; init; }
    public required int FunctionTable { get; init; }
    public required int JumpTable { get; init; }
    public required int PrivateDataOffset { get; init; }
    public required int SharedDataOffset { get; init; }
    public required IReadOnlyList<int> IntegerConstantValues { get; init; }
    public required IReadOnlyList<int> FloatConstantBits { get; init; }

    /// <summary>Resolve a PUSH_FLOAT_ARR operand to its real float value (bit pattern -> float).</summary>
    public float GetFloatConstant(ushort index)
        => index < FloatConstantBits.Count ? BitConverter.Int32BitsToSingle(FloatConstantBits[index]) : 0f;
}

/// <summary>Reads the ATEL static header + worker descriptor table.</summary>
public static class AtelScriptParser
{
    public const int StaticHeaderLength = 0x38;
    public const int WorkerHeaderLength = 0x34;

    public static IReadOnlyList<AtelWorkerInfo> ReadWorkers(
        byte[] bytes, int workerCount, int tableOffset = 0x38)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        var workers = new List<AtelWorkerInfo>(workerCount);
        RequireRange(bytes, tableOffset, checked(workerCount * 4), "ATEL worker offset table");
        for (int i = 0; i < workerCount; i++)
        {
            int workerOffset = ReadInt32(bytes, tableOffset + i * 4);
            workers.Add(ReadWorker(bytes, i, workerOffset));
        }
        return workers;
    }

    public static AtelWorkerInfo ReadWorker(byte[] bytes, int index, int offset)
    {
        RequireRange(bytes, offset, WorkerHeaderLength, $"worker {index} header");
        return new AtelWorkerInfo
        {
            Index = index,
            HeaderOffset = offset,
            EventType = ReadUInt16(bytes, offset),
            VariableCount = ReadUInt16(bytes, offset + 0x02),
            IntegerConstantCount = ReadUInt16(bytes, offset + 0x04),
            IntegerConstantOffset = ReadInt32(bytes, offset + 0x18),
            FloatConstantCount = ReadUInt16(bytes, offset + 0x06),
            FloatConstantOffset = ReadInt32(bytes, offset + 0x1C),
            FunctionCount = ReadUInt16(bytes, offset + 0x08),
            JumpCount = ReadUInt16(bytes, offset + 0x0A),
            PrivateDataLength = ReadInt32(bytes, offset + 0x10),
            FunctionTable = ReadInt32(bytes, offset + 0x20),
            JumpTable = ReadInt32(bytes, offset + 0x24),
            PrivateDataOffset = ReadInt32(bytes, offset + 0x2C),
            SharedDataOffset = ReadInt32(bytes, offset + 0x30),
            IntegerConstantValues = ReadInt32Table(bytes, ReadInt32(bytes, offset + 0x18),
                ReadUInt16(bytes, offset + 0x04), $"worker {index} integer constants"),
            FloatConstantBits = ReadInt32Table(bytes, ReadInt32(bytes, offset + 0x1C),
                ReadUInt16(bytes, offset + 0x06), $"worker {index} float constants"),
        };
    }

    public static IReadOnlyList<int> ReadInt32Table(byte[] bytes, int offset, int count, string description)
    {
        if (count == 0) return [];
        RequireRange(bytes, offset, checked(count * 4), description);
        return Enumerable.Range(0, count).Select(i => ReadInt32(bytes, offset + i * 4)).ToList();
    }

    public static int ReadInt32(byte[] bytes, int offset)
        => bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);

    public static ushort ReadUInt16(byte[] bytes, int offset)
        => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    public static void RequireRange(byte[] bytes, int offset, int length, string description)
    {
        if (offset < 0 || length < 0 || offset + length > bytes.Length)
            throw new InvalidOperationException($"ATEL {description} out of range (off=0x{offset:X} len=0x{length:X} blob=0x{bytes.Length:X}).");
    }
}
