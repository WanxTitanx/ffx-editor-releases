using System;
using System.IO;
using FFXProjectEditor.Tests.Infrastructure;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.Core;
public class NoclipLocatorTests
{
    [Fact]
    public void NormalizeCandidate_AcceptsRootDataAndFinalFantasyX()
    {
        string root = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx-noclip-locator-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data");
        string ffx = Path.Combine(data, "FinalFantasyX");
        Directory.CreateDirectory(ffx);
        try
        {
            Assert.Equal(Path.GetFullPath(root), NoclipLocator.NormalizeCandidate(root));
            Assert.Equal(Path.GetFullPath(root), NoclipLocator.NormalizeCandidate(data));
            Assert.Equal(Path.GetFullPath(root), NoclipLocator.NormalizeCandidate(ffx));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NormalizeCandidate_RejectsMissingOrUnrelatedDirectory()
    {
        string unrelated = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "ffx-noclip-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unrelated);
        try
        {
            Assert.Null(NoclipLocator.NormalizeCandidate(unrelated));
            Assert.Null(NoclipLocator.NormalizeCandidate(Path.Combine(unrelated, "missing")));
            Assert.Null(NoclipLocator.NormalizeCandidate(null));
        }
        finally
        {
            Directory.Delete(unrelated, recursive: true);
        }
    }

    [Fact]
    public void ValidateConfigurationCandidate_RejectsIncompleteCapabilityBeforePersistence()
    {
        string root = Path.Combine(
            FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work", "ffx-noclip-incomplete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data", "FinalFantasyX"));
        try
        {
            Assert.NotNull(NoclipLocator.NormalizeCandidate(root));
            Assert.False(NoclipLocator.TryValidateConfigurationCandidate(
                root,
                out string? normalized,
                out string error));
            Assert.Equal(Path.GetFullPath(root), normalized);
            Assert.Contains("REQUIRED_DIRECTORY_MISSING", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidateConfigurationCandidate_RejectsNetworkAndDevicePaths()
    {
        Assert.False(NoclipLocator.IsLocalConfigurationPath(@"\\server\share\noclip"));
        Assert.False(NoclipLocator.IsLocalConfigurationPath(@"\\?\C:\noclip"));
        Assert.False(NoclipLocator.IsLocalConfigurationPath(@"\\.\C:\noclip"));
        Assert.True(NoclipLocator.IsLocalConfigurationPath(
            Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "noclip")));
    }

    [Fact]
    public void AcceptDiscoveredCandidate_AppliesTheLocalOnlyGateToConfigAndEnvironmentValues()
    {
        string root = Path.Combine(
            FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work", "ffx-noclip-discovered-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data", "FinalFantasyX"));
        try
        {
            Assert.Equal(Path.GetFullPath(root), NoclipLocator.AcceptDiscoveredCandidate(root));
            Assert.Null(NoclipLocator.AcceptDiscoveredCandidate(@"\\server\share\noclip"));
            Assert.Null(NoclipLocator.AcceptDiscoveredCandidate(@"\\?\C:\noclip"));
            Assert.Null(NoclipLocator.AcceptDiscoveredCandidate(@"\\.\C:\noclip"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
