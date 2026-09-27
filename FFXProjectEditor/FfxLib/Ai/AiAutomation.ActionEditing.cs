using System;
using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    // F7.1: edicao de acoes (insert/move/change/remove) extraida do god file AiAutomation.cs.
    public static partial class AiAutomation
    {
public static List<AiInstruction> InstructionsWithout(AiScriptFile script, AiDetectedAction action)
        {
            var drop = new HashSet<int>(action.RemoveOffsets);
            return script.Instructions.Where(i => !drop.Contains(i.Offset)).ToList();
        }

        /// <summary>REORDER — move a detected action's triplet up (earlier) or down (later) past the ADJACENT detected
        /// action, returning the reordered instruction list. LENGTH-PRESERVING (same instructions, new order) — feed to
        /// AiScript_File.Rebuild (which remaps every jump/entrypoint by instruction identity, so control flow follows
        /// the moved block). Returns null when the action is at the edge or the neighbour is a non-self-contained
        /// (complex-target) action that can't be cleanly swapped. Only offered for self-contained (Removable) triplets.</summary>
        public static List<AiInstruction>? MoveActionInstructions(AiScriptFile script, AiDetectedAction action, bool up)
        {
            IReadOnlyList<AiDetectedAction> actions = DetectActions(script);   // stream order (ascending offset), all kinds
            int idx = -1;
            for (int i = 0; i < actions.Count; i++) if (actions[i].CallOffset == action.CallOffset) { idx = i; break; }
            if (idx < 0) return null;
            int nIdx = up ? idx - 1 : idx + 1;
            if (nIdx < 0 || nIdx >= actions.Count) return null;   // already first / last
            AiDetectedAction self = actions[idx], neighbour = actions[nIdx];
            if (!self.Removable || !neighbour.Removable) return null;   // need two clean self-contained triplets

            var list = script.Instructions.ToList();
            var tripletOffsets = new HashSet<int>(self.RemoveOffsets);
            List<AiInstruction> triplet = list.Where(i => tripletOffsets.Contains(i.Offset)).OrderBy(i => i.Offset).ToList();
            if (triplet.Count != self.RemoveOffsets.Count) return null;
            list.RemoveAll(i => tripletOffsets.Contains(i.Offset));

            // Re-insert at the neighbour's boundary: before its first instruction (up) / after its last = call (down).
            int anchorOffset = up ? neighbour.RemoveOffsets.Min() : neighbour.CallOffset;
            int at = list.FindIndex(i => i.Offset == anchorOffset);
            if (at < 0) return null;
            if (!up) at += 1;
            list.InsertRange(at, triplet);
            return list;
        }

        /// <summary>REORDER GROUP — move a consecutive group of self-contained detected actions up/down past one
        /// adjacent self-contained action. This is the safe core for the human multiselect UI: it moves real bytecode
        /// blocks as a group while preserving the selected actions' internal order. Returns null if the group is not
        /// contiguous in real script order, any block is complex/non-removable, or the edge neighbour is unsafe.</summary>
        public static List<AiInstruction>? MoveActionGroupInstructions(
            AiScriptFile script,
            IReadOnlyList<AiDetectedAction> selectedActions,
            bool up)
        {
            if (selectedActions.Count == 0) return null;

            IReadOnlyList<AiDetectedAction> actions = DetectActions(script);
            var selectedCalls = new HashSet<int>(selectedActions.Select(a => a.CallOffset));
            var selectedIndexes = new List<int>();
            for (int i = 0; i < actions.Count; i++)
                if (selectedCalls.Contains(actions[i].CallOffset))
                    selectedIndexes.Add(i);

            if (selectedIndexes.Count != selectedCalls.Count) return null;
            selectedIndexes.Sort();
            int first = selectedIndexes[0], last = selectedIndexes[^1];
            if (last - first + 1 != selectedIndexes.Count) return null;

            var movingActions = selectedIndexes.Select(i => actions[i]).ToList();
            if (movingActions.Any(a => !a.Removable)) return null;

            int neighbourIndex = up ? first - 1 : last + 1;
            if (neighbourIndex < 0 || neighbourIndex >= actions.Count) return null;
            AiDetectedAction neighbour = actions[neighbourIndex];
            if (!neighbour.Removable) return null;

            var movingOffsets = new HashSet<int>();
            int expectedOffsetCount = 0;
            foreach (AiDetectedAction action in movingActions)
            {
                expectedOffsetCount += action.RemoveOffsets.Count;
                foreach (int offset in action.RemoveOffsets)
                    movingOffsets.Add(offset);
            }
            if (movingOffsets.Count != expectedOffsetCount) return null;

            List<AiInstruction> movingBlock = script.Instructions
                .Where(i => movingOffsets.Contains(i.Offset))
                .OrderBy(i => i.Offset)
                .ToList();
            if (movingBlock.Count != movingOffsets.Count) return null;

            List<AiInstruction> list = script.Instructions
                .Where(i => !movingOffsets.Contains(i.Offset))
                .ToList();

            int anchorOffset = up ? neighbour.RemoveOffsets.Min() : neighbour.RemoveOffsets.Max();
            int at = list.FindIndex(i => i.Offset == anchorOffset);
            if (at < 0) return null;
            if (!up) at += 1;
            list.InsertRange(at, movingBlock);
            return list;
        }

        /// <summary>CHANGE ABILITY — rewrite the command id of a detected action's PUSHII to a new (cat&lt;&lt;12)|id
        /// operand (e.g. "Fire" -> "Firaga", or a MonMagic2 Multi-Fira). LENGTH-PRESERVING (one operand changes) — this
        /// is the same single-operand edit proven RT2-live (Firaga 0x3049 -> Thundaga 0x304B). Returns the new
        /// instruction list for AiScript_File.Rebuild; null if the command push can't be located.</summary>
        public static List<AiInstruction>? ChangeActionInstructions(AiScriptFile script, AiDetectedAction action, ushort newCommandOperand)
        {
            var list = new List<AiInstruction>(script.Instructions.Count);
            bool changed = false;
            foreach (AiInstruction ins in script.Instructions)
            {
                if (ins.Offset == action.CmdPushOffset)
                {
                    list.Add(new AiInstruction
                    {
                        Offset = ins.Offset, Opcode = ins.Opcode, HasOperand = ins.HasOperand,
                        Operand = newCommandOperand, OperandKind = ins.OperandKind,
                    });
                    changed = true;
                }
                else list.Add(ins);
            }
            return changed ? list : null;
        }

        /// <summary>Change a command operand and insert a direct status write immediately after that same command.
        /// This is the "Trocar selecionada + Forbidden Rite vinculado" path.</summary>
        public static List<AiInstruction>? ChangeActionAndInsertChrPropertyWrite(AiScriptFile script, AiDetectedAction action,
            ushort newCommandOperand, ushort targetOperand, ushort fieldId, ushort value)
        {
            if (action.Kind != AiActionKind.Command || action.CmdPushOffset < 0)
                return null;

            List<AiInstruction> status = BuildChrPropertyWriteAction(targetOperand, fieldId, value);
            var list = new List<AiInstruction>(script.Instructions.Count + status.Count);
            bool changed = false, inserted = false;
            foreach (AiInstruction ins in script.Instructions)
            {
                if (ins.Offset == action.CmdPushOffset)
                {
                    list.Add(new AiInstruction
                    {
                        Offset = ins.Offset,
                        Opcode = ins.Opcode,
                        HasOperand = ins.HasOperand,
                        Operand = newCommandOperand,
                        OperandKind = ins.OperandKind,
                    });
                    changed = true;
                }
                else list.Add(ins);

                if (ins.Offset == action.CallOffset)
                {
                    list.AddRange(status);
                    inserted = true;
                }
            }
            return changed && inserted ? list : null;
        }

        public static List<AiInstruction>? ChangeActionAndInsertChrPropertyWriteUsingActionTarget(
            AiScriptFile script, AiDetectedAction action, ushort newCommandOperand, ushort fieldId, ushort value)
        {
            AiInstruction? target = TargetPushInstruction(script, action);
            if (target == null) return null;
            if (action.Kind != AiActionKind.Command) return null;
            var status = BuildChrPropertyWriteAction(target, fieldId, value);
            var list = new List<AiInstruction>(script.Instructions.Count + status.Count);
            bool changed = false, inserted = false;
            foreach (AiInstruction ins in script.Instructions)
            {
                if (ins.Offset == action.CmdPushOffset)
                {
                    list.Add(new AiInstruction
                    {
                        Offset = ins.Offset,
                        Opcode = ins.Opcode,
                        HasOperand = true,
                        Operand = newCommandOperand,
                        OperandKind = ins.OperandKind,
                    });
                    changed = true;
                }
                else list.Add(ins);

                if (ins.Offset == action.CallOffset)
                {
                    list.AddRange(status);
                    inserted = true;
                }
            }
            return changed && inserted ? list : null;
        }

        /// <summary>CHANGE TARGET (#8) — rewrite the TARGET sentinel of a command action's target push to a new literal
        /// (e.g. self 0xFFF3, or a target value copied-by-example from another action of the same monster). This is the
        /// SAME length-preserving single-operand edit proven RT2-live, just on the target slot instead of the command
        /// slot. Works for both PUSHII literals AND computed single-push targets (PUSHV/PUSHF/0x9F) — all are 3 bytes,
        /// so we force the slot to PUSHII + newTargetOperand. Only self (0xFFF3) is byte-proven; other sentinels are
        /// corpus-consistent — caller must flag RT2.</summary>
        public static List<AiInstruction>? ChangeTargetInstructions(AiScriptFile script, AiDetectedAction action, ushort newTargetOperand)
        {
            if (action.Kind != AiActionKind.Command || action.TargetPushOffset < 0) return null;
            var list = new List<AiInstruction>(script.Instructions.Count);
            bool changed = false;
            int? linkedStatusTargetOffset = FindImmediateLinkedStatusTargetOffset(script, action);
            foreach (AiInstruction ins in script.Instructions)
            {
                if (ins.Offset == action.TargetPushOffset)
                {
                    list.Add(new AiInstruction
                    {
                        Offset = ins.Offset, Opcode = PUSHII, HasOperand = true,
                        Operand = newTargetOperand, OperandKind = ins.OperandKind,
                    });
                    changed = true;
                }
                else if (linkedStatusTargetOffset.HasValue && ins.Offset == linkedStatusTargetOffset.Value)
                {
                    list.Add(new AiInstruction
                    {
                        Offset = ins.Offset, Opcode = PUSHII, HasOperand = true,
                        Operand = newTargetOperand, OperandKind = ins.OperandKind,
                    });
                }
                else list.Add(ins);
            }
            return changed ? list : null;
        }

        static int? FindImmediateLinkedStatusTargetOffset(AiScriptFile script, AiDetectedAction action)
        {
            if (action.Kind != AiActionKind.Command || !action.TargetIsLiteral) return null;
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            int callIndex = -1;
            for (int i = 0; i < ins.Count; i++)
            {
                if (ins[i].Offset == action.CallOffset)
                {
                    callIndex = i;
                    break;
                }
            }
            if (callIndex < 0 || callIndex + 4 >= ins.Count) return null;

            AiInstruction chr = ins[callIndex + 1];
            AiInstruction field = ins[callIndex + 2];
            AiInstruction val = ins[callIndex + 3];
            AiInstruction call = ins[callIndex + 4];
            if (chr.Opcode != PUSHII || chr.Operand != action.TargetOperand) return null;
            if (!SinglePush.Contains(field.Opcode) || !SinglePush.Contains(val.Opcode)) return null;
            if (call.Opcode != CALLPOPA || call.Operand != WriteChrProperty) return null;
            return chr.Offset;
        }

        static bool IsPerformCommandCallOperand(ushort operand) =>
            operand == PerformCommand || operand == ForcePerformCommand;

        static List<AiInstruction>? BuildCopiedCommandActionUsingActionTarget(
            AiScriptFile script,
            AiDetectedAction action,
            ushort commandOperand)
        {
            if (action.Kind != AiActionKind.Command || !AiCommandId.IsCommandOperand(commandOperand))
                return null;

            AiInstruction? target = TargetPushInstruction(script, action);
            if (target == null || !SinglePush.Contains(target.Opcode) || !IsPerformCommandCallOperand(action.PerformOperand))
                return null;

            return new List<AiInstruction>
            {
                CloneForInsert(target),
                Op(PUSHII, commandOperand),
                Op(CALLPOPA, action.PerformOperand),
            };
        }

        /// <summary>Append only a writeChrProperty body after the selected command action. This is the route-bundle
        /// path for "Forbidden Rite nesta rota" when the user picked a literal battle target explicitly.</summary>
        public static List<AiInstruction>? InsertChrPropertyWriteAfterAction(
            AiScriptFile script,
            AiDetectedAction action,
            ushort targetOperand,
            ushort fieldId,
            ushort value)
        {
            if (action.Kind != AiActionKind.Command || !IsPerformCommandCallOperand(action.PerformOperand))
                return null;

            return InsertBodyAfterActionInstructions(
                script,
                action,
                BuildChrPropertyWriteAction(targetOperand, fieldId, value));
        }

        /// <summary>Append only a writeChrProperty body after the selected command action, cloning the real target
        /// push used by that action (including PUSHV indirect targets). This is the safe path for Seymour-style
        /// route bundles where the consumer target already lives in a slot var.</summary>
        public static List<AiInstruction>? InsertChrPropertyWriteAfterActionUsingActionTarget(
            AiScriptFile script,
            AiDetectedAction action,
            ushort fieldId,
            ushort value)
        {
            if (action.Kind != AiActionKind.Command)
                return null;

            AiInstruction? target = TargetPushInstruction(script, action);
            if (target == null)
                return null;

            return InsertBodyAfterActionInstructions(
                script,
                action,
                BuildChrPropertyWriteAction(target, fieldId, value));
        }

        /// <summary>Insert one extra command right after the selected action, cloning the action's real target push
        /// and preserving perform/forcePerform mode. Unlike InsertSecondCommand, this path does NOT require a
        /// removable triplet and is meant for indirect consumers discovered through PUSHV command slots.</summary>
        public static List<AiInstruction>? InsertSecondCommandAfterActionUsingActionTarget(
            AiScriptFile script,
            AiDetectedAction action,
            ushort newCommandOperand)
        {
            List<AiInstruction>? body = BuildCopiedCommandActionUsingActionTarget(script, action, newCommandOperand);
            return body == null ? null : InsertBodyAfterActionInstructions(script, action, body);
        }

        /// <summary>MULTI-CAST / second ability (#9) — insert a NEW command triplet right after a detected command
        /// action, cloning its target + perform mode but with a DIFFERENT command id. Result: the monster casts the
        /// original action and then the chosen ability back-to-back (Seymour-style multi-action). GROWS by the triplet;
        /// feed to AiScript_File.Rebuild. Only offered for self-contained (Removable) command actions. The clones get
        /// Offset=-1 so Rebuild treats them as fresh code. In-game whether BOTH resolve in one turn is RT2.</summary>
        public static List<AiInstruction>? InsertSecondCommand(AiScriptFile script, AiDetectedAction action, ushort newCommandOperand)
        {
            if (action.Kind != AiActionKind.Command || !action.Removable) return null;
            // The triplet is [target][command][call]; clone it but rewrite the command-id push to newCommandOperand.
            List<AiInstruction>? src = BuildCopiedCommandAction(script, action, newCommandOperand);
            if (src == null) return null;
            return InsertBodyAfterActionInstructions(script, action, src);
        }

        /// <summary>Insert a second command followed by a direct status write. The status write is linked to the
        /// inserted command, not to the original one.</summary>
        public static List<AiInstruction>? InsertSecondCommandWithChrPropertyWrite(AiScriptFile script, AiDetectedAction action,
            ushort newCommandOperand, ushort targetOperand, ushort fieldId, ushort value)
        {
            if (action.Kind != AiActionKind.Command || !action.Removable) return null;
            List<AiInstruction>? body = BuildCopiedCommandAction(script, action, newCommandOperand);
            if (body == null) return null;
            body.AddRange(BuildChrPropertyWriteAction(targetOperand, fieldId, value));
            return InsertBodyAfterActionInstructions(script, action, body);
        }

        public static List<AiInstruction>? InsertSecondCommandWithLinkedChrPropertyWrite(AiScriptFile script, AiDetectedAction action,
            ushort newCommandOperand, ushort fieldId, ushort value)
        {
            if (action.Kind != AiActionKind.Command || !action.Removable) return null;
            List<AiInstruction>? body = BuildCopiedCommandAction(script, action, newCommandOperand);
            if (body == null) return null;
            AiInstruction? target = TargetPushFromCommandBody(body);
            if (target == null) return null;
            body.AddRange(BuildChrPropertyWriteAction(target, fieldId, value));
            return InsertBodyAfterActionInstructions(script, action, body);
        }

        /// <summary>Insert N copied commands after an existing command action, preserving target + perform mode.
        /// This is the human "Passo 1 -> Passo 2 -> Passo 3..." path. The first step is the real action already in
        /// the script; operands are the extra steps appended after it.</summary>
        public static List<AiInstruction>? InsertCommandSequence(AiScriptFile script, AiDetectedAction action,
            IReadOnlyList<ushort> commandOperands, bool stopAfterSequence = false)
        {
            if (action.Kind != AiActionKind.Command || !action.Removable || commandOperands.Count == 0) return null;
            List<AiInstruction>? body = BuildCopiedCommandSequenceBody(script, action, commandOperands);
            if (body != null && stopAfterSequence)
                body.Add(RetInstruction());
            return body == null ? null : InsertBodyAfterActionInstructions(script, action, body);
        }

        public static List<AiInstruction>? InsertCommandSequenceWithChrPropertyWrite(AiScriptFile script, AiDetectedAction action,
            IReadOnlyList<ushort> commandOperands, ushort targetOperand, ushort fieldId, ushort value, bool afterEachStep, bool stopAfterSequence = false)
        {
            if (action.Kind != AiActionKind.Command || !action.Removable || commandOperands.Count == 0) return null;
            List<AiInstruction>? body = BuildCopiedCommandSequenceBody(script, action, commandOperands, commandBody =>
                BuildChrPropertyWriteAction(targetOperand, fieldId, value), afterEachStep);
            if (body == null) return null;
            if (!afterEachStep)
                body.AddRange(BuildChrPropertyWriteAction(targetOperand, fieldId, value));
            if (stopAfterSequence)
                body.Add(RetInstruction());
            return InsertBodyAfterActionInstructions(script, action, body);
        }

        public static List<AiInstruction>? InsertCommandSequenceWithLinkedChrPropertyWrite(AiScriptFile script, AiDetectedAction action,
            IReadOnlyList<ushort> commandOperands, ushort fieldId, ushort value, bool afterEachStep, bool stopAfterSequence = false)
        {
            if (action.Kind != AiActionKind.Command || !action.Removable || commandOperands.Count == 0) return null;
            AiInstruction? originalTarget = TargetPushInstruction(script, action);
            if (originalTarget == null) return null;

            List<AiInstruction>? body = BuildCopiedCommandSequenceBody(script, action, commandOperands, commandBody =>
            {
                AiInstruction? target = TargetPushFromCommandBody(commandBody);
                return target == null ? null : BuildChrPropertyWriteAction(target, fieldId, value);
            }, afterEachStep, BuildChrPropertyWriteAction(originalTarget, fieldId, value));
            if (body == null) return null;
            if (!afterEachStep)
            {
                AiInstruction? target = LastCommandTargetFromBody(body);
                if (target == null) return null;
                body.AddRange(BuildChrPropertyWriteAction(target, fieldId, value));
            }
            if (stopAfterSequence)
                body.Add(RetInstruction());
            return InsertBodyAfterActionInstructions(script, action, body);
        }

        static AiInstruction RetInstruction() => new()
        {
            Offset = -1,
            Opcode = 0x3C,
            HasOperand = false,
            Operand = 0,
            OperandKind = AiScript_File.OperandKindOf(0x3C),
        };

        static List<AiInstruction>? BuildCopiedCommandSequenceBody(
            AiScriptFile script,
            AiDetectedAction action,
            IReadOnlyList<ushort> commandOperands,
            Func<IReadOnlyList<AiInstruction>, List<AiInstruction>?>? linkedStatusFactory = null,
            bool afterEachStep = false,
            IReadOnlyList<AiInstruction>? statusAfterOriginalAction = null)
        {
            var body = new List<AiInstruction>();
            if (afterEachStep && statusAfterOriginalAction != null)
                body.AddRange(statusAfterOriginalAction);

            foreach (ushort op in commandOperands)
            {
                List<AiInstruction>? command = BuildCopiedCommandAction(script, action, op);
                if (command == null) return null;
                body.AddRange(command);
                if (afterEachStep && linkedStatusFactory != null)
                {
                    List<AiInstruction>? status = linkedStatusFactory(command);
                    if (status == null) return null;
                    body.AddRange(status);
                }
            }
            return body;
        }

        static AiInstruction? LastCommandTargetFromBody(IReadOnlyList<AiInstruction> body)
        {
            for (int i = body.Count - 1; i >= 0; i--)
            {
                if (body[i].Opcode == CALLPOPA && (body[i].Operand == PerformCommand || body[i].Operand == ForcePerformCommand))
                    return i >= 2 && SinglePush.Contains(body[i - 2].Opcode) ? body[i - 2] : null;
            }
            return null;
        }

        /// <summary>LOCAL ADD / "Talvez" on a selected action. This is the human-editor path when the user selected
        /// a concrete command card: insert the new command right after that card. Without chance, this is the same
        /// direct sequence as InsertSecondCommand. With chance, it inserts:
        ///   [guard]
        ///   D7 POPXNCJMP -> old-next-instruction
        ///   [copied command action]
        /// so the random/conditional branch wraps ONLY the new action and does not repoint the worker entrypoint.
        /// This avoids the old "whole handler looks reorganized" wrapper produced by AppendGuardedAction.</summary>
        public static byte[] InsertCommandAfterAction(AiScriptFile script, AiDetectedAction action, ushort newCommandOperand,
            bool random, int k, bool stopAfterAction = false)
        {
            ArgumentNullException.ThrowIfNull(script);
            if (action.Kind != AiActionKind.Command || !action.Removable)
                throw new InvalidOperationException("select a simple command action to use as template.");

            List<AiInstruction> body = BuildCopiedCommandAction(script, action, newCommandOperand)
                ?? throw new InvalidOperationException("could not copy target/mode of the selected action.");
            return InsertBodyAfterAction(script, action, body, random, k, stopAfterAction);
        }

        /// <summary>LOCAL ADD with linked Forbidden Rite. The command and the status write live inside the same local
        /// insertion/chance wrapper, so "Talvez 1/2" applies to the pair, not to the whole worker.</summary>
        public static byte[] InsertCommandAfterActionWithChrPropertyWrite(AiScriptFile script, AiDetectedAction action,
            ushort newCommandOperand, ushort targetOperand, ushort fieldId, ushort value, bool random, int k, bool stopAfterAction = false)
        {
            ArgumentNullException.ThrowIfNull(script);
            if (action.Kind != AiActionKind.Command || !action.Removable)
                throw new InvalidOperationException("select a simple command action to use as template.");

            List<AiInstruction> body = BuildCopiedCommandAction(script, action, newCommandOperand)
                ?? throw new InvalidOperationException("could not copy target/mode of the selected action.");
            body.AddRange(BuildChrPropertyWriteAction(targetOperand, fieldId, value));
            return InsertBodyAfterAction(script, action, body, random, k, stopAfterAction);
        }

        public static byte[] InsertCommandAfterActionWithLinkedChrPropertyWrite(AiScriptFile script, AiDetectedAction action,
            ushort newCommandOperand, ushort fieldId, ushort value, bool random, int k, bool stopAfterAction = false)
        {
            ArgumentNullException.ThrowIfNull(script);
            if (action.Kind != AiActionKind.Command || !action.Removable)
                throw new InvalidOperationException("select a simple command action to use as template.");

            List<AiInstruction> body = BuildCopiedCommandAction(script, action, newCommandOperand)
                ?? throw new InvalidOperationException("could not copy target/mode of the selected action.");
            AiInstruction? target = TargetPushFromCommandBody(body);
            if (target == null)
                throw new InvalidOperationException("I could not reuse the target of the selected action.");
            body.AddRange(BuildChrPropertyWriteAction(target, fieldId, value));
            return InsertBodyAfterAction(script, action, body, random, k, stopAfterAction);
        }

        static AiInstruction? TargetPushInstruction(AiScriptFile script, AiDetectedAction action)
        {
            if (action.Kind != AiActionKind.Command || action.TargetPushOffset < 0) return null;
            AiInstruction? target = script.Instructions.FirstOrDefault(i => i.Offset == action.TargetPushOffset);
            return target != null && SinglePush.Contains(target.Opcode) ? target : null;
        }

        static AiInstruction? TargetPushFromCommandBody(IReadOnlyList<AiInstruction> body)
        {
            int callIndex = -1;
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i].Opcode == CALLPOPA && (body[i].Operand == PerformCommand || body[i].Operand == ForcePerformCommand))
                {
                    callIndex = i;
                    break;
                }
            }
            return callIndex >= 2 && SinglePush.Contains(body[callIndex - 2].Opcode) ? body[callIndex - 2] : null;
        }

        static List<AiInstruction>? InsertBodyAfterActionInstructions(AiScriptFile script, AiDetectedAction action,
            IReadOnlyList<AiInstruction> body)
        {
            if (body.Count == 0) return null;
            var list = new List<AiInstruction>(script.Instructions.Count + body.Count);
            bool inserted = false;
            foreach (AiInstruction ins in script.Instructions)
            {
                list.Add(ins);
                if (ins.Offset == action.CallOffset)
                {
                    foreach (AiInstruction s in body)
                        list.Add(s);
                    inserted = true;
                }
            }
            return inserted ? list : null;
        }

        static byte[] InsertBodyAfterAction(AiScriptFile script, AiDetectedAction action,
            IReadOnlyList<AiInstruction> body, bool random, int k, bool stopAfterAction = false)
        {
            if (!random)
            {
                IReadOnlyList<AiInstruction> directBody = body;
                if (stopAfterAction)
                {
                    var bodyAndStop = new List<AiInstruction>(body.Count + 1);
                    bodyAndStop.AddRange(body);
                    bodyAndStop.Add(RetInstruction());
                    directBody = bodyAndStop;
                }

                List<AiInstruction>? direct = InsertBodyAfterActionInstructions(script, action, directBody);
                if (direct == null)
                    throw new InvalidOperationException(Strings.F2_could_not_find_the_insertion_point_for_t_bcf4fe04);
                return AiScript_File.Rebuild(script, direct);
            }

            if (action.WorkerIndex < 0 || action.WorkerIndex >= script.Workers.Count)
                throw new InvalidOperationException(Strings.F2_could_not_identify_the_worker_that_owns__641329ad);
            AiInstruction call = script.Instructions.FirstOrDefault(i => i.Offset == action.CallOffset)
                ?? throw new InvalidOperationException(Strings.F2_could_not_locate_the_call_for_the_select_30bb97f3);
            List<AiInstruction> guard = RngGuard(k);

            AiWorker owner = script.Workers[action.WorkerIndex];
            int slotSkip = owner.JumpTargets.Count;
            if (slotSkip > ushort.MaxValue)
                throw new InvalidOperationException("The jump table for this worker already exceeds the u16 index limit.");

            int insertionRel = action.CallOffset + call.Length - script.ScriptStart;
            int insertedLen = guard.Sum(i => i.Length) + 3 + body.Sum(i => i.Length) + (stopAfterAction ? 1 : 0);
            int skipRel = insertionRel + insertedLen;

            AiInstruction SkipIfFalse() => new AiInstruction
            {
                Offset = -1,
                Opcode = 0xD7,
                HasOperand = true,
                Operand = (ushort)slotSkip,
                OperandKind = AiOperandKind.JumpIndex,
            };

            bool inserted = false;
            var list = new List<AiInstruction>(script.Instructions.Count + guard.Count + 1 + body.Count);
            foreach (AiInstruction ins in script.Instructions)
            {
                list.Add(ins);
                if (ins.Offset == action.CallOffset)
                {
                    list.AddRange(guard);
                    list.Add(SkipIfFalse());
                    list.AddRange(body);
                    if (stopAfterAction) list.Add(RetInstruction());
                    inserted = true;
                }
            }
            if (!inserted)
                throw new InvalidOperationException(Strings.F2_could_not_find_the_insertion_point_for_t_bcf4fe04);

            byte[] rebuilt = AiScript_File.Rebuild(script, list);
            return AiScript_File.GrowWorkerJumpTable(AiScript_File.Read(rebuilt), action.WorkerIndex, new[] { skipRel });
        }

        /// <summary>DUPLICATE — insert a copy of a detected action's triplet immediately after it (a crude "cast twice"
        /// / multi-cast by repetition). GROWS by the triplet's bytes; feed to AiScript_File.Rebuild. Only offered for
        /// self-contained (Removable) triplets. The clones get Offset=-1 so Rebuild treats them as fresh code.</summary>
        public static List<AiInstruction>? DuplicateActionInstructions(AiScriptFile script, AiDetectedAction action)
        {
            if (!action.Removable) return null;
            var src = action.RemoveOffsets.OrderBy(o => o)
                .Select(off => script.Instructions.First(i => i.Offset == off)).ToList();
            var list = new List<AiInstruction>(script.Instructions.Count + src.Count);
            foreach (AiInstruction ins in script.Instructions)
            {
                list.Add(ins);
                if (ins.Offset == action.CallOffset)
                    foreach (AiInstruction s in src)
                        list.Add(new AiInstruction { Offset = -1, Opcode = s.Opcode, HasOperand = s.HasOperand, Operand = s.Operand, OperandKind = s.OperandKind });
            }
            return list;
        }

        /// <summary>MOVE TO HOOK — remove a self-contained action from its current location, then append the same
        /// statement as an always-guarded action at the chosen worker/entrypoint. This is the "move this action to
        /// MotionHandler/etc." expert path: it validates the removal first, then uses the same grow-aware guarded
        /// append path as the Behavior Library. Returns a complete rebuilt AiFile.</summary>
        public static byte[] MoveActionToHook(AiScriptFile script, AiDetectedAction action, int workerIndex, int entrypointIndex)
        {
            if (!action.Removable)
                throw new InvalidOperationException(Strings.F2_this_action_is_not_a_self_contained_bloc_e2d69daa);
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));

            var actionBody = action.RemoveOffsets.OrderBy(o => o)
                .Select(off => script.Instructions.First(i => i.Offset == off))
                .Select(s => new AiInstruction { Offset = -1, Opcode = s.Opcode, HasOperand = s.HasOperand, Operand = s.Operand, OperandKind = s.OperandKind })
                .ToList();

            List<AiInstruction> kept = InstructionsWithout(script, action);
            AiValidationReport removalCheck = AiValidator.Validate(script, kept);
            if (!removalCheck.IsValid)
                throw new InvalidOperationException(removalCheck.Errors.FirstOrDefault()?.Message ?? "invalid structural removal.");

            byte[] withoutAction = AiScript_File.Rebuild(script, kept);
            AiScriptFile compact = AiScript_File.Read(withoutAction);
            if (workerIndex >= compact.Workers.Count)
                throw new InvalidOperationException("Target worker does not exist after the rebuild.");
            if (entrypointIndex < 0 || entrypointIndex >= compact.Workers[workerIndex].Entrypoints.Count)
                throw new InvalidOperationException("Target entrypoint does not exist after the rebuild.");

            return AiScript_File.AppendGuardedAction(compact, workerIndex, entrypointIndex, AlwaysGuard(), actionBody);
        }

        /// <summary>TOGGLE FORCE — flip a detected action's call between forcePerformCommand(705A) and the queued
        /// performCommand(700B). LENGTH-PRESERVING (one operand on the CALLPOPA). Feed to Rebuild.</summary>
        public static List<AiInstruction>? ToggleForceInstructions(AiScriptFile script, AiDetectedAction action)
        {
            ushort newCall = action.ForcePerform ? PerformCommand : ForcePerformCommand;
            var list = new List<AiInstruction>(script.Instructions.Count);
            bool changed = false;
            foreach (AiInstruction ins in script.Instructions)
            {
                if (ins.Offset == action.CallOffset)
                {
                    list.Add(new AiInstruction { Offset = ins.Offset, Opcode = ins.Opcode, HasOperand = ins.HasOperand, Operand = newCall, OperandKind = ins.OperandKind });
                    changed = true;
                }
                else list.Add(ins);
            }
            return changed ? list : null;
        }

        /// <summary>SELF-BUFF — append a guarded writeChrProperty(self, field, 1) to an explicit hook.</summary>
        public static byte[] AddSelfBuff(AiScriptFile script, ushort fieldId, bool random, int k, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            List<AiInstruction> guard = random ? RngGuard(k) : AlwaysGuard();
            var action = new List<AiInstruction> { Op(PUSHII, SelfTarget), Op(PUSHII, fieldId), Op(PUSHII, 1), Op(CALLPOPA, WriteChrProperty) };
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex, guard, action, stopAfterAction);
        }

        /// <summary>LAB DIRECT STATUS — append a guarded writeChrProperty(target, field, value). This is the actor-ref
        /// shape (7018), intentionally separate from setStatField(70AB), so anti-Ribbon experiments cannot reuse the
        /// wrong Shiva/Overdrive recipe. Structural proof only; gameplay effect, resistance bypass and target semantics
        /// remain RT2 until confirmed in-game.</summary>
        public static byte[] AddChrPropertyWrite(AiScriptFile script, ushort targetOperand, ushort fieldId, ushort value,
            bool random, int k, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            List<AiInstruction> guard = random ? RngGuard(k) : AlwaysGuard();
            var action = new List<AiInstruction>
            {
                Op(PUSHII, targetOperand),
                Op(PUSHII, fieldId),
                Op(PUSHII, value),
                Op(CALLPOPA, WriteChrProperty),
            };
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex, guard, action, stopAfterAction);
        }

        /// <summary>Append one guarded action that writes several actor properties in sequence. Used by the human
        /// YUNALESCA surface for direct status bundles: the condition runs once, then every selected status is applied.
        /// Structure is the proven writeChrProperty shape; trigger semantics remain caller-labelled.</summary>
        public static byte[] AddChrPropertyWritesWithGuard(AiScriptFile script, ushort targetOperand, ushort value,
            IReadOnlyList<ushort> fieldIds, IReadOnlyList<AiInstruction> guard, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (fieldIds == null || fieldIds.Count == 0)
                throw new ArgumentException(Strings.F2_please_provide_at_least_one_field_status_76610c80, nameof(fieldIds));

            var action = new List<AiInstruction>(fieldIds.Count * 4);
            foreach (ushort fieldId in fieldIds)
                action.AddRange(BuildChrPropertyWriteAction(targetOperand, fieldId, value));
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        /// <summary>FIELD/STAT PLANT — append a guarded setStatField(field, value). No actor ref: this is the
        /// 70AB context/stat descriptor shape, intentionally separate from actor-explicit writeChrProperty(7018).</summary>
        public static byte[] AddSetStatField(AiScriptFile script, ushort fieldId, ushort value, bool random, int k, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            List<AiInstruction> guard = random ? RngGuard(k) : AlwaysGuard();
            var action = new List<AiInstruction> { Op(PUSHII, fieldId), Op(PUSHII, value), Op(CALLPOPA, SetStatField) };
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex, guard, action, stopAfterAction);
        }

        /// <summary>Append one guarded action that writes several setStatField(field,value) pairs in sequence.
        /// This keeps multi-field actor setup, such as the Overdrive gauge, under one condition and one hook.</summary>
        public static byte[] AddSetStatFieldsWithGuard(AiScriptFile script,
            IReadOnlyList<(ushort FieldId, ushort Value)> writes, IReadOnlyList<AiInstruction> guard,
            int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (writes == null || writes.Count == 0)
                throw new ArgumentException(Strings.F2_please_provide_at_least_one_field_stat_b6881322, nameof(writes));

            var action = new List<AiInstruction>(writes.Count * 3);
            foreach ((ushort fieldId, ushort value) in writes)
                action.AddRange(BuildSetStatFieldAction(fieldId, value));
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        /// <summary>Append one guarded action that writes several writeChrProperty(target, field, value) pairs in sequence,
        /// allowing each field to carry a different value.</summary>
        public static byte[] AddChrPropertyValueWritesWithGuard(AiScriptFile script, ushort targetOperand,
            IReadOnlyList<(ushort FieldId, ushort Value)> writes, IReadOnlyList<AiInstruction> guard,
            int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (writes == null || writes.Count == 0)
                throw new ArgumentException(Strings.F2_please_provide_at_least_one_field_status_76610c80, nameof(writes));

            var action = new List<AiInstruction>(writes.Count * 4);
            foreach ((ushort fieldId, ushort value) in writes)
                action.AddRange(BuildChrPropertyWriteAction(targetOperand, fieldId, value));
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        /// <summary>LAB Overdrive gauge setup: show bar + max/current, optionally mode. Uses the corpus-backed
        /// writeChrProperty(Self, field, value) shape seen in Aeon/Dark Aeon scripts. Structural writer only;
        /// visible gauge and gameplay semantics remain RT2 until tested on the chosen monster.</summary>
        public static byte[] AddOverdriveGaugeSetup(AiScriptFile script, ushort max, ushort current, bool setMode,
            ushort mode, int workerIndex, int entrypointIndex, bool stopAfterAction = false, bool showBar = true)
        {
            var writes = new List<(ushort FieldId, ushort Value)>();
            if (showBar)
                writes.Add((ShowOverdriveBarField, 1));
            if (setMode)
                writes.Add((OverdriveModeField, mode));
            writes.Add((OverdriveMaxField, max));
            writes.Add((OverdriveCurrentField, current));
            return AddChrPropertyValueWritesWithGuard(script, SelfTarget, writes, AlwaysGuard(), workerIndex, entrypointIndex, stopAfterAction);
        }

        public static byte[] AddOverdriveChargeSource(AiScriptFile script, ushort amount,
            IReadOnlyList<AiInstruction> guard, int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (amount == 0)
                throw new ArgumentOutOfRangeException(nameof(amount), Strings.F2_the_charge_must_be_greater_than_zero_5b0d287b);
            if (guard == null || guard.Count == 0)
                throw new ArgumentException(Strings.F2_please_provide_a_condition_guard_for_the_15e67ca0, nameof(guard));

            List<AiInstruction> action = BuildOverdriveCurrentAddAction(amount);
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                guard.Select(i => CloneForInsert(i)).ToList(), action, stopAfterAction);
        }

        public static byte[] AddOverdriveChargeAfterAction(AiScriptFile script, AiDetectedAction action, ushort amount,
            bool stopAfterAction = false)
        {
            ArgumentNullException.ThrowIfNull(script);
            if (action.Kind != AiActionKind.Command || !action.Removable)
                throw new InvalidOperationException(Strings.F2_select_a_simple_command_action_to_be_the_4c4dff60);
            if (amount == 0)
                throw new ArgumentOutOfRangeException(nameof(amount), Strings.F2_the_charge_must_be_greater_than_zero_5b0d287b);

            return InsertBodyAfterAction(script, action, BuildOverdriveCurrentAddAction(amount), random: false, k: 0, stopAfterAction);
        }

        public static byte[] AddOverdriveClampToMax(AiScriptFile script,
            int workerIndex, int entrypointIndex, bool stopAfterAction = false)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));

            List<AiInstruction> guard = BuildOverdriveCurrentVsMaxGuard(requireAtLeast: false);
            List<AiInstruction> action = BuildOverdriveCurrentSetToMaxAction();
            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex, guard, action, stopAfterAction);
        }

        public static byte[] AddOverdriveFinisherSequence(AiScriptFile script,
            IReadOnlyList<(ushort command, AiTargetRecipe target)> finishers,
            bool resetAfterSequence,
            int workerIndex,
            int entrypointIndex,
            bool stopAfterAction = true)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            if (entrypointIndex < 0 || entrypointIndex >= script.Workers[workerIndex].Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));
            if (finishers == null || finishers.Count == 0)
                throw new ArgumentException(Strings.F2_please_provide_at_least_one_ability_for__8dbb3361, nameof(finishers));

            var action = new List<AiInstruction>();
            foreach ((ushort command, AiTargetRecipe target) in finishers)
                action.AddRange(BuildQueuedCommandAction(command, target));
            if (resetAfterSequence)
                action.AddRange(BuildChrPropertyWriteAction(SelfTarget, OverdriveCurrentField, 0));

            return AiScript_File.AppendGuardedAction(script, workerIndex, entrypointIndex,
                BuildOverdriveCurrentVsMaxGuard(requireAtLeast: true), action, stopAfterAction);
        }

        static List<AiInstruction> BuildQueuedCommandAction(ushort commandOperand, AiTargetRecipe targetRecipe)
        {
            var action = new List<AiInstruction>();
            AddTargetToStack(action, targetRecipe);
            action.Add(Op(PUSHII, commandOperand));
            action.Add(Op(CALLPOPA, PerformCommand));
            return action;
        }

        static void AddReadSelfFieldToStack(List<AiInstruction> action, ushort fieldId)
        {
            AddReadActorFieldToStack(action, SelfTarget, fieldId);
        }

        static void AddReadActorFieldToStack(List<AiInstruction> action, ushort targetOperand, ushort fieldId)
        {
            action.Add(Op(PUSHII, targetOperand));
            action.Add(Op(PUSHII, fieldId));
            action.Add(Op(CALL, ReadChrProperty));
        }

        static List<AiInstruction> BuildOverdriveCurrentAddAction(ushort amount)
        {
            var action = new List<AiInstruction>
            {
                Op(PUSHII, SelfTarget),
                Op(PUSHII, OverdriveCurrentField),
            };
            AddReadSelfFieldToStack(action, OverdriveCurrentField);
            action.Add(Op(PUSHII, amount));
            action.Add(Op(ADD));
            action.Add(Op(CALLPOPA, WriteChrProperty));
            return action;
        }

        static List<AiInstruction> BuildOverdriveCurrentSetToMaxAction()
        {
            var action = new List<AiInstruction>
            {
                Op(PUSHII, SelfTarget),
                Op(PUSHII, OverdriveCurrentField),
            };
            AddReadSelfFieldToStack(action, OverdriveMaxField);
            action.Add(Op(CALLPOPA, WriteChrProperty));
            return action;
        }

        /// <summary>current OP max using the existing swapped operand idiom: emit [max, current] + LT/LE
        /// (max &lt; current ≡ current &gt; max ; max &lt;= current ≡ current &gt;= max).</summary>
        static List<AiInstruction> BuildOverdriveCurrentVsMaxGuard(bool requireAtLeast)
        {
            var guard = new List<AiInstruction>();
            AddReadSelfFieldToStack(guard, OverdriveMaxField);
            AddReadSelfFieldToStack(guard, OverdriveCurrentField);
            guard.Add(Op(requireAtLeast ? LE : LT));
            return guard;
        }

        /// <summary>Counts obvious Overdrive field reads/writes in the script. This is a status hint for the editor,
        /// not a claim that every hit is from the generated LAB recipe.</summary>
        public static int CountOverdriveFieldAccesses(AiScriptFile script)
        {
            if (script == null || !script.HasScript) return 0;
            int count = 0;
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            for (int i = 0; i < ins.Count; i++)
            {
                AiInstruction call = ins[i];
                if (call.Opcode is not (CALL or CALLPOPA)) continue;
                if (call.Operand == WriteChrProperty && i >= 2
                    && ins[i - 2].Opcode == PUSHII
                    && OverdriveActorFields.Contains(ins[i - 2].Operand))
                    count++;
                else if (call.Operand == ReadChrProperty && i >= 1
                         && ins[i - 1].Opcode == PUSHII
                         && OverdriveActorFields.Contains(ins[i - 1].Operand))
                    count++;
            }
            return count;
        }

        /// <summary>Counts generated-looking Overdrive LAB blocks that can be safely stripped by
        /// StripGeneratedOverdriveLab. This simulates stripping in memory and never mutates the passed script.</summary>
        public static int CountGeneratedOverdriveLabBlocks(AiScriptFile script, int minStartRel = 0)
        {
            if (script == null || !script.HasScript) return 0;
            int count = 0;
            AiScriptFile working = script;
            for (int guard = 0; guard < 128; guard++)
            {
                if (!TryStripOneGeneratedOverdriveLabBlock(working, minStartRel, out byte[] stripped))
                    break;
                if (stripped.Length >= working.OriginalAiFileBytes.Length)
                    break;
                count++;
                working = AiScript_File.Read(stripped);
            }
            return count;
        }

        /// <summary>Remove whole editor-generated Overdrive LAB guarded blocks, not individual field writes.
        /// This is intentionally conservative: it only removes AppendGuardedAction-shaped blocks reachable as current
        /// entrypoints and containing Overdrive field reads/writes. Vanilla logic that is not in this generated shape
        /// is left alone.</summary>
        public static byte[] StripGeneratedOverdriveLab(AiScriptFile script, out int removedBlocks, int minStartRel = 0)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            removedBlocks = 0;
            AiScriptFile working = script;
            for (int guard = 0; guard < 128; guard++)
            {
                if (!TryStripOneGeneratedOverdriveLabBlock(working, minStartRel, out byte[] stripped))
                    break;
                if (stripped.Length >= working.OriginalAiFileBytes.Length)
                    break;
                removedBlocks++;
                working = AiScript_File.Read(stripped);
            }
            return working.OriginalAiFileBytes;
        }

        static bool TryStripOneGeneratedOverdriveLabBlock(AiScriptFile script, int minStartRel, out byte[] stripped)
        {
            stripped = script.OriginalAiFileBytes;
            foreach (AiWorker worker in script.Workers)
            {
                foreach (int entry in worker.Entrypoints.Distinct().OrderByDescending(x => x))
                {
                    if (entry < minStartRel)
                        continue;
                    if (!TryParseGeneratedBlock(script, worker, entry, out GeneratedOverdriveBlock block))
                        continue;
                    if (!BlockContainsOverdriveFieldAccess(script, block.StartRel, block.EndRel))
                        continue;
                    stripped = RemoveGeneratedBlock(script, block);
                    return true;
                }
            }
            if (TryStripOneInlineOverdriveCurrentAddAfterCommand(script, out stripped))
                return true;
            return false;
        }

        static bool TryParseGeneratedBlock(
            AiScriptFile script,
            AiWorker worker,
            int startRel,
            out GeneratedOverdriveBlock block)
        {
            block = default;
            if (startRel < 0 || startRel >= script.CodeLength)
                return false;

            int startIndex = InstructionIndexAtRel(script, startRel);
            if (startIndex < 0)
                return false;

            IReadOnlyList<AiInstruction> ins = script.Instructions;
            int d7Index = -1;
            int searchLimitRel = Math.Min(script.CodeLength, startRel + 0x800);
            for (int i = startIndex; i < ins.Count && Rel(script, ins[i]) < searchLimitRel; i++)
            {
                if (ins[i].Opcode == 0xD7)
                {
                    d7Index = i;
                    break;
                }
            }
            if (d7Index < 0)
                return false;

            AiInstruction d7 = ins[d7Index];
            if (d7.Operand >= worker.JumpTargets.Count)
                return false;
            int skipTarget = worker.JumpTargets[d7.Operand];
            int d7Rel = Rel(script, d7);

            // Default AppendGuardedAction shape: D7 skips to an internal B0 rejoin, whose jump goes to the older
            // chain/original handler.
            int skipIndex = InstructionIndexAtRel(script, skipTarget);
            if (skipIndex >= 0 && skipTarget > d7Rel && ins[skipIndex].Opcode == 0xB0)
            {
                AiInstruction b0 = ins[skipIndex];
                if (b0.Operand >= worker.JumpTargets.Count)
                    return false;
                int nextRel = worker.JumpTargets[b0.Operand];
                int endRel = skipTarget + b0.Length;
                if (endRel <= startRel || endRel > script.CodeLength)
                    return false;
                if (nextRel >= startRel && nextRel < endRel)
                    return false;
                block = new GeneratedOverdriveBlock(worker.Index, startRel, endRel, nextRel);
                return true;
            }

            // stopAfterAction shape: D7 skips to the older/original chain, successful action ends in RET.
            for (int i = d7Index + 1; i < ins.Count && Rel(script, ins[i]) < searchLimitRel; i++)
            {
                if (ins[i].Opcode != 0x3C)
                    continue;
                int endRel = Rel(script, ins[i]) + ins[i].Length;
                if (endRel <= startRel || endRel > script.CodeLength)
                    return false;
                if (skipTarget >= startRel && skipTarget < endRel)
                    return false;
                block = new GeneratedOverdriveBlock(worker.Index, startRel, endRel, skipTarget);
                return true;
            }

            return false;
        }

        static bool BlockContainsOverdriveFieldAccess(AiScriptFile script, int startRel, int endRel)
        {
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            for (int i = 0; i < ins.Count; i++)
            {
                int rel = Rel(script, ins[i]);
                if (rel < startRel || rel >= endRel)
                    continue;
                AiInstruction call = ins[i];
                if (call.Opcode is not (CALL or CALLPOPA))
                    continue;
                if (call.Operand == WriteChrProperty && i >= 2
                    && ins[i - 2].Opcode == PUSHII
                    && OverdriveActorFields.Contains(ins[i - 2].Operand))
                    return true;
                if (call.Operand == ReadChrProperty && i >= 1
                    && ins[i - 1].Opcode == PUSHII
                    && OverdriveActorFields.Contains(ins[i - 1].Operand))
                    return true;
            }
            return false;
        }

        static byte[] RemoveGeneratedBlock(AiScriptFile script, GeneratedOverdriveBlock block)
        {
            byte[] patched = (byte[])script.OriginalAiFileBytes.Clone();
            PatchTargetsInsideBlock(patched, script, block.StartRel, block.EndRel, block.NextRel);
            AiScriptFile patchedScript = AiScript_File.Read(patched);
            List<AiInstruction> kept = patchedScript.Instructions
                .Where(i =>
                {
                    int rel = Rel(patchedScript, i);
                    return rel < block.StartRel || rel >= block.EndRel;
                })
                .ToList();
            return AiScript_File.Rebuild(patchedScript, kept);
        }

        static bool TryStripOneInlineOverdriveCurrentAddAfterCommand(AiScriptFile script, out byte[] stripped)
        {
            stripped = script.OriginalAiFileBytes;
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            for (int i = 0; i + 8 < ins.Count; i++)
            {
                AiInstruction commandCall = ins[i];
                if (commandCall.Opcode != CALLPOPA || commandCall.Operand is not (PerformCommand or ForcePerformCommand))
                    continue;

                int start = i + 1;
                if (!IsPush(ins[start + 0], SelfTarget)
                    || !IsPush(ins[start + 1], OverdriveCurrentField)
                    || !IsPush(ins[start + 2], SelfTarget)
                    || !IsPush(ins[start + 3], OverdriveCurrentField)
                    || ins[start + 4].Opcode != CALL
                    || ins[start + 4].Operand != ReadChrProperty
                    || ins[start + 5].Opcode != PUSHII
                    || ins[start + 6].Opcode != ADD
                    || ins[start + 7].Opcode != CALLPOPA
                    || ins[start + 7].Operand != WriteChrProperty)
                    continue;

                int removeStart = ins[start].Offset;
                int removeEnd = ins[start + 7].Offset + ins[start + 7].Length;
                List<AiInstruction> kept = ins
                    .Where(x => x.Offset < removeStart || x.Offset >= removeEnd)
                    .ToList();
                stripped = AiScript_File.Rebuild(script, kept);
                return true;
            }
            return false;

            static bool IsPush(AiInstruction instruction, ushort operand) =>
                instruction.Opcode == PUSHII && instruction.Operand == operand;
        }

        static void PatchTargetsInsideBlock(byte[] ai, AiScriptFile script, int startRel, int endRel, int nextRel)
        {
            bool Inside(uint value) => value >= startRel && value < endRel;
            foreach (AiWorker worker in script.Workers)
            {
                int desc = worker.DescriptorOffset;
                if (desc < 0 || desc + WdJumpTable + 4 > ai.Length)
                    continue;
                int entryTab = (int)ReadU32(ai, desc + WdEntryTable);
                for (int i = 0; i < worker.Entrypoints.Count; i++)
                {
                    int off = entryTab + 4 * i;
                    if (off < 0 || off + 4 > ai.Length) continue;
                    if (Inside(ReadU32(ai, off))) WriteU32Local(ai, off, (uint)nextRel);
                }
                int jumpTab = (int)ReadU32(ai, desc + WdJumpTable);
                for (int i = 0; i < worker.JumpTargets.Count; i++)
                {
                    int off = jumpTab + 4 * i;
                    if (off < 0 || off + 4 > ai.Length) continue;
                    if (Inside(ReadU32(ai, off))) WriteU32Local(ai, off, (uint)nextRel);
                }
            }
        }

        static int InstructionIndexAtRel(AiScriptFile script, int codeRel)
        {
            int abs = script.ScriptStart + codeRel;
            for (int i = 0; i < script.Instructions.Count; i++)
                if (script.Instructions[i].Offset == abs)
                    return i;
            return -1;
        }

        static int Rel(AiScriptFile script, AiInstruction instruction) => instruction.Offset - script.ScriptStart;

        static uint ReadU32(byte[] b, int o) =>
            (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

        static void WriteU32Local(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }

        readonly record struct GeneratedOverdriveBlock(int WorkerIndex, int StartRel, int EndRel, int NextRel);

        /// <summary>Legacy structural fallback. Prefer the WorkerFile-backed CombatHandler.onTurn hook for 1-click UI.</summary>

    }
}
