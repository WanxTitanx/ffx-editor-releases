using System;
using System.Buffers.Binary;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace FFXProjectEditor.FfxLib.Event
{
    /// <summary>ATEL event-script disassembler for EBP chunk 0 (EV01 container).
    /// Opcode format: bit7=has_imm (3 bytes), low7=opcode (0x00-0x7A).
    /// Native call dispatch: opcode 0x35 or 0x58 triggers FFX_Atel_DispatchNativeCall.
    /// See docs/reverse/FFX_EVENTVM_OPS_2026-09-17.md for the full opcode table.</summary>
    public static class EventAtelDisassembler
    {
        // Native name table @0xC54600, 123 records in the verified FFX.exe.
        // Drop only the arithmetic/logical OP prefix for the existing display convention.
        static readonly string[] Mnemonics =
        {
            "NOP", "LOR", "LAND", "OR", "EOR", "AND", "EQ", "NE", // 0x00
            "GTU", "LSU", "GT", "LS", "GTEU", "LSEU", "GTE", "LSE", // 0x08
            "BON", "BOFF", "SLL", "SRL", "ADD", "SUB", "MUL", "DIV", // 0x10
            "MOD", "NOT", "UMINUS", "FIXADRS", "BNOT", "LABEL", "TAG", "PUSHV", // 0x18
            "POPV", "POPVL", "PUSHAR", "POPAR", "POPARL", "POPA", "PUSHA", "PUSHARP", // 0x20
            "PUSHX", "PUSHY", "POPX", "REPUSH", "POPY", "PUSHI", "PUSHII", "PUSHF", // 0x28
            "JMP", "CJMP", "NCJMP", "JSR", "RTS", "CALL", "REQ", "REQSW", // 0x30
            "REQEW", "PREQ", "PREQSW", "PREQEW", "RET", "RETN", "RETT", "RETTN", // 0x38
            "HALT", "PUSHN", "PUSHT", "PUSHVP", "PUSHFIX", "FREQ", "TREQ", "BREQ", // 0x40
            "BFREQ", "BTREQ", "FREQSW", "TREQSW", "BREQSW", "BFREQSW", "BTREQSW", "FREQEW", // 0x48
            "TREQEW", "BREQEW", "BFREQEW", "BTREQEW", "DRET", "POPXJMP", "POPXCJMP", "POPXNCJMP", // 0x50
            "CALLPOPA", "POPI0", "POPI1", "POPI2", "POPI3", "POPF0", "POPF1", "POPF2", // 0x58
            "POPF3", "POPF4", "POPF5", "POPF6", "POPF7", "POPF8", "POPF9", "PUSHI0", // 0x60
            "PUSHI1", "PUSHI2", "PUSHI3", "PUSHF0", "PUSHF1", "PUSHF2", "PUSHF3", "PUSHF4", // 0x68
            "PUSHF5", "PUSHF6", "PUSHF7", "PUSHF8", "PUSHF9", "PUSHAINTER", "SYSTEM", "REQWAIT", // 0x70
            "PREQWAIT", "REQCHG", "ACTREQ", // 0x78
        };

        /// <summary>True when opcode encodes a u16 operand (bit7=0x80 set).</summary>
        public static bool HasOperand(byte opcode) => (opcode & 0x80) != 0;

        /// <summary>Extract the opcode mnemonic from the raw byte.</summary>
        public static string Mnemonic(byte rawOpcode)
        {
            byte op = (byte)(rawOpcode & 0x7F);
            return op < Mnemonics.Length ? Mnemonics[op] : $"?0x{op:X2}";
        }

        /// <summary>Disassemble one instruction at offset. Returns (offset+length, line text).</summary>
        public static (int nextOffset, string line) DisassembleOne(byte[] script, int offset, Func<int, string>? resolveCall = null)
        {
            ArgumentNullException.ThrowIfNull(script);
            if (offset < 0 || offset >= script.Length)
                return (offset, $";; OUT OF BOUNDS @ 0x{offset:X4}");

            byte raw = script[offset];
            byte op = (byte)(raw & 0x7F);
            bool hasImm = HasOperand(raw);
            string mnem = Mnemonic(raw);
            string arg = "";
            ushort operand = 0;

            if (hasImm)
            {
                if (offset + 3 > script.Length)
                    return (offset + 1, $";; TRUNCATED @ 0x{offset:X4}");
                operand = (ushort)(script[offset + 1] | (script[offset + 2] << 8));
                int callId = op switch
                {
                    0x35 or 0x58 when resolveCall != null => operand,
                    _ => -1
                };
                arg = callId >= 0
                    ? $" 0x{operand:X4}  ; {resolveCall(callId)}"
                    : $" 0x{operand:X4}";
                return (offset + 3, $"{offset:X4}  {raw:X2}  {mnem}{arg}");
            }
            return (offset + 1, $"{offset:X4}  {raw:X2}  {mnem}");
        }

        /// <summary>Disassemble full ATEL script byte range. Uses EventCallGlossary for native call resolution.</summary>
        public static List<string> Disassemble(byte[] script, int start = 0, int? length = null, Func<int, string>? resolveCall = null)
        {
            int end = RangeEnd(script, start, length);
            resolveCall ??= (callId) => EventCallGlossary.Entries.TryGetValue(callId, out string? name)
                ? name : $"?0x{callId:X4}";
            var lines = new List<string>();
            int offset = start;
            while (offset < end)
            {
                if (HasOperand(script[offset]) && end - offset < 3)
                {
                    lines.Add($";; TRUNCATED @ 0x{offset:X4}");
                    break;
                }
                var (next, line) = DisassembleOne(script, offset, resolveCall);
                lines.Add(line);
                if (next <= offset) break; // safety: prevent infinite loop
                offset = next;
            }
            return lines;
        }

        /// <summary>Native call resolver for the EBP call glossary.
        /// Uses funcspace = (operand >> 12), callIndex = (operand & 0xFFF).</summary>
        public static string ResolveNativeCall(int operand, Dictionary<int, string> callGlossary)
        {
            int callId = operand;
            return callGlossary.TryGetValue(callId, out string? name) ? name : $"UNKNOWN_0x{callId:X4}";
        }

        /// <summary>Structured disassembly: returns typed instructions for UI binding.</summary>
        public static List<DisassemblyInstruction> DisassembleStructured(byte[] script, int start = 0, int? length = null, Func<int, string>? resolveCall = null)
        {
            int end = RangeEnd(script, start, length);
            resolveCall ??= (callId) => EventCallGlossary.Entries.TryGetValue(callId, out string? name)
                ? name : $"?0x{callId:X4}";
            var instructions = new List<DisassemblyInstruction>();
            int offset = start;
            while (offset < end)
            {
                byte raw = script[offset];
                byte op = (byte)(raw & 0x7F);
                bool hasImm = HasOperand(raw);
                string mnem = Mnemonic(raw);
                ushort operand = 0;
                bool isNativeCall = false;
                string glossaryName = "";
                int instrLen = 1;
                if (hasImm)
                {
                    if (end - offset < 3) break;
                    operand = (ushort)(script[offset + 1] | (script[offset + 2] << 8));
                    instrLen = 3;
                    if (op == 0x35 || op == 0x58)
                    {
                        isNativeCall = true;
                        glossaryName = resolveCall(operand);
                    }
                }
                instructions.Add(new DisassemblyInstruction
                {
                    Offset = offset, Length = instrLen, Raw = raw, Mnemonic = mnem,
                    HasOperand = hasImm, Operand = operand, IsNativeCall = isNativeCall, GlossaryName = glossaryName
                });
                offset += instrLen;
            }
            return instructions;
        }

        /// <summary>Read the code interval from an ATEL chunk-0 header. Offsets stay
        /// chunk-relative so consumers cannot confuse header/data bytes with editable code.</summary>
        public static (int Offset, int Length) CodeRange(byte[] chunk)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            if (chunk.Length < 0x38)
                throw new InvalidDataException("ATEL chunk is shorter than its 0x38-byte header.");
            int length = BinaryPrimitives.ReadInt32LittleEndian(chunk);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(chunk.AsSpan(0x30));
            // Subtraction avoids overflow on malformed offset+length values.
            if (offset < 0x38 || offset > chunk.Length || length < 0 || length > chunk.Length - offset)
                throw new InvalidDataException($"ATEL code range is invalid (offset=0x{offset:X}, length=0x{length:X}, chunk=0x{chunk.Length:X}).");
            return (offset, length);
        }

        /// <summary>Disassemble only the declared code in a complete ATEL chunk.
        /// Reject truncated code instead of exposing a partial patchable instruction list.</summary>
        public static List<DisassemblyInstruction> DisassembleAtelChunk(byte[] chunk, Func<int, string>? resolveCall = null)
        {
            var range = CodeRange(chunk);
            var instructions = DisassembleStructured(chunk, range.Offset, range.Length, resolveCall);
            int decodedEnd = instructions.Count == 0 ? range.Offset : instructions[^1].Offset + instructions[^1].Length;
            if (decodedEnd != range.Offset + range.Length)
                throw new InvalidDataException($"ATEL instruction is truncated at 0x{decodedEnd:X}.");
            return instructions;
        }

        static int RangeEnd(byte[] bytes, int start, int? length)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (start < 0 || start > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(start));
            int count = length ?? bytes.Length - start;
            if (count < 0 || count > bytes.Length - start)
                throw new ArgumentOutOfRangeException(nameof(length));
            return start + count;
        }
    }

    public sealed class DisassemblyInstruction
    {
        public required int Offset { get; init; }
        public required int Length { get; init; }
        public required byte Raw { get; init; }
        public required string Mnemonic { get; init; }
        public required bool HasOperand { get; init; }
        public ushort Operand { get; init; }
        public bool IsNativeCall { get; init; }
        public string GlossaryName { get; init; } = "";

        public int FuncspaceId => IsNativeCall ? Operand >> 12 : -1;
        public int CallIndex => IsNativeCall ? Operand & 0xFFF : -1;
        public string FuncspaceName => FuncspaceId >= 0 ? s_funcspaceNames.GetValueOrDefault(FuncspaceId, $"?FS{FuncspaceId}") : "";

        public string OffsetLabel => $"{Offset:X4}";
        public string RawLabel => $"{Raw:X2}";
        public string OperandLabel => HasOperand ? $"0x{Operand:X4}" : "";
        public string GlossaryLabel => string.IsNullOrEmpty(GlossaryName) ? "" : $"; {GlossaryName}";
        public string FormattedLine => $"{Offset:X4}  {Raw:X2}  {Mnemonic}{(HasOperand ? $"  0x{Operand:X4}" : "")}{(string.IsNullOrEmpty(GlossaryName) ? "" : $"  ; {GlossaryName}")}";
        public bool ShowOperand => HasOperand;
        public bool ShowGlossary => IsNativeCall;
        public string MnemonicBrush => IsNativeCall ? "#FFC107" : "#CCC";

        static readonly Dictionary<int, string> s_funcspaceNames = new()
        {
            [0] = "Common", [1] = "Math", [4] = "SgEvent", [5] = "ChEvent",
            [6] = "Camera", [7] = "Battle", [8] = "Map", [9] = "Mount",
            [0xB] = "Movie", [0xC] = "Debug", [0xD] = "AbiMap",
        };
    }
}
