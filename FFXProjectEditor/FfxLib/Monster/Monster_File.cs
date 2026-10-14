// ============================================================================
// Monster_File — container reader/writer for the monster .bin (m###_X.bin under battle/mon/)
// PURPOSE : parses the .bin by chunk-offset pointers from a 0x34-byte header, exposing typed
//           StatSheet + Loot and raw byte-blocks (Ai/Worker/Unk/Audio/Text). Used by MonEditor / CustomBossCreator.
// WHY     : the .bin is a header followed by variable-sized sections at absolute offsets held in
//           MonsterHeaderFile (Ai/Worker/StatSheet/Spoils/Loot/Audio/Text pointers + FileSize). Section
//           order is NOT fixed on disk — Read honors each pointer; Write emits sections in a fixed order.
// EVIDENCE: RT0 byte-identity round-trip on vanilla m000/m00X (Baseline 185/185 + MonsterFileAdapter gate).
// MAINT   : Ai + Worker are pass-through byte-blocks here (their format = ATEL script, OUT of scope — see
//           FfxLib/Ai + MonsterAiEditor). Do NOT realign sections without re-proving the pointer math.
//           The header Padding overlaps AiFile start (0x30..0x33 == AiFile[0..3]) — see Write().
// ============================================================================
using System.IO;
using Xe.BinaryMapper;
using static FFXProjectEditor.FfxLib.Monster.Monster_Structs;

namespace FFXProjectEditor.FfxLib.Monster
{
    public class Monster_File
    {
        // ── Sections exposed by the container ──
        // StatSheet + Loot are parsed to typed models; the rest are raw byte-blocks the owning editor
        // interprets (Ai/Worker = ATEL out of scope; Unk/Audio/Text = pass-through to save/load already
        // proven by RT0). OriginalHeader keeps the on-disk Signature + Padding verbatim for byte-identity Write.
        public byte[] AiFile {  get; set; }
        public byte[] WorkerFile {  get; set; }
        public Monster_StatSheet StatSheetFile {  get; set; }
        public byte[] UnkFile {  get; set; }
        public Monster_Loot LootFile {  get; set; }
        public byte[] AudioFile {  get; set; }
        public byte[] TextFile {  get; set; }
        public MonsterHeaderFile OriginalHeader { get; set; }

        // Reads the container: starts at FileSize (end) and walks each section backward using its pointer,
        // so the bytes before a pointer belong to the earlier section (sections on disk are packed adjacently).
        // StatSheet + Loot are parsed to typed models afterwards; Ai/Worker/Unk/Audio/Text stay as raw blocks.
        public static Monster_File Read(byte[] fileByte)
        {
            Monster_File file = new();

            byte[] statSheetByteFile = [];
            byte[] lootByteFile = [];
            using (MemoryStream stream = new MemoryStream(fileByte))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                MonsterHeaderFile header = BinaryMapping.ReadObject<MonsterHeaderFile>(stream);
                file.OriginalHeader = header;

                int currentEofIndex = header.FileSize;

                if (header.TextFilePointer > 0)
                {
                    stream.Position = header.TextFilePointer;
                    file.TextFile = reader.ReadBytes(currentEofIndex - header.TextFilePointer);
                    currentEofIndex = header.TextFilePointer;
                }

                if (header.AudioFilePointer > 0)
                {
                    stream.Position = header.AudioFilePointer;
                    file.AudioFile = reader.ReadBytes(currentEofIndex - header.AudioFilePointer);
                    currentEofIndex = header.AudioFilePointer;
                }

                if (header.LootFilePointer > 0)
                {
                    stream.Position = header.LootFilePointer;
                    lootByteFile = reader.ReadBytes(currentEofIndex - header.LootFilePointer);
                    currentEofIndex = header.LootFilePointer;
                }

                if (header.SpoilsFilePointer > 0)
                {
                    stream.Position = header.SpoilsFilePointer;
                    file.UnkFile = reader.ReadBytes(currentEofIndex - header.SpoilsFilePointer);
                    currentEofIndex = header.SpoilsFilePointer;
                }

                if (header.StatSheetPointer > 0)
                {
                    stream.Position = header.StatSheetPointer;
                    statSheetByteFile = reader.ReadBytes(currentEofIndex - header.StatSheetPointer);
                    currentEofIndex = header.StatSheetPointer;
                }

                if (header.WorkerFilePointer > 0)
                {
                    stream.Position = header.WorkerFilePointer;
                    file.WorkerFile = reader.ReadBytes(currentEofIndex - header.WorkerFilePointer);
                    currentEofIndex = header.WorkerFilePointer;
                }

                if (header.AiFilePointer > 0)
                {
                    stream.Position = header.AiFilePointer;
                    file.AiFile = reader.ReadBytes(currentEofIndex - header.AiFilePointer);
                }
            }

