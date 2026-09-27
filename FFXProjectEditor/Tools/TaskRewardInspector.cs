using FFXProjectEditor.FfxLib.Save;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// FROZEN (v2.152.1.0): CLI-only research atlas. UI removed from the editor — not a product surface.
    /// No writer. Use docs/reverse/FFX_TASK_REWARD_* for RE; use Kernel Commands / Event patch / Blitzball / Mon Editor to author.
    /// </summary>
    internal static class TaskRewardInspector
    {
        internal const string FrozenBanner =
            "FROZEN: Task Reward Inspector is CLI-only research (UI removed v2.152.1.0). Not an authoring tool.";
        const int CharacterAbilityBase = 22090;
        const int CharacterOverdriveBase = 15788;
        const int CharacterOverdriveStride = 148;
        const int OverdriveModeHistoryBase = 22124;
        const int OverdriveModeFlagsBase = 22164;
        const int OverdriveModeFlagsLength = 3;

        public static IReadOnlyList<TaskRewardRow> VanillaTaskRows => VanillaTasks;
        public static IReadOnlyList<SaveRegionRow> SaveRegionRows => SaveRegions;
        public static IReadOnlyList<AuthoringPathRow> AuthoringPathRows => AuthoringPaths;
        public static IReadOnlyList<KnownRewardFlagRow> KnownRewardFlagRows => KnownRewardFlags;

        static readonly Regex FurySuffix = new(" Fury$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        static readonly Regex ReelsSuffix = new(" Reels$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        static readonly TaskRewardRow[] VanillaTasks =
        [
            new("Tidus", "Use Overdrives repeatedly", "Slice & Dice / Energy Rain / Blitz Ace",
                "Counter-driven vanilla task; exact thresholds are known from gameplay, but per-reward save bits still need labelled before/after diffs.",
                "Save region 15788..15852 via FfxSaveBatchActions.GetAllOverdrives; command.bin rows expose OD-Choice."),
            new("Auron", "Collect Jecht/Braska/Auron spheres around Spira", "Shooting Star / Banishing Blade / Tornado",
                "Event/key-item driven vanilla task; repo has key-item/flag infrastructure but not named Bushido sphere flags yet.",
                "Save key-item/flag range plus overdrive region; needs event ATEL crosslink."),
            new("Wakka", "Win Blitzball league/tournament prizes", "Attack Reels / Status Reels / Aurochs Reels",
                "Blitzball prize indices 187..189 resolve as overdrive metadata; specific active prize is runtime/save data.",
                "BlitzballData prize u16s in save; Atlas guardrail keeps this read-only until RT2."),
            new("Kimahri", "Use Lancet on enemies that carry Ronso Rage skills", "Ronso Rage choices",
                "Battle-action driven learn task. command.bin/Lancet/Ronso rows are catalogued; exact learned-bit map still needs per-skill save diff.",
                "Ronso Mana docs and --kimahri-ronso-parse cover command metadata; save overdrive bits are still coarse."),
            new("Lulu", "Progress through Black Magic availability for Fury choices", "Fury spell choices",
                "Mostly command/kernel availability, not a separate collectible task. Fury command rows are visible in command.bin.",
                "Use command.bin OD-User/OD-Choice plus ability availability bits."),
            new("Rikku", "Story unlock plus item inventory for Mix", "Mix",
                "Mix is a command plus item-combination table, not a per-task learned list. prepare.bin mix matrix is already indexed.",
                "SpiraDataAtlas mix rows; save task authoring should target items/flags, not Mix rows."),
            new("Yuna", "Story/aeon progression and aeon Overdrive state", "Grand Summon / aeon Overdrives",
                "Yuna's OD surface is tied to aeons and story progression; not yet mapped as custom task gates.",
                "Player/aeon save stats and command metadata exist; task flags need diff/ATEL pass."),
            new("All characters", "Combat behavior unlocks Overdrive modes", "OD modes such as Warrior, Slayer, Victor, etc.",
                "Mode unlocks are counter/condition gates, not new abilities. The editor can bulk enable/clear mode flags.",
                "Save OD mode history 22124..24680 and flags 22164..24680 are known coarse regions.")
        ];

        static readonly SaveRegionRow[] SaveRegions =
        [
            new("Character command availability", "22090..24606", "12 bytes per character in FFXED layout", "Includes learned abilities/OD-like command bits; remove-all-overdrives clears bits 9..95 here."),
            new("Character OD reward block", "15788..15852", "coarse import/all-OD region", "Used by GetAllOverdrives and ImportOverdrives; individual reward labels still incomplete."),
            new("OD mode counters/history", "22124..24640", "34 bytes per character", "ClearOverdriveModes zeroes this area."),
            new("OD mode flags", "22164..24682", "3 bytes per character", "EnableOverdriveModeFlags writes FF FF + bit0."),
            new("Blitzball region", "3252..7211", "bulk Blitzball save region", "Contains prize, player, tech and tournament state."),
            new("Blitzball active prizes", "SaveData+0x19FC/0x1A02/0x1A08/0x1A0A", "u16 prize slots", "Indices 187..189 are overdrive prize metadata."),
            new("Key item / minigame flags", "700..3300", "registry-backed save fields", "Candidate home for sphere collection/event task flags."),
            new("Sphere Grid activation", "8748..12601", "node content + activation bits", "Best path for learnable custom commands when reward is a command.")
        ];

        static readonly KnownRewardFlagRow[] KnownRewardFlags = BuildKnownRewardFlags();

        static readonly AuthoringPathRow[] AuthoringPaths =
        [
            new("save-side inspector/writer", "Safest first writer path after labelled diffs.", "External editor grants rewards once task conditions are proven in save data."),
            new("Sphere Grid teach", "Best for learnable commands.", "Use existing panel/command infrastructure when the reward is a command learned from a node."),
            new("event/ATEL patch", "Most vanilla-like but higher risk.", "Patch event scripts that already award key items, prizes or commands."),
            new("runtime hook + sidecar", "Most flexible for new dynamic tasks.", "Use only for tasks that need live battle/field observation and cannot be represented in save/event data.")
        ];

        public static int Run(string[] args)
        {
            string? savePath = null;
            string? projectPath = null;
            bool json = false;
            bool gameFiles = false;
            bool allTreasureGrants = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--save" && i + 1 < args.Length)
                    savePath = args[++i];
                else if (args[i] == "--project" && i + 1 < args.Length)
                    projectPath = args[++i];
                else if (args[i] == "--game-files")
                    gameFiles = true;
                else if (args[i] == "--all-treasure-grants")
                    allTreasureGrants = true;
                else if (args[i] == "--json")
                    json = true;
                else if (args[i] is "-h" or "--help")
                {
                    PrintUsage();
                    return 0;
                }
                else
                {
                    Console.WriteLine($"ERROR: unknown argument '{args[i]}'");
                    PrintUsage();
                    return 2;
                }
            }

            try
            {
                SaveSnapshot? save = savePath == null ? null : LoadSaveSnapshot(savePath);
                GameFileSnapshot? game = gameFiles
                    ? TaskRewardGameFileLoader.Load(new GameFileLoadRequest
                    {
                        ProjectPath = projectPath,
                        ScanEvents = true,
                        EventGrantScope = allTreasureGrants
                            ? TreasureGrantScope.AllTreasureGrants
                            : TreasureGrantScope.TaskRelevant,
                    })
                    : null;
                if (json)
                    PrintJson(save, game);
                else
                    PrintText(save, game);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine(FrozenBanner);
            Console.WriteLine("usage: --task-reward-inspect [--project <ffx-project-root>] [--game-files] [--all-treasure-grants] [--save <raw-25848-save>] [--json]");
        }

        public static IReadOnlyList<KnownRewardFlagRow> BuildKnownRewardFlagSnapshot(byte[]? data)
        {
            if (data == null || data.Length != FfxSaveCore.DataSize)
                return KnownRewardFlags;

            var core = new FfxSaveCore(data);
            List<KnownRewardFlagRow> rows = new(KnownRewardFlags.Length);
            foreach (KnownRewardFlagRow row in KnownRewardFlags)
            {
                bool? isSet = row.Kind == "bit"
                    ? core.ReadBit(row.Offset, row.Bit)
                    : null;
                rows.Add(row with { IsSet = isSet });
            }
            return rows;
        }

        static KnownRewardFlagRow[] BuildKnownRewardFlags()
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<KnownRewardFlagRow> rows = new();

            foreach (FfxSaveRegistryField field in FfxSaveRegistry.FieldsInRange(15788, 15852))
            {
                if (!string.Equals(field.Kind, "bit", StringComparison.Ordinal))
                    continue;
                if (!seen.Add($"{field.Offset}:{field.Bit}:{field.Label}"))
                    continue;

                rows.Add(new KnownRewardFlagRow(
                    ResolveRewardOwner(field.Label),
                    field.Label,
                    field.Offset,
                    field.Bit,
                    field.Kind,
                    ResolveRewardMechanism(field.Label),
                    null));
            }

            foreach (FfxSaveRegistryField field in FfxSaveRegistry.Root.KeyItemBits)
            {
                if (!field.Label.Contains("Sphere", StringComparison.OrdinalIgnoreCase)
                    && !field.Label.Contains("Sigil", StringComparison.OrdinalIgnoreCase)
                    && !field.Label.Contains("Crest", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!seen.Add($"{field.Offset}:{field.Bit}:{field.Label}"))
                    continue;

                rows.Add(new KnownRewardFlagRow(
                    ResolveKeyItemOwner(field.Label),
                    field.Label,
                    field.Offset,
                    field.Bit,
                    "bit",
                    "key-item/event",
                    null));
            }

            return rows
                .OrderBy(r => r.Offset)
                .ThenBy(r => r.Bit)
                .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static string ResolveRewardOwner(string label)
        {
            if (label is "Spiral Cut" or "Slice & Dice" or "Energy Rain" or "Blitz Ace")
                return "Tidus";
            if (label is "Shooting Star" or "Banishing Blade" or "Tornado" or "Bushido")
                return "Auron";
            if (ReelsSuffix.IsMatch(label))
                return "Wakka";
            if (label is "Ronso Rage" or "Jump" or "Dragon Fang")
                return "Kimahri";
            if (label is "Fury" || FurySuffix.IsMatch(label))
                return "Lulu";
            if (label is "Mix" or "Bribe" or "Steal" or "Use")
                return "Rikku";
            if (label is "Grand Summon")
                return "Yuna";
            return "Party";
        }

        static string ResolveKeyItemOwner(string label) =>
            label.Contains("Jecht", StringComparison.OrdinalIgnoreCase) ? "Auron"
            : label.Contains("Braska", StringComparison.OrdinalIgnoreCase) ? "Auron"
            : label.Contains("Sun", StringComparison.OrdinalIgnoreCase) ? "Wakka"
            : label.Contains("Moon", StringComparison.OrdinalIgnoreCase) ? "Wakka"
            : label.Contains("Mars", StringComparison.OrdinalIgnoreCase) ? "Auron"
            : label.Contains("Saturn", StringComparison.OrdinalIgnoreCase) ? "Kimahri"
            : label.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) ? "Lulu"
            : label.Contains("Venus", StringComparison.OrdinalIgnoreCase) ? "Rikku"
            : label.Contains("Mercury", StringComparison.OrdinalIgnoreCase) ? "Yuna"
            : "Key item";

        static string ResolveRewardMechanism(string label) =>
            ReelsSuffix.IsMatch(label) ? "blitzball"
            : label is "Ronso Rage" or "Jump" or "Dragon Fang" ? "lancet/battle"
            : FurySuffix.IsMatch(label) || label == "Fury" ? "progression"
            : label is "Grand Summon" ? "story/aeon"
            : "overdrive-bit";

        public static SaveSnapshot LoadSaveSnapshot(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length != FfxSaveCore.DataSize)
                throw new InvalidOperationException($"expected raw {FfxSaveCore.DataSize}-byte save, got {data.Length} bytes.");

            var core = new FfxSaveCore(data);
            List<CharacterSnapshot> chars = new();
            for (int i = 0; i < FfxSaveCharacterOffsets.CharacterNames.Length; i++)
            {
                string name = FfxSaveCharacterOffsets.CharacterNames[i];
                int overdriveUnlockedBits = CountBits(data, CharacterOverdriveBase + i * CharacterOverdriveStride, 13);
                int abilityBits = CountBits(data, CharacterAbilityBase + i * FfxSaveCharacterOffsets.CharacterStride, 12);
                int modeKnownBits = CountBits(data, OverdriveModeFlagsBase + i * FfxSaveCharacterOffsets.CharacterStride, OverdriveModeFlagsLength);

                chars.Add(new CharacterSnapshot(
                    name,
                    core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.OverdriveMode, i), 1),
                    core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.OverdriveGauge, i), 1),
                    core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.EnemiesDefeated, i), 4),
                    overdriveUnlockedBits,
                    abilityBits,
                    modeKnownBits));
            }

            return new SaveSnapshot(path, chars, ReadBlitzPrizeValues(data), BuildKnownRewardFlagSnapshot(data), core.ReadInt32Le(15852, 4));
        }

        static IReadOnlyList<int> ReadBlitzPrizeValues(byte[] data)
        {
            int[] offsets = [0x19FC, 0x19FE, 0x1A00, 0x1A02, 0x1A04, 0x1A06, 0x1A08, 0x1A0A];
            List<int> values = new(offsets.Length);
            foreach (int offset in offsets)
            {
                if (offset + 1 >= data.Length)
                    continue;
                values.Add(data[offset] | (data[offset + 1] << 8));
            }
            return values;
        }

        static int CountBits(byte[] data, int offset, int length)
        {
            if (offset < 0 || offset >= data.Length)
                return 0;

            int count = 0;
            int end = Math.Min(data.Length, offset + length);
            for (int i = offset; i < end; i++)
            {
                byte b = data[i];
                for (int bit = 0; bit < 8; bit++)
                    if (((b >> bit) & 1) != 0)
                        count++;
            }
            return count;
        }

        static void PrintText(SaveSnapshot? save, GameFileSnapshot? game)
        {
            Console.WriteLine("=== Task Reward Inspector (FROZEN · CLI-only · read-only) ===");
            Console.WriteLine(FrozenBanner);
            Console.WriteLine("Scope: vanilla task gates, game files (optional), save regions, authoring paths.");
            Console.WriteLine("Writer policy: no writes; UI removed from editor — do not treat as a movable/writable module.");
            Console.WriteLine();

            if (game != null)
                PrintGameFileText(game);

            Console.WriteLine("-- vanilla task inventory --");
            foreach (TaskRewardRow row in VanillaTasks)
            {
                Console.WriteLine($"[{row.Owner}] {row.Task}");
                Console.WriteLine($"  reward : {row.Reward}");
                Console.WriteLine($"  status : {row.Status}");
                Console.WriteLine($"  source : {row.Source}");
            }
            Console.WriteLine();

            Console.WriteLine("-- save / data map --");
            foreach (SaveRegionRow row in SaveRegions)
                Console.WriteLine($"{row.Name,-32} {row.Range,-30} {row.Shape} | {row.Notes}");
            Console.WriteLine();

            Console.WriteLine("-- known reward flags (ffxed_registry, no save required) --");
            foreach (KnownRewardFlagRow row in KnownRewardFlags)
            {
                string live = save == null ? "n/a" : row.IsSet == true ? "SET" : row.IsSet == false ? "clear" : "?";
                Console.WriteLine($"{row.Owner,-10} {row.Label,-24} @{row.Offset}+bit{row.Bit,-2} {row.Mechanism,-14} live={live}");
            }
            Console.WriteLine();

            Console.WriteLine("-- event/script map --");
            Console.WriteLine("Auron sphere tasks: key-item/event flags must be diffed and then crosslinked to ATEL event scripts.");
            Console.WriteLine("Wakka Blitzball tasks: prize indices are visible in save, but event-specific prize assignment remains runtime/blocked.");
            Console.WriteLine("Kimahri Lancet tasks: command metadata is known; learned Ronso Rage bits need per-enemy before/after save diffs.");
            Console.WriteLine();

            Console.WriteLine("-- authoring paths --");
            foreach (AuthoringPathRow row in AuthoringPaths)
                Console.WriteLine($"{row.Name,-24} {row.Recommendation} {row.Why}");

            if (save != null)
            {
                Console.WriteLine();
                PrintSaveText(save);
            }
        }

        static void PrintSaveText(SaveSnapshot save)
        {
            Console.WriteLine("-- save snapshot --");
            Console.WriteLine($"file: {save.Path}");
            foreach (CharacterSnapshot c in save.Characters)
            {
                Console.WriteLine(
                    $"{c.Name,-10} OD mode={c.OverdriveMode,3} gauge={c.OverdriveGauge,3} kills={c.EnemiesDefeated,5} " +
                    $"OD-region-bits={c.OverdriveUnlockedBits,3} ability-bits={c.AbilityBits,3} mode-flag-bits={c.ModeKnownBits,2}");
            }

            string prizes = string.Join(", ", save.BlitzPrizeValues.Select(v => $"{v}({ResolvePrizeFamily(v)})"));
            Console.WriteLine($"Blitzball prize u16s @0x19FC..0x1A0A: {prizes}");
            Console.WriteLine($"Tidus Overdrive Count @15852: {save.TidusOverdriveUseCount} (battle-counter gate, not .ebp)");
        }

        public static string ResolvePrizeFamily(int prize)
        {
            if (prize == 0) return "empty";
            if (prize is >= 1 and <= 100) return "treasure";
            if (prize is >= 101 and <= 160) return "tech";
            if (prize is >= 187 and <= 189) return "overdrive";
            return "unknown";
        }

        static void PrintGameFileText(GameFileSnapshot game)
        {
            Console.WriteLine("-- game files --");
            Console.WriteLine(game.Summary);
            Console.WriteLine();
            Console.WriteLine("command.bin task rows:");
            foreach (GameFileCommandRow row in game.Commands)
                Console.WriteLine($"  [{row.Owner,-8}] {row.TaskReward,-18} id={row.CommandId,3} {row.CommandName} · {row.Notes}");
            Console.WriteLine();
            Console.WriteLine("takara.bin task rows:");
            foreach (GameFileTakaraRow row in game.TakaraRows)
                Console.WriteLine($"  #{row.TakaraIndex,3} {row.RewardLabel,-24} {row.Mechanism}");
            Console.WriteLine();
            Console.WriteLine($"ATEL event grants ({game.EventGrants.Count} displayed, {game.RawObtainTreasureCalls} raw obtainTreasure):");
            foreach (GameFileEventGrantRow row in game.EventGrants.Take(40))
                Console.WriteLine($"  [{row.Owner,-8}] {row.DecodedReward,-22} {row.EventId}.ebp native={row.NativeName} takara={row.TakaraIndex} @0x{row.ScriptOffset:X}");
            if (game.EventGrants.Count > 40)
                Console.WriteLine($"  ... +{game.EventGrants.Count - 40} more");
            Console.WriteLine();
            Console.WriteLine($"Kimahri Ronso sources ({game.RonsoSources.Count} mon.bin hits):");
            foreach (GameFileRonsoSourceRow row in game.RonsoSources.Take(20))
                Console.WriteLine($"  {row.MonsterLabel} → cmd {row.CommandId} {row.CommandName} ({row.CommandHex})");
            if (game.RonsoSources.Count > 20)
                Console.WriteLine($"  ... +{game.RonsoSources.Count - 20} more");
            Console.WriteLine();
            foreach (GameFileBattleGateRow row in game.BattleGates)
                Console.WriteLine($"Battle gate [{row.Owner}] {row.RewardLabel}: {row.GateKind} · {row.SaveLayout}");
            Console.WriteLine();
        }

        static void PrintJson(SaveSnapshot? save, GameFileSnapshot? game)
        {
            Console.WriteLine("{");
            Console.WriteLine("  \"tool\": \"task-reward-inspect\",");
            Console.WriteLine("  \"writerPolicy\": \"read-only\",");
            Console.WriteLine("  \"vanillaTasks\": [");
            for (int i = 0; i < VanillaTasks.Length; i++)
            {
                TaskRewardRow r = VanillaTasks[i];
                Console.WriteLine("    {");
                Console.WriteLine($"      \"owner\": \"{Esc(r.Owner)}\",");
                Console.WriteLine($"      \"task\": \"{Esc(r.Task)}\",");
                Console.WriteLine($"      \"reward\": \"{Esc(r.Reward)}\",");
                Console.WriteLine($"      \"status\": \"{Esc(r.Status)}\",");
                Console.WriteLine($"      \"source\": \"{Esc(r.Source)}\"");
                Console.WriteLine(i + 1 == VanillaTasks.Length ? "    }" : "    },");
            }
            Console.WriteLine("  ],");
            Console.WriteLine("  \"saveRegions\": [");
            for (int i = 0; i < SaveRegions.Length; i++)
            {
                SaveRegionRow r = SaveRegions[i];
                Console.WriteLine("    {");
                Console.WriteLine($"      \"name\": \"{Esc(r.Name)}\",");
                Console.WriteLine($"      \"range\": \"{Esc(r.Range)}\",");
                Console.WriteLine($"      \"shape\": \"{Esc(r.Shape)}\",");
                Console.WriteLine($"      \"notes\": \"{Esc(r.Notes)}\"");
                Console.WriteLine(i + 1 == SaveRegions.Length ? "    }" : "    },");
            }
            Console.WriteLine("  ],");
            Console.WriteLine("  \"authoringPaths\": [");
            for (int i = 0; i < AuthoringPaths.Length; i++)
            {
                AuthoringPathRow r = AuthoringPaths[i];
                Console.WriteLine("    {");
                Console.WriteLine($"      \"name\": \"{Esc(r.Name)}\",");
                Console.WriteLine($"      \"recommendation\": \"{Esc(r.Recommendation)}\",");
                Console.WriteLine($"      \"why\": \"{Esc(r.Why)}\"");
                Console.WriteLine(i + 1 == AuthoringPaths.Length ? "    }" : "    },");
            }
            Console.WriteLine("  ],");
            if (save == null)
            {
                Console.WriteLine("  \"saveSnapshot\": null");
            }
            else
            {
                Console.WriteLine("  \"saveSnapshot\": {");
                Console.WriteLine($"    \"path\": \"{Esc(save.Path)}\",");
                Console.WriteLine("    \"characters\": [");
                for (int i = 0; i < save.Characters.Count; i++)
                {
                    CharacterSnapshot c = save.Characters[i];
                    Console.WriteLine("      {");
                    Console.WriteLine($"        \"name\": \"{Esc(c.Name)}\",");
                    Console.WriteLine($"        \"overdriveMode\": {c.OverdriveMode},");
                    Console.WriteLine($"        \"overdriveGauge\": {c.OverdriveGauge},");
                    Console.WriteLine($"        \"enemiesDefeated\": {c.EnemiesDefeated},");
                    Console.WriteLine($"        \"overdriveRegionBits\": {c.OverdriveUnlockedBits},");
                    Console.WriteLine($"        \"abilityBits\": {c.AbilityBits},");
                    Console.WriteLine($"        \"modeFlagBits\": {c.ModeKnownBits}");
                    Console.WriteLine(i + 1 == save.Characters.Count ? "      }" : "      },");
                }
                Console.WriteLine("    ],");
                Console.WriteLine($"    \"blitzPrizeValues\": [{string.Join(", ", save.BlitzPrizeValues)}]");
                Console.WriteLine("  }");
            }
            Console.WriteLine("}");
        }

        static string Esc(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        public sealed record TaskRewardRow(string Owner, string Task, string Reward, string Status, string Source);
        public sealed record SaveRegionRow(string Name, string Range, string Shape, string Notes);
        public sealed record AuthoringPathRow(string Name, string Recommendation, string Why);
        public sealed record KnownRewardFlagRow(
            string Owner,
            string Label,
            int Offset,
            int Bit,
            string Kind,
            string Mechanism,
            bool? IsSet)
        {
            public string Address => $"@{Offset}+bit{Bit}";
            public string LiveState => IsSet switch
            {
                true => "SET",
                false => "clear",
                _ => "—",
            };
        }

        public sealed record SaveSnapshot(
            string Path,
            IReadOnlyList<CharacterSnapshot> Characters,
            IReadOnlyList<int> BlitzPrizeValues,
            IReadOnlyList<KnownRewardFlagRow> RewardFlags,
            int TidusOverdriveUseCount);
        public sealed record CharacterSnapshot(
            string Name,
            int OverdriveMode,
            int OverdriveGauge,
            int EnemiesDefeated,
            int OverdriveUnlockedBits,
            int AbilityBits,
            int ModeKnownBits);
    }
}
