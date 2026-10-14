using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Regression coverage for the copy-only preview boundary. The selected extraction is a
    /// read-only capability; preview bytes live in app-owned viewer data and are deleted only by
    /// the owner that staged the exact file identity.
    /// </summary>
    [Collection(FileSystemReparseGuardHookCollection.Name)]
    public sealed class MagicOverrideServiceTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work", "magic-override-tests-" + Guid.NewGuid().ToString("N"));

        public MagicOverrideServiceTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            FileSystemReparseGuard.BeforeHandleOperationForTests = null;
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Fact]
        public async Task StageMagicOverride_PreservesSourceAndSelectedDataByteForByte()
        {
            if (OperatingSystem.IsLinux())
            {
                await MagicPreviewNativeAssertions.PreserveSourceAsync();
                return;
            }
            string magicDirectory = Path.Combine(_root, "selected", "data", "FinalFantasyX", "11");
            string viewerDataRoot = Path.Combine(_root, "local-app-data", "viewer-data");
            string source = Path.Combine(_root, "magic_00AF.dll");
            string selectedTarget = Path.Combine(magicDirectory, "00AF.bin");
            Directory.CreateDirectory(magicDirectory);
            byte[] vanilla = { 0x56, 0x41, 0x4E, 0x49, 0x4C, 0x4C, 0x41 };
            byte[] custom = { 0x43, 0x55, 0x53, 0x54, 0x4F, 0x4D };
            File.WriteAllBytes(source, custom);
            File.WriteAllBytes(selectedTarget, vanilla);
            string sourceHashBefore = Sha256(source);
            string selectedHashBefore = Sha256(selectedTarget);

            MagicOverrideResult result = MagicOverrideService.StageMagicOverride(
                0xAF,
                File.ReadAllBytes(source),
                magicDirectory,
                viewerDataRoot);

            Assert.True(result.Success, result.Message);
            Assert.Equal("/data/FinalFantasyX/11/00af.bin", result.RequestPath);
            string stagedPath = Assert.IsType<string>(result.StagedPath);
            Assert.StartsWith(
                Path.GetFullPath(Path.Combine(viewerDataRoot, "magic-overrides")) + Path.DirectorySeparatorChar,
                Path.GetFullPath(stagedPath),
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(custom, File.ReadAllBytes(stagedPath));
            Assert.Equal(sourceHashBefore, Sha256(source));
            Assert.Equal(selectedHashBefore, Sha256(selectedTarget));
            Assert.False(File.Exists(selectedTarget + MagicOverrideService.BackupSuffix));
            Assert.True(MagicOverrideService.TryDeleteStagedOverride(viewerDataRoot, stagedPath));
        }

        [Fact]
        public async Task PublicDocumentSnapshots_CannotAlterPreviewStagingBytes()
        {
            if (OperatingSystem.IsLinux())
            {
                await MagicPreviewNativeAssertions.PublicSnapshotsAsync();
                return;
            }
            string sourceDirectory = Path.Combine(_root, "source-snapshot");
            string source = MagicDllTestFixture.Write(sourceDirectory, "magic_0021.dll");
            byte[] expected = File.ReadAllBytes(source);
            var wrapper = new MagicDllDocument_Wrapper();
            Assert.True(wrapper.TryLoad(source, out string loadError), loadError);
            byte[] exposedWorking = wrapper.WorkingBytes!;
            byte[] exposedSource = wrapper.SourceBytes!;
            exposedWorking[0] ^= 0xFF;
            exposedSource[1] ^= 0xFF;

            string magicDirectory = Path.Combine(_root, "selected-snapshot", "11");
            string viewerDataRoot = Path.Combine(_root, "viewer-snapshot");
            Directory.CreateDirectory(magicDirectory);
            File.WriteAllBytes(Path.Combine(magicDirectory, "0015.bin"), new byte[] { 0x56 });

            MagicOverrideResult result = MagicOverrideService.StageMagicOverride(
                wrapper.MagicId,
                wrapper.WorkingBytes!,
                magicDirectory,
                viewerDataRoot);

            Assert.True(result.Success, result.Message);
            Assert.Equal(expected, File.ReadAllBytes(result.StagedPath!));
            Assert.Equal(expected, wrapper.WorkingBytes);
            Assert.Equal(expected, wrapper.SourceBytes);
            Assert.True(MagicOverrideService.TryDeleteStagedOverride(viewerDataRoot, result.StagedPath!));
        }

        [Fact]
        public async Task SameMagicId_UsesDistinctOwnershipFilesAndDeletesOnlyTheExactOwner()
        {
            if (OperatingSystem.IsLinux())
            {
                await MagicPreviewNativeAssertions.SeparateOwnersAsync();
                return;
            }
            string magicDirectory = Path.Combine(_root, "selected-owner", "data", "FinalFantasyX", "11");
            string viewerDataRoot = Path.Combine(_root, "viewer-owner");
            Directory.CreateDirectory(magicDirectory);
            File.WriteAllBytes(Path.Combine(magicDirectory, "0021.bin"), new byte[] { 0x56 });

            MagicOverrideResult first = MagicOverrideService.StageMagicOverride(
                0x21, new byte[] { 0xA1 }, magicDirectory, viewerDataRoot);
            MagicOverrideResult second = MagicOverrideService.StageMagicOverride(
                0x21, new byte[] { 0xB2 }, magicDirectory, viewerDataRoot);
            Assert.True(first.Success, first.Message);
            Assert.True(second.Success, second.Message);
            Assert.NotEqual(first.StagedPath, second.StagedPath);

            Assert.True(MagicOverrideService.TryDeleteStagedOverride(viewerDataRoot, first.StagedPath!));
            Assert.False(File.Exists(first.StagedPath));
            Assert.True(File.Exists(second.StagedPath));
            Assert.False(MagicOverrideService.TryDeleteStagedOverride(viewerDataRoot, first.StagedPath!));
            Assert.True(MagicOverrideService.TryDeleteStagedOverride(viewerDataRoot, second.StagedPath!));
        }

        [Fact]
        public void StageMagicOverride_RejectsAReparseSelectedDataAncestor()
        {
            if (!OperatingSystem.IsWindows()) return;
            string actual = Path.Combine(_root, "actual-selected");
            string actualMagicDirectory = Path.Combine(actual, "data", "FinalFantasyX", "11");
            string linked = Path.Combine(_root, "linked-selected");
            Directory.CreateDirectory(actualMagicDirectory);
            File.WriteAllBytes(Path.Combine(actualMagicDirectory, "0021.bin"), new byte[] { 0x56 });
            CreateDirectoryLink(linked, actual);
            try
            {
                MagicOverrideResult result = MagicOverrideService.StageMagicOverride(
                    0x21,
                    new byte[] { 0xA1 },
                    Path.Combine(linked, "data", "FinalFantasyX", "11"),
                    Path.Combine(_root, "viewer-safe"));

                Assert.False(result.Success);
                Assert.Equal(new byte[] { 0x56 }, File.ReadAllBytes(Path.Combine(actualMagicDirectory, "0021.bin")));
            }
            finally
            {
                Directory.Delete(linked);
            }
        }

        [Fact]
        public void StageMagicOverride_PathSwapBeforeOwnedDirectoryOpen_FailsWithoutExternalWrite()
        {
            if (!OperatingSystem.IsWindows()) return;
            string magicDirectory = Path.Combine(_root, "selected-swap", "data", "FinalFantasyX", "11");
            string viewerDataRoot = Path.Combine(_root, "viewer-swap");
            string stagingDirectory = Path.Combine(viewerDataRoot, "magic-overrides", "11");
            string movedViewerData = Path.Combine(_root, "viewer-swap-original");
            string external = Path.Combine(_root, "external-target");
            Directory.CreateDirectory(magicDirectory);
            Directory.CreateDirectory(stagingDirectory);
            Directory.CreateDirectory(external);
            File.WriteAllBytes(Path.Combine(magicDirectory, "0021.bin"), new byte[] { 0x56 });
            bool swapped = false;
            FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
            {
                if (swapped || operation != "open-directory" ||
                    !string.Equals(path, Path.GetFullPath(stagingDirectory), StringComparison.OrdinalIgnoreCase))
                    return;
                swapped = true;
                Directory.Move(viewerDataRoot, movedViewerData);
                CreateDirectoryLink(viewerDataRoot, external);
            };

            try
            {
                MagicOverrideResult result = MagicOverrideService.StageMagicOverride(
                    0x21, new byte[] { 0xA1 }, magicDirectory, viewerDataRoot);

                Assert.True(swapped);
                Assert.False(result.Success);
                Assert.Empty(Directory.EnumerateFiles(external, "*", SearchOption.AllDirectories));
            }
            finally
            {
                FileSystemReparseGuard.BeforeHandleOperationForTests = null;
                if (Directory.Exists(viewerDataRoot)) Directory.Delete(viewerDataRoot);
            }
        }

        [Fact]
        public void StageMagicOverride_DirectoryRenameBeforePromotedRead_IsBlockedUntilCleanup()
        {
            if (!OperatingSystem.IsWindows()) return;
            string magicDirectory = Path.Combine(_root, "selected-late-rename", "11");
            string viewerDataRoot = Path.Combine(_root, "viewer-late-rename");
            string stagingDirectory = Path.Combine(viewerDataRoot, "magic-overrides", "11");
            string movedStaging = Path.Combine(_root, "magic-overrides-moved");
            Directory.CreateDirectory(magicDirectory);
            File.WriteAllBytes(Path.Combine(magicDirectory, "0021.bin"), new byte[] { 0x56 });
            bool attempted = false;
            bool blocked = false;
            FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
            {
                string stagingPrefix = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
                if (attempted || operation != "open-read-relative" ||
                    !Path.GetFullPath(path).StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
                    return;
                attempted = true;
                try { Directory.Move(stagingDirectory, movedStaging); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    blocked = true;
                }
            };

            MagicOverrideResult result = MagicOverrideService.StageMagicOverride(
                0x21,
                new byte[] { 0xA1, 0xB2 },
                magicDirectory,
                viewerDataRoot);

            Assert.True(result.Success, result.Message);
            Assert.True(attempted);
            Assert.True(blocked);
            Assert.False(Directory.Exists(movedStaging));
            Assert.Equal(new byte[] { 0xA1, 0xB2 }, File.ReadAllBytes(result.StagedPath!));
            Assert.True(MagicOverrideService.TryDeleteStagedOverride(viewerDataRoot, result.StagedPath!));
        }

        private static string Sha256(string file)
        {
            using FileStream stream = File.OpenRead(file);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static void CreateDirectoryLink(string link, string target)
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Directory junctions need no Developer Mode and exercise the same reparse guard.
                var startInfo = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add("mklink");
                startInfo.ArgumentList.Add("/J");
                startInfo.ArgumentList.Add(link);
                startInfo.ArgumentList.Add(target);
                using Process process = Process.Start(startInfo)!;
                Assert.True(process.WaitForExit(5000), "mklink /J timed out");
                Assert.Equal(0, process.ExitCode);
            }
        }
    }
}
