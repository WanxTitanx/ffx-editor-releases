using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.Tools
{
    // --sin-possessed-scan m037 — dump how vanilla uses Possessed by Yu Yevon! in AI + abilities.
    internal static class SinPossessedAiScanRt0
    {
        public static int Run(string[] args)
        {
            string monsterId = "m037";
            string? binPath = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--monster" && i + 1 < args.Length)
                    monsterId = args[++i].Trim().ToLowerInvariant();
                else if (!args[i].StartsWith('-'))
                    binPath = args[i];
            }

            if (string.IsNullOrWhiteSpace(binPath))
            {
                string id = monsterId.TrimStart('m');
                binPath = Path.Combine(
                    AiSinPresetCatalog.FindRepoRoot(),
                    "mods", "Spira Reforge", "data", "mods", "ffx_ps2", "ffx", "master", "jppc", "battle", "mon",
                    $"_m{id}", $"m{id}.bin");
            }

            if (!File.Exists(binPath))
            {
                Console.Error.WriteLine($"missing: {binPath}");
                return 1;
            }

            byte[] bytes = File.ReadAllBytes(binPath);
            var mon = Monster_File.Read(bytes);
            var script = AiScript_File.Read(mon.AiFile);
            var actions = AiAutomation.DetectActions(script);

            Console.WriteLine($"=== Possessed scan · {monsterId} ===");
            Console.WriteLine($"path: {binPath}");
            Console.WriteLine($"abilities (non-zero):");
            foreach (ushort ab in mon.StatSheetFile.Abilities.Where(a => a != 0))
            {
                ushort mon2Id = (ushort)(ab & 0x0FFF);
                string name = CommandMonster2_Dictionary.Instance.TryGetValue(mon2Id, out string? n)
                    ? n
                    : $"cmd 0x{ab:X4}";
                bool possessed = ab is >= 0x60E7 and <= 0x60EE;
                Console.WriteLine($"  0x{ab:X4}  {name}{(possessed ? "  [POSSESSED]" : "")}");
            }

            Console.WriteLine($"forced: 0x{mon.StatSheetFile.ForcedAction:X4}");

            var possessedActions = actions
                .Where(a => a.Kind == AiActionKind.Command
                            && a.CommandOperand is >= 0x60E7 and <= 0x60EE)
                .ToList();

            Console.WriteLine($"possessed command actions in script: {possessedActions.Count}");
            foreach (var act in possessedActions.Take(8))
            {
                Console.WriteLine(
                    $"  worker={act.WorkerIndex} call@0x{act.CallOffset:X} force={act.ForcePerform} target=0x{act.TargetOperand:X4}  {act.AbilityName} 0x{act.CommandOperand:X4}");
            }

            if (AiWorkerMapping.TryResolveCombatOnTurn(bytes, script, out AiEventHook hook, out string? mapErr))
                Console.WriteLine($"CombatHandler onTurn: worker={hook.WorkerIndex} entry={hook.EntrypointIndex}");
            else
                Console.WriteLine($"CombatHandler onTurn: unresolved ({mapErr})");

            bool verbose = args.Contains("--verbose");
            if (verbose && possessedActions.Count > 0)
            {
                var act = possessedActions[0];
                Console.WriteLine($"--- context @0x{act.CallOffset:X} worker={act.WorkerIndex} ---");
                DumpInstructionWindow(script, act.CallOffset, 24, 12);
            }
            if (verbose && possessedActions.Count > 0)
            {
                int wi = possessedActions[0].WorkerIndex;
                if (wi >= 0 && wi < script.Workers.Count)
                {
                    var w = script.Workers[wi];
                    Console.WriteLine($"worker {wi} entrypoints ({w.Entrypoints.Count}): {string.Join(", ", w.Entrypoints.Select(e => $"0x{e:X}"))}");
                    for (int idx = 0; idx < w.Entrypoints.Count; idx++)
                    {
                        int abs = script.ScriptStart + w.Entrypoints[idx];
                        string mark = possessedActions.Any(a => a.WorkerIndex == wi && a.CallOffset >= abs && a.CallOffset < abs + 0x40)
                            ? " [near POSSESSED]"
                            : "";
                        Console.WriteLine($"  entry[{idx}] rel=0x{w.Entrypoints[idx]:X} abs=0x{abs:X}{mark}");
                    }
                    if (AiWorkerMapping.TryResolveCombatOnTurn(bytes, script, out AiEventHook h, out _))
                    {
                        int ep = w.Entrypoints[h.EntrypointIndex];
                        int absEp = script.ScriptStart + ep;
                        Console.WriteLine($"onTurn entry[{h.EntrypointIndex}] rel=0x{ep:X} abs=0x{absEp:X} first 8 ins:");
                        DumpInstructionWindow(script, absEp, 0, 8);
                    }
                }
            }

            int workerPtr = BitConverter.ToInt32(bytes, 0x08);
            int statPtr = BitConverter.ToInt32(bytes, 0x0C);
            if (workerPtr > 0 && statPtr > workerPtr)
            {
                Console.WriteLine($"WorkerFile @0x{workerPtr:X} len={statPtr - workerPtr}");
                int sectionCount = bytes[workerPtr];
                int pre = bytes[workerPtr + 1];
                int line = pre + ((pre & 1) == 0 ? 2 : 3);
                for (int i = 0; i < sectionCount && i < 8; i++)
                {
                    int rec = workerPtr + line + i * 4;
                    Console.WriteLine(
                        $"  map[{i}] worker={bytes[rec]} type=0x{bytes[rec + 1]:X2} section@+{BitConverter.ToUInt16(bytes, rec + 2)}");
                }
            }

            return possessedActions.Count > 0 ? 0 : 2;
        }

        static void DumpInstructionWindow(AiScriptFile script, int centerOffset, int before, int after)
        {
            var ins = script.Instructions;
            int idx = ins.ToList().FindIndex(i => i.Offset == centerOffset);
            if (idx < 0)
            {
                Console.WriteLine("  (offset not found in instruction list)");
                return;
            }

            int start = Math.Max(0, idx - before);
            int end = Math.Min(ins.Count - 1, idx + after);
            for (int i = start; i <= end; i++)
            {
                AiInstruction x = ins[i];
                string mark = x.Offset == centerOffset ? " <<" : "";
                string op = $"op 0x{x.Opcode:X2}";
                string operand = x.HasOperand ? $" 0x{x.Operand:X4}" : "";
                Console.WriteLine($"  @0x{x.Offset:X4}  {op}{operand}{mark}");
            }
        }
    }
}