            if(statSheetByteFile.Length > 0) {
                file.StatSheetFile = Monster_StatSheet.ReadSingle(statSheetByteFile);
            }
            if (lootByteFile.Length > 0)
            {
                file.LootFile = Monster_Loot.ReadSingle(lootByteFile);
            }

            return file;
        }

        // Serializes the container back to bytes. Sections are written at 0x30+ in fixed order (Ai/Worker/StatSheet/
        // Spoils/Loot/Audio/Text) and the header pointers + FileSize are recomputed. The header is 0x34 bytes but
        // sections start at 0x30; the last 4 Padding bytes overlap AiFile[0..3] — OriginalHeader.Padding is reused
        // verbatim (see below) so Write() is byte-identity for unmodified files.
        public byte[] Write()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                MonsterHeaderFile header = new();
                // Preserve the original Signature + header Padding. The header is 0x34 bytes but the AiFile
                // starts at 0x30, so the last 4 Padding bytes overlap AiFile[0..3]; a zeroed Padding would
                // corrupt the AiFile start. Reusing the original Padding writes those bytes back identically.
                if (OriginalHeader != null)
                {
                    header.Signature = OriginalHeader.Signature;
                    header.Padding = OriginalHeader.Padding;
                }

                stream.Position = 0x30;

                if(AiFile != null && AiFile.Length > 0)
                {
                    header.AiFilePointer = (int)stream.Position;
                    writer.Write(AiFile);
                }
                if (WorkerFile != null && WorkerFile.Length > 0)
                {
                    header.WorkerFilePointer = WorkerFile.Length == 0 ? 0 : (int)stream.Position;
                    writer.Write(WorkerFile);
                }
                if (StatSheetFile != null)
                {
                    header.StatSheetPointer = StatSheetFile == null ? 0 : (int)stream.Position;
                    writer.Write(StatSheetFile.WriteSingle());
                    // Note: Files may be aligned to 4 or 8 bytes using 0xFF
                }
                if (UnkFile != null && UnkFile.Length > 0)
                {
                    header.SpoilsFilePointer = UnkFile.Length == 0 ? 0 : (int)stream.Position;
                    writer.Write(UnkFile);
                }
                if (LootFile != null)
                {
                    header.LootFilePointer = LootFile == null ? 0 : (int)stream.Position;
                    writer.Write(LootFile.WriteSingle());
                }
                if (AudioFile != null && AudioFile.Length > 0)
                {
                    header.AudioFilePointer = AudioFile.Length == 0 ? 0 : (int)stream.Position;
                    writer.Write(AudioFile);
                }
                if (TextFile != null && TextFile.Length > 0)
                {
                    header.TextFilePointer = TextFile.Length == 0 ? 0 : (int)stream.Position;
                    writer.Write(TextFile);
                }

                header.FileSize = (int)stream.Length;

                stream.Position = 0;
                BinaryMapping.WriteObject<MonsterHeaderFile>(stream, header);

                return stream.ToArray();
            }
        }
    }
}
