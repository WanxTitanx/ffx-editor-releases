// ============================================================================
// FfxSaveBlitzball — Blitzball player/league/tournament state in the save
// PURPOSE : reads/writes per-player fields (technique capacity/level/exp/contract/salary/goals/5 techniques)
//           for the 60 players, with default {in-game} names for display.
// WHY     : player data is a SoA layout of per-field banks, NOT one AoS record:
//             technique slots      5266 + index*5 + slot   (the ONLY per-player AoS record, 5B)
//             technique capacity   5566 + index            (contiguous 60-byte bank)
//             level                5626 + index
//             contract             5974 + index
//             experience u16       6036 + index*2
//             league goals u16     6356 + index*2
//             tournament goals u16 6476 + index*2
//             salary u16           6596 + index*2
// EVIDENCE: FFXED v0.749 FFXED.java: each player widget carries stride array {i, i<<1, i<<2, i*5}
//           and each field picks one via d(1)=i / d(2)=i*2 / d(4)=i*5 — capacity uses d(1) (contiguous),
//           techniques use d(4) (AoS 5-byte record). Independently proven by IDA:
//           memset(&g_BlitzTechCapacityArr, 5, 0x3C) @ FFX_Encounter_InitStatusEffectBuffers 0x784660 —
//           60 contiguous bytes. (The old stride-5 capacity read collided with Level[0] at index 12.)
// MAINT   : read/write MUST stay symmetric with the offsets above; add/remove a technique field on BOTH sides.
// ============================================================================
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Save
{
    public static class FfxSaveBlitzball
    {
        public const int PlayerCount = 60;
        public const int TechniqueRecordStride = 5;

        // Offset of technique slot `fieldIndex` (0..4) for `playerIndex` — the only per-player
        // AoS record (5 consecutive bytes at 5266 + index*5). All other player fields live in
        // contiguous or *2-stride banks; do NOT reuse this helper for them.
        public static int PlayerFieldOffset(int playerIndex, int fieldIndex) =>
            5266 + playerIndex * TechniqueRecordStride + fieldIndex;
    }

    public sealed class FfxSaveBlitzballPlayerSnapshot
    {
        public int Index { get; init; }
        public string Label { get; init; } = string.Empty;
        public int TechniqueCapacity { get; set; }
        public int Level { get; set; }
        public int Experience { get; set; }
        public int ContractDuration { get; set; }
        public int Salary { get; set; }
        public int LeagueGoals { get; set; }
        public int TournamentGoals { get; set; }
        public int Technique1 { get; set; }
        public int Technique2 { get; set; }
        public int Technique3 { get; set; }
        public int Technique4 { get; set; }
        public int Technique5 { get; set; }

        static readonly string[] DefaultNames =
        [
            "Tidus", "Wakka", "Datto", "Letty", "Jassu", "Botta", "Keepa", "Bickson", "Abus", "Graav",
            "Doram", "Balgerda", "Raudy", "Larbeight", "Isken", "Vuroja", "Kulukan", "Deim", "Nizarut", "Eigaar",
            "Blappa", "Berrik", "Judda", "Lakkam", "Nimrook", "Basik Ronso", "Argai Ronso", "Gazna Ronso", "Nuvy Ronso", "Irga Ronso",
            "Zamzi Ronso", "Giera Guado", "Zazi Guado", "Nav Guado", "Auda Guado", "Pah Guado", "Noy Guado", "Rin", "Tatts", "Kyou",
            "Shuu", "Nedus", "Biggs", "Wedge", "Ropp", "Linna", "Mep", "Zalitz", "Naida", "Durren",
            "Jumal", "Svanda", "Vilucha", "Shaami", "Zev Ronso", "Yuma Guado", "Kiyuri", "Brother", "Mifurey", "Miyu",
        ];

        public static FfxSaveBlitzballPlayerSnapshot Read(FfxSaveCore core, int index)
        {
            int techOff = FfxSaveBlitzball.PlayerFieldOffset(index, 0);
            return new FfxSaveBlitzballPlayerSnapshot
            {
                Index = index,
                Label = index < DefaultNames.Length ? DefaultNames[index] : $"Player {index}",
                TechniqueCapacity = core.Data[5566 + index],
                Level = core.Data[5626 + index],
                Experience = core.ReadInt32Le(6036 + index * 2, 2),
                ContractDuration = core.Data[5974 + index],
                Salary = core.ReadInt32Le(6596 + index * 2, 2),
                LeagueGoals = core.ReadInt32Le(6356 + index * 2, 2),
                TournamentGoals = core.ReadInt32Le(6476 + index * 2, 2),
                Technique1 = core.Data[techOff],
                Technique2 = core.Data[techOff + 1],
                Technique3 = core.Data[techOff + 2],
                Technique4 = core.Data[techOff + 3],
                Technique5 = core.Data[techOff + 4],
            };
        }

        public void Write(FfxSaveCore core)
        {
            int techOff = FfxSaveBlitzball.PlayerFieldOffset(Index, 0);
            core.Data[5566 + Index] = (byte)TechniqueCapacity;
            core.Data[5626 + Index] = (byte)Level;
            core.WriteInt32Le(6036 + Index * 2, Experience, 2);
            core.Data[5974 + Index] = (byte)ContractDuration;
            core.WriteInt32Le(6596 + Index * 2, Salary, 2);
            core.WriteInt32Le(6356 + Index * 2, LeagueGoals, 2);
            core.WriteInt32Le(6476 + Index * 2, TournamentGoals, 2);
            core.Data[techOff] = (byte)Technique1;
            core.Data[techOff + 1] = (byte)Technique2;
            core.Data[techOff + 2] = (byte)Technique3;
            core.Data[techOff + 3] = (byte)Technique4;
            core.Data[techOff + 4] = (byte)Technique5;
        }
    }
}
