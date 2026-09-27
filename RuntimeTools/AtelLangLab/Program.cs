using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.AtelScript;

namespace AtelLangLab
{
    /// <summary>
    /// F5-L6: CLI da linguagem ATEL de alto nível.
    ///
    /// Uso:
    ///   dotnet run --project RuntimeTools\AtelLangLab -- --src script.atel --monster m001.bin --dry-run
    ///   dotnet run --project RuntimeTools\AtelLangLab -- --src script.atel --monster m001.bin --out m001_new.bin
    ///   [--worker N] [--entrypoint N]  (default: PickCombatWorker + PickMainEntrypoint)
    ///
    /// Dry-run: mostra o diff 3 camadas (bytes/disassembly/semântica) sem escrever nada.
    /// Exit: 0 OK · 1 erro de compilação/aplicação · 2 uso inválido.
    /// </summary>
    internal static class Program
    {
        static int Main(string[] args)
        {
            string? src = null, monster = null, outp = null;
            bool dryRun = false;
            int? workerOverride = null, entrypointOverride = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--src" when i + 1 < args.Length: src = args[++i]; break;
                    case "--monster" when i + 1 < args.Length: monster = args[++i]; break;
                    case "--out" when i + 1 < args.Length: outp = args[++i]; break;
                    case "--dry-run": dryRun = true; break;
                    case "--worker" when i + 1 < args.Length: workerOverride = int.Parse(args[++i]); break;
                    case "--entrypoint" when i + 1 < args.Length: entrypointOverride = int.Parse(args[++i]); break;
                    default:
                        Console.Error.WriteLine($"argumento desconhecido: {args[i]}");
                        return 2;
                }
            }

            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(monster))
            {
                Console.Error.WriteLine(
                    "uso: --src <script.atel> --monster <monster.bin> [--out <out.bin>] [--dry-run] [--worker N] [--entrypoint N]");
                return 2;
            }
            if (!File.Exists(src))
            {
                Console.Error.WriteLine($"script não encontrado: {src}");
                return 2;
            }
            if (!File.Exists(monster))
            {
                Console.Error.WriteLine($"monster bin não encontrado: {monster}");
                return 2;
            }

            try
            {
                byte[] monsterBin = File.ReadAllBytes(monster);
                byte[]? ai = AiScript_File.SliceAiFileFromMonster(monsterBin);
                if (ai == null)
                {
                    Console.Error.WriteLine("monster bin sem partição AI");
                    return 1;
                }
                AiScriptFile script = AiScript_File.Read(ai);

                AiWorker? worker = workerOverride.HasValue
                    ? script.Workers.FirstOrDefault(w => w.Index == workerOverride.Value)
                    : AiAutomation.PickCombatWorker(script);
                if (worker == null)
                {
                    Console.Error.WriteLine($"worker {(workerOverride?.ToString() ?? "combate")} não encontrado "
                        + $"(workers: {string.Join(", ", script.Workers.Select(w => w.Index))})");
                    return 1;
                }
                int entrypoint = entrypointOverride ?? AiAutomation.PickMainEntrypoint(script, worker);

                AtelProgram program = new AtelParser(File.ReadAllText(src)).ParseProgram();
                AtelCompileResult result = AtelProgramCompiler.CompileProgram(program, new AtelCompileContext
                {
                    Script = script,
                    WorkerIndex = worker.Index,
                    EntrypointIndex = entrypoint,
                });

                byte[] outBin = AiScript_File.SpliceAiFileIntoMonsterGrow(monsterBin, result.AiFile);

                if (dryRun)
                {
                    AiScriptFile edited = AiScript_File.Read(result.AiFile);
                    IReadOnlyList<AiDiffEntry> diff = AiScript_Diff.Compare(script, edited);
                    Console.WriteLine($"OK — {result.AppliedSteps} statement(s): {result.Summary}");
                    Console.WriteLine($"diff: {diff.Count} entrada(s) (Unchanged/Modified/Added/Removed)");
                    foreach (AiDiffEntry entry in diff.Where(e => e.Type != AiDiffType.Unchanged).Take(10))
                        Console.WriteLine("  " + entry);
                    return 0;
                }

                if (string.IsNullOrEmpty(outp))
                {
                    Console.Error.WriteLine("informe --out <out.bin> ou --dry-run");
                    return 2;
                }
                File.WriteAllBytes(outp, outBin);
                Console.WriteLine($"OK — {outp} escrito ({outBin.Length} bytes, {result.AppliedSteps} statement(s))");
                return 0;
            }
            catch (AtelSyntaxException ex)
            {
                Console.Error.WriteLine("erro de sintaxe: " + ex.Message);
                return 1;
            }
            catch (AtelEmitException ex)
            {
                Console.Error.WriteLine("erro de emissão: " + ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("erro: " + ex.Message);
                return 1;
            }
        }
    }
}
