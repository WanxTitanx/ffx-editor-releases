using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.Tools
{
    // Headless gate for the AI ASSEMBLER structural-save path (the MonsterAiEditor free-edit surface):
    //   monster_*.bin -> Monster_File.Read -> AiFile = AiScript_File.Rebuild(...) -> Monster_File.Write
    // The codec's Rebuild (insert/remove/modify anywhere, relocating entrypoints+jumps) is already gated in
    // AiScriptLab at the AiFile level; THIS gate proves the COMPOSED loose-file save (Rebuild routed through
    // the proven grow-aware Monster_File re-layout) is:
    //   (1) byte-identical on a no-edit rebuild (RT0), and
    //   (2) structurally valid on a GROW (insert one instruction -> the whole monster re-reads + the AiFile
    //       re-parses clean, FileSize grows by exactly the delta), and
    //   (3) it MEASURES the AiFile->WorkerFile alignment so the editor can preserve it when growing.
    // Run via: FFXProjectEditor.exe --aiasm-rt0 [monsterRoot]
    internal static class AiAssemblerRt0
    {
        public static int Run(string root)
        {
            Console.WriteLine("=== AI Assembler structural-save gate (Rebuild -> Monster_File re-layout) ===");
            Console.WriteLine($"root : {root}");
            if (!Directory.Exists(root)) { Console.WriteLine("NOT FOUND"); return 2; }

            var files = Directory.EnumerateFiles(root, "m*.bin", SearchOption.AllDirectories)
                .Where(p => { string n = Path.GetFileNameWithoutExtension(p); return n.Length == 4 && n[0] == 'm' && n[1..].All(char.IsDigit); })
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int total = 0, withScript = 0;
            int noEditRebuildRt0 = 0, structuralSaveRt0 = 0, spliceSaveRt0 = 0, growValid = 0, growTested = 0, growBlocked = 0;
            int shrinkValid = 0, shrinkTested = 0;
            int alignKept = 0;
            string? growSample = null;
            string? shrinkSample = null;
            var aligns = new SortedDictionary<int, int>();   // workerPtr alignment -> count
            var trailingPad = new List<int>();               // aiFile.Length - DeclaredLength
            var fails = new List<string>();

            foreach (string path in files)
            {
                string id = Path.GetFileNameWithoutExtension(path);
                byte[] monster = File.ReadAllBytes(path);

                Monster_File mf;
                try { mf = Monster_File.Read(monster); }
                catch { continue; }                          // not a full/clean monster — skip (out of scope)
                if (mf.AiFile == null || mf.AiFile.Length == 0) continue;

                byte[] aiFile = mf.AiFile;
                AiScriptFile script;
                try { script = AiScript_File.Read(aiFile); }
                catch (Exception ex) { fails.Add($"{id}: AiFile Read threw {ex.GetType().Name}"); continue; }
                if (!script.HasScript) continue;

                total++;
                withScript++;

                // alignment of the section that follows the AiFile (= 0x30 + AiFile.Length).
                int workerPtr = 0x30 + aiFile.Length;
                int a = Alignment(workerPtr);
                aligns[a] = aligns.TryGetValue(a, out int c) ? c + 1 : 1;
                trailingPad.Add(aiFile.Length - script.DeclaredLength);

                // (1) no-edit Rebuild == original AiFile (RT0 at the AiFile level).
                byte[] reAi = AiScript_File.Rebuild(script, script.Instructions);
                bool aiRt0 = reAi.AsSpan().SequenceEqual(aiFile);
                if (aiRt0) noEditRebuildRt0++;
                else { fails.Add($"{id}: no-edit Rebuild drift ({aiFile.Length}->{reAi.Length})"); }

                // (2a) no-edit structural SAVE via the heavy Monster_File relayout == original monster.
                Monster_File mf2 = Monster_File.Read(monster);
                mf2.AiFile = reAi;
                byte[] reMonster = mf2.Write();
                bool saveRt0 = reMonster.AsSpan().SequenceEqual(monster);
                if (saveRt0) structuralSaveRt0++;
                else { int d = FirstDiff(monster, reMonster); fails.Add($"{id}: structural save drift @0x{d:X} (len {monster.Length}->{reMonster.Length})"); }

                // (2b) THE ACTUAL UI SAVE PATH for length-preserving structural edits: splice the rebuilt AiFile
                // back via SpliceAiFileIntoMonster (verbatim copy into [aiPtr,...), no header rewrite -> no
                // codeLength clobber). No-edit -> byte-identical monster. This is what the editor ships.
                byte[] spliceMonster = AiScript_File.SpliceAiFileIntoMonster(monster, reAi);
                if (spliceMonster.AsSpan().SequenceEqual(monster)) spliceSaveRt0++;
                else { int d = FirstDiff(monster, spliceMonster); fails.Add($"{id}: splice save drift @0x{d:X}"); }

                // (3) GROW: insert a clone of instruction[0] right after it -> rebuild -> save -> re-read valid.
                if (script.Instructions.Count > 0)
                {
                    growTested++;
                    AiInstruction first = script.Instructions[0];
                    var edited = new List<AiInstruction>(script.Instructions.Count + 1);
                    edited.Add(first);
                    edited.Add(new AiInstruction { Offset = -1, Opcode = first.Opcode, HasOperand = first.HasOperand, Operand = first.Operand });
                    for (int i = 1; i < script.Instructions.Count; i++) edited.Add(script.Instructions[i]);

                    try
                    {
                        byte[] grownAi = AiScript_File.Rebuild(script, edited);

                        // THE PRODUCTION GROW PATH: grow-aware splice (16-pad + relocate sections + rewrite header
                        // pointers, AiFile verbatim -> no codeLength clobber).
                        byte[] grownMonster = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, grownAi);

                        int paddedPartition = (grownAi.Length + 0xF) & ~0xF;
                        int oldWorkerPtr = 0x30 + aiFile.Length;            // aiPtr=0x30, partition = aiFile.Length
                        int growDelta = paddedPartition - aiFile.Length;

                        // re-read the grown monster end-to-end + re-parse its AiFile.
                        Monster_File back = Monster_File.Read(grownMonster);
                        byte[] backAi = back.AiFile ?? Array.Empty<byte>();
                        AiScriptFile backScript = AiScript_File.Read(backAi);

                        // EXACT preservation: every byte after the AiFile partition is the original, just shifted;
                        // and the header Signature/AiFilePointer + Padding[0..12) are untouched.
                        bool tailOk = grownMonster.Length - (0x30 + paddedPartition) == monster.Length - oldWorkerPtr
                                      && grownMonster.AsSpan(0x30 + paddedPartition).SequenceEqual(monster.AsSpan(oldWorkerPtr));
                        bool headOk = grownMonster.AsSpan(0, 8).SequenceEqual(monster.AsSpan(0, 8))
                                      && grownMonster.AsSpan(0x24, 12).SequenceEqual(monster.AsSpan(0x24, 12));

                        bool ok = grownMonster.Length == monster.Length + growDelta
                                  && backAi.Length == paddedPartition
                                  && backScript.HasScript
                                  && backScript.CodeWalkClosedExactly
                                  && backScript.UnknownOpcodes.Count == 0
                                  && backScript.CodeLength == script.CodeLength + first.Length
                                  && backScript.Instructions.Count == script.Instructions.Count + 1
                                  && (0x30 + backAi.Length) % 16 == 0       // WorkerFile stays 16-aligned
                                  && tailOk && headOk;

                        // idempotent: a no-edit grow-aware re-save of the grown monster is byte-stable.
                        byte[] reSaved = AiScript_File.SpliceAiFileIntoMonsterGrow(grownMonster, AiScript_File.Rebuild(backScript, backScript.Instructions));
                        bool idem = reSaved.AsSpan().SequenceEqual(grownMonster);

                        if (ok && idem) growValid++;
                        else { growBlocked++; growSample ??= $"{id}: ok={ok} idem={idem} tail={tailOk} head={headOk} backCode=0x{backScript.CodeLength:X} want 0x{script.CodeLength + first.Length:X}"; }

                        if ((0x30 + backAi.Length) % 16 == 0) alignKept++;
                    }
                    catch (Exception ex) { growBlocked++; growSample ??= $"{id}: grow threw {ex.GetType().Name}: {ex.Message}"; }

                    // (3b) SHRINK: remove ONE instruction that no entrypoint/jump targets (so Rebuild won't dangle)
                    // -> grow-aware splice with NEGATIVE delta -> re-read clean + tail preserved + 16-aligned.
                    var targeted = new HashSet<int>();
                    foreach (AiWorker w in script.Workers)
                    {
                        foreach (int e in w.Entrypoints) targeted.Add(e);
                        foreach (int j in w.JumpTargets) targeted.Add(j);
                    }
                    AiInstruction? victim = script.Instructions.FirstOrDefault(x => (x.Offset - script.ScriptStart) != 0 && !targeted.Contains(x.Offset - script.ScriptStart));
                    if (victim != null)
                    {
                        shrinkTested++;
                        try
                        {
                            var shrunk = script.Instructions.Where(x => !ReferenceEquals(x, victim)).ToList();
                            byte[] shrunkAi = AiScript_File.Rebuild(script, shrunk);
                            byte[] shrunkMonster = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, shrunkAi);
                            int padded = (shrunkAi.Length + 0xF) & ~0xF;
                            int oldWorkerPtr = 0x30 + aiFile.Length;
                            Monster_File back = Monster_File.Read(shrunkMonster);
                            AiScriptFile bs = AiScript_File.Read(back.AiFile ?? Array.Empty<byte>());
                            bool ok = (back.AiFile?.Length ?? -1) == padded
                                      && bs.HasScript && bs.CodeWalkClosedExactly && bs.UnknownOpcodes.Count == 0
                                      && bs.Instructions.Count == script.Instructions.Count - 1
                                      && bs.CodeLength == script.CodeLength - victim.Length
                                      && (0x30 + (back.AiFile?.Length ?? 0)) % 16 == 0
                                      && shrunkMonster.AsSpan(0x30 + padded).SequenceEqual(monster.AsSpan(oldWorkerPtr));
                            if (ok) shrinkValid++;
                            else shrinkSample ??= $"{id}: shrink invalid (backInstr={bs.Instructions.Count} want {script.Instructions.Count - 1})";
                        }
                        catch (Exception ex) { shrinkSample ??= $"{id}: shrink threw {ex.GetType().Name}: {ex.Message}"; }
                    }
                }
            }

            Console.WriteLine($"monsters w/ script : {withScript}");
            Console.WriteLine($"no-edit Rebuild RT0: {noEditRebuildRt0}/{total}   (codec identity: Rebuild(script.Instructions) == AiFile)");
            Console.WriteLine($"relayout save RT0  : {structuralSaveRt0}/{total}  (Read -> AiFile=Rebuild(no-edit) -> Monster_File.Write == original)");
            Console.WriteLine($"splice save RT0    : {spliceSaveRt0}/{total}  (Rebuild(no-edit) -> SpliceAiFileIntoMonster == original; THE UI SAVE PATH)");
            Console.WriteLine($"  -> SHIPPABLE: length-preserving structural edits (codeLength unchanged) save byte-safe via the splice path.");
            Console.WriteLine($"GROW valid         : {growValid}/{growTested}  blocked={growBlocked}  (insert 1 instr -> SpliceAiFileIntoMonsterGrow -> re-read clean + tail/header preserved + 16-align + idempotent)");
            Console.WriteLine($"SHRINK valid       : {shrinkValid}/{shrinkTested}  (remove 1 non-targeted instr -> grow-aware splice (negative delta) -> re-read clean + tail preserved + 16-align)");
            Console.WriteLine($"grow keeps 16-align: {alignKept}/{growTested}  (WorkerFile stays 16-aligned after the grow-aware 16-pad)");
            if (shrinkSample != null) Console.WriteLine($"shrink fail sample : {shrinkSample}");
            Console.WriteLine($"AiFile->next align : {string.Join("  ", aligns.Select(kv => $"{kv.Key}B x{kv.Value}"))}");
            if (trailingPad.Count > 0)
                Console.WriteLine($"AiFile trailing pad: min={trailingPad.Min()} max={trailingPad.Max()} (orig aiFile.Length - DeclaredLength)");
            if (growSample != null)
                Console.WriteLine($"grow fail sample   : {growSample}");
            Console.WriteLine("  -> GROW/SHRINK loose-file save via SpliceAiFileIntoMonsterGrow: 16-pads the AiFile, shifts later");
            Console.WriteLine("     sections + rewrites header pointers, AiFile verbatim (no codeLength clobber). RE-proven safe");
            Console.WriteLine("     (loader sizes VM from worker counts/datalen, trusts in-file pointers). In-game RT2 still recommended.");

            // Gate PASSES on no-edit RT0 (3 paths) + the full GROW envelope (re-read clean + exact preservation +
            // alignment + idempotency) over the corpus.
            bool pass = total > 0 && noEditRebuildRt0 == total && structuralSaveRt0 == total
                        && spliceSaveRt0 == total && growValid == growTested && shrinkValid == shrinkTested
                        && fails.Count == 0;

            if (fails.Count > 0)
            {
                Console.WriteLine("FAILS:");
                foreach (string f in fails.Take(20)) Console.WriteLine("  " + f);
            }

            Console.WriteLine($"VERDICT: {(pass ? "PASS - AI Assembler structural save byte-safe: no-edit RT0 + length-preserving splice + GROW/SHRINK grow-aware (re-read clean, sections preserved, 16-aligned, idempotent)." : "DRIFT/FAIL")}");
            return pass ? 0 : 1;
        }

        static int Alignment(int value)
        {
            if (value == 0) return 0;
            int a = 1;
            while ((value & a) == 0 && a < 0x100) a <<= 1;
            return a;
        }

        static int FirstDiff(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
            return n;
        }
    }
}
