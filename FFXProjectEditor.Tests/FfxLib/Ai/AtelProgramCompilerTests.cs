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
    /// F5-L3: AtelProgramCompiler — var persistente (cria privada), while (loop via jump table)
    /// e func/call (macro inline). Round-trip nos fixtures reais m001/m000.
    /// </summary>
    public class AtelProgramCompilerTests
    {
        const byte PUSHV = 0x9F, POPV = 0xA0, PUSHII = 0xAE;
        const byte EQ = 0x06, LT = 0x0B;

        static AtelProgram Parse(string src) => new AtelParser(src).ParseProgram();

        static (AiScriptFile Script, AiWorker Worker, int Entrypoint) Load(string fixture)
        {
            byte[] monster = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", fixture));
            byte[]? ai = AiScript_File.SliceAiFileFromMonster(monster);
            Assert.NotNull(ai);
            AiScriptFile script = AiScript_File.Read(ai!);
            AiWorker? worker = AiAutomation.PickCombatWorker(script);
            Assert.NotNull(worker);
            int entrypoint = AiAutomation.PickMainEntrypoint(script, worker!);
            return (script, worker!, entrypoint);
        }

        static AtelCompileContext Ctx(AiScriptFile script, AiWorker worker, int entrypoint) => new()
        {
            Script = script,
            WorkerIndex = worker.Index,
            EntrypointIndex = entrypoint,
        };

        static AiScriptFile Reread(AtelCompileResult result)
        {
            AiScriptFile reread = AiScript_File.Read(result.AiFile);
            Assert.True(reread.CodeWalkClosedExactly, "walk deve fechar exato após a compilação");
            Assert.Empty(reread.UnknownOpcodes);
            return reread;
        }

        // --- 1. var persistente: cria privada nova e inicializa ------------------------

        [Fact]
        public void VarPersistente_CriaPrivada_EInicializa()
        {
            var (script, worker, ep) = Load("m001.bin");
            int varsBefore = script.Variables.Count;

            var program = Parse("var meuCounter = 0;");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            AiScriptFile reread = Reread(result);
            Assert.True(reread.Variables.Count > varsBefore, "deve criar uma variável privada nova");
            var priv = reread.Variables.FirstOrDefault(v => v.Name.StartsWith("priv", StringComparison.Ordinal));
            Assert.NotNull(priv);
            // inicialização: POPV da privada no código
            Assert.Contains(reread.Instructions, i => i.Opcode == POPV && i.Operand == priv!.Index);
        }

        // --- 2. var persistente: nome existente não duplica -----------------------------

        [Fact]
        public void VarPersistente_NomeExistente_NaoCriaNova()
        {
            var (script, worker, ep) = Load("m001.bin");
            int varsBefore = script.Variables.Count;

            var program = Parse("var battleVar0014 = 1;");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            Assert.Equal(varsBefore, Reread(result).Variables.Count);
        }

        // --- 3. while: emit → round-trip com D7/B0 + jump table crescida -----------------

        [Fact]
        public void While_RoundTrips_WithJumpTable()
        {
            var (script, worker, ep) = Load("m001.bin");
            int jumpsBefore = worker.JumpTargets.Count;

            var program = Parse("while battleVar0014 < 5 { battleVar0014 = battleVar0014 + 1; }");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            Assert.Equal(1, result.AppliedSteps);
            AiScriptFile reread = Reread(result);
            // o loop tem: guard + D7 (exit) + corpo + B0 (loop)
            Assert.Contains(reread.Instructions, i => i.Opcode == 0xD7);
            Assert.Contains(reread.Instructions, i => i.Opcode == 0xB0);
            Assert.Contains(reread.Instructions, i => i.Opcode == LT);
            // jump table cresceu em 2 slots
            var rereadWorker = reread.Workers.First(w => w.Index == worker.Index);
            Assert.Equal(jumpsBefore + 2, rereadWorker.JumpTargets.Count);
            // os targets novos: exit = entrypoint original, loop = baseRel (início do guard)
            Assert.Equal(worker.Entrypoints[ep], rereadWorker.JumpTargets[^2]);
            Assert.Equal(script.CodeLength, rereadWorker.JumpTargets[^1]);
        }

        // --- 4. func/call: macro inline expandido ---------------------------------------

        [Fact]
        public void FuncCall_ExpandeInline()
        {
            var (script, worker, ep) = Load("m001.bin");
            int countBefore = script.Instructions.Count;

            var program = Parse("func inc { battleVar0014 = battleVar0014 + 1; } call inc;");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            Assert.Equal(1, result.AppliedSteps);
            AiScriptFile reread = Reread(result);
            Assert.True(reread.Instructions.Count > countBefore);
            Assert.Contains(reread.Instructions, i => i.Opcode == 0x14); // ADD do corpo expandido
            Assert.Contains(result.Summary, "call inc", StringComparison.Ordinal);
        }

        // --- 5. call sem func declarada → erro claro ------------------------------------

        [Fact]
        public void CallSemFunc_Throws()
        {
            var (script, worker, ep) = Load("m001.bin");

            var program = Parse("call naoExiste;");

            var ex = Assert.Throws<AtelEmitException>(() =>
                AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep)));

            Assert.Equal(string.Format(Strings.U_Ai_AtelFunctionNotDeclaredFormat, "naoExiste"), ex.Message);
        }

        // --- 6. if/while combinados no mesmo programa -----------------------------------

        [Fact]
        public void Program_IfEWhileCombinados()
        {
            var (script, worker, ep) = Load("m001.bin");

            var program = Parse(
                "if battleVar0014 < 10 { battleVar0014 = 0; } while battleVar0014 < 3 { battleVar0014 = battleVar0014 + 1; }");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            Assert.Equal(2, result.AppliedSteps);
            Reread(result);
        }

        // --- 7. m000 (sem private storage) → erro claro honesto ------------------------

        [Fact]
        public void VarPersistente_m000_SemPrivateStorage_ThrowsClearError()
        {
            var (script, worker, ep) = Load("m000.bin");
            Assert.Empty(script.Variables);
            bool foundSlot = AiScript_File.TryFindFreePrivateVariableSlot(script, out int slot, out string reason);
            Assert.False(foundSlot, $"expected no free private slot but found {slot}");

            var program = Parse("var x = 7;");

            // m000 não declara private storage em nenhum worker — criar var do zero exigiria
            // escrita no worker descriptor (fora do escopo L3): o compilador falha com erro claro.
            var ex = Assert.Throws<AtelEmitException>(() =>
                AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep)));

            Assert.Equal(string.Format(Strings.U_Ai_AtelNoFreePrivateSlotFormat, "x", reason), ex.Message);
        }

        // --- 8. while com corpo que usa call --------------------------------------------

        [Fact]
        public void WhileComCallNoCorpo()
        {
            var (script, worker, ep) = Load("m001.bin");

            var program = Parse(
                "func passo { battleVar0014 = battleVar0014 + 1; } while battleVar0014 < 4 { call passo; }");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            Assert.Equal(1, result.AppliedSteps); // só o while é top-level (o call está no corpo)
            AiScriptFile reread = Reread(result);
            Assert.Contains(reread.Instructions, i => i.Opcode == 0xD7);
            Assert.Contains(reread.Instructions, i => i.Opcode == 0xB0);
        }

        // --- 9. guard com > (ordem invertida) dentro de while ----------------------------

        [Fact]
        public void While_ComGreaterThan_SemanticaCorrigida()
        {
            var (script, worker, ep) = Load("m001.bin");

            var program = Parse("while battleVar0014 > 0 { battleVar0014 = battleVar0014 - 1; }");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            AiScriptFile reread = Reread(result);
            // > emite [PUSHII 0, PUSHV var, LT] — o literal vem ANTES da var (ordem invertida)
            var ins = reread.Instructions;
            int lt = Array.FindIndex(ins.ToArray(), i => i.Opcode == LT);
            Assert.True(lt >= 2, "LT deve existir no guard");
            Assert.Equal(PUSHII, ins[lt - 2].Opcode);
            Assert.Equal((ushort)0, ins[lt - 2].Operand);
            Assert.Equal(PUSHV, ins[lt - 1].Opcode);
            Assert.Contains(reread.Instructions, i => i.Opcode == 0xB0); // loop
        }



        // --- 10. if/else: round-trip com jump table +2, D7/B0/RET -----------------------

        [Fact]
        public void IfElse_RoundTrips_WithJumpTable()
        {
            var (script, worker, ep) = Load("m001.bin");
            int jumpsBefore = worker.JumpTargets.Count;

            var program = Parse("if battleVar0014 < 5 { battleVar0014 = 0; } else { battleVar0014 = 9; }");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            Assert.Equal(1, result.AppliedSteps);
            AiScriptFile reread = Reread(result);
            Assert.Contains(reread.Instructions, i => i.Opcode == 0xD7);  // guard falso → else
            Assert.Contains(reread.Instructions, i => i.Opcode == 0xB0);  // then → original
            Assert.Contains(reread.Instructions, i => i.Opcode == 0x3C);  // RET no fim do else
            var rereadWorker = reread.Workers.First(w => w.Index == worker.Index);
            Assert.Equal(jumpsBefore + 2, rereadWorker.JumpTargets.Count);
            // slot else → inicio do else (baseRel + guard + D7 + then); slot end → entrypoint original
            Assert.Equal(worker.Entrypoints[ep], rereadWorker.JumpTargets[^1]);
            Assert.True(rereadWorker.JumpTargets[^2] > script.CodeLength);
        }

        // --- 11. else-if → erro claro ---------------------------------------------------

        [Fact]
        public void IfElse_ElseIf_ThrowsClearError()
        {
            var (script, worker, ep) = Load("m001.bin");

            var program = Parse("if battleVar0014 == 1 { battleVar0014 = 2; } else if battleVar0014 == 2 { battleVar0014 = 3; }");

            var ex = Assert.Throws<AtelEmitException>(() =>
                AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep)));

            Assert.Contains("else-if", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // --- 12. && → LAND (0x02) -------------------------------------------------------

        [Fact]
        public void Guard_LogicalAnd_EmitsLAND()
        {
            var (script, worker, ep) = Load("m001.bin");

            var program = Parse("if battleVar0014 < 5 && battleVar0014 > 0 { battleVar0014 = 0; }");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            AiScriptFile reread = Reread(result);
            Assert.Contains(reread.Instructions, i => i.Opcode == 0x02); // LAND
            Assert.Equal(2, reread.Instructions.Count(i => i.Opcode == LT)); // duas comparações
        }

        // --- 13. || → LOR (0x01) --------------------------------------------------------

        [Fact]
        public void Guard_LogicalOr_EmitsLOR()
        {
            var (script, worker, ep) = Load("m001.bin");

            var program = Parse("if battleVar0014 < 5 || battleVar0014 == 7 { battleVar0014 = 0; }");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            AiScriptFile reread = Reread(result);
            Assert.Contains(reread.Instructions, i => i.Opcode == 0x01); // LOR
            Assert.Contains(reread.Instructions, i => i.Opcode == EQ);
        }

        // --- 14. if/else + while combinados ----------------------------------------------

        [Fact]
        public void Program_IfElseComWhile()
        {
            var (script, worker, ep) = Load("m001.bin");

            var program = Parse(
                "if battleVar0014 < 2 { battleVar0014 = 5; } else { battleVar0014 = 0; } while battleVar0014 > 0 { battleVar0014 = battleVar0014 - 1; }");

            AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, Ctx(script, worker, ep));

            Assert.Equal(2, result.AppliedSteps);
            Reread(result);
        }
    }
}

