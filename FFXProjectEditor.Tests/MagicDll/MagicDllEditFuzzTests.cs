using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FfxMagicFieldType = FFXProjectEditor.FfxLib.MagicDll.MagicFieldType;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// FUZZ DE EDIÇÃO: aplica edits aleatórios (dentro da janela provada) em vários
    /// slots do 0021 e valida que (a) o diff fica CONFINADO à janela do campo,
    /// (b) o re-parse lê o valor editado, (c) o resto do arquivo permanece intacto.
    /// Complementa o round-trip em massa (que valida o pipeline sem edição).
    /// </summary>
    public class MagicDllEditFuzzTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;

        [Fact]
        public void EditFuzz_MultipleFields_DiffConfinedAndRoundTrips()
        {
            string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx_editfuzz_" + Guid.NewGuid().ToString("N"));
            TestDirectory.CreatePrivate(dir);
            try
            {
                string src = Path.Combine(CorpusDir, "magic_0021.dll");
                string path = Path.Combine(dir, "magic_0021.dll");
                File.Copy(src, path, overwrite: true);
                byte[] original = File.ReadAllBytes(path);

                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out _));

                // Coleta até 12 campos editáveis (f32/s32/u16/u8, offset >= 8 — fora do prefixo).
                var editables = wrapper.ParsedFile!.Roots
                    .SelectMany(r => r.Programs)
                    .SelectMany(p => p.Slots)
                    .SelectMany(s => wrapper.ResolveAllFields(s).Select(f => (Slot: s, Field: f)))
                    .Where(t => t.Field.Offset >= 8 && t.Field.Width is 4 or 2 or 1)
                    .Take(12)
                    .ToList();

                Assert.NotEmpty(editables);
                var rng = new Random(42);
                int applied = 0;

                foreach (var (slot, field) in editables)
                {
                    // Estado anterior (cada edit é confinado contra o estado ATUAL, não o original).
                    byte[] workingBefore = wrapper.WorkingBytes!.ToArray();

                    // Gera bytes novos DENTRO da largura do campo (todos os bits possíveis).
                    byte[] newBytes = new byte[field.Width];
                    rng.NextBytes(newBytes);
                    // F32: garante valor finito (evita NaN que complica o re-parse textual).
                    if (field.Type == FfxMagicFieldType.F32)
                    {
                        float v = (float)(rng.NextDouble() * 200.0 - 100.0);
                        newBytes = BitConverter.GetBytes(v);
                    }

                    if (!wrapper.TryApplyFieldEdit(field, newBytes, out string editError))
                        continue; // campo protegido (ex.: flag) — pula
                    applied++;

                    // (a) Diff confinado à janela do campo nos working bytes.
                    byte[] working = wrapper.WorkingBytes!;
                    int abs = wrapper.ParsedFile!.DataSectionRawPtr + field.RecordOffset + field.Offset;
                    Assert.True(
                        workingBefore.AsSpan(0, abs).SequenceEqual(working.AsSpan(0, abs)),
                        $"{field.Name}@+0x{field.Offset:X}: bytes antes mudaram");
                    Assert.True(
                        workingBefore.AsSpan(abs + field.Width).SequenceEqual(working.AsSpan(abs + field.Width)),
                        $"{field.Name}@+0x{field.Offset:X}: bytes depois mudaram");
                    Assert.Equal(newBytes, working.Skip(abs).Take(field.Width).ToArray());

                    // (b) SHA mudou.
                    Assert.NotEqual(wrapper.ShaBefore, wrapper.ShaAfter);

                    // (c) Arquivo salvo: os bytes editados estão no lugar (leitura direta).
                    string tmp2 = Path.Combine(dir, "magic_reparse.dll");
                    Assert.True(wrapper.TrySaveCopy(tmp2, out _));
                    byte[] saved = File.ReadAllBytes(tmp2);
                    Assert.Equal(newBytes, saved.Skip(abs).Take(field.Width).ToArray());
                }

                Assert.True(applied >= 6, $"esperado >= 6 edits aplicados, veio {applied}");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void EditFuzz_0098_Onda1Families_DiffConfined()
        {
            // ONDA 6 (GOAL 8h): fuzz de edição no 0098 cobrindo as famílias Rand PROVADAS na
            // Onda 1 (RandFV/IV/SRand* — as variantes que o fp.h do 0098 realmente usa;
            // RandChar/Short/Int/CV são EXE-only e NÃO aparecem neste efeito) — prova que o
            // write-back respeita as janelas provadas por decompile em edição real.
            string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx_editfuzz98_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string src = Path.Combine(CorpusDir, "magic_0098.dll");
                Assert.True(File.Exists(src), $"Required corpus fixture missing: {src}");
                string path = Path.Combine(dir, "magic_0098.dll");
                File.Copy(src, path, overwrite: true);

                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out string loadError), loadError);

                // Famílias Rand presentes no fp.h do 0098 (janelas provadas Onda 1: 4+32B etc.).
                string[] onda1Families = { "pppRandFV", "pppRandUpFV", "pppRandDownFV", "pppRandIV",
                    "pppSRandFV", "pppSRandUpFV", "pppSRandDownFV", "pppDrawMdlTs2", "pppDrawMatrix" };
                var targets = wrapper.ParsedFile!.Roots
                    .SelectMany(r => r.Programs)
                    .SelectMany(p => p.Slots)
                    .Where(s => s.OpcodeName != null && onda1Families.Contains(s.OpcodeName!))
                    .SelectMany(s => wrapper.ResolveAllFields(s).Select(f => (Slot: s, Field: f)))
                    .Where(t => t.Field.Offset >= 8 && t.Field.Width is 4 or 2 or 1)
                    .Take(15)
                    .ToList();

                Assert.NotEmpty(targets);
                var rng = new Random(99);
                int applied = 0;
                foreach (var (slot, field) in targets)
                {
                    byte[] workingBefore = wrapper.WorkingBytes!.ToArray();
                    byte[] newBytes = field.Type == FfxMagicFieldType.F32
                        ? BitConverter.GetBytes((float)(rng.NextDouble() * 200.0 - 100.0))
                        : Enumerable.Range(0, field.Width).Select(_ => (byte)rng.Next(256)).ToArray();

                    if (!wrapper.TryApplyFieldEdit(field, newBytes, out string editError))
                        continue;
                    applied++;

                    int abs = wrapper.ParsedFile!.DataSectionRawPtr + field.RecordOffset + field.Offset;
                    Assert.True(
                        workingBefore.AsSpan(0, abs).SequenceEqual(wrapper.WorkingBytes!.AsSpan(0, abs)),
                        $"{slot.OpcodeName}.{field.Name}@+0x{field.Offset:X}: bytes antes mudaram");
                    Assert.True(
                        workingBefore.AsSpan(abs + field.Width).SequenceEqual(wrapper.WorkingBytes!.AsSpan(abs + field.Width)),
                        $"{slot.OpcodeName}.{field.Name}@+0x{field.Offset:X}: bytes depois mudaram");
                    Assert.Equal(newBytes, wrapper.WorkingBytes!.Skip(abs).Take(field.Width).ToArray());
                }

                Assert.True(applied >= 5, $"esperado >= 5 edits aplicados nas famílias Onda 1, veio {applied}");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void EditFuzz_0021_VarreduraFamilies_DiffConfined()
        {
            // ONDA 6.9 (GOAL 8h): fuzz das famílias descobertas na varredura SEM_PAYLOAD
            // (ColAccele 8B, KeBornRnd5/6 21B/17B, VertexApAt 5B) no 0021 — prova que o
            // write-back respeita as janelas provadas nas famílias recém-classificadas.
            string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx_editfuzz21v_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string src = Path.Combine(CorpusDir, "magic_0021.dll");
                Assert.True(File.Exists(src), $"Required corpus fixture missing: {src}");
                string path = Path.Combine(dir, "magic_0021.dll");
                File.Copy(src, path, overwrite: true);

                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out string loadError), loadError);

                string[] families = { "pppColAccele", "pppKeBornRnd5", "pppKeBornRnd6", "pppVertexApAt" };
                var targets = wrapper.ParsedFile!.Roots
                    .SelectMany(r => r.Programs)
                    .SelectMany(p => p.Slots)
                    .Where(s => s.OpcodeName != null && families.Contains(s.OpcodeName!))
                    .SelectMany(s => wrapper.ResolveAllFields(s).Select(f => (Slot: s, Field: f)))
                    .Where(t => t.Field.Offset >= 8 && t.Field.Width is 4 or 2 or 1)
                    .Take(10)
                    .ToList();

                Assert.NotEmpty(targets);
                var rng = new Random(77);
                int applied = 0;
                foreach (var (slot, field) in targets)
                {
                    byte[] workingBefore = wrapper.WorkingBytes!.ToArray();
                    byte[] newBytes = field.Type == FfxMagicFieldType.F32
                        ? BitConverter.GetBytes((float)(rng.NextDouble() * 200.0 - 100.0))
                        : Enumerable.Range(0, field.Width).Select(_ => (byte)rng.Next(256)).ToArray();

                    if (!wrapper.TryApplyFieldEdit(field, newBytes, out string editError))
                        continue;
                    applied++;

                    int abs = wrapper.ParsedFile!.DataSectionRawPtr + field.RecordOffset + field.Offset;
                    Assert.True(
                        workingBefore.AsSpan(0, abs).SequenceEqual(wrapper.WorkingBytes!.AsSpan(0, abs)),
                        $"{slot.OpcodeName}.{field.Name}@+0x{field.Offset:X}: bytes antes mudaram");
                    Assert.True(
                        workingBefore.AsSpan(abs + field.Width).SequenceEqual(wrapper.WorkingBytes!.AsSpan(abs + field.Width)),
                        $"{slot.OpcodeName}.{field.Name}@+0x{field.Offset:X}: bytes depois mudaram");
                    Assert.Equal(newBytes, wrapper.WorkingBytes!.Skip(abs).Take(field.Width).ToArray());
                }

                Assert.True(applied >= 3, $"esperado >= 3 edits nas famílias da varredura, veio {applied}");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        private static byte[] ReadFieldBytes(MagicDllFile dll, MagicField field)
        {
            // Re-lê os bytes do record do campo (mesma âncora do write-back).
            byte[] record = dll.Roots
                .SelectMany(r => r.Programs)
                .SelectMany(p => p.Slots)
                .SelectMany(s => new[] { s })
                .First(s => s.RecordOffset == field.RecordOffset).Record;
            return record.Skip(field.Offset).Take(field.Width).ToArray();
        }
    }
}
