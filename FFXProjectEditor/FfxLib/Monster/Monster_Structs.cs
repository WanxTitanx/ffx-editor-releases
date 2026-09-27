// ============================================================================
// Monster_Structs — binary layouts for the monster .bin container and stat sheet
// PURPOSE : Xe.BinaryMapper [Data]-annotated structs: MonsterHeaderFile (0x34 header with 7 section pointers)
//           and MonsterStatSheetStruct (name/sensor/scan TSInfo + StatSheet + padding).
// WHY     : the [Data] ordering IS the on-disk layout; offsets here drive Monster_File/StatSheet reads and the
//           byte-level writers (e.g. MonsterCaptureFlagWriter StatSheetPointer@0x0C).
// EVIDENCE: RT0 round-trip; header 9 ints (0x24) + 16 padding = 0x34; sections start at 0x30 (see Monster_File).
// MAINT   : reordering/changing field size breaks the binary layout silently (BinaryMapper is positional) —
//           change with care + RT0 re-proof. Signature comment "Could be a uint count" is still open.
// ============================================================================
using Xe.BinaryMapper;
using static FFXProjectEditor.FfxLib.Common.CommonStructs;

namespace FFXProjectEditor.FfxLib.Monster
{
    public class Monster_Structs
    {
        public class MonsterHeaderFile
        {
            [Data] public int Signature { get; set; } // Could be a uint count
            [Data] public int AiFilePointer { get; set; }
            [Data] public int WorkerFilePointer { get; set; }
            [Data] public int StatSheetPointer { get; set; }
            [Data] public int SpoilsFilePointer { get; set; }
            [Data] public int LootFilePointer { get; set; }
            [Data] public int AudioFilePointer { get; set; }
            [Data] public int TextFilePointer { get; set; }
            [Data] public int FileSize { get; set; } // Size of the whole file
            [Data(Count=16)] public byte[] Padding { get; set; } // Aligns the header to 16 bytes

            public MonsterHeaderFile()
            {
                Signature = 8;
                Padding = new byte[16];
            }
        }

        public class MonsterStatSheetStruct
        {
            [Data] public TextScriptInfo NameTSInfo { get; set; }
            [Data] public TextScriptInfo SensorTSInfo { get; set; }
            [Data] public TextScriptInfo UnusedText1TSInfo { get; set; } // みしよう (unused)
            [Data] public TextScriptInfo ScanTSInfo { get; set; }
            [Data] public TextScriptInfo UnusedText2TSInfo { get; set; } // みしよう (unused)
            [Data] public Monster_StatSheet StatSheet { get; set; }
            [Data(Count = 4)] public byte[] Padding { get; set; }

            public MonsterStatSheetStruct()
            {
                NameTSInfo = new();
                SensorTSInfo = new();
                UnusedText1TSInfo = new();
                ScanTSInfo = new();
                UnusedText2TSInfo = new();
                StatSheet = new();
                Padding = new byte[4];
            }
        }
    }
}
