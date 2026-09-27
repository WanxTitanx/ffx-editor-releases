// BattleCompanionActivationLab — RT0 / read-only gate for the m213 hidden companion activation reader.
//
// Proves, on the real battle + monster corpus:
//   (1) The five current-corpus m213 host battles recognize the full package:
//       formation preseeded + host btlSetAppear + host 0x408A on Monster#01/#02 + companion Hidden birth.
//   (2) The host-script footprint still knows six CurrentBattle tokens, but mcyt00_21 is now a current-corpus
//       drift/collision sentinel instead of a valid slot0=m213 host battle.
//   (3) Recognized packages are EXACTLY those five host battles.
//   (4) Corpus clean: the reader never throws across the whole battle corpus.
//
// Exit 0 only if (1)-(4) hold. Read-only: never writes anything.

using System.Text.Json;
using FFXProjectEditor.FfxLib.Battle;

internal static class Program
{
    const string CurrentCorpusDriftBattle = "mcyt00_21";
    static readonly int[] CurrentCorpusDriftExpectedFormation = { 334, 335, 336 };

    static readonly string[] GoldenRecognizedBattles =
    {
        "maca03_20",
        "maca03_21",
        "maca03_22",
        "mcyt00_20",
        "mcyt00_22",
    };

    static readonly Dictionary<string, int[]> GoldenCompanions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["maca03_20"] = new[] { 12, 4 },
        ["maca03_21"] = new[] { 37, 37 },
        ["maca03_22"] = new[] { 19, 4 },
        ["mcyt00_20"] = new[] { 12, 4 },
        ["mcyt00_22"] = new[] { 19, 4 },
    };

    static int Main(string[] args)
    {
        string btlRoot = DefaultBtlRoot();
        string monRoot = DefaultMonRoot();
        string? jsonOut = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
            else if (args[i] == "--mon-root" && i + 1 < args.Length) monRoot = args[++i];
            else if (!args[i].StartsWith("--", StringComparison.Ordinal)) btlRoot = args[i];
        }

        if (!Directory.Exists(btlRoot))
        {
            Console.Error.WriteLine($"btl root not found: {btlRoot}");
            return 2;
        }

        if (!Directory.Exists(monRoot))
        {
            Console.Error.WriteLine($"mon root not found: {monRoot}");
            return 2;
        }

        var fails = new List<string>();
        var recognized = new List<string>();
        var samples = new List<object>();
        string driftFormationLabel = "-";
        object? driftSample = null;

        foreach (string battleId in GoldenRecognizedBattles)
        {
            string path = Path.Combine(btlRoot, battleId, battleId + ".bin");
            if (!File.Exists(path))
            {
                fails.Add($"GOLDEN {battleId}: battle bin not found");
                continue;
            }

            try
            {
                BattleCompanionActivation_File activation = BattleCompanionActivation_File.ReadFromBattleBin(
                    battleId,
                    File.ReadAllBytes(path),
                    monsterId => ResolveMonsterBin(monRoot, monsterId));

                samples.Add(new
                {
                    battleId,
                    activation.HasRecognizedPackage,
                    activation.HumanSummary,
                    activation.BattleTokenLabel,
                    activation.PeerGameplaySummary,
                    rows = activation.Rows.Select(row => new
                    {
                        row.SlotLabel,
                        row.MonsterId,
                        row.HiddenForSelectedBattle,
                        row.HiddenBattleIdsLabel,
                        row.HostActionOffsetHex,
                    }).ToArray()
                });

                if (!activation.HasRecognizedPackage)
                    fails.Add($"GOLDEN {battleId}: package not recognized");
                if (!activation.HostUsesBtlSetAppear)
                    fails.Add($"GOLDEN {battleId}: missing host btlSetAppear(Self,0,0)");
                if (!activation.SelectedBattleHasHostSummonPair)
                    fails.Add($"GOLDEN {battleId}: missing selected-battle host summon pair");
                if (!activation.SpawnFromZeroNotSupported)
                    fails.Add($"GOLDEN {battleId}: spawn-from-zero boundary did not close");
                if (activation.Rows.Count != 2)
                    fails.Add($"GOLDEN {battleId}: expected 2 companion rows, got {activation.Rows.Count}");
                if (activation.Rows.Any(static row => !row.HiddenForSelectedBattle))
                    fails.Add($"GOLDEN {battleId}: at least one companion did not prove Hidden for the selected battle");
                if (activation.Rows.Any(static row => row.HostActionOffset < 0))
                    fails.Add($"GOLDEN {battleId}: at least one companion row lost the host 0x408A site");

                int[] expectedCompanions = GoldenCompanions[battleId];
                int[] actualCompanions = activation.Rows
                    .OrderBy(static row => row.FormationSlotIndex)
                    .Select(static row => row.MonsterId)
                    .ToArray();
                if (actualCompanions.Length == 2
                    && (actualCompanions[0] != expectedCompanions[0] || actualCompanions[1] != expectedCompanions[1]))
                {
                    fails.Add($"GOLDEN {battleId}: companion ids mismatch ({string.Join(",", actualCompanions)} != {string.Join(",", expectedCompanions)})");
                }
            }
            catch (Exception ex)
            {
                fails.Add($"GOLDEN {battleId}: reader threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        string driftPath = Path.Combine(btlRoot, CurrentCorpusDriftBattle, CurrentCorpusDriftBattle + ".bin");
        if (File.Exists(driftPath))
        {
            try
            {
                byte[] driftBytes = File.ReadAllBytes(driftPath);
                Battle_File driftBattle = Battle_File.Read(CurrentCorpusDriftBattle, driftBytes);
                BattleCompanionActivation_File driftActivation = BattleCompanionActivation_File.ReadFromBattleBin(
                    CurrentCorpusDriftBattle,
                    driftBytes,
                    monsterId => ResolveMonsterBin(monRoot, monsterId));

                int[] driftFormation = driftBattle.Formation?.Slots
                    .Where(static slot => !slot.IsEmpty)
                    .Take(3)
                    .Select(static slot => slot.DictionaryId)
                    .ToArray()
                    ?? Array.Empty<int>();
                driftFormationLabel = driftFormation.Length == 0
                    ? "(no formation)"
                    : string.Join(", ", driftBattle.Formation!.Slots
                        .Where(static slot => !slot.IsEmpty)
                        .Take(3)
                        .Select(static slot => $"slot{slot.SlotIndex}=m{slot.DictionaryId:D3}"));

                driftSample = new
                {
                    battleId = CurrentCorpusDriftBattle,
                    driftFormationLabel,
                    expectedFormation = CurrentCorpusDriftExpectedFormation.Select(id => $"m{id:D3}").ToArray(),
                    driftActivation.HumanSummary,
                    driftActivation.BattleTokenLabel,
                    notes = driftActivation.Notes.ToArray(),
                };

                if (driftActivation.HasRecognizedPackage)
                    fails.Add($"DRIFT {CurrentCorpusDriftBattle}: unexpectedly recognized as an m213 host battle");
                if (!driftActivation.SelectedBattleTokenKnown)
                    fails.Add($"DRIFT {CurrentCorpusDriftBattle}: token should still be known from the host-script footprint");
                if (!driftFormation.SequenceEqual(CurrentCorpusDriftExpectedFormation))
                {
                    fails.Add($"DRIFT {CurrentCorpusDriftBattle}: formation mismatch ({string.Join(",", driftFormation)} != {string.Join(",", CurrentCorpusDriftExpectedFormation)})");
                }
                if (!driftActivation.Notes.Any(static note => note.Contains("drift/collision", StringComparison.OrdinalIgnoreCase)))
                    fails.Add($"DRIFT {CurrentCorpusDriftBattle}: reader did not surface the drift/collision note");
            }
            catch (Exception ex)
            {
                fails.Add($"DRIFT {CurrentCorpusDriftBattle}: reader threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        int battlesScanned = 0;
        int readerThrows = 0;
        foreach (string dir in Directory.EnumerateDirectories(btlRoot))
        {
            string battleId = Path.GetFileName(dir);
            string path = Path.Combine(dir, battleId + ".bin");
            if (!File.Exists(path))
                continue;

            battlesScanned++;
            try
            {
                BattleCompanionActivation_File activation = BattleCompanionActivation_File.ReadFromBattleBin(
                    battleId,
                    File.ReadAllBytes(path),
                    monsterId => ResolveMonsterBin(monRoot, monsterId));

                if (activation.HasRecognizedPackage)
                    recognized.Add(battleId);
            }
            catch (Exception ex)
            {
                readerThrows++;
                fails.Add($"CORPUS {battleId}: reader threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        string[] recognizedSorted = recognized.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        string[] expectedSorted = GoldenRecognizedBattles.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        if (!recognizedSorted.SequenceEqual(expectedSorted))
        {
            fails.Add("Recognized set mismatch: " +
                      $"got [{string.Join(", ", recognizedSorted)}] expected [{string.Join(", ", expectedSorted)}]");
        }

        bool pass = fails.Count == 0;
        Console.WriteLine("BattleCompanionActivationLab — m213 hidden companion activation / summon-handoff gate");
        Console.WriteLine($"  btl root            : {btlRoot}");
        Console.WriteLine($"  mon root            : {monRoot}");
        Console.WriteLine($"  battles scanned     : {battlesScanned}");
        Console.WriteLine($"  recognized packages : {recognizedSorted.Length}");
        Console.WriteLine($"  reader throws       : {readerThrows}");
        Console.WriteLine($"  recognized ids      : {(recognizedSorted.Length == 0 ? "(none)" : string.Join(", ", recognizedSorted))}");
        Console.WriteLine($"  drift sentinel      : {CurrentCorpusDriftBattle} (token/script known, current battle file on disk is not slot0=m213)");
        Console.WriteLine($"  drift formation     : {driftFormationLabel}");

        if (fails.Count > 0)
        {
            Console.WriteLine($"  FAILS ({fails.Count}):");
            foreach (string fail in fails.Take(40))
                Console.WriteLine($"    {fail}");
        }

        Console.WriteLine(pass
            ? "VERDICT: PASS — five current-corpus m213 host battles recognized, drift sentinel isolated, corpus clean."
            : "VERDICT: FAIL — see FAILS above.");

        if (jsonOut != null)
        {
            var payload = new
            {
                pass,
                btlRoot,
                monRoot,
                battlesScanned,
                readerThrows,
                recognized = recognizedSorted,
                expected = expectedSorted,
                driftSentinel = CurrentCorpusDriftBattle,
                driftExpectedFormation = CurrentCorpusDriftExpectedFormation,
                driftFormationLabel,
                drift = driftSample,
                fails,
                samples,
            };
            File.WriteAllText(jsonOut, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        return pass ? 0 : 1;
    }

    static string DefaultBtlRoot() => @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl";

    static string DefaultMonRoot() => @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\mon";

    static byte[]? ResolveMonsterBin(string monRoot, int monsterId)
    {
        string path = Path.Combine(monRoot, $"_m{monsterId:D3}", $"m{monsterId:D3}.bin");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
