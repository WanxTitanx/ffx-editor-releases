using System;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai.AtelScript;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai
{
    /// <summary>
    /// F5-L1 (docs/reverse/PPP_ATEL_ASSEMBLER_F5_FEASIBILITY_2026-08-01.md): lexer + parser da
    /// linguagem ATEL de alto nível — sintaxe mínima (var/if-else/return/assign/expr).
    /// </summary>
    public class AtelScriptParserTests
    {
        static AtelProgram Parse(string src) => new AtelParser(src).ParseProgram();

        // --- (a) var decl simples ----------------------------------------------------

        [Fact]
        public void Parse_VarDecl_Simple()
        {
            var p = Parse("var x = 5;");

            Assert.Single(p.Statements);
            var v = Assert.IsType<AtelVarDecl>(p.Statements[0]);
            Assert.Equal("x", v.Name);
            Assert.Equal(5, Assert.IsType<AtelLiteral>(v.Initializer!).Value);
        }

        // --- (b) var decl sem inicializador + hex ------------------------------------

        [Fact]
        public void Parse_VarDecl_NoInit_And_Hex()
        {
            var p = Parse("var a; var b = 0x10;");

            Assert.Equal(2, p.Statements.Count);
            Assert.Null(Assert.IsType<AtelVarDecl>(p.Statements[0]).Initializer);
            var b = Assert.IsType<AtelVarDecl>(p.Statements[1]);
            Assert.Equal(16, Assert.IsType<AtelLiteral>(b.Initializer!).Value);
        }

        // --- (c) if/else -------------------------------------------------------------

        [Fact]
        public void Parse_IfElse()
        {
            var p = Parse("if x < 5 { x = x + 1; } else { x = 0; }");

            var stmt = Assert.IsType<AtelIf>(p.Statements[0]);
            var cond = Assert.IsType<AtelBinary>(stmt.Condition);
            Assert.Equal("<", cond.Op);
            Assert.IsType<AtelVarRef>(cond.Left);
            Assert.Equal(5, Assert.IsType<AtelLiteral>(cond.Right).Value);
            Assert.Single(stmt.Then);
            Assert.IsType<AtelAssign>(stmt.Then[0]);
            Assert.NotNull(stmt.Else);
            Assert.IsType<AtelAssign>(stmt.Else![0]);
        }

        // --- (d) if sem else ---------------------------------------------------------

        [Fact]
        public void Parse_If_WithoutElse()
        {
            var p = Parse("if x >= 1 { return; }");

            var stmt = Assert.IsType<AtelIf>(p.Statements[0]);
            Assert.Equal(">=", Assert.IsType<AtelBinary>(stmt.Condition).Op);
            Assert.Null(stmt.Else);
            Assert.IsType<AtelReturn>(stmt.Then[0]);
        }

        // --- (e) precedência: a + b * c ----------------------------------------------

        [Fact]
        public void Parse_Precedence_MultBeforeAdd()
        {
            var p = Parse("var r = a + b * c;");

            var init = Assert.IsType<AtelVarDecl>(p.Statements[0]).Initializer;
            var add = Assert.IsType<AtelBinary>(init!);
            Assert.Equal("+", add.Op);
            Assert.IsType<AtelVarRef>(add.Left);
            var mul = Assert.IsType<AtelBinary>(add.Right);
            Assert.Equal("*", mul.Op);
        }

        // --- (f) comparação encadeada + parênteses -----------------------------------

        [Fact]
        public void Parse_ComparisonAndParens()
        {
            var p = Parse("if (a + 1) <= 10 { a = a + 2; }");

            var stmt = Assert.IsType<AtelIf>(p.Statements[0]);
            var cmp = Assert.IsType<AtelBinary>(stmt.Condition);
            Assert.Equal("<=", cmp.Op);
            var add = Assert.IsType<AtelBinary>(cmp.Left);
            Assert.Equal("+", add.Op);
        }

        // --- (g) else-if -------------------------------------------------------------

        [Fact]
        public void Parse_ElseIf()
        {
            var p = Parse("if a == 1 { a = 2; } else if a == 2 { a = 3; } else { a = 0; }");

            var outer = Assert.IsType<AtelIf>(p.Statements[0]);
            Assert.NotNull(outer.Else);
            Assert.IsType<AtelIf>(outer.Else![0]);
        }

        // --- (h) return com valor ----------------------------------------------------

        [Fact]
        public void Parse_ReturnWithValue()
        {
            var p = Parse("return x * 2;");

            var r = Assert.IsType<AtelReturn>(p.Statements[0]);
            Assert.NotNull(r.Value);
            Assert.Equal("*", Assert.IsType<AtelBinary>(r.Value!).Op);
        }

        // --- (i) comentários // e /* */ ----------------------------------------------

        [Fact]
        public void Lexer_Comments_AreSkipped()
        {
            var p = Parse("// linha\nvar x = 1; /* bloco\nmultilinha */ var y = 2;");

            Assert.Equal(2, p.Statements.Count);
        }

        // --- (j) battle var $ --------------------------------------------------------

        [Fact]
        public void Lexer_DollarIdentifiers_Supported()
        {
            var p = Parse("var $battleVar0014 = 0;");

            Assert.Equal("$battleVar0014", Assert.IsType<AtelVarDecl>(p.Statements[0]).Name);
        }

        // --- (k) erro: token inesperado ----------------------------------------------

        [Fact]
        public void Parse_UnexpectedToken_ThrowsWithPosition()
        {
            // Keep this English assertion independent of the Windows user's UI language.
            // The scope restores only this execution context; no preference file is written.
            using var language = TestUiCultureScope.English();
            var ex = Assert.Throws<AtelSyntaxException>(() => Parse("var x = ;"));

            Assert.Contains("expected expression", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(ex.Line >= 1);
            Assert.True(ex.Column >= 1);
        }

        // --- (l) erro: EOF no meio do bloco ------------------------------------------

        [Fact]
        public void Parse_UnclosedBlock_Throws()
        {
            var ex = Assert.Throws<AtelSyntaxException>(() => Parse("if x < 5 { x = 1;"));

            Assert.Contains("'}'", ex.Message);
        }

        // --- (m) while agora é suportado (L3) → parseia como AtelWhile ------------------

        [Fact]
        public void Parse_While_IsSupported()
        {
            var p = Parse("while x < 5 { x = x + 1; }");

            var wh = Assert.IsType<AtelWhile>(p.Statements[0]);
            Assert.Equal("<", Assert.IsType<AtelBinary>(wh.Condition).Op);
            Assert.Single(wh.Body);
        }

        // --- (n) erro: atribuição sem '=' --------------------------------------------

        [Fact]
        public void Parse_AssignMissingEquals_Throws()
        {
            var ex = Assert.Throws<AtelSyntaxException>(() => Parse("x 5;"));

            Assert.Contains("'='", ex.Message);
        }

        // --- (o) unário negativo -----------------------------------------------------

        [Fact]
        public void Parse_UnaryMinus()
        {
            var p = Parse("var x = -5;");

            var init = Assert.IsType<AtelVarDecl>(p.Statements[0]).Initializer;
            var u = Assert.IsType<AtelUnary>(init!);
            Assert.Equal("-", u.Op);
            Assert.Equal(5, Assert.IsType<AtelLiteral>(u.Operand).Value);
        }
    }
}

