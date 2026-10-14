using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;
using Xe.BinaryMapper;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// RT0 round-trip tests for the Monster writer chain (backlog #4 / --monster-rt0):
    /// a no-edit save through <see cref="Monster_File.Write"/> must reproduce the input
    /// byte-for-byte, and <see cref="Monster_StatSheet.WriteSingle"/> must preserve its
    /// section (preserve-only path: TextScriptInfo + padding + text pool kept, only the
    /// fixed stat block re-stamped at +0x14).
    ///
    /// Fixtures are vanilla monster_*.bin files from the official corpus
    /// (jppc/battle/mon). They are copied next to the test assembly by the
    /// "Fixtures\**\*.bin" content glob in FFXProjectEditor.Tests.csproj.
    /// </summary>
    public class MonsterStatSheetRoundTripTests
    {
        // The fixed stat block starts right after the 5 TextScriptInfo records (5 * 4 bytes).
        // Mirrors Monster_StatSheet.StatBlockOffset.
        const int StatBlockOffset = 0x14;

        // Header field offsets (MonsterHeaderFile, little-endian int32s).
        const int HeaderSignatureOffset = 0x00;
        const int HeaderStatSheetPointerOffset = 0x0C;
        const int HeaderFileSizeOffset = 0x20;

        public static TheoryData<string> MonsterFixtures => new()
        {
            // m000: every section present; the stat sheet shares two text scripts
            // (UnusedText1/UnusedText2 point at the same offset) - the case that made
            // the pre-preserve-only writer drift.
            { "m000.bin" },
            // m001: full-size vanilla monster (64 KB).
            { "m001.bin" },
        };

        // --- Round-trip (no-edit byte-identity) -----------------------------------

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void Write_NoEditRoundTrip_IsByteIdentical(string fixtureName)
        {
            byte[] original = File.ReadAllBytes(FixturePath(fixtureName));

            byte[] rewritten = Monster_File.Read(original).Write();

            Assert.True(
                original.AsSpan().SequenceEqual(rewritten),
                $"no-edit save drifted: orig={original.Length} re={rewritten.Length}, " +
                $"first diff @0x{FirstDifference(original, rewritten):X}");
        }

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void Write_PreservesHeaderFields(string fixtureName)
        {
            byte[] original = File.ReadAllBytes(FixturePath(fixtureName));

            byte[] rewritten = Monster_File.Read(original).Write();

            // Signature (0x00), the 7 section pointers (0x04..0x1C) and FileSize (0x20)
            // must be identical between the original and the rewritten header.
            for (int offset = HeaderSignatureOffset; offset <= HeaderFileSizeOffset; offset += 4)
            {
                Assert.Equal(
                    BitConverter.ToInt32(original, offset),
                    BitConverter.ToInt32(rewritten, offset));
            }
        }

        // --- StatSheet section round-trip (preserve-only writer) ------------------

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void StatSheet_NoEditRoundTrip_IsByteIdentical(string fixtureName)
        {
            byte[] section = ExtractStatSheetSection(File.ReadAllBytes(FixturePath(fixtureName)));

            byte[] rewritten = Monster_StatSheet.ReadSingle(section).WriteSingle();

            Assert.True(
                section.AsSpan().SequenceEqual(rewritten),
                $"stat sheet section drifted: first diff @0x{FirstDifference(section, rewritten):X}");
        }

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void StatSheet_HpEdit_IsByteLocalToStatBlock(string fixtureName)
        {
            byte[] section = ExtractStatSheetSection(File.ReadAllBytes(FixturePath(fixtureName)));
            Monster_StatSheet statSheet = Monster_StatSheet.ReadSingle(section);

            int statBlockLength = StatBlockLength(statSheet);
            statSheet.Hp += 1;
            byte[] edited = statSheet.WriteSingle();

            Assert.Equal(section.Length, edited.Length);
            Assert.True(
                section.AsSpan(0, StatBlockOffset).SequenceEqual(edited.AsSpan(0, StatBlockOffset)),
                "TextScriptInfo block must be preserved on an edit");
            Assert.False(
                section.AsSpan(StatBlockOffset, statBlockLength).SequenceEqual(edited.AsSpan(StatBlockOffset, statBlockLength)),
                "the fixed stat block should change when a stat is edited");
            Assert.True(
                section.AsSpan(StatBlockOffset + statBlockLength).SequenceEqual(edited.AsSpan(StatBlockOffset + statBlockLength)),
                "text pool + padding tail must be preserved on an edit");
        }

        // --- Helpers ----------------------------------------------------------------

        private static string FixturePath(string fixtureName) =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", fixtureName);

        /// <summary>Serializes the fixed stat block exactly like <see cref="Monster_StatSheet.WriteSingle"/>
        /// does internally, returning its byte length (the re-stamped slot inside the section).</summary>
        private static int StatBlockLength(Monster_StatSheet statSheet)
        {
            using MemoryStream stream = new MemoryStream();
            BinaryMapping.WriteObject<Monster_StatSheet>(stream, statSheet);
            return (int)stream.Length;
        }

        /// <summary>Extracts the stat sheet section from a monster file: from StatSheetPointer up to the
        /// next section start (Spoils, else Loot, else Audio, else Text, else end-of-file), mirroring the
        /// pointer walk in <see cref="Monster_File.Read"/>.</summary>
        private static byte[] ExtractStatSheetSection(byte[] monsterFile)
        {
            int statSheetPointer = BitConverter.ToInt32(monsterFile, HeaderStatSheetPointerOffset);
            int[] sectionEnds =
            {
                BitConverter.ToInt32(monsterFile, 0x10), // SpoilsFilePointer
                BitConverter.ToInt32(monsterFile, 0x14), // LootFilePointer
                BitConverter.ToInt32(monsterFile, 0x18), // AudioFilePointer
                BitConverter.ToInt32(monsterFile, 0x1C), // TextFilePointer
                BitConverter.ToInt32(monsterFile, HeaderFileSizeOffset),
            };

            int sectionEnd = sectionEnds.Where(p => p > statSheetPointer).Min();
            return monsterFile[statSheetPointer..sectionEnd];
        }

        private static int FirstDifference(byte[] a, byte[] b)
        {
            int length = Math.Min(a.Length, b.Length);
            for (int i = 0; i < length; i++)
                if (a[i] != b[i])
                    return i;
            return a.Length == b.Length ? -1 : length;
        }
    }
}
