using System;
using System.Globalization;
using System.IO;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.Tools
{
    // --sin-scale-pilot --monster m019 [--scale 1.8] [--dry-run] [--source-backup]
    // --scale is a multiplier: fresh insert = 1.0×factor; existing scaleOwnSize = current×factor.
    internal static class SinScalePilotRt0
    {
        public static int Run(string[] args)
        {
            string monsterId = "m019";
            string? modRoot = null;
            string? sourceBin = null;
            float scale = SinScaleOpener.DefaultUniformScale;
            bool dryRun = false;
            bool sourceBackup = false;
            bool allowDirtySource = false;
            bool battleStart = false;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--monster" when i + 1 < args.Length:
                        monsterId = args[++i].Trim().ToLowerInvariant();
                        break;
                    case "--mod-mon-root" when i + 1 < args.Length:
                        modRoot = args[++i];
                        break;
                    case "--source-bin" when i + 1 < args.Length:
                        sourceBin = args[++i];
                        break;
                    case "--scale" when i + 1 < args.Length:
                        scale = float.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--source-backup":
                        sourceBackup = true;
                        break;
                    case "--allow-dirty-source":
                        allowDirtySource = true;
                        break;
                    case "--battle-start":
                        battleStart = true;
                        break;
                    case "--inline":
                        battleStart = false;
                        break;
                }
            }

            string id = monsterId.TrimStart('m');
            modRoot ??= Path.Combine(SinCurseSpreadBaker.DefaultModMonRoot(), $"_m{id}");
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
            AiScriptFile sourceScript = AiScript_File.Read(before.AiFile);

            SinMonsterEmitResult emit;
            bool replaceMode = false;
            if (SinScaleOpener.ScriptHasScaleOwnSize(sourceScript))
            {
                emit = SinScaleOpener.TryReplaceUniformScaleOwnSize(source, scale);
                replaceMode = emit.Ok;
                if (!emit.Ok && allowDirtySource)
                {
                    emit = battleStart
                        ? SinScaleOpener.TryEmitBattleStartScale(source, scale)
                        : SinScaleOpener.TryEmitInlineAfterDeathAnimation(source, scale);
                }
                else if (!emit.Ok)
                {
                    Console.Error.WriteLine($"replace failed: {emit.Error}");
                    Console.Error.WriteLine("Script already has scaleOwnSize — replace edits the existing float pool.");
                    return 1;
                }
            }
            else
            {
                emit = battleStart
                    ? SinScaleOpener.TryEmitBattleStartScale(source, scale)
                    : SinScaleOpener.TryEmitInlineAfterDeathAnimation(source, scale);
            }

            if (!emit.Ok)
            {
                Console.Error.WriteLine($"emit failed: {emit.Error}");
                return 1;
            }

            Console.WriteLine($"=== Sin scale pilot · {monsterId} ===");
            Console.WriteLine($"scale factor: {scale.ToString(CultureInfo.InvariantCulture)} (× on existing pool; fresh insert = 1.0 × factor)");
            Console.WriteLine($"mode: {(replaceMode ? "multiply existing uniform scaleOwnSize (float pool)" : battleStart ? "battle-start prepend (entrypoint 0)" : "inline after DeathAnimation (flan corpus)")}");
            Console.WriteLine($"hook: worker {emit.WorkerIndex}, entrypoint {emit.EntrypointIndex} ({emit.WorkerResolution})");
            Console.WriteLine($"source: {Path.GetFileName(sourcePath)}");
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
            Console.WriteLine("RT2: force battle vs this mob — expect visibly larger/smaller model at spawn.");
            return 0;
        }
    }
}
