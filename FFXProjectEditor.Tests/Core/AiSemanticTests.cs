using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// L1 of docs/ai/P2_IR_SEMANTICA_2026-07-31.md: read-only semantic IR (AiSemanticScript)
    /// + three-layer diff (AiDiffThreeLayer) over real monster fixtures and a synthetic AiFile.
    ///
    /// Fixtures: Fixtures/Monster/m000.bin and m001.bin are vanilla monster_*.bin files whose
    /// AiFile partition is sliced via AiScript_File.SliceAiFileFromMonster (the codec's own API).
    /// m000 was verified by hand: AiFile = [0x30..0x470), 1 worker, script "3C D8 5F 00 3C 3C"
    /// (codeLength 6, 3 entrypoints, walk closes exactly).
    /// </summary>
    public class AiSemanticTests
    {
        // --- fixtures --------------------------------------------------------------

        public static TheoryData<string> MonsterFixtures => new()
        {
            { "m000.bin" },
            { "m001.bin" },
        };

        static byte[] MonsterFixtureBytes(string name) =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", name));

        static byte[] SliceAiFile(string monsterFixtureName)
        {
            byte[]? ai = AiScript_File.SliceAiFileFromMonster(MonsterFixtureBytes(monsterFixtureName));
            Assert.NotNull(ai);
            return ai!;
        }


        /// <summary>Minimal but valid synthetic AiFile: 1 worker (entrypoint[0]=0, jump[0]=0),
        /// 3 known instructions: PUSHII 0x3049 (Firaga) + PUSHF 0 + RET. RT0 byte-identical
        /// through Read/Write (asserted by SyntheticFixture_RoundTripsByteIdentical).</summary>
        static byte[] CreateSyntheticAiFile()
        {
            const int scriptStart = 0x6C;
            byte[] ai = new byte[0x73];

            WriteU32(ai, 0x00, 7);                 // codeLength = 3 + 3 + 1
            WriteU32(ai, 0x10, (uint)ai.Length);   // declaredLength
            WriteU16(ai, 0x14, 1);                 // workersTotal
            WriteU32(ai, 0x30, scriptStart);       // scriptStart
            WriteU16(ai, 0x36, 1);                 // workerCount
            WriteU32(ai, 0x38, 0x3C);              // worker[0] descriptor offset

            // worker descriptor @0x3C (fields the codec parses)
            WriteU16(ai, 0x3C + 0x08, 1);          // entrypoint count
            WriteU16(ai, 0x3C + 0x0A, 1);          // jump count
            WriteU16(ai, 0x3C + 0x10, 0);          // private data length
            WriteU32(ai, 0x3C + 0x14, 0);          // varsOff
            WriteU32(ai, 0x3C + 0x18, 0);          // varsEnd / int pool
            WriteU32(ai, 0x3C + 0x1C, 0);          // float pool
            WriteU32(ai, 0x3C + 0x20, 0x64);       // entrypoint table
            WriteU32(ai, 0x3C + 0x24, 0x68);       // jump table

            WriteU32(ai, 0x64, 0);                 // entrypoint[0] (code-relative 0)
            WriteU32(ai, 0x68, 0);                 // jump[0] (code-relative 0)

            // code @0x6C: AE 49 30 (PUSHII 0x3049 = Firaga) | AF 00 00 (PUSHF 0) | 3C (RET)
            ai[0x6C] = 0xAE; ai[0x6D] = 0x49; ai[0x6E] = 0x30;
            ai[0x6F] = 0xAF; ai[0x70] = 0x00; ai[0x71] = 0x00;
            ai[0x72] = 0x3C;
            return ai;
        }

        /// <summary>Read the synthetic fixture, apply a same-length operand edit to the decoded
        /// model, and re-write via the codec (AiInstruction.Operand is settable; Write re-emits).</summary>
        static (byte[] Before, byte[] After) SyntheticFixturePair(Action<List<AiInstruction>> edit)
        {
            byte[] before = CreateSyntheticAiFile();
            AiScriptFile script = AiScript_File.Read(before);
            edit(script.Instructions.ToList());
            byte[] after = AiScript_File.Write(script);
            return (before, after);
        }

        static void WriteU16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        static void WriteU32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        // --- (a) Build over real monster fixtures ------------------------------------

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void Build_FromRealMonsterAiFile_ProducesOneNodePerOwnerInstruction(string fixtureName)
        {
            byte[] ai = SliceAiFile(fixtureName);
            AiScriptFile script = AiScript_File.Read(ai);

            Assert.True(script.HasScript, fixtureName + " deve conter script ATEL (AiFile real do corpus)");
            Assert.True(script.CodeWalkClosedExactly, fixtureName + " deve walkar fechado exato (pré-condição RT0 da fixture)");

            AiSemanticScript ir = AiSemanticScript.Build(script);

            // cada instrução do codec vira pelo menos um node; multi-owner expande
            Assert.True(ir.Nodes.Count >= script.Instructions.Count);
            // o conjunto de offsets dos nodes == o conjunto de offsets das instruções
            Assert.Equal(
                script.Instructions.Select(i => i.Offset).Distinct().OrderBy(o => o),
                ir.Nodes.Select(n => n.Offset).Distinct().OrderBy(o => o));
            // todo node tem par (opcode, offset) consistente com o codec
            foreach (AiInstructionNode node in ir.Nodes)
                Assert.Contains(script.Instructions,
                    i => i.Offset == node.Offset && i.Opcode == node.Opcode);
        }

        [Fact]
        public void Build_FromM000RealFixture_MatchesKnownScriptBytes()
        {
            AiScriptFile script = AiScript_File.Read(SliceAiFile("m000.bin"));
            AiSemanticScript ir = AiSemanticScript.Build(script);

            // fixture versionada: 1 worker, script 3C D8 5F 00 3C 3C (codeLength 6)
            // = 4 instruções: RET | CALLPOPA 0x5F (3 bytes) | RET | RET
            Assert.Equal(1, script.Workers.Count);
            Assert.Equal(4, script.Instructions.Count);
            Assert.Equal(4, ir.Nodes.Count);
            Assert.All(ir.Nodes, n => Assert.Equal(0, n.WorkerIndex));
            Assert.Equal(new[] { 0x78, 0x79, 0x7C, 0x7D }, ir.Nodes.Select(n => n.Offset).OrderBy(o => o).ToArray());
            Assert.Equal(new byte[] { 0x3C, 0x3C, 0x3C, 0xD8 }, ir.Nodes.Select(n => n.Opcode).OrderBy(o => o).ToArray());

            AiInstructionNode call = ir.Nodes.Single(n => n.Opcode == 0xD8);
            Assert.Equal((ushort)0x5F, call.Operand);
            Assert.Equal(AiOperandKind.FuncId, call.OperandKind);
            Assert.Equal("CALLPOPA (void call)", call.Meaning);
        }


        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void Build_Nodes_ReflectOffsetsOpcodesAndOperandsOfTheFile(string fixtureName)
        {
            byte[] ai = SliceAiFile(fixtureName);
            AiScriptFile script = AiScript_File.Read(ai);
            AiSemanticScript ir = AiSemanticScript.Build(script);

            foreach (AiInstructionNode node in ir.Nodes)
            {
                // offsets dentro da região de código do codec
                Assert.InRange(node.Offset, script.ScriptStart, script.ScriptStart + script.CodeLength);
                // OperandKind resolvido do opcode pela API do codec (nunca do modelo à mão)
                Assert.Equal(AiScript_File.OperandKindOf(node.Opcode), node.OperandKind);

                AiInstruction instruction = script.Instructions.Single(i => i.Offset == node.Offset);
                Assert.Equal(instruction.Opcode, node.Opcode);
                Assert.Equal(
                    instruction.HasOperand ? instruction.Operand : (ushort?)null,
                    node.Operand);
            }
        }

        // --- (c) Default evidence ------------------------------------------------------

        [Fact]
        public void Build_DefaultEvidence_IsDecodedWithOpcodeTableSource()
        {
            // m000: 3C / D8 / 3C — nenhum PUSHII de comando; tudo (Decoded, "opcode-table").
            AiSemanticScript ir = AiSemanticScript.Build(AiScript_File.Read(SliceAiFile("m000.bin")));

            Assert.NotEmpty(ir.Nodes);
            Assert.All(ir.Nodes, n =>
            {
                Assert.Equal(AiEvidenceState.Decoded, n.Evidence.State);
                Assert.Equal("opcode-table", n.Evidence.Source);
            });
        }

        [Fact]
        public void Build_KnownCommandOperand_EnrichesMeaningFromAiCommandId()
        {
            AiSemanticScript ir = AiSemanticScript.Build(AiScript_File.Read(CreateSyntheticAiFile()));

            AiInstructionNode pushii = ir.Nodes.Single(n => n.Opcode == 0xAE);
            AiCommandDecode decode = AiCommandId.Decode((ushort)0x3049);
            Assert.True(decode.IsKnown,
                "0x3049 (Firaga) deve estar no CommandCharacter_Dictionary — RT2-proven (AiCommandId.cs:11)");

            Assert.Equal($"PUSHII (push immediate int16) -> {decode.Name}", pushii.Meaning);
            Assert.Equal("opcode-table|bible", pushii.Evidence.Source);
            Assert.Equal(AiEvidenceState.Decoded, pushii.Evidence.State);
        }

        // --- (d) ThreeLayerDiff before == after ----------------------------------------


        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void ThreeLayerDiff_BeforeEqualsAfter_AllLayersEmpty(string fixtureName)
        {
            byte[] ai = SliceAiFile(fixtureName);

            ThreeLayerDiff diff = AiDiffThreeLayer.Compute(ai, ai, hasExtraInfo: false);

            Assert.Empty(diff.ByteDiff);
            Assert.Empty(diff.DisassemblyDiff);
            Assert.Empty(diff.SemanticDiff);
            Assert.True(diff.IsEmpty);
        }

        [Fact]
        public void ThreeLayerDiff_SyntheticBeforeEqualsAfter_AllLayersEmpty()
        {
            byte[] synthetic = CreateSyntheticAiFile();

            ThreeLayerDiff diff = AiDiffThreeLayer.Compute(synthetic, synthetic, hasExtraInfo: false);

            Assert.Empty(diff.ByteDiff);
            Assert.Empty(diff.DisassemblyDiff);
            Assert.Empty(diff.SemanticDiff);
        }

        // --- (e) ThreeLayerDiff with one instruction changed ---------------------------

        [Fact]
        public void ThreeLayerDiff_OperandChangeWithMeaning_AllLayersReport()
        {
            (byte[] before, byte[] after) = SyntheticFixturePair(instructions =>
            {
                AiInstruction pushii = instructions.Single(i => i.Opcode == 0xAE);
                AiCommandDecode decode = AiCommandId.Decode(pushii.Operand);
                Assert.True(decode.IsKnown,
                    "0x3049 (Firaga) deve ser conhecido no AiCommandId — RT2-proven (AiCommandId.cs:11)");
                pushii.Operand = 0x304B; // Firaga -> Thundaga: mesmo comprimento (3 bytes), byte-local
            });

            ThreeLayerDiff diff = AiDiffThreeLayer.Compute(before, after, hasExtraInfo: false);

            // camada de bytes: o byte do operand mudou (49 -> 4B)
            Assert.NotEmpty(diff.ByteDiff);
            Assert.Contains(diff.ByteDiff, line => line.Contains("49->4B", StringComparison.Ordinal));
            // camada de disassembly: entry Modified
            Assert.NotEmpty(diff.DisassemblyDiff);
            Assert.Contains(diff.DisassemblyDiff, line => line.Contains("[Modified]", StringComparison.Ordinal));
            // camada semântica: o Meaning envolve o nome do comando -> mudou
            Assert.NotEmpty(diff.SemanticDiff);
            Assert.Contains(diff.SemanticDiff, line => line.Contains("meaning", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ThreeLayerDiff_OperandChangeWithoutMeaning_SemanticLayerStaysEmpty()
        {
            (byte[] before, byte[] after) = SyntheticFixturePair(instructions =>
            {
                AiInstruction pushf = instructions.Single(i => i.Opcode == 0xAF);
                pushf.Operand = 1; // índice do float pool: bytes mudam, Meaning ("PUSHF ...") não
            });

            ThreeLayerDiff diff = AiDiffThreeLayer.Compute(before, after, hasExtraInfo: false);

            Assert.NotEmpty(diff.ByteDiff);
            Assert.NotEmpty(diff.DisassemblyDiff);
            // sem Meaning/Evidence envolvidos -> camada semântica honestamente vazia
            Assert.Empty(diff.SemanticDiff);
        }

        // --- (f) Synthetic fixture round-trip ------------------------------------------

        [Fact]
        public void SyntheticFixture_RoundTripsByteIdentical()
        {
            byte[] synthetic = CreateSyntheticAiFile();

            Assert.True(AiScript_File.RoundTripsByteIdentical(synthetic),
                "fixture sintética deve ser RT0 byte-idêntica (Write(Read(x)) == x)");

            AiScriptFile script = AiScript_File.Read(synthetic);
            Assert.Equal(3, script.Instructions.Count);
            Assert.True(script.CodeWalkClosedExactly);
            Assert.Equal(0xAE, script.Instructions[0].Opcode);
            Assert.Equal((ushort)0x3049, script.Instructions[0].Operand);
            Assert.Equal(0xAF, script.Instructions[1].Opcode);
            Assert.Equal(0x3C, script.Instructions[2].Opcode);
        }
    }
}

