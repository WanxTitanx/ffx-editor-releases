using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// RT0 EM MASSA: round-trip (parse → serialize → SHA) em TODAS as magic DLLs do
    /// corpus. Caça regressões silenciosas: o contrato "abrir→salvar sem editar =
    /// byte-idêntico" deve valer para as 576 DLLs editáveis (não só 0021/0098).
    /// Resultado serializado em work\magic_editor\ROUNDTRIP_ALL.json.
    /// </summary>
    public class MagicDllRoundTripAllTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;
        private static readonly MagicDllParser Parser = new();
        private readonly ITestOutputHelper _output;

        public MagicDllRoundTripAllTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void RoundTrip_AllDlls_ByteIdentical_AndReport()
        {
            string[] dlls = Directory.GetFiles(CorpusDir, "magic_*.dll")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var failures = new List<string>();
            var ok = new List<string>();
            var noRoot = new List<string>();

            foreach (string path in dlls)
            {
                string name = Path.GetFileName(path);
                try
                {
                    byte[] original = File.ReadAllBytes(path);
                    string shaOriginal = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();

                    if (!Parser.TryParse(path, out MagicDllFile? file, out string? error))
                    {
                        noRoot.Add($"{name}: {error}");
                        continue;
                    }

                    byte[] roundTripped = file!.Serialize();
                    string shaRt = Convert.ToHexString(SHA256.HashData(roundTripped)).ToLowerInvariant();

                    if (original.Length != roundTripped.Length || shaOriginal != shaRt)
                    {
                        failures.Add($"{name}: tamanho {original.Length}->{roundTripped.Length} ou SHA diverge");
                    }
                    else
                    {
                        ok.Add(name);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{name}: exceção {ex.GetType().Name}: {ex.Message}");
                }
            }

            var report = new
            {
                total = dlls.Length,
                ok_byte_identical = ok.Count,
                no_root = noRoot.Count,
                failures = failures.Count,
                no_root_samples = noRoot.Take(10).ToArray(),
                failure_samples = failures.Take(10).ToArray(),
            };
            string reportPath = TestDataPaths.ReportPath("ROUNDTRIP_ALL.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

            _output.WriteLine($"Report: {reportPath}");
            _output.WriteLine($"total={dlls.Length} ok={ok.Count} no_root={noRoot.Count} falhas={failures.Count}");
            foreach (string f in failures.Take(10))
                _output.WriteLine("FAIL: " + f);

            // Contrato: 100% das DLLs parseáveis devem ser byte-idênticas no round-trip.
            Assert.True(
                failures.Count == 0,
                $"round-trip falhou em {failures.Count} DLLs: {string.Join("; ", failures.Take(5))}");
            Assert.True(ok.Count >= 500, $"esperado >= 500 DLLs byte-idênticas, veio {ok.Count}");
        }
    }
}
