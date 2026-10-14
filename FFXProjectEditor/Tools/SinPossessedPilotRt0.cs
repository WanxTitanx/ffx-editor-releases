using System;
using System.Globalization;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.Tools
{
    // --sin-possessed-pilot --monster m019 [--preset UNI-004] [--dry-run]
    // Offline RT2-prep: append Possessed opener + optional SIN preset to a mod-folder monster bin.
    internal static class SinPossessedPilotRt0
    {
        public static int Run(string[] args)
        {
            string monsterId = "m019";
            string? presetId = null;
            string? modRoot = null;
            string? sourceBin = null;
            int? hookEntrypoint = 0;
            bool dryRun = false;
            bool forcePerform = false;
            bool clearForcedAction = true;
            bool battleStart = false;
            bool sourceBackup = false;
            bool allowDirtySource = false;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--monster" when i + 1 < args.Length:
                        monsterId = args[++i].Trim().ToLowerInvariant();
                        break;
                    case "--preset" when i + 1 < args.Length:
                        presetId = args[++i].Trim();
                        break;
                    case "--mod-mon-root" when i + 1 < args.Length:
                        modRoot = args[++i];
                        break;
                    case "--source-bin" when i + 1 < args.Length:
                        sourceBin = args[++i];
                        break;
                    case "--hook-entrypoint" when i + 1 < args.Length:
                        hookEntrypoint = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--force-perform":
                        forcePerform = true;
                        break;
                    case "--no-force-perform":
                        forcePerform = false;
                        break;
                    case "--clear-forced-action":
                        clearForcedAction = true;
                        break;
                    case "--keep-forced-action":
                        clearForcedAction = false;
                        break;
                    case "--battle-start":
                        battleStart = true;
                        hookEntrypoint = 0;
                        break;
                    case "--on-turn":
                        hookEntrypoint = null;
                        break;
                    case "--source-backup":
                        sourceBackup = true;
                        break;
                    case "--allow-dirty-source":
                        allowDirtySource = true;
                        break;
                }
            }

            string id = monsterId.TrimStart('m');
            modRoot ??= Path.Combine(
                SinCurseSpreadBaker.DefaultModMonRoot(),
                $"_m{id}");
            string binPath = Path.Combine(modRoot, $"m{id}.bin");

            if (!File.Exists(binPath))
            {
                Console.Error.WriteLine($"missing {binPath} — copy vanilla m{id}.bin into mod folder first.");
                return 1;
            }

            string backup = binPath + SinCurseSpreadBaker.BackupSuffix;
            string sourcePath = sourceBin ?? (sourceBackup && File.Exists(backup) ? backup : binPath);
            if (sourceBin is not null && !File.Exists(sourceBin))
            {
                Console.Error.WriteLine($"source bin not found: {sourceBin}");
                return 1;
            }
            if (sourceBackup && !File.Exists(backup))
            {
                Console.Error.WriteLine($"backup not found: {backup}");
                return 1;
            }

            byte[] source = File.ReadAllBytes(sourcePath);
            Monster_File before = Monster_File.Read(source);
            bool sourceAlreadyPossessedAbility = before.StatSheetFile.Abilities.Any(a => a is >= 0x60E7 and <= 0x60EE);
            AiScriptFile sourceScript = AiScript_File.Read(before.AiFile);
            bool sourceAlreadyPossessedScript = AiAutomation.DetectActions(sourceScript)
                .Any(a => a.Kind == AiActionKind.Command && a.CommandOperand is >= 0x60E7 and <= 0x60EE);
            if ((sourceAlreadyPossessedAbility || sourceAlreadyPossessedScript) && !allowDirtySource)
            {
                Console.Error.WriteLine($"source already contains Possessed data: {sourcePath}");
                Console.Error.WriteLine("Use a clean vanilla source, or pass --allow-dirty-source only for debugging a contaminated file.");
                return 1;
            }
            if (clearForcedAction)
            {
                before.StatSheetFile.ForcedAction = 0;
                source = before.Write();
            }

            SinPresetRecipeResolver.ResolveResult resolved = presetId is null
                ? new(SinPossessedOpener.BuildOpenerRecipe(forcePerform: forcePerform), "possessed-opener", null)
                : SinPresetRecipeResolver.Resolve(presetId);

            if (!resolved.Ok || resolved.Recipe is null)
            {
                Console.Error.WriteLine(resolved.BlockReason ?? "preset resolve failed");
                return 1;
            }

            SinChainRecipe main = resolved.Recipe;

            int mobNum = int.Parse(id, CultureInfo.InvariantCulture);
            ushort operand = SinPossessedOpener.OperandForMonsterIndex(mobNum);

            SinMonsterEmitResult emit;
            int? hook = battleStart ? 0 : hookEntrypoint;
            if (presetId is null)
            {
                emit = SinPossessedOpener.TryEmitOpenerOnly(source, operand, forcePerform, hook);
            }
            else
            {
                emit = SinPossessedOpener.TryEmitWithPossessedOpener(source, main, operand, forcePerform, hook);
            }
            if (!emit.Ok)
            {
                Console.Error.WriteLine($"emit failed: {emit.Error}");
                return 1;
            }

            Monster_File after = Monster_File.Read(emit.EditedMonster!);
            bool hasAbility = after.StatSheetFile.Abilities.Any(a => a == operand);

            Console.WriteLine($"=== Sin Possessed pilot · {monsterId} ===");
            Console.WriteLine($"operand: 0x{operand:X4} (monmagic2 row {operand & 0x0FFF})");
            Console.WriteLine($"preset: {(presetId ?? "opener-only")}");
            Console.WriteLine($"forcePerform: {forcePerform}");
            Console.WriteLine($"forced action: 0x{after.StatSheetFile.ForcedAction:X4}{(clearForcedAction ? " (cleared)" : "")}");
            Console.WriteLine($"hook: worker {emit.WorkerIndex}, entrypoint {emit.EntrypointIndex} ({emit.WorkerResolution})");
            Console.WriteLine($"source: {Path.GetFileName(sourcePath)}");
            Console.WriteLine($"ability slot added: {hasAbility}");
            Console.WriteLine($"AI growth: +{emit.AddedRows} instructions (approx)");
            Console.WriteLine($"target: {binPath}");

            if (dryRun)
            {
                Console.WriteLine("dry-run — bin not written");
                return 0;
            }

            bool backupCreated = false;
            if (!File.Exists(backup))
            {
                File.Copy(binPath, backup, overwrite: false);
                backupCreated = true;
            }
            File.WriteAllBytes(binPath, emit.EditedMonster!);
            Console.WriteLine($"written · backup={Path.GetFileName(backup)} ({(backupCreated ? "created" : "preserved")})");
            Console.WriteLine("RT2: force battle vs this mob — expect Possessed VFX/message turn 1.");
            return 0;
        }
    }
}
