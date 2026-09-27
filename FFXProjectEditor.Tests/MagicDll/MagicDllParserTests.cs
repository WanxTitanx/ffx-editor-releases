using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Testes do parser contra fixtures PE32 autocontidas e geradas deterministicamente. O corpus
    /// privado continua sendo evidência offline separada; o gate de produto não depende dele.
    /// </summary>
    public class MagicDllParserTests
    {
        private static readonly MagicDllParser Parser = new();

        private static string CorpusPath(string dllName) => MagicDllTestFixture.GetPath(dllName);

        // --- Parse estrutural ----------------------------------------------------------

        [Fact]
        public void Parse_Magic0021_RootProgramsSlots_Consistent()
        {
            MagicDllFile dll = Parser.Parse(CorpusPath("magic_0021.dll"));

            Assert.Equal("magic_0021.dll", dll.DllName);
            Assert.Equal(21, dll.MagicId);
            Assert.NotEmpty(dll.Data);
            Assert.True(dll.DataSectionRawPtr > 0);
            Assert.Single(dll.Roots);

            MagicDllRoot root = dll.Roots[0];
            Assert.Equal(0, root.RootAbs);
            Assert.Equal(1, root.PrimaryCount);
            Assert.Equal(0x40, root.PrimarySections[0]);
            Assert.Single(root.Programs);
            Assert.Equal(74, root.TotalSlots);
            Assert.Empty(root.Descriptors);
            Assert.Equal(new[] { 36, 40, 44 }, root.RelocationTableOffsets);

            // handler_indices_used: 0..36 (37 índices locais do fp.h)
            Assert.Equal(37, root.HandlerIndicesUsed.Count);
            Assert.Equal(0, root.HandlerIndicesUsed[0]);
            Assert.Equal(36, root.HandlerIndicesUsed[^1]);

            // Todo slot tem record com âncora SHA-256 (hex de 64 chars).
            Assert.All(
                root.Programs.SelectMany(p => p.Slots),
                slot => Assert.Equal(64, slot.RecordSha256.Length));
        }

        [Fact]
        public void Parse_Magic0098_RootProgramsSlots_Consistent()
        {
            MagicDllFile dll = Parser.Parse(CorpusPath("magic_0098.dll"));

            Assert.Equal("magic_0098.dll", dll.DllName);
            Assert.Equal(98, dll.MagicId);
            Assert.Single(dll.Roots);

            MagicDllRoot root = dll.Roots[0];
            Assert.Equal(0, root.RootAbs);
            Assert.Equal(1, root.PrimaryCount);
            Assert.Equal(new[] { 0x40 }, root.PrimarySections);
            Assert.Single(root.Programs);
            Assert.Equal(78, root.TotalSlots);
            Assert.Empty(root.Descriptors);
        }

        // --- Resolução de campos via schema (payload_consumer) ------------------------

        [Fact]
        public void Parse_ResolvesSchemaField_ForKnownPayloadConsumerSlots()
        {
            // 0021: handler_table_index 7 = pppSclMove (fp.h local) → payload_consumer,
            // janela 16B em record+0x10..+0x1F, campos delta_x/y/z/w (f32).
            MagicDllFile dll0021 = Parser.Parse(CorpusPath("magic_0021.dll"));
            MagicSlot? sclMove = dll0021.Roots[0].Programs
                .SelectMany(p => p.Slots)
                .FirstOrDefault(s => s.OpcodeName == "pppSclMove" && s.Field != null);
            Assert.NotNull(sclMove);
            Assert.Equal(7u, sclMove!.HandlerTableIndex);
            Assert.Equal("pppSclMove", sclMove.OpcodeName);
            Assert.NotNull(sclMove.FieldWindow);
            Assert.Equal(16, sclMove.FieldWindow!.Value.Start);
            Assert.Equal(16, sclMove.FieldWindow.Value.Width);
            Assert.Equal(32, sclMove.RecordWidth);    // max(32, 16+16)
            Assert.Equal("delta_x", sclMove.Field!.Name);
            Assert.Equal(16, sclMove.Field.Offset);
            Assert.Equal(MagicFieldType.F32, sclMove.Field.Type);
            Assert.NotNull(sclMove.Field.ValueFloat);
            Assert.Equal(64, sclMove.Field.RecordSha256.Length);

            // 0098: handler_table_index 0 = pppAccele (fp.h local) → payload_consumer.
            MagicDllFile dll0098 = Parser.Parse(CorpusPath("magic_0098.dll"));
            MagicSlot? accele = dll0098.Roots[0].Programs
                .SelectMany(p => p.Slots)
                .FirstOrDefault(s => s.OpcodeName == "pppAccele" && s.Field != null);
            Assert.NotNull(accele);
            Assert.Equal(0u, accele!.HandlerTableIndex);
            Assert.Equal("pppAccele", accele.OpcodeName);
            Assert.Equal("accel_x", accele.Field!.Name);
            Assert.NotNull(accele.Field.ValueFloat);

            // Ao menos 1 slot com campo resolvido em CADA DLL (contrato do parser).
            Assert.Contains(
                dll0021.Roots[0].Programs.SelectMany(p => p.Slots),
                s => s.Field != null);
            Assert.Contains(
                dll0098.Roots[0].Programs.SelectMany(p => p.Slots),
                s => s.Field != null);
        }

        // --- Round-trip byte-idêntico (SHA-256) ---------------------------------------

        [Fact]
        public void RoundTrip_NoEdit_IsByteIdentical_Sha256()
        {
            // Copia a DLL para um temp — o corpus NUNCA é escrito pelos testes.
            string tempDir = Path.Combine(
                FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work", "ffx_magicdll_rt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string tempDll = Path.Combine(tempDir, "magic_0021.dll");
                File.Copy(CorpusPath("magic_0021.dll"), tempDll, overwrite: true);

                byte[] original = File.ReadAllBytes(CorpusPath("magic_0021.dll"));
                MagicDllFile dll = Parser.Parse(tempDll);
                byte[] roundTripped = dll.Serialize();

                Assert.Equal(original.Length, roundTripped.Length);
                Assert.True(
                    original.AsSpan().SequenceEqual(roundTripped),
                    "round-trip sem edição divergiu dos bytes originais");
                Assert.Equal(
                    Convert.ToHexString(SHA256.HashData(original)),
                    Convert.ToHexString(SHA256.HashData(roundTripped)));
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }

        // --- TryParse sem exceção ------------------------------------------------------

        [Fact]
        public void TryParse_MissingFile_ReturnsFalse_NoThrow()
        {
            using var language = TestUiCultureScope.English();
            string missing = Path.Combine(
                FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work", "ffx_magicdll_missing_" + Guid.NewGuid().ToString("N") + ".dll");

            bool ok = Parser.TryParse(missing, out MagicDllFile? file, out string? error);

            Assert.False(ok);
            Assert.Null(file);
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
        }

        // --- Mapa embutido (fallback offline) -----------------------------------------

        [Fact]
        public void EmbeddedFieldMap_ContainsPayloadConsumerFamilies()
        {
            MagicFieldMap map = MagicFieldMap.LoadEmbedded();

            Assert.True(map.Count >= 40, $"esperado >= 40 famílias embutidas, veio {map.Count}");
            Assert.True(map.TryGet("pppSclMove", out MagicFamilySchema? sclMove));
            Assert.True(sclMove!.PayloadConsumer);
            Assert.Equal(16, sclMove.Window!.Value.Start);
            Assert.Equal(16, sclMove.Window.Value.Width);
            Assert.Equal(4, sclMove.Fields.Count);
            Assert.Equal(MagicFieldType.F32, sclMove.Fields[0].Type);

            Assert.True(map.TryGet("pppKeTh", out MagicFamilySchema? keTh));
            Assert.Equal(88, keTh!.CallbackRecordWidth);   // 16+72 (0x736110 — correção 2026-08-02)
            Assert.True(map.TryGet("pppKeThSft", out MagicFamilySchema? keThSft));
            Assert.Equal(57, keThSft!.CallbackRecordWidth); // 8+49 (0x736F50 — janela do schema antigo)

            // EXE-only integradas (2026-08-01): RandChar e KeMdlTfdUv2.
            Assert.True(map.TryGet("pppRandChar", out MagicFamilySchema? randChar));
            Assert.True(randChar!.PayloadConsumer);
            Assert.Equal(6, randChar.Window!.Value.Width);

            Assert.True(map.TryGet("pppKeMdlTfdUv2", out MagicFamilySchema? tfd));
            Assert.True(tfd!.PayloadConsumer);
            Assert.Equal(49, tfd.Window!.Value.Width);

            // Grupo Rand completo PROVADO por decompile (2026-08-02):
            // Short/UpShort/DownShort = 7B (u16 value@+8 + flag@+10); Up/DownInt/CV = 9B.
            Assert.True(map.TryGet("pppRandShort", out MagicFamilySchema? rShort));
            Assert.True(rShort!.PayloadConsumer);
            Assert.Equal(7, rShort.Window!.Value.Width);
            Assert.True(map.TryGet("pppRandUpInt", out MagicFamilySchema? rUpInt));
            Assert.True(rUpInt!.PayloadConsumer);
            Assert.Equal(9, rUpInt.Window!.Value.Width);
            Assert.True(map.TryGet("pppRandDownCV", out MagicFamilySchema? rDownCV));
            Assert.True(rDownCV!.PayloadConsumer);
            Assert.Equal(9, rDownCV.Window!.Value.Width);

            // KeMdlTfd2/3 NÃO seguem o padrão do base (49B) — decompile provou 26B/30B!
            Assert.True(map.TryGet("pppKeMdlTfd2", out MagicFamilySchema? tfd2));
            Assert.True(tfd2!.PayloadConsumer);
            Assert.Equal(26, tfd2.Window!.Value.Width);
            Assert.True(map.TryGet("pppKeMdlTfd3", out MagicFamilySchema? tfd3));
            Assert.True(tfd3!.PayloadConsumer);
            Assert.Equal(30, tfd3.Window!.Value.Width);

            // ONDA 1 (2026-08-02, field_map 209/209 com status): novas provas por decompile.
            Assert.True(map.Count >= 63, $"esperado >= 63 famílias embutidas (Onda 1), veio {map.Count}");

            // KeHmgEff = KNOB HUD/portrait (0x759CE0): lerp f32@+4, refs vizinho +8/+12.
            Assert.True(map.TryGet("pppKeHmgEff", out MagicFamilySchema? hmg));
            Assert.True(hmg!.PayloadConsumer);
            Assert.Equal(4, hmg.Window!.Value.Start);
            Assert.Equal(12, hmg.Window.Value.Width);

            // KeMdlTfdUv3 (0x74A0C0, renomeado FFX_PppHandler_KeMdlTfdUv3): drawable@+4, 3×Vec3@+8..+52, flags@+56..+60.
            Assert.True(map.TryGet("pppKeMdlTfdUv3", out MagicFamilySchema? tfdUv3));
            Assert.True(tfdUv3!.PayloadConsumer);
            Assert.Equal(4, tfdUv3.Window!.Value.Start);
            Assert.Equal(57, tfdUv3.Window.Value.Width);

            // DrawFilter (0x757370): wrapX u32@+12<<8, wrapY@+16<<8, deltas@+28/+32.
            Assert.True(map.TryGet("pppDrawFilter", out MagicFamilySchema? drawFilter));
            Assert.True(drawFilter!.PayloadConsumer);
            Assert.Equal(12, drawFilter.Window!.Value.Start);
            Assert.Equal(24, drawFilter.Window.Value.Width);

            // EiWindFun (0x75D540): 6×f32 init@+16..+36, deltas@+40/+44/+48.
            Assert.True(map.TryGet("pppEiWindFun", out MagicFamilySchema? wind));
            Assert.True(wind!.PayloadConsumer);
            Assert.Equal(16, wind.Window!.Value.Start);
            Assert.Equal(36, wind.Window.Value.Width);

            // NeiPointLight (0x75D7F0): f32@+4/+8/+12.
            Assert.True(map.TryGet("pppNeiPointLight", out MagicFamilySchema? nei));
            Assert.True(nei!.PayloadConsumer);
            Assert.Equal(4, nei.Window!.Value.Start);
            Assert.Equal(12, nei.Window.Value.Width);

            // ColAccele (0x75BB30): byte-idêntico ao ColMove (decompile Onda 6.4) — 4×u16 @+8..+15.
            Assert.True(map.TryGet("pppColAccele", out MagicFamilySchema? colAccele));
            Assert.True(colAccele!.PayloadConsumer);
            Assert.Equal(8, colAccele.Window!.Value.Start);
            Assert.Equal(8, colAccele.Window.Value.Width);
            Assert.Equal(4, colAccele.Fields.Count);

            // KeParMatR is catalogued for parsing, but remains a non-payload/non-editable family
            // (FFX_MagicHost_ProjectChildCoords does not read a2).
            Assert.True(map.TryGet("pppKeParMatR", out MagicFamilySchema? keParMatR));
            Assert.False(keParMatR!.PayloadConsumer);
            Assert.False(keParMatR.Editable);
            Assert.Null(keParMatR.Window);
        }

        [Fact]
        public void EmbeddedFieldMap_IncludesTheCompleteCanonicalFamilyCatalogReadOnlyByDefault()
        {
            MagicFieldMap map = MagicFieldMap.LoadEmbedded();

            Assert.Equal(221, map.Count);
            foreach (string opcode in new[] { "pppSMatrix", "pppKeThRes48", "pppMatrixXYZ" })
            {
                Assert.True(map.TryGet(opcode, out MagicFamilySchema? schema));
                Assert.False(schema!.PayloadConsumer);
                Assert.False(schema.Editable);
                Assert.Null(schema.Window);
            }
        }

        // --- Catálogo embutido de handlers locais -------------------------------------

        [Fact]
        public void KnownEffectHandlers_MatchesRealFpTables()
        {
            // 0021: 37 handlers; índice 7 = pppSclMove (PROVADO pelo T3 da lane MAGIC).
            IReadOnlyDictionary<int, string>? h21 = MagicKnownEffectHandlers.TryGet(21);
            Assert.NotNull(h21);
            Assert.Equal(37, h21!.Count);
            Assert.Equal("pppSclMove", h21[7]);
            Assert.Equal("pppDrawMdl", h21[30]);

            // 0098: 39 handlers na ordem do .rdata do PC (2026-08-02 — o fp.h PS2 tinha
            // 35 e estava deslocado: o PC inseriu KeZCrct/KeZCrctShp/DrawMdl3/DrawShapeX).
            IReadOnlyDictionary<int, string>? h98 = MagicKnownEffectHandlers.TryGet(98);
            Assert.NotNull(h98);
            Assert.Equal(39, h98!.Count);
            Assert.Equal("pppKeZCrct", h98[26]);
            Assert.Equal("pppDrawMdl", h98[28]);

            Assert.Null(MagicKnownEffectHandlers.TryGet(9999));
        }
    }
}

