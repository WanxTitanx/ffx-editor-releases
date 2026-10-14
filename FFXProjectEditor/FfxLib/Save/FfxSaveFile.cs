// ============================================================================
// FfxSaveFile — load/save FFX save-game containers (FFXED compatible subset)
// PURPOSE : session wrapper over FfxSaveCore that detects the outer container (RawPs2/PSU/PcFfx/BIN),
//           loads the 25848-byte payload, preserves wrappers (PC .ffx header/footer, PSU blocks), and
//           saves back byte-identically for unmodified payloads.
// WHY     : the same payload is wrapped differently per platform; writing back must preserve the outer
//           wrapper or the game won't accept the save (header/footer and CRC are part of the container).
// EVIDENCE: PSU blocks = 504-byte header + payload@+504 + label@-448 + pad (size+1023)&~1023.
//           PC .ffx GENUINE layout (fixed 2026-09-14, FFX_SAVES_HUNT_2026-09-14): 26880 bytes =
//           25848 payload @0 + 1032 tail — proven by ALL 81 real saves available (12 user Steam
//           slots + 66 TAS checkpoints + 3 converter references, game CRC validated 81/81); the
//           previous "0x40 header + payload + 1032 = 26944" layout matches ZERO real saves and
//           was an editor invention. Legacy 26944 files written by older editor builds are still
//           READ (payload @0x40 via tamper tag) and re-saved as the genuine 26880 layout.
// MAINT   : ResolvePcFfxPayloadStart uses the tamper-tag to hedge ambiguous PC layouts — keep PayloadHasTamperTag
//           in sync with FfxSaveCore.TamperTag. SaveBin pads 1024 (or BIIN's 40+984 from a PSU source).
// ============================================================================
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FFXProjectEditor.FfxLib.Save
{
    public sealed class FfxSavePsuLayout
    {
        public long PayloadOffset { get; init; }
        public long SlotLabelOffset { get; init; }
        public long SlotCopyOffsetA { get; init; }
        public long SlotCopyOffsetB { get; init; }
        public string DisplayName { get; init; } = string.Empty;
    }

    /// <summary>
    /// Load/save FFX save-game containers (FFXED-compatible subset).
    /// </summary>
    public sealed class FfxSaveFile
    {
        public const int PcFfxHeaderSize = 0x40;
        public const int PcFfxFooterSize = 1032;

        public FfxSaveCore Core { get; }
        public FfxSaveFormat Format { get; private set; }
        public string SourcePath { get; private set; } = string.Empty;
        public string DisplayLabel { get; private set; } = string.Empty;
        public FfxSavePsuLayout? PsuLayout { get; private set; }
        public byte[]? PsuSourceBytes { get; private set; }
        /// <summary>64-byte PC .ffx wrapper preserved for round-trip (Steam/Square layout).</summary>
        public byte[]? PcFfxHeader { get; private set; }
        /// <summary>1032-byte PC .ffx tail preserved for round-trip.</summary>
        public byte[]? PcFfxFooter { get; private set; }
        /// <summary>True when loaded from the legacy editor-invented 26944 layout (payload @0x40);
        /// re-saving emits the genuine 26880 game layout.</summary>
        public bool PcFfxLegacyEditorLayout { get; private set; }
        /// <summary>Payload snapshot taken at Load time: an UNMODIFIED session re-saves
        /// byte-identically (no tamper tag/CRC stamping); any edit restores the
        /// PrepareForSave behavior (FFXED tag + fresh checksum).</summary>
        private byte[]? LoadedPayloadSnapshot { get; set; }

        FfxSaveFile(FfxSaveCore core) => Core = core;

        internal static FfxSaveFile CreateSession(
            FfxSaveCore core,
            FfxSaveFormat format,
            string sourcePath,
            string displayLabel,
            byte[]? psuSourceBytes = null,
            FfxSavePsuLayout? psuLayout = null) =>
            new(core)
            {
                Format = format,
                SourcePath = sourcePath,
                DisplayLabel = displayLabel,
                PsuSourceBytes = psuSourceBytes,
                PsuLayout = psuLayout,
            };

        public static FfxSaveFile Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Save file not found.", path);

            long length = new FileInfo(path).Length;
            FfxSaveFormat format = DetectFormat(path, length);
            FfxSaveCore core = new();
            FfxSavePsuLayout? psu = null;
            byte[]? psuBytes = null;
            byte[]? pcHeader = null;
            byte[]? pcFooter = null;
            bool legacyEditorLayout = false;
            string label;

            switch (format)
            {
                case FfxSaveFormat.RawPs2:
                {
                    byte[] raw = File.ReadAllBytes(path);
                    Array.Copy(raw, core.Data, FfxSaveCore.DataSize);
                    label = Path.GetFileName(path);
                    break;
                }
                case FfxSaveFormat.Psu:
                {
                    psuBytes = File.ReadAllBytes(path);
                    psu = ParsePsu(psuBytes, out byte[] payload);
                    Array.Copy(payload, core.Data, FfxSaveCore.DataSize);
                    label = psu.DisplayName;
                    break;
                }
                case FfxSaveFormat.PcFfx:
                {
                    byte[] raw = File.ReadAllBytes(path);
                    if (raw.Length < FfxSaveCore.DataSize)
                        throw new InvalidDataException("PC .ffx file too small.");

                    // Genuine PC .ffx = 25848 payload @0 + 1032 tail (26880 total); the legacy
                    // 26944 editor layout (0x40 header) is still read via the tamper tag and
                    // re-saved as genuine (see Save below).
                    int payloadStart = ResolvePcFfxPayloadStart(raw);
                    Array.Copy(raw, payloadStart, core.Data, 0, FfxSaveCore.DataSize);
                    label = Path.GetFileName(path);

                    legacyEditorLayout = payloadStart == PcFfxHeaderSize;
                    pcHeader = legacyEditorLayout && raw.Length >= PcFfxHeaderSize
                        ? raw.AsSpan(0, PcFfxHeaderSize).ToArray()
                        : new byte[PcFfxHeaderSize];

                    int footerStart = payloadStart + FfxSaveCore.DataSize;
                    pcFooter = raw.Length >= footerStart + PcFfxFooterSize
                        ? raw.AsSpan(footerStart, PcFfxFooterSize).ToArray()
                        : new byte[PcFfxFooterSize];
                    break;
                }
                case FfxSaveFormat.Vme:
                {
                    using var fs = File.OpenRead(path);
                    fs.Seek(8, SeekOrigin.Begin);
                    if (fs.Read(core.Data, 0, FfxSaveCore.DataSize) != FfxSaveCore.DataSize)
                        throw new InvalidDataException("VME payload truncated.");
                    label = Path.GetFileName(path);
                    break;
                }
                case FfxSaveFormat.Bin:
                {
                    byte[] raw = File.ReadAllBytes(path);
                    if (raw.Length < FfxSaveCore.DataSize)
                        throw new InvalidDataException("BIN payload truncated.");
                    Array.Copy(raw, core.Data, FfxSaveCore.DataSize);
                    // FIX 2026-09-16 (FMT-AUDIO audit): keep the raw container in
                    // PsuSourceBytes so SaveBin can re-emit its 40B metadata +
                    // 984B pad (SaveBin already reads PsuSourceBytes[DataSize..+40]).
                    // Without this the load dropped the tail and every unmodified
                    // Bin re-save came out 2 bytes short of byte-identical
                    // (converter "PS Vita"/"PS3 (decrypted)" carry 0x1111 at
                    // tail+24). Caveat: the last 984B of tail are always re-emitted
                    // as zeros — true for every observed real Bin (verified on the
                    // converter references); a nonzero pad would not round-trip.
                    psuBytes = raw;
                    label = Path.GetFileName(path);
                    break;
                }
                case FfxSaveFormat.MemoryCard:
                    return FfxSaveMemoryCard.LoadFirst(path);
                default:
                    throw new NotSupportedException($"Format {format} is not supported in the native Save Editor yet.");
            }

            var snapshot = new byte[FfxSaveCore.DataSize];
            Array.Copy(core.Data, snapshot, FfxSaveCore.DataSize);

            return new FfxSaveFile(core)
            {
                Format = format,
                SourcePath = path,
                DisplayLabel = label,
                PsuLayout = psu,
                PsuSourceBytes = psuBytes,
                PcFfxHeader = pcHeader,
                PcFfxFooter = pcFooter,
                PcFfxLegacyEditorLayout = format == FfxSaveFormat.PcFfx && legacyEditorLayout,
                LoadedPayloadSnapshot = snapshot,
            };
        }

        public void Save(string? path = null)
        {
            string target = path ?? SourcePath;
            if (string.IsNullOrWhiteSpace(target))
                throw new InvalidOperationException("No destination path.");

            // WHY (2026-09-14, FFX_SAVES_HUNT regression): only stamp the FFXED tamper tag and
            // recompute the CRC when the payload actually changed — an unmodified session must
            // re-save byte-identically (all 81 genuine saves round-trip exactly this way).
            bool unmodified = LoadedPayloadSnapshot is byte[] snapshot
                && Core.Data.AsSpan().SequenceEqual(snapshot);
            if (!unmodified)
                Core.PrepareForSave();

            switch (Format)
            {
                case FfxSaveFormat.RawPs2:
                    File.WriteAllBytes(target, Core.Data);
                    break;
                case FfxSaveFormat.Psu:
                    SavePsu(target);
                    break;
                case FfxSaveFormat.PcFfx:
                {
                    // Genuine game layout (2026-09-14): payload @0 + 1032 tail = 26880 bytes.
                    // The 0x40 header is NOT part of the real format (81/81 real saves prove it)
                    // and is dropped on save even for legacy-loaded files.
                    byte[] footer = PcFfxFooter ?? new byte[PcFfxFooterSize];
                    using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
                    fs.Write(Core.Data);
                    fs.Write(footer);
                    PcFfxFooter = footer;
                    PcFfxLegacyEditorLayout = false;
                    break;
                }
                case FfxSaveFormat.Vme:
                {
                    using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
                    fs.Write(new byte[] { 125, 196, 47, 93 });
                    fs.Write(Core.Data);
                    fs.WriteByte(0xFF);
                    fs.Write(new byte[1023]);
                    break;
                }
                case FfxSaveFormat.Bin:
                    SaveBin(target);
                    break;
                case FfxSaveFormat.MemoryCard:
                    FfxSaveMemoryCard.SaveSlot(this, target);
                    SourcePath = target;
                    break;
                default:
                    throw new NotSupportedException($"Cannot save format {Format}.");
            }

            SourcePath = target;
        }

        public void SaveAs(string path, FfxSaveFormat format)
        {
            Format = format;
            if (format != FfxSaveFormat.Psu)
            {
                PsuLayout = null;
                PsuSourceBytes = null;
            }

            if (format != FfxSaveFormat.PcFfx)
            {
                PcFfxHeader = null;
                PcFfxFooter = null;
            }

            Save(path);
        }

        static FfxSaveFormat DetectFormat(string path, long length)
        {
            if (length == FfxSaveCore.DataSize)
                return FfxSaveFormat.RawPs2;
            // PC .ffx with the 0x40 wrapper: 64 + 25848 + 1032 = 26944 (what Save()
            // emits), and the legacy pre-0x40 layout: 25848 + 1032 = 26880.
            if (length == PcFfxHeaderSize + FfxSaveCore.DataSize + PcFfxFooterSize)
                return FfxSaveFormat.PcFfx;
            if (length == FfxSaveCore.DataSize + PcFfxFooterSize)
                return FfxSaveFormat.PcFfx;
            // FIX 2026-09-16 (FMT-AUDIO audit): 26872 is unique to the Bin
            // container (payload 25848 @0 + 40B metadata + 984 pad) — Vme is
            // 26876, PcFfx 26880, Psu 64512 — so the ".bin" extension gate was
            // both unnecessary and wrong for extension-less reference saves
            // (converter "PS Vita"/"PS3 (decrypted)" = 26872 and now load).
            if (length == 26872)
                return FfxSaveFormat.Bin;
            if (length == 64512 && path.EndsWith(".psu", StringComparison.OrdinalIgnoreCase))
                return FfxSaveFormat.Psu;
            if (length == 8_650_752 && path.EndsWith(".ps2", StringComparison.OrdinalIgnoreCase))
                return FfxSaveFormat.MemoryCard;

            // Heuristic: large PSU-like images.
            if (length > FfxSaveCore.DataSize + 1024)
                return FfxSaveFormat.Psu;

            throw new InvalidDataException($"Unrecognized save container ({length} bytes): {path}");
        }

        static FfxSavePsuLayout ParsePsu(byte[] file, out byte[] payload)
        {
            long payloadOffset = -1;
            long slotLabelOffset = -1;
            long slotCopyA = -1;
            long slotCopyB = -1;
            bool needPayload = true, needSlot = true, needCopyA = true;

            using var ms = new MemoryStream(file, writable: false);
            using var br = new BinaryReader(ms);
            long fileLength = file.Length;

            while (ms.Position < fileLength)
            {
                byte h0 = br.ReadByte();
                byte h1 = br.ReadByte();
                if (h0 == 0x27 && h1 == 0x84)
                {
                    ms.Seek(510, SeekOrigin.Current);
                    continue;
                }

                br.ReadByte();
                br.ReadByte();
                int blockSize = br.ReadInt32();
                long blockStart = ms.Position - 8;

                if (needPayload && blockSize == FfxSaveCore.DataSize)
                {
                    payloadOffset = blockStart + 504;
                    needPayload = false;
                }
                else if (needSlot && blockSize == 964)
                {
                    long restore = ms.Position;
                    ms.Seek(696, SeekOrigin.Current);
                    for (int i = 0; i < 20; i++)
                    {
                        byte a = br.ReadByte();
                        byte b = br.ReadByte();
                        if (a == 0x81 && b == 0x6D)
                        {
                            slotLabelOffset = ms.Position + 1;
                            needSlot = false;
                            break;
                        }
                    }

                    ms.Position = restore;
                    slotCopyB = blockStart + 770;
                }
                else if (needCopyA && blockSize == 33688)
                {
                    slotCopyA = blockStart + 62;
                    needCopyA = false;
                }

                if (!needPayload && !needSlot && !needCopyA)
                    break;

                int padded = 504 + ((blockSize + 1023) & ~1023);
                ms.Seek(blockStart + padded, SeekOrigin.Begin);
            }

            if (needPayload || needSlot || needCopyA)
                throw new InvalidDataException("Could not locate FFX save blocks inside PSU image.");

            payload = new byte[FfxSaveCore.DataSize];
            Array.Copy(file, (int)payloadOffset, payload, 0, FfxSaveCore.DataSize);

            string displayName = ReadNullTerminatedAscii(file, (int)payloadOffset - 448);

            return new FfxSavePsuLayout
            {
                PayloadOffset = payloadOffset,
                SlotLabelOffset = slotLabelOffset,
                SlotCopyOffsetA = slotCopyA,
                SlotCopyOffsetB = slotCopyB,
                DisplayName = displayName,
            };
        }

        void SavePsu(string target)
        {
            if (PsuLayout == null || PsuSourceBytes == null)
                throw new InvalidOperationException("PSU layout metadata missing — reload from a .psu file before saving.");

            byte[] output = (byte[])PsuSourceBytes.Clone();
            Array.Copy(Core.Data, 0, output, (int)PsuLayout.PayloadOffset, FfxSaveCore.DataSize);
            File.WriteAllBytes(target, output);
            PsuSourceBytes = output;
            SourcePath = target;
        }

        void SaveBin(string target)
        {
            using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
            fs.Write(Core.Data);
            if (Format == FfxSaveFormat.Bin && PsuSourceBytes != null && PsuSourceBytes.Length >= FfxSaveCore.DataSize + 40)
            {
                fs.Write(PsuSourceBytes, FfxSaveCore.DataSize, 40);
                fs.Write(new byte[984]);
            }
            else
            {
                fs.Write(new byte[1024]);
            }
        }

        /// <summary>
        /// Validates the payload against the GAME's checksum method: the CRC covers bytes
        /// 64..25847, which INCLUDES the checksum field itself (@25844/25845) and the header
        /// copy (@26/27), so a genuine save only validates when those self-referential bytes are
        /// zeroed first (converter Modules/checksum.py behavior; 81/81 real saves match).
        /// </summary>
        public bool ValidateGameChecksum()
        {
            byte[] copy = (byte[])Core.Data.Clone();
            copy[26] = 0;
            copy[27] = 0;
            copy[25844] = 0;
            copy[25845] = 0;
            ushort computed = FfxSaveChecksum.Compute(copy);
            ushort stored = (ushort)(Core.Data[26] | (Core.Data[27] << 8));
            return stored == computed;
        }

        static string ReadNullTerminatedAscii(byte[] data, int offset)
        {
            var sb = new StringBuilder();
            for (int i = offset; i < data.Length && data[i] != 0; i++)
                sb.Append((char)data[i]);
            return sb.ToString();
        }

        static int ResolvePcFfxPayloadStart(byte[] raw)
        {
            // Genuine game layout (2026-09-14 evidence): 26880 = payload@0 + 1032 tail.
            if (raw.Length == FfxSaveCore.DataSize + PcFfxFooterSize)
                return 0;

            // Legacy editor layout (26944 with the FFXED tamper tag @0x40): keep reading it.
            if (raw.Length == PcFfxHeaderSize + FfxSaveCore.DataSize + PcFfxFooterSize
                && PayloadHasTamperTag(raw, PcFfxHeaderSize))
                return PcFfxHeaderSize;

            // Fallbacks for unusual sizes: trust the tamper tag wherever it resolves.
            if (PayloadHasTamperTag(raw, PcFfxHeaderSize))
                return PcFfxHeaderSize;
            if (PayloadHasTamperTag(raw, 0))
                return 0;

            return raw.Length >= PcFfxHeaderSize + FfxSaveCore.DataSize ? PcFfxHeaderSize : 0;
        }

        static bool PayloadHasTamperTag(byte[] raw, int payloadStart)
        {
            const int tagOffsetInPayload = 32;
            ReadOnlySpan<byte> expected =
            [
                84, 115, 120, 131, 116, 115, 58, 113, 136, 58, 85, 85, 103, 84, 83,
            ];

            int tagStart = payloadStart + tagOffsetInPayload;
            if (tagStart + expected.Length > raw.Length)
                return false;

            return raw.AsSpan(tagStart, expected.Length).SequenceEqual(expected);
        }
    }
}
