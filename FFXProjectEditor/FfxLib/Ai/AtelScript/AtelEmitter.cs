using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai.AtelScript
{
    /// <summary>Erro de emissão com mensagem acionável (sem stack trace cru).</summary>
    public sealed class AtelEmitException : Exception
    {
        public AtelEmitException(string message) : base(message) { }
    }

    /// <summary>Contexto de compilação: script alvo + worker/entrypoint onde os guarded actions entram.</summary>
    public sealed record AtelCompileContext
    {
        public required AiScriptFile Script { get; init; }
        public required int WorkerIndex { get; init; }
        public required int EntrypointIndex { get; init; }
    }

    /// <summary>Resultado da compilação de um programa (bytes do AiFile + resumo).</summary>
    public sealed record AtelCompileResult(byte[] AiFile, int AppliedSteps, string Summary);

    /// <summary>
    /// F5-L2 (docs/reverse/PPP_ATEL_ASSEMBLER_F5_FEASIBILITY_2026-08-01.md): emissor da linguagem
    /// ATEL de alto nível → instruções AiFile.
    ///
    /// Native stack [A(base), B(top)]: 0x0A = A &gt; B, 0x0B = A &lt; B,
    /// 0x0E = A &gt;= B, 0x0F = A &lt;= B. Keep the existing swapped-LT/LE
    /// emission for greater-than comparisons. Integer/float selection uses operand tags.
    /// Evidence: docs/reverse/FFX_EVENTVM_OPS_2026-09-17.md section 7, interpreter @0x864180.
    /// </summary>
    public static class AtelEmitter
    {
        const byte PUSHV = 0x9F, POPV = 0xA0, PUSHII = 0xAE;
        const byte EQ = 0x06, NE = 0x07, LT = 0x0B, LE = 0x0F;
        const byte ADD = 0x14, SUB = 0x15, MUL = 0x16, DIV = 0x17, MOD = 0x18;

        // ── expressões ──────────────────────────────────────────────────────────────

        /// <summary>Emite uma expressão (deixa exatamente 1 valor na pilha).</summary>
        public static IReadOnlyList<AiInstruction> EmitExpr(AtelExpr expr, Func<string, ushort> resolveVar)
        {
            switch (expr)
            {
                case AtelLiteral lit:
                    return new[] { Op(PUSHII, unchecked((ushort)lit.Value)) };

                case AtelVarRef v:
                    return new[] { Op(PUSHV, resolveVar(v.Name)) };

                case AtelUnary { Op: "-" } un:
                {
                    var list = new List<AiInstruction> { Op(PUSHII, 0) };
                    list.AddRange(EmitExpr(un.Operand, resolveVar));
                    list.Add(Op(SUB)); // 0 - x
                    return list;
                }

                case AtelUnary { Op: "!" } un:
                {
                    var list = new List<AiInstruction>();
                    list.AddRange(EmitExpr(un.Operand, resolveVar));
                    list.Add(Op(PUSHII, 0));
                    list.Add(Op(EQ)); // x == 0
                    return list;
                }

                case AtelBinary { Op: "+" or "-" or "*" or "/" or "%" } bin:
                {
                    var list = new List<AiInstruction>();
                    list.AddRange(EmitExpr(bin.Left, resolveVar));
                    list.AddRange(EmitExpr(bin.Right, resolveVar));
                    list.Add(Op(ArithOpcode(bin.Op)));
                    return list;
                }

                default:
                    throw new AtelEmitException(
                        $"expression not supported at L2: {expr.GetType().Name} (use literals, vars, arithmetic and unary)");
            }
        }

        // ── comparação (semântica corrigida) ────────────────────────────────────────

        /// <summary>Emite um guard de comparação (deixa 1 booleano na pilha).</summary>
        public static IReadOnlyList<AiInstruction> EmitComparison(AtelBinary cmp, Func<string, ushort> resolveVar)
        {
            var list = new List<AiInstruction>();
            switch (cmp.Op)
            {
                case "==":
                    list.AddRange(EmitExpr(cmp.Left, resolveVar));
                    list.AddRange(EmitExpr(cmp.Right, resolveVar));
                    list.Add(Op(EQ));
                    break;
                case "!=":
                    list.AddRange(EmitExpr(cmp.Left, resolveVar));
                    list.AddRange(EmitExpr(cmp.Right, resolveVar));
                    list.Add(Op(NE));
                    break;
                case "<":
                    list.AddRange(EmitExpr(cmp.Left, resolveVar));
                    list.AddRange(EmitExpr(cmp.Right, resolveVar));
                    list.Add(Op(LT)); // base < topo
                    break;
                case "<=":
                    list.AddRange(EmitExpr(cmp.Left, resolveVar));
                    list.AddRange(EmitExpr(cmp.Right, resolveVar));
                    list.Add(Op(LE)); // base <= topo
                    break;
                case ">":
                    // left > right ≡ right < left → emite [right, left, LT] (ordem invertida!)
                    list.AddRange(EmitExpr(cmp.Right, resolveVar));
                    list.AddRange(EmitExpr(cmp.Left, resolveVar));
                    list.Add(Op(LT));
                    break;
                case ">=":
                    // left >= right ≡ right <= left → emite [right, left, LE]
                    list.AddRange(EmitExpr(cmp.Right, resolveVar));
                    list.AddRange(EmitExpr(cmp.Left, resolveVar));
                    list.Add(Op(LE));
                    break;
                default:
                    throw new AtelEmitException($"invalid comparison operator: '{cmp.Op}'");
            }
            return list;
        }

        /// <summary>
        /// Emite o guard de uma condição: comparação binária, ou composição lógica
        /// `left && right` (LAND 0x02) / `left || right` (LOR 0x01).
        /// </summary>
        public static IReadOnlyList<AiInstruction> EmitGuard(AtelExpr condition, Func<string, ushort> resolveVar)
        {
            if (condition is AtelBinary { Op: "&&" or "||" } logic)
            {
                var list = new List<AiInstruction>(EmitGuard(logic.Left, resolveVar));
                list.AddRange(EmitGuard(logic.Right, resolveVar));
                list.Add(Op(logic.Op == "&&" ? (byte)0x02 : (byte)0x01)); // LAND / LOR
                return list;
            }
            if (condition is AtelBinary bin)
                return EmitComparison(bin, resolveVar);
            throw new AtelEmitException("if condition must be a comparison or logical composition (e.g., x < 5 && y > 2)");
        }

        // ── ações ───────────────────────────────────────────────────────────────────

        /// <summary>Emite a ação de atribuição: [expr, POPV index].</summary>
        public static IReadOnlyList<AiInstruction> EmitAssignAction(AtelAssign assign, Func<string, ushort> resolveVar)
        {
            var list = new List<AiInstruction>(EmitExpr(assign.Value, resolveVar));
            list.Add(Op(POPV, resolveVar(assign.Name)));
            return list;
        }

        /// <summary>Emite o corpo de um if (L2: atribuições; return → ação vazia + stop no caller).</summary>
        public static IReadOnlyList<AiInstruction> EmitBlockActions(
            IReadOnlyList<AtelStmt> stmts, Func<string, ushort> resolveVar)
        {
            var action = new List<AiInstruction>();
            foreach (AtelStmt s in stmts)
            {
                switch (s)
                {
                    case AtelAssign asg:
                        action.AddRange(EmitAssignAction(asg, resolveVar));
                        break;
                    case AtelReturn:
                        // stopAfterAction=true no caller encerra a thread — return explícito é no-op
                        break;
                    default:
                        throw new AtelEmitException(
                            $"statement in the if body not supported at L2: {s.GetType().Name} (use assignments)");
                }
            }
            return action;
        }


        // ── compilação de programa ──────────────────────────────────────────────────

        /// <summary>
        /// Compila um programa completo (L2/L3) — delega ao AtelProgramCompiler.
        /// </summary>
        public static AtelCompileResult CompileProgram(AtelProgram program, AtelCompileContext ctx)
            => AtelProgramCompiler.CompileProgram(program, ctx);

        /// <summary>Resolve o nome de uma variável para o índice na tabela do AiFile (erro claro se ausente).</summary>
        public static ushort ResolveVariable(AiScriptFile script, string name)
        {
            AiVariable? v = script.Variables.FirstOrDefault(x => x.Name == name);
            if (v != null)
                return (ushort)v.Index;
            string known = string.Join(", ", script.Variables.Select(x => x.Name).Take(10));
            throw new AtelEmitException(
                $"variable '{name}' does not exist in the AiFile table (known: {known}{(script.Variables.Count > 10 ? ", ..." : "")})");
        }

        /// <summary>Guard sempre-verdadeiro: 0 == 0.</summary>
        public static IReadOnlyList<AiInstruction> AlwaysGuard() => new[]
        {
            Op(PUSHII, 0),
            Op(PUSHII, 0),
            Op(EQ),
        };

        static byte ArithOpcode(string op) => op switch
        {
            "+" => ADD,
            "-" => SUB,
            "*" => MUL,
            "/" => DIV,
            "%" => MOD,
            _ => throw new AtelEmitException($"invalid arithmetic operator: '{op}'"),
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

