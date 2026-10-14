// ============================================================================
// FfxSaveCharacterFieldCatalog — character-tab field catalogs (FFXED v0.749 mirror)
// PURPOSE : static catalogs used by the character editor: party-slot members, activation states, the 20
//           overdrive-mode bit+counter tuples, and ability/overdrive/special-ability registry field groups.
// WHY     : these are UI-preserving lookups over the save registry bit fields; the special-ability set is
//           the filter that separates overdrive modes from aeon "special" abilities (both at 22090/15788..).
// EVIDENCE: mirrored from FFXED v0.749 FFXED.h(); offsets verified against real saves.
// MAINT   : by-byte tuple triples (bit offset, bit index, count offset) are layout-derived — extend the
//           overdrive list with both bit AND counter offsets. Nobody = -1 maps to "no party member".
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Save
{
    /// <summary>
    /// Character-tab field groups mirrored from FFXED v0.749 <c>FFXED.h()</c>.
    /// </summary>
    public static class FfxSaveCharacterFieldCatalog
    {
        public const int PartySlotOffsetBase = 15768;
        public const int PartySlotUiCount = 7;

        public static readonly FfxSaveCatalogEntry[] PartyMemberCatalog =
        {
            new() { Label = "Tidus", Bytes = new[] { 0 } },
            new() { Label = "Yuna", Bytes = new[] { 1 } },
            new() { Label = "Auron", Bytes = new[] { 2 } },
            new() { Label = "Kimahri", Bytes = new[] { 3 } },
            new() { Label = "Wakka", Bytes = new[] { 4 } },
            new() { Label = "Lulu", Bytes = new[] { 5 } },
            new() { Label = "Rikku", Bytes = new[] { 6 } },
            new() { Label = "Seymour", Bytes = new[] { 7 } },
            new() { Label = "Valefor", Bytes = new[] { 8 } },
            new() { Label = "Ifrit", Bytes = new[] { 9 } },
            new() { Label = "Ixion", Bytes = new[] { 10 } },
            new() { Label = "Shiva", Bytes = new[] { 11 } },
            new() { Label = "Bahamut", Bytes = new[] { 12 } },
            new() { Label = "Anima", Bytes = new[] { 13 } },
            new() { Label = "Yojimbo", Bytes = new[] { 14 } },
            new() { Label = "Cindy", Bytes = new[] { 15 } },
            new() { Label = "Sandy", Bytes = new[] { 16 } },
            new() { Label = "Mindy", Bytes = new[] { 17 } },
            new() { Label = "Nobody", Bytes = new[] { -1 } },
        };

        public static readonly FfxSaveCatalogEntry[] ActivationCatalog =
        {
            new() { Label = "Enabled", Bytes = new[] { 17 } },
            new() { Label = "Disabled", Bytes = new[] { 16 } },
            new() { Label = "Inactivated", Bytes = new[] { 0 } },
        };

        public static readonly (string Label, int BitOffset, int Bit, int CountOffset)[] OverdriveModeFields =
        {
            ("Stoic", 22164, 2, 22128),
            ("Warrior", 22164, 0, 22124),
            ("Comrade", 22164, 1, 22126),
            ("Healer", 22164, 3, 22130),
            ("Tactician", 22164, 4, 22132),
            ("Victim", 22164, 5, 22134),
            ("Dancer", 22164, 6, 22136),
            ("Avenger", 22164, 7, 22138),
            ("Slayer", 22165, 0, 22140),
            ("Hero", 22165, 1, 22142),
            ("Rook", 22165, 2, 22144),
            ("Victor", 22165, 3, 22146),
            ("Coward", 22165, 4, 22148),
            ("Ally", 22165, 5, 22150),
            ("Sufferer", 22165, 6, 22152),
            ("Daredevil", 22165, 7, 22154),
            ("Loner", 22166, 0, 22156),
            ("-", 22166, 1, 22158),
            ("-", 22166, 2, 22160),
            ("Aeons Only", 22166, 3, 22162),
        };

        static readonly HashSet<string> SpecialAbilityLabels = new(StringComparer.Ordinal)
        {
            "Summon", "Attack (Valefor)", "Sonic Wings", "Attack (Ifrit)", "Meteor Strike", "Attack (Ixion)",
            "Aerospark", "Attack (Shiva)", "Heavenly Strike", "Attack (Bahamut)", "Impulse", "Attack (Anima)",
            "Pain", "Pay", "Dismiss (Yojimbo)", "Daigoro", "Kozuka", "Zanmato", "Camisade", "Razzia", "Passado",
            "Auto-Life", "Stand by", "Struggle", "Talk",
        };

        public static IEnumerable<FfxSaveRegistryField> AbilityFields =>
            FfxSaveRegistry.Root.BitFields.Where(f => f.Offset == 22090);

        public static IEnumerable<FfxSaveRegistryField> OverdriveFields =>
            FfxSaveRegistry.Root.BitFields.Where(f =>
                f.Offset >= 15788 && f.Offset <= 15805 && !SpecialAbilityLabels.Contains(f.Label));

        public static IEnumerable<FfxSaveRegistryField> SpecialAbilityFields =>
            FfxSaveRegistry.Root.BitFields.Where(f => SpecialAbilityLabels.Contains(f.Label));
    }
}
