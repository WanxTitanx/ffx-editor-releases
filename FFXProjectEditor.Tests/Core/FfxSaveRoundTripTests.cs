using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.IO;
using FFXProjectEditor.FfxLib.Save;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// RT0 round-trip tests for the FFX save container chain (<see cref="FfxSaveFile"/>):
    /// a session created in memory, written to disk and loaded back must reproduce the
    /// prepared payload byte-for-byte, and each container format must keep its wrapper
    /// bytes (PC .ffx header/footer, BIN metadata tail) stable across the round trip.
    ///
    /// The in-memory payload is always <see cref="FfxSaveCore.DataSize"/> (25848) bytes
    /// in FFXED v0.749-compatible layout; <see cref="FfxSaveCore.PrepareForSave"/> stamps
    /// the tamper tag at payload+32 and applies the checksum on every save.
    /// </summary>
    public class FfxSaveRoundTripTests
    {
        const string TestLabel = "RT2 Round Trip";

        // FFXED tamper tag written by PrepareForSave at payload+32 (see FfxSaveCore).
        static readonly byte[] TamperTag =
        {
            84, 115, 120, 131, 116, 115, 58, 113, 136, 58, 85, 85, 103, 84, 83,
        };

        // --- Raw PS2 payload (25848 bytes) -------------------------------------

        [Fact]
        public void RawPs2_CreateWriteReadBack_IsByteIdentical()
        {
            string path = TempPath("save_raw.bin");

            try
            {
                FfxSaveCore core = CreateTestCore();
                FfxSaveFile session = FfxSaveFile.CreateSession(
                    core, FfxSaveFormat.RawPs2, path, TestLabel);

                session.Save();
                byte[] onDisk = File.ReadAllBytes(path);
                Assert.Equal(FfxSaveCore.DataSize, onDisk.Length);

                FfxSaveFile loaded = FfxSaveFile.Load(path);

                Assert.Equal(FfxSaveFormat.RawPs2, loaded.Format);
                Assert.Equal(path, loaded.SourcePath);
                Assert.Equal(Path.GetFileName(path), loaded.DisplayLabel);
                Assert.True(
                    core.Data.AsSpan().SequenceEqual(loaded.Core.Data),
                    $"raw payload drifted: first diff @0x{FirstDifference(core.Data, loaded.Core.Data):X}");
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void Save_WritesTamperTag()
        {
            string path = TempPath("save_tag.bin");

            try
            {
                FfxSaveFile session = FfxSaveFile.CreateSession(
                    CreateTestCore(), FfxSaveFormat.RawPs2, path, TestLabel);

                session.Save();
                byte[] onDisk = File.ReadAllBytes(path);

                // PrepareForSave stamps the FFXED tamper tag at payload+32.
                Assert.True(
                    onDisk.AsSpan(32, TamperTag.Length).SequenceEqual(TamperTag),
                    "tamper tag not stamped at payload+32");
            }
            finally
            {
                Cleanup(path);
            }
        }

        // --- PC .ffx (GENUINE: payload@0 + 1032 tail = 26880; fixed 2026-09-14) --

        [Fact]
        public void PcFfx_RoundTrips_PayloadAtZeroWithTail()
        {
            string path = TempPath("save.ffx");

            try
            {
                FfxSaveFile session = FfxSaveFile.CreateSession(
                    CreateTestCore(), FfxSaveFormat.PcFfx, path, TestLabel);

                session.Save();
                byte[] onDisk = File.ReadAllBytes(path);

                // Genuine game layout (81/81 real saves, FFX_SAVES_HUNT_2026-09-14):
                // payload@0 + 1032 tail = 26880 — NO 0x40 header.
                int expectedLength = FfxSaveCore.DataSize + FfxSaveFile.PcFfxFooterSize;
                Assert.Equal(expectedLength, onDisk.Length);
                Assert.False(session.PcFfxLegacyEditorLayout);

                FfxSaveFile loaded = FfxSaveFile.Load(path);

                Assert.Equal(FfxSaveFormat.PcFfx, loaded.Format);
                Assert.False(loaded.PcFfxLegacyEditorLayout);
                Assert.True(
                    session.Core.Data.AsSpan().SequenceEqual(loaded.Core.Data),
                    $"PC .ffx payload drifted: first diff @0x{FirstDifference(session.Core.Data, loaded.Core.Data):X}");

                Assert.NotNull(loaded.PcFfxFooter);
                Assert.Equal(FfxSaveFile.PcFfxFooterSize, loaded.PcFfxFooter!.Length);
                Assert.True(onDisk.AsSpan(FfxSaveCore.DataSize).SequenceEqual(loaded.PcFfxFooter),
                    "PC .ffx tail bytes were not preserved across the round trip");
            }
            finally
            {
                Cleanup(path);
            }
        }

        [Fact]
        public void PcFfx_LegacyEditorLayout_IsReadAndResavedAsGenuine()
        {
            string legacyPath = TempPath("legacy.ffx");
            string genuinePath = TempPath("genuine.ffx");

            try
            {
                // Build a legacy 26944 file (0x40 header + payload-with-tag + footer) the way
                // older editor builds wrote it.
                FfxSaveCore core = CreateTestCore();
                core.PrepareForSave();
                byte[] legacy = new byte[FfxSaveFile.PcFfxHeaderSize + FfxSaveCore.DataSize + FfxSaveFile.PcFfxFooterSize];
                Array.Copy(core.Data, 0, legacy, FfxSaveFile.PcFfxHeaderSize, FfxSaveCore.DataSize);
                File.WriteAllBytes(legacyPath, legacy);

                FfxSaveFile loaded = FfxSaveFile.Load(legacyPath);
                Assert.Equal(FfxSaveFormat.PcFfx, loaded.Format);
                Assert.True(loaded.PcFfxLegacyEditorLayout, "legacy 26944 layout must be detected");
                Assert.True(
                    core.Data.AsSpan().SequenceEqual(loaded.Core.Data),
                    "legacy payload must still load from @0x40");

                loaded.Save(genuinePath);
                byte[] onDisk = File.ReadAllBytes(genuinePath);
                Assert.Equal(FfxSaveCore.DataSize + FfxSaveFile.PcFfxFooterSize, onDisk.Length);
                Assert.False(loaded.PcFfxLegacyEditorLayout);
                Assert.True(onDisk.AsSpan(0, FfxSaveCore.DataSize).SequenceEqual(core.Data),
                    "re-save must emit the genuine payload@0 layout");
            }
            finally
            {
                Cleanup(legacyPath);
                Cleanup(genuinePath);
            }
        }

        // --- Gil mirror refresh (FFX_SAVE_EXP finding F1, 2026-09-14) ------------

        [Fact]
        public void EditingGil_RefreshesHeaderMirror()
        {
            string fixtureDir = FindFixturesDir();
            string source = Path.Combine(fixtureDir, "user_ffx_000");
            if (!File.Exists(source))
                return; // fixtures absent in trimmed checkouts
            string edited = TempPath("gil_mirror.ffx");

            try
            {
                FfxSaveFile session = FfxSaveFile.Load(source);
                // Bump gil by 1 through the same offset the inventory editor writes.
                int gil = session.Core.ReadInt32Le(15752, 4);
                session.Core.WriteInt32Le(15752, gil + 1, 4);
                session.Save(edited);

                byte[] onDisk = File.ReadAllBytes(edited);
                int mirror = BitConverter.ToInt32(onDisk, 0x14);
                int stored = BitConverter.ToInt32(onDisk, 15752);
                Assert.Equal(stored, mirror);
                Assert.Equal(gil + 1, stored);
            }
            finally
            {
                Cleanup(edited);
            }
        }

        // --- Real-corpus regression (genuine user/converter saves, 2026-09-14) --

        [Theory]
        [InlineData("user_ffx_000")]
        [InlineData("converter_steam")]
        public void PcFfx_RealCorpus_LoadsValidatesAndRoundTrips(string fixtureName)
        {
            string fixtureDir = FindFixturesDir();
            string path = Path.Combine(fixtureDir, fixtureName);
            if (!File.Exists(path))
                return; // fixtures absent in trimmed checkouts

            byte[] original = File.ReadAllBytes(path);
            Assert.Equal(FfxSaveCore.DataSize + FfxSaveFile.PcFfxFooterSize, original.Length);

            FfxSaveFile loaded = FfxSaveFile.Load(path);
            Assert.Equal(FfxSaveFormat.PcFfx, loaded.Format);
            Assert.False(loaded.PcFfxLegacyEditorLayout);
            Assert.True(loaded.ValidateGameChecksum(),
                $"game CRC mismatch on genuine save {fixtureName} (loader must read payload@0)");

            string copy = TempPath(fixtureName + ".rt");
            try
            {
                loaded.Save(copy);
                Assert.True(
                    File.ReadAllBytes(copy).AsSpan().SequenceEqual(original),
                    $"real save {fixtureName} must round-trip byte-identically (unmodified payload)");
            }
            finally
            {
                Cleanup(copy);
            }
        }

        static string FindFixturesDir()
        {
            // Test bin dir -> project dir -> repo root -> FFXProjectEditor.Tests/Fixtures/Save
            string? dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "FFXProjectEditor.Tests", "Fixtures", "Save");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return Path.Combine(AppContext.BaseDirectory, "Fixtures", "Save");
        }

        // --- BIN export (payload + 1024 tail, 26872 total) ----------------------

        [Fact]
        public void SaveAs_Bin_Produces26872Bytes_AndRoundTrips()
        {
            string path = TempPath("save.bin");

            try
            {
                FfxSaveFile session = FfxSaveFile.CreateSession(
                    CreateTestCore(), FfxSaveFormat.RawPs2, path, TestLabel);

                session.SaveAs(path, FfxSaveFormat.Bin);
                Assert.Equal(FfxSaveCore.DataSize + 1024, new FileInfo(path).Length);

                FfxSaveFile loaded = FfxSaveFile.Load(path);

                Assert.Equal(FfxSaveFormat.Bin, loaded.Format);
                Assert.True(
                    session.Core.Data.AsSpan().SequenceEqual(loaded.Core.Data),
                    $"BIN payload drifted: first diff @0x{FirstDifference(session.Core.Data, loaded.Core.Data):X}");
            }
            finally
            {
                Cleanup(path);
            }
        }

        // --- Helpers --------------------------------------------------------------

        private static FfxSaveCore CreateTestCore()
        {
            var core = new FfxSaveCore();
            core.Data[0] = 0x01;
            core.WriteInt32Le(4, 0x1234ABCD, 4);
            core.WriteFfxString(56, TestLabel);
            return core;
        }

        private static string TempPath(string fileName)
        {
            string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx_save_rt0_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, fileName);
        }

        private static void Cleanup(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                string? dir = Path.GetDirectoryName(path);
                if (dir != null && Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; never mask the assertion result.
            }
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

