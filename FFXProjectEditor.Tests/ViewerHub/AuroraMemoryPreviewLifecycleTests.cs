// WHY: Exercise the existing Aurora owner/family lifecycle, not a replacement UI or fake transport.
// MAINT: Enter/release barriers synchronize in-flight readers; final usage uses a bounded predicate.
using System;
using System.IO;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.AuroraChamber;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;
using Xunit;
using TestIo = FFXProjectEditor.Tests.MagicDll.MagicPreviewFixture;

namespace FFXProjectEditor.Tests.ViewerHub;

internal static class AuroraNativeCompatibility
{
    internal static async Task StagesAndRestores()
    {
        Assert.True(AuroraMemoryFixture.Native());
        await using var f = new AuroraMemoryFixture();
        string sibling = Path.Combine(f.SelectedDirectory, "00F0.bin");
        File.WriteAllBytes(sibling, new byte[] { 0x20 });
        string[] files = TestIo.Files(f.Root);
        // Exercise the existing default-session overload and existing Clear wrapper with a real host.
        var result = Aurora3DLauncher.StageBattleOverride(
            f.Battle, 0xEF, f.SelectedDirectory, f.ViewerRoot, f.Server);
        f.Track(result);
        try
        {
            await f.AssertMemoryAsync(result, new byte[] { 0xA1, 0xA2 });
            Assert.Equal(new byte[] { 0x20 },
                await f.Client.GetByteArrayAsync("/data/FinalFantasyX/0e/00f0.bin"));
            Assert.Equal(1, Aurora3DLauncher.ClearStagedOverrides(f.ViewerRoot, f.Server, out _));
            Assert.False(result.MemoryLease!.IsActive);
            Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
            Assert.Equal(new byte[] { 0x20 }, File.ReadAllBytes(sibling));
            Assert.Equal(new byte[] { 0x10 }, File.ReadAllBytes(f.SelectedPath));
            Assert.Equal(new byte[] { 0xA1, 0xA2 }, File.ReadAllBytes(f.Battle));
            Assert.Equal(files, TestIo.Files(f.Root));
            await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        }
        finally { Aurora3DLauncher.TryReleaseBattleOverlay(f.ViewerRoot, f.Server, result); }
    }

