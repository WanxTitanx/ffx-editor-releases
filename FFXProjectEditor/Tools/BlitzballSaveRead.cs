using System;
using System.Collections.Generic;
using System.IO;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;

namespace FFXProjectEditor.Tools
{
    // Read-only Blitzball save-file prize reader / save-compare differ (Jarvis-WAKKA scout tooling).
    // Decodes the league/tournament prize-index u16 fields at the RE-derived offsets and resolves
    // each value to its reward via the closed Atlas catalog. NEVER writes anything.
    //
    // Offset model (see docs/ai/BLITZBALL_SAVE_RUNTIME_WRITER_SCOUT_2026-06-09.md):
    //   - SaveData block starts at file offset 0x40 (IDA sub_8B5450: memcpy(SaveData, SaveFile+0x40, 0x68C0)).
    //   - BlitzballData @ SaveData+0x1984; prize fields are u16 LE inside it.
    //   - The on-disk container format is NOT live-confirmed, so --base overrides the SaveData-in-file
    //     offset, and --blitz-save-diff does a full-file byte diff to locate the prize region empirically.
    internal static class BlitzballSaveRead
    {
        const int SaveDataInFileDefault = 0x40;   // SaveData starts at file+0x40 (IDA-derived)
        const int BlitzDataStart        = 0x1984; // BlitzballData within SaveData
        const int BlitzDataSize         = 0x808;  // mapped BlitzballData span (cost + prizes + tech pages)
        const int OffLeaguePrize        = 0x19FC; // u16[3]
        const int OffTournamentPrize    = 0x1A02; // u16[3]
        const int OffLeagueTopScorer    = 0x1A08; // u16
        const int OffTournamentTop      = 0x1A0A; // u16

        static Dictionary<string, string>? _catalog;

        public static int Run(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "";
            int baseOff = SaveDataInFileDefault;
            List<string> pos = new();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--base" && i + 1 < args.Length)
                    baseOff = ParseHex(args[++i]);
                else
                    pos.Add(args[i]);
            }

            try
            {
                if (mode == "--blitz-save-diff")
                {
                    if (pos.Count < 2)
                    {
                        Console.WriteLine("usage: --blitz-save-diff <before-save> <after-save> [--base 0x40]");
                        return 2;
                    }
                    return Diff(pos[0], pos[1], baseOff);
                }

                if (pos.Count < 1)
                {
                    Console.WriteLine("usage: --blitz-save-read <save-file> [--base 0x40]");
                    return 2;
                }
                return ReadOne(pos[0], baseOff);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static int ReadOne(string path, int baseOff)
        {
            byte[] data = File.ReadAllBytes(path);
            Console.WriteLine("=== Blitzball prize reader (read-only) ===");
            Console.WriteLine($"file: {path} ({data.Length} bytes)");
            Console.WriteLine($"base (SaveData in file): 0x{baseOff:X} [IDA-derived; NOT live-confirmed; override with --base]");
            Console.WriteLine($"BlitzballData @ file 0x{baseOff + BlitzDataStart:X} (SaveData+0x{BlitzDataStart:X})");
            Console.WriteLine();
            PrintPrizes(data, baseOff);
            return 0;
        }

        static int Diff(string pathA, string pathB, int baseOff)
        {
            byte[] a = File.ReadAllBytes(pathA);
            byte[] b = File.ReadAllBytes(pathB);
            Console.WriteLine("=== Blitzball save-compare (read-only diff) ===");
            Console.WriteLine($"A (before): {pathA} ({a.Length} bytes)");
            Console.WriteLine($"B (after):  {pathB} ({b.Length} bytes)");
            Console.WriteLine($"base (SaveData in file): 0x{baseOff:X}");
            Console.WriteLine();
            Console.WriteLine("--- prize fields: A (before) ---");
            PrintPrizes(a, baseOff);
            Console.WriteLine("--- prize fields: B (after) ---");
            PrintPrizes(b, baseOff);
            Console.WriteLine("--- full-file byte diff (locate the real prize region empirically) ---");
            FullDiff(a, b, baseOff);
            return 0;
        }

        static void PrintPrizes(byte[] data, int baseOff)
        {
            int[] vals = new int[8];
            int vi = 0;
            vi = PrintField(data, baseOff, OffLeaguePrize, 3, "league_prize_index", vals, vi);
            vi = PrintField(data, baseOff, OffTournamentPrize, 3, "tournament_prize_index", vals, vi);
            vi = PrintField(data, baseOff, OffLeagueTopScorer, 1, "league_top_scorer_prize", vals, vi);
            vi = PrintField(data, baseOff, OffTournamentTop, 1, "tournament_top_scorer_prize", vals, vi);

            // Honesty heuristic: if everything looks empty or out-of-range, the on-disk base is probably wrong.
            int valid = 0, nonzero = 0;
            for (int i = 0; i < vi; i++)
            {
                if (vals[i] != 0) nonzero++;
                if (vals[i] == 0 || (vals[i] >= 1 && vals[i] <= 160) || (vals[i] >= 187 && vals[i] <= 189)) valid++;
            }
            if (vi > 0 && valid < vi)
                Console.WriteLine("  ! WARNING: some values are out of the expected prize range (0..160, 187..189).");
            if (vi > 0 && valid < vi)
                Console.WriteLine("    The on-disk SaveData offset is likely != --base. Use --blitz-save-diff on a before/after pair to locate it.");
            if (nonzero == 0)
                Console.WriteLine("  (all prize slots are 0 = empty/claimed; capture right after winning, before claiming.)");
            Console.WriteLine();
        }

        static int PrintField(byte[] data, int baseOff, int sdOff, int count, string name, int[] vals, int vi)
        {
            for (int i = 0; i < count; i++)
            {
                int fileOff = baseOff + sdOff + i * 2;
                string label = count > 1 ? $"{name}[{i}]" : name;
                if (fileOff + 1 >= data.Length)
                {
                    Console.WriteLine($"  {label,-28} @0x{fileOff:X}: <out of file bounds>");
                    continue;
                }
                int val = data[fileOff] | (data[fileOff + 1] << 8);
                if (vi < vals.Length) vals[vi++] = val;
                Console.WriteLine($"  {label,-28} @0x{fileOff:X} = {val,5} (0x{val:X4}) -> {ResolvePrize(val)}");
            }
            return vi;
        }

        // prize value -> domain + resolved reward (via the closed Atlas catalog; graceful fallback).
        // internal so the save WRITER tool reuses the exact same decode for its before/after display.
        internal static string ResolvePrize(int prize)
        {
            if (prize == 0) return "(0 = empty/claimed)";

            string domain, id, extra = "";
            if (prize >= 1 && prize <= 100) { domain = "treasure"; id = $"atlas:blitzball-prize:treasure:{prize}"; extra = $" takara {prize + 220}"; }
            else if (prize >= 101 && prize <= 160) { domain = "tech"; id = $"atlas:blitzball-prize:tech:{prize}"; }
            else if (prize >= 187 && prize <= 189) { domain = "overdrive"; id = $"atlas:blitzball-prize:overdrive:{prize}"; }
            else return $"out-of-range (0x{prize:X4}) — unknown / likely wrong offset";

            _catalog ??= BuildCatalog();
            string title = _catalog.TryGetValue(id, out string? t) ? t : "(reward name needs dev work/ catalog)";
            return $"{domain}{extra}: {title}";
        }

        static Dictionary<string, string> BuildCatalog()
        {
            Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (SpiraDataAtlasDetailEntry d in SpiraDataAtlasCatalog.BlitzballPrizeDetails)
                    map[d.Id] = d.Title;
            }
            catch
            {
                // catalog optional; values still decode without reward names.
            }
            return map;
        }

