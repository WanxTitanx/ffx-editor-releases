using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.IO;
using FFXProjectEditor.Core;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    public class WorkspaceHealthCheckTests : IDisposable
    {
        private readonly string _tempDir;

        public WorkspaceHealthCheckTests()
        {
            _tempDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"ws_health_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public void Inspect_NonExistentPath_ThrowsDirectoryNotFound()
        {
            var sut = new WorkspaceHealthCheck();
            Assert.Throws<DirectoryNotFoundException>(
                () => sut.Inspect(@"C:\nonexistent_path_that_does_not_exist"));
        }

        [Fact]
        public void Inspect_EmptyWorkspace_ReturnsUnknownVersion()
        {
            var sut = new WorkspaceHealthCheck();
            var result = sut.Inspect(_tempDir);

            Assert.Equal("UNKNOWN", result.DetectedVersion);
            Assert.Equal(Platform.All, result.DetectedPlatform);
        }

        [Fact]
        public void Inspect_WithFfxExe_DetectsPcPlatform()
        {
            File.WriteAllText(Path.Combine(_tempDir, "ffx.exe"), "fake");

            var sut = new WorkspaceHealthCheck();
            var result = sut.Inspect(_tempDir);

            Assert.Equal(Platform.PC, result.DetectedPlatform);
            Assert.Equal("PC_STEAM", result.DetectedVersion);
        }

        [Fact]
        public void Inspect_WithFfxDat_DetectsPs2Platform()
        {
            File.WriteAllText(Path.Combine(_tempDir, "ffx.dat"), "fake");

            var sut = new WorkspaceHealthCheck();
            var result = sut.Inspect(_tempDir);

            Assert.Equal(Platform.PS2, result.DetectedPlatform);
            Assert.Equal("PS2_NTSC", result.DetectedVersion);
        }

        [Fact]
        public void Inspect_WithMasterDir_ProducesNoWarningForMaster()
        {
            Directory.CreateDirectory(Path.Combine(_tempDir, "master"));

            var sut = new WorkspaceHealthCheck();
            var result = sut.Inspect(_tempDir);

            Assert.DoesNotContain(result.Warnings, w => w.Contains("master"));
        }

        [Fact]
        public void Inspect_MissingExpectedDirs_ProducesWarnings()
        {
            var sut = new WorkspaceHealthCheck();
            var result = sut.Inspect(_tempDir);

            Assert.Contains(result.Warnings, w => w.Contains("monster"));
            Assert.Contains(result.Warnings, w => w.Contains("battle"));
        }

        [Fact]
        public void Inspect_OutputPath_Set_InResult()
        {
            var outputDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"ws_out_{Guid.NewGuid():N}");
            Directory.CreateDirectory(outputDir);

            try
            {
                var sut = new WorkspaceHealthCheck();
                var result = sut.Inspect(_tempDir, outputDir);

                Assert.NotNull(result.OutputPath);
                Assert.Equal(Path.GetFullPath(outputDir), result.OutputPath);
            }
            finally
            {
                if (Directory.Exists(outputDir))
                    Directory.Delete(outputDir, recursive: true);
            }
        }

        [Fact]
        public void Inspect_OutputPath_Null_ResultsInNullOutput()
        {
            var sut = new WorkspaceHealthCheck();
            var result = sut.Inspect(_tempDir);

            Assert.Null(result.OutputPath);
        }

        [Fact]
        public void Inspect_ScanTimestamp_IsRecent()
        {
            var sut = new WorkspaceHealthCheck();
            var before = DateTimeOffset.UtcNow;
            var result = sut.Inspect(_tempDir);
            var after = DateTimeOffset.UtcNow;

            Assert.InRange(result.ScanTimestamp, before.AddSeconds(-1), after.AddSeconds(1));
        }

        [Fact]
        public void Inspect_CapabilitiesList_InitiallyEmpty()
        {
            var sut = new WorkspaceHealthCheck();
            var result = sut.Inspect(_tempDir);

            Assert.NotNull(result.Capabilities);
            Assert.Empty(result.Capabilities);
        }

        [Fact]
        public void Inspect_FullWorkspace_AllDirsPresent_NoWarnings()
        {
            foreach (var dir in new[] { "master", "monster", "battle", "field", "save" })
                Directory.CreateDirectory(Path.Combine(_tempDir, dir));

            var sut = new WorkspaceHealthCheck();
            var result = sut.Inspect(_tempDir);

            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void DetectPlatform_ReturnsAll_WhenNoSignaturesFound()
        {
            var sut = new WorkspaceHealthCheck();
            var platform = sut.DetectPlatform(_tempDir);
            Assert.Equal(Platform.All, platform);
        }
    }
}