    internal static async Task DistinctAcquisitions()
    {
        Assert.True(AuroraMemoryFixture.Native());
        await using var f = new AuroraMemoryFixture();
        var first = f.Stage();
        File.WriteAllBytes(f.Battle, new byte[] { 0xB2 });
        var second = f.Stage();
        Assert.True(first.Success, first.Message);
        Assert.NotSame(first.MemoryLease, second.MemoryLease);
        Assert.Null(first.StagedPath);
        await f.AssertMemoryAsync(second, new byte[] { 0xB2 });
        Assert.True(Aurora3DLauncher.TryReleaseBattleOverlay(f.ViewerRoot, f.Server, first));
        Assert.True(second.MemoryLease!.IsActive);
        Assert.Equal(new byte[] { 0xB2 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        Assert.True(Aurora3DLauncher.TryReleaseBattleOverlay(f.ViewerRoot, null, second));
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        f.AssertNoOutput();
    }

    internal static async Task OwnerToggle()
    {
        Assert.True(AuroraMemoryFixture.Native());
        await using var f = new AuroraMemoryFixture();
        using var owner = new AuroraBattleOverlayOwner(f.ViewerRoot);
        owner.Update(true, f.Server, () => f.Stage(), "disabled");
        var current = Assert.IsType<AuroraBattleOverlayResult>(owner.Current);
        await f.AssertMemoryAsync(current, new byte[] { 0xA1, 0xA2 });
        Assert.Equal("disabled", owner.Update(false, f.Server,
            () => throw new InvalidOperationException("Disabled must not stage."), "disabled"));
        Assert.Null(owner.Current);
        Assert.False(current.MemoryLease!.IsActive);
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        f.AssertNoOutput();
    }

    internal static async Task OwnerStack()
    {
        Assert.True(AuroraMemoryFixture.Native());
        await using var f = new AuroraMemoryFixture();
        using var older = new AuroraBattleOverlayOwner(f.ViewerRoot);
        using var newer = new AuroraBattleOverlayOwner(f.ViewerRoot);
        older.Update(true, f.Server, () => f.Stage(), "disabled");
        var first = Assert.IsType<AuroraBattleOverlayResult>(older.Current);
        File.WriteAllBytes(f.Battle, new byte[] { 0xB2 });
        newer.Update(true, f.Server, () => f.Stage(), "disabled");
        var second = Assert.IsType<AuroraBattleOverlayResult>(newer.Current);
        await f.AssertMemoryAsync(second, new byte[] { 0xB2 });
        newer.Clear();
        Assert.False(second.MemoryLease!.IsActive);
        Assert.True(first.MemoryLease!.IsActive);
        Assert.Equal(new byte[] { 0xA1, 0xA2 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        older.Clear();
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        f.AssertNoOutput();
    }

    internal static async Task Sessions()
    {
        Assert.True(AuroraMemoryFixture.Native());
        await using var f = new AuroraMemoryFixture();
        using var secondSession = new AuroraMemoryPreviewSession();
        using var unrelated = new AuroraMemoryPreviewSession();
        var first = f.Stage();
        File.WriteAllBytes(f.Battle, new byte[] { 0xB2 });
        var second = f.Stage(session: secondSession);
        Assert.True(first.Success, first.Message);
        await f.AssertMemoryAsync(second, new byte[] { 0xB2 });
        Assert.Equal(0, Aurora3DLauncher.ClearStagedOverrides(unrelated, out _));
        Assert.False(Aurora3DLauncher.TryRegisterBattleOverlay(
            f.Server, first with { MemorySession = secondSession }));
        Assert.True(first.MemoryLease!.IsActive);
        Assert.Equal(1, Aurora3DLauncher.ClearStagedOverrides(secondSession, out _));
        Assert.True(first.MemoryLease.IsActive);
        Assert.False(second.MemoryLease!.IsActive);
        Assert.Equal(new byte[] { 0xA1, 0xA2 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        Assert.Equal(1, Aurora3DLauncher.RestoreAllOverrides(f.Session, out _));
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        f.AssertNoOutput();
    }
}

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class AuroraMemoryPreviewLifecycleTests
{
    [Fact]
    public async Task Native_OwnerRefreshAndServerSwitch_ReleasesPreviousExactOwner()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        using var owner = new AuroraBattleOverlayOwner(f.ViewerRoot);
        var other = new StudioWebServer().MapPrefix("/data/FinalFantasyX/0e", f.SelectedDirectory);
        try
        {
            Assert.True(other.Start(0), other.Status);
            using var otherClient = TestIo.NewClient(other);
            owner.Update(true, f.Server, () => f.Stage(), "disabled");
            var first = Assert.IsType<AuroraBattleOverlayResult>(owner.Current);
            await f.AssertMemoryAsync(first, new byte[] { 0xA1, 0xA2 });
            File.WriteAllBytes(f.Battle, new byte[] { 0xB2 });
            owner.Update(true, f.Server, () => f.Stage(), "disabled");
            Assert.False(first.MemoryLease!.IsActive);
            await f.AssertMemoryAsync(Assert.IsType<AuroraBattleOverlayResult>(owner.Current),
                new byte[] { 0xB2 });
            owner.Update(true, other, () => f.Stage(server: other), "disabled");
            var changed = Assert.IsType<AuroraBattleOverlayResult>(owner.Current);
            Assert.True(changed.MemoryLease!.IsOwnedBy(other));
            Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
            Assert.Equal(new byte[] { 0xB2 }, await otherClient.GetByteArrayAsync(AuroraMemoryFixture.Route));
            await TestIo.AwaitUsageAsync(f.Server, 0, 0);
            owner.Dispose();
            Assert.Null(owner.Current);
            Assert.False(changed.MemoryLease.IsActive);
            Assert.Equal(new byte[] { 0x10 }, await otherClient.GetByteArrayAsync(AuroraMemoryFixture.Route));
            await TestIo.AwaitUsageAsync(other, 0, 0);
        }
        finally
        {
            owner.Clear();
            other.Stop();
        }
    }

    [Fact]
    public async Task Native_ThrownStage_ClearsOwnPreviewWithoutPurgingOtherOwners()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        Assert.True(f.Server.TryMapExactBytes(AuroraMemoryFixture.Route, new byte[] { 0xCC }, out var foreign));
        using (foreign)
        using (var owner = new AuroraBattleOverlayOwner(f.ViewerRoot))
        {
            owner.Update(true, f.Server, () => f.Stage(), "disabled");
            var current = Assert.IsType<AuroraBattleOverlayResult>(owner.Current);
            string status = owner.Update(true, f.Server, () => throw new IOException("fixture read refused"), "disabled");
            Assert.Equal(Strings.U_Au_MemoryPreviewUnavailable, status);
            Assert.Null(owner.Current);
            Assert.False(current.MemoryLease!.IsActive);
            Assert.True(foreign!.IsActive);
            Assert.Equal(new byte[] { 0xCC }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
            await TestIo.AwaitUsageAsync(f.Server, 1, 1);
        }
    }

    [Fact]
    public async Task Native_StoppedOwnerUpdate_DoesNotInvokeStageFactory()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        using var owner = new AuroraBattleOverlayOwner(f.ViewerRoot);
        owner.Update(true, f.Server, () => f.Stage(), "disabled");
        var current = Assert.IsType<AuroraBattleOverlayResult>(owner.Current);
        f.Server.Stop();
        int calls = 0;
        string status = owner.Update(true, f.Server, () => { calls++; return f.Stage(); }, "disabled");
        Assert.Equal(0, calls);
        Assert.Equal(Strings.U_Au_MemoryPreviewUnavailable, status);
        Assert.Null(owner.Current);
        Assert.False(current.MemoryLease!.IsActive);
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        Assert.True(f.Server.Start(0), f.Server.Status);
        using var client = TestIo.NewClient(f.Server);
        Assert.Equal(new byte[] { 0x10 }, await client.GetByteArrayAsync(AuroraMemoryFixture.Route));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_FailedStageOrWrongHostRegistration_ClearsOnlyOwner(bool wrongHost)
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        using var owner = new AuroraBattleOverlayOwner(f.ViewerRoot);
        var other = new StudioWebServer();
        try
        {
            Assert.True(other.Start(0), other.Status);
            Assert.True(f.Server.TryMapExactBytes(AuroraMemoryFixture.Route, new byte[] { 0xCC }, out var foreign));
            using (foreign)
            {
                owner.Update(true, f.Server, () => f.Stage(), "disabled");
                var old = Assert.IsType<AuroraBattleOverlayResult>(owner.Current);
                owner.Update(true, f.Server,
                    () => wrongHost ? f.Stage(server: other) : f.Stage(Path.Combine(f.Root, "missing.bin")),
                    "disabled");
                Assert.Null(owner.Current);
                Assert.False(old.MemoryLease!.IsActive);
                Assert.True(foreign!.IsActive);
                Assert.Equal(new byte[] { 0xCC }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
                await TestIo.AwaitUsageAsync(other, 0, 0);
                await TestIo.AwaitUsageAsync(f.Server, 1, 1);
            }
        }
        finally { other.Stop(); }
    }

    [Fact]
    public async Task Native_FamilyClear_InvalidatesCurrentAndRejectsReRegistration()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        using var owner = new AuroraBattleOverlayOwner(f.ViewerRoot);
        owner.Update(true, f.Server, () => f.Stage(), "disabled");
        var old = Assert.IsType<AuroraBattleOverlayResult>(owner.Current);
        Assert.Equal(1, Aurora3DLauncher.RestoreAllOverrides(f.Session, out _));
        Assert.Null(owner.Current);
        Assert.False(Aurora3DLauncher.TryRegisterBattleOverlay(f.Server, old));
        Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        owner.Dispose(); // Existing owner Dispose means Clear, not permanent closure.
        owner.Update(true, f.Server, () => f.Stage(), "disabled");
        await f.AssertMemoryAsync(Assert.IsType<AuroraBattleOverlayResult>(owner.Current), new byte[] { 0xA1, 0xA2 });
    }

    [Fact]
    public async Task Native_FamilyClear_RetainsReaderChargeUntilExplicitReleaseBarrier()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        f.Server.LowerExactMemoryLimitsForTests(2, 2, 4); // Spare entry slots: retired BYTE guard matters.
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Server.BeforeMemoryWriteForTests = async token =>
        {
            entered.TrySetResult(true);
            await release.Task.WaitAsync(token);
        };
        try
        {
            var overlay = f.Stage();
            Assert.True(overlay.Success, overlay.Message);
            Task<byte[]> response = f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, f.Server.GetExactMemoryUsageForTests().ActiveReaders);
            Assert.Equal(1, Aurora3DLauncher.ClearStagedOverrides(f.Session, out _));
            Assert.False(overlay.MemoryLease!.IsActive);
            // This immediate assertion IS synchronized: the entered reader cannot leave until release.
            Assert.Equal(new StudioWebServer.ExactMemoryUsageSnapshot(2, 1, 1, 2, 1),
                f.Server.GetExactMemoryUsageForTests());
            Assert.False(f.Stage().Success);
            Assert.Equal(new byte[] { 0x10 }, await f.Client.GetByteArrayAsync(AuroraMemoryFixture.Route));
            release.TrySetResult(true);
            Assert.Equal(new byte[] { 0xA1, 0xA2 }, await response.WaitAsync(TimeSpan.FromSeconds(5)));
            await TestIo.AwaitUsageAsync(f.Server, 0, 0);
            f.Server.BeforeMemoryWriteForTests = null;
            Assert.True(f.Stage().Success);
        }
        finally
        {
            release.TrySetResult(true);
            f.Server.BeforeMemoryWriteForTests = null;
        }
    }

    [Fact]
    public async Task Native_StopRestart_RetainsLeaseUntilExplicitSessionClear()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        var result = f.Stage();
        await f.AssertMemoryAsync(result, new byte[] { 0xA1, 0xA2 });
        await TestIo.AwaitUsageAsync(f.Server, 2, 1);
        f.Server.Stop();
        Assert.True(result.MemoryLease!.IsActive);
        Assert.True(result.MemoryLease.IsOwnedBy(f.Server));
        Assert.False(Aurora3DLauncher.TryRegisterBattleOverlay(f.Server, result));
        Assert.True(f.Server.Start(0), f.Server.Status);
        using (var client = TestIo.NewClient(f.Server))
            Assert.Equal(new byte[] { 0xA1, 0xA2 }, await client.GetByteArrayAsync(AuroraMemoryFixture.Route));
        await TestIo.AwaitUsageAsync(f.Server, 2, 1);
        f.Server.Stop();
        Assert.Equal(1, Aurora3DLauncher.ClearStagedOverrides(f.Session, out _));
        Assert.False(result.MemoryLease.IsActive);
        await TestIo.AwaitUsageAsync(f.Server, 0, 0);
        Assert.True(f.Server.Start(0), f.Server.Status);
        using var restarted = TestIo.NewClient(f.Server);
        Assert.Equal(new byte[] { 0x10 }, await restarted.GetByteArrayAsync(AuroraMemoryFixture.Route));
    }

    [Fact]
    public async Task Native_LegacyWrappers_UseDefaultFamilyNotArbitraryStringAuthority()
    {
        if (!AuroraMemoryFixture.Native()) return;
        await using var f = new AuroraMemoryFixture();
        var missing = Aurora3DLauncher.StageBattleOverride(
            f.Battle, 0xEF, f.SelectedDirectory, f.ViewerRoot);
        Assert.False(missing.Success);
        string customId = Guid.NewGuid().ToString("N");
        var unsupported = Aurora3DLauncher.StageBattleOverride(
            f.Battle, 0xEF, f.SelectedDirectory, f.ViewerRoot, customId, f.Server);
        Assert.False(unsupported.Success);
        var explicitSession = f.Stage();
        var legacy = Aurora3DLauncher.StageBattleOverride(
            f.Battle, 0xEF, f.SelectedDirectory, f.ViewerRoot, f.Server);
        f.Track(legacy);
        try
        {
            Assert.True(legacy.Success, legacy.Message);
            Assert.Equal(0, Aurora3DLauncher.ClearStagedOverrides(f.ViewerRoot, f.Server, customId, out _));
            Assert.True(legacy.MemoryLease!.IsActive);
            Assert.Equal(1, Aurora3DLauncher.RestoreAllOverrides(out _)); // Actual public wrapper, no Hub lookup on Linux.
            Assert.False(legacy.MemoryLease.IsActive);
            Assert.True(explicitSession.MemoryLease!.IsActive);
            await f.AssertMemoryAsync(explicitSession, new byte[] { 0xA1, 0xA2 });
        }
        finally { Aurora3DLauncher.TryReleaseBattleOverlay(f.ViewerRoot, null, legacy); }
    }
}
