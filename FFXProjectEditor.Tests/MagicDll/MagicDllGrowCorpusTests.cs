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
    /// Fuzz estrutural do GROW no corpus (2026-08-02, após correção das counted-u32
    /// tables + chave ordinal no VerifyGrow): para uma amostra representativa de DLLs,
    /// tenta grow de W=4 no primeiro slot payload_consumer e exige que o resultado
    /// seja OU aplicado com re-parse íntegro (mesmo walker) OU rejeitado com rollback
    /// byte-idêntico — nunca corrompido. Prova que a receita não corrompe outras DLLs
    /// além do 0021 (onde os testes de unidade focam). Nunca escreve no corpus.
    /// </summary>
    public class MagicDllGrowCorpusTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;

        private static string MakeTempDir()
        {
            string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx_magicgrowcorpus_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static IEnumerable<string> SampleDlls()
        {
            // Núcleo dos T3/T4 (0021/0098) + outras com famílias U1 conhecidas + aleatórias.
            string[] wanted = { "magic_0021.dll", "magic_0098.dll", "magic_0086.dll", "magic_0087.dll" };
            foreach (string w in wanted)
                yield return Path.Combine(CorpusDir, w);

            // Amostra pseudo-aleatória estável (hash do nome → sem dependência de ordem do FS).
            foreach (string f in Directory.GetFiles(CorpusDir, "magic_*.dll")
                         .Where(p => !wanted.Contains(Path.GetFileName(p))))
            {
                int h = Path.GetFileName(f).GetHashCode() & 0x7FFFFFFF;
                if (h % 17 == 0)
                    yield return f;
            }
        }

        [Fact]
        public void Grow_Corpus_ApplyOrRollback_NeverCorrupts()
        {
            string dir = MakeTempDir();
            int applied = 0, rejected = 0, noSlot = 0;
            var failures = new List<string>();
            try
            {
                foreach (string path in SampleDlls().Take(40))
                {
                    string dest = Path.Combine(dir, Path.GetFileName(path));
                    File.Copy(path, dest, overwrite: true);
                    byte[] original = File.ReadAllBytes(dest);

                    var wrapper = new MagicDllDocument_Wrapper();
                    if (!wrapper.TryLoad(dest, out string loadError))
                    {
                        failures.Add($"{Path.GetFileName(path)}: load falhou: {loadError}");
                        continue;
                    }

                    MagicSlot? slot = wrapper.ParsedFile!.Roots
                        .SelectMany(r => r.Programs)
                        .SelectMany(p => p.Slots)
                        .Where(s => s.OpcodeName != null && s.RecordWidth is >= 16 and <= 64)
                        .OrderBy(s => s.RecordOffset)
                        .FirstOrDefault();
                    if (slot is null)
                    {
                        noSlot++;
                        continue;
                    }

                    bool ok = wrapper.TryGrowRecord(slot, 4, out string report, out string growError);
                    if (ok)
                    {
                        applied++;
                        // Re-parse externo do grown: mesmo walker do before.
                        byte[] grown = wrapper.WorkingBytes!;
                        Assert.Equal(original.Length + 4, grown.Length);
                        string reparseDir = Path.Combine(dir, "re");
                        Directory.CreateDirectory(reparseDir);
                        string rp = Path.Combine(reparseDir, Path.GetFileName(dest));
                        File.WriteAllBytes(rp, grown);
                        var parser = new MagicDllParser();
                        MagicDllFile before = parser.Parse(dest);
                        MagicDllFile after = parser.Parse(rp);
                        Assert.Equal(before.Roots.Sum(r => r.ProgramCount), after.Roots.Sum(r => r.ProgramCount));
                        Assert.Equal(before.Roots.Sum(r => r.TotalSlots), after.Roots.Sum(r => r.TotalSlots));
                    }
                    else
                    {
                        rejected++;
                        // Rollback byte-idêntico.
                        Assert.False(wrapper.IsDirty, $"{Path.GetFileName(path)}: rollback não limpou dirty: {growError}");
                        Assert.True(original.AsSpan().SequenceEqual(wrapper.WorkingBytes!),
                            $"{Path.GetFileName(path)}: working bytes != original após rejeição: {growError}");
                    }
                }

                Assert.Empty(failures);
                // Amostra representativa: pelo menos 1 aplicado e 1 rejeitado (sanidade do fuzz).
                Assert.True(applied >= 1, $"nenhum grow aplicou na amostra (aplicados={applied}, rejeitados={rejected}, sem-slot={noSlot})");
                Assert.True(rejected + noSlot >= 1, "fuzz não encontrou nenhum caso de rejeição/sem-slot (amostra vazia?)");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// Fuzz multi-width: grow de W ∈ {8,12,16} em DLLs com slots de janela grande
        /// (DrawMdl*, EiWindFun, KeTh, Rand*). Garante que a receita funciona para
        /// qualquer largura permitida e nunca corrompe (aplica com re-parse íntegro OU
        /// rejeita com rollback byte-idêntico).
        /// </summary>
        [Fact]
        public void Grow_Corpus_MultiWidth_NeverCorrupts()
        {
            string dir = MakeTempDir();
            int applied = 0, rejected = 0, noSlot = 0;
            var failures = new List<string>();
            try
            {
                // Foco em DLLs com famílias de janela larga (onde grow 8/12/16 faz sentido).
                string[] candidates = { "magic_0021.dll", "magic_0098.dll", "magic_0086.dll", "magic_0087.dll" };
                foreach (string name in candidates)
                {
                    string path = Path.Combine(CorpusDir, name);
                    if (!File.Exists(path))
                        continue;
                    string dest = Path.Combine(dir, name);
                    File.Copy(path, dest, overwrite: true);
                    byte[] original = File.ReadAllBytes(dest);

                    var wrapper = new MagicDllDocument_Wrapper();
                    if (!wrapper.TryLoad(dest, out string loadError))
                    {
                        failures.Add($"{name}: load falhou: {loadError}");
                        continue;
                    }

                    // Slots de famílias com janela >= 16 (payload real grande).
                    var slots = wrapper.ParsedFile!.Roots
                        .SelectMany(r => r.Programs)
                        .SelectMany(p => p.Slots)
                        .Where(s => s.OpcodeName != null && s.RecordWidth is >= 16 and <= 96)
                        .OrderBy(s => s.RecordOffset)
                        .ToList();
                    if (slots.Count == 0)
                    {
                        noSlot++;
                        continue;
                    }

                    foreach (int width in new[] { 8, 12, 16 })
                    {
                        int beforeLen = wrapper.WorkingBytes!.Length;
                        bool ok = wrapper.TryGrowRecord(slots[0], width, out string report, out string growError);
                        if (ok)
                        {
                            applied++;
                            byte[] grown = wrapper.WorkingBytes!;
                            Assert.Equal(beforeLen + width, grown.Length);
                            string reparseDir = Path.Combine(dir, "re");
                            Directory.CreateDirectory(reparseDir);
                            string rp = Path.Combine(reparseDir, name);
                            File.WriteAllBytes(rp, grown);
                            var parser = new MagicDllParser();
                            MagicDllFile before = parser.Parse(dest);
                            MagicDllFile after = parser.Parse(rp);
                            Assert.Equal(before.Roots.Sum(r => r.ProgramCount), after.Roots.Sum(r => r.ProgramCount));
                            Assert.Equal(before.Roots.Sum(r => r.TotalSlots), after.Roots.Sum(r => r.TotalSlots));
                        }
                        else
                        {
                            rejected++;
                            Assert.False(wrapper.IsDirty, $"{name} W={width}: rollback não limpou dirty: {growError}");
                            Assert.Equal(beforeLen, wrapper.WorkingBytes!.Length);
                        }
                    }
                }

                Assert.Empty(failures);
                Assert.True(applied + rejected >= 6, $"cobertura insuficiente (aplicados={applied}, rejeitados={rejected}, sem-slot={noSlot})");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
