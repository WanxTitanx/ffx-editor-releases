// ============================================================================
// FfxSaveCharacterOffsets — per-character field bases in the 25848-byte save
// PURPOSE : field bases + stride to address any character/aeon stat block (final = base + 148*idx).
// WHY     : char data is a fixed-stride array; aeon party members (Valefor..Mindy) share the layout.
// EVIDENCE: FFXED v0.749 offsets; cross-checked against real ffx_000 save slots.
// MAINT   : offsets are HARD-CODED to the vanilla layout — do not shift without re-validating a save.
// ============================================================================
namespace FFXProjectEditor.FfxLib.Save
{
    /// <summary>
    /// Per-character field bases inside the 25848-byte save (FFXED v0.749).
    /// Final offset = base + <see cref="CharacterStride"/> * characterIndex.
    /// </summary>
    public static class FfxSaveCharacterOffsets
    {
        public const int CharacterStride = 148;

        public static readonly string[] CharacterNames =
        {
            "Tidus", "Yuna", "Auron", "Kimahri", "Wakka", "Lulu", "Rikku", "Seymour",
            "Valefor", "Ifrit", "Ixion", "Shiva", "Bahamut", "Anima", "Yojimbo",
            "Cindy", "Sandy", "Mindy",
        };

        public const int Activation = 22072;
        public const int BaseHp = 22032;
        public const int BaseMp = 22036;
        public const int BaseStrength = 22040;
        public const int BaseDefense = 22041;
        public const int BaseMagic = 22042;
        public const int BaseMagicDefense = 22043;
        public const int BaseAgility = 22044;
        public const int BaseLuck = 22045;
        public const int BaseEvasion = 22046;
        public const int BaseAccuracy = 22047;
        public const int AbilityPoints = 22052;
        public const int CurrentHp = 22056;
        public const int CurrentMp = 22060;
        public const int PoisonDamagePercent = 22083;
        public const int OverdriveMode = 22084;
        public const int OverdriveGauge = 22085;
        public const int SphereLevel = 22087;
        public const int EnemiesDefeated = 22112;
        public const int CharacterName = 25484;
        public const int AffectionBase = 108;

        public static int ForCharacter(int fieldBase, int characterIndex) =>
            fieldBase + CharacterStride * characterIndex;
    }
}
