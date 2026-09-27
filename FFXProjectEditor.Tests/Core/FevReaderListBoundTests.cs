using System;
using System.IO;
using System.Linq;
using FevReference;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// Regression tests for the FEV (FMOD Event, RIFF) chunk-walk bound fix — queue
    /// item 6 in docs/reverse/FFX_LOST_WORK_RECOVERY_2026-09-15.md.
    ///
    /// The old FevReader.ParseChunks walked chunks up to `data.Length - 8`, which made
    /// the trailing LIST chunk fail the overflow check (LIST ends exactly at EOF on
    /// intact banks) so only FMT was ever surfaced — "LIST final dá OVERFLOW",
    /// artifacts/2026-09-15/script-validation/menu-phyre-magic-audio-encoding.md §4.
    /// Layout evidence (docs/reverse/FFX_STRUCTURE_COMPLETE_2026-09-14.md §11.24 +
    /// real banks): the RIFF u32 at 0x04 holds fileSize-8, so the payload ends at
    /// 8 + riffSize == data.Length, never 8 bytes short.
    ///
    /// Fixtures are real banks copied next to the test assembly by the
    /// "Fixtures\**\*.fev" content glob in FFXProjectEditor.Tests.csproj:
    ///  - 0328.fev       — FFX PC SFX bank (sound_pc/sfx/us), same family as the lost
    ///                     404,335 B work file targeted by fev_lgcy_parse.py (same
    ///                     layout: OBCT@0x24 size 268, PROP size 9, LGCY body @0x152);
    ///  - ffx2_music.fev — FFX-2 PC music bank, the sample that exposed the overflow
    ///                     during the 2026-09-15 script validation.
    /// </summary>
    public class FevReaderListBoundTests
    {
        public static TheoryData<string> RealBankFixtures => new()
        {
            { "0328.fev" },
            { "ffx2_music.fev" },
        };

        static string FixturePath(string name) =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Audio", name);

        // --- Bound regression: the trailing LIST must survive the walk ------------

        [Theory]
        [MemberData(nameof(RealBankFixtures))]
        public void ParseChunks_KeepsTrailingListChunk(string fixtureName)
        {
            byte[] data = File.ReadAllBytes(FixturePath(fixtureName));

            var chunks = FevReader.ParseChunks(data);

            Assert.Contains(chunks, c => c.Id == "FMT ");
            FevChunk list = Assert.Single(chunks, c => c.Id == "LIST");
            // LIST is the last chunk and its body must end exactly at EOF: this is
            // the assertion the old `data.Length - 8` bound broke (overflow -> drop).
            Assert.Equal(data.Length, list.BodyOffset + list.Size);
        }

        [Theory]
        [MemberData(nameof(RealBankFixtures))]
        public void ParseChunks_WalkCoversFullRiffPayload(string fixtureName)
        {
            byte[] data = File.ReadAllBytes(FixturePath(fixtureName));

            var chunks = FevReader.ParseChunks(data);

            // On intact banks the declared RIFF size accounts for every byte...
            int riffSize = BitConverter.ToInt32(data, 4);
            Assert.Equal(data.Length, 8 + riffSize);
            // ...and the walk consumes the whole payload: after the final chunk there
            // is no room left for another chunk header.
            FevChunk last = chunks[^1];
            int walkEnd = last.BodyOffset + last.Size + (last.Size & 1);
            Assert.True(walkEnd + 8 > data.Length,
                $"walk stopped early: last chunk ends at 0x{walkEnd:X}, file is 0x{data.Length:X}");
        }

        [Theory]
        [MemberData(nameof(RealBankFixtures))]
        public void ReadVersion_FmodEx0x45(string fixtureName)
        {
            byte[] data = File.ReadAllBytes(FixturePath(fixtureName));

            Assert.Equal(0x00450000u, FevReader.ReadVersion(data));
        }

        // --- Bound clamp: editor-appended FFX2SEID trailer must not be walked -----

        [Fact]
        public void ParseChunks_TrailerAppendedFile_ClampsToDeclaredRiffEnd()
        {
            byte[] data = File.ReadAllBytes(FixturePath("0328.fev"));
            // Simulate a bank edited by FevLegacySequenceWriter-style append: the
            // editor pastes a 20-byte FFX2SEID registration trailer AFTER the RIFF
            // payload (see FevLegacyReader.TryReadRegistrationTrailer /
            // docs/reverse/FFX_STRUCTURE_COMPLETE_2026-09-14.md §11.24 item 2.10).
            byte[] withTrailer = data
                .Concat(new byte[8] { (byte)'F', (byte)'F', (byte)'X', (byte)'2',
                                      (byte)'S', (byte)'E', (byte)'I', (byte)'D' })
                .Concat(BitConverter.GetBytes(9999u))
                .Concat(BitConverter.GetBytes(3u))
                .Concat(BitConverter.GetBytes(9998u))
                .ToArray();
            Assert.Equal(data.Length + 20, withTrailer.Length);

            var chunks = FevReader.ParseChunks(withTrailer);

            // The walk must stop at the declared RIFF end (data.Length of the intact
            // part) instead of trying to parse the trailer bytes as a chunk header.
            Assert.Equal(2, chunks.Count);
            Assert.Equal("LIST", chunks[1].Id);
            Assert.Equal(data.Length, chunks[1].BodyOffset + chunks[1].Size);
        }
    }
}
