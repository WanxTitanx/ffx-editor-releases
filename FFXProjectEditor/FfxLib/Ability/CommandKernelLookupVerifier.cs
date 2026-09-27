using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ability
{
    /// <summary>
    /// Offline replay of the game's command lookup so we can prove (without launching the game)
    /// that a grown <c>command.bin</c> resolves a given command id to a real row and not the
    /// fallback row 0.
    /// <para>
    /// RE source: <c>docs/reverse/FFX_NUL_WARD_TEACH_SURFACE_RE_VERDICT_2026-06-16.md</c> §F.
    /// The engine loads <c>command.bin</c> verbatim (<c>FFX_Kernel_LoadFileToTable</c> @0x781E00,
    /// case 0 → <c>g_CommandKernelTable</c> @0x112A92C) and <c>FFX_Kernel_GetCommandEntryById</c>
    /// (@0x790AE0) calls <c>FFX_Table_GetEntryByIdRange</c> (@0x7AB890) which reads the range header
    /// straight from the file bytes:
    /// <code>
    /// numRanges = int16 @ 0            (Signature byte; 1 for command.bin)
    /// range @ +8 = { lo  = u16 @ 8     (PreviousFileCount)
    ///                hi  = u16 @ 10    (EntryCount - 1)
    ///                stride = u16 @ 12 (EntrySize, 0x60)
    ///                size  = u16 @ 14  (EntryTableSize)
    ///                base  = i32 @ 16  (EntryTableFileOffset, 0x14) }
    /// record(id) = file + base + (id - lo) * stride   when lo &lt;= id &lt;= hi
    /// fallback   = file + base   (= row 0)             otherwise
    /// </code>
    /// </para>
    /// </summary>
    public static class CommandKernelLookupVerifier
    {
        public readonly record struct KernelRange(int Lo, int Hi, int Stride, int Size, int Base);

        public sealed class IdResolution
        {
            public required int Id { get; init; }
            /// <summary>Byte offset the engine would return for this id.</summary>
            public required int EngineOffset { get; init; }
            /// <summary>True when the id fell inside a declared range (not the row-0 fallback).</summary>
            public required bool InRange { get; init; }
            /// <summary>Index of the matched range, or -1 when it fell through to the fallback.</summary>
            public required int RangeIndex { get; init; }
        }

        /// <summary>Parse the range header exactly like <c>FFX_Table_GetEntryByIdRange</c> does.</summary>
        public static IReadOnlyList<KernelRange> ParseRanges(ReadOnlySpan<byte> file)
        {
            if (file.Length < 0x14)
                throw new ArgumentException($"command.bin too small ({file.Length} bytes) to hold a kernel header.", nameof(file));

            int numRanges = ReadI16(file, 0);
            var ranges = new List<KernelRange>(Math.Max(0, numRanges));
            if (numRanges <= 0)
                return ranges;

            for (int i = 0; i < numRanges; i++)
            {
                int off = 8 + i * 12;
                if (off + 12 > file.Length)
                    break;
                ranges.Add(new KernelRange(
                    Lo: ReadU16(file, off + 0),
                    Hi: ReadU16(file, off + 2),
                    Stride: ReadU16(file, off + 4),
                    Size: ReadU16(file, off + 6),
                    Base: ReadI32(file, off + 8)));
            }
            return ranges;
        }

        /// <summary>Replay the lookup for one id. Masks to the low 12 bits like the engine (<c>id &amp; 0xFFF</c>).</summary>
        public static IdResolution Resolve(ReadOnlySpan<byte> file, int encodedId)
        {
            int id = encodedId & 0xFFF;
            int numRanges = ReadI16(file, 0);

            if (numRanges > 0)
            {
                for (int i = 0; i < numRanges; i++)
                {
                    int off = 8 + i * 12;
                    if (off + 12 > file.Length)
                        break;
                    int lo = ReadU16(file, off + 0);
                    int hi = ReadU16(file, off + 2);
                    if (id < lo || id > hi)
                        continue;
                    int stride = ReadU16(file, off + 4);
                    int @base = ReadI32(file, off + 8);
                    return new IdResolution
                    {
                        Id = id,
                        EngineOffset = @base + (id - lo) * stride,
                        InRange = true,
                        RangeIndex = i,
                    };
                }
            }

            // Fallback: row 0 of range 0 (file + base@range0). Mirrors the LABEL_6 path.
            int fallbackBase = numRanges > 0 ? ReadI32(file, 0x10) : 0x14;
            return new IdResolution
            {
                Id = id,
                EngineOffset = fallbackBase,
                InRange = false,
                RangeIndex = -1,
            };
        }

        public sealed class WardLookupReport
        {
            public required IdResolution Radiant { get; init; }
            public required IdResolution Umbral { get; init; }
            public required int RowZeroOffset { get; init; }
            public required int ExpectedRadiantOffset { get; init; }
            public required int ExpectedUmbralOffset { get; init; }

            public bool RadiantResolves => Radiant.InRange
                && Radiant.EngineOffset == ExpectedRadiantOffset
                && Radiant.EngineOffset != RowZeroOffset;

            public bool UmbralResolves => Umbral.InRange
                && Umbral.EngineOffset == ExpectedUmbralOffset
                && Umbral.EngineOffset != RowZeroOffset;

            public bool Pass => RadiantResolves && UmbralResolves;

            public string Describe()
            {
                return
                    $"row0@0x{RowZeroOffset:X}; " +
                    $"Radiant(320) engine@0x{Radiant.EngineOffset:X} expected@0x{ExpectedRadiantOffset:X} inRange={Radiant.InRange} ok={RadiantResolves}; " +
                    $"Umbral(321) engine@0x{Umbral.EngineOffset:X} expected@0x{ExpectedUmbralOffset:X} inRange={Umbral.InRange} ok={UmbralResolves}";
            }
        }

        /// <summary>
        /// Prove a grown command.bin resolves Radiant(320)/Umbral(321) to their appended rows the way
        /// the engine reads it. <paramref name="stride"/>/<paramref name="headerBase"/> default to the
        /// vanilla command.bin layout (0x60 row, 0x14 header).
        /// </summary>
        public static WardLookupReport VerifyWards(
            ReadOnlySpan<byte> grownCommandBin,
            int radiantId = CommandGrowWriter.RadiantWardCommandId,
            int umbralId = CommandGrowWriter.UmbralWardCommandId,
            int stride = CommandGrowWriter.CommandEntrySize,
            int headerBase = 0x14)
        {
            var radiant = Resolve(grownCommandBin, radiantId);
            var umbral = Resolve(grownCommandBin, umbralId);
            return new WardLookupReport
            {
                Radiant = radiant,
                Umbral = umbral,
                RowZeroOffset = headerBase,
                ExpectedRadiantOffset = headerBase + radiantId * stride,
                ExpectedUmbralOffset = headerBase + umbralId * stride,
            };
        }

        static int ReadI16(ReadOnlySpan<byte> b, int off) => (short)(b[off] | (b[off + 1] << 8));
        static int ReadU16(ReadOnlySpan<byte> b, int off) => b[off] | (b[off + 1] << 8);
        static int ReadI32(ReadOnlySpan<byte> b, int off) => b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24);
    }
}
