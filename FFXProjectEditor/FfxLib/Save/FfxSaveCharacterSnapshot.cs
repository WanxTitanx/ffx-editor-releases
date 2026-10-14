// ============================================================================
// FfxSaveCharacterSnapshot — read/write snapshot of one character/aeon block in the save
// PURPOSE : typed view of a character's stats, OD, sphere level, name and affection (index < 7 only), read
//           and written via FfxSaveCharacterOffsets.ForCharacter.
// WHY     : each character lives at base + 148*index inside the save; aeon members (Valefor..Mindy) share the
//           layout. Affection is stored as an int[?] starting at 108 for the 7 lead characters only.
// EVIDENCE: FFXED v0.749 offsets; cross-checked against real ffx_000 saves.
// MAINT   : Read()/Write() must stay symmetric field-for-field; adding/removing a stat touches BOTH. Do NOT
//           shift the character base without re-validating a save.
// ============================================================================
namespace FFXProjectEditor.FfxLib.Save
{
    public sealed class FfxSaveCharacterSnapshot
    {
        public int Index { get; init; }
        public string Label => FfxSaveCharacterOffsets.CharacterNames[Index];

        public int Activation { get; set; }
        public int BaseHp { get; set; }
        public int BaseMp { get; set; }
        public int BaseStrength { get; set; }
        public int BaseDefense { get; set; }
        public int BaseMagic { get; set; }
        public int BaseMagicDefense { get; set; }
        public int BaseAgility { get; set; }
        public int BaseLuck { get; set; }
        public int BaseEvasion { get; set; }
        public int BaseAccuracy { get; set; }
        public int CurrentHp { get; set; }
        public int CurrentMp { get; set; }
        public int AbilityPoints { get; set; }
        public int SphereLevel { get; set; }
        public int OverdriveGauge { get; set; }
        public int OverdriveMode { get; set; }
        public int EnemiesDefeated { get; set; }
        public int PoisonDamagePercent { get; set; }
        public int Affection { get; set; }
        public string Name { get; set; } = string.Empty;

        public static FfxSaveCharacterSnapshot Read(FfxSaveCore core, int index)
        {
            var snap = new FfxSaveCharacterSnapshot
            {
                Index = index,
                Activation = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.Activation, index), 1),
                BaseHp = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseHp, index), 4),
                BaseMp = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseMp, index), 4),
                BaseStrength = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseStrength, index), 1),
                BaseDefense = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseDefense, index), 1),
                BaseMagic = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseMagic, index), 1),
                BaseMagicDefense = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseMagicDefense, index), 1),
                BaseAgility = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseAgility, index), 1),
                BaseLuck = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseLuck, index), 1),
                BaseEvasion = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseEvasion, index), 1),
                BaseAccuracy = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseAccuracy, index), 1),
                CurrentHp = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.CurrentHp, index), 4),
                CurrentMp = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.CurrentMp, index), 4),
                AbilityPoints = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.AbilityPoints, index), 4),
                SphereLevel = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.SphereLevel, index), 1),
                OverdriveGauge = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.OverdriveGauge, index), 1),
                OverdriveMode = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.OverdriveMode, index), 1),
                EnemiesDefeated = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.EnemiesDefeated, index), 4),
                PoisonDamagePercent = core.ReadInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.PoisonDamagePercent, index), 1),
                Name = core.ReadFfxString(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.CharacterName, index)),
            };

            if (index < 7)
                snap.Affection = core.ReadInt32Le(FfxSaveCharacterOffsets.AffectionBase + (index << 2), 4);

            return snap;
        }

        public void Write(FfxSaveCore core)
        {
            int index = Index;
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.Activation, index), Activation, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseHp, index), BaseHp, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseMp, index), BaseMp, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseStrength, index), BaseStrength, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseDefense, index), BaseDefense, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseMagic, index), BaseMagic, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseMagicDefense, index), BaseMagicDefense, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseAgility, index), BaseAgility, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseLuck, index), BaseLuck, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseEvasion, index), BaseEvasion, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.BaseAccuracy, index), BaseAccuracy, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.CurrentHp, index), CurrentHp, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.CurrentMp, index), CurrentMp, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.AbilityPoints, index), AbilityPoints, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.SphereLevel, index), SphereLevel, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.OverdriveGauge, index), OverdriveGauge, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.OverdriveMode, index), OverdriveMode, 1);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.EnemiesDefeated, index), EnemiesDefeated, 4);
            core.WriteInt32Le(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.PoisonDamagePercent, index), PoisonDamagePercent, 1);
            core.WriteFfxString(FfxSaveCharacterOffsets.ForCharacter(FfxSaveCharacterOffsets.CharacterName, index), Name);

            if (index < 7)
                core.WriteInt32Le(FfxSaveCharacterOffsets.AffectionBase + (index << 2), Affection, 4);
        }
    }
}
