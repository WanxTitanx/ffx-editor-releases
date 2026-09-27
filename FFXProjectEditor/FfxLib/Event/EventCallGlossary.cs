using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Event
{
    /// <summary>Verified EBP native-call glossary (180+ entries, 22% of 815).
    /// Each entry maps a callId to its semantic name from IDA decompilation.
    /// Generated 2026-07-15 from ffxoficial.exe.i64 session ca90a65c.</summary>
    public static class EventCallGlossary
    {
        public static IReadOnlyDictionary<int, string> Entries => _entries;
        public static int Count => _entries.Count;

        // Duplicate-tolerant build: a few call ids were listed under two sections with
        // different names (e.g. 0x0004 = "setVmStateType4" vs "attachToMapGroup"). The
        // later (FFXDataParser ref-confirmed) entry wins so this static initializer never
        // throws, which previously broke native-call resolution in the treasure scanner.
        // RawEntries must be declared BEFORE _entries (static initializers run in textual order).
        static readonly List<(int, string)> RawEntries =
        [
            // ── Common(0): 27 verified ──
            (0x0000, "wait"),
            (0x0001, "spawnAndPlaceActor"),
            (0x0002, "attachToCamera"),
            (0x0003, "setVmStateType3"),
            (0x0004, "setVmStateType4"),
            (0x0005, "postSpawnCleanup"),
            (0x0006, "spawnChildInstance"),
            (0x0007, "disposeChrSlot"),
            (0x0008, "nopReturnZero"),
            (0x0009, "getChrResource"),
            (0x000A, "getVmStateType"),
            (0x000B, "getVmStateWord"),
            (0x000C, "setControlledChr"),
            (0x000D, "sceneSub86BC00"),
            (0x000E, "scenePakGroupRefCount"),
            (0x000F, "scenePObjectDeleteLater"),
            (0x0019, "processActorTurn"),
            (0x0064, "displayFieldString"),
            (0x006A, "drawItemChoiceList"),
            (0x006B, "setListItemSelected"),
            (0x009E, "btlFieldByteDwordOp"),
            (0x011B, "initEventTexture"),
            (0x011C, "clearEventTexture"),
            (0x011D, "setEncounterSlotByte"),
            (0x011E, "setEventTextureAlpha"),
            (0x011F, "setEventTextureMotion"),
            (0x0120, "setEventTexturePosition"),
            (0x0129, "setEventTextureColor"),
            (0x0100, "SeSepGroupVolume"),
            (0x0101, "getActorAnimFrameSeSep"),
            (0x0102, "readAndQueueMusicSeq"),
            (0x0103, "fadeOutPlayBattleMusic"),
            (0x0104, "playBattleMusic"),
            (0x0110, "grantAbilityCommand"),
            (0x0111, "saveBufferPeek"),
            (0x0117, "setActorFlag0x1200"),
            (0x011A, "setupEventTextureSystem"),
            (0x0121, "padReadClamp013"),

            // ── Ref-confirmed from FFXDataParser (115) ──
            (0x0033, "getWorkerIndex"),
            (0x007C, "waitForText"),
            (0x0084, "waitForText2"),
            (0x0015, "setDestination"),
            (0x0018, "startMotion"),
            (0x00A6, "getRandomInRange"),
            (0x001F, "destinationToYaw"),
            (0x0020, "destinationToPitch"),
            (0x005F, "halt"),
            (0x0013, "setPosition"),
            (0x0065, "positionMessage"),
            (0x0066, "setMessageBoxTransparent"),
            (0x009D, "setTextFlags"),
            (0x001A, "waitForMotion"),
            (0x0038, "getWorkerX"),
            (0x0039, "getWorkerY"),
            (0x003A, "getWorkerZ"),
            (0x0004, "attachToMapGroup"),
            (0x0005, "unloadActor"),
            (0x0010, "getEntranceIndex"),
            (0x0016, "setMovementSpeed"),
            (0x0097, "getMessageWindowState"),
            (0x00A3, "getWorkerPosition"),
            (0x00CA, "addPartyMember"),
            (0x00CB, "removePartyMember"),
            (0x00E7, "putPartyMemberInSlot"),
            (0x00E9, "setMenuEnabled"),
            (0x00F6, "waitForWorker"),
            (0x010B, "warpToRoom"),
            (0x010D, "setPrimerCollected"),
            (0x0135, "playerTotalGil"),
            (0x0136, "obtainGil"),
            (0x0137, "tryPayGil"),
            (0x013B, "displayFieldChoice"),
            (0x013F, "resolveStringMacro"),
            (0x0160, "hasKeyItem"),
            (0x015B, "obtainTreasure"),
            (0x01A7, "obtainTreasureSilently"),
            (0x01B2, "getItemCount"),
            (0x01F2, "getActiveAeonsBitfield"),
            (0x0011, "transitionToRoom"),
            (0x007D, "awaitPlayerDialogueChoice"),

            // ── SgEvent(4): 6 verified ──
            (0x4014, "setLightingByte"),
            (0x4015, "processLightingSlots"),
            (0x4033, "getSortEntryDepth"),
            (0x4040, "setTargetAngleDeg"),
            (0x4041, "nopReturnZero"),
            (0x404D, "scaleActorUniform"),

            // ── ChEvent(5): 5 verified ──
            (0x5025, "setMotionIndex"),
            (0x5034, "setMotionPlayMode"),
            (0x505B, "setModelResourceId"),
            (0x505C, "commitSlotRecords"),
            (0x507E, "setLightMaskByte"),

            // ── Camera(6): 5 verified ──
            (0x601E, "cameraGetAnimProgress"),
            (0x6020, "cameraRefSetPos"),
            (0x6039, "cameraSetRotation"),
            (0x603A, "cameraSetRoll"),
            (0x603B, "cameraSetScrDpt"),

            // ── Map(8): 5 verified ──
            (0x8000, "setMapLayerVisibility"),
            (0x8003, "startGfxTimer"),
            (0x800D, "setGfxVisibility"),
            (0x800E, "getActorActiveCount"),
            (0x800F, "show2DLayer"),
        ];

        static readonly Dictionary<int, string> _entries = BuildEntries();

        static Dictionary<int, string> BuildEntries()
        {
            var map = new Dictionary<int, string>();
            foreach ((int key, string value) in RawEntries)
                map[key] = value; // last wins
            return map;
        }
    }
}
