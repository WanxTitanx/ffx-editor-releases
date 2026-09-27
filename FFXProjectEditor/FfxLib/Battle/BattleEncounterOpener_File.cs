// ============================================================================
// BattleEncounterOpener_File — read-only reader of encounter opener / CTB seed in battle chunk0
// PURPOSE : if a battle's chunk0 ATEL uses HookStart/CTB-seed writes (FirstStrike, CurrentTurnDelay), decodes
//           them into typed EncounterOpenerWriteRow facts + honest Notes. Lazy + never throws on unsupported.
// WHY     : this is ENCOUNTER-driven truth (battle bin side), NOT monster-script truth; used to explain/support
//           scripted opener and CTB-seed battles (proof SEYMOUR_OPENER_CTB_SETUP_RESEARCH_RESULT_2026-06-30).
// EVIDENCE: battle-bin ATEL chunk0; read-only, dependency-light (consumable by lab headless).
// MAINT   : decode only the PROVEN patterns; unsupported => Notes, not exceptions/generalization.
// ============================================================================
// BattleEncounterOpener_File — read-only reader for encounter opener / CTB seed in chunk0.
//
// RE truth (SEYMOUR_OPENER_CTB_SETUP_RESEARCH_RESULT_2026-06-30.md):
//   mcyt06_00 uses ?StartEndHooks2::HookStart to seed the initial CTB state:
//     - AllMonsters.FirstStrike = 1
//     - AllMonsters.CurrentTurnDelay = 0
//     - Monster#01 [0x0015].CurrentTurnDelay = 1
//     - Character#1/#2/#3 and reserves CurrentTurnDelay += 2
//
// This is encounter-driven truth (battle-bin side), NOT monster-script truth.
// The reader is lazy, read-only, and never throws on unsupported patterns.
//
// Deliberately dependency-light (System.* + AiScript_File) so both the editor
// UI and the RT0 lab can consume it without pulling Avalonia.
//
// Design rules:
//   1. Reuse the chunk0 extraction pattern from BattleCameraScript_File.
//   2. Treat unsupported/open-ended cases as Notes, not exceptions.
//   3. Do NOT generalize beyond what the current proof supports.

