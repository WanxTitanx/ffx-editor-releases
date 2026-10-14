using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// SMOKE do usuário (2026-08-02): magic_0117.dll abriu no editor mas TODOS os slots
    /// caíram em "opcode não catalogado" — o catálogo embutido só tem 0021/0098 e não
    /// existe fp.h do mag_0117 no Yonishi.
    ///
    /// FIX (2026-08-02): resolvedor universal <see cref="MagicFpHandlerTable.TryExtractFromDll"/>
    /// extrai a pppProgTbl_FP do .rdata da PRÓPRIA DLL — a ordem física das strings "ppp*"
    /// é o handler_table_index (prova: 0021 .rdata == fp.h do Yonishi 37/37 na ordem).
    /// O 0117 tem 32 handlers e a ordem DIFERE da canônica (ex.: #22 = pppDrawMatrixFront,
    /// não pppMatrixScl) — por isso a correlação por larguras tinha falsos positivos.
    /// </summary>
    public class MagicDll_0117_HandlerTableTests
    {
        private readonly ITestOutputHelper _output;

        public MagicDll_0117_HandlerTableTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string FindDll(string name) => MagicDllTestFixture.GetPath(name);
    internal static string RepoWorkDir()
    {
        string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "linux-tests");
        TestDirectory.CreatePrivate(dir);
        return dir;
    }


        [Fact]
        public void Magic0117_AllSlots_ResolveViaRdata()
        {
            string? path = FindDll("magic_0117.dll");
            Assert.True(path != null, "magic_0117.dll não encontrado no corpus (F: ou Steam).");

            var parser = new MagicDllParser();
            Assert.True(parser.TryParse(path, out MagicDllFile? file, out string? err), err);
            Assert.True(file!.Roots.Count >= 1, "0117 sem roots PPP.");

            // Todos os handler_table_index usados devem resolver via .rdata (32 nomes).
            MagicFpHandlerTable? table = MagicFpHandlerTable.TryExtractFromDll(file);
            Assert.NotNull(table);
            Assert.True(table!.Count >= 32, $"0117 deveria ter >= 32 handlers no .rdata (tem {table.Count}).");
            _output.WriteLine($"0117 .rdata pppProgTbl: {table.Count} handlers.");
            foreach ((int idx, string name) in table.Names.OrderBy(kv => kv.Key))
                _output.WriteLine($"  #{idx:D2} -> {name}");

            // Caso do smoke do usuário: P0 (key 0x040C) s11 = handler #22.
            int totalSlots = 0;
            int unresolved = 0;
            MagicProgram? p0 = null;
            foreach (MagicDllRoot root in file.Roots)
            {
                foreach (MagicProgram program in root.Programs)
                {
                    if (program.Key == 0x040C)
                        p0 = program;
                    foreach (MagicSlot slot in program.Slots)
                    {
                        totalSlots++;
                        if (string.IsNullOrEmpty(slot.OpcodeName))
                            unresolved++;
                    }
                }
            }

            Assert.NotNull(p0);
            MagicSlot? s11 = p0!.Slots.FirstOrDefault(s => s.SlotIndex == 11);
            Assert.NotNull(s11);
            _output.WriteLine($"P0 (key 0x040C): {p0.Slots.Count} slots.");
            _output.WriteLine($"s11/P0: handler_table_index={s11.HandlerTableIndex} -> {s11.OpcodeName}");
            _output.WriteLine($"slots: {totalSlots}, não catalogados: {unresolved}.");
            Assert.True(unresolved == 0,
                $"0117 tem {unresolved}/{totalSlots} slots não catalogados (esperado 0 com o resolvedor .rdata).");
            Assert.Equal(22u, s11.HandlerTableIndex);
            Assert.Equal("pppDrawMatrixFront", s11.OpcodeName);
        }

        [Fact]
        public void Magic0021_Rdata_MatchesKnownCatalog()
        {
            string? path = FindDll("magic_0021.dll");
            Assert.True(path != null, "magic_0021.dll não encontrado no corpus (F: ou Steam).");

            var parser = new MagicDllParser();
            Assert.True(parser.TryParse(path, out MagicDllFile? file, out string? err), err);
            MagicFpHandlerTable? table = MagicFpHandlerTable.TryExtractFromDll(file!);
            Assert.NotNull(table);

            IReadOnlyDictionary<int, string> known = MagicKnownEffectHandlers.TryGet(21)!;
            Assert.NotNull(known);
            Assert.Equal(known.Count, table!.Count);
            foreach ((int idx, string name) in known)
                Assert.Equal(name, table.Resolve(idx));
        }

        [Fact]
        public void Magic0117_NewSemanticFields_ResolveViaFieldMap()
        {
            // F2 GOAL24H (2026-08-02): o field_map/embedded agora tem 83 familias com
            // fields semanticos (432 campos). O parse do 0117 deve resolver campos
            // nomeados de familias que antes só tinham genéricos.
            string? path = FindDll("magic_0117.dll");
            Assert.True(path != null, "magic_0117.dll não encontrado no corpus (F: ou Steam).");

            var parser = new MagicDllParser();
            Assert.True(parser.TryParse(path, out MagicDllFile? file, out string? err), err);

            var resolved = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            int slotsWithField = 0, total = 0;
            foreach (MagicDllRoot root in file!.Roots)
            {
                foreach (MagicProgram program in root.Programs)
                {
                    foreach (MagicSlot slot in program.Slots)
                    {
                        total++;
                        if (slot.Field != null && slot.OpcodeName != null)
                        {
                            slotsWithField++;
                            resolved[slot.OpcodeName] = resolved.GetValueOrDefault(slot.OpcodeName) + 1;
                        }
                    }
                }
            }

            _output.WriteLine($"0117: {total} slots, {slotsWithField} com campo estruturado resolvido.");
            foreach ((string op, int n) in resolved.OrderByDescending(kv => kv.Value).Take(12))
                _output.WriteLine($"  {op}: {n} slots");

            // Famílias novas com campos semânticos devem aparecer no 0117
            // (pppKeLnsCrn tem pos_x_fixo etc.; pppVertexAp tem flag_frame).
            Assert.True(slotsWithField > 0,
                "Nenhum slot do 0117 resolveu campo estruturado (field_map inoperante?).");
            Assert.True(resolved.ContainsKey("pppVertexAp"),
                "pppVertexAp (4 campos) não resolveu campo no 0117.");
        }

        [Fact]
        public void Magic0117_EditNewSemanticField_Rt0ByteIdentity()
        {
            // F2 GOAL24H: editar um campo SEMÂNTICO novo (pppScale.scl_x, janela 16+16)
            // com write-back confinado + restore byte-identity.
            string? src = FindDll("magic_0117.dll");
            Assert.True(src != null, "magic_0117.dll não encontrado no corpus.");
            string dir = Path.Combine(RepoWorkDir(), "ffx_rt0_117_" + Guid.NewGuid().ToString("N"));
            TestDirectory.CreatePrivate(dir);
            string path = Path.Combine(dir, "magic_0117.dll");
            string outputPath = Path.Combine(dir, "magic_0117_working-copy.dll");
            File.Copy(src, path);
            try
            {
                var wrapper = new FFXProjectEditor.Modules.MagicDllEditor.MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out _));

                // acha slot pppScale com campo scl_x (f32)
                (MagicSlot slot, MagicField field) = (default!, null!);
                bool found = false;
                foreach (MagicSlot s in wrapper.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots))
                {
                    if (s.OpcodeName != "pppScale")
                        continue;
                    foreach (MagicField f in wrapper.ResolveAllFields(s))
                    {
                        if (f.Name == "scale_x" && f.Type == MagicFieldType.F32)
                        {
                            (slot, field) = (s, f);
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                }
                Assert.True(found, "campo pppScale.scl_x não resolvido no 0117.");

                byte[] originalFile = File.ReadAllBytes(path);
                string originalHash = Convert.ToHexString(SHA256.HashData(originalFile));
                byte[] newBytes = BitConverter.GetBytes(2.5f);
                Assert.True(wrapper.TryApplyFieldEdit(field, newBytes, out string editError), editError);
                Assert.True(wrapper.IsDirty);

                byte[] working = wrapper.WorkingBytes!;
                int abs = wrapper.ParsedFile!.DataSectionRawPtr + field.RecordOffset + field.Offset;
                Assert.Equal(originalFile.Length, working.Length);
                Assert.True(originalFile.AsSpan(0, abs).SequenceEqual(working.AsSpan(0, abs)),
                    "bytes antes do campo mudaram");
                Assert.True(originalFile.AsSpan(abs + field.Width).SequenceEqual(working.AsSpan(abs + field.Width)),
                    "bytes depois do campo mudaram");
                Assert.Equal(newBytes, working.Skip(abs).Take(field.Width).ToArray());

                // salva cópia (cria .bak com os bytes originais — pré-requisito do restore)
                Assert.True(wrapper.TrySaveCopy(outputPath, out string saveError), saveError);

                // restore → byte-identity total
                Assert.True(wrapper.TryRestoreBackup(outputPath, out string restoreError), restoreError);
                Assert.Equal(originalFile, File.ReadAllBytes(outputPath));
                Assert.Equal(originalFile, File.ReadAllBytes(path));
                Assert.Equal(originalHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Magic0117_Fuzz500Edits_MultiFamily_Rt0AlwaysRestores()
        {
            // F2 GOAL24H: fuzz de edição em campos de MUITAS famílias (não só uma):
            // cada edição confina à janela do campo e o restore volta byte-identity.
            string? src = FindDll("magic_0117.dll");
            Assert.True(src != null);
            string dir = Path.Combine(RepoWorkDir(), "ffx_fuzz117_" + Guid.NewGuid().ToString("N"));
            TestDirectory.CreatePrivate(dir);
            string path = Path.Combine(dir, "magic_0117.dll");
            string outputPath = Path.Combine(dir, "magic_0117_working-copy.dll");
            File.Copy(src, path);
            try
            {
                var wrapper = new FFXProjectEditor.Modules.MagicDllEditor.MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out _));
                byte[] original = File.ReadAllBytes(path);
                string originalHash = Convert.ToHexString(SHA256.HashData(original));

                // coleta campos de famílias variadas
                var targets = new System.Collections.Generic.List<(MagicField Field, int Abs)>();
                var fams = new System.Collections.Generic.HashSet<string>();
                foreach (MagicSlot s in wrapper.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots))
                {
                    if (s.OpcodeName == null || fams.Count >= 25)
                        break;
                    foreach (MagicField f in wrapper.ResolveAllFields(s))
                    {
                        // prefixo [+0,+8) é protegido (match word — WRITEBACK_SPEC R6): exibível, não editável
                        if (f.Offset < 8)
                            continue;
                        if (s.FieldWindow == null)
                            continue; // família sem janela runtime (ex.: pppMatrixScl) — exibível, não editável
                        if (f.Type is MagicFieldType.F32 or MagicFieldType.U16 or MagicFieldType.U8 or MagicFieldType.S32)
                        {
                            targets.Add((f, wrapper.ParsedFile.DataSectionRawPtr + f.RecordOffset + f.Offset));
                            fams.Add(s.OpcodeName);
                            break;
                        }
                    }
                }
                Assert.True(targets.Count >= 40, $"alvos insuficientes: {targets.Count} (esperado >= 40).");
                _output.WriteLine($"fuzz alvos: {targets.Count} campos de {fams.Count} famílias.");

                var rng = new Random(1234);
                for (int i = 0; i < 500; i++)
                {
                    (MagicField field, int abs) = targets[i % targets.Count];
                    byte[] before = wrapper.WorkingBytes!; // snapshot (records compartilhados são legítimos)
                    byte[] newBytes = field.Width switch
                    {
                        4 => BitConverter.GetBytes((float)(rng.NextDouble() * 100 - 50)),
                        2 => BitConverter.GetBytes((ushort)rng.Next(0, 65536)),
                        _ => new[] { (byte)rng.Next(0, 256) },
                    };
                    Assert.True(wrapper.TryApplyFieldEdit(field, newBytes, out string editError), editError);

                    byte[] working = wrapper.WorkingBytes!;
                    Assert.Equal(before.Length, working.Length);
                    Assert.True(before.AsSpan(0, abs).SequenceEqual(working.AsSpan(0, abs)),
                        $"edit {i}: bytes antes do campo mudaram (offset {abs})");
                    Assert.True(before.AsSpan(abs + field.Width).SequenceEqual(working.AsSpan(abs + field.Width)),
                        $"edit {i}: bytes depois do campo mudaram");
                    Assert.Equal(newBytes, working.Skip(abs).Take(field.Width).ToArray());

                    Assert.True(wrapper.TrySaveCopy(outputPath, out string saveError), saveError);
                    Assert.True(wrapper.TryRestoreBackup(outputPath, out string restoreError), restoreError);
                    Assert.Equal(original, File.ReadAllBytes(outputPath));
                    Assert.Equal(original, File.ReadAllBytes(path));
                    Assert.Equal(originalHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                }
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Magic0098_Rdata_MatchesKnownCatalogExactly()
        {
            string? path = FindDll("magic_0098.dll");
            Assert.True(path != null, "magic_0098.dll não encontrado no corpus (F: ou Steam).");

            var parser = new MagicDllParser();
            Assert.True(parser.TryParse(path, out MagicDllFile? file, out string? err), err);
            MagicFpHandlerTable? table = MagicFpHandlerTable.TryExtractFromDll(file!);
            Assert.NotNull(table);

            // O catálogo embutido [98] foi regenerado do .rdata do PC (2026-08-02):
            // 39 handlers, ordem EXATA (o antigo fp.h PS2 tinha 35 e estava deslocado
            // de 26 em diante — o PC inseriu KeZCrct/KeZCrctShp/DrawMdl3/DrawShapeX).
            IReadOnlyDictionary<int, string> known = MagicKnownEffectHandlers.TryGet(98)!;
            Assert.Equal(table!.Count, known.Count);
            foreach ((int idx, string name) in known)
                Assert.Equal(name, table.Resolve(idx));
        }
    }
}
