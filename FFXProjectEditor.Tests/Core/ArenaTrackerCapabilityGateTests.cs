using FFXProjectEditor.Modules.ArenaTracker;
using System;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

// ============================================================================
// ArenaTrackerCapabilityGateTests - live-game honest degradation (D5 finding)
// WHY: the shipped Linux build crashed at arena-tracker because the constructor
//      called .Take() on a null game-memory read. The public catalog keeps the
//      module; navigating it must show the unavailable state, never throw.
// ============================================================================
public sealed class ArenaTrackerCapabilityGateTests
{
    [Fact]
    public void Constructor_WithoutLiveGame_SetsUnavailableInsteadOfThrowing()
    {
        if (!OperatingSystem.IsLinux())
            return; // the gate is proven where the game-memory bridge is absent

        ArenaTracker_DataModel model;
        var exception = Record.Exception(() => model = new ArenaTracker_DataModel());
        Assert.Null(exception);

        ArenaTracker_DataModel liveModel = new();
        Assert.True(liveModel.LiveGameUnavailable);
    }

    [Fact]
    public void WriteArenaData_WhenUnavailable_IsANoThrowNoOp()
    {
        if (!OperatingSystem.IsLinux())
            return;

        ArenaTracker_DataModel model = new();
        if (!model.LiveGameUnavailable)
            return; // a live game would make this test host-specific

        var exception = Record.Exception(() => model.WriteArenaData());
        Assert.Null(exception);
    }

    [Fact]
    public void MemoryFailure_TransitionsToUnavailableWithReason()
    {
        ArenaTracker_DataModel model = new();

        model.SetLiveGameUnavailable("synthetic memory read failure");

        Assert.True(model.LiveGameUnavailable);
        Assert.Equal("synthetic memory read failure", model.LiveGameUnavailableReason);
    }
}
