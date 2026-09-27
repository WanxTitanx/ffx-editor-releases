using System;
using System.Collections.Generic;
using System.Text;

namespace FFXProjectEditor.FfxLib.Magic
{
    /// <summary>
    /// A single disassembled EgoVM instruction.
    /// </summary>
    public sealed class EgoVmInstruction
    {
        public int Offset { get; init; }                 // Byte offset in the code
        public ushort OpcodeWord { get; init; }          // Raw 16-bit opcode word
        public EgoVmOpcode Opcode => (EgoVmOpcode)(OpcodeWord & 0xFF);
        public bool IsExtended => (OpcodeWord & 0x100) != 0;
        public byte[] Operands { get; init; }             // Operand bytes after opcode
        public int TotalSize { get; init; }               // Total bytes of this instruction
        public string Mnemonic { get; init; }             // Resolved mnemonic or "op_XX"

        public string FormatRaw() => IsExtended
            ? $"ext_{Mnemonic}"
            : Mnemonic;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{Offset:X4}  ");
            sb.Append(FormatRaw().PadRight(24));

            // Hex dump of raw bytes up to 12
            int showLen = Math.Min(TotalSize, 12);
            for (int i = 0; i < showLen; i++)
                sb.Append($"{OpcodeWord & 0xFF:X2} ");

            if (TotalSize > 12)
                sb.Append("...");

            sb.Append(new string(' ', Math.Max(1, 42 - sb.Length)));
            sb.Append($"// {CategoryOrDesc()}");
            return sb.ToString();
        }

        private string CategoryOrDesc()
        {
            var info = EgoVmOpcodeTable.Get(Opcode);
            if (!string.IsNullOrEmpty(info.Description))
                return $"[{info.Category}] {info.Description}";
            return $"[{info.Category}]";
        }

        public string ToShortString()
        {
            return $"{Offset:X4}: {FormatRaw()} {string.Join(" ", Array.ConvertAll(Operands, b => $"{b:X2}"))}";
        }
    }

    /// <summary>
    /// Disassembles EgoVM bytecode into a sequence of instructions.
    ///
    /// Format:
    ///   - 2-byte opcode word (low byte = opcode, bit 0x100 = extended dispatch)
    ///   - N bytes of operand data (size depends on opcode)
    ///
    /// Approach:
    ///   - Look up opcode in EgoVmOpcodeTable for operand size
    ///   - If opcode byte is 0x00 (RecordEndCleanup), stop (end of record)
    ///   - Unknown opcodes fall back to default 2-byte operand size
    /// </summary>
    public static class EgoVmDisassembler
    {
        /// <summary>
        /// Disassemble a complete magic DLL file.
        /// WHY (corrected 2026-09-13, audit F73; base PINNED 2026-09-14, re-RE M2): the
        /// bytecode region lives in .data — the previous implementation fed the .text section
        /// (x86 thunks) as bytecode and produced garbage. Re-RE via idalib (docs/reverse/
        /// FFX_STRUCTURE_RERE_KERNEL_2026-09-14.md M2) pinned the region start:
        /// bytecode_base = imagebase + 0xB000 (= .data + 0x1000; file offset = raw_ptr + 0x1000),
        /// with magic "WD3\x01" at bytecode_base + 0x30 (verified on the vanilla PE, consistent
        /// with magic_0086/0087). Runtime consumer: interpreter 0x80CD60 reads words with a
        /// pointer-PC; DLL exports bound via GetProcAddress("InitMagicPRX"/"GetEffectOverlayTable").
        /// </summary>
        public static List<EgoVmInstruction> Disassemble(MagicDllFile dll)
        {
            return DisassembleBytes(dll.DataSection, 0);
        }

        /// <summary>
        /// Disassemble raw EgoVM bytecode starting from offset.
        /// </summary>
        public static List<EgoVmInstruction> DisassembleBytes(byte[] code, int startOffset = 0)
        {
            var result = new List<EgoVmInstruction>();
            int i = startOffset;

            while (i + 2 <= code.Length)
            {
                // Read 16-bit opcode word (little-endian)
                ushort opcodeWord = BitConverter.ToUInt16(code, i);
                byte opcodeByte = (byte)(opcodeWord & 0xFF);
                var opInfo = EgoVmOpcodeTable.Get(opcodeByte);

                // Determine operand size
                int operandSize = opInfo.OperandSize;

                // End of record: opcode 0x00 terminates this record
                if (opcodeByte == 0x00)
                {
                    result.Add(new EgoVmInstruction
                    {
                        Offset = i,
                        OpcodeWord = opcodeWord,
                        Operands = [],
                        TotalSize = 2,
                        Mnemonic = opInfo.Name,
                    });
                    break;
                }

                // Clamp operand size to available bytes
                int maxOperand = code.Length - i - 2;
                if (operandSize > maxOperand)
                    operandSize = Math.Max(0, maxOperand);

                // Extract operand bytes
                var operands = new byte[operandSize];
                if (operandSize > 0)
                    Array.Copy(code, i + 2, operands, 0, operandSize);

                int totalSize = 2 + operandSize;

                result.Add(new EgoVmInstruction
                {
                    Offset = i,
                    OpcodeWord = opcodeWord,
                    Operands = operands,
                    TotalSize = totalSize,
                    Mnemonic = opInfo.Name,
                });

                i += totalSize;
            }

            return result;
        }

        /// <summary>
        /// Format full disassembly as a string with header info.
        /// </summary>
        public static string FormatDisassembly(MagicDllFile dll)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// Magic DLL: {dll.DllName} (ID={dll.MagicId})");
            sb.AppendLine($"// .text (x86): {dll.TextSection.Length} bytes");
            sb.AppendLine($"// .data (EgoVM region + containers): {dll.DataSection.Length} bytes");
            sb.AppendLine($"// Handled opcodes: {EgoVmOpcodeTable.HandledCount}/256");
            sb.AppendLine();

            int instrCount = 0;
            var instructions = Disassemble(dll);
            foreach (var instr in instructions)
            {
                sb.AppendLine(instr.ToString());
                instrCount++;
            }

            sb.AppendLine();
            sb.AppendLine($"// Total: {instrCount} instructions");
            return sb.ToString();
        }
    }
}
