using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ai
{
    // Names for saveData variable slots (ATEL variable storage 0x54).
    // Cross-referenced from Karifean/FFXDataParser ScriptConstants.putSaveDataVariable.
    // The current monster AI corpus declares/references no saveData slots; this is parser-attributed display metadata.
    public static class AiSaveDataVariableNames
    {
        public static readonly IReadOnlyDictionary<ushort, string> Names = new Dictionary<ushort, string>
        {
            [0x008D] = "CalmLandsQuestProgressionFlags",
            [0x0092] = "MushroomRockRoadTreasureFlags",
            [0x00A8] = "WobblyChocoboRecordMinutes",
            [0x00A9] = "WobblyChocoboRecordSeconds",
            [0x00AA] = "WobblyChocoboRecordTenths",
            [0x00AB] = "DodgerChocoboRecordMinutes",
            [0x00AC] = "DodgerChocoboRecordSeconds",
            [0x00AD] = "DodgerChocoboRecordTenths",
            [0x00AE] = "HyperDodgerChocoboRecordMinutes",
            [0x00AF] = "HyperDodgerChocoboRecordSeconds",
            [0x00B0] = "HyperDodgerChocoboRecordTenths",
            [0x00B1] = "CatcherChocoboRecordMinutes",
            [0x00B2] = "CatcherChocoboRecordSeconds",
            [0x00B3] = "CatcherChocoboRecordTenths",
            [0x00CE] = "BikanelTreasureFlags1",
            [0x00CF] = "BikanelTreasureFlags2",
            [0x00D0] = "BikanelTreasureFlags3",
            [0x0104] = "EnergyBlastProgressionFlags",
            [0x0115] = "BesaidVillageTreasureFlags",
            [0x014B] = "ControllableCharacterInLuca",
            [0x0193] = "DebugSkipJechtIntroScenes",
            [0x01C0] = "KilikaForestTreasureFlags",
            [0x01C5] = "BesaidTreasureFlags",
            [0x01CD] = "MacalaniaTreasureFlags",
            [0x01D2] = "OmegaRuinsProgressionFlags",
            [0x01D4] = "HomeProgressionFlags",
            [0x0205] = "ThunderPlainsProgressionFlags",
            [0x0208] = "LightningDodgingRewardsToPickUpFlags",
            [0x0210] = "LightningDodgingTotalBolts",
            [0x0212] = "LightningDodgingTotalDodges",
            [0x0214] = "LightningDodgingHighestConsecutiveDodges",
            [0x024C] = "BlitzballWakkaPowerProgress",
            [0x0A00] = "GameMoment",
            [0x0A34] = "GilLentToOAka",
            [0x0A38] = "MacalaniaPricesChosenForOAka",
            [0x0A4A] = "SaveSphereInstructionsSeen",
            [0x0A60] = "AlBhedPrimersCollectedCount",
            // RE 2026-09-15 (docs/reverse/FFX_G2G3_COUNTERS_RE_2026-09-15.md, confirmed by
            // EV01 writer analysis + 80-real-save cross-check): live twins of the saved
            // header fields @0xBA/@0xB8 plus the fahrenheit-named airship location.
            // Writers are EV01 scripts (Common:0087 TkGetNowEventJumpId, Common:016F,
            // hiku* = airship scenes), not engine natives.
            [0x0A68] = "CurrentEventJumpRoomId",
            [0x0A6C] = "CurrentSpawnPoint",
            [0x0A74] = "CurrentAirshipLocation",
            [0x0A88] = "BlitzballTeamPlayerCount",
            [0x0A93] = "JechtSpheresCollectedCount",
            [0x0A95] = "AirshipDestinationUnlocks",
            [0x0A99] = "CactuarGuardiansBeaten",
            [0x0A9A] = "AlBhedPrimersInstructionsSeen",
            [0x0A9B] = "RemiemRaceTreasureFlags",
            [0x0A9D] = "DarkValeforCompletionFlags",
            [0x0A9E] = "DarkIfritCompletionFlags",
            [0x0A9F] = "DarkIxionCompletionFlags",
            [0x0AA0] = "DarkShivaCompletionFlags",
            [0x0AA1] = "DarkBahamutCompletionFlags",
            [0x0AA2] = "DarkYojimboCompletionFlags",
            [0x0AA3] = "DarkAnimaCompletionFlags",
            [0x0AA4] = "DarkMagusSistersCompletionFlags",
            [0x0AA5] = "PenanceUnlockState",
            [0x141A] = "BlitzballTeamPlayers",
            [0x1465] = "BlitzballEnemyTeam",
            [0x152A] = "BlitzballPlayerContractDurations",
            [0x1798] = "BlitzballPlayerCostPerGame",
            [0x1810] = "BlitzballLeaguePrizeIndex",
            [0x1816] = "BlitzballTournamentPrizeIndex",
            [0x181C] = "BlitzballLeagueTopScorerPrizeIndex",
            [0x181E] = "BlitzballTournamentTopScorerPrizeIndex",
        };

        public static string? Get(int slot) =>
            slot >= 0 && slot <= ushort.MaxValue && Names.TryGetValue((ushort)slot, out string? n) ? n : null;
    }
}
