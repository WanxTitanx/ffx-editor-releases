using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ai.AtelScript
{
    /// <summary>
    /// F5-L1 (docs/reverse/PPP_ATEL_ASSEMBLER_F5_FEASIBILITY_2026-08-01.md): AST da linguagem
    /// de script ATEL de alto nível (sintaxe inspirada no atelier do fahrenheit, reimplementada —
    /// semântica nativa documentada em FFX_EVENTVM_OPS_2026-09-17.md).
    ///
    /// Escopo L1: var/if-else/return/atribuição/expressões (aritmética + comparação) — o emissor
    /// (L2) consumirá este AST via AiVarConditionBuilder/AppendGuardedAction.
    /// </summary>
    public abstract record AtelStmt;

    /// <summary>var name (= expr)? ;</summary>
    public sealed record AtelVarDecl(string Name, AtelExpr? Initializer) : AtelStmt;

    /// <summary>if (cond) { ... } else { ... }</summary>
    public sealed record AtelIf(AtelExpr Condition, IReadOnlyList<AtelStmt> Then, IReadOnlyList<AtelStmt>? Else) : AtelStmt;

    /// <summary>return expr? ;</summary>
    public sealed record AtelReturn(AtelExpr? Value) : AtelStmt;

    /// <summary>name = expr ;</summary>
    public sealed record AtelAssign(string Name, AtelExpr Value) : AtelStmt;

    /// <summary>Bloco de statements (usado por if/else; também aceito no topo).</summary>
    public sealed record AtelBlock(IReadOnlyList<AtelStmt> Statements) : AtelStmt;

    /// <summary>while (cond) { ... } — loop via jump table (F5-L3).</summary>
    public sealed record AtelWhile(AtelExpr Condition, IReadOnlyList<AtelStmt> Body) : AtelStmt;

    /// <summary>func nome { ... } — bloco nomeado (F5-L3, macro inline — sem stack de chamada).</summary>
    public sealed record AtelFuncDecl(string Name, IReadOnlyList<AtelStmt> Body) : AtelStmt;

    /// <summary>call nome ; — expande inline a função (F5-L3).</summary>
    public sealed record AtelCall(string Name) : AtelStmt;

    public abstract record AtelExpr;

    /// <summary>Literal inteiro (dec ou hex 0x..).</summary>
    public sealed record AtelLiteral(int Value) : AtelExpr;

    /// <summary>Referência a variável.</summary>
    public sealed record AtelVarRef(string Name) : AtelExpr;

    /// <summary>Operação binária: Op ∈ {+,-,*,/,%,==,!=,&lt;,&gt;,&lt;=,&gt;=}.</summary>
    public sealed record AtelBinary(string Op, AtelExpr Left, AtelExpr Right) : AtelExpr;

    /// <summary>Negativo unário (-x).</summary>
    public sealed record AtelUnary(string Op, AtelExpr Operand) : AtelExpr;

    /// <summary>Programa completo: lista de statements de topo.</summary>
    public sealed record AtelProgram(IReadOnlyList<AtelStmt> Statements);
}
