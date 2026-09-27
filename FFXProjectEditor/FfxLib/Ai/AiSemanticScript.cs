using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    // P2 semantic IR (read-only overlay) — see docs/ai/P2_IR_SEMANTICA_2026-07-31.md (L1).
    //
    // This file does NOT duplicate or modify the ATEL codec (AiScript_File.cs is untouched).
    // It layers a per-instruction evidence + meaning model on top of the codec's own walk:
    //   • nodes are produced from AiScriptFile.Instructions (the codec's byte-exact disassembly);
    //   • WorkerIndex per instruction comes from AiScript_File.InstructionOwners (the CFG walk),
    //     with the same entrypoint-range fallback AiScript_Diff uses for dead code;
    //   • Evidence defaults to (Decoded, "opcode-table"); a PUSHII whose operand is a known
    //     command enriches Meaning via AiCommandId.Decode (the real public API) and records the
    //     source as "opcode-table|bible".
    // Every meaning string below is taken verbatim from the codec's own comments / AiStackModel
    // (same FFXDataParser provenance) — nothing is invented here.

    /// <summary>Evidence state of an ATEL instruction in the semantic IR. L1 only produces
    /// <see cref="Decoded"/>; Hypothesis/Confirmed are reserved for later layers (bible/corpus/runtime).</summary>
    public enum AiEvidenceState
    {
        /// <summary>Decoded from the bytecode itself via the proven opcode table.</summary>
        Decoded = 0,
        /// <summary>Advisory interpretation (e.g. a command/function name from a catalog).</summary>
        Hypothesis = 1,
        /// <summary>Confirmed by an external source (runtime probe, RT2, corpus oracle).</summary>
        Confirmed = 2,
    }

    /// <summary>Per-instruction evidence: state + provenance source string (e.g. "opcode-table",
    /// "opcode-table|bible", "corpus", "runtime").</summary>
    public sealed record AiEvidence(
        AiEvidenceState State,
        string Source,
        string? Note = null);

    /// <summary>One semantic node: the codec's decoded instruction plus evidence and meaning.
    /// WorkerIndex follows AiScript_Diff's convention (-1 = no owner could be attributed).</summary>
    public sealed record AiInstructionNode(
        int WorkerIndex,
        int Offset,
        byte Opcode,
        ushort? Operand,
        AiOperandKind OperandKind,
        AiEvidence Evidence,
        string? Meaning);

    /// <summary>Read-only semantic overlay of an ATEL script. Source is a live reference to the
    /// codec model (never re-parses the bytes); Nodes is a deterministic expansion of the
    /// instructions, one node per (worker, offset) owner pair.</summary>
    public sealed record AiSemanticScript(
        AiScriptFile Source,
        IReadOnlyList<AiInstructionNode> Nodes)
    {
        /// <summary>Build the semantic IR over a codec model. Iterates the codec's own decoded
        /// instruction list (AiScriptFile.Instructions — the byte-exact walk of
        /// [ScriptStart .. ScriptStart+CodeLength)) and attributes each instruction to its
        /// executing worker(s) via AiScript_File.InstructionOwners (the CFG walk of
        /// entrypoints + branches). Instructions the walk cannot reach fall back to the
        /// entrypoint-range heuristic (same rule as AiScript_Diff), so every instruction
        /// always yields at least one node.</summary>
        public static AiSemanticScript Build(AiScriptFile file)
        {
            ArgumentNullException.ThrowIfNull(file);

            IReadOnlyDictionary<int, IReadOnlyList<int>> owners = AiScript_File.InstructionOwners(file);

            var nodes = new List<AiInstructionNode>(file.Instructions.Count);
            foreach (AiInstruction instruction in file.Instructions)
            {
                IReadOnlyList<int> instructionOwners =
                    owners.TryGetValue(instruction.Offset, out IReadOnlyList<int>? exact)
                        ? exact
                        : InferPhysicalOwners(file, instruction.Offset);
                if (instructionOwners.Count == 0)
                    instructionOwners = new[] { -1 };

                (string? meaning, string source) = ResolveMeaning(instruction);
                AiEvidence evidence = new(AiEvidenceState.Decoded, source);

                foreach (int workerIndex in instructionOwners.Distinct().OrderBy(i => i))
                {
                    nodes.Add(new AiInstructionNode(
                        workerIndex,
                        instruction.Offset,
                        instruction.Opcode,
                        instruction.HasOperand ? instruction.Operand : (ushort?)null,
                        AiScript_File.OperandKindOf(instruction.Opcode),
                        evidence,
                        meaning));
                }
            }

            return new AiSemanticScript(file, nodes);
        }

        /// <summary>Fallback owner attribution for code the CFG walk cannot reach (dead/appended
        /// code): the worker whose earliest entrypoint precedes the offset (last such worker),
        /// mirroring AiScript_Diff.InferPhysicalOwners so diff keys stay consistent across layers.</summary>
        static IReadOnlyList<int> InferPhysicalOwners(AiScriptFile script, int offset)
        {
            var ranges = script.Workers
                .Where(worker => worker.Entrypoints.Count > 0)
                .Select(worker => (Start: script.ScriptStart + worker.Entrypoints.Min(), worker))
                .OrderBy(pair => pair.Start)
                .ToList();

            if (ranges.Count == 0)
                return Array.Empty<int>();

            AiWorker? current = null;
            foreach ((int start, AiWorker worker) in ranges)
            {
                if (offset < start)
                    break;
                current = worker;
            }

            return current == null ? Array.Empty<int>() : new[] { current.Index };
        }

        /// <summary>Resolve the meaning + evidence source for one instruction. Meanings come from
        /// the small opcode table below (names taken from the codec's own comments). A PUSHII
        /// (immediate) whose operand is a known command id additionally appends the real command
        /// name from AiCommandId.Decode and records "opcode-table|bible".</summary>
        static (string? Meaning, string Source) ResolveMeaning(AiInstruction instruction)
        {
            if (!OpcodeMeanings.TryGetValue(instruction.Opcode, out string? baseMeaning))
                return (null, "opcode-table");

            if (AiScript_File.OperandKindOf(instruction.Opcode) == AiOperandKind.Immediate)
            {
                AiCommandDecode decode = AiCommandId.Decode(instruction.Operand);
                if (decode.IsKnown)
                    return ($"{baseMeaning} -> {decode.Name}", "opcode-table|bible");
            }

            return (baseMeaning, "opcode-table");
        }



        // Opcode -> meaning. Every name/description is copied from the codec's own documentation:
        // AiScript_File.cs (opcode comments, Kinds table, InstructionOwners walk) and
        // AiStackModel.cs (stack-effect table) — both built on the public FFXDataParser RE
        // (Karifean/Fahrenheit). Opcodes absent here get Meaning == null (honest "not named").
        static readonly IReadOnlyDictionary<byte, string> OpcodeMeanings = new Dictionary<byte, string>
        {
            [0x00] = "NOP",
            // binary operators (pop 2, push 1) — comparison / logic / arithmetic / shift
            [0x01] = "binary operator (pop 2, push 1)",
            [0x02] = "binary operator (pop 2, push 1)",
            [0x03] = "binary operator (pop 2, push 1)",
            [0x04] = "binary operator (pop 2, push 1)",
            [0x05] = "binary operator (pop 2, push 1)",
            [0x06] = "binary operator (pop 2, push 1)",
            [0x07] = "binary operator (pop 2, push 1)",
            [0x08] = "binary operator (pop 2, push 1)",
            [0x09] = "binary operator (pop 2, push 1)",
            [0x0A] = "binary operator (pop 2, push 1)",
            [0x0B] = "binary operator (pop 2, push 1)",
            [0x0C] = "binary operator (pop 2, push 1)",
            [0x0D] = "binary operator (pop 2, push 1)",
            [0x0E] = "binary operator (pop 2, push 1)",
            [0x0F] = "binary operator (pop 2, push 1)",
            [0x12] = "binary operator (pop 2, push 1)",
            [0x13] = "binary operator (pop 2, push 1)",
            [0x14] = "binary operator (pop 2, push 1)",
            [0x15] = "binary operator (pop 2, push 1)",
            [0x16] = "binary operator (pop 2, push 1)",
            [0x17] = "binary operator (pop 2, push 1)",
            [0x18] = "binary operator (pop 2, push 1)",
            [0x19] = "NOT (unary)",
            [0x29] = "PUSHY / GET_CASE",
            [0x2A] = "POPX / SET_TEST",
            [0x2B] = "REPUSH / COPY",
            [0x2C] = "POPY / SET_CASE",
            [0x3C] = "RET / END",
            [0x40] = "terminator (walk-only)",
            // POPI/F (store temp reg)
            [0x59] = "POPI/F (store temp reg)",
            [0x5A] = "POPI/F (store temp reg)",
            [0x5B] = "POPI/F (store temp reg)",
            [0x5C] = "POPI/F (store temp reg)",
            [0x5D] = "POPI/F (store temp reg)",
            [0x5E] = "POPI/F (store temp reg)",
            [0x60] = "POPI/F (store temp reg)",
            // PUSHI/F (load temp reg)
            [0x67] = "PUSHI/F (load temp reg)",
            [0x68] = "PUSHI/F (load temp reg)",
            [0x69] = "PUSHI/F (load temp reg)",
            [0x6A] = "PUSHI/F (load temp reg)",
            [0x6B] = "PUSHI/F (load temp reg)",
            [0x6C] = "PUSHI/F (load temp reg)",
            [0x6E] = "PUSHI/F (load temp reg)",
            [0x9F] = "PUSHV (load variable)",
            [0xA0] = "POPV (store variable)",
            [0xA2] = "PUSHAR (load array element)",
            [0xA3] = "POPAR (store array element)",
            [0xAD] = "PUSHI (push int32 from refInts pool)",
            [0xAE] = "PUSHII (push immediate int16)",
            [0xAF] = "PUSHF (push float from float pool)",
            [0xB0] = "JMP",
            [0xB5] = "CALL",
            [0xD6] = "POPXCJMP (jump if true)",
            [0xD7] = "POPXNCJMP (jump if false)",
            [0xD8] = "CALLPOPA (void call)",
        };
    }
}