        static void FullDiff(byte[] a, byte[] b, int baseOff)
        {
            int min = Math.Min(a.Length, b.Length);
            int blitzStart = baseOff + BlitzDataStart;
            int blitzEnd = blitzStart + BlitzDataSize;

            int runs = 0, totalChanged = 0;
            int i = 0;
            const int maxRuns = 60;
            while (i < min)
            {
                if (a[i] == b[i]) { i++; continue; }
                int start = i;
                while (i < min && a[i] != b[i]) i++;
                int len = i - start;
                totalChanged += len;
                runs++;
                if (runs <= maxRuns)
                {
                    bool inBlitz = start < blitzEnd && i > blitzStart;
                    string tag = inBlitz ? "  <-- inside BlitzballData" : "";
                    Console.WriteLine($"  @0x{start:X}..0x{i - 1:X} ({len}B){tag}");
                    Console.WriteLine($"      A: {Hex(a, start, len)}");
                    Console.WriteLine($"      B: {Hex(b, start, len)}");
                    if (inBlitz)
                        DecodeBlitzRun(a, b, start, len, blitzStart);
                }
                i++;
            }

            if (a.Length != b.Length)
                Console.WriteLine($"  (note: files differ in length: A={a.Length}, B={b.Length}; compared first {min} bytes.)");
            if (runs > maxRuns)
                Console.WriteLine($"  ... {runs - maxRuns} more changed run(s) omitted.");
            Console.WriteLine($"  total: {runs} changed run(s), {totalChanged} byte(s).");
            Console.WriteLine("  Tip: a prize win should change a u16 in BlitzballData; cross-check its new value -> reward above.");
        }

        // For a changed run inside BlitzballData, decode aligned u16s as prize values old->new.
        static void DecodeBlitzRun(byte[] a, byte[] b, int start, int len, int blitzStart)
        {
            for (int off = start; off + 1 < start + len + 1 && off + 1 < a.Length && off + 1 < b.Length; off += 2)
            {
                if ((off - blitzStart) % 2 != 0) { off--; continue; }
                int av = a[off] | (a[off + 1] << 8);
                int bv = b[off] | (b[off + 1] << 8);
                if (av == bv) continue;
                int sd = off - (blitzStart - BlitzDataStart); // SaveData-relative offset
                Console.WriteLine($"        u16 @0x{off:X} (SaveData+0x{sd:X}): {av}->{bv}  [{ResolvePrize(av)}] => [{ResolvePrize(bv)}]");
            }
        }

        static string Hex(byte[] data, int start, int len)
        {
            int n = Math.Min(len, 32);
            System.Text.StringBuilder sb = new();
            for (int i = 0; i < n; i++) sb.Append(data[start + i].ToString("X2")).Append(' ');
            if (len > n) sb.Append("...");
            return sb.ToString().TrimEnd();
        }

        static int ParseHex(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return Convert.ToInt32(s, 16);
        }
    }
}
