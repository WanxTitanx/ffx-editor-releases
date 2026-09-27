using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.Tools
{
    internal static class SinAiHeaderDumpRt0
    {
        public static int Run(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var mon = Monster_File.Read(bytes);
            var script = AiScript_File.Read(mon.AiFile);
            int maxOff = script.Instructions.Count > 0 ? script.Instructions.Max(i => i.Offset) : 0;
            Console.WriteLine($"path: {path}");
            Console.WriteLine($"CodeLength=0x{script.CodeLength:X} ScriptStart=0x{script.ScriptStart:X} codeEnd=0x{script.ScriptStart + script.CodeLength:X}");
            Console.WriteLine($"instructions={script.Instructions.Count} maxOffset=0x{maxOff:X} closed={script.CodeWalkClosedExactly}");
            if (AiWorkerMapping.TryResolveCombatOnTurn(bytes, script, out var hook, out _))
            {
                var w = script.Workers[hook.WorkerIndex];
                int ep = w.Entrypoints[hook.EntrypointIndex];
                Console.WriteLine($"onTurn worker={hook.WorkerIndex} entryIdx={hook.EntrypointIndex} entryOff=0x{ep:X} abs=0x{script.ScriptStart + ep:X}");
            }
            return 0;
        }
    }
}
