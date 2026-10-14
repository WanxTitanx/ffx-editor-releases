using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Varredura de cobertura do field_map: abre uma amostra de DLLs e reporta os
    /// opcodes resolvidos que NÃO estão no catálogo embutido (famílias desconhecidas)
    /// e os slots sem opcode (RAW). Relatório em stdout — não é assert de regressão
    /// (apenas exige que a abertura não falhe). 2026-08-02.
    /// </summary>
    public class MagicDllCoverageScanTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;

        [Fact]
        public void CoverageScan_50Dlls_ReportsUnknownOpcodes()
        {
            // Mesmas options do wrapper (fp.h local Yonishi quando acessível) — o teste
            // direto sem fp.h deixa ~32K slots RAW; com fp.h a cobertura real aparece.
            string? fpDir = TestDataPaths.DatOvRoot;
            var fieldMap = MagicFieldMap.Load();
            var parser = System.IO.Directory.Exists(fpDir)
                ? new MagicDllParser(new MagicDllParserOptions { FieldMap = fieldMap, FpDirectoryPath = fpDir })
                : new MagicDllParser(new MagicDllParserOptions { FieldMap = fieldMap });
            var knownNames = new System.Collections.Generic.HashSet<string>(
                fieldMap.Families.Keys, StringComparer.Ordinal);

            string[] dlls = Directory.GetFiles(CorpusDir, "magic_*.dll")
                .OrderBy(f => f)
                .Where((_, i) => i % 12 == 0) // amostra ~12%
                .Take(50)
                .ToArray();

            var unknownOpcodes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int rawSlots = 0, resolvedSlots = 0, dllsOpened = 0, dllsFailed = 0;

            foreach (string path in dlls)
            {
                try
                {
                    MagicDllFile dll = parser.Parse(path);
                    dllsOpened++;
                    foreach (var slot in dll.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots))
                    {
                        if (slot.OpcodeName is null)
                        {
                            rawSlots++;
                            continue;
                        }
                        resolvedSlots++;
                        if (!knownNames.Contains(slot.OpcodeName))
                            unknownOpcodes[slot.OpcodeName] = unknownOpcodes.GetValueOrDefault(slot.OpcodeName) + 1;
                    }
                }
                catch (Exception ex)
                {
                    dllsFailed++;
                    Console.WriteLine($"FAIL {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            Console.WriteLine($"DLLs abertas: {dllsOpened}/{dlls.Length} (falhas: {dllsFailed})");
            Console.WriteLine($"Slots resolvidos: {resolvedSlots} · RAW: {rawSlots}");
            Console.WriteLine($"Opcodes FORA do field_map ({unknownOpcodes.Count}):");
            foreach ((string op, int count) in unknownOpcodes)
                Console.WriteLine($"  {op}: {count} slots");

            // Gate de cobertura: o resolvedor .rdata (2026-08-02) revela TODOS os opcodes
            // por nome; famílias fora do field_map = sem schema de payload (editáveis só
            // por nome/raw) — estado normal (65 famílias SEM_PAYLOAD no catálogo master).
            // O report acima documenta quais precisam de schema; 0 opcodes fora do
            // field_map só quando TODAS as famílias do corpus tiverem schema.
            Assert.True(dllsOpened >= 45, $"cobertura insuficiente: {dllsOpened}/50");
            Assert.True(resolvedSlots > 0, "nenhum slot resolvido — resolvedor .rdata inoperante?");
        }
    }
}
