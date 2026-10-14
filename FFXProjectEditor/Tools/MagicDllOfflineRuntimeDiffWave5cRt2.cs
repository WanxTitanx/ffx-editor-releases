using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Wave 5c: batch offline <c>live vs PE</c> on existing runtime dumps under <c>work/</c> — no FFX.
    /// </summary>
    internal static class MagicDllOfflineRuntimeDiffWave5cRt2
    {
        public static int Run(string[] args)
        {
            string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
            string workRoot = Path.IsPathRooted(ArgValue(args, "--work") ?? "work")
                ? ArgValue(args, "--work")!
                : Path.Combine(repoRoot, ArgValue(args, "--work") ?? "work");
            string magicRoot = ArgValue(args, "--magic-root") ?? MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
            string outDir = Path.Combine(workRoot, "magic_dll_logical_decompile_wave2", "wave5");

            Console.WriteLine("=== Wave 5c — offline runtime diff (existing dumps) ===");
            Console.WriteLine($"work root : {workRoot}");

            if (!Directory.Exists(workRoot))
            {
                Console.WriteLine("SKIP: work root missing");
                return 2;
            }

            var sessions = FindSessions(workRoot).ToList();
            if (sessions.Count == 0)
            {
                Console.WriteLine("SKIP: no session folders with ppp_dataA.bin found under work/");
                Directory.CreateDirectory(outDir);
                WriteJson(outDir, "wave5c_offline_runtime_diff.json", new { sessions = Array.Empty<object>(), note = "no dumps" });
                return 2;
            }

            var results = new List<object>();
            foreach (string session in sessions)
            {
                string? phase = Directory.GetDirectories(session, "phase_*")
                    .OrderByDescending(Directory.GetCreationTimeUtc)
                    .FirstOrDefault(d => File.Exists(Path.Combine(d, "ppp_dataA.bin")));
                if (phase == null)
                    continue;

                string livePath = Path.Combine(phase, "ppp_dataA.bin");
                int magicId = GuessMagicId(session);
                string? peDll = ResolvePeDll(magicRoot, magicId, session);
                if (peDll == null)
                {
                    results.Add(new { session, phase = Path.GetFileName(phase), error = "pe_dll_not_found", magicId });
                    continue;
                }

                var diff = DiffDataA(livePath, peDll);
                string outSession = Path.Combine(outDir, "runtime_diff", Path.GetFileName(session));
                Directory.CreateDirectory(outSession);
                WriteJson(outSession, "diff_summary.json", diff);

                results.Add(new
                {
                    session = Path.GetFileName(session),
                    magicId,
                    peDll = Path.GetFileName(peDll),
                    diff.diffBytes,
                    diff.cyanStripDiffs,
                    diff.liveNonZero,
                    diff.peNonZero,
                });
                Console.WriteLine($"  {Path.GetFileName(session)}: diff={diff.diffBytes} cyan={diff.cyanStripDiffs}");
            }

            Directory.CreateDirectory(outDir);
            WriteJson(outDir, "wave5c_offline_runtime_diff.json", new { count = results.Count, results });
            File.WriteAllText(
                Path.Combine(outDir, "WAVE5C_OFFLINE_RUNTIME.md"),
                BuildMarkdown(results),
                new UTF8Encoding(false));

            Console.WriteLine($"sessions analyzed : {results.Count}");
            Console.WriteLine($"output            : {outDir}");
            return 0;
        }

        static IEnumerable<string> FindSessions(string workRoot)
        {
            foreach (string path in Directory.EnumerateFiles(workRoot, "ppp_dataA.bin", SearchOption.AllDirectories))
            {
                string? dir = Path.GetDirectoryName(path);
                while (dir != null && !dir.EndsWith("work", StringComparison.OrdinalIgnoreCase))
                {
                    if (Path.GetFileName(dir).StartsWith("session_", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return dir;
                        break;
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }
        }

        static int GuessMagicId(string sessionPath)
        {
            string name = Path.GetFileName(sessionPath).ToLowerInvariant();
            if (name.Contains("thundafira") || name.Contains("0716"))
                return 716;
            if (name.Contains("0717"))
                return 717;
            return 716;
        }

        static string? ResolvePeDll(string magicRoot, int magicId, string sessionPath)
        {
            string primary = Path.Combine(magicRoot, $"magic_{magicId:D4}.dll");
            if (File.Exists(primary))
                return primary;
            foreach (string f in Directory.EnumerateFiles(sessionPath, "magic_*.dll", SearchOption.AllDirectories))
                return f;
            return null;
        }

        static (int diffBytes, int cyanStripDiffs, int liveNonZero, int peNonZero) DiffDataA(string livePath, string peDllPath)
        {
            const uint dataARva = 0x94B0;
            const int dataASize = 0x1000;
            byte[] live = File.ReadAllBytes(livePath);
            byte[] pe = File.ReadAllBytes(peDllPath);
            byte[] staticSlice = new byte[dataASize];
            if (dataARva + dataASize <= pe.Length)
                Array.Copy(pe, (int)dataARva, staticSlice, 0, dataASize);

            int diffs = 0, cyan = 0;
            int len = Math.Min(live.Length, staticSlice.Length);
            for (int i = 0; i < len; i++)
            {
                if (live[i] == staticSlice[i])
                    continue;
                diffs++;
                if (i is >= 0x58C and <= 0x620)
                    cyan++;
            }

            int liveNz = live.Count(b => b != 0);
            int peNz = staticSlice.Count(b => b != 0);
            return (diffs, cyan, liveNz, peNz);
        }

        static string BuildMarkdown(IReadOnlyList<object> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Wave 5c — offline runtime diff");
            sb.AppendLine();
            sb.AppendLine($"Sessions: **{results.Count}** (no FFX — pre-existing dumps only)");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            sb.AppendLine("```");
            return sb.ToString();
        }

        static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        static void WriteJson(string dir, string name, object value) =>
            File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(value, JsonOpts), new UTF8Encoding(false));

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
