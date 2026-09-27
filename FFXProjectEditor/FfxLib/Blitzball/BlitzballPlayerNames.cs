using FFXProjectEditor.FfxLib.Text;

namespace FFXProjectEditor.FfxLib.Blitzball
{
    /// <summary>
    /// Maps a blitzball player index to its display NAME, which lives in the menu macro dictionary
    /// (<c>menu/macrodic.dcp</c>) as macro id <c>0x700 + playerIndex</c> = chunk <c>7</c>, entry <c>playerIndex</c>
    /// (the same `MACRO[0x700+idx]` lookup the game/ATEL uses — proven by decoding the real file:
    /// 0x702="Datto", 0x703="Letty", 0x704="Jassu", 0x705="Botta", 0x706="Keepa", 0x707="Bickson", …).
    /// Entries 0/1 (0x700/0x701) are control-code refs to the renameable lead characters (Tidus/Wakka),
    /// so their blitzball name follows the player-entered character name, not a literal macro string.
    ///
    /// These names are ALREADY editable today via the <c>MacroExplorer</c> module (the macro dictionary
    /// writer: SafeWriter + undo/validation + structural round-trip). This helper just resolves the
    /// index ↔ macro-id link so the BlitzballRosterEditor can surface the name next to each player's stats.
    /// Names are stored in a SEPARATE game file from the stats (macrodic.dcp ≠ bltz0002.ebp).
    /// See docs/reverse/FFX_BLITZBALL_ROSTER_BASE_RE_2026-06-10.md §3.
    /// </summary>
    public static class BlitzballPlayerNames
    {
        /// <summary>Macro dictionary chunk that holds the blitzball name pool.</summary>
        public const int MacroChunkIndex = 7;

        /// <summary>Macro id of player 0's name (chunk 7, entry 0).</summary>
        public const int MacroBaseId = 0x700;

        /// <summary>Macro id for a player's name = <c>0x700 + playerIndex</c>.</summary>
        public static int MacroIdForPlayer(int playerIndex) => MacroBaseId + playerIndex;

        /// <summary>Entries 0/1 reference the renameable lead characters (their name is player-entered).</summary>
        public static bool IsDynamicCharacterName(int playerIndex) => playerIndex <= 1;

        /// <summary>Resolve a player's current display name from a loaded macro dictionary, or null if absent.</summary>
        public static string? TryGetName(MacroDictionary_File dictionary, int playerIndex)
        {
            if (dictionary == null)
                return null;

            foreach (MacroDictionary_Chunk chunk in dictionary.Chunks)
            {
                if (chunk.Index != MacroChunkIndex)
                    continue;
                foreach (MacroDictionary_Entry entry in chunk.Entries)
                {
                    if (entry.EntryIndex == playerIndex)
                        return entry.RegularText;
                }
            }
            return null;
        }
    }
}
