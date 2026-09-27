using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using FFXProjectEditor.FfxLib.Ability;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// RT0 round-trip tests for the battle kernel command writer chain (P1 backlog B1):
    /// a no-edit save through <see cref="Ability_Command.WriteList"/> must reproduce the
    /// input byte-for-byte (the captured Original*Offset text-pool preservation path),
    /// and a fixed-block edit must be byte-local and read back to the new value.
    ///
    /// Fixtures are vanilla kernel files from the official corpus (inpc/battle/kernel):
    ///   - command.bin  -> Command entries with extra info (0x60)
    ///   - a_ability.bin -> Monster abilities without extra info (0x5C)
    /// They are copied next to the test assembly by the "Fixtures\**\*.bin" content glob.
    /// </summary>
    public class AbilityCommandRoundTripTests
    {
        public static TheoryData<string> KernelFixtures => new()
        {
            // Player/Aeon command table (has extra info, RT0-certified by FFX_ABILITY_WRITER_AUDIT_2026-06-05).
            { "command.bin" },
            // Item table (has extra info, RT0-certified).
            { "item.bin" },
            // Monster ability table (no extra info). The preserved-text-pool writer made
            // its no-edit round-trip byte-identical (previously documented as drift in
            // FFX_ABILITY_WRITER_AUDIT_2026-06-05 - superseded 2026-07-31 by these tests).
            { "monmagic2.bin" },
        };

        // --- Round-trip (no-edit byte-identity) -----------------------------------

        [Theory]
        [MemberData(nameof(KernelFixtures))]
        public void WriteList_NoEditRoundTrip_IsByteIdentical(string fixtureName)
        {
            byte[] original = File.ReadAllBytes(FixturePath(fixtureName));
            bool hasExtra = DetectExtraInfo(original);

            List<Ability_Command> commands = Ability_Command.ReadList(original, hasExtra);
            byte[] rewritten = Ability_Command.WriteList(commands, hasExtra);

            Assert.True(
                original.AsSpan().SequenceEqual(rewritten),
                $"no-edit save drifted: orig={original.Length} re={rewritten.Length}, " +
                $"first diff @0x{FirstDifference(original, rewritten):X}, hasExtra={hasExtra}, count={commands.Count}");
        }

        // --- Fixed-block edit -------------------------------------------------------

        [Theory]
        [MemberData(nameof(KernelFixtures))]
        public void WriteList_FixedFieldEdit_ReadsBackAndKeepsLength(string fixtureName)
        {
            byte[] original = File.ReadAllBytes(FixturePath(fixtureName));
            bool hasExtra = DetectExtraInfo(original);
            List<Ability_Command> commands = Ability_Command.ReadList(original, hasExtra);
            Assert.NotEmpty(commands);

            // Flip CostMp on the first command (byte 0..255; use a value guaranteed different).
            Ability_Command first = commands[0];
            byte originalCost = first.CostMp;
            byte newCost = (byte)(originalCost == 0 ? 42 : 0);
            first.CostMp = newCost;

            byte[] edited = Ability_Command.WriteList(commands, hasExtra);

            // Length must be preserved (fixed-size records; pool untouched).
            Assert.Equal(original.Length, edited.Length);

            // The edit must read back.
            List<Ability_Command> reread = Ability_Command.ReadList(edited, hasExtra);
            Assert.Equal(newCost, reread[0].CostMp);

            // And the file must actually differ (edit is not a no-op).
            Assert.False(original.AsSpan().SequenceEqual(edited), "edited file must differ from original");
        }

        // --- Adapter contract -------------------------------------------------------

        [Fact]
        public void Adapter_ValidateEdits_RequiresCommandIndex()
        {
            var adapter = new AbilityCommandAdapter();

            var errors = adapter.ValidateEdits(new Dictionary<string, object> { ["CostMp"] = (byte)5 });

            Assert.NotEmpty(errors);
            Assert.Contains("CommandIndex", errors[0]);
        }

        [Fact]
        public void Adapter_ValidateEdits_RejectsUnknownField()
        {
            var adapter = new AbilityCommandAdapter();

            var errors = adapter.ValidateEdits(new Dictionary<string, object>
            {
                [AbilityCommandAdapter.CommandIndexField] = 0,
                ["NameScriptBytes"] = new byte[] { 0x41 },
            });

            Assert.Contains(errors, e => e.Contains("NameScriptBytes"));
        }

        [Fact]
        public void Adapter_StageAsync_AppliesEditAndChangesHash()
        {
            string fixture = FixturePath("command.bin");
            string tempDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx_ability_adapter_" + Guid.NewGuid().ToString("N"));
            try
            {
                string staged = Path.Combine(tempDir, "command.bin");
                var adapter = new AbilityCommandAdapter();
                string before = adapter.ComputeBeforeHash(fixture);

                string after = adapter.StageAsync(
                    fixture,
                    staged,
                    new Dictionary<string, object>
                    {
                        [AbilityCommandAdapter.CommandIndexField] = 0,
                        ["CostMp"] = (byte)1,
                        ["HitCount"] = (byte)3,
                    }).GetAwaiter().GetResult();

                Assert.True(File.Exists(staged), "staged file must exist");
                Assert.NotEqual(before, after);
                Assert.NotEqual(before, adapter.ComputeBeforeHash(staged));

                // Staged file must read back with the edited values.
                byte[] stagedBytes = File.ReadAllBytes(staged);
                var reread = Ability_Command.ReadList(stagedBytes, DetectExtraInfo(stagedBytes));
                Assert.Equal((byte)1, reread[0].CostMp);
                Assert.Equal((byte)3, reread[0].HitCount);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void Adapter_StageAsync_IndexOutOfRange_Throws()
        {
            string fixture = FixturePath("command.bin");
            string tempDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx_ability_adapter_" + Guid.NewGuid().ToString("N"));
            try
            {
                string staged = Path.Combine(tempDir, "command.bin");
                var adapter = new AbilityCommandAdapter();

                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => adapter.StageAsync(
                    fixture,
                    staged,
                    new Dictionary<string, object>
                    {
                        [AbilityCommandAdapter.CommandIndexField] = 99999,
                        ["CostMp"] = (byte)1,
                    }).GetAwaiter().GetResult());

                Assert.Contains("out of range", ex.Message);
                Assert.False(File.Exists(staged), "nothing must be staged on failure");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void ReadList_CorruptedData_ThrowsInsteadOfSilentTruncation()
        {
            // A count far beyond what the buffer can hold must not silently truncate.
            // Header: Signature(1)+Unknown(7)+PreviousFileCount(2)+EntryCount(2)@10+EntrySize(2)@12.
            byte[] corrupted = new byte[64];
            corrupted[0] = 1;                                  // signature
            BitConverter.GetBytes((short)0x7FFF).CopyTo(corrupted, 10); // 32767 entries
            BitConverter.GetBytes((short)0x60).CopyTo(corrupted, 12);   // entry size (extra info)

            Assert.ThrowsAny<Exception>(() => Ability_Command.ReadList(corrupted, hasExtraInfo: true));
        }

        // --- Helpers ----------------------------------------------------------------

        private static string FixturePath(string fixtureName) =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Battle", fixtureName);

        private static bool DetectExtraInfo(byte[] fileBytes)
        {
            if (fileBytes.Length < 16)
                return false;

            short entrySize = BitConverter.ToInt16(fileBytes, 12);
            short entryCount = BitConverter.ToInt16(fileBytes, 10);
            if (entryCount < 0 || entrySize <= 0)
                return false;

            return entrySize == 0x60;
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



