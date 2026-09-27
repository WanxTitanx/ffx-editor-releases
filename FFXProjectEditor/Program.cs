using Avalonia;
using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.Services;
#if FFX_INCLUDE_DEVTOOLS
using FFXProjectEditor.Tools;
#endif

namespace FFXProjectEditor
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Capture any otherwise-fatal exception to %LocalAppData%\FFXProjectEditor\crash.log so a
            // GUI crash (e.g. opening a module against a workspace that lacks the file) leaves a stack
            // trace instead of silently killing the window.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Utils.CrashLog.Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);

#if FFX_INCLUDE_DEVTOOLS
            // Headless offline audits (no Avalonia). Keep BEFORE any Avalonia/UI init.
            if (args.Length > 0 && args[0] == "--monster-rt0")
            {
                string root = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\mon");
                Environment.Exit(Tools.MonsterRt0.Run(root));
                return;
            }
            if (args.Length > 0 && args[0] == "--monster-capture-bit-rt0")
            {
                // Capture Cascade Cap-1 Phase C: byte-level flag writer (single-byte ArenaId at
                // StatSheetPointer+0x78). Gate proves same-value byte-identity + slot-only 1-byte
                // diff + flip-and-restore byte-identity across the monster corpus.
                string root = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\mon");
                Environment.Exit(Tools.MonsterCaptureFlagRt0.Run(root));
                return;
            }
            if (args.Length > 0 && args[0] == "--wave1-rt0")
            {
                Environment.Exit(Tools.Wave1Rt0.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--field-explorer-refresh-walk")
            {
                Environment.Exit(Tools.FieldExplorerWalkPublishRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--scanward-ball-inject-rt0")
            {
                // Scan-UI art track: author Holy(gold)+Dark(violet) element balls into free space
                // of atlas 16128 (battle.dds.phyre) by lossless block-copy + endpoint recolor of the
                // white Blizzard ball. Prints the game-UVs the Scan-UI hook must draw. DLL-independent.
                Environment.Exit(Tools.ScanWardBallInjectRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--btltexttable-rt0")
            {
                // Defaults to the JP file (3982B, kanji-bearing); BtlTextTableRt0 also probes sibling
                // language dirs (new_uspc/jppc/inpc) if the supplied path is missing.
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\btl_txt.bin");
                Environment.Exit(Tools.BtlTextTableRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--aurora-editviewer-rt0")
            {
                // Headless EditViewer: runs the same RenderSelectedEmbeddedAsync + ExternalBrowserLauncher
                // path as the button — proves the ?edit=1 battle-stage route + browser open on Linux,
                // where Xwayland input injection can't drive the Avalonia UI.
                Environment.Exit(Tools.AuroraEditViewerRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--dragbridge-selftest")
            {
                // Prove the web→editor drag bridge round-trips: start it, POST a drag to its own port, read it back.
                Modules.AuroraChamber.AuroraDragBridge.EnsureStarted();
                int port = Modules.AuroraChamber.AuroraDragBridge.Port;
                Console.WriteLine($"bridge port (OS-assigned free): {port}");
                if (port == 0) { Console.WriteLine("FAIL: bridge did not start."); Environment.Exit(1); return; }
                using System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
                string payload = "{\"battleId\":\"__selftest__\",\"role\":\"monster_live\",\"index\":2,\"x\":1.5,\"y\":2.5,\"z\":-3.5}";
                System.Net.Http.HttpResponseMessage resp =
                    http.PostAsync($"http://127.0.0.1:{port}/drag",
                        new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                System.Collections.Generic.IReadOnlyDictionary<string, Modules.AuroraChamber.AuroraDragBridge.Coord> snap =
                    Modules.AuroraChamber.AuroraDragBridge.Snapshot("__selftest__");
                bool ok = false;
                string detail;
                if (resp.IsSuccessStatusCode && snap.TryGetValue("monster_live|2", out Modules.AuroraChamber.AuroraDragBridge.Coord c))
                {
                    ok = Math.Abs(c.X - 1.5f) < 1e-4 && Math.Abs(c.Y - 2.5f) < 1e-4 && Math.Abs(c.Z + 3.5f) < 1e-4;
                    detail = $"monster_live|2 = {c.X},{c.Y},{c.Z}";
                }
                else { detail = $"keys=[{string.Join(",", snap.Keys)}]"; }
                Console.WriteLine(ok
                    ? $"PASS — POST {(int)resp.StatusCode}, drag round-tripped ({detail})."
                    : $"FAIL — POST {(int)resp.StatusCode}, {detail}");
                Environment.Exit(ok ? 0 : 1);
                return;
            }
            if (args.Length > 0 && args[0] == "--kernelcmd-roundtrip")
            {
                string dir = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\new_uspc\battle\kernel");
                Environment.Exit(Tools.KernelCommandRoundtripRt0.Run(dir));
                return;
            }
            if (args.Length > 0 && args[0] == "--kernelcmd-clone-rt0")
            {
                string? path = args.Length > 1 ? args[1] : null;
                int donor = args.Length > 2 && int.TryParse(args[2], out int d) ? d : 104;
                Environment.Exit(Tools.KernelCommandCloneRt0.Run(path, donor));
                return;
            }
            if (args.Length > 0 && args[0] == "--kimahri-ronso-parse")
            {
                string? path = args.Length > 1 ? args[1] : null;
                Environment.Exit(Tools.KimahriRonsoParseRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--spira-reforge-extended-pack-rt0")
            {
                Environment.Exit(Tools.SpiraReforgeExtendedPackRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--aicmdmeta-rt0")
            {
                Environment.Exit(Tools.AiCommandMetadataRt0.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--spiraatlas-rt0")
            {
                Environment.Exit(Tools.SpiraDataAtlasRt0.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-dryrun-rt0")
            {
                // SIN Chain Builder Gate 2: plan the 3 pilot recipes into read-only preview plans (no writer, no save).
                Environment.Exit(Tools.SinDryRunRt0.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-validate-rt0")
            {
                // SIN Chain Builder Gate 3: validate the 3 pilot plans via the read-only AiScriptLab bridge
                // (structural/encoding/stack + honest blockers) — no monster, no Rebuild, no apply, no save.
                Environment.Exit(Tools.SinValidateRt0.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-aeon-preview-rt0")
            {
                // SIN Chain Builder Gate 4: render the AEON diff preview for the 3 pilot plans (synthetic target,
                // ADDED-only) — no monster, no real worker diffed, no backup, no apply, no save.
                Environment.Exit(Tools.SinAeonPreviewRt0.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-sandbox-rt0")
            {
                // SIN Chain Builder Gate 5: operator-gated backup/apply SANDBOX. Applies SIN-006 to a throwaway COPY
                // of a real monster under work/ (.prev.bak backup, real AiScript_Diff, byte-identical restore, original
                // untouched); SIN-009 (candidate) + SIN-010 (0x16/0x17) stay BLOCKED. NOT RT2, NOT a public button.
                Environment.Exit(Tools.SinSandboxApplyRt0.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-pilot-rt2-rt0")
            {
                // SIN Chain Builder Gate 6: operator-gated RT2 in-game pilot PREFLIGHT. Reuses the Gate-5 sandbox
                // round-trip (copy-only, byte-identical restore) under a strictly narrower RT2 eligibility gate
                // (SIN-006 only, Self only, no payload, no 0x16/0x17, separate AllowRt2InGamePilot permission) and
                // stages the aim + on-screen effect a live pilot would verify. NEVER writes a real file or drives the
                // probe; OperatorVerdict stays Pending (RT2-pending). SIN-009/010 stay BLOCKED. NOT RT2-proved, NOT public.
                Environment.Exit(Tools.SinRt2PilotRt0.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-pilot-rt2-live-rt0")
            {
                // SIN Gate 6 LIVE glue self-test: stage (operator-gated, artifacts under work/ only) + read-only live
                // verify. SIN-006 grows the script → the grown image enters via the RELOAD path (operator's manual
                // swap per the runbook), NEVER an in-place live RAM write. Headless-safe; refusals leave zero files.
                Environment.Exit(Tools.SinRt2LiveRt0.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-pilot-rt2-stage")
            {
                // OPERATOR command: stage the SIN-006 pilot artifacts to work/sin_rt2_live (kept). Staging != applying;
                // the real game-file swap stays a manual runbook step with backup + immediate restore.
                Environment.Exit(Tools.SinRt2LiveRt0.RunStage(args.Length > 1 ? args[1] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-pilot-rt2-verify")
            {
                // OPERATOR command: READ-ONLY live verification — locates the staged monster in the current battle and
                // proves byte-exactly whether the ORIGINAL or the EDITED (SIN-006) image is loaded. Never writes.
                Environment.Exit(Tools.SinRt2LiveRt0.RunVerify());
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-possessed-pilot")
            {
                Environment.Exit(Tools.SinPossessedPilotRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-possessed-scan")
            {
                Environment.Exit(Tools.SinPossessedAiScanRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-scale-pilot")
            {
                Environment.Exit(Tools.SinScalePilotRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--monster-od-grow-pilot" && args.Length > 2)
            {
                string input = args[1];
                string outputDir = args[2];
                bool monsterMagic2 = args.Length < 4 || args[3] != "--magic1";
                bool fromBackup = args.Length > 3 && args[3] == "--from-backup";
                var original = File.ReadAllBytes(input);
                if (!fromBackup) {
                    var r0a = FfxLib.Ability.MonsterMagicGrowWriter.AppendPrismFlare(original, monsterMagic2);
                    Console.WriteLine($"PrismFlare:   NewId=0x{r0a.Operand:X4} Pass={r0a.Pass}");
                    var r0b = FfxLib.Ability.MonsterMagicGrowWriter.AppendThundaFira(r0a.GrownBytes, monsterMagic2);
                    Console.WriteLine($"ThundaFira:   NewId=0x{r0b.Operand:X4} Pass={r0b.Pass}");
                    var r0c = FfxLib.Ability.MonsterMagicGrowWriter.AppendFlameFlanFlood(r0b.GrownBytes, monsterMagic2);
                    Console.WriteLine($"FlanFlood:    NewId=0x{r0c.Operand:X4} Pass={r0c.Pass}");
                    var r1 = FfxLib.Ability.MonsterMagicGrowWriter.AppendGoreCharge(r0c.GrownBytes, monsterMagic2, 171);
                    Console.WriteLine($"GoreCharge:   NewId=0x{r1.Operand:X4} Pass={r1.Pass}");
                    var r2 = FfxLib.Ability.MonsterMagicGrowWriter.AppendFangStrike(r1.GrownBytes, monsterMagic2);
                    Console.WriteLine($"FangStrike:   NewId=0x{r2.Operand:X4} Pass={r2.Pass}");
                    var r3 = FfxLib.Ability.MonsterMagicGrowWriter.AppendSnipe(r2.GrownBytes, monsterMagic2);
                    Console.WriteLine($"Snipe:        NewId=0x{r3.Operand:X4} Pass={r3.Pass}");
                    var r4 = FfxLib.Ability.MonsterMagicGrowWriter.AppendFeatherStorm(r3.GrownBytes, monsterMagic2);
                    Console.WriteLine($"FeatherStorm: NewId=0x{r4.Operand:X4} Pass={r4.Pass}");
                    var r5 = FfxLib.Ability.MonsterMagicGrowWriter.AppendUltraBlizzara(r4.GrownBytes, monsterMagic2);
                    Console.WriteLine($"UltraBlizzara: NewId=0x{r5.Operand:X4} Pass={r5.Pass}");
                    var r6 = FfxLib.Ability.MonsterMagicGrowWriter.AppendVenomSting(r5.GrownBytes, monsterMagic2);
                    Console.WriteLine($"VenomSting:    NewId=0x{r6.Operand:X4} Pass={r6.Pass}");
                    var r7 = FfxLib.Ability.MonsterMagicGrowWriter.AppendChaosSpark(r6.GrownBytes, monsterMagic2);
                    Console.WriteLine($"ChaosSpark:    NewId=0x{r7.Operand:X4} Pass={r7.Pass}");
                    var r8 = FfxLib.Ability.MonsterMagicGrowWriter.AppendMaggotBurst(r7.GrownBytes, monsterMagic2);
                    Console.WriteLine($"MaggotBurst:   NewId=0x{r8.Operand:X4} Pass={r8.Pass}");
                    var r9 = FfxLib.Ability.MonsterMagicGrowWriter.AppendEvilGaze(r8.GrownBytes, monsterMagic2);
                    Console.WriteLine($"EvilGaze:      NewId=0x{r9.Operand:X4} Pass={r9.Pass}");
                    var r10 = FfxLib.Ability.MonsterMagicGrowWriter.AppendSoulDrain(r9.GrownBytes, monsterMagic2);
                    Console.WriteLine($"SoulDrain:     NewId=0x{r10.Operand:X4} Pass={r10.Pass}");
                    var r11 = FfxLib.Ability.MonsterMagicGrowWriter.AppendThunderCharge(r10.GrownBytes, monsterMagic2);
                    Console.WriteLine($"ThunderCharge: NewId=0x{r11.Operand:X4} Pass={r11.Pass}");
                    var r12 = FfxLib.Ability.MonsterMagicGrowWriter.AppendWildFlurry(r11.GrownBytes, monsterMagic2);
                    Console.WriteLine($"WildFlurry:    NewId=0x{r12.Operand:X4} Pass={r12.Pass}");
                    var r13 = FfxLib.Ability.MonsterMagicGrowWriter.AppendPermafrost(r12.GrownBytes, monsterMagic2);
                    Console.WriteLine($"Permafrost:    NewId=0x{r13.Operand:X4} Pass={r13.Pass}");
                    var r14 = FfxLib.Ability.MonsterMagicGrowWriter.AppendManaStormFire(r13.GrownBytes, monsterMagic2);
                    Console.WriteLine($"ManaStormFire: NewId=0x{r14.Operand:X4} Pass={r14.Pass}");
                    var r15 = FfxLib.Ability.MonsterMagicGrowWriter.AppendManaStormIce(r14.GrownBytes, monsterMagic2);
                    Console.WriteLine($"ManaStormIce:  NewId=0x{r15.Operand:X4} Pass={r15.Pass}");
                    var r16 = FfxLib.Ability.MonsterMagicGrowWriter.AppendManaStormThunder(r15.GrownBytes, monsterMagic2);
                    Console.WriteLine($"ManaStormThun: NewId=0x{r16.Operand:X4} Pass={r16.Pass}");
                    var r17 = FfxLib.Ability.MonsterMagicGrowWriter.AppendFrostClaw(r16.GrownBytes, monsterMagic2);
                    Console.WriteLine($"FrostClaw:     NewId=0x{r17.Operand:X4} Pass={r17.Pass}");
                    original = r17.GrownBytes;
                }
                var r18 = FfxLib.Ability.MonsterMagicGrowWriter.AppendWardStack(original, monsterMagic2);
                Console.WriteLine($"WardStack:     NewId=0x{r18.Operand:X4} Pass={r18.Pass}");
                Directory.CreateDirectory(outputDir);
                string outFile = Path.Combine(outputDir, Path.GetFileName(input));
                File.WriteAllBytes(outFile, r18.GrownBytes);
                Console.WriteLine($"OK: {outFile} ({r18.GrownLength} bytes, {r18.NewEntryCount} entries)");
                Environment.Exit(0);
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-ai-header-dump" && args.Length > 1)
            {
                Environment.Exit(Tools.SinAiHeaderDumpRt0.Run(args[1]));
                return;
            }
            if (args.Length > 0 && args[0] == "--monmagic2-names" && args.Length > 1)
            {
                string path = args[1];
                byte[] raw = System.IO.File.ReadAllBytes(path);
                var list = FfxLib.Ability.Ability_Command.ReadList(raw, hasExtraInfo: false);
                int startIdx = args.Length > 2 && int.TryParse(args[2], out int s) ? s : 0;
                for (int i = startIdx; i < list.Count; i++)
                {
                    var c = list[i];
                    string name = Utils.Encoding.FfxEncoding.DecodeScriptLossless(
                        c.NameScriptBytes ?? System.Array.Empty<byte>(),
                        Utils.Encoding.FfxEncoding.UsDecoder);
                    int operand = 0x6000 | i;
                    System.Console.WriteLine($"  idx {i,3} (0x{operand:X4}) Anim1={c.Anim1Id,5} Anim2={c.Anim2Id,5} | '{name}'");
                }
                Environment.Exit(0);
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-curse-spread")
            {
                Environment.Exit(Tools.SinCurseSpreadLab.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-curse-bake")
            {
                Environment.Exit(Tools.SinCurseSpreadBakeLab.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--bake-uni004" && args.Length > 1)
            {
                string monsterPath = args[1];
                byte[] monsterBytes = System.IO.File.ReadAllBytes(monsterPath);
                byte[]? aiBytes = FfxLib.Ai.AiScript_File.SliceAiFileFromMonster(monsterBytes);
                if (aiBytes == null)
                { System.Console.WriteLine("FAIL: monster has no AiFile"); Environment.Exit(1); return; }
                var script = FfxLib.Ai.AiScript_File.Read(aiBytes);
                if (script == null || !script.HasScript)
                { System.Console.WriteLine("FAIL: monster has no AiFile"); Environment.Exit(1); return; }

                if (!FfxLib.Ai.AiScript_File.TryFindFreePrivateVariableSlot(script, out int varSlot, out string varReason))
                { System.Console.WriteLine($"FAIL: {varReason}"); Environment.Exit(1); return; }

                if (!FfxLib.Ai.AiWorkerMapping.TryResolveCombatOnTurn(monsterBytes, script, out FfxLib.Ai.AiEventHook hook, out string resolveErr))
                { System.Console.WriteLine($"FAIL: {resolveErr}"); Environment.Exit(1); return; }

                System.Func<byte, ushort, FfxLib.Ai.AiInstruction> Op = (opcode, operand) => new FfxLib.Ai.AiInstruction
                {
                    Offset = -1, Opcode = opcode,
                    HasOperand = FfxLib.Ai.AiScript_File.IsOperandBearing(opcode),
                    Operand = operand,
                    OperandKind = FfxLib.Ai.AiScript_File.OperandKindOf(opcode),
                };
                System.Func<byte, FfxLib.Ai.AiInstruction> Op0 = (opcode) => new FfxLib.Ai.AiInstruction
                {
                    Offset = -1, Opcode = opcode,
                    HasOperand = false, Operand = 0,
                    OperandKind = FfxLib.Ai.AiOperandKind.None,
                };

                var guard = new System.Collections.Generic.List<FfxLib.Ai.AiInstruction>
                {
                    Op(0x9F, (ushort)varSlot),
                    Op(0xAE, 0),
                    Op0(0x06),
                };
                var action = new System.Collections.Generic.List<FfxLib.Ai.AiInstruction>
                {
                    Op(0xAE, 0xFFF3), Op(0xAE, 0x610B), Op(0xD8, 0x705A),
                    Op(0x9F, (ushort)varSlot), Op(0xAE, 1), Op0(0x14), Op(0xA0, (ushort)varSlot),
                };

                byte[] newAi;
                try { newAi = FfxLib.Ai.AiScript_File.AppendGuardedAction(script, hook.WorkerIndex, hook.EntrypointIndex, guard, action); }
                catch (System.Exception ex) { System.Console.WriteLine($"FAIL append: {ex.Message}"); Environment.Exit(1); return; }

                var check = FfxLib.Ai.AiValidator.ValidateRebuilt(newAi, script.OriginalAiFileBytes.Length);
                if (!check.IsValid)
                { System.Console.WriteLine($"FAIL validation: {check.Errors.FirstOrDefault()}"); Environment.Exit(1); return; }

                byte[] patched = FfxLib.Ai.AiScript_File.SpliceAiFileIntoMonsterGrow(monsterBytes, newAi);
                System.IO.File.WriteAllBytes(monsterPath, patched);
                System.Console.WriteLine($"OK: UNI-004 Ward Stack applied to {System.IO.Path.GetFileName(monsterPath)} (var[{varSlot}], w{hook.WorkerIndex}_e{hook.EntrypointIndex})");
                Environment.Exit(0);
                return;
            }
            if (args.Length > 0 && args[0] == "--sin-roster-from-btl")
            {
                Environment.Exit(Tools.SinAreaRosterFromBtlLab.Run(args));
                return;
            }
            if (args.Length > 0 && (args[0] == "--blitz-save-read" || args[0] == "--blitz-save-diff"))
            {
                // Read-only Blitzball save prize reader / save-compare differ (Jarvis-WAKKA scout).
                Environment.Exit(Tools.BlitzballSaveRead.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--task-reward-inspect")
            {
                // FROZEN (v2.152.1.0): CLI-only research atlas — UI removed from editor. No writer.
                Environment.Exit(Tools.TaskRewardInspector.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--blitz-save-write")
            {
                // Blitzball save prize writer (lab): writes a NEW file, never overwrites the source save.
                Environment.Exit(Tools.BlitzballSaveWrite.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--ffx-save-rt0")
            {
                Environment.Exit(Tools.FfxSaveRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--ffx-save-rt2")
            {
                Environment.Exit(Tools.FfxSaveRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--spheregrid-save-migration-analyze")
            {
                Environment.Exit(Tools.SphereGridSaveMigrationAnalyze.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--spheregrid-sidecar-validate")
            {
                Environment.Exit(Tools.SphereGridSidecarRt0.RunValidate(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--spheregrid-sidecar-create")
            {
                Environment.Exit(Tools.SphereGridSidecarRt0.RunCreate(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--spheregrid-sidecar-info")
            {
                Environment.Exit(Tools.SphereGridSidecarRt0.RunInfo(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--sgm-fixtures-bootstrap")
            {
                Environment.Exit(Tools.SgmFixturesBootstrap.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--spheregrid-extra-node-rt0")
            {
                Environment.Exit(Tools.SphereGridExtraNodeRt0.RunRoundTrip(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--spheregrid-save-diff")
            {
                Environment.Exit(Tools.SphereGridExtraNodeRt0.RunDiff(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--spheregrid-insert-node")
            {
                Environment.Exit(Tools.SphereGridInsertNodeRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--btltext-jp-decode")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\btl_txt.bin");
                Environment.Exit(Tools.BtlTextJpDecodeRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--encounter-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\btl.bin");
                Environment.Exit(Tools.EncounterRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--live-battle-snapshot")
            {
                Environment.Exit(Tools.LiveBattleSnapshotRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--encounter-rebuild-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\btl.bin");
                Environment.Exit(Tools.EncounterRebuildRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--player-rt0")
            {
                string save = args.Length > 1 ? args[1] : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\ply_save.bin");
                string rom = args.Length > 2 ? args[2] : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\ply_rom.bin");
                Environment.Exit(Tools.PlayerRt0.Run(save, rom));
                return;
            }
            if (args.Length > 0 && args[0] == "--textstr-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\help_txt.bin");
                Environment.Exit(Tools.TextStringRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--keyitem-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\important.bin");
                Environment.Exit(Tools.KeyItemRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--treasure-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\takara.bin");
                Environment.Exit(Tools.TreasureRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--blitzball-roster-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\event\obj\bl\bltz0002\bltz0002.ebp");
                Environment.Exit(Tools.BlitzballRosterRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--blitzball-recruit-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\event\obj\gu\guad0000\guad0000.ebp");
                Environment.Exit(Tools.BlitzballRecruitRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--blitzball-prizestruct-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\event\obj\bl\bltz0200\bltz0200.ebp");
                Environment.Exit(Tools.BlitzballPrizeStructRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--weapon-name-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\new_uspc\battle\kernel\w_name.bin");
                Environment.Exit(Tools.WeaponNameTableRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--shopgearcatalog-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\shop_arms.bin");
                Environment.Exit(Tools.ShopGearCatalogRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--bukigettreasurecatalog-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\buki_get.bin");
                Environment.Exit(Tools.BukiGetTreasureCatalogRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--customization-rt0")
            {
                string gear = args.Length > 1 ? args[1] : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\kaizou.bin");
                string aeon = args.Length > 2 ? args[2] : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\sum_grow.bin");
                Environment.Exit(Tools.CustomizationRt0.Run(gear, aeon));
                return;
            }
            if (args.Length > 0 && args[0] == "--autoability-rt0")
            {
                string ability = args.Length > 1 ? args[1] : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\a_ability.bin");
                string prices = args.Length > 2 ? args[2] : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\arms_rate.bin");
                Environment.Exit(Tools.AutoAbilityRt0.Run(ability, prices));
                return;
            }
            if (args.Length > 0 && args[0] == "--autoability-grow-rt0")
            {
                string ability = args.Length > 1 ? args[1] : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\new_uspc\battle\kernel\a_ability.bin");
                string prices = args.Length > 2 ? args[2] : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\arms_rate.bin");
                Environment.Exit(Tools.AutoAbilityGrowRt0.Run(ability, prices));
                return;
            }
            if (args.Length > 0 && args[0] == "--autoability-id129-pilot-rt0")
            {
                string ability = args.Length > 1 ? args[1] : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\new_uspc\battle\kernel\a_ability.bin");
                string prices = args.Length > 2 ? args[2] : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\arms_rate.bin");
                Environment.Exit(Tools.AutoAbilityId129PilotRt0.Run(ability, prices));
                return;
            }
            if (args.Length > 0 && args[0] == "--ps3magic-phyre-repack-rt0")
            {
                Environment.Exit(Tools.Ps3MagicPhyreRepackRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--ps3magic-recolor-rt0")
            {
                Environment.Exit(Tools.Ps3MagicRecolorRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--newcontent-textplan-rt0")
            {
                string root = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master");
                Environment.Exit(Tools.NewContentTextPlanRt0.Run(root));
                return;
            }
            if (args.Length > 0 && args[0] == "--monmagic-grow-rt0")
            {
                string input = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\new_uspc\battle\kernel\monmagic2.bin");
                string outputDir = args.Length > 2 ? args[2]
                    : @"work\monster_magic_grow_pilot";
                Environment.Exit(Tools.MonsterMagicGrowRt0.Run(input, outputDir));
                return;
            }
            if (args.Length > 0 && args[0] == "--monster-od-grow-pack")
            {
                MonsterOdGrowPackRt0.Run(args[1], args[2]);
                return;
            }
            if (args.Length > 0 && args[0] == "--monmagic2-sin-pack-rt0")
            {
                string input = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.SteamCommonRoot, @"FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\monmagic2.bin");
                string outputDir = args.Length > 2 ? args[2]
                    : @"work\monmagic2_sin_pack";
                Environment.Exit(Tools.MonMagic2SinPackRt0.Run(input, outputDir));
                return;
            }
            if (args.Length == 3 && args[0] == "--monmagic2-bikanel-sin-pack-rt0")
            {
                Environment.Exit(Tools.MonMagic2BikanelSinPackRt0.Run(args[1], args[2]));
                return;
            }
            if (args.Length == 3 && args[0] == "--monmagic2-calm-cavern-sin-pack-rt0")
            {
                Environment.Exit(Tools.MonMagic2CalmCavernSinPackRt0.Run(args[1], args[2]));
                return;
            }
            if (args.Length == 3 && args[0] == "--monmagic2-gagazet-sin-pack-rt0")
            {
                Environment.Exit(Tools.MonMagic2GagazetSinPackRt0.Run(args[1], args[2]));
                return;
            }
            if (args.Length == 3 && args[0] == "--legacy-monster-vfx-patch-rt0")
            {
                Environment.Exit(Tools.LegacyMonsterVfxPatchRt0.Run(args[1], args[2]));
                return;
            }
            if (args.Length == 5 && args[0] == "--calm-cavern-sin-vfx-stage-rt0")
            {
                Environment.Exit(Tools.CalmCavernSinVfxStageRt0.Run(args[1], args[2], args[3], args[4]));
                return;
            }
            if (args.Length == 5 && args[0] == "--calm-cavern-sin-preview-rt0")
            {
                Environment.Exit(Tools.CalmCavernSinPreviewRt0.Run(args[1], args[2], args[3], args[4]));
                return;
            }
            if (args.Length > 0 && args[0] == "--magic-effect-link-rt0")
            {
                Environment.Exit(Tools.MagicEffectLinkRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magic-effect-assign-rt0")
            {
                Environment.Exit(Tools.MagicEffectAssignRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--prism-flare-v2-pack")
            {
                Environment.Exit(Tools.PrismFlareV2PackRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--prism-flare-clone-pack")
            {
                Environment.Exit(Tools.PrismFlareClonePackRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--prism-flare-recolor")
            {
                Environment.Exit(Tools.PrismFlareRecolorRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--thundafira-pack")
            {
                Environment.Exit(Tools.ThundaFiraPackRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--nul-ward-pack")
            {
                Environment.Exit(Tools.NulWardPackRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--nul-ward-static")
            {
                Environment.Exit(Tools.NulWardStaticRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--nul-ward-rt2-verdict")
            {
                Environment.Exit(Tools.NulWardRt2Verdict.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--nul-ward-atel-diff")
            {
                Environment.Exit(Tools.NulWardAtelDiff.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--thundafira-recolor")
            {
                Environment.Exit(Tools.ThundaFiraRecolorRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--flameflan-flood-pack")
            {
                Environment.Exit(Tools.FlameFlanFloodPackRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--flameflan-flood-recolor")
            {
                Environment.Exit(Tools.FlameFlanFloodRecolorRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--thundafira-ppp-probe")
            {
                Environment.Exit(Tools.ThundaFiraPppProbeRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--thundafira-ppp-color-test")
            {
                Environment.Exit(Tools.ThundaFiraPppColorTestRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--thundafira-kethres-parse")
            {
                Environment.Exit(Tools.ThundaFiraKeThResParseRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--thundafira-kethres-dump")
            {
                Environment.Exit(Tools.ThundaFiraKeThResDumpRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--thundafira-kethres-patch")
            {
                Environment.Exit(Tools.ThundaFiraKeThResPatchRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--thundafira-kethres-reloc")
            {
                Environment.Exit(Tools.ThundaFiraKeThResRelocRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--thundafira-ppp-runtime-color")
            {
                Environment.Exit(Tools.ThundaFiraPppRuntimeColorPatchRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-lab-rt0")
            {
                Environment.Exit(Tools.MagicDllLabRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-semantics-rt0")
            {
                Environment.Exit(Tools.MagicDllSemanticRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-family-rt2")
            {
                Environment.Exit(Tools.MagicDllFamilyRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-logical-decompile-wave1")
            {
                Environment.Exit(Tools.MagicDllLogicalDecompileBatchRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-logical-decompile-wave2")
            {
                Environment.Exit(Tools.MagicDllLogicalDecompileWave2Rt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-rt2-visual-prep")
            {
                Environment.Exit(Tools.MagicDllRt2VisualPrepRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-classify-wave3")
            {
                Environment.Exit(Tools.MagicDllClassifyWave3Rt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-deep-corpus-wave4")
            {
                Environment.Exit(Tools.MagicDllDeepCorpusWave4Rt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-orphan-catalog")
            {
                Environment.Exit(Tools.MagicDllOrphanCatalogRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-kethres-corpus-wave5")
            {
                Environment.Exit(Tools.MagicDllKeThResCorpusWave5Rt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-inferno-offline-wave5")
            {
                Environment.Exit(Tools.MagicDllInfernoOfflineWave5MasterRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-offline-runtime-diff-wave5c")
            {
                Environment.Exit(Tools.MagicDllOfflineRuntimeDiffWave5cRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--magicdll-sound-corpus-wave6")
            {
                Environment.Exit(Tools.MagicDllSoundCorpusWave6Rt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--command-sound-rt0")
            {
                Environment.Exit(Tools.CommandSoundRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--command-sound-pack")
            {
                Environment.Exit(Tools.CommandSoundPackRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--fev9999-corpus-wave7")
            {
                Environment.Exit(Tools.Fev9999CorpusWave7Rt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--command-sound-custom-wizard")
            {
                Environment.Exit(Tools.CommandSoundCustomAudioWizard.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--audio-tools-health")
            {
                Environment.Exit(Tools.AudioToolsHealthRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--fsb9999-lab")
            {
                Environment.Exit(Tools.Fsb9999LabRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--fev9999-seid-map-wave8")
            {
                Environment.Exit(Tools.Fev9999SeidMapWave8Rt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--command-sound-custom-pack")
            {
                Environment.Exit(Tools.CommandSoundCustomPackRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--fev9999-sequence-clone-wave9")
            {
                Environment.Exit(Tools.Fev9999SequenceCloneWave9Rt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--fsb9999-append-lab")
            {
                Environment.Exit(Tools.Fsb9999AppendLabRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--command-sound-new-seid-pack")
            {
                Environment.Exit(Tools.CommandSoundNewSeIdPackRt2.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--vbfextract-rt0")
            {
                Environment.Exit(Tools.VbfExtractRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--phyre-package-io-rt0")
            {
                Environment.Exit(Tools.PhyrePackageIoRt0.Run(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--ctbbase-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\ctb_base.bin");
                Environment.Exit(Tools.CtbBaseRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--mixtable-rt0")
            {
                string path = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\kernel\prepare.bin");
                Environment.Exit(Tools.MixTableRt0.Run(path));
                return;
            }
            if (args.Length > 0 && args[0] == "--aiasm-rt0")
            {
                string root = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\battle\mon");
                Environment.Exit(Tools.AiAssemblerRt0.Run(root));
                return;
            }
            if (args.Length > 0 && args[0] == "--event-rt0")
            {
                string root = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\event\obj");
                Environment.Exit(Tools.EventRt0.Run(root));
                return;
            }
            if (args.Length > 0 && args[0] == "--eventscript-rt0")
            {
                string root = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\event\obj");
                Environment.Exit(Tools.EventScriptRt0.Run(root));
                return;
            }
            if (args.Length > 0 && args[0] == "--spheregrid-layout-rt0")
            {
                string dir = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\menu\abmap");
                Environment.Exit(Tools.SphereGridLayoutRt0.Run(dir));
                return;
            }
            if (args.Length > 0 && args[0] == "--spheregrid-build-rt0")
            {
                Environment.Exit(Tools.SphereGridLayoutBuildRt0.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--spheregrid-edit-rt0")
            {
                string dir = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\menu\abmap");
                Environment.Exit(Tools.SphereGridLayoutEditRt0.Run(dir));
                return;
            }
            if (args.Length > 0 && args[0] == "--jptext-rt0")
            {
                string root = args.Length > 1 ? args[1]
                    : Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"fx_ps2\ffx\master\jppc\event\obj");
                Environment.Exit(Tools.JpTextRt0.Run(root));
                return;
            }
            if (args.Length > 0 && args[0] == "--monster-od-assign" && args.Length > 1)
            {
                Environment.Exit(Tools.MonsterOdAssignRt0.Run(args[1]));
                return;
            }
            if (args.Length > 0 && args[0] == "--name-catalog-extract")
            {
                Environment.Exit(Tools.NameCatalogExtractorRt0.Run(args));
                return;
            }
#endif
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        // The csproj defines FFX_WIN32_BACKEND whenever the build targets a win-* RID or runs
        // on a Windows host without a RID — those builds reference Avalonia.Win32 only, so the
        // unused X11/FreeDesktop/D-Bus chain (and its vulnerable Tmds.DBus.Protocol) stays out
        // of the win-x64 ZIP. Everywhere else we reference Avalonia.Desktop + UsePlatformDetect:
        // the checkpoint d81ab20c hardcoded Win32 and every Linux run died at startup on
        // kernel32 DllNotFound (crash.log 2026-09-09). PlatformDetect picks Win32 on Windows
        // anyway, so behavior there is identical either way.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                // Select the host backend without reintroducing Desktop auto-detection.
                .UseSupportedDesktop()
                .UseSkia()
                .WithInterFont()
                .LogToTrace();
    }
}
