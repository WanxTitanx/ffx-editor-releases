using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Blitzball;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Event;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;
using FFXProjectEditor.FfxLib.Treasure;
using FFXProjectEditor.Services;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// FROZEN (v2.152.1.0): CLI-only helper for <c>--task-reward-inspect --game-files</c>. UI removed from editor.
    /// </summary>
    internal static class TaskRewardGameFileLoader
    {
        const byte OpPushImmediate = 0xAE;
        const byte OpCall = 0xB5;
        const ushort FuncObtainTreasure = 0x015B;
        const ushort FuncObtainTreasureSilent = 0x01A7;
        const ushort FuncHasKeyItem = 0x0160;
        const ushort FuncGrantAbility = 0x01FC;
        const ushort FuncRevokeAbility = 0x01FD;

        static readonly string[] CharacterSlotNames = ["Tidus", "Yuna", "Auron", "Kimahri", "Wakka", "Lulu", "Rikku"];

        static readonly (string Owner, string Reward, int CommandId)[] TaskCommandLinks =
        [
            ("Tidus", "Spiral Cut", 96),
            ("Tidus", "Slice & Dice", 97),
            ("Tidus", "Energy Rain", 98),
            ("Tidus", "Blitz Ace", 99),
            ("Auron", "Shooting Star", 100),
            ("Auron", "Dragon Fang", 101),
            ("Auron", "Banishing Blade", 102),
            ("Auron", "Tornado", 103),
            ("Kimahri", "Lancet", 32),
            ("Kimahri", "Jump", 104),
            ("Kimahri", "Fire Breath", 105),
            ("Kimahri", "Seed Cannon", 106),
            ("Kimahri", "Self-Destruct", 107),
            ("Kimahri", "Thrust Kick", 108),
            ("Kimahri", "Stone Breath", 109),
            ("Kimahri", "Aqua Breath", 110),
            ("Kimahri", "Doom", 111),
            ("Kimahri", "White Wind", 112),
            ("Kimahri", "Bad Breath", 113),
            ("Kimahri", "Mighty Guard", 114),
            ("Kimahri", "Nova", 115),
            ("Wakka", "Element Reels", 116),
            ("Wakka", "Attack Reels", 117),
            ("Wakka", "Status Reels", 118),
            ("Wakka", "Aurochs Reels", 119),
            ("Lulu", "Fury (menu)", 285),
            ("Rikku", "Mix (menu)", 286),
            ("Yuna", "Grand Summon", 280),
        ];

        static readonly int[] RonsoCommandIds = KimahriExtendedCommandWriter.RonsoRageDonorIds;

        public static GameFileSnapshot Load(GameFileLoadRequest request)
        {
            var paths = ResolvePaths(request);
            var notes = new List<string>();
            var commands = new List<GameFileCommandRow>();
            var takaraRows = new List<GameFileTakaraRow>();
            var eventGrants = new List<GameFileEventGrantRow>();
            var blitzRows = new List<GameFileBlitzballRow>();
            var ronsoRows = new List<GameFileRonsoSourceRow>();
            int rawTreasureCalls = 0;

            bool allTreasure = request.EventGrantScope == TreasureGrantScope.AllTreasureGrants;

            if (paths.CommandBin != null && File.Exists(paths.CommandBin))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(paths.CommandBin);
                    List<Ability_Command> list = Ability_Command.ReadList(bytes, hasExtraInfo: true);
                    commands.AddRange(BuildCommandRows(list, paths.CommandBin));
                    notes.Add($"command.bin: {list.Count} rows @ {paths.CommandBin}");
                }
                catch (Exception ex)
                {
                    notes.Add($"command.bin: ERROR {ex.Message}");
                }
            }
            else
            {
                notes.Add("command.bin: not found (load a project or pass --kernel-root)");
            }

            List<Treasure_Entry>? takara = null;
            if (paths.TakaraBin != null && File.Exists(paths.TakaraBin))
            {
                try
                {
                    takara = Treasure_File.ReadAll(File.ReadAllBytes(paths.TakaraBin));
                    takaraRows.AddRange(BuildTaskTakaraRows(takara, allTreasure));
                    notes.Add($"takara.bin: {takara.Count} rows @ {paths.TakaraBin}");
                }
                catch (Exception ex)
                {
                    notes.Add($"takara.bin: ERROR {ex.Message}");
                }
            }
            else
            {
                notes.Add("takara.bin: not found");
            }

            if (paths.ImportantBin != null && File.Exists(paths.ImportantBin))
            {
                try
                {
                    KeyItemTable table = KeyItem_File.Read(File.ReadAllBytes(paths.ImportantBin));
                    notes.Add($"important.bin: {table.Entries.Count} key items @ {paths.ImportantBin}");
                }
                catch (Exception ex)
                {
                    notes.Add($"important.bin: ERROR {ex.Message}");
                }
            }

            blitzRows.AddRange(BuildBlitzballAtlasRows());
            blitzRows.AddRange(BuildBlitzballStructureRows(paths));
            ronsoRows.AddRange(BuildRonsoSourceRows(paths.MonRoot));

            if (request.ScanEvents && paths.EventRoot != null && Directory.Exists(paths.EventRoot))
            {
                int scanned = 0;
                foreach (string ebpPath in Directory.EnumerateFiles(paths.EventRoot, "*.ebp", SearchOption.AllDirectories))
                {
                    scanned++;
                    try
                    {
                        byte[] raw = File.ReadAllBytes(ebpPath);
                        Event_File ev = Event_File.Read(Path.GetFileNameWithoutExtension(ebpPath), raw);
                        if (!ev.Chunks[Event_File.ChunkScript].IsPresent)
                            continue;

                        byte[] script = ev.ScriptChunkBytes.ToArray();
                        (List<GameFileEventGrantRow> hits, int treasureCalls) =
                            ScanEventScript(ev.EventId, script, takara, allTreasure);
                        rawTreasureCalls += treasureCalls;
                        eventGrants.AddRange(hits);
                    }
                    catch
                    {
                        // skip unreadable events; RT0 corpus has a few edge cases
                    }
                }

                string scopeLabel = allTreasure ? "all obtainTreasure/grant hits" : "task-filtered hits";
                notes.Add($"events: scanned {scanned} .ebp, {eventGrants.Count} {scopeLabel} ({rawTreasureCalls} raw obtainTreasure calls)");
            }
            else if (request.ScanEvents)
            {
                notes.Add("events: root not found (load a project with jppc/event/obj)");
            }

            string summary = string.Join(" · ", notes);
            return new GameFileSnapshot(
                summary,
                commands,
                takaraRows,
                eventGrants,
                blitzRows,
                ronsoRows,
                BuildBattleGateRows(),
                rawTreasureCalls,
                eventGrants.Count);
        }

        public static GameFileSnapshot LoadFromProject(bool scanEvents = true, TreasureGrantScope scope = TreasureGrantScope.TaskRelevant)
        {
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                return new GameFileSnapshot(
                    Strings.F2_no_project_loaded_open_an_ffx_project_to_fe2d010b,
                    Array.Empty<GameFileCommandRow>(),
                    Array.Empty<GameFileTakaraRow>(),
                    Array.Empty<GameFileEventGrantRow>(),
                    Array.Empty<GameFileBlitzballRow>(),
                    Array.Empty<GameFileRonsoSourceRow>(),
                    BuildBattleGateRows(),
                    0,
                    0);
            }

            return Load(new GameFileLoadRequest
            {
                ProjectPath = Project_Service.Instance.ProjectPath,
                ScanEvents = scanEvents,
                EventGrantScope = scope,
            });
        }

        static IReadOnlyList<GameFileBattleGateRow> BuildBattleGateRows() =>
        [
            new("Tidus", "Slice & Dice / Energy Rain / Blitz Ace",
                "battle-counter",
                "Save @15852 u32 (Tidus Overdrive Count) · unlock bits @15788:1..3",
                "Runtime: battle EXE increments counter → sub_7B1190 → FFX_GrantCommandToCharacter @0x785D10",
                "No field .ebp · thresholds gameplay/diff pending",
                "0x7B15A0 is OD gauge only, not unlock"),
        ];

        static ResolvedPaths ResolvePaths(GameFileLoadRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                string root = request.ProjectPath!;
                return new ResolvedPaths
                {
                    CommandBin = FirstExisting(
                        Path.Combine(root, "new_uspc", "battle", "kernel", "command.bin"),
                        Path.Combine(root, "jppc", "battle", "kernel", "command.bin")),
                    TakaraBin = Path.Combine(root, "jppc", "battle", "kernel", "takara.bin"),
                    ImportantBin = FirstExisting(
                        Path.Combine(root, "new_uspc", "battle", "kernel", "important.bin"),
                        Path.Combine(root, "jppc", "battle", "kernel", "important.bin")),
                    EventRoot = Path.Combine(root, "jppc", "event", "obj"),
                    Blitz0200 = Path.Combine(root, "jppc", "event", "obj", "bl", "bltz0200", "bltz0200.ebp"),
                    MonRoot = Path.Combine(root, "jppc", "battle", "mon"),
                };
            }

            if (!string.IsNullOrWhiteSpace(request.KernelRoot))
            {
                string k = request.KernelRoot!;
                return new ResolvedPaths
                {
                    CommandBin = Path.Combine(k, "command.bin"),
                    TakaraBin = Path.Combine(k, "takara.bin"),
                    ImportantBin = Path.Combine(k, "important.bin"),
                    EventRoot = request.EventRoot,
                    Blitz0200 = request.Bltz0200Path,
                };
            }

            return new ResolvedPaths
            {
                EventRoot = request.EventRoot,
                Blitz0200 = request.Bltz0200Path,
            };
        }

        static string? FirstExisting(params string[] paths)
        {
            foreach (string p in paths)
            {
                if (File.Exists(p))
                    return p;
            }
            return paths.FirstOrDefault();
        }

        static IEnumerable<GameFileCommandRow> BuildCommandRows(List<Ability_Command> list, string sourcePath)
        {
            foreach ((string owner, string reward, int id) in TaskCommandLinks)
            {
                if (id < 0 || id >= list.Count)
                {
                    yield return new GameFileCommandRow(
                        owner, reward, id, "(missing row)", sourcePath,
                        Character_Enum.None, 0, 0, "command.bin row absent");
                    continue;
                }

                Ability_Command c = list[id];
                string name = CommandCharacter_Dictionary.Instance.TryGetValue((ushort)id, out string? dictName)
                    ? dictName
                    : $"command #{id}";

                yield return new GameFileCommandRow(
                    owner,
                    reward,
                    id,
                    name,
                    sourcePath,
                    c.CharacterUser,
                    c.OverdriveCategory,
                    c.SubMenuCategorization,
                    DescribeCommandSource(c, id));
            }

            yield return new GameFileCommandRow(
                "Kimahri",
                "Ronso Rage (menu opener)",
                282,
                CommandCharacter_Dictionary.Instance.TryGetValue(282, out string? rName) ? rName : "Ronso Rage",
                sourcePath,
                list.Count > 282 ? list[282].CharacterUser : Character_Enum.None,
                list.Count > 282 ? list[282].OverdriveCategory : (byte)0,
                list.Count > 282 ? list[282].SubMenuCategorization : (byte)0,
                "command.bin parent menu · Lancet-learned children 104–115");
        }

        static string DescribeCommandSource(Ability_Command c, int id)
        {
            if (RonsoCommandIds.Contains(id))
                return "command.bin · learned via Lancet + mon.bin RonsoRageId";
            if (id is >= 96 and <= 119 or 280)
                return "command.bin · unlock via save OD/reels bits + runtime grant";
            if (id == 32)
                return "command.bin · Sphere Grid / default kit";
            if (id == 285)
                return "command.bin · Fury submenu · per-spell save bits @15791+";
            if (id == 286)
                return "command.bin · Mix menu · recipes in prepare.bin";
            return $"command.bin · OD-User={(int)c.CharacterUser} · OD-Choice={c.OverdriveCategory}";
        }

        static IEnumerable<GameFileTakaraRow> BuildTaskTakaraRows(IReadOnlyList<Treasure_Entry> takara, bool includeAll)
        {
            for (int i = 0; i < takara.Count; i++)
            {
                Treasure_Entry e = takara[i];
                string label = DecodeTakaraReward(e);
                bool relevant = includeAll
                    || label.Contains("Sphere", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Sigil", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Crest", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("Soul", StringComparison.OrdinalIgnoreCase)
                    || e.Kind == 0x0A
                    || i is >= 187 and <= 189;

                if (!relevant)
                    continue;

                yield return new GameFileTakaraRow(
                    i,
                    e.Kind,
                    e.ItemId,
                    label,
                    ResolveTakaraOwner(label),
                    "takara.bin",
                    DescribeTakaraMechanism(e));
            }
        }

        static string DescribeTakaraMechanism(Treasure_Entry e) => e.Kind switch
        {
            0x0A => "takara Kind=0x0A → obtainTreasure → key-item save bit",
            0x02 => "takara Kind=0x02 → obtainTreasure → item",
            0x05 => "takara Kind=0x05 → obtainTreasure → buki_get gear",
            0x00 => "takara Kind=0x00 → gil",
            _ => $"takara Kind=0x{e.Kind:X2}",
        };

        static IEnumerable<GameFileBlitzballRow> BuildBlitzballAtlasRows()
        {
            foreach (SpiraDataAtlasDetailEntry row in SpiraDataAtlasCatalog.BlitzballPrizeDetails)
            {
                if (!row.Id.Contains("blitzball-prize", StringComparison.OrdinalIgnoreCase))
                    continue;

                string owner = row.Title.Contains("Reels", StringComparison.OrdinalIgnoreCase) ? "Wakka" : "Blitzball";
                yield return new GameFileBlitzballRow(
                    owner,
                    row.Title,
                    row.Id,
                    row.Evidence,
                    row.Summary,
                    "SpiraDataAtlas / blitz_prize corpus");
            }
        }

        static List<GameFileBlitzballRow> BuildBlitzballStructureRows(ResolvedPaths paths)
        {
            var rows = new List<GameFileBlitzballRow>();
            if (paths.Blitz0200 == null || !File.Exists(paths.Blitz0200))
                return rows;

            try
            {
                Event_File ev = Event_File.Read(BlitzballPrizeStructure_File.EventId, File.ReadAllBytes(paths.Blitz0200));
                List<BlitzballPrizeSite> sites = BlitzballPrizeStructure_File.FindSites(ev);
                foreach (int prizeId in new[] { 187, 188, 189 })
                {
                    int count = sites.Count(s => s.PrizeIndex == prizeId);
                    if (count == 0)
                        continue;

                    string reward = prizeId switch
                    {
                        187 => "Attack Reels",
                        188 => "Status Reels",
                        189 => "Aurochs Reels",
                        _ => $"prize {prizeId}",
                    };

                    rows.Add(new GameFileBlitzballRow(
                        "Wakka",
                        reward,
                        $"prize-index:{prizeId}",
                        "structural",
                        $"{count} assignment site(s) in bltz0200.ebp prize table",
                        paths.Blitz0200));
                }
            }
            catch
            {
                // unreadable bltz0200 in partial extracts
            }

            return rows;
        }

        static List<GameFileRonsoSourceRow> BuildRonsoSourceRows(string? monRoot)
        {
            var rows = new List<GameFileRonsoSourceRow>();
            if (string.IsNullOrWhiteSpace(monRoot) || !Directory.Exists(monRoot))
                return rows;

            for (int monsterId = 0; monsterId <= 999; monsterId++)
            {
                string path = Path.Combine(monRoot, $"_m{monsterId:D3}", $"m{monsterId:D3}.bin");
                if (!File.Exists(path))
                    continue;

                try
                {
                    Monster_File mon = Monster_File.Read(File.ReadAllBytes(path));
                    ushort raw = mon.LootFile?.RonsoRageId ?? 0;
                    if (raw == 0)
                        continue;

                    int cmdId = raw >= 0x3000 ? raw - 0x3000 : raw;
                    string cmdName = CommandCharacter_Dictionary.Instance.TryGetValue((ushort)cmdId, out string? dictName)
                        ? dictName
                        : $"command #{cmdId}";

                    rows.Add(new GameFileRonsoSourceRow(
                        monsterId,
                        cmdId,
                        raw,
                        cmdName,
                        path,
                        "mon.bin loot RonsoRageId +0x06 · Lancet learn source"));
                }
                catch
                {
                    // partial extracts may have truncated mon files
                }
            }

            return rows;
        }

        static (List<GameFileEventGrantRow> rows, int rawTreasureCalls) ScanEventScript(
            string eventId,
            byte[] script,
            IReadOnlyList<Treasure_Entry>? takara,
            bool allTreasure)
        {
            var rows = new List<GameFileEventGrantRow>();
            int rawTreasureCalls = 0;
            for (int i = 0; i + 2 < script.Length; i++)
            {
                if (script[i] != OpCall)
                    continue;

                ushort funcId = (ushort)(script[i + 1] | (script[i + 2] << 8));
                if (funcId is not (FuncObtainTreasure or FuncObtainTreasureSilent or FuncHasKeyItem or FuncGrantAbility or FuncRevokeAbility))
                    continue;

                List<ushort> pushes = CollectPushImmediatesBefore(script, i, 16);
                string native = funcId switch
                {
                    FuncObtainTreasure => "obtainTreasure",
                    FuncObtainTreasureSilent => "obtainTreasureSilently",
                    FuncHasKeyItem => "hasKeyItem",
                    FuncGrantAbility => "grantAbilityCommand",
                    FuncRevokeAbility => "revokeAbilityCommand",
                    _ => $"native:0x{funcId:X4}",
                };

                if (funcId is FuncGrantAbility or FuncRevokeAbility)
                {
                    if (pushes.Count < 2)
                        continue;

                    int cmdId = pushes[^1];
                    int charSlot = pushes[^2];
                    if (!IsTaskCommandId(cmdId))
                        continue;

                    string charName = charSlot >= 0 && charSlot < CharacterSlotNames.Length
                        ? CharacterSlotNames[charSlot]
                        : $"char#{charSlot}";
                    string cmdName = CommandCharacter_Dictionary.Instance.TryGetValue((ushort)cmdId, out string? dictName)
                        ? dictName
                        : $"command #{cmdId}";
                    string owner = ResolveCommandOwner(cmdId, charName);
                    bool grant = funcId == FuncGrantAbility;

                    rows.Add(new GameFileEventGrantRow(
                        owner,
                        cmdName,
                        eventId,
                        i,
                        native,
                        cmdId,
                        null,
                        $"{charName} · cmd {cmdId} ({cmdName})",
                        grant ? "grantAbilityCommand → command.bin" : "revokeAbilityCommand (on=0)",
                        "bytecode-scan · B5 FC/FD 01 · stack: char then cmdId"));
                    continue;
                }

                if (funcId == FuncHasKeyItem)
                {
                    ushort keyRaw = pushes.LastOrDefault();
                    int keyIndex = DecodeKeyItemIndex(keyRaw);
                    string keyLabel = ResolveKeyItemName(keyIndex);
                    if (!IsTaskKeyItem(keyLabel))
                        continue;

                    rows.Add(new GameFileEventGrantRow(
                        ResolveKeyItemOwner(keyLabel),
                        keyLabel,
                        eventId,
                        i,
                        native,
                        keyRaw,
                        null,
                        keyLabel,
                        "key-item gate",
                        "bytecode-scan"));
                    continue;
                }

                rawTreasureCalls++;
                int? takaraIndex = GuessTakaraIndex(pushes);
                if (takaraIndex == null)
                    continue;

                string reward = takara != null && takaraIndex.Value < takara.Count
                    ? DecodeTakaraReward(takara[takaraIndex.Value])
                    : $"takara #{takaraIndex.Value}";

                if (!allTreasure && !IsTaskRelevantRewardLabel(reward))
                    continue;

                rows.Add(new GameFileEventGrantRow(
                    ResolveTakaraOwner(reward),
                    reward,
                    eventId,
                    i,
                    native,
                    takaraIndex.Value,
                    takaraIndex,
                    reward,
                    "obtainTreasure → takara.bin",
                    "bytecode-scan"));
            }

            return (rows, rawTreasureCalls);
        }

        static bool IsTaskCommandId(int cmdId) =>
            cmdId is >= 96 and <= 119 or 280 or 285 or 286 or >= 104 and <= 115;

        static string ResolveCommandOwner(int cmdId, string charName)
        {
            if (RonsoCommandIds.Contains(cmdId))
                return "Kimahri";
            if (cmdId is >= 96 and <= 99)
                return "Tidus";
            if (cmdId is >= 100 and <= 103)
                return "Auron";
            if (cmdId is >= 116 and <= 119)
                return "Wakka";
            if (cmdId == 280)
                return "Yuna";
            if (cmdId == 285)
                return "Lulu";
            if (cmdId == 286)
                return "Rikku";
            return charName;
        }

        static List<ushort> CollectPushImmediatesBefore(byte[] script, int callOffset, int lookback)
        {
            List<ushort> values = new();
            int start = Math.Max(0, callOffset - lookback);
            for (int j = start; j + 2 < callOffset; j++)
            {
                if (script[j] != OpPushImmediate)
                    continue;
                values.Add((ushort)(script[j + 1] | (script[j + 2] << 8)));
            }
            return values;
        }

        static int? GuessTakaraIndex(IReadOnlyList<ushort> pushes)
        {
            foreach (ushort v in pushes.Reverse())
            {
                if (v <= 497)
                    return v;
            }
            return null;
        }

        static int DecodeKeyItemIndex(ushort raw)
        {
            try
            {
                if (FfxCommon_Util.GetGameCategory(raw) == (byte)GameCategory_Enum.KeyItems)
                    return FfxCommon_Util.GetGameIndex(raw);
            }
            catch
            {
            }
            return raw <= 0x3F ? raw : raw & 0xFF;
        }

        static string ResolveKeyItemName(int index)
        {
            if (KeyItem_Dictionary.TryGetName((ushort)index, out string? name))
                return name;
            return $"Key item #{index}";
        }

        static bool IsTaskKeyItem(string label) =>
            label.Contains("Sphere", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Sigil", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Crest", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Soul", StringComparison.OrdinalIgnoreCase);

        static bool IsTaskRelevantRewardLabel(string label) =>
            IsTaskKeyItem(label)
            || label.Contains("Reels", StringComparison.OrdinalIgnoreCase);

        static string DecodeTakaraReward(Treasure_Entry e) => e.Kind switch
        {
            0x00 => $"Gil ×{e.Quantity * 100}",
            0x02 => ResolveItemName(e.ItemId),
            0x05 => $"Gear buki_get #{e.ItemId}",
            0x0A => ResolveKeyItemName(DecodeKeyItemIndex(e.ItemId)),
            _ => $"Kind=0x{e.Kind:X2} Type=0x{e.ItemId:X4}",
        };

        static string ResolveItemName(ushort rawType)
        {
            try
            {
                byte cat = FfxCommon_Util.GetGameCategory(rawType);
                ushort idx = FfxCommon_Util.GetGameIndex(rawType);
                if (cat == (byte)GameCategory_Enum.Items)
                    return FfxCommon_Util.GetGameIndexName(cat, idx);
            }
            catch
            {
            }

            return Item_Dictionary.Instance.TryGetValue(rawType, out string? name) ? name : $"Item 0x{rawType:X4}";
        }

        static string ResolveTakaraOwner(string label) =>
            label.Contains("Jecht", StringComparison.OrdinalIgnoreCase) ? "Auron"
            : label.Contains("Braska", StringComparison.OrdinalIgnoreCase) ? "Auron"
            : label.Contains("Attack Reels", StringComparison.OrdinalIgnoreCase) ? "Wakka"
            : label.Contains("Status Reels", StringComparison.OrdinalIgnoreCase) ? "Wakka"
            : label.Contains("Aurochs Reels", StringComparison.OrdinalIgnoreCase) ? "Wakka"
            : label.Contains("Sun", StringComparison.OrdinalIgnoreCase) ? "Wakka"
            : label.Contains("Moon", StringComparison.OrdinalIgnoreCase) ? "Wakka"
            : label.Contains("Mars", StringComparison.OrdinalIgnoreCase) ? "Auron"
            : label.Contains("Saturn", StringComparison.OrdinalIgnoreCase) ? "Kimahri"
            : label.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) ? "Lulu"
            : label.Contains("Venus", StringComparison.OrdinalIgnoreCase) ? "Rikku"
            : label.Contains("Mercury", StringComparison.OrdinalIgnoreCase) ? "Yuna"
            : "Task item";

        static string ResolveKeyItemOwner(string label) => ResolveTakaraOwner(label);

        sealed class ResolvedPaths
        {
            public string? CommandBin { get; init; }
            public string? TakaraBin { get; init; }
            public string? ImportantBin { get; init; }
            public string? EventRoot { get; init; }
            public string? Blitz0200 { get; init; }
            public string? MonRoot { get; init; }
        }
    }

    internal enum TreasureGrantScope
    {
        TaskRelevant,
        AllTreasureGrants,
    }

    internal sealed class GameFileLoadRequest
    {
        public string? ProjectPath { get; init; }
        public string? KernelRoot { get; init; }
        public string? EventRoot { get; init; }
        public string? Bltz0200Path { get; init; }
        public bool ScanEvents { get; init; } = true;
        public TreasureGrantScope EventGrantScope { get; init; } = TreasureGrantScope.TaskRelevant;
    }

    internal sealed record GameFileSnapshot(
        string Summary,
        IReadOnlyList<GameFileCommandRow> Commands,
        IReadOnlyList<GameFileTakaraRow> TakaraRows,
        IReadOnlyList<GameFileEventGrantRow> EventGrants,
        IReadOnlyList<GameFileBlitzballRow> BlitzballRows,
        IReadOnlyList<GameFileRonsoSourceRow> RonsoSources,
        IReadOnlyList<GameFileBattleGateRow> BattleGates,
        int RawObtainTreasureCalls,
        int DisplayedEventGrantCount);

    internal sealed record GameFileCommandRow(
        string Owner,
        string TaskReward,
        int CommandId,
        string CommandName,
        string SourceFile,
        Character_Enum CharacterUser,
        byte OverdriveCategory,
        byte SubMenu,
        string Notes)
    {
        public string CommandHex => $"0x{0x3000 | CommandId:X4}";
    }

    internal sealed record GameFileTakaraRow(
        int TakaraIndex,
        byte Kind,
        ushort Type,
        string RewardLabel,
        string Owner,
        string SourceFile,
        string Mechanism);

    internal sealed record GameFileEventGrantRow(
        string Owner,
        string RewardLabel,
        string EventId,
        int ScriptOffset,
        string NativeName,
        int PrimaryOperand,
        int? TakaraIndex,
        string DecodedReward,
        string Mechanism,
        string Evidence)
    {
        public string Location => $"{EventId}.ebp@0x{ScriptOffset:X}";
    }

    internal sealed record GameFileBlitzballRow(
        string Owner,
        string RewardLabel,
        string RefId,
        string Status,
        string Detail,
        string SourceFile);

    internal sealed record GameFileRonsoSourceRow(
        int MonsterId,
        int CommandId,
        ushort RonsoRageRaw,
        string CommandName,
        string SourceFile,
        string Mechanism)
    {
        public string MonsterLabel => $"m{MonsterId:D3}";
        public string CommandHex => $"0x{0x3000 | CommandId:X4}";
    }

    internal sealed record GameFileBattleGateRow(
        string Owner,
        string RewardLabel,
        string GateKind,
        string SaveLayout,
        string RuntimePath,
        string Notes,
        string Evidence);
}
