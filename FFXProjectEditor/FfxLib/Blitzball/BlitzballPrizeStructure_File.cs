using System;
using System.Collections.Generic;
using FFXProjectEditor.FfxLib.Event;

namespace FFXProjectEditor.FfxLib.Blitzball
{
    /// <summary>Which blitzball prize slot a write-site fills (the bltz0200-local ATEL variable id).</summary>
    public enum BlitzballPrizeVar : byte
    {
        LeagueStandings = 0x30,      // array[0..2] = 1st/2nd/3rd place league prize-index
        TournamentStandings = 0x31,  // array[0..2] = 1st/2nd/3rd place tournament prize-index
        LeagueTopScorer = 0x32,      // scalar = league top-scorer prize-index
        TournamentTopScorer = 0x33,  // scalar = tournament top-scorer prize-index
    }

    /// <summary>One blitzball prize-index assignment in the bltz0200 ATEL script: a constant
    /// <c>Set Blitzball*PrizeIndex[slot] = &lt;prizeIndex&gt;</c>. <see cref="ValueOffset"/> is chunk-0-relative
    /// and points at the 2-byte little-endian PUSHII operand that holds the prize index.</summary>
    public sealed class BlitzballPrizeSite
    {
        public required int ValueOffset { get; init; }
        public required ushort PrizeIndex { get; init; }
        public required BlitzballPrizeVar Var { get; init; }
        public required int Slot { get; init; }   // 0/1/2 for the standings arrays; -1 for the top-scorer scalars
        public required bool IsArray { get; init; }
    }

    /// <summary>One roll-threshold (ODDS) immediate inside a prize switch's case-range dispatch:
    /// <c>DUP · PUSHII &lt;threshold&gt; · {GE|LE} · {jump}</c> = <c>case &gt;= N</c> / <c>case &lt;= M</c> over the
    /// <c>GetRandomInRange(100)</c> roll. Changing the threshold changes the probability of the bucket it bounds.
    /// <see cref="ValueOffset"/> is chunk-0-relative and points at the 2-byte LE PUSHII operand.</summary>
    public sealed class BlitzballRollThresholdSite
    {
        public required int ValueOffset { get; init; }
        public required ushort Threshold { get; init; }
        public required bool IsUpperBound { get; init; } // true = "case <= M"; false = "case >= N"
    }

    /// <summary>
    /// READ + edit the blitzball PRIZE STRUCTURE inside <c>bltz0200.ebp</c> — the table that decides WHICH prize
    /// index each league/tournament placement (and the random reward bucket) awards. Each assignment is a constant
    /// immediate in the ATEL bytecode:
    ///   array  : <c>AE [slot:2] AE [prizeIndex:2] A3 [0x30|0x31] 00</c>  (PUSHII slot · PUSHII value · write-array)
    ///   scalar : <c>AE [prizeIndex:2] A0 [0x32|0x33] 00</c>             (PUSHII value · write-scalar, top-scorer)
    /// The prize index is a 2-byte immediate, so changing it is a length-preserving GAME-FILE edit (no EXE patch),
    /// exactly like recruitment. The awarded reward then resolves as prizeIndex+220 → takara row (0..100) or
    /// MacroDict#8 #(prizeIndex-100) (>100). The bltz0200-local ATEL var ids (0x30..0x33) were proved from the
    /// bltz0200 dump switch/read sites. Corpus: ~459 assignment sites (league + tournament, all rounds + the
    /// random reward distribution). See docs/reverse/FFX_BLITZBALL_PRIZE_STRUCTURE_RE_2026-06-10.md.
    /// </summary>
    public static class BlitzballPrizeStructure_File
    {
        public const string EventId = "bltz0200";

        const byte OpPushImmediate = 0xAE; // PUSHII <u16>
        const byte OpWriteArray = 0xA3;    // write-array <varid> <00>
        const byte OpWriteScalar = 0xA0;   // write-scalar <varid> <00>
        const byte OpDup = 0x29;           // DUP (the switch value, for a case-range compare)
        const byte OpGe = 0x0E;            // >=
        const byte OpLe = 0x0F;            // <=
        const byte OpJumpIfFalse = 0xD7;   // else-jump (pairs with >=)
        const byte OpJumpIfTrue = 0xD6;    // then-jump (pairs with <=)

        const byte OpCall = 0xB5;             // call <u16 funcId>
        const ushort FuncGetRandomInRange = 0x00A6; // Common.GetRandomInRange — the roll that opens each prize draw

        // A roll-threshold is only treated as a prize ODDS knob if it sits within this many bytes of a prize-index
        // assignment, so the scan stays inside the prize tables and never edits an unrelated bltz0200 switch.
        const int MaxGapToPrizeSet = 768;

