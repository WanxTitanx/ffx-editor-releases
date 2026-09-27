using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.AuroraChamber;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

/// <summary>
/// Regression coverage for the read-only boundary between Aurora battle previews and the
/// user-selected NoClip extraction. Preview bytes belong only to the per-user overlay store.
/// </summary>
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class Aurora3DLauncherReadOnlyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work", "aurora-readonly-tests-" + Guid.NewGuid().ToString("N"));

    public Aurora3DLauncherReadOnlyTests() => TestDirectory.CreatePrivate(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task BattleOverride_StagesExactOverlayAndRestoreOnlyClearsStaging()
    {
        if (OperatingSystem.IsLinux())
        {
            await AuroraNativeCompatibility.StagesAndRestores();
            return;
        }
        string selectedDataRoot = Path.Combine(_root, "selected", "data");
        string selected0e = Path.Combine(selectedDataRoot, "FinalFantasyX", "0e");
        string viewerDataRoot = Path.Combine(_root, "local-app-data", "viewer-data");
        string battlePath = Path.Combine(_root, "btl_test.bin");
        string selectedTarget = Path.Combine(selected0e, "00EF.bin");
        string selectedSibling = Path.Combine(selected0e, "00F0.bin");
        TestDirectory.CreatePrivate(selected0e);

        byte[] custom = { 0x43, 0x55, 0x53, 0x54, 0x4F, 0x4D };
        byte[] vanilla = { 0x56, 0x41, 0x4E, 0x49, 0x4C, 0x4C, 0x41 };
        byte[] sibling = { 0x53, 0x49, 0x42, 0x4C, 0x49, 0x4E, 0x47 };
        File.WriteAllBytes(battlePath, custom);
        File.WriteAllBytes(selectedTarget, vanilla);
        File.WriteAllBytes(selectedSibling, sibling);
        string targetHashBefore = Sha256(selectedTarget);
        string siblingHashBefore = Sha256(selectedSibling);

        var result = Aurora3DLauncher.StageBattleOverride(
            battlePath,
            0xEF,
            selected0e,
            viewerDataRoot);

        Assert.True(result.Success, result.Message);
        Assert.Equal("/data/FinalFantasyX/0e/00ef.bin", result.RequestPath);
        string stagedPath = Assert.IsType<string>(result.StagedPath);
        string battleRoot = Path.GetFullPath(Path.Combine(
            viewerDataRoot,
            "noclip-overlays",
            "FinalFantasyX",
            "0e"));
        string sessionRoot = Assert.IsType<string>(Path.GetDirectoryName(stagedPath));
        Assert.Equal(battleRoot, Directory.GetParent(sessionRoot)!.FullName);
        Assert.Matches("^[0-9a-f]{32}$", Path.GetFileName(sessionRoot));
        Assert.Matches("^00EF\\.[0-9a-f]{32}\\.bin$", Path.GetFileName(stagedPath));
        Assert.Equal(custom, File.ReadAllBytes(Assert.IsType<string>(result.StagedPath)));
        Assert.Equal(targetHashBefore, Sha256(selectedTarget));
        Assert.Equal(siblingHashBefore, Sha256(selectedSibling));
        Assert.False(File.Exists(selectedTarget + Aurora3DLauncher.BackupSuffix));

        var server = new StudioWebServer().MapPrefix("/data", selectedDataRoot);
        try
        {
            Assert.True(server.Start(0), server.Status);
            Assert.True(Aurora3DLauncher.TryRegisterBattleOverlay(server, result));
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{server.Port}")
            };

            Assert.Equal(custom, await client.GetByteArrayAsync(result.RequestPath));
            Assert.Equal(sibling, await client.GetByteArrayAsync(
                "/data/FinalFantasyX/0e/00F0.bin"));

            Assert.Equal(1, Aurora3DLauncher.ClearStagedOverrides(viewerDataRoot, server, out _));
            Assert.False(File.Exists(result.StagedPath));
            Assert.Equal(vanilla, await client.GetByteArrayAsync(result.RequestPath));
            Assert.False(server.TryUnmapExactFile(result.RequestPath!, result.StagedPath!));
            Assert.Equal(targetHashBefore, Sha256(selectedTarget));
            Assert.Equal(siblingHashBefore, Sha256(selectedSibling));
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task BattleOverride_SameEncounterUsesDistinctOwnedStagingFiles()
    {
        if (OperatingSystem.IsLinux())
        {
            await AuroraNativeCompatibility.DistinctAcquisitions();
            return;
        }
        string selected0e = Path.Combine(_root, "selected-ownership", "data", "FinalFantasyX", "0e");
        string viewerDataRoot = Path.Combine(_root, "local-app-data-ownership", "viewer-data");
        string firstBattle = Path.Combine(_root, "first.bin");
        string secondBattle = Path.Combine(_root, "second.bin");
        TestDirectory.CreatePrivate(selected0e);
        File.WriteAllBytes(Path.Combine(selected0e, "00EF.bin"), new byte[] { 0x00 });
        File.WriteAllBytes(firstBattle, new byte[] { 0x01 });
        File.WriteAllBytes(secondBattle, new byte[] { 0x02 });

        AuroraBattleOverlayResult first = Aurora3DLauncher.StageBattleOverride(
            firstBattle, 0xEF, selected0e, viewerDataRoot);
        AuroraBattleOverlayResult second = Aurora3DLauncher.StageBattleOverride(
            secondBattle, 0xEF, selected0e, viewerDataRoot);

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.NotEqual(first.StagedPath, second.StagedPath);
        Assert.Equal(new byte[] { 0x01 }, File.ReadAllBytes(Assert.IsType<string>(first.StagedPath)));
        Assert.Equal(new byte[] { 0x02 }, File.ReadAllBytes(Assert.IsType<string>(second.StagedPath)));
    }

    [Fact]
    public async Task BattleOverlayOwner_OnThenOff_UnmapsAndDeletesOnlyItsExactStagingFile()
    {
        if (OperatingSystem.IsLinux())
        {
            await AuroraNativeCompatibility.OwnerToggle();
            return;
        }
        string selectedDataRoot = Path.Combine(_root, "selected-toggle", "data");
        string selected0e = Path.Combine(selectedDataRoot, "FinalFantasyX", "0e");
        string viewerDataRoot = Path.Combine(_root, "viewer-toggle");
        string battlePath = Path.Combine(_root, "toggle.bin");
        string selectedTarget = Path.Combine(selected0e, "00EF.bin");
        TestDirectory.CreatePrivate(selected0e);
        byte[] vanilla = { 0x10, 0x11 };
        byte[] custom = { 0xA0, 0xA1 };
        File.WriteAllBytes(selectedTarget, vanilla);
        File.WriteAllBytes(battlePath, custom);

        var server = new StudioWebServer().MapPrefix("/data", selectedDataRoot);
        var owner = new AuroraBattleOverlayOwner(viewerDataRoot);
        try
        {
            Assert.True(server.Start(0), server.Status);
            string enabled = owner.Update(
                true,
                server,
                () => Aurora3DLauncher.StageBattleOverride(
                    battlePath, 0xEF, selected0e, viewerDataRoot),
                "override disabled");
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };

            Assert.DoesNotContain("disabled", enabled, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(custom, await client.GetByteArrayAsync("/data/FinalFantasyX/0e/00ef.bin"));
            string stagedPath = Assert.IsType<string>(owner.Current?.StagedPath);
            Assert.True(File.Exists(stagedPath));

            string disabled = owner.Update(
                false,
                server,
                () => throw new InvalidOperationException("disabled must not stage"),
                "override disabled");

            Assert.Equal("override disabled", disabled);
            Assert.Null(owner.Current);
            Assert.False(File.Exists(stagedPath));
            Assert.Equal(vanilla, await client.GetByteArrayAsync("/data/FinalFantasyX/0e/00ef.bin"));
            Assert.False(server.TryUnmapExactFile("/data/FinalFantasyX/0e/00ef.bin", stagedPath));
        }
        finally
        {
            owner.Dispose();
            server.Stop();
        }
    }

    [Fact]
    public async Task BattleOverlayOwners_SameEncounter_RestoreThePreviousLiveOwnerWhenTopCloses()
    {
        if (OperatingSystem.IsLinux())
        {
            await AuroraNativeCompatibility.OwnerStack();
            return;
        }
        string selectedDataRoot = Path.Combine(_root, "selected-stack", "data");
        string selected0e = Path.Combine(selectedDataRoot, "FinalFantasyX", "0e");
        string viewerDataRoot = Path.Combine(_root, "viewer-stack");
        string firstBattle = Path.Combine(_root, "stack-first.bin");
        string secondBattle = Path.Combine(_root, "stack-second.bin");
        string selectedTarget = Path.Combine(selected0e, "00EF.bin");
        TestDirectory.CreatePrivate(selected0e);
        byte[] vanilla = { 0x10 };
        byte[] first = { 0xA1 };
        byte[] second = { 0xB2 };
        File.WriteAllBytes(selectedTarget, vanilla);
        File.WriteAllBytes(firstBattle, first);
        File.WriteAllBytes(secondBattle, second);

        var server = new StudioWebServer().MapPrefix("/data", selectedDataRoot);
        var firstOwner = new AuroraBattleOverlayOwner(viewerDataRoot);
        var secondOwner = new AuroraBattleOverlayOwner(viewerDataRoot);
        try
        {
            Assert.True(server.Start(0), server.Status);
            firstOwner.Update(
                true,
                server,
                () => Aurora3DLauncher.StageBattleOverride(
                    firstBattle, 0xEF, selected0e, viewerDataRoot),
                "disabled");
            secondOwner.Update(
                true,
                server,
                () => Aurora3DLauncher.StageBattleOverride(
                    secondBattle, 0xEF, selected0e, viewerDataRoot),
                "disabled");
            string firstStaged = Assert.IsType<string>(firstOwner.Current?.StagedPath);
            string secondStaged = Assert.IsType<string>(secondOwner.Current?.StagedPath);
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };

            Assert.Equal(second, await client.GetByteArrayAsync("/data/FinalFantasyX/0e/00ef.bin"));
            secondOwner.Clear();

            Assert.False(File.Exists(secondStaged));
            Assert.True(File.Exists(firstStaged));
            Assert.Equal(first, await client.GetByteArrayAsync("/data/FinalFantasyX/0e/00ef.bin"));

            firstOwner.Clear();
            Assert.False(File.Exists(firstStaged));
            Assert.Equal(vanilla, await client.GetByteArrayAsync("/data/FinalFantasyX/0e/00ef.bin"));
        }
        finally
        {
            secondOwner.Dispose();
            firstOwner.Dispose();
            server.Stop();
        }
    }

    [Fact]
    public void EncounterIndexCache_IsRebuiltWheneverEitherFactualRootChanges()
    {
        string btlA = Path.Combine(_root, "cache", "btl-a");
        string btlB = Path.Combine(_root, "cache", "btl-b");
        string noclipA = Path.Combine(_root, "cache", "noclip-a");
        string noclipB = Path.Combine(_root, "cache", "noclip-b");
        TestDirectory.CreatePrivate(btlA);
        TestDirectory.CreatePrivate(btlB);
        TestDirectory.CreatePrivate(noclipA);
        TestDirectory.CreatePrivate(noclipB);

        object first = Aurora3DLauncher.GetOrBuildIndex(btlA, noclipA);
        object sameRoots = Aurora3DLauncher.GetOrBuildIndex(btlA, noclipA);
        object changedNoclip = Aurora3DLauncher.GetOrBuildIndex(btlA, noclipB);
        object changedBattle = Aurora3DLauncher.GetOrBuildIndex(btlB, noclipB);

        Assert.Same(first, sameRoots);
        Assert.NotSame(first, changedNoclip);
        Assert.NotSame(changedNoclip, changedBattle);
    }

    [Fact]
    public async Task BattleOverlaySessions_CannotDeleteAnotherLiveOwner()
    {
        if (OperatingSystem.IsLinux())
        {
            await AuroraNativeCompatibility.Sessions();
            return;
        }
        string selected0e = Path.Combine(_root, "selected-sessions", "data", "FinalFantasyX", "0e");
        string viewerDataRoot = Path.Combine(_root, "viewer-sessions");
        string firstBattle = Path.Combine(_root, "session-first.bin");
        string secondBattle = Path.Combine(_root, "session-second.bin");
        string ownerA = Guid.NewGuid().ToString("N");
        string ownerB = Guid.NewGuid().ToString("N");
        TestDirectory.CreatePrivate(selected0e);
        File.WriteAllBytes(Path.Combine(selected0e, "00EF.bin"), new byte[] { 0x00 });
        File.WriteAllBytes(firstBattle, new byte[] { 0xA1 });
        File.WriteAllBytes(secondBattle, new byte[] { 0xB2 });

        AuroraBattleOverlayResult first = Aurora3DLauncher.StageBattleOverride(
            firstBattle, 0xEF, selected0e, viewerDataRoot, ownerA);
        AuroraBattleOverlayResult second = Aurora3DLauncher.StageBattleOverride(
            secondBattle, 0xEF, selected0e, viewerDataRoot, ownerB);
        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        string firstPath = Assert.IsType<string>(first.StagedPath);
        string secondPath = Assert.IsType<string>(second.StagedPath);

        Assert.False(Aurora3DLauncher.TryReleaseBattleOverlay(
            viewerDataRoot, null, first, ownerB));
        Assert.True(File.Exists(firstPath));
        Assert.Equal(1, Aurora3DLauncher.ClearStagedOverrides(
            viewerDataRoot, null, ownerB, out _));
        Assert.True(File.Exists(firstPath));
        Assert.False(File.Exists(secondPath));
        Assert.True(Aurora3DLauncher.TryReleaseBattleOverlay(
            viewerDataRoot, null, first, ownerA));
        Assert.False(File.Exists(firstPath));
    }

    [Fact]
    public void BattleOverlayDelete_LeafReplacementPreservesUnexpectedIdentityAndOwnershipRecord()
    {
        if (!OperatingSystem.IsWindows()) return;

        string selected0e = Path.Combine(_root, "selected-leaf-replacement", "data", "FinalFantasyX", "0e");
        string viewerDataRoot = Path.Combine(_root, "viewer-leaf-replacement");
        string battlePath = Path.Combine(_root, "leaf-replacement-original.bin");
        string replacementSource = Path.Combine(_root, "leaf-replacement-substitute.bin");
        string owner = Guid.NewGuid().ToString("N");
        TestDirectory.CreatePrivate(selected0e);
        File.WriteAllBytes(Path.Combine(selected0e, "00EF.bin"), new byte[] { 0x00 });
        File.WriteAllBytes(battlePath, new byte[] { 0xA1 });
        File.WriteAllBytes(replacementSource, new byte[] { 0xEE, 0xEF });

        AuroraBattleOverlayResult overlay = Aurora3DLauncher.StageBattleOverride(
            battlePath, 0xEF, selected0e, viewerDataRoot, owner);
        Assert.True(overlay.Success, overlay.Message);
        string staged = Assert.IsType<string>(overlay.StagedPath);

        File.Move(replacementSource, staged, overwrite: true);

        Assert.False(Aurora3DLauncher.TryReleaseBattleOverlay(
            viewerDataRoot, null, overlay, owner));
        Assert.Equal(new byte[] { 0xEE, 0xEF }, File.ReadAllBytes(staged));
        Assert.Contains(staged, NoclipOverlayStore.SnapshotOwnedBattleFiles(owner));
        Assert.Equal(0, Aurora3DLauncher.ClearStagedOverrides(
            viewerDataRoot, null, owner, out _));
        Assert.True(File.Exists(staged));
    }

    [Fact]
    public void BattleOverlayDelete_PathSwapCannotDeleteOutsideTheOwnedSession()
    {
        if (!OperatingSystem.IsWindows()) return;

        string selected0e = Path.Combine(_root, "selected-delete-swap", "data", "FinalFantasyX", "0e");
        string viewerDataRoot = Path.Combine(_root, "viewer-delete-swap");
        string battlePath = Path.Combine(_root, "delete-swap.bin");
        string owner = Guid.NewGuid().ToString("N");
        TestDirectory.CreatePrivate(selected0e);
        File.WriteAllBytes(Path.Combine(selected0e, "00EF.bin"), new byte[] { 0x00 });
        File.WriteAllBytes(battlePath, new byte[] { 0xA1 });
        AuroraBattleOverlayResult overlay = Aurora3DLauncher.StageBattleOverride(
            battlePath, 0xEF, selected0e, viewerDataRoot, owner);
        Assert.True(overlay.Success, overlay.Message);
        string staged = Assert.IsType<string>(overlay.StagedPath);
        string sessionRoot = Path.GetDirectoryName(staged)!;
        string backup = sessionRoot + "-backup";
        string outside = Path.Combine(_root, "delete-swap-outside");
        string poison = Path.Combine(_root, "delete-swap-link");
        TestDirectory.CreatePrivate(outside);
        File.WriteAllBytes(Path.Combine(outside, Path.GetFileName(staged)), new byte[] { 0xEE });
        CreateDirectoryLink(poison, outside);
        int swapped = 0;

        FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
        {
            if (operation != "open-delete" ||
                !string.Equals(path, staged, StringComparison.OrdinalIgnoreCase) ||
                System.Threading.Interlocked.Exchange(ref swapped, 1) != 0)
                return;

            Directory.Move(sessionRoot, backup);
            Directory.Move(poison, sessionRoot);
        };

        try
        {
            Assert.False(Aurora3DLauncher.TryReleaseBattleOverlay(
                viewerDataRoot, null, overlay, owner));
            Assert.True(File.Exists(Path.Combine(outside, Path.GetFileName(staged))));
            Assert.True(File.Exists(Path.Combine(backup, Path.GetFileName(staged))));
        }
        finally
        {
            FileSystemReparseGuard.BeforeHandleOperationForTests = null;
            try { if (Directory.Exists(sessionRoot)) Directory.Delete(sessionRoot); } catch { }
            if (Directory.Exists(backup)) Directory.Move(backup, sessionRoot);
            try { if (Directory.Exists(poison)) Directory.Delete(poison); } catch { }
        }
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
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true
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
