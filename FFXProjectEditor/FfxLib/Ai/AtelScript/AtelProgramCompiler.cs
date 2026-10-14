using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai.AtelScript
{
    /// <summary>
    /// F5-L3: compilador completo da linguagem ATEL de alto nível — estende o AtelEmitter com:
    ///   • var persistente: `var nome = expr;` cria uma variável privada (privNNNN) no AiFile se
    ///     o nome não existir (AppendPrivateVariableDescriptor + slot livre) e inicializa;
    ///   • while: loop via jump table (guard → D7 para a saída/original → corpo → B0 de volta ao
    ///     guard), montado no mesmo padrão provado do AppendGuardedAction (Rebuild + GrowWorkerJumpTable
    ///     + repoint do entrypoint);
    ///   • func/call: `func nome { corpo }` = bloco nomeado reutilizável; `call nome;` expande INLINE
    ///     (macro — sem stack de chamada JSR/RTS, que exige RT2).
    /// </summary>
    public static class AtelProgramCompiler
    {
        const byte PUSHV = 0x9F, POPV = 0xA0, PUSHII = 0xAE;
        const byte EQ = 0x06;

        /// <summary>
        /// Compila um programa completo (L3): funcs → vars persistentes → ifs/whiles/assigns/calls.
        /// </summary>
        public static AtelCompileResult CompileProgram(AtelProgram program, AtelCompileContext ctx)
        {
            if (ctx.WorkerIndex < 0 || ctx.WorkerIndex >= ctx.Script.Workers.Count)
                throw new AtelEmitException($"workerIndex {ctx.WorkerIndex} fora do script ({ctx.Script.Workers.Count} workers)");
            if (ctx.EntrypointIndex < 0 || ctx.EntrypointIndex >= ctx.Script.Workers[ctx.WorkerIndex].Entrypoints.Count)
                throw new AtelEmitException($"entrypointIndex {ctx.EntrypointIndex} fora do worker {ctx.WorkerIndex}");

            // passo 0: funções nomeadas (macro inline)
            var funcs = program.Statements
                .OfType<AtelFuncDecl>()
                .ToDictionary(f => f.Name, f => f.Body, StringComparer.Ordinal);

            byte[] current = ctx.Script.OriginalAiFileBytes;
            AiScriptFile s = ctx.Script;
            var aliases = new Dictionary<string, ushort>(StringComparer.Ordinal);
            int applied = 0;
            var appliedList = new List<string>();

            Func<string, ushort> resolve = name => Resolve(name, s, aliases);

            // passo 1: declarações de variável persistente (cria privada se faltar + inicializa)
            foreach (AtelVarDecl vd in program.Statements.OfType<AtelVarDecl>())
            {
                if (!aliases.ContainsKey(vd.Name))
                {
                    ushort index;
                    try
                    {
                        index = AtelEmitter.ResolveVariable(s, vd.Name);
                    }
                    catch (AtelEmitException)
                    {
                        if (!AiScript_File.TryFindFreePrivateVariableSlot(s, out int slot, out string reason))
                            throw new AtelEmitException(
                                string.Format(Strings.U_Ai_AtelNoFreePrivateSlotFormat, vd.Name, reason));
                        byte[] grown = AiScript_File.AppendPrivateVariableDescriptor(s, slot, 1);
                        s = AiScript_File.Read(grown);
                        current = grown;
                        index = AtelEmitter.ResolveVariable(s, $"priv{slot:X4}");
                    }
                    aliases[vd.Name] = index;
                }

                if (vd.Initializer != null)
                {
                    var action = new List<AiInstruction>(AtelEmitter.EmitExpr(vd.Initializer, resolve));
                    action.Add(Op(POPV, aliases[vd.Name]));
                    current = AiScript_File.AppendGuardedAction(
                        s, ctx.WorkerIndex, ctx.EntrypointIndex, AtelEmitter.AlwaysGuard(), action, stopAfterAction: false);
                    s = AiScript_File.Read(current);
                    applied++;
                    appliedList.Add($"var {vd.Name} = {vd.Initializer}");
                }
            }

            // passo 2: statements de topo (if / while / assign / call)
            foreach (AtelStmt stmt in program.Statements)
            {
                switch (stmt)
                {
                    case AtelVarDecl:
                        break; // já tratado no passo 1
                    case AtelFuncDecl:
                        break; // registro já coletado
                    case AtelIf iff when iff.Else == null:
                    {
                        IReadOnlyList<AiInstruction> guard = AtelEmitter.EmitGuard(iff.Condition, resolve);
                        IReadOnlyList<AiInstruction> action = EmitBody(iff.Then, resolve, funcs);
                        current = AiScript_File.AppendGuardedAction(
                            s, ctx.WorkerIndex, ctx.EntrypointIndex, guard, action, stopAfterAction: true);
                        s = AiScript_File.Read(current);
                        applied++;
                        appliedList.Add($"if({iff.Condition})");
                        break;
                    }
                    case AtelIf iff:
                    {
                        // if/else via jump table (montagem manual no padrão do while):
                        // [guard, D7→else, corpo_then, B0→fim, corpo_else] — fim = fluxo original do entrypoint
                        current = EmitIfElse(s, iff, resolve, ctx.WorkerIndex, ctx.EntrypointIndex, funcs);
                        s = AiScript_File.Read(current);
                        applied++;
                        appliedList.Add($"if({iff.Condition}) else");
                        break;
                    }
                    case AtelWhile wh:
                    {
                        current = EmitWhile(s, wh, resolve, ctx.WorkerIndex, ctx.EntrypointIndex, funcs);
                        s = AiScript_File.Read(current);
                        applied++;
                        appliedList.Add($"while({wh.Condition})");
                        break;
                    }
                    case AtelAssign asg:
                    {
                        IReadOnlyList<AiInstruction> action = AtelEmitter.EmitAssignAction(asg, resolve);
                        current = AiScript_File.AppendGuardedAction(
                            s, ctx.WorkerIndex, ctx.EntrypointIndex, AtelEmitter.AlwaysGuard(), action, stopAfterAction: false);
                        s = AiScript_File.Read(current);
                        applied++;
                        appliedList.Add($"assign {asg.Name}");
                        break;
                    }
                    case AtelCall call:
                    {
                        IReadOnlyList<AiInstruction> action = EmitBody(ResolveFunc(funcs, call.Name), resolve, funcs);
                        current = AiScript_File.AppendGuardedAction(
                            s, ctx.WorkerIndex, ctx.EntrypointIndex, AtelEmitter.AlwaysGuard(), action, stopAfterAction: false);
                        s = AiScript_File.Read(current);
                        applied++;
                        appliedList.Add($"call {call.Name}");
                        break;
                    }
                    case AtelReturn:
                        throw new AtelEmitException(Strings.U_Ai_AtelTopLevelReturnUnsupported);
                    default:
                        throw new AtelEmitException($"statement de topo não suportado: {stmt.GetType().Name}");
                }
            }

            return new AtelCompileResult(current, applied,
                applied == 0 ? "nenhum statement aplicado" : string.Join("; ", appliedList));
        }


        // ── while (loop via jump table) ──────────────────────────────────────────────

        /// <summary>
        /// Emite um while no entrypoint alvo: [guard, D7→original, corpo, B0→guard].
        /// Mesmo padrão do AppendGuardedAction (Rebuild + GrowWorkerJumpTable + repoint do entrypoint).
        /// Guard falso → salta para o fluxo ORIGINAL do entrypoint; guard verdadeiro → corpo e volta ao guard.
        /// </summary>
        public static byte[] EmitWhile(AiScriptFile script, AtelWhile wh, Func<string, ushort> resolveVar,
            int workerIndex, int entrypointIndex, IReadOnlyDictionary<string, IReadOnlyList<AtelStmt>> funcs)
        {
            AiWorker w = script.Workers[workerIndex];
            if (entrypointIndex < 0 || entrypointIndex >= w.Entrypoints.Count)
                throw new AtelEmitException($"entrypointIndex {entrypointIndex} fora do worker {workerIndex}");

            int baseRel = script.CodeLength;              // início do loop (code-relative)
            int originalEntry = w.Entrypoints[entrypointIndex];
            IReadOnlyList<AiInstruction> guard = AtelEmitter.EmitGuard(wh.Condition, resolveVar);
            IReadOnlyList<AiInstruction> body = EmitBody(wh.Body, resolveVar, funcs);

            int oldJumps = w.JumpTargets.Count;
            int exitSlot = oldJumps;                       // D7 → original (guarda falhou)
            int loopSlot = oldJumps + 1;                   // B0 → início do guard

            var newInstrs = new List<AiInstruction>(script.Instructions);
            newInstrs.AddRange(guard);
            newInstrs.Add(Jump(0xD7, exitSlot));
            newInstrs.AddRange(body);
            newInstrs.Add(Jump(0xB0, loopSlot));

            byte[] rebuilt = AiScript_File.Rebuild(script, newInstrs);
            byte[] grown = AiScript_File.GrowWorkerJumpTable(
                AiScript_File.Read(rebuilt), workerIndex, new[] { originalEntry, baseRel });
            AiScriptFile s2 = AiScript_File.Read(grown);
            WriteEntrypointOffset(grown, s2, workerIndex, entrypointIndex, baseRel);
            return grown;
        }

        // ── if/else (jump table) ──────────────────────────────────────────────────────

        /// <summary>
        /// Emite um if/else no entrypoint alvo: [guard, D7→else, corpo_then, B0→original,
        /// corpo_else, RET]. Guard passa → then → volta ao fluxo original; guard falha → else
        /// → RET (a thread termina — mesmo contrato do stopAfterAction). else-if → erro claro (L5.1).
        /// </summary>
        public static byte[] EmitIfElse(AiScriptFile script, AtelIf iff, Func<string, ushort> resolveVar,
            int workerIndex, int entrypointIndex, IReadOnlyDictionary<string, IReadOnlyList<AtelStmt>> funcs)
        {
            AiWorker w = script.Workers[workerIndex];
            if (entrypointIndex < 0 || entrypointIndex >= w.Entrypoints.Count)
                throw new AtelEmitException($"entrypointIndex {entrypointIndex} fora do worker {workerIndex}");

            if (iff.Else is { Count: 1 } && iff.Else[0] is AtelIf)
                throw new AtelEmitException("else-if ainda não suportado no L5 (use if/else simples ou funções)");

            int baseRel = script.CodeLength;
            int originalEntry = w.Entrypoints[entrypointIndex];
            IReadOnlyList<AiInstruction> guard = AtelEmitter.EmitGuard(iff.Condition, resolveVar);
            IReadOnlyList<AiInstruction> then = EmitBody(iff.Then, resolveVar, funcs);
            IReadOnlyList<AiInstruction> els = EmitBody(iff.Else ?? Array.Empty<AtelStmt>(), resolveVar, funcs);

            int guardLen = guard.Sum(static i => i.Length);
            int thenLen = then.Sum(static i => i.Length);
            int elseRel = baseRel + guardLen + 3 + thenLen;   // início do else (D7 = 3 bytes)
            int oldJumps = w.JumpTargets.Count;
            int elseSlot = oldJumps;
            int endSlot = oldJumps + 1;

            var newInstrs = new List<AiInstruction>(script.Instructions);
            newInstrs.AddRange(guard);
            newInstrs.Add(Jump(0xD7, elseSlot));
            newInstrs.AddRange(then);
            newInstrs.Add(Jump(0xB0, endSlot));               // then → fluxo original
            newInstrs.AddRange(els);
            newInstrs.Add(new AiInstruction
            {
                Offset = -1,
                Opcode = 0x3C, // RET
                HasOperand = false,
                Operand = 0,
                OperandKind = AiOperandKind.None,
            });

            byte[] rebuilt = AiScript_File.Rebuild(script, newInstrs);
            byte[] grown = AiScript_File.GrowWorkerJumpTable(
                AiScript_File.Read(rebuilt), workerIndex, new[] { elseRel, originalEntry });
            AiScriptFile s2 = AiScript_File.Read(grown);
            WriteEntrypointOffset(grown, s2, workerIndex, entrypointIndex, baseRel);
            return grown;
        }

        // ── helpers ──────────────────────────────────────────────────────────────────

        /// <summary>Emite um corpo de bloco, expandindo `call nome` inline (macro).</summary>
        static IReadOnlyList<AiInstruction> EmitBody(IReadOnlyList<AtelStmt> stmts, Func<string, ushort> resolveVar,
            IReadOnlyDictionary<string, IReadOnlyList<AtelStmt>> funcs)
        {
            var action = new List<AiInstruction>();
            foreach (AtelStmt s in stmts)
            {
                switch (s)
                {
                    case AtelAssign asg:
                        action.AddRange(AtelEmitter.EmitAssignAction(asg, resolveVar));
                        break;
                    case AtelCall call:
                        action.AddRange(EmitBody(ResolveFunc(funcs, call.Name), resolveVar, funcs));
                        break;
                    case AtelReturn:
                        break; // no-op (stopAfterAction decide o término)
                    case AtelIf iff:
                        throw new AtelEmitException(
                            "if aninhado dentro de corpo ainda não suportado no L3 (use funções para reutilizar blocos)");
                    default:
                        throw new AtelEmitException(
                            $"statement no corpo não suportado no L3: {s.GetType().Name} (use atribuições e call)");
                }
            }
            return action;
        }

        static IReadOnlyList<AtelStmt> ResolveFunc(IReadOnlyDictionary<string, IReadOnlyList<AtelStmt>> funcs, string name)
        {
            if (funcs.TryGetValue(name, out var body))
                return body;
            throw new AtelEmitException(
                string.Format(Strings.U_Ai_AtelFunctionNotDeclaredFormat, name));
        }

        static ushort Resolve(string name, AiScriptFile script, Dictionary<string, ushort> aliases)
            => aliases.TryGetValue(name, out ushort index) ? index : AtelEmitter.ResolveVariable(script, name);

        /// <summary>Repoint do entrypoint (mesmos offsets do AiScript_File: descriptor+0x20 = entry table).</summary>
        static void WriteEntrypointOffset(byte[] ai, AiScriptFile script, int workerIndex, int entrypointIndex, int codeRelTarget)
        {
            int desc = script.Workers[workerIndex].DescriptorOffset;
            int entryTab = BitConverter.ToInt32(ai, desc + 0x20);
            byte[] target = BitConverter.GetBytes(codeRelTarget);
            Buffer.BlockCopy(target, 0, ai, entryTab + 4 * entrypointIndex, 4);
        }

        static AiInstruction Jump(byte opcode, int slot) => new()
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = true,
            Operand = (ushort)slot,
            OperandKind = AiOperandKind.JumpIndex,
        };

        static AiInstruction Op(byte opcode, ushort operand = 0) => new()
        {
            Offset = -1,
            Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };
    }
}

