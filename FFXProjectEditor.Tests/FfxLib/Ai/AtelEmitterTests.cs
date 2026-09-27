using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.AtelScript;
using FFXProjectEditor.Resources;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai
{
    /// <summary>
    /// F5-L2: AtelEmitter — emissão de guards com a SEMÂNTICA CORRIGIDA (0x0F = base&lt;=topo,
    /// 0x0B = base&lt;topo; "maior-que" = ordem invertida) + compilação de ifs reais via
    /// AppendGuardedAction (round-trip no fixture m001).
    /// </summary>
    public class AtelEmitterTests
    {
        const byte PUSHV = 0x9F, POPV = 0xA0, PUSHII = 0xAE;
        const byte EQ = 0x06, NE = 0x07, LT = 0x0B, LE = 0x0F;
        const byte ADD = 0x14, SUB = 0x15, MUL = 0x16, DIV = 0x17, MOD = 0x18;

        static ushort Var(string name) => name switch
        {
            "x" => 0,
            "a" => 0,
            "b" => 1,
            "c" => 2,
            _ => throw new InvalidOperationException($"var desconhecida {name}"),
        };

        static AtelProgram Parse(string src) => new AtelParser(src).ParseProgram();

        static AtelBinary FirstCondition(string src) =>
            Assert.IsType<AtelIf>(Parse(src).Statements[0]).Condition as AtelBinary
            ?? throw new InvalidOperationException("condição não é binária");

        // --- 1. x < 5 → [PUSHV, PUSHII, 0x0B] ----------------------------------------

        [Fact]
        public void EmitComparison_LessThan_NormalOrder()
        {
            var ins = AtelEmitter.EmitComparison(FirstCondition("if x < 5 { x = 0; }"), Var);

            Assert.Collection(ins,
                i => Assert.Equal((PUSHV, (ushort)0), (i.Opcode, i.Operand)),
                i => Assert.Equal((PUSHII, (ushort)5), (i.Opcode, i.Operand)),
                i => Assert.Equal(LT, i.Opcode));
        }

        // --- 2. x <= 5 → [PUSHV, PUSHII, 0x0F] ---------------------------------------

        [Fact]
        public void EmitComparison_LessOrEqual_Uses0x0F()
        {
            var ins = AtelEmitter.EmitComparison(FirstCondition("if x <= 5 { x = 0; }"), Var);

            Assert.Equal(new byte[] { PUSHV, PUSHII, LE }, ins.Select(i => i.Opcode).ToArray());
        }

        // --- 3. x > 5 → [PUSHII 5, PUSHV x, 0x0B] (ORDEM INVERTIDA — corrigido!) -------

        [Fact]
        public void EmitComparison_GreaterThan_SwappedOrder()
        {
            var ins = AtelEmitter.EmitComparison(FirstCondition("if x > 5 { x = 0; }"), Var);

            // left > right ≡ right < left → o valor 5 é empurrado PRIMEIRO
            Assert.Collection(ins,
                i => Assert.Equal((PUSHII, (ushort)5), (i.Opcode, i.Operand)),
                i => Assert.Equal((PUSHV, (ushort)0), (i.Opcode, i.Operand)),
                i => Assert.Equal(LT, i.Opcode));
        }

        // --- 4. x >= 5 → [PUSHII 5, PUSHV x, 0x0F] (ordem invertida) -------------------

        [Fact]
        public void EmitComparison_GreaterOrEqual_SwappedOrder()
        {
            var ins = AtelEmitter.EmitComparison(FirstCondition("if x >= 5 { x = 0; }"), Var);

            Assert.Collection(ins,
                i => Assert.Equal((PUSHII, (ushort)5), (i.Opcode, i.Operand)),
                i => Assert.Equal((PUSHV, (ushort)0), (i.Opcode, i.Operand)),
                i => Assert.Equal(LE, i.Opcode));
        }

        // --- 5. == e != ---------------------------------------------------------------

        [Fact]
        public void EmitComparison_EqualAndNotEqual()
        {
            var eq = AtelEmitter.EmitComparison(FirstCondition("if x == 5 { x = 0; }"), Var);
            Assert.Equal(new byte[] { PUSHV, PUSHII, EQ }, eq.Select(i => i.Opcode).ToArray());

            var ne = AtelEmitter.EmitComparison(FirstCondition("if x != 5 { x = 0; }"), Var);
            Assert.Equal(new byte[] { PUSHV, PUSHII, NE }, ne.Select(i => i.Opcode).ToArray());
        }

        // --- 6. aritmética: a + b * 2 → [a, b, 2, MUL, ADD] ----------------------------

        [Fact]
        public void EmitExpr_Aritmetica_PosOrdem()
        {
            var init = Assert.IsType<AtelVarDecl>(Parse("var r = a + b * 2;").Statements[0]).Initializer!;
            var ins = AtelEmitter.EmitExpr(init, Var);

            Assert.Collection(ins,
                i => Assert.Equal((PUSHV, (ushort)0), (i.Opcode, i.Operand)), // a
                i => Assert.Equal((PUSHV, (ushort)1), (i.Opcode, i.Operand)), // b
                i => Assert.Equal((PUSHII, (ushort)2), (i.Opcode, i.Operand)), // 2
                i => Assert.Equal(MUL, i.Opcode),
                i => Assert.Equal(ADD, i.Opcode));
        }

        // --- 7. unário: -x → [0, x, SUB] ------------------------------------------------

        [Fact]
        public void EmitExpr_UnaryMinus()
        {
            var init = Assert.IsType<AtelVarDecl>(Parse("var r = -x;").Statements[0]).Initializer!;
            var ins = AtelEmitter.EmitExpr(init, Var);

            Assert.Collection(ins,
                i => Assert.Equal((PUSHII, (ushort)0), (i.Opcode, i.Operand)),
                i => Assert.Equal((PUSHV, (ushort)0), (i.Opcode, i.Operand)),
                i => Assert.Equal(SUB, i.Opcode));
        }

        // --- 8. assign: x = a + 1 → [a, 1, ADD, POPV x] --------------------------------

        [Fact]
        public void EmitAssignAction_PushesThenPops()
        {
            var assign = Assert.IsType<AtelAssign>(Parse("x = a + 1;").Statements[0]);
            var ins = AtelEmitter.EmitAssignAction(assign, Var);

            Assert.Collection(ins,
                i => Assert.Equal((PUSHV, (ushort)0), (i.Opcode, i.Operand)), // a
                i => Assert.Equal((PUSHII, (ushort)1), (i.Opcode, i.Operand)), // 1
                i => Assert.Equal(ADD, i.Opcode),
                i => Assert.Equal((POPV, (ushort)0), (i.Opcode, i.Operand))); // x
        }

        // --- 9. CompileProgram num AiFile real (m001) → round-trip ---------------------

        [Fact]
        public void CompileProgram_RealFixture_RoundTrips()
        {
            byte[] monster = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", "m001.bin"));
            byte[]? ai = AiScript_File.SliceAiFileFromMonster(monster);
            Assert.NotNull(ai);
            AiScriptFile script = AiScript_File.Read(ai!);
            AiWorker? worker = AiAutomation.PickCombatWorker(script);
            Assert.NotNull(worker);
            int entrypoint = AiAutomation.PickMainEntrypoint(script, worker!);

            var program = Parse("if battleVar0014 < 5 { battleVar0014 = battleVar0014 + 1; }");

            AtelCompileResult result = AtelEmitter.CompileProgram(program, new AtelCompileContext
            {
                Script = script,
                WorkerIndex = worker!.Index,
                EntrypointIndex = entrypoint,
            });

            Assert.Equal(1, result.AppliedSteps);

            AiScriptFile reread = AiScript_File.Read(result.AiFile);
            Assert.True(reread.CodeWalkClosedExactly, "walk deve fechar exato após o append");
            Assert.Empty(reread.UnknownOpcodes);
            // o guard emitido (ordem normal para <) deve existir no código re-lido
            Assert.Contains(reread.Instructions, i => i.Opcode == LT);
            Assert.Contains(reread.Instructions, i => i.Opcode == ADD);
        }

        // --- 10. variável inexistente → erro claro -------------------------------------

        [Fact]
        public void ResolveVariable_Missing_ThrowsClearError()
        {
            byte[] monster = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", "m001.bin"));
            byte[]? ai = AiScript_File.SliceAiFileFromMonster(monster);
            var script = AiScript_File.Read(ai!);

            var ex = Assert.Throws<AtelEmitException>(() => AtelEmitter.ResolveVariable(script, "naoExiste"));

            string known = string.Join(", ", script.Variables.Select(x => x.Name).Take(10));
            string suffix = script.Variables.Count > 10 ? ", ..." : "";
            Assert.Equal(string.Format(Strings.U_Ai_AtelVariableMissingFormat, "naoExiste", known, suffix), ex.Message);
        }

        // --- 11. statement de topo não suportado → erro --------------------------------

        [Fact]
        public void CompileProgram_UnsupportedTopLevel_Throws()
        {
            byte[] monster = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", "m001.bin"));
            var script = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(monster)!);
            AiWorker? worker = AiAutomation.PickCombatWorker(script);
            Assert.NotNull(worker);
            int entrypoint = AiAutomation.PickMainEntrypoint(script, worker!);

            var program = Parse("return 5;"); // return no topo não é suportado no L2

            var ex = Assert.Throws<AtelEmitException>(() => AtelEmitter.CompileProgram(program,
                new AtelCompileContext { Script = script, WorkerIndex = worker!.Index, EntrypointIndex = entrypoint }));

            Assert.Equal(Strings.U_Ai_AtelTopLevelReturnUnsupported, ex.Message);
        }

        // --- 12. condição não-comparativa → erro ---------------------------------------

        [Fact]
        public void EmitGuard_NonComparison_Throws()
        {
            var init = Assert.IsType<AtelVarDecl>(Parse("var r = x + 1;").Statements[0]).Initializer!;
            var ex = Assert.Throws<AtelEmitException>(() => AtelEmitter.EmitGuard(init, Var));
            Assert.Equal(string.Format(Strings.U_Ai_AtelInvalidComparisonOperatorFormat, "+"), ex.Message);
        }
    }
}

