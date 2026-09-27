// ============================================================================
// FfxSaveMemoryCardFixture — builds minimal PS2 memory-card images (lab/RT2)
// PURPOSE : constructs a raw .ps2 card image and writes FFX PS2 slots with embedded payload blocks.
// WHY     : provides deterministic test/RT2 saves without a real memory-card tool.
// EVIDENCE: PSU block layout (504-byte header, 64-byte label at -448); payload = FfxSaveCore.DataSize.
// MAINT   : block padding uses (size+1023)&~1023; label ASCII <= 64. Not a full mymc writer.
// ============================================================================
using System;
using System.IO;

namespace FFXProjectEditor.FfxLib.Save
{
    /// <summary>
    /// Builds minimal PS2 memory-card images with embedded FFX PSU-style payload blocks (lab / RT2).
    /// </summary>
    public static class FfxSaveMemoryCardFixture
    {
        public static byte[] CreateMinimalPayload(string label = "RT2 Test Save")
        {
            var core = new FfxSaveCore();
            core.Data[0] = 0x01;
            core.WriteFfxString(56, label.Length > 32 ? label[..32] : label);
            core.PrepareForSave();
            return core.Data;
        }

        public static void WritePs2WithSlots(string path, params (byte[] Payload, string Label)[] slots)
        {
            if (slots.Length == 0)
                throw new ArgumentException("At least one slot required.");

            byte[] card = new byte[FfxSaveMemoryCard.CardSize];
            long pos = 0;

            foreach ((byte[] payload, string label) in slots)
            {
                if (payload.Length != FfxSaveCore.DataSize)
                    throw new ArgumentException($"Payload must be {FfxSaveCore.DataSize} bytes.");

                WritePayloadBlock(card, ref pos, payload, label);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            File.WriteAllBytes(path, card);
        }

        static void WritePayloadBlock(byte[] card, ref long pos, byte[] payload, string label)
        {
            long blockStart = pos;
            card[blockStart + 4] = (byte)(FfxSaveCore.DataSize & 0xFF);
            card[blockStart + 5] = (byte)((FfxSaveCore.DataSize >> 8) & 0xFF);
            card[blockStart + 6] = (byte)((FfxSaveCore.DataSize >> 16) & 0xFF);
            card[blockStart + 7] = (byte)((FfxSaveCore.DataSize >> 24) & 0xFF);

            long payloadOffset = blockStart + 504;
            Array.Copy(payload, 0, card, payloadOffset, FfxSaveCore.DataSize);

            int labelOffset = (int)payloadOffset - 448;
            byte[] labelBytes = System.Text.Encoding.ASCII.GetBytes(label);
            int copy = Math.Min(labelBytes.Length, 64);
            Array.Copy(labelBytes, 0, card, labelOffset, copy);

            int padded = 504 + ((FfxSaveCore.DataSize + 1023) & ~1023);
            pos = blockStart + padded;
        }
    }
}