        public static bool IsArrayVar(byte v) => v == (byte)BlitzballPrizeVar.LeagueStandings || v == (byte)BlitzballPrizeVar.TournamentStandings;
        public static bool IsScalarVar(byte v) => v == (byte)BlitzballPrizeVar.LeagueTopScorer || v == (byte)BlitzballPrizeVar.TournamentTopScorer;

        /// <summary>Scan the event's ATEL script for every constant prize-index assignment site.</summary>
        public static List<BlitzballPrizeSite> FindSites(Event_File ev)
        {
            ArgumentNullException.ThrowIfNull(ev);
            ReadOnlySpan<byte> s = ev.ScriptChunkBytes;
            var sites = new List<BlitzballPrizeSite>();

            for (int i = 0; i + 9 <= s.Length; i++)
            {
                // Array write: AE slot:2 AE value:2 A3 varid 00
                if (s[i] == OpPushImmediate && s[i + 3] == OpPushImmediate
                    && s[i + 6] == OpWriteArray && s[i + 8] == 0x00 && IsArrayVar(s[i + 7]))
                {
                    int slot = s[i + 1] | (s[i + 2] << 8);
                    int valOff = i + 4;
                    sites.Add(new BlitzballPrizeSite
                    {
                        ValueOffset = valOff,
                        PrizeIndex = (ushort)(s[valOff] | (s[valOff + 1] << 8)),
                        Var = (BlitzballPrizeVar)s[i + 7],
                        Slot = slot,
                        IsArray = true,
                    });
                }
            }

            for (int i = 0; i + 6 <= s.Length; i++)
            {
                // Scalar write: AE value:2 A0 varid 00
                if (s[i] == OpPushImmediate && s[i + 3] == OpWriteScalar
                    && s[i + 5] == 0x00 && IsScalarVar(s[i + 4]))
                {
                    int valOff = i + 1;
                    sites.Add(new BlitzballPrizeSite
                    {
                        ValueOffset = valOff,
                        PrizeIndex = (ushort)(s[valOff] | (s[valOff + 1] << 8)),
                        Var = (BlitzballPrizeVar)s[i + 4],
                        Slot = -1,
                        IsArray = false,
                    });
                }
            }

            sites.Sort((a, b) => a.ValueOffset.CompareTo(b.ValueOffset));
            return sites;
        }

        /// <summary>Scan for the roll-threshold (ODDS) immediates of the prize switches: the
        /// <c>case &gt;= N</c> / <c>case &lt;= M</c> bounds over <c>GetRandomInRange(100)</c>. Scoped to within
        /// <see cref="MaxGapToPrizeSet"/> bytes of a prize-index assignment so only prize-table bounds surface.</summary>
        public static List<BlitzballRollThresholdSite> FindRollThresholds(Event_File ev)
        {
            ArgumentNullException.ThrowIfNull(ev);
            ReadOnlySpan<byte> s = ev.ScriptChunkBytes;

            List<int> prizeOffsets = FindSites(ev).ConvertAll(p => p.ValueOffset);
            prizeOffsets.Sort();

            var sites = new List<BlitzballRollThresholdSite>();
            for (int i = 0; i + 6 <= s.Length; i++)
            {
                if (s[i] != OpDup || s[i + 1] != OpPushImmediate)
                    continue;
                bool ge = s[i + 4] == OpGe && s[i + 5] == OpJumpIfFalse;
                bool le = s[i + 4] == OpLe && s[i + 5] == OpJumpIfTrue;
                if (!ge && !le)
                    continue;

                int valOff = i + 2;
                if (!NearAny(prizeOffsets, valOff, MaxGapToPrizeSet))
                    continue;

                sites.Add(new BlitzballRollThresholdSite
                {
                    ValueOffset = valOff,
                    Threshold = (ushort)(s[valOff] | (s[valOff + 1] << 8)),
                    IsUpperBound = le,
                });
            }

            sites.Sort((a, b) => a.ValueOffset.CompareTo(b.ValueOffset));
            return sites;
        }

