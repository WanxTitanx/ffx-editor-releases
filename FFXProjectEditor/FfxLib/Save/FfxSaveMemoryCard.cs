// ============================================================================
// FfxSaveMemoryCard — PS2 memory card (.ps2, 8MB) scan + slot load/save
// PURPOSE : scans an 8MB card for embedded FFX PSU payload blocks (0x27/0x84 markers), lists slots, loads a
//           slot as a FfxSaveFile session, and writes a slot back in-place.
// WHY     : a card is a sequence of sector-aligned blocks; each FFX save is a 25848-byte payload at +504 of
//           its block with a 64-byte label at -448 (same PSU layout as the .ps2 loader).
// EVIDENCE: CardSize = 8_650_752 (8MB); block padding (size+1023)&~1023; marker 0x27/0x84 skips 512.
// MAINT   : SaveSlot writes into the session's PsuSourceBytes at the slot payload offset — the card image is
//           re-serialized to disk, not a shrink/grow. mymc.exe listing is optional-only.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Save
{
    public sealed class FfxSaveMemoryCardSlot
    {
        public string Label { get; init; } = string.Empty;
        public long PayloadOffset { get; init; }
    }

    /// <summary>
    /// PS2 memory card (.ps2, 8MB) — scans for embedded FFX PSU blocks (same layout as FfxSaveFile.ParsePsu).
    /// Optional mymc.exe listing when present beside FFXED.jar.
    /// </summary>
    public static class FfxSaveMemoryCard
    {
        public const long CardSize = 8_650_752;

        public static IReadOnlyList<FfxSaveMemoryCardSlot> ListSlots(string path)
        {
            byte[] card = File.ReadAllBytes(path);
            if (card.LongLength != CardSize)
                throw new InvalidDataException($"Expected {CardSize} byte memory card, got {card.LongLength}.");

            var slots = new List<FfxSaveMemoryCardSlot>();
            long fileLength = card.Length;
            long pos = 0;

            while (pos < fileLength)
            {
                if (pos + 8 > fileLength)
                    break;

                byte h0 = card[pos];
                byte h1 = card[pos + 1];
                if (h0 == 0x27 && h1 == 0x84)
                {
                    pos += 512;
                    continue;
                }

                int blockSize = card[pos + 4] | (card[pos + 5] << 8) | (card[pos + 6] << 16) | (card[pos + 7] << 24);
                long blockStart = pos;

                if (blockSize == FfxSaveCore.DataSize)
                {
                    long payloadOffset = blockStart + 504;
                    string label = ReadAscii(card, (int)payloadOffset - 448);
                    slots.Add(new FfxSaveMemoryCardSlot
                    {
                        Label = string.IsNullOrWhiteSpace(label) ? $"Save @ 0x{payloadOffset:X}" : label,
                        PayloadOffset = payloadOffset,
                    });
                }

                int padded = 504 + ((blockSize + 1023) & ~1023);
                pos = blockStart + padded;
            }

            return slots;
        }

        public static FfxSaveFile LoadSlot(string path, FfxSaveMemoryCardSlot slot)
        {
            byte[] card = File.ReadAllBytes(path);
            var core = new FfxSaveCore();
            Array.Copy(card, (int)slot.PayloadOffset, core.Data, 0, FfxSaveCore.DataSize);

            return FfxSaveFile.CreateSession(
                core,
                FfxSaveFormat.MemoryCard,
                path,
                slot.Label,
                card,
                new FfxSavePsuLayout
                {
                    PayloadOffset = slot.PayloadOffset,
                    DisplayName = slot.Label,
                });
        }

        public static FfxSaveFile LoadFirst(string path)
        {
            FfxSaveMemoryCardSlot slot = ListSlots(path).FirstOrDefault()
                ?? throw new InvalidDataException("No FFX save payload found in memory card image.");
            return LoadSlot(path, slot);
        }

        public static void SaveSlot(FfxSaveFile session, string? targetPath = null)
        {
            if (session.Format != FfxSaveFormat.MemoryCard || session.PsuLayout == null || session.PsuSourceBytes == null)
                throw new InvalidOperationException("Session is not a memory card slot.");

            session.Core.PrepareForSave();
            Array.Copy(session.Core.Data, 0, session.PsuSourceBytes, (int)session.PsuLayout.PayloadOffset, FfxSaveCore.DataSize);
            string path = targetPath ?? session.SourcePath;
            File.WriteAllBytes(path, session.PsuSourceBytes);
        }

        static string ReadAscii(byte[] data, int offset)
        {
            var chars = new List<char>();
            for (int i = Math.Max(0, offset); i < data.Length && data[i] != 0; i++)
                chars.Add((char)data[i]);
            return new string(chars.ToArray());
        }
    }
}
