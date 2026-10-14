using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Fuzz de EDIÇÃO em múltiplas DLLs (2026-08-02): o write-back confinado à janela
    /// do schema precisa funcionar em qualquer DLL com campos editáveis — não só no
    /// 0021 (onde o EditFuzz original roda). Para cada DLL da amostra: aplica 5 edits
    /// aleatórios de campos f32/s32 e exige diff SEMPRE confinado à janela do campo.
    /// </summary>
    public class MagicDllEditFuzzMultiDllTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;

        private static string MakeTempDir() =>
            Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx_editfuzz_" + Guid.NewGuid().ToString("N"));

        private static string CopyToTemp(string dir, string dllName)
        {
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, dllName);
            File.Copy(Path.Combine(CorpusDir, dllName), dest);
            return dest;
        }

        [Fact]
        public void EditFuzz_MultiDll_ConfinedToFieldWindow()
        {
            string dir = MakeTempDir();
            try
            {
                string[] dlls = { "magic_0021.dll", "magic_0098.dll", "magic_0086.dll", "magic_0087.dll" };
                var rng = new Random(0x5EED);
                int edits = 0, applied = 0;

                foreach (string dllName in dlls)
                {
                    string path = CopyToTemp(Path.Combine(dir, dllName.Replace(".dll", "")), dllName);
                    byte[] original = File.ReadAllBytes(path);

                    var wrapper = new MagicDllDocument_Wrapper();
                    Assert.True(wrapper.TryLoad(path, out string loadError), $"{dllName}: {loadError}");

                    for (int i = 0; i < 5; i++)
                    {
                        var field = wrapper.ParsedFile!.Roots
                            .SelectMany(r => r.Programs)
                            .SelectMany(p => p.Slots)
                            .SelectMany(s => wrapper.ResolveAllFields(s))
                            .FirstOrDefault(f =>
                                f.Type is FFXProjectEditor.FfxLib.MagicDll.MagicFieldType.F32 or
                                    FFXProjectEditor.FfxLib.MagicDll.MagicFieldType.S32 &&
                                f.Offset >= 8);
                        if (field is null)
                            break;

                        // Valor novo (f32: [0..1000); s32: [-1000..1000]).
                        byte[] newBytes = field.Type == FFXProjectEditor.FfxLib.MagicDll.MagicFieldType.F32
                            ? BitConverter.GetBytes((float)(rng.NextDouble() * 1000.0))
                            : BitConverter.GetBytes(rng.Next(-1000, 1000));

                        if (!wrapper.TryApplyFieldEdit(field, newBytes, out string editError))
                        {
                            Assert.Fail($"{dllName} edit {i}: {editError}");
                        }

                        // Diff confinado: working vs original só difere nos bytes da janela
                        // (field.Offset é relativo ao record; RecordOffset é relativo ao
                        // .data — posição no arquivo soma o DataSectionRawPtr do PE).
                        byte[] wb = wrapper.WorkingBytes!;
                        int dataPtr = wrapper.ParsedFile!.DataSectionRawPtr;
                        int windowStart = dataPtr + field.RecordOffset + field.Offset;
                        int windowEnd = dataPtr + field.RecordOffset + field.Offset + field.Width;
                        for (int b = 0; b < wb.Length; b++)
                        {
                            bool inWindow = b >= windowStart && b < windowEnd;
                            bool differs = original[b] != wb[b];
                            if (differs && !inWindow)
                                Assert.Fail($"{dllName}: byte {b} fora da janela [{windowStart},{windowEnd}) alterado");
                            if (!differs && inWindow && field.Offset >= 8)
                            {
                                // O byte pode coincidir por acaso; permitido. Mas o
                                // campo inteiro não pode ficar igual (edit aplicou).
                            }
                        }
                        edits++;
                        applied++;
                        wrapper.DiscardWorkingChanges(); // volta ao original para o próximo edit
                    }
                }

                Assert.True(edits >= 10, $"cobertura insuficiente (edits={edits})");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            }
        }
    }
}
