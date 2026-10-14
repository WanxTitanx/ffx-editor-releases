using System;
using System.Collections.Generic;
using FFXProjectEditor.FfxLib.Event;

namespace FFXProjectEditor.FfxLib.Blitzball
{
    /// <summary>One blitzball recruitment site inside a field event's ATEL script: the player-id immediate
    /// that decides WHO is recruited there. <see cref="ScriptOffset"/> is chunk-0-relative.</summary>
    public sealed class BlitzballRecruitSite
    {
        public required int ScriptOffset { get; init; }
        public required byte PlayerId { get; init; }
    }

    /// <summary>
    /// READ + edit blitzball RECRUITMENT inside a field event <c>.ebp</c>. Recruiting a free agent is an ATEL
    /// write that APPENDS the player to YOUR team (Besaid Aurochs, slots 40..47):
    /// <c>BlitzballTeamPlayers[BlitzballTeamPlayerCount[Aurochs] + 40] = &lt;playerId&gt;</c>. In bytecode the tail is
    /// <c>AE 28 00 14 AE &lt;playerId&gt; 00 A3</c> = <c>PUSHII 40 · ADD · PUSHII &lt;playerId&gt; · write-array</c>. The recruited
    /// player is a 1-byte immediate, so changing who is recruited where is a length-preserving GAME-FILE edit
    /// (no EXE patch). Each player usually has 2 sites (two code branches) — <see cref="SetRecruit"/> patches both.
    /// PROVEN vs the real events (guad0000 = Giera/Auda/Nav, lchb0000 = Biggs/Wedge, hiku0500 = Al Bhed Psyches).
    /// See docs/reverse/FFX_BLITZBALL_ENGINE_IDA_SCOUT_2026-06-10.md (Recrutamento).
    /// </summary>
    public static class BlitzballRecruit_File
    {
        // PUSHII 0x28(40) · ADD(0x14) · PUSHII(0xAE) ... then <id> 00 A3(write-array)
        static readonly byte[] SigHead = { 0xAE, 0x28, 0x00, 0x14, 0xAE };
        const byte WriteArrayOp = 0xA3;

        /// <summary>Scan the event's ATEL script for every recruitment site (the player-id immediates).</summary>
        public static List<BlitzballRecruitSite> FindSites(Event_File ev)
        {
            ArgumentNullException.ThrowIfNull(ev);
            ReadOnlySpan<byte> s = ev.ScriptChunkBytes;
            var sites = new List<BlitzballRecruitSite>();
            for (int i = 0; i + 8 <= s.Length; i++)
            {
                if (s[i] == SigHead[0] && s[i + 1] == SigHead[1] && s[i + 2] == SigHead[2]
                    && s[i + 3] == SigHead[3] && s[i + 4] == SigHead[4]
                    && s[i + 6] == 0x00 && s[i + 7] == WriteArrayOp)
                {
                    sites.Add(new BlitzballRecruitSite { ScriptOffset = i + 5, PlayerId = s[i + 5] });
                }
            }
            return sites;
        }

        /// <summary>Change every recruit site currently equal to <paramref name="oldPlayerId"/> to
        /// <paramref name="newPlayerId"/> (covers both code branches). Returns the number of bytes patched.</summary>
        public static int SetRecruit(Event_File ev, byte oldPlayerId, byte newPlayerId)
        {
            ArgumentNullException.ThrowIfNull(ev);
            int patched = 0;
            foreach (BlitzballRecruitSite site in FindSites(ev))
            {
                if (site.PlayerId == oldPlayerId)
                {
                    ev.PatchScriptByte(site.ScriptOffset, newPlayerId);
                    patched++;
                }
            }
            return patched;
        }

        /// <summary>Patch a single recruit site (by chunk-0 offset) to a new player id.</summary>
        public static void SetRecruitAt(Event_File ev, int scriptOffset, byte newPlayerId)
        {
            ArgumentNullException.ThrowIfNull(ev);
            ev.PatchScriptByte(scriptOffset, newPlayerId);
        }
    }
}