using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.Battle
{
    /// <summary>One recognized write inside a HookStart / CTB seed sequence in chunk0.</summary>
    public sealed class EncounterOpenerWriteRow
    {
        /// <summary>Actor group targeted (e.g. "AllMonsters", "Monster#01", "Character#1", "Reserves").</summary>
        public required string TargetKind { get; init; }
        /// <summary>Raw actor-ref operand used by the ATEL write (e.g. 0x0015, 0xFFF1, 0xFFFA).</summary>
        public required ushort TargetOperand { get; init; }
        public required ushort FieldId { get; init; }
        /// <summary>Friendly field name from AiChrPropertyNames (e.g. "FirstStrike", "CurrentTurnDelay").</summary>
        public required string FieldName { get; init; }
        /// <summary>"set" for direct assignment, "add" for incremental (read + add + write).</summary>
        public required string Operation { get; init; }
        /// <summary>The literal value or delta.</summary>
        public required short Value { get; init; }
        /// <summary>Battle-bin absolute offset of the write instruction (chunk0 base + instruction offset).</summary>
        public required int SourceOffset { get; init; }
        public string TargetDisplay => $"{TargetKind} [0x{TargetOperand:X4}]";
    }

    /// <summary>Read-only encounter opener facts decoded from a battle bin's chunk0 (ATEL script).</summary>
    public sealed class BattleEncounterOpener_File
    {
        public required string BattleId { get; init; }
        public required int Chunk0Offset { get; init; }
        public required int Chunk0Length { get; init; }
        /// <summary>Recognized HookStart/CTB-seed writes. Empty when no proven pattern is found.</summary>
        public required IReadOnlyList<EncounterOpenerWriteRow> Writes { get; init; }
        /// <summary>Honest notes about decode limitations or unsupported patterns.</summary>
        public required IReadOnlyList<string> Notes { get; init; }
        /// <summary>Human-readable summary of the recognized seed, in pt-BR.</summary>
        public required string HumanSummary { get; init; }
        /// <summary>Reference to the proof document or lane.</summary>
        public required string ProofLane { get; init; }
        /// <summary>Writer readiness: always "read-only" / "writer still closed".</summary>
        public required string WriterPolicy { get; init; }

        public bool HasRecognizedSeed => Writes.Count > 0;

        static readonly string WriterPolicyReadOnly = "read-only — writer still closed";
        static readonly string ProofLaneSeymour = "SEYMOUR_OPENER_CTB_SETUP_RESEARCH_RESULT_2026-06-30 — encounter-driven, HookStart seed";

        static readonly string SummaryNoSeed = "Nenhum opener / seed de CTB reconhecivel foi encontrado no chunk0 desta batalha.";

        /// <summary>Decode the encounter opener from a whole per-battle bin. Never throws — surfaces
        /// unsupported/open-ended cases via Notes and an empty Writes list.</summary>
        public static BattleEncounterOpener_File ReadFromBattleBin(string battleId, byte[] battleBinBytes)
        {
            ArgumentNullException.ThrowIfNull(battleBinBytes);
            var notes = new List<string>();
            (int start, int length) = ExtractChunk0Span(battleBinBytes);

            if (start < 0)
            {
                notes.Add("chunk0 (script ATEL) ausente ou menor que um cabecalho minimo de AiFile.");
                return Empty(battleId, battleBinBytes, notes);
            }

            byte[] chunk0 = new byte[length];
            Array.Copy(battleBinBytes, start, chunk0, 0, length);

            AiScriptFile script;
            try { script = AiScript_File.Read(chunk0); }
            catch (Exception ex)
            {
                notes.Add($"chunk0 @ 0x{start:X} (len 0x{length:X}) nao decodificou como ATEL: {ex.Message}");
                return Empty(battleId, battleBinBytes, notes, start, length);
            }

            if (!script.HasScript)
            {
                notes.Add("chunk0 decodificou como ATEL, mas nao trouxe instrucoes.");
                return Empty(battleId, battleBinBytes, notes, start, length);
            }

            var writes = DetectWrites(script, start, notes);
            string summary = BuildSummary(writes, battleId);

            return new BattleEncounterOpener_File
            {
                BattleId = battleId,
                Chunk0Offset = start,
                Chunk0Length = length,
                Writes = writes,
                Notes = notes,
                HumanSummary = summary,
                ProofLane = writes.Count > 0 ? ProofLaneSeymour : "(no recognized seed)",
                WriterPolicy = WriterPolicyReadOnly,
            };
        }

        static BattleEncounterOpener_File Empty(string battleId, byte[] bin, List<string> notes, int start = -1, int length = 0) => new()
        {
            BattleId = battleId,
            Chunk0Offset = start,
            Chunk0Length = length,
            Writes = Array.Empty<EncounterOpenerWriteRow>(),
            Notes = notes,
            HumanSummary = SummaryNoSeed,
            ProofLane = "(no chunk0 / no ATEL decode)",
            WriterPolicy = WriterPolicyReadOnly,
        };

        // ── write detection ─────────────────────────────────────────────────────────────────────────────

        const ushort WriteChrPropertyFuncId = 0x7018;  // CALLPOPA writeChrProperty(actorRef, fieldId, value)
        const ushort ReadChrPropertyFuncId = 0x700F;   // CALL readChrProperty(actorRef, fieldId) → result
        const ushort FieldFirstStrike = 0x003B;
        const ushort FieldCurrentTurnDelay = 0x00E8;
        const ushort TargetSelf = 0xFFF3;
        const ushort TargetAllMonsters = 0xFFF1;
        const ushort TargetFrontlineChars = 0xFFF2;
        const ushort TargetCharacter1 = 0xFFFA;
        const ushort TargetCharacter2 = 0xFFF9;
        const ushort TargetCharacter3 = 0xFFF8;
        const ushort TargetReserve1 = 0xFFF7;
        const ushort TargetReserve2 = 0xFFF6;
        const ushort TargetReserve3 = 0xFFF5;
        const ushort TargetReserve4 = 0xFFF4;
        // Positive monster refs in the Seymour lane follow the repo's established encounter-slot naming:
        //   0x0014=Monster#00, 0x0015=Monster#01, 0x0016=Monster#02, 0x0017=Monster#03 ...
        // This matches the existing Seymour docs + dumps, and prevents mislabelling 0x0015 as a different slot.
        const ushort TargetMonster00 = 0x0014;
        const ushort TargetMonster01 = 0x0015;
        const ushort TargetMonster02 = 0x0016;
        const ushort TargetMonster03 = 0x0017;
        const ushort TargetMonster04 = 0x0018;
        const ushort TargetMonster05 = 0x0019;
        const ushort TargetMonster06 = 0x001A;
        const ushort TargetMonster07 = 0x001B;

        static IReadOnlyList<EncounterOpenerWriteRow> DetectWrites(AiScriptFile script, int chunk0Offset, List<string> notes)
        {
            var writes = new List<EncounterOpenerWriteRow>();
            IReadOnlyList<AiInstruction> ins = script.Instructions;

            for (int idx = 0; idx < ins.Count; idx++)
            {
                AiInstruction call = ins[idx];
                if (call.Opcode != 0xD8) continue;  // CALLPOPA only
                if (AiScript_File.OperandKindOf(call.Opcode) != AiOperandKind.FuncId) continue;
                if (call.Operand != WriteChrPropertyFuncId) continue;

                // Try direct write: writeChrProperty(actor, field, value) via 3 PUSHII.
                if (TryReadWriteChrPropertyDirect(ins, idx, out ushort actorRef, out ushort fieldId, out short value))
                {
                    if (IsOpenerRelevant(actorRef, fieldId))
                    {
                        writes.Add(new EncounterOpenerWriteRow
                        {
                            TargetKind = ClassifyTarget(actorRef),
                            TargetOperand = actorRef,
                            FieldId = fieldId,
                            FieldName = AiChrPropertyNames.Get(fieldId) ?? $"?0x{fieldId:X4}",
                            Operation = "set",
                            Value = value,
                            SourceOffset = chunk0Offset + ins[idx].Offset,
                        });
                    }
                }
                // Try RMW write (+= pattern): readChrProperty + ADD + writeChrProperty.
                else if (TryReadWriteChrPropertyRmw(ins, idx, out actorRef, out fieldId, out value))
                {
                    if (IsOpenerRelevant(actorRef, fieldId))
                    {
                        writes.Add(new EncounterOpenerWriteRow
                        {
                            TargetKind = ClassifyTarget(actorRef),
                            TargetOperand = actorRef,
                            FieldId = fieldId,
                            FieldName = AiChrPropertyNames.Get(fieldId) ?? $"?0x{fieldId:X4}",
                            Operation = "add",
                            Value = value,
                            SourceOffset = chunk0Offset + ins[idx].Offset,
                        });
                    }
                }
            }

            return writes;
        }

        /// <summary>Detect direct write: 3 PUSHII before CALLPOPA writeChrProperty.</summary>
        static bool TryReadWriteChrPropertyDirect(IReadOnlyList<AiInstruction> ins, int callIdx,
            out ushort actorRef, out ushort fieldId, out short value)
        {
            actorRef = 0; fieldId = 0; value = 0;
            if (callIdx < 3) return false;
            if (!IsPushIi(ins, callIdx - 1, out short val)) return false;
            if (!IsPushIi(ins, callIdx - 2, out short fid)) return false;
            if (!IsPushIi(ins, callIdx - 3, out short aref)) return false;
            value = val;
            fieldId = unchecked((ushort)fid);
            actorRef = unchecked((ushort)aref);
            return true;
        }

        /// <summary>Detect RMW write (+= pattern) used by mcyt06_00 party/reserve CurrentTurnDelay:
        /// PUSHII actor → PUSHII field → PUSHII actor → PUSHII field →
        /// readChrProperty (0xB5, operand 0x700F readChrProperty) → PUSHII delta → ADD →
        /// CALLPOPA writeChrProperty(actor, field, readResult+delta).
        ///
        /// We detect this by looking 8-9 instructions before the CALLPOPA for the
        /// readChrProperty (0xB5 with func=0x700F) and matching actor+field pairs.</summary>
        static bool TryReadWriteChrPropertyRmw(IReadOnlyList<AiInstruction> ins, int callIdx,
            out ushort actorRef, out ushort fieldId, out short delta)
        {
            actorRef = 0; fieldId = 0; delta = 0;
            if (callIdx < 6) return false;

            // Scan backward ~10 instructions for a readChrProperty (0xB5, operand 0x700F).
            int searchStart = Math.Max(0, callIdx - 12);
            for (int scan = callIdx - 1; scan >= searchStart; scan--)
            {
                AiInstruction candidate = ins[scan];
                if (candidate.Opcode != 0xB5) continue;
                if (AiScript_File.OperandKindOf(candidate.Opcode) != AiOperandKind.FuncId) continue;
                if (candidate.Operand != 0x700F) continue;  // readChrProperty

                // After readChrProperty, expect: PUSHII delta → ADD.
                // Then CALLPOPA writeChrProperty.
                // Verify the ADD is between read and write.
                bool foundAdd = false;
                bool foundDelta = false;
                short addDelta = 0;

                for (int mid = scan + 1; mid < callIdx; mid++)
                {
                    if (ins[mid].Opcode == 0x14) foundAdd = true;  // ADD
                    else if (IsPushIi(ins, mid, out short val)) { addDelta = val; foundDelta = true; }
                }

                if (!foundAdd || !foundDelta) continue;

                // Extract the actor+field pair from instructions BEFORE readChrProperty.
                // Expect: PUSHII actor → PUSHII field → PUSHII actor → PUSHII field → readChrProperty
                // The second pair maps to the write target.
                // Actually: readChrProperty pops its args from the stack, and the remaining
                // stack has [actor, field] for the write. The actor+field for write are the
                // first pushes (oldest on stack).
                // Pattern: PUSHII actor → PUSHII field → PUSHII actor → PUSHII field → read...
                //                                     └─── read args ───┘
                //            └─── write args (left on stack) ────────┘

                // Find the 4th PUSHII before readChrProperty.
                int pushCount = 0;
                ushort rmwActor = 0, rmwField = 0;
                for (int p = scan - 1; p >= Math.Max(0, scan - 5); p--)
                {
                    if (IsPushIi(ins, p, out short pushVal))
                    {
                        pushCount++;
                        if (pushCount == 4) rmwActor = unchecked((ushort)pushVal);  // 4th push = actor
                        else if (pushCount == 3) rmwField = unchecked((ushort)pushVal); // 3rd push = field
                    }
                }

                if (pushCount >= 4 && rmwActor != 0)
                {
                    // Verify field is CurrentTurnDelay (the RMW is specifically for delays)
                    if (rmwField != 0x00E8) continue;

                    actorRef = rmwActor;
                    fieldId = rmwField;
                    delta = addDelta;
                    return true;
                }

                // Also try the simpler pattern: just read the actor field from
                // the direct args of readChrProperty (2 PUSHII before it).
                if (scan >= 2 && IsPushIi(ins, scan - 1, out short f2) && IsPushIi(ins, scan - 2, out short a2))
                {
                    actorRef = unchecked((ushort)a2);
                    fieldId = unchecked((ushort)f2);
                    delta = addDelta;
                    return true;
                }
            }

            return false;
        }

        static bool IsPushIi(IReadOnlyList<AiInstruction> ins, int idx, out short value)
        {
            AiInstruction i = ins[idx];
            if (i.Opcode == 0xAE) // PUSHII
            {
                value = unchecked((short)i.Operand);
                return true;
            }
            value = 0;
            return false;
        }

        /// <summary>Returns true if this field+target combo is relevant to encounter opener / CTB seed.</summary>
        static bool IsOpenerRelevant(ushort actorRef, ushort fieldId)
        {
            // We care about: FirstStrike and CurrentTurnDelay writes targeting monster/character groups.
            if (fieldId != FieldFirstStrike && fieldId != FieldCurrentTurnDelay)
                return false;

            return actorRef switch
            {
                TargetAllMonsters => true,
                TargetCharacter1 or TargetCharacter2 or TargetCharacter3 => true,
                TargetReserve1 or TargetReserve2 or TargetReserve3 or TargetReserve4 => true,
                >= TargetMonster00 and <= TargetMonster07 => true,
                _ => false,
            };
        }

        static string ClassifyTarget(ushort operand)
        {
            return operand switch
            {
                TargetSelf => "Self",
                TargetAllMonsters => "AllMonsters",
                TargetFrontlineChars => "FrontlineChars",
                TargetCharacter1 => "Character#1",
                TargetCharacter2 => "Character#2",
                TargetCharacter3 => "Character#3",
                TargetReserve1 or TargetReserve2 or TargetReserve3 or TargetReserve4 => "Reserves",
                TargetMonster00 => "Monster#00",
                TargetMonster01 => "Monster#01",
                TargetMonster02 => "Monster#02",
                TargetMonster03 => "Monster#03",
                TargetMonster04 => "Monster#04",
                TargetMonster05 => "Monster#05",
                TargetMonster06 => "Monster#06",
                TargetMonster07 => "Monster#07",
                _ when operand >= TargetMonster00 && operand <= TargetMonster07 => $"Monster#{operand - TargetMonster00:00}",
                _ => $"target:0x{operand:X4}",
            };
        }

        static string BuildSummary(IReadOnlyList<EncounterOpenerWriteRow> writes, string battleId)
        {
            if (writes.Count == 0)
                return SummaryNoSeed;

            var parts = new List<string>();
            foreach (EncounterOpenerWriteRow w in writes)
            {
                string op = w.Operation == "add" ? "+=" : "=";
                parts.Add($"{w.TargetDisplay}.{w.FieldName} {op} {w.Value}");
            }

            string detail = string.Join("; ", parts);
            return $"{battleId}: opener / CTB seed reconhecido: {detail}. " +
                   "Isto e startup encounter-driven (HookStart), nao autoria local de CTB no monstro.";
        }

        // ── chunk table (mirrored from BattleCameraScript_File) ──────────────────────────────────────

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

            int end = -1;
            for (int j = wantIndex + 1; j <= chunkCount; j++)
                if (offsets[j] >= start) { end = offsets[j]; break; }
            if (end < 0 || end > bytes.Length) end = bytes.Length;

            int len = Math.Max(0, end - start);
            if (len < 0x34) return (-1, 0);
            return (start, len);
        }

        static int ReadInt32(byte[] b, int o) =>
            (o < 0 || o + 4 > b.Length) ? 0 : (b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }
}
