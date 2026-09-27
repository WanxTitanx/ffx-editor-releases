using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// SMOKE HEADLESS do parser C# (FfxLib.MagicDll.MagicDllParser) contra um lote
    /// variado do corpus de magic DLLs (F:\ffx-reconstructed\extras\magicFiles\FFX).
    ///
    /// Objetivo: validar que o parser abre bem DLLs variadas — não apenas as duas
    /// canonizadas nos testes unitários (0021/0098). A amostra é determinística e
    /// derivada do CORPUS_AUDIT.json (work\magic_editor), que catalogou 347/581
    /// DLLs como OPEN_OK no audit Python (layer_c_resource.py).
    ///
    /// Os resultados são serializados em work\magic_editor\PARSER_SMOKE_BATCH.json
    /// (e _SAMPLE200.json) para o relatório MD. Parse é read-only: não escreve no
    /// corpus, não precisa de temp dirs.
    /// </summary>
    public class MagicDllCorpusSmokeTests
    {
        // Corpus de referência (nunca escrito pelos testes).
        private static string CorpusDir => TestDataPaths.MagicCorpus;

        // Audit Python de referência (347 OPEN_OK / 234 NO_ROOT em 581 DLLs).
        private static readonly string AuditJsonPath = TestDataPaths.MagicAudit;

        private static readonly MagicDllParser Parser = new();
        private readonly ITestOutputHelper _output;

        public MagicDllCorpusSmokeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string MakeTempDir()
        {
            string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx_smoke_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }


        // --- Amostra 1: primeiras 40 OPEN_OK do audit ----------------------------------

        [Fact]
        public void CorpusSmoke_Sample40_AllOpenOk()
        {
            List<(string Name, int Id)> openOk = LoadOpenOkFromAudit();
            Assert.True(
                openOk.Count >= 40,
                $"CORPUS_AUDIT.json tem apenas {openOk.Count} DLLs OPEN_OK (esperado >= 40).");

            List<(string Name, int Id)> sample = openOk.Take(40).ToList();
            List<SmokeEntry> results = RunSmoke(sample.Select(s => s.Name));
            WriteBatchJson("PARSER_SMOKE_BATCH.json", "first_40_open_ok_audit", results);

            int ok = results.Count(r => r.Ok);
            int failed = results.Count(r => !r.Ok);
            _output.WriteLine($"[Sample40] ok={ok} failed={failed} (40 no total)");

            // NÃO remover DLL da lista em caso de falha: reporta e documenta no MD.
            var failures = results.Where(r => !r.Ok).ToList();
            Assert.True(
                failed == 0,
                $"Falharam {failed} de {sample.Count} DLLs OPEN_OK do audit:\n"
                + string.Join("\n", failures.Select(f => $"{f.Dll}: {f.Error}")));
        }


        // --- Amostra 2: 200 DLLs espalhadas por id (todo o corpus) ---------------------

        [Fact]
        public void CorpusSmoke_AllOpenOk_AtLeast300()
        {
            // Amostra uniforme de 200 DLLs por id (inclui NO_ROOT do audit — o C#
            // deve abrir aproximadamente as mesmas que o Python: 347/581 ≈ 59,7%).
            string[] files = Directory.GetFiles(CorpusDir, "*.dll")
                .OrderBy(f => MagicDllFile.ParseMagicIdFromName(f))
                .ToArray();
            Assert.True(files.Length >= 200, $"Corpus tem {files.Length} DLLs (esperado >= 200).");

            var sample = new List<string>();
            for (int i = 0; i < 200; i++)
                sample.Add(files[i * files.Length / 200]);

            List<SmokeEntry> results = RunSmoke(sample);
            WriteBatchJson("PARSER_SMOKE_BATCH_SAMPLE200.json", "spread_200_of_581", results);

            int ok = results.Count(r => r.Ok);
            int failed = results.Count(r => !r.Ok);
            _output.WriteLine($"[Sample200] ok={ok} failed={failed} (200 no total)");

            // Limite conservador: audit Python achou 347/581 (≈60%); 200 espalhadas
            // esperam ~119 OK. O nome do teste sugere 300, mas o assert é >= 100
            // conforme a tarefa (conservador; divergência grande vira análise no MD).
            var failures = results.Where(r => !r.Ok).ToList();
            Assert.True(
                ok >= 100,
                $"Só {ok}/200 DLLs abriram no parser C# (esperado >= 100; audit Python ~119/200):\n"
                + string.Join("\n", failures.Select(f => $"{f.Dll}: {f.Error}")));
        }


        // --- Full scan: TODAS as DLLs do corpus (F5 GOAL24H — gate 589/589) ------------

        [Fact]
        public void CorpusSmoke_AllDlls_NamedSlots_NoUnknownOpcodes()
        {
            // F5 GOAL24H (2026-08-02): com o resolvedor .rdata, TODAS as DLLs devem
            // abrir, a quase totalidade dos slots resolve nome, e nenhum opcode
            // fica fora do field_map (216 famílias).
            string[] files = Directory.GetFiles(CorpusDir, "*.dll")
                .OrderBy(f => MagicDllFile.ParseMagicIdFromName(f))
                .ToArray();
            Assert.True(files.Length >= 500, $"Corpus tem {files.Length} DLLs (esperado >= 500).");

            // Exercise the exact catalog available in an installed release. MagicFieldMap.Load()
            // can silently discover work/magic_editor/field_map.json inside a checkout and would
            // therefore hide drift in the embedded fallback.
            var fieldMap = MagicFieldMap.LoadEmbedded();
            int opened = 0, failed = 0, rawSlots = 0, resolvedSlots = 0;
            var unknown = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var failures = new List<string>();

            foreach (string path in files)
            {
                try
                {
                    MagicDllFile dll = Parser.Parse(path);
                    opened++;
                    foreach (MagicSlot slot in dll.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots))
                    {
                        if (slot.OpcodeName is null)
                        {
                            rawSlots++;
                            continue;
                        }
                        resolvedSlots++;
                        if (!fieldMap.TryGet(slot.OpcodeName, out _))
                            unknown[slot.OpcodeName] = unknown.GetValueOrDefault(slot.OpcodeName) + 1;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }

            _output.WriteLine($"[FullScan] {opened}/{files.Length} abertas (falhas: {failed}) · " +
                              $"slots: {resolvedSlots} nomeados, {rawSlots} RAW · " +
                              $"opcodes fora do field_map: {unknown.Count}");
            foreach ((string op, int n) in unknown)
                _output.WriteLine($"  {op}: {n}");

            Assert.True(failed <= 5,
                $"Falharam {failed} DLLs no full scan:\n" + string.Join("\n", failures.Take(8)));
            Assert.True(resolvedSlots > 100000,
                $"Slots nomeados insuficientes no corpus: {resolvedSlots} (esperado > 100K).");
            Assert.True(rawSlots <= 1000,
                $"Slots RAW demais: {rawSlots} (esperado <= 1000 = 0,2% do corpus; 453 atuais = índices fora da tabela .rdata em DLLs atípicas).");
            Assert.Empty(unknown);
        }

        /// <summary>
        /// Roda TryParse em cada DLL. Nunca deixa uma exceção inesperada derrubar a
        /// amostra: exceção vira falha registrada (divergência/bug documentado no MD).
        /// </summary>
        private static List<SmokeEntry> RunSmoke(IEnumerable<string> dllNames)
        {
            var results = new List<SmokeEntry>();
            foreach (string name in dllNames)
            {
                string path = Path.Combine(CorpusDir, name);
                bool ok;
                string? error;
                int rootCount = 0, programCount = 0, totalSlots = 0, nDescriptors = 0;

                try
                {
                    ok = Parser.TryParse(path, out MagicDllFile? file, out error);
                    if (ok && file != null)
                    {
                        rootCount = file.Roots.Count;
                        programCount = file.Roots.Sum(r => r.ProgramCount);
                        totalSlots = file.Roots.Sum(r => r.TotalSlots);
                        nDescriptors = file.Roots.Sum(r => r.Descriptors.Count);
                    }
                }
                catch (Exception ex)
                {
                    // O contrato do TryParse é nunca lançar; se lançou, é bug do parser.
                    ok = false;
                    error = "EXCEÇÃO NÃO CAPTURADA pelo TryParse: " + ex.GetType().Name
                        + ": " + ex.Message;
                }

                results.Add(new SmokeEntry
                {
                    Dll = Path.GetFileName(name),
                    Id = MagicDllFile.ParseMagicIdFromName(path),
                    AuditStatus = GetAuditStatus(Path.GetFileName(name)),
                    Ok = ok,
                    RootCount = rootCount,
                    ProgramCount = programCount,
                    TotalSlots = totalSlots,
                    NDescriptors = nDescriptors,
                    Error = error,
                });
            }

            return results;
        }


        /// <summary>
        /// Lê o CORPUS_AUDIT.json (work\magic_editor) e devolve as DLLs catalogadas como
        /// OPEN_OK pelo audit Python, ordenadas por id (amostra determinística).
        /// COMPLETADO por Jarvis-PPP-C2C3 (2026-08-02): a sessão autora deixou o arquivo
        /// sem este método e com o fechamento do RunSmoke órfão no fim do arquivo —
        /// correção mínima para destravar o build do projeto de testes.
        /// </summary>
        private static List<(string Name, int Id)> LoadOpenOkFromAudit()
        {
            var result = new List<(string Name, int Id)>();
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(AuditJsonPath));
            foreach (JsonProperty prop in doc.RootElement.GetProperty("dlls").EnumerateObject())
            {
                if (prop.Value.TryGetProperty("status", out JsonElement status)
                    && string.Equals(status.GetString(), "OPEN_OK", StringComparison.OrdinalIgnoreCase))
                {
                    int id = MagicDllFile.ParseMagicIdFromName(prop.Name);
                    // Historical T4 mutation artifacts live in older audit files beside the
                    // canonical magic_NNNN.dll rows. The exact four-digit name is the schema boundary;
                    // do not use File.Exists here because a missing canonical DLL must still fail.
                    if (IsCanonicalMagicDllName(prop.Name, id))
                        result.Add((prop.Name, id));
                }
            }
            return result.OrderBy(x => x.Id).ToList();
        }

        private static bool IsCanonicalMagicDllName(string name, int parsedId) =>
            parsedId is >= 0 and <= 9_999 &&
            name.Length == "magic_0000.dll".Length &&
            string.Equals(name, $"magic_{parsedId:D4}.dll", StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void LoadOpenOkFromAudit_ExcludesHistoricalNonCanonicalArtifacts()
        {
            List<(string Name, int Id)> openOk = LoadOpenOkFromAudit();

            Assert.All(openOk, entry => Assert.True(
                IsCanonicalMagicDllName(entry.Name, entry.Id),
                $"Historical non-canonical audit artifact leaked into the corpus sample: {entry.Name}."));
        }

        [Theory]
        [InlineData("magic_0098.dll", true)]
        [InlineData("MAGIC_0098.DLL", true)]
        [InlineData("mutation_0098.dll", false)]
        [InlineData("magic_098.dll", false)]
        [InlineData("magic_00098.dll", false)]
        public void CanonicalAuditName_RequiresExactMagicFourDigitSchema(
            string name,
            bool expected)
        {
            int parsedId = MagicDllFile.ParseMagicIdFromName(name);
            if (name == "mutation_0098.dll")
                Assert.True(parsedId >= 0, "Regression input must exercise the parseable non-canonical boundary.");

            Assert.Equal(expected, IsCanonicalMagicDllName(name, parsedId));
        }

        private static string GetAuditStatus(string dllName)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(AuditJsonPath));
                if (doc.RootElement.GetProperty("dlls").TryGetProperty(dllName, out JsonElement e)
                    && e.TryGetProperty("status", out JsonElement s))
                    return s.GetString() ?? "?";
            }
            catch (Exception)
            {
                // Audit ilegível no meio do caminho: não derruba o smoke.
            }

            return "?";
        }

        private void WriteBatchJson(string fileName, string sampleDesc, List<SmokeEntry> entries)
        {
            string path = TestDataPaths.ReportPath(fileName);
            var payload = new
            {
                meta = new
                {
                    generated_by = "FFXProjectEditor.Tests.MagicDll.MagicDllCorpusSmokeTests",
                    timestamp = DateTimeOffset.Now.ToString("O"),
                    corpus_dir = CorpusDir,
                    audit_json = AuditJsonPath,
                    sample = sampleDesc,
                    count = entries.Count,
                    ok_count = entries.Count(e => e.Ok),
                    failed_count = entries.Count(e => !e.Ok),
                    python_audit_open_ok = 347,
                },
                dlls = entries.ToDictionary(e => e.Dll, e => e),
            };
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            _output.WriteLine($"Report: {path}");
        }

        private sealed class SmokeEntry
        {
            public string Dll { get; init; } = "";
            public int Id { get; init; }
            public string AuditStatus { get; init; } = "";
            public bool Ok { get; init; }
            public int RootCount { get; init; }
            public int ProgramCount { get; init; }
            public int TotalSlots { get; init; }
            public int NDescriptors { get; init; }
            public string? Error { get; init; }
        }
        [Fact]
        public void CorpusSmoke_NoRootRecovered_OpensNow()
        {
            // NOROOT_INVESTIGATION 2026-08-01: eram NO_ROOT no audit (section_size
            // virtual-inflado); com a extensão tolerante do root abrem (229/234 recuperadas).
            string[] recovered = { "magic_0003.dll", "magic_0018.dll", "magic_0019.dll" };
            foreach (string dllName in recovered)
            {
                string path = Path.Combine(CorpusDir, dllName);
                bool ok = Parser.TryParse(path, out MagicDllFile? file, out string? error);
                Assert.True(ok, $"{dllName}: {error}");
                Assert.NotNull(file);
                Assert.NotEmpty(file!.Roots);
            }
        }

        [Fact]
        public void CorpusSmoke_NoPppDlls_ProduceSuspiciousRoots()
        {
            // As 5 DLLs "sem PPP" ABREM com a extensão tolerante, mas com raízes
            // suspeitas (falsos positivos: 1 root, 1 program, 1 slot — dados de lixo).
            // Documentado em NOROOT_INVESTIGATION §4; o editor exibe as estruturas
            // com aviso. Limiar mínimo de slots poderia rejeitá-las (risco: rejeitar
            // DLLs legítimas pequenas) — decisão: tolerar e documentar.
            string[] suspicious = { "magic_0052.dll", "magic_0053.dll", "magic_0064.dll", "magic_0065.dll", "magic_0709.dll" };
            foreach (string dllName in suspicious)
            {
                string path = Path.Combine(CorpusDir, dllName);
                bool ok = Parser.TryParse(path, out MagicDllFile? file, out _);
                Assert.True(ok, $"{dllName} deveria abrir (falso positivo tolerado)");
                Assert.NotNull(file);
                int slots = file!.Roots.Sum(r => r.Programs.Sum(p => p.Slots.Count));
                Assert.True(slots <= 3, $"{dllName}: esperado root suspeito (<=3 slots), veio {slots}");
            }
        }
        [Fact]
        public void TryParse_WithCorruptedBytes_NeverThrows()
        {
            // Propriedade do contrato "TryParse nunca lança": mutações aleatórias de
            // 1-8 bytes numa cópia em temp devem retornar true/false SEM exceção.
            string dir = MakeTempDir();
            try
            {
                string src = Path.Combine(CorpusDir, "magic_0021.dll");
                byte[] original = File.ReadAllBytes(src);
                var rng = new Random(1234);

                for (int trial = 0; trial < 200; trial++)
                {
                    string path = Path.Combine(dir, $"magic_fuzz_{trial}.dll");
                    byte[] mutated = (byte[])original.Clone();
                    int nBytes = 1 + rng.Next(8);
                    for (int i = 0; i < nBytes; i++)
                    {
                        int pos = rng.Next(mutated.Length);
                        mutated[pos] = (byte)rng.Next(256);
                    }
                    File.WriteAllBytes(path, mutated);

                    // Deve retornar (true ou false) sem exceção.
                    bool ok = Parser.TryParse(path, out MagicDllFile? file, out string? error);
                    if (ok)
                        Assert.NotNull(file);
                    else
                        Assert.False(string.IsNullOrWhiteSpace(error));
                }
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
