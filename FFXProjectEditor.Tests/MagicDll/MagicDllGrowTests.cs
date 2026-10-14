using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Testes do GROW de record do Magic DLL Editor (WRITEBACK_SPEC §2.2 — Estratégia A:
    /// shift de W bytes + ajuste de ponteiros + header PE). Cobre a aplicação (SHA/dirty/
    /// tamanho), re-parse externo dos working bytes, largura inválida, sem documento e
    /// round-trip restore. Nunca escreve no corpus — sempre cópia em temp.
    ///
    /// Ajustes à implementação REAL do wrapper (MagicDllDocument_Wrapper.TryGrowRecord):
    /// - W é restrito a {4,8,12,16} (guardrail de alinhamento da spec) — 0/3 rejeitados;
    /// - o grow exige slot com opcode resolvido E família payload_consumer (R7);
    /// - o re-parse do grow é INTERNO (VerifyGrow: walker + RT0 por record + commit da
    ///   árvore via _lastVerifiedReparse) — o teste 2 re-prova externamente com parser novo;
    /// - Rt0Check NÃO se aplica pós-grow (documento sujo por design) — o gate pós-grow é o
    ///   VerifyGrow interno, coberto pelos asserts de estado/árvore dos testes 1 e 2;
    /// - o parser lê RecordWidth = max(32, janela) — o record re-parseado de pppSclMove
    ///   continua com 32B; o campo novo (4B zero-fill) é verificado no .data do grown.
    ///
    /// RESOLVIDO 2026-08-02 (Jarvis-PPP-C2C3): o grow hoje APLICA. Causa raiz do ACHADO
    /// anterior: a receita não cobria as DUAS counted-u32 tables do header da section
    /// (rel @ section+8/+12 — no 0021 em 0x1C444/0x1C4E4, depois do insertion_point
    /// 0x193E0): o shift físico as deslocava +4 sem ajustar os rel fields (nem as
    /// entries >= insertion) → IsValidCountedU32Table lia count lixo → seção inválida →
    /// root inválido → 0 programs. A correção (rel field += W quando a tabela estava >=
    /// insertion; entry += W quando o destino >= insertion) está no TryGrowRecord e os
    /// testes Grow_AplicaEMudaSha/Grow_ReParseLega/Grow_RoundTripRestore voltaram às
    /// expectativas originais da spec.
    /// </summary>
    public class MagicDllGrowTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;

        private static string CorpusPath(string dllName) => Path.Combine(CorpusDir, dllName);

        private static string MakeTempDir()
        {
            string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx_magicgrow_" + Guid.NewGuid().ToString("N"));
            TestDirectory.CreatePrivate(dir);
            return dir;
        }

        private static string CopyCorpusToTemp(string tempDir, string dllName)
        {
            string dest = Path.Combine(tempDir, dllName);
            File.Copy(Path.Combine(CorpusDir, dllName), dest, overwrite: true);
            return dest;
        }

        /// <summary>
        /// Primeiro slot pppSclMove (payload_consumer, record 32B U1) ordenado por
        /// RecordOffset. O grow da spec §2.3 usa o record 0x193C0 do 0021 (handler 7 —
        /// provado pelo T3 da lane MAGIC); se o fp.h local nomear mais de um pppSclMove,
        /// o de menor offset é o candidato estável (ponteiro do próprio slot aponta para
        /// antes do insertion_point → RecordOffset não desloca sob grow).
        /// </summary>
        private static MagicSlot FirstSclMoveSlot(MagicDllFile dll)
        {
            MagicSlot? slot = dll.Roots
                .SelectMany(r => r.Programs)
                .SelectMany(p => p.Slots)
                .Where(s => s.OpcodeName == "pppSclMove")
                .OrderBy(s => s.RecordOffset)
                .FirstOrDefault();
            if (slot is null)
                throw new Xunit.Sdk.XunitException(
                    "nenhum slot pppSclMove em magic_0021.dll (opcode não resolvido pelo fp.h/catálogo)");
            return slot;
        }

        /// <summary>Opcodes resolvidos do documento (ordem estável) — para comparar legibilidade pós-grow.</summary>
        private static List<string> ResolvedOpcodeNames(MagicDllFile dll) =>
            dll.Roots
                .SelectMany(r => r.Programs)
                .SelectMany(p => p.Slots)
                .Select(s => s.OpcodeName)
                .Where(n => n is not null)
                .OrderBy(n => n!, StringComparer.Ordinal)
                .ToList();

        // --- 1. Grow com W válido: APLICA e muda o SHA (receita corrigida 2026-08-02) ----

        [Fact]
        public void Grow_AplicaEMudaSha()
        {
            // Receita corrigida 2026-08-02: as counted-u32 tables do header da section
            // (+8/+12) agora são ajustadas (rel field + entries >= insertion) — o grow
            // APLICA de verdade. Expectativas originais da spec religadas.
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] original = File.ReadAllBytes(path);

                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out string loadError), loadError);

                MagicDllFile before = wrapper.ParsedFile!;
                int programsBefore = before.Roots.Sum(r => r.ProgramCount);
                int slotsBefore = before.Roots.Sum(r => r.TotalSlots);
                MagicSlot slot = FirstSclMoveSlot(before);

                Assert.False(wrapper.IsDirty);
                Assert.Equal(wrapper.ShaBefore, wrapper.ShaAfter);

                Assert.True(wrapper.TryGrowRecord(slot, 4, out string report, out string growError), growError);
                Assert.Contains("grow +4B", report);

                // Aplicou: dirty, SHA mudou, tamanho +4, walker preservado.
                Assert.True(wrapper.IsDirty);
                Assert.NotEqual(wrapper.ShaBefore, wrapper.ShaAfter);
                Assert.Equal(original.Length + 4, wrapper.WorkingBytes!.Length);
                Assert.Equal(programsBefore, wrapper.ParsedFile!.Roots.Sum(r => r.ProgramCount));
                Assert.Equal(slotsBefore, wrapper.ParsedFile!.Roots.Sum(r => r.TotalSlots));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // --- 2. Working bytes pós-tentativa continuam re-parseáveis --------------------

        [Fact]
        public void Grow_ReParseLega()
        {
            // Com o grow rejeitado (ACHADO no docstring), o que o re-parse externo prova:
            // o rollback preservou um documento ÍNTEGRO — parser novo re-parseia os
            // working bytes sem erro, com o mesmo walker e os mesmos opcodes resolvidos.
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] original = File.ReadAllBytes(path);

                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out string loadError), loadError);
                MagicSlot slot = FirstSclMoveSlot(wrapper.ParsedFile!);
                Assert.True(wrapper.TryGrowRecord(slot, 4, out string report, out string growError), growError);

                // insertion_point REAL usado pelo wrapper (parseia do report).
                System.Text.RegularExpressions.Match m =
                    System.Text.RegularExpressions.Regex.Match(report, @"insertion_point 0x([0-9A-Fa-f]+)");
                Assert.True(m.Success, "report sem insertion_point: " + report);
                int insertionPoint = Convert.ToInt32(m.Groups[1].Value, 16);

                byte[] working = wrapper.WorkingBytes!;
                Assert.Equal(original.Length + 4, working.Length);

                // O record do slot editado (RecordOffset..insertion) preserva os bytes
                // originais — o campo novo (zero-fill) é inserido DEPOIS dele.
                Assert.True(original.AsSpan(slot.RecordOffset, insertionPoint - slot.RecordOffset).SequenceEqual(
                    working.AsSpan(slot.RecordOffset, insertionPoint - slot.RecordOffset)),
                    $"record editado alterado antes do insertion_point 0x{insertionPoint:X} (report: {report})");
                // NOTA: bytes antes do record PODEM mudar legitimamente (ponteiros de
                // slots anteriores apontando para destinos >= insertion são ajustados
                // +W). A integridade real é provada pelo re-parse externo abaixo +
                // RT0 por record interno do VerifyGrow.

                // Re-parse EXTERNO (parser novo, opções default). O nome preserva o id do
                // efeito (magic_0021 → 21) para o parser resolver o fp.h/catálogo.
                string reparseDir = Path.Combine(dir, "reparse");
                Directory.CreateDirectory(reparseDir);
                string reparsePath = Path.Combine(reparseDir, "magic_0021.dll");
                File.WriteAllBytes(reparsePath, working);

                var parser = new MagicDllParser();
                MagicDllFile originalParsed = parser.Parse(path);
                MagicDllFile reparsed = parser.Parse(reparsePath);

                Assert.Equal(
                    originalParsed.Roots.Sum(r => r.ProgramCount),
                    reparsed.Roots.Sum(r => r.ProgramCount));
                Assert.Equal(
                    originalParsed.Roots.Sum(r => r.TotalSlots),
                    reparsed.Roots.Sum(r => r.TotalSlots));
                Assert.Equal(ResolvedOpcodeNames(originalParsed), ResolvedOpcodeNames(reparsed));
                Assert.Contains("pppSclMove", ResolvedOpcodeNames(reparsed));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        // --- 3. Largura inválida -------------------------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public void Grow_WidthInvalido_RetornaFalseComErro(int width)
        {
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out string loadError), loadError);
                MagicSlot slot = FirstSclMoveSlot(wrapper.ParsedFile!);

                Assert.False(wrapper.TryGrowRecord(slot, width, out _, out string error));
                Assert.Contains("Invalid grow width", error);

                // Falha de validação não suja o documento.
                Assert.False(wrapper.IsDirty);
                Assert.Equal(wrapper.ShaBefore, wrapper.ShaAfter);
                Assert.Equal(File.ReadAllBytes(path).Length, wrapper.WorkingBytes!.Length);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // --- 4. Sem documento ----------------------------------------------------------

        [Fact]
        public void Grow_SemDocumento_RetornaFalse()
        {
            using var language = TestUiCultureScope.English();
            // Slot real obtido de um parse avulso — o grow nem toca o slot: sem documento → false.
            var parser = new MagicDllParser();
            MagicDllFile dll = parser.Parse(CorpusPath("magic_0021.dll"));
            MagicSlot slot = FirstSclMoveSlot(dll);

            var wrapper = new MagicDllDocument_Wrapper();
            Assert.False(wrapper.TryGrowRecord(slot, 4, out _, out string error));
            Assert.Equal("No document loaded.", error);
            Assert.False(wrapper.HasDocument);
            Assert.Null(wrapper.WorkingBytes);
        }

        // --- 5. Round-trip pós-tentativa: save/restore continuam byte-idênticos --------

        [Fact]
        public void Grow_RoundTripRestore_VoltaAoOriginal()
        {
            // Com o grow APLICADO (receita corrigida 2026-08-02), o round-trip que o teste
            // prova: grow→save-cópia→restore hash-gated volta byte-idêntico ao original.
            // O .bak da 1ª gravação é o vanilla (o grow só existe nos working bytes).
            string dir = MakeTempDir();
            try
            {
                string path = CopyCorpusToTemp(dir, "magic_0021.dll");
                byte[] original = File.ReadAllBytes(path);

                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out string loadError), loadError);
                MagicSlot slot = FirstSclMoveSlot(wrapper.ParsedFile!);
                Assert.True(wrapper.TryGrowRecord(slot, 4, out _, out string growError), growError);

                string dest = Path.Combine(dir, "magic_grown.dll");
                Assert.True(wrapper.TrySaveCopy(dest, out string saveError), saveError);
                Assert.True(File.Exists(dest));
                Assert.True(File.Exists(dest + ".bak"));
                Assert.Equal(dest, wrapper.LastSavedPath);

                // O arquivo salvo tem +4 (grow); o .bak é o vanilla.
                Assert.Equal(original.Length + 4, File.ReadAllBytes(dest).Length);
                Assert.True(
                    original.AsSpan().SequenceEqual(File.ReadAllBytes(dest + ".bak")),
                    ".bak não contém o original");

                // Restore hash-gated: SHA atual do dest bate com ShaAfter → restaura.
                Assert.True(wrapper.TryRestoreBackup(dest, out string restoreError), restoreError);
                byte[] restored = File.ReadAllBytes(dest);
                Assert.True(
                    original.AsSpan().SequenceEqual(restored),
                    "restore não voltou byte-idêntico ao original");
                Assert.Equal(original.Length, restored.Length);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        /// <summary>
        /// Guarda do node +0xC (ACHADO 2026-08-02): o insertion_point do grow pode cair
        /// DENTRO do node da cadeia PppMem do slot (22/161 no 0021 = 14%) — corrupção
        /// silenciosa que o VerifyGrow não detecta. Unit test do helper privado
        /// InsertionPointCrossesSlotNode com bytes sintéticos.
        /// </summary>
        [Fact]
        public void Grow_NodeChainGuard_BlocksUnsafeInsertions()
        {
            // Bytes sintéticos: node em offset 0x100 com header size 0x50 (80B).
            var bytes = new byte[0x200];
            BitConverter.GetBytes((uint)0x50).CopyTo(bytes, 0x100);
            var wrapper = new MagicDllDocument_Wrapper();
            var method = typeof(MagicDllDocument_Wrapper).GetMethod(
                "InsertionPointCrossesSlotNode",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

            // Slot fabricado: record em 0x40, +8=0x20 (section em 0x20), +12=0xE0 → node em 0x100.
            // sectionAbs = RecordOffset - PrimaryCallbackRel = 0x40 - 0x20 = 0x20; node = 0x20 + 0xE0 = 0x100.
            var slot = MakeFakeSlot(recordOffset: 0x40, primaryRel: 0x20, secondaryRel: 0xE0);

            // insertion DENTRO do node [0x100, 0x150) → bloqueia.
            bool r1 = (bool)method.Invoke(null, new object[] { bytes, 0, slot, 0x120, "" })!;
            Assert.True(r1, "insertion dentro do node deveria bloquear");

            // insertion FORA do node (antes) → não bloqueia.
            bool r2 = (bool)method.Invoke(null, new object[] { bytes, 0, slot, 0x90, "" })!;
            Assert.False(r2, "insertion antes do node não deveria bloquear");

            // insertion FORA (depois do node) → não bloqueia.
            bool r3 = (bool)method.Invoke(null, new object[] { bytes, 0, slot, 0x160, "" })!;
            Assert.False(r3, "insertion depois do node não deveria bloquear");

            // Tamanho não-plausível (13 — não múltiplo de 16) → não bloqueia.
            var bad = (byte[])bytes.Clone();
            BitConverter.GetBytes((uint)13).CopyTo(bad, 0x100);
            bool r4 = (bool)method.Invoke(null, new object[] { bad, 0, slot, 0x120, "" })!;
            Assert.False(r4, "node com tamanho não-plausível não deveria bloquear");

            // SecondaryCallbackRel = 0 → não bloqueia (sem node).
            bool r5 = (bool)method.Invoke(null, new object[] { bytes, 0, MakeFakeSlot(0x40, 0x20, 0), 0x120, "" })!;
            Assert.False(r5, "sem node (+12=0) não deveria bloquear");
        }

        /// <summary>Slot fake para o unit test do helper (apenas os campos usados pela guarda).</summary>
        private static MagicSlot MakeFakeSlot(int recordOffset, uint primaryRel, uint secondaryRel) =>
            new(
                slotIndex: 0,
                slotAbs: 0,
                handlerTableIndex: 0,
                parameterOffset: 0x20,
                flags: 1,
                primaryCallbackRel: primaryRel,
                secondaryCallbackRel: secondaryRel,
                recordOffset: recordOffset,
                recordWidth: 32,
                record: Array.Empty<byte>(),
                recordSha256: "",
                opcodeName: null,
                field: null,
                fieldWindow: null);
    }
}

