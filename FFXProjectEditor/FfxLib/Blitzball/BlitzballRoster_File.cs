using System;
using System.Collections.Generic;
using FFXProjectEditor.FfxLib.Event;

namespace FFXProjectEditor.FfxLib.Blitzball
{
    /// <summary>
    /// One (player, stat) blitzball growth descriptor, decoded from the bltz0002 ATEL eventData floats.
    /// stat(Lv) per <see cref="GrowthType"/>:
    /// <list type="bullet">
    /// <item>-1 =&gt; 1 (constant)</item>
    /// <item> 0 =&gt; A + B*Lv (linear)</item>
    /// <item> 1 =&gt; A + B*Lv^C (power)</item>
    /// <item> 2 =&gt; A + B*Lv - C*Lv^2 (down-parabola)</item>
    /// <item> 3 =&gt; A + B*Lv + C*Lv^2 (up-parabola)</item>
    /// </list>
    /// See docs/reverse/FFX_BLITZBALL_ROSTER_BASE_RE_2026-06-10.md.
    /// </summary>
    public sealed class BlitzballStatGrowth
    {
        public float A { get; set; }
        public float B { get; set; }
        public float C { get; set; }
        public float GrowthType { get; set; }
    }

    /// <summary>
    /// READ/WRITE the 60-player blitzball base stat-growth tables that live INSIDE the game file
    /// <c>bltz0002.ebp</c> as ATEL eventData variables 0x126..0x12E (the ONLY blitzball data physically
    /// stored in a game file; names/positions/techs are elsewhere — see the RE doc). The writer patches
    /// the floats in place via <see cref="Event_File.PatchEventDataElement"/> (length-preserving) and
    /// re-emits the container with <see cref="Event_File.Write"/>. Gated by <c>--blitzball-roster-rt0</c>.
    /// </summary>
    public static class BlitzballRoster_File
    {
        public const string EventId = "bltz0002";
        public const int PlayerCount = 60;             // ids 0x00..0x3B; 0x3C = <Empty> sentinel
        public const int FieldsPerStat = 4;            // a, b, c, growthType (all float)

        // Stat var ids in display order: HP, SP, AT, EN, PA, SH, BL, CA. (var 0x127 is a non-stat gap.)
        public static readonly IReadOnlyList<(int VarId, string Name)> Stats = new[]
        {
            (0x126, "HP"), (0x12E, "SP"), (0x128, "AT"), (0x129, "EN"),
            (0x12A, "PA"), (0x12B, "SH"), (0x12C, "BL"), (0x12D, "CA"),
        };

        /// <summary>Decode every (player, stat) growth descriptor from the event's eventData float arrays.</summary>
        public static BlitzballStatGrowth[,] ReadAll(Event_File ev)
        {
            ArgumentNullException.ThrowIfNull(ev);
            var grid = new BlitzballStatGrowth[PlayerCount, Stats.Count];
            for (int s = 0; s < Stats.Count; s++)
            {
                int varId = Stats[s].VarId;
                for (int p = 0; p < PlayerCount; p++)
                {
                    grid[p, s] = new BlitzballStatGrowth
                    {
                        A = ReadFloat(ev, varId, p * FieldsPerStat + 0),
                        B = ReadFloat(ev, varId, p * FieldsPerStat + 1),
                        C = ReadFloat(ev, varId, p * FieldsPerStat + 2),
                        GrowthType = ReadFloat(ev, varId, p * FieldsPerStat + 3),
                    };
                }
            }
            return grid;
        }

        static float ReadFloat(Event_File ev, int varId, int idx)
            => BitConverter.ToSingle(ev.ReadEventDataElement(varId, idx), 0);

        /// <summary>Patch a single growth field (length-preserving) into the event's script-chunk override.
        /// field: 0=A, 1=B, 2=C, 3=GrowthType.</summary>
        public static void PatchField(Event_File ev, int playerIndex, int statIndex, int field, float value)
        {
            ArgumentNullException.ThrowIfNull(ev);
            int varId = Stats[statIndex].VarId;
            ev.PatchEventDataElement(varId, playerIndex * FieldsPerStat + field, BitConverter.GetBytes(value));
        }

        /// <summary>Apply an edited grid against a freshly-read event and return the repacked <c>.ebp</c> bytes.
        /// Only cells whose float bits differ from the on-disk values are patched (a no-edit save is byte-identical).</summary>
        public static byte[] WriteAll(Event_File ev, BlitzballStatGrowth[,] edited)
        {
            ArgumentNullException.ThrowIfNull(ev);
            ArgumentNullException.ThrowIfNull(edited);
            BlitzballStatGrowth[,] orig = ReadAll(ev);
            for (int s = 0; s < Stats.Count; s++)
                for (int p = 0; p < PlayerCount; p++)
                {
                    PatchIfChanged(ev, p, s, 0, orig[p, s].A, edited[p, s].A);
                    PatchIfChanged(ev, p, s, 1, orig[p, s].B, edited[p, s].B);
                    PatchIfChanged(ev, p, s, 2, orig[p, s].C, edited[p, s].C);
                    PatchIfChanged(ev, p, s, 3, orig[p, s].GrowthType, edited[p, s].GrowthType);
                }
            return ev.Write();
        }

        static void PatchIfChanged(Event_File ev, int p, int s, int field, float oldV, float newV)
        {
            if (BitConverter.SingleToInt32Bits(oldV) != BitConverter.SingleToInt32Bits(newV))
                PatchField(ev, p, s, field, newV);
        }
    }
}
