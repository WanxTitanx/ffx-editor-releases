using System;
using System.Collections.Generic;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai.AtelScript
{
    /// <summary>Erro de sintaxe com posição (linha:coluna) — mensagem acionável, sem stack trace cru.</summary>
    public sealed class AtelSyntaxException : Exception
    {
        public int Line { get; }
        public int Column { get; }

        private AtelSyntaxException(string message, int line, int column) : base($"{message} (linha {line}, coluna {column})")
        {
            Line = line;
            Column = column;
        }

        public static AtelSyntaxException Unexpected(string what, int line, int column)
            => new(string.Format(Strings.U_Ai_AtelUnexpectedToken, what), line, column);

        public static AtelSyntaxException Expected(string what, AtelToken got)
            => new(string.Format(Strings.U_Ai_AtelExpectedFound, what, got.Text), got.Line, got.Column);
    }

    /// <summary>
    /// F5-L1: parser recursivo descendente da linguagem ATEL de alto nível.
    ///
    /// Gramática (L1):
    ///   program   := stmt*
    ///   stmt      := 'var' IDENT ('=' expr)? ';' | 'if' expr block ('else' (block | 'if' ...))?
    ///              | 'return' expr? ';' | IDENT '=' expr ';' | block
    ///   block     := '{' stmt* '}'
    ///   expr      := comparison
    ///   comparison:= additive (('=='|'!='|'&lt;'|'&gt;'|'&lt;='|'&gt;=') additive)*
    ///   additive  := multiplicative (('+'|'-') multiplicative)*
    ///   mult      := unary (('*'|'/'|'%') unary)*
    ///   unary     := ('-'|'!') unary | primary
    ///   primary   := INT | IDENT | '(' expr ')'
    ///
    /// 'while' e 'func' são reservados (tokens Keyword) mas NÃO implementados no L1 — o parser
    /// lança erro claro se usados como statement (L3).
    /// </summary>
    public sealed class AtelParser
    {
        private readonly IReadOnlyList<AtelToken> _tokens;
        private int _idx;

        public AtelParser(string source)
            : this(new AtelLexer(source).Tokenize())
        {
        }

        public AtelParser(IReadOnlyList<AtelToken> tokens)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        public AtelProgram ParseProgram()
        {
            var stmts = new List<AtelStmt>();
            while (!Is(AtelTokenKind.EndOfFile))
                stmts.Add(ParseStmt());
            return new AtelProgram(stmts);
        }

        // ── statements ─────────────────────────────────────────────────────────────

        private AtelStmt ParseStmt()
        {
            AtelToken t = Peek();

            if (t.Kind == AtelTokenKind.Keyword)
            {
                switch (t.Text)
                {
                    case "var":
                        return ParseVarDecl();
                    case "if":
                        return ParseIf();
                    case "return":
                        return ParseReturn();
                    case "while":
                        return ParseWhile();
                    case "func":
                        return ParseFuncDecl();
                    case "call":
                        return ParseCall();
                }
            }

            if (t.Kind == AtelTokenKind.Operator && t.Text == "{")
                return ParseBlock();

            if (t.Kind == AtelTokenKind.Identifier)
                return ParseAssign();

            throw AtelSyntaxException.Unexpected($"'{t.Text}'", t.Line, t.Column);
        }

        private AtelStmt ParseVarDecl()
        {
            Expect(AtelTokenKind.Keyword, "var");
            AtelToken name = Expect(AtelTokenKind.Identifier, Strings.U_Ai_AtelVarName);

            AtelExpr? init = null;
            if (Match("="))
                init = ParseExpr();

            Expect(";");
            return new AtelVarDecl(name.Text, init);
        }

        private AtelStmt ParseIf()
        {
            Expect(AtelTokenKind.Keyword, "if");
            AtelExpr cond = ParseExpr();
            var then = ParseBlock().Statements;

            IReadOnlyList<AtelStmt>? elseBranch = null;
            if (Is(AtelTokenKind.Keyword) && Peek().Text == "else")
            {
                Advance();
                elseBranch = Is(AtelTokenKind.Keyword) && Peek().Text == "if"
                    ? new[] { ParseIf() }
                    : ParseBlock().Statements;
            }

            return new AtelIf(cond, then, elseBranch);
        }

        private AtelStmt ParseReturn()
        {
            Expect(AtelTokenKind.Keyword, "return");
            AtelExpr? value = Is(";") ? null : ParseExpr();
            Expect(";");
            return new AtelReturn(value);
        }

        private AtelStmt ParseWhile()
        {
            Expect(AtelTokenKind.Keyword, "while");
            AtelExpr cond = ParseExpr();
            var body = ParseBlock().Statements;
            return new AtelWhile(cond, body);
        }

        private AtelStmt ParseFuncDecl()
        {
            Expect(AtelTokenKind.Keyword, "func");
            AtelToken name = Expect(AtelTokenKind.Identifier, Strings.U_Ai_AtelFuncName);
            var body = ParseBlock().Statements;
            return new AtelFuncDecl(name.Text, body);
        }

        private AtelStmt ParseCall()
        {
            Expect(AtelTokenKind.Keyword, "call");
            AtelToken name = Expect(AtelTokenKind.Identifier, Strings.U_Ai_AtelFuncName);
            Expect(";");
            return new AtelCall(name.Text);
        }

        private AtelStmt ParseAssign()
        {
            AtelToken name = Expect(AtelTokenKind.Identifier, Strings.U_Ai_AtelVarName);
            Expect("=");
            AtelExpr value = ParseExpr();
            Expect(";");
            return new AtelAssign(name.Text, value);
        }

        private AtelBlock ParseBlock()
        {
            Expect("{");
            var stmts = new List<AtelStmt>();
            while (!Is("}"))
            {
                if (Is(AtelTokenKind.EndOfFile))
                    throw AtelSyntaxException.Expected("'}'", Peek());
                stmts.Add(ParseStmt());
            }
            Expect("}");
            return new AtelBlock(stmts);
        }

        // ── expressões ──────────────────────────────────────────────────────────────

        private AtelExpr ParseExpr() => ParseOr();

        private AtelExpr ParseOr()
        {
            AtelExpr left = ParseAnd();
            while (Is("||"))
            {
                string op = Advance().Text;
                AtelExpr right = ParseAnd();
                left = new AtelBinary(op, left, right);
            }
            return left;
        }

        private AtelExpr ParseAnd()
        {
            AtelExpr left = ParseComparison();
            while (Is("&&"))
            {
                string op = Advance().Text;
                AtelExpr right = ParseComparison();
                left = new AtelBinary(op, left, right);
            }
            return left;
        }

        private AtelExpr ParseComparison()
        {
            AtelExpr left = ParseAdditive();
            while (Is("==") || Is("!=") || Is("<") || Is(">") || Is("<=") || Is(">="))
            {
                string op = Advance().Text;
                AtelExpr right = ParseAdditive();
                left = new AtelBinary(op, left, right);
            }
            return left;
        }

        private AtelExpr ParseAdditive()
        {
            AtelExpr left = ParseMultiplicative();
            while (Is("+") || Is("-"))
            {
                string op = Advance().Text;
                AtelExpr right = ParseMultiplicative();
                left = new AtelBinary(op, left, right);
            }
            return left;
        }

        private AtelExpr ParseMultiplicative()
        {
            AtelExpr left = ParseUnary();
            while (Is("*") || Is("/") || Is("%"))
            {
                string op = Advance().Text;
                AtelExpr right = ParseUnary();
                left = new AtelBinary(op, left, right);
            }
            return left;
        }


        private AtelExpr ParseUnary()
        {
            if (Is("-") || Is("!"))
            {
                string op = Advance().Text;
                return new AtelUnary(op, ParseUnary());
            }
            return ParsePrimary();
        }

        private AtelExpr ParsePrimary()
        {
            AtelToken t = Peek();
            if (t.Kind == AtelTokenKind.IntLiteral)
            {
                Advance();
                int value = t.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToInt32(t.Text[2..], 16)
                    : int.Parse(t.Text);
                return new AtelLiteral(value);
            }
            if (t.Kind == AtelTokenKind.Identifier)
            {
                Advance();
                return new AtelVarRef(t.Text);
            }
            if (t.Kind == AtelTokenKind.Operator && t.Text == "(")
            {
                Advance();
                AtelExpr inner = ParseExpr();
                Expect(")");
                return inner;
            }
            throw AtelSyntaxException.Unexpected(string.Format(Strings.U_Ai_AtelExpectedExpression, t.Text), t.Line, t.Column);
        }

        // ── helpers ─────────────────────────────────────────────────────────────────

        private AtelToken Peek() => _tokens[Math.Min(_idx, _tokens.Count - 1)];

        private AtelToken Advance()
        {
            AtelToken t = Peek();
            if (_idx < _tokens.Count - 1)
                _idx++;
            return t;
        }

        private bool Is(AtelTokenKind kind) => Peek().Kind == kind;

        private bool Is(string op) => Peek().Kind == AtelTokenKind.Operator && Peek().Text == op;

        private bool Match(string op)
        {
            if (Is(op))
            {
                Advance();
                return true;
            }
            return false;
        }

        private AtelToken Expect(AtelTokenKind kind, string what)
        {
            if (Peek().Kind != kind)
                throw AtelSyntaxException.Expected(what, Peek());
            return Advance();
        }

        private void Expect(string op)
        {
            if (!Is(op))
                throw AtelSyntaxException.Expected($"'{op}'", Peek());
            Advance();
        }
    }
}

