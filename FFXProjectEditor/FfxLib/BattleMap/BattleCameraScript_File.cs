using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// 🎥 AURORA CHAMBER — reader/editor of the BATTLE CAMERA shots that live in the ATEL script (chunk0)
    /// of a per-battle <c>&lt;id&gt;/&lt;id&gt;.bin</c>.
    ///
    /// RE (docs/reverse/FFX_BATTLE_CAMERA_NOT_IN_CHUNK3_IDA_2026-06-07.md): the battle camera is NOT a static
    /// transform in chunk3 (<c>+0x2C</c> is a constant default) — it is <b>script-driven</b>. The script lives in
    /// chunk0, which is a plain ATEL AiFile blob (same format the monster AI codec <see cref="AiScript_File"/>
    /// decodes: <c>codeLength@+0x00</c>, <c>declaredLength@+0x10 == chunk0 length</c>, <c>scriptStart@+0x30</c>).
    /// Each camera cut is a <c>camReq</c> call (<c>FFX_Atel_Battle_camReq @ 0x7A5E10</c>, func-id <c>0x703F</c>,
    /// Camera namespace high-nibble 0x6→…) which pops two operands: <c>arg1 = SHOT/angle (1-based)</c> and
    /// <c>arg2 = TARGET</c> (<c>0xFFFF</c> = none). In a stack VM the args are pushed in order before the call, so
    /// the instruction immediately preceding the <c>camReq</c> pushes the TARGET and the one before it pushes the
    /// SHOT.
    ///
    /// This is therefore a <b>codec operation</b>, exactly like editing monster AI: find the <c>camReq</c> calls in
    /// chunk0 and read/rewrite the two literal pushes that feed them. Editing a <c>PUSHII</c> (0xAE) immediate is
    /// byte-local (same length — operand edits never resize), so the chunk0 partition keeps its length and every
    /// other chunk offset is preserved verbatim (no relocation). This swaps between the camera angles/targets that
    /// the script ALREADY references; a brand-new angle (custom position/FOV) needs RE of the shot table inside
    /// <c>FFX_Battle_Camera_RequestShot @ 0x797BD0</c> (a later, IDA-gated step).
    ///
    /// Deliberately Avalonia-free / dependency-light (only <c>System.*</c> + the System.*-only
    /// <see cref="AiScript_File"/>) so the RT0 gate (RuntimeTools/BattleCameraScanLab) can link it directly,
    /// mirroring how the other Aurora labs link the production reader without pulling the editor.
    /// </summary>
    public sealed class BattleCameraScript_File
    {
        /// <summary>The ATEL func-id of the battle camera-request call (<c>camReq</c>). High nibble 0x6 = Camera ns.</summary>
        public const ushort CamReqFuncId = 0x703F;

        /// <summary>The whole per-battle bin as read — needed by the (same-length) edit primitives.</summary>
        public required byte[] OriginalBinBytes { get; init; }
        public required string BattleId { get; init; }
        /// <summary>Byte offset of chunk0 (the ATEL script) within the bin; -1 when chunk0 is absent.</summary>
        public required int Chunk0Offset { get; init; }
        public required int Chunk0Length { get; init; }
        /// <summary>The decoded chunk0 script (null when chunk0 is absent / not a valid ATEL blob).</summary>
        public AiScriptFile? Script { get; init; }
        /// <summary>True when the chunk0 instruction walk closed exactly on codeLength (the codec understands it).</summary>
        public bool CodeWalkClosedExactly { get; init; }
        /// <summary>Opcodes seen in chunk0 outside the proven ATEL set (should be empty — a non-ATEL chunk0 flag).</summary>
        public IReadOnlyList<byte> UnknownOpcodes { get; init; } = Array.Empty<byte>();
        /// <summary>The camera cuts (<c>camReq</c> calls) found in chunk0, in script order.</summary>
        public required IReadOnlyList<CameraShotRef> Shots { get; init; }
        /// <summary>Diagnostics (chunk0 absent / decode threw / pointer issues). Empty == clean.</summary>
        public required IReadOnlyList<string> Notes { get; init; }

        public bool HasScript => Script != null && Script.HasScript;
        public int ShotCount => Shots.Count;

        /// <summary>Decode the camera shots from a whole per-battle bin (parses the chunk table inline; no
        /// Battle_File dependency). Never throws on malformed input — surfaces problems via <see cref="Notes"/>.</summary>
        public static BattleCameraScript_File ReadFromBattleBin(string battleId, byte[] battleBinBytes)
        {
            ArgumentNullException.ThrowIfNull(battleBinBytes);
            var notes = new List<string>();
            (int start, int length) = ExtractChunk0Span(battleBinBytes);
            if (start < 0)
            {
                notes.Add("chunk0 (ATEL script) absent or smaller than an AiFile header.");
                return Empty(battleId, battleBinBytes, notes);
            }

            byte[] chunk0 = new byte[length];
            Array.Copy(battleBinBytes, start, chunk0, 0, length);

            AiScriptFile script;
            try { script = AiScript_File.Read(chunk0); }
            catch (Exception ex)
            {
                notes.Add($"chunk0 @ 0x{start:X} (len 0x{length:X}) did not decode as ATEL: {ex.Message}");
                return Empty(battleId, battleBinBytes, notes, start, length);
            }

            if (!script.CodeWalkClosedExactly)
                notes.Add("chunk0 code walk did not close exactly on codeLength (length table incomplete for this script).");
            if (script.UnknownOpcodes.Count > 0)
                notes.Add($"chunk0 carries {script.UnknownOpcodes.Count} opcode(s) outside the proven ATEL set.");

            var shots = ExtractShots(script);

            return new BattleCameraScript_File
            {
                OriginalBinBytes = battleBinBytes,
                BattleId = battleId,
                Chunk0Offset = start,
                Chunk0Length = length,
                Script = script,
                CodeWalkClosedExactly = script.CodeWalkClosedExactly,
                UnknownOpcodes = script.UnknownOpcodes,
                Shots = shots,
                Notes = notes,
            };
        }

        static BattleCameraScript_File Empty(string battleId, byte[] bin, List<string> notes, int start = -1, int length = 0) =>
            new()
            {
                OriginalBinBytes = bin,
                BattleId = battleId,
                Chunk0Offset = start,
                Chunk0Length = length,
                Script = null,
                CodeWalkClosedExactly = false,
                UnknownOpcodes = Array.Empty<byte>(),
                Shots = Array.Empty<CameraShotRef>(),
                Notes = notes,
            };

        // ---- shot extraction -------------------------------------------------------------------------------

        static IReadOnlyList<CameraShotRef> ExtractShots(AiScriptFile script)
        {
            var shots = new List<CameraShotRef>();
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            for (int idx = 0; idx < ins.Count; idx++)
            {
                AiInstruction call = ins[idx];
                if (AiScript_File.OperandKindOf(call.Opcode) != AiOperandKind.FuncId) continue;
                if (call.Operand != CamReqFuncId) continue;

                // arg2 (TARGET) is the push immediately before; arg1 (SHOT) the one before that.
                CameraArg target = idx - 1 >= 0 ? ClassifyArg(script, ins[idx - 1], idx - 1) : CameraArg.Missing;
                CameraArg shot = idx - 2 >= 0 ? ClassifyArg(script, ins[idx - 2], idx - 2) : CameraArg.Missing;
                shots.Add(new CameraShotRef
                {
                    Index = shots.Count,
                    InstructionIndex = idx,
                    Offset = call.Offset,
                    CallOpcode = call.Opcode,
                    Shot = shot,
                    Target = target,
                });
            }
            return shots;
        }

        // Classify a pushed argument: a direct literal (PUSHII immediate) is edit-ready; an int-const-pool push
        // (PUSHI) has a known value but editing it touches a SHARED pool entry; a temp-register / computed push
        // has no statically-known literal. Only PUSHII immediates are byte-local edit-ready.
        static CameraArg ClassifyArg(AiScriptFile script, AiInstruction push, int instrIndex)
        {
            byte op = push.Opcode;
            switch (op)
            {
                case 0xAE: // PUSHII — immediate int16, value IS the operand → directly editable, byte-local.
                    return new CameraArg
                    {
                        Present = true,
                        InstructionIndex = instrIndex,
                        Offset = push.Offset,
                        Opcode = op,
                        Kind = CameraArgKind.Immediate,
                        Value = (short)push.Operand,
                        RawValue = push.Operand,
                        Editable = true,
                    };
                case 0xAD: // PUSHI — int32 from the refInts const pool by index (operand = pool index). Shared.
                    int? poolVal = null;
                    if (script.IntPoolOffset >= 0)
                    {
                        int off = script.IntPoolOffset + 4 * push.Operand;
                        if (off >= 0 && off + 4 <= script.OriginalAiFileBytes.Length)
                            poolVal = BinaryPrimitives.ReadInt32LittleEndian(script.OriginalAiFileBytes.AsSpan(off, 4));
                    }
                    return new CameraArg
                    {
                        Present = true,
                        InstructionIndex = instrIndex,
                        Offset = push.Offset,
                        Opcode = op,
                        Kind = CameraArgKind.IntConst,
                        Value = poolVal,
                        RawValue = push.Operand,
                        Editable = false, // editable only via the shared pool — not exposed as a byte-local shot edit
                    };
                default:
                    // Temp-register banks (PUSHI0-3 0x67-0x6A / PUSHF0-9 0x6B-0x74) or a computed expression.
                    return new CameraArg
                    {
                        Present = true,
                        InstructionIndex = instrIndex,
                        Offset = push.Offset,
                        Opcode = op,
                        Kind = CameraArgKind.Computed,
                        Value = null,
                        RawValue = push.HasOperand ? push.Operand : (ushort)0,
                        Editable = false,
                    };
            }
        }

        // ---- byte-local edit (same length; chunk0 + every other chunk offset preserved) -------------------

        /// <summary>Rewrite the SHOT angle of a camera cut. The shot must be a directly-editable immediate
        /// (<see cref="CameraArgKind.Immediate"/>); returns a new bin with only the two operand bytes changed.</summary>
        public byte[] WithShot(CameraShotRef shot, short newShot)
        {
            ArgumentNullException.ThrowIfNull(shot);
            return PatchImmediate(shot.Shot, unchecked((ushort)newShot), "shot");
        }

        /// <summary>Rewrite the TARGET of a camera cut (0xFFFF = none). Must be a directly-editable immediate.</summary>
        public byte[] WithTarget(CameraShotRef shot, ushort newTarget)
        {
            ArgumentNullException.ThrowIfNull(shot);
            return PatchImmediate(shot.Target, newTarget, "target");
        }

        byte[] PatchImmediate(CameraArg arg, ushort newValue, string what)
        {
            if (!arg.Present || arg.Kind != CameraArgKind.Immediate || !arg.Editable)
                throw new InvalidOperationException($"camera {what} is not a directly-editable immediate (opcode 0x{arg.Opcode:X2}, kind {arg.Kind}).");
            if (Chunk0Offset < 0)
                throw new InvalidOperationException("no chunk0 to edit.");

            // arg.Offset is AiFile-relative (chunk0-relative); the u16 operand sits at instruction+1.
            int abs = Chunk0Offset + arg.Offset + 1;
            if (abs < 0 || abs + 2 > OriginalBinBytes.Length)
                throw new InvalidOperationException($"camera {what} operand offset 0x{abs:X} out of range (len 0x{OriginalBinBytes.Length:X}).");

            byte[] o = (byte[])OriginalBinBytes.Clone();
            o[abs] = (byte)(newValue & 0xFF);
            o[abs + 1] = (byte)((newValue >> 8) & 0xFF);
            return o;
        }

        // ---- chunk table -----------------------------------------------------------------------------------

        /// <summary>Locate chunk index 0 (the ATEL script) within a per-battle bin, mirroring the EOF-robust
        /// chunk-table walk of <see cref="BattleArenaAnchors_File"/>. Returns (start,length) or (-1,0) if absent.</summary>
        static (int start, int length) ExtractChunk0Span(byte[] bytes)
        {
            const int wantIndex = 0;
            if (bytes.Length < 8) return (-1, 0);

            int rawChunkValue = ReadInt32(bytes, 0x00);
            int chunkCount = rawChunkValue - 1;
            if (chunkCount < wantIndex) return (-1, 0);

            int[] offsets = new int[chunkCount + 1];
            for (int i = 0; i <= chunkCount; i++)
            {
                int off = ReadInt32(bytes, 0x04 + i * 4);
                if (off == unchecked((int)0xFFFFFFFF)) { chunkCount = i - 1; break; }
                offsets[i] = off;
            }
            if (chunkCount < wantIndex) return (-1, 0);

            int start = offsets[wantIndex];
            if (start <= 0 || start > bytes.Length) return (-1, 0);

            // end = first later offset that is >= start (same rule the chunk3 reader uses), else EOF.
            int end = -1;
            for (int j = wantIndex + 1; j <= chunkCount; j++)
                if (offsets[j] >= start) { end = offsets[j]; break; }
            if (end < 0 || end > bytes.Length) end = bytes.Length;

            int len = Math.Max(0, end - start);
            // An AiFile needs at least the minimal header; reject a degenerate slice.
            if (len < 0x34) return (-1, 0);
            return (start, len);
        }

        static int ReadInt32(byte[] b, int o) =>
            (o < 0 || o + 4 > b.Length) ? 0 : BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o, 4));
    }

    public enum CameraArgKind
    {
        Missing,    // the call had no preceding instruction in this slot
        Immediate,  // PUSHII (0xAE): the value is the operand → byte-local editable
        IntConst,   // PUSHI  (0xAD): int32 from the shared refInts pool (value known, edit not byte-local)
        Computed,   // temp-register bank / computed expression → no static literal
    }

    /// <summary>One operand feeding a <c>camReq</c> call (the SHOT or the TARGET), with its provenance so the
    /// editor knows whether it can rewrite it as a byte-local immediate.</summary>
    public sealed class CameraArg
    {
        public bool Present { get; init; }
        public int InstructionIndex { get; init; }
        /// <summary>AiFile (chunk0)-relative offset of the pushing instruction.</summary>
        public int Offset { get; init; }
        public byte Opcode { get; init; }
        public CameraArgKind Kind { get; init; }
        /// <summary>The decoded literal value when known (PUSHII signed / PUSHI pool value); null when computed.</summary>
        public int? Value { get; init; }
        /// <summary>The raw operand of the pushing instruction (immediate value, pool index, or register bank).</summary>
        public ushort RawValue { get; init; }
        public bool Editable { get; init; }

        public static CameraArg Missing => new() { Present = false, Kind = CameraArgKind.Missing };
    }

    /// <summary>One camera cut: a <c>camReq</c> call plus the two pushes (SHOT, TARGET) that feed it.</summary>
    public sealed class CameraShotRef
    {
        public required int Index { get; init; }
        /// <summary>Index of the <c>camReq</c> in the flat instruction list.</summary>
        public required int InstructionIndex { get; init; }
        /// <summary>AiFile (chunk0)-relative offset of the <c>camReq</c> call.</summary>
        public required int Offset { get; init; }
        /// <summary>The call opcode (0xD8 CALLPOPA / 0xB5 CALL).</summary>
        public required byte CallOpcode { get; init; }
        public required CameraArg Shot { get; init; }
        public required CameraArg Target { get; init; }

        /// <summary>True when BOTH operands are directly-editable immediates (the panel can rewrite this cut).</summary>
        public bool FullyEditable => Shot.Editable && Target.Editable;

        public override string ToString()
        {
            string s = Shot.Value?.ToString() ?? $"op_{Shot.Opcode:X2}";
            string t = Target.Value is int tv ? (tv == -1 ? "none" : tv.ToString()) : $"op_{Target.Opcode:X2}";
            return $"camReq#{Index} @0x{Offset:X4} shot={s} target={t}";
        }
    }
}
