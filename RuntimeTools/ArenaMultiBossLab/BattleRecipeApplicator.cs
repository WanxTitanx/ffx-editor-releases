using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.BattleMap;

namespace ArenaMultiBossLab;

internal sealed class BattleRecipeApplyResult
{
    public int ExitCode { get; init; }
    public byte[]? OutputBytes { get; init; }
    public string? DeployPath { get; init; }
}

internal static class BattleRecipeApplicator
{
    public static BattleRecipeApplyResult Apply(
        Recipe recipe,
        string sourcePath,
        string? vanillaRoot,
        string? modRoot,
        bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(recipe.SourceBattleId))
        {
            Console.Error.WriteLine("recipe needs source_battle_id (or legacy alias_battle_id).");
            return Fail(2);
        }

        Console.WriteLine($"recipe       : {recipe.Id}  ({recipe.Tier}, {recipe.Bosses.Count} bosses)");
        Console.WriteLine($"source       : {recipe.SourceBattleId}");
        Console.WriteLine($"output       : {recipe.OutputBattleId}");
        Console.WriteLine($"token F7     : {recipe.TokenF7}");
        Console.WriteLine($"base template: {recipe.BaseTemplate}");
        Console.WriteLine($"chunk3 mode  : {recipe.Chunk3Mode}");

        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"vanilla source not found: {sourcePath}");
            return Fail(2);
        }

        byte[] originalBytes;
        Battle_File originalBattle;
        try
        {
            originalBytes = File.ReadAllBytes(sourcePath);
            originalBattle = Battle_File.Read(recipe.SourceBattleId, originalBytes);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to load vanilla {sourcePath}: {ex.Message}");
            return Fail(2);
        }

        if (!originalBattle.CanWriteFormation)
        {
            Console.Error.WriteLine($"{recipe.SourceBattleId}: no writable formation chunk2 — refusing.");
            return Fail(3);
        }

        if (recipe.Chunk2Slots.Count != 8)
        {
            Console.Error.WriteLine($"recipe.chunk2_slots must have exactly 8 entries (got {recipe.Chunk2Slots.Count}).");
            return Fail(2);
        }

        ushort[] slots = new ushort[8];
        for (int i = 0; i < 8; i++)
            slots[i] = HexUtil.ParseU16(recipe.Chunk2Slots[i]);

        int firstEmpty = -1;
        for (int i = 0; i < 8; i++)
        {
            if (slots[i] == 0xFFFF) { firstEmpty = i; break; }
        }
        if (firstEmpty >= 0)
        {
            for (int j = firstEmpty + 1; j < 8; j++)
            {
                if (slots[j] != 0xFFFF)
                {
                    Console.Error.WriteLine($"chunk2 has gap: slot {firstEmpty} = 0xFFFF but slot {j} = 0x{slots[j]:X4}. Refusing.");
                    return Fail(3);
                }
            }
        }

        byte[] afterChunk2;
        try
        {
            afterChunk2 = originalBattle.WriteWithFormationSlots(slots);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"chunk2 write failed: {ex.Message}");
            return Fail(3);
        }

        int slotsOff = originalBattle.FormationSlotsOffset;
        int slotsLen = Battle_File.FormationSlotsLength;
        if (!FormationSlotWriter.IsSlotOnly(originalBytes, afterChunk2, slotsOff, slotsLen))
        {
            Console.Error.WriteLine("chunk2 transform escaped slot region — refusing.");
            return Fail(3);
        }

        Console.WriteLine($"chunk2 ok    : {recipe.Bosses.Count} actor slots @ 0x{slotsOff:X} ({slotsLen} bytes)");

        byte[] afterChunk3 = afterChunk2;
        var monLiveCoords = recipe.Chunk3MonsterLive
            .Select(p => (X: p[0], Y: p[1], Z: p[2]))
            .ToList();

        if (monLiveCoords.Count < recipe.Bosses.Count)
        {
            Console.Error.WriteLine($"chunk3_monster_live has {monLiveCoords.Count} positions but recipe has {recipe.Bosses.Count} bosses. Refusing.");
            return Fail(3);
        }

        if (string.Equals(recipe.Chunk3Mode, "preserve", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("chunk3 ok    : preserve mode (no edit)");
        }
        else if (string.Equals(recipe.Chunk3Mode, "position-only", StringComparison.OrdinalIgnoreCase))
        {
            var loc = BattleArenaPositionWriter.LocateAnchorArray(afterChunk2, 0, BattleArena_AnchorRole.MonsterLive);
            if (loc == null)
            {
                Console.Error.WriteLine("chunk3 monLive array absent — cannot position-only edit. Try chunk3_mode=grow or pick another base_template.");
                return Fail(3);
            }
            if (monLiveCoords.Count != loc.Value.Count)
            {
                Console.Error.WriteLine(
                    $"position-only mode requires exactly {loc.Value.Count} coords (vanilla monPos), got {monLiveCoords.Count}. " +
                    "Use chunk3_mode=grow to change the count.");
                return Fail(3);
            }
            try
            {
                afterChunk3 = BattleArenaPositionWriter.WriteAnchorPositions(afterChunk2, 0, BattleArena_AnchorRole.MonsterLive, monLiveCoords);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"chunk3 position-only write failed: {ex.Message}");
                return Fail(3);
            }
            if (!BattleArenaPositionWriter.IsPositionOnly(afterChunk2, afterChunk3, loc.Value.Offset, loc.Value.Length))
            {
                Console.Error.WriteLine("chunk3 position-only transform escaped its array — refusing.");
                return Fail(3);
            }
            Console.WriteLine($"chunk3 ok    : {monLiveCoords.Count} monLive positions @ 0x{loc.Value.Offset:X} ({loc.Value.Length} bytes, position-only)");
        }
        else if (string.Equals(recipe.Chunk3Mode, "grow", StringComparison.OrdinalIgnoreCase))
        {
            BattleArenaGrowWriter.GrowPlan plan = BattleArenaGrowWriter.Plan(afterChunk2);
            if (!plan.CanGrow)
            {
                Console.Error.WriteLine($"chunk3 grow refused: {plan.Reason}");
                return Fail(3);
            }
            if (monLiveCoords.Count > plan.MaxCount)
            {
                Console.Error.WriteLine($"chunk3 grow refused: requested {monLiveCoords.Count} > MaxCount {plan.MaxCount} " +
                                        $"(monA cap {plan.MonACapacity}, monB cap {plan.MonBCapacity}).");
                return Fail(3);
            }
            try
            {
                afterChunk3 = BattleArenaGrowWriter.GrowMonsters(afterChunk2, monLiveCoords);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"chunk3 grow failed: {ex.Message}");
                return Fail(3);
            }
            Console.WriteLine($"chunk3 ok    : grew {plan.OldCount} -> {monLiveCoords.Count} monLive (hard cap {BattleArenaGrowWriter.HardActorCap})");
        }
        else
        {
            Console.Error.WriteLine($"unknown chunk3_mode: {recipe.Chunk3Mode}  (expected preserve | position-only | grow)");
            return Fail(2);
        }

        try
        {
            Battle_File reread = Battle_File.Read(recipe.OutputBattleId, afterChunk3);
            var reSlots = reread.Formation?.Slots.Select(s => (ushort)s.RawMonsterId).ToArray() ?? Array.Empty<ushort>();
            for (int i = 0; i < 8; i++)
            {
                ushort got = i < reSlots.Length ? reSlots[i] : (ushort)0xFFFF;
                if (got != slots[i])
                {
                    Console.Error.WriteLine($"re-read mismatch slot {i}: got 0x{got:X4}, expected 0x{slots[i]:X4}");
                    return Fail(3);
                }
            }
            Console.WriteLine("re-read ok   : chunk2 slots match recipe");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"re-read failed: {ex.Message}");
            return Fail(3);
        }

        if (dryRun)
        {
            Console.WriteLine("dry-run      : NOT writing. Done.");
            return new BattleRecipeApplyResult { ExitCode = 0, OutputBytes = afterChunk3 };
        }

        if (string.IsNullOrEmpty(modRoot))
        {
            Console.Error.WriteLine("missing --mod-root <btlRoot> (omit --dry-run only if you want to deploy)");
            return Fail(1);
        }

        string deployDir = Path.Combine(modRoot, recipe.OutputBattleId);
        string deployPath = Path.Combine(deployDir, recipe.OutputBattleId + ".bin");
        string backupPath = deployPath + FormationSlotWriter.DefaultBackupSuffix;

        try
        {
            Directory.CreateDirectory(deployDir);

            if (!File.Exists(backupPath))
            {
                string outputVanilla = !string.IsNullOrEmpty(vanillaRoot)
                    ? Path.Combine(vanillaRoot, recipe.OutputBattleId, recipe.OutputBattleId + ".bin")
                    : "";
                string backupSource = File.Exists(deployPath) ? deployPath
                    : (!string.IsNullOrEmpty(outputVanilla) && File.Exists(outputVanilla) ? outputVanilla : sourcePath);
                File.Copy(backupSource, backupPath, overwrite: false);
                Console.WriteLine($"backup       : {backupPath} (from {Path.GetFileName(backupSource)})");
            }
            else
            {
                Console.WriteLine($"backup       : {backupPath} (already present, kept)");
            }

            File.WriteAllBytes(deployPath, afterChunk3);
            Console.WriteLine($"deployed     : {deployPath} ({afterChunk3.Length} bytes)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"deploy failed: {ex.Message}");
            return Fail(2);
        }

        Console.WriteLine();
        Console.WriteLine("Next: load FFX with EFL active, F7 -> Arena+ -> select the aliased row, run _RT2_CHECKLIST.md.");
        Console.WriteLine($"Rollback: copy {Path.GetFileName(backupPath)} over {Path.GetFileName(deployPath)}.");

        return new BattleRecipeApplyResult
        {
            ExitCode = 0,
            OutputBytes = afterChunk3,
            DeployPath = deployPath,
        };
    }

    private static BattleRecipeApplyResult Fail(int code) => new() { ExitCode = code };
}