        /// <summary>
        /// Offsets of the <c>Common.GetRandomInRange</c> roll calls that open each prize DRAW (one switch =
        /// one roll over a 0..N range that picks a prize from its buckets). Every prize-index / roll-threshold
        /// site belongs to the draw of the nearest <em>preceding</em> start (see <see cref="DrawStartFor"/>).
        /// This is the RE-grounded grouping unit behind the editor's League/Tournament → placement → draw tree:
        /// a placement (e.g. "League 2nd place") repeats across several draws as the prize escalates with league
        /// progression. Read-only — it does not touch the writer. On the real bltz0200.ebp this finds the 68
        /// prize draws (every one of the 459 prize sites maps to a draw). Offsets are chunk-0-relative, ascending.
        /// </summary>
        public static List<int> FindRollSwitchStarts(Event_File ev)
        {
            ArgumentNullException.ThrowIfNull(ev);
            ReadOnlySpan<byte> s = ev.ScriptChunkBytes;
            var starts = new List<int>();
            for (int i = 0; i + 3 <= s.Length; i++)
                if (s[i] == OpCall && (ushort)(s[i + 1] | (s[i + 2] << 8)) == FuncGetRandomInRange)
                    starts.Add(i); // scan is left-to-right, so the list is already ascending
            return starts;
        }

        /// <summary>The draw-start offset that owns the site at <paramref name="valueOffset"/> — the nearest
        /// roll-switch start at or before it — or -1 if the site precedes the first roll (an unrolled / default
        /// assignment). <paramref name="ascendingStarts"/> must be sorted (as returned by
        /// <see cref="FindRollSwitchStarts"/>).</summary>
        public static int DrawStartFor(IReadOnlyList<int> ascendingStarts, int valueOffset)
        {
            ArgumentNullException.ThrowIfNull(ascendingStarts);
            int lo = 0, hi = ascendingStarts.Count - 1, ans = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (ascendingStarts[mid] <= valueOffset) { ans = ascendingStarts[mid]; lo = mid + 1; }
                else hi = mid - 1;
            }
            return ans;
        }

        static bool NearAny(List<int> sortedOffsets, int offset, int maxGap)
        {
            if (sortedOffsets.Count == 0)
                return false;
            int idx = sortedOffsets.BinarySearch(offset);
            if (idx >= 0)
                return true;
            idx = ~idx;
            if (idx < sortedOffsets.Count && sortedOffsets[idx] - offset <= maxGap)
                return true;
            if (idx > 0 && offset - sortedOffsets[idx - 1] <= maxGap)
                return true;
            return false;
        }

        /// <summary>Patch the prize index at a known site offset (2-byte immediate, length-preserving).</summary>
        public static void SetPrizeIndexAt(Event_File ev, int valueOffset, ushort newPrizeIndex)
        {
            ArgumentNullException.ThrowIfNull(ev);
            ev.PatchScriptUInt16(valueOffset, newPrizeIndex);
        }

        /// <summary>Patch a roll-threshold (ODDS) immediate at a known site offset (2-byte, length-preserving).</summary>
        public static void SetThresholdAt(Event_File ev, int valueOffset, ushort newThreshold)
        {
            ArgumentNullException.ThrowIfNull(ev);
            ev.PatchScriptUInt16(valueOffset, newThreshold);
        }

        public static string VarLabel(BlitzballPrizeVar v) => v switch
        {
            BlitzballPrizeVar.LeagueStandings => "League Standings",
            BlitzballPrizeVar.TournamentStandings => "Tournament Standings",
            BlitzballPrizeVar.LeagueTopScorer => "League Top Scorer",
            BlitzballPrizeVar.TournamentTopScorer => "Tournament Top Scorer",
            _ => $"Var 0x{(byte)v:X2}",
        };

        /// <summary>Place label for a standings slot (0=1st, 1=2nd, 2=3rd); top-scorer scalars have no slot.</summary>
        public static string SlotLabel(int slot) => slot switch
        {
            0 => "1st place",
            1 => "2nd place",
            2 => "3rd place",
            -1 => "top scorer",
            _ => $"slot {slot}",
        };

        /// <summary>The competition a prize var belongs to (League vs Tournament) — the top level of the
        /// editor's prize tree. League = standings 0x30 + top-scorer 0x32; Tournament = 0x31 + 0x33.</summary>
        public static string CompetitionLabel(BlitzballPrizeVar v) => v switch
        {
            BlitzballPrizeVar.LeagueStandings or BlitzballPrizeVar.LeagueTopScorer => "League",
            BlitzballPrizeVar.TournamentStandings or BlitzballPrizeVar.TournamentTopScorer => "Tournament",
            _ => $"Var 0x{(byte)v:X2}",
        };

        /// <summary>The award within a competition (the middle level of the prize tree): 1st/2nd/3rd place for
        /// the standings arrays, or "Top Scorer" for the scalar vars (awarded to the team with the top scorer,
        /// independent of final placement).</summary>
        public static string AwardLabel(BlitzballPrizeVar v, int slot)
            => IsScalarVar((byte)v) ? "Top Scorer" : SlotLabel(slot);
    }
}
