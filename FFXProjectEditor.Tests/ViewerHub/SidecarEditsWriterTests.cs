using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

[CollectionDefinition(FileSystemReparseGuardHookCollection.Name, DisableParallelization = true)]
public sealed class FileSystemReparseGuardHookCollection
{
    public const string Name = "FileSystemReparseGuard hook tests";
}

/// <summary>F3.5 — valida o writer do sidecar de edição do Aurora (deltas por slot).</summary>
[Collection(FileSystemReparseGuardHookCollection.Name)]
public class SidecarEditsWriterTests
{
    [Fact]
    public void ApplySlot_CriaEstruturaComPositionHeadingScale()
    {
        string json = SidecarEditsWriter.ApplySlot(null, 3, 10.5, -2, 0.25, 1.57, 2.0);

        var root = JsonNode.Parse(json) as JsonObject;
        var actors = root!["actors"] as JsonObject;
        var slot = actors!["3"] as JsonObject;
        Assert.NotNull(slot);
        Assert.Equal(10.5, (double)slot!["position"]![0]!);
        Assert.Equal(-2, (double)slot["position"]![1]!);
        Assert.Equal(0.25, (double)slot["position"]![2]!);
        Assert.Equal(1.57, (double)slot["heading"]!);
        Assert.Equal(2.0, (double)slot["scale"]!);
    }

    [Fact]
    public void ApplySlot_MergeNaoPerdeOutrosSlots()
    {
        string first = SidecarEditsWriter.ApplySlot(null, 0, 1, 2, 3, null, null);

        string merged = SidecarEditsWriter.ApplySlot(first, 5, 4, 5, 6, 0.5, 1.5);

        var root = JsonNode.Parse(merged) as JsonObject;
        var actors = root!["actors"] as JsonObject;
        Assert.NotNull(actors!["0"]);
        Assert.NotNull(actors["5"]);
        Assert.Equal(4, (double)actors["5"]!["position"]![0]!);
    }

    [Fact]
    public void ApplySlot_SobrescreveMesmoSlot()
    {
        string first = SidecarEditsWriter.ApplySlot(null, 2, 1, 1, 1, 0.1, 1.0);

        string updated = SidecarEditsWriter.ApplySlot(first, 2, 9, 9, 9, null, null);

        var root = JsonNode.Parse(updated) as JsonObject;
        var slot = root!["actors"]!["2"] as JsonObject;
        Assert.Equal(9, (double)slot!["position"]![0]!);
        // campos não reenviados permanecem
        Assert.Equal(0.1, (double)slot["heading"]!);
        Assert.Equal(1.0, (double)slot["scale"]!);
    }

    [Fact]
    public void ApplySlot_NullTotalPreservaSlotVazio()
    {
        string json = SidecarEditsWriter.ApplySlot(null, 1, null, null, null, null, null);

        var root = JsonNode.Parse(json) as JsonObject;
        var actors = root!["actors"] as JsonObject;
        var slot = actors!["1"] as JsonObject;
        Assert.NotNull(slot);
        Assert.Empty(slot);
    }

    [Fact]
    public async Task OverlayStore_WritesAtomicallyOutsideSelectedDataAndMapsTheSpecificPrefix()
    {
        string root = Path.Combine(
            Path.Combine(TestDataPaths.RepoRoot, "work", "sidecar-overlay-tests-" + Guid.NewGuid().ToString("N")));
        string selectedDataRoot = Path.Combine(root, "selected", "data");
        string selectedEdits = Path.Combine(selectedDataRoot, "FinalFantasyX", "edits");
        string selectedFile = Path.Combine(selectedEdits, "239.json");
        string viewerDataRoot = Path.Combine(root, "local-app-data", "viewer-data");
        TestDirectory.CreatePrivate(selectedEdits);
        File.WriteAllText(selectedFile, "{\"selected\":true}");
        string selectedHashBefore = Sha256(selectedFile);

        try
        {
            string stagedFile = NoclipOverlayStore.WriteEdit(
                viewerDataRoot,
                239,
                slot: 4,
                dx: 1,
                dy: 2,
                dz: 3,
                heading: 0.5,
                scale: 1.25);
            string editRoot = NoclipOverlayStore.ResolveEditsRoot(viewerDataRoot);

            Assert.Equal(Path.Combine(editRoot, "239.json"), stagedFile);
            Assert.Equal(selectedHashBefore, Sha256(selectedFile));
            Assert.Empty(Directory.EnumerateFiles(viewerDataRoot, "*.tmp", SearchOption.AllDirectories));

            var server = new StudioWebServer()
                .MapPrefix("/data", selectedDataRoot)
                .MapPrefix("/data/FinalFantasyX/edits", editRoot);
            try
            {
                Assert.True(server.Start(0), server.Status);
                using var client = new HttpClient
                {
                    BaseAddress = new Uri($"http://127.0.0.1:{server.Port}")
                };

                string served = await client.GetStringAsync(
                    "/data/FinalFantasyX/edits/239.json");
                Assert.Contains("\"4\"", served);
                Assert.Contains("1.25", served);
                Assert.DoesNotContain("selected", served, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(selectedHashBefore, Sha256(selectedFile));
            }
            finally
            {
                server.Stop();
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task OverlayStore_ConcurrentSlotWritesPreserveEverySlot()
    {
        string root = Path.Combine(
            Path.Combine(TestDataPaths.RepoRoot, "work", "sidecar-concurrency-" + Guid.NewGuid().ToString("N")));
        string editsRoot = Path.Combine(root, "edits");
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(slot => Task.Run(() =>
                NoclipOverlayStore.WriteEditToRoot(
                    editsRoot,
                    "239",
                    slot,
                    dx: slot,
                    dy: slot + 0.25,
                    dz: slot + 0.5,
                    heading: null,
                    scale: null))));

            JsonObject json = Assert.IsType<JsonObject>(
                JsonNode.Parse(File.ReadAllText(Path.Combine(editsRoot, "239.json"))));
            JsonObject actors = Assert.IsType<JsonObject>(json["actors"]);
            for (int slot = 0; slot < 8; slot++)
                Assert.NotNull(actors[slot.ToString()]);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task OverlayStore_WaitsForTheOperatingSystemLockBeforeMergingSidecars()
    {
        string root = Path.Combine(
            Path.Combine(TestDataPaths.RepoRoot, "work", "sidecar-cross-process-lock-" + Guid.NewGuid().ToString("N")));
        string editsRoot = Path.Combine(root, "edits");
        TestDirectory.CreatePrivate(editsRoot);
        string destination = Path.Combine(editsRoot, "239.json");
        string lockPath = destination + ".lock";

        try
        {
            // The cross-process lock mechanism is platform-specific: an exclusive Win32 file open on
            // Windows, the native flock lease on Linux. Hold the platform's own lock from "another
            // process" and require the mutation to wait for it before merging.
            FileStream? heldByAnotherProcessBoundary = null;
            LinuxOwnedOutputDirectory? heldNativeDirectory = null;
            IDisposable? heldNativeLease = null;
            if (OperatingSystem.IsWindows())
            {
                heldByAnotherProcessBoundary = new(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            else
            {
                heldNativeDirectory = LinuxOwnedOutputDirectory.OpenOrCreate(editsRoot);
                heldNativeLease = heldNativeDirectory.AcquireWriteLock(TimeSpan.Zero);
            }
            Task<string> waitingWrite = Task.Run(() => NoclipOverlayStore.WriteEditToRoot(
                editsRoot,
                "239",
                slot: 6,
                dx: 6,
                dy: null,
                dz: null,
                heading: null,
                scale: null));

            await Task.Delay(150);
            Assert.False(waitingWrite.IsCompleted);

            heldByAnotherProcessBoundary?.Dispose();
            heldNativeLease?.Dispose();
            heldNativeDirectory?.Dispose();
            string written = await waitingWrite.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(destination, written);
            Assert.Contains("\"6\"", File.ReadAllText(destination), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void OverlayStore_NormalizesAndBoundsTheEncounterNamespace()
    {
        string root = Path.Combine(
            Path.Combine(TestDataPaths.RepoRoot, "work", "sidecar-encounter-domain-" + Guid.NewGuid().ToString("N")));
        try
        {
            string written = NoclipOverlayStore.WriteEditToRoot(
                root,
                "00065535",
                slot: 0,
                dx: null,
                dy: null,
                dz: null,
                heading: null,
                scale: null);

            Assert.Equal(Path.Combine(root, "65535.json"), written);
            Assert.Throws<ArgumentException>(() => NoclipOverlayStore.WriteEditToRoot(
                root,
                "65536",
                slot: 0,
                dx: null,
                dy: null,
                dz: null,
                heading: null,
                scale: null));
            Assert.False(File.Exists(Path.Combine(root, "65536.json")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void OverlayStore_PathSwapBeforeCreateCannotRedirectOutsideOwnedRoot()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = Path.Combine(
            Path.GetTempPath(), "sidecar-handle-swap-" + Guid.NewGuid().ToString("N"));
        string editsRoot = Path.Combine(root, "owned", "edits");
        string backup = editsRoot + "-backup";
        string outside = Path.Combine(root, "outside");
        string poison = Path.Combine(root, "poison");
        TestDirectory.CreatePrivate(editsRoot);
        TestDirectory.CreatePrivate(outside);
        CreateDirectoryLink(poison, outside);
        int swapped = 0;

        FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
        {
            if (operation != "open-directory" ||
                !string.Equals(path, editsRoot, StringComparison.OrdinalIgnoreCase) ||
                Interlocked.Exchange(ref swapped, 1) != 0)
                return;

            Directory.Move(editsRoot, backup);
            Directory.Move(poison, editsRoot);
        };

        try
        {
            Assert.Throws<IOException>(() => NoclipOverlayStore.WriteEditToRoot(
                editsRoot, "239", 0, 1, 2, 3, null, null));
            Assert.False(File.Exists(Path.Combine(outside, "239.json")));
            Assert.Empty(Directory.EnumerateFiles(outside));
        }
        finally
        {
            FileSystemReparseGuard.BeforeHandleOperationForTests = null;
            try { if (Directory.Exists(editsRoot)) Directory.Delete(editsRoot); } catch { }
            if (Directory.Exists(backup)) Directory.Move(backup, editsRoot);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string Sha256(string file)
    {
        using FileStream stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // ── EditViewer battle-stage: sidecar → .bin merge (Jarvis-UI, 2026-09-15) ────────────────
    // The noclip gizmo saves per-slot position DELTAS; SaveDraggedPositions consumes them via
    // AuroraChamber_DataModel.TryReadNoclipEditDeltas and commits absolute coords to the battle bin.

    [Fact]
    public void TryReadNoclipEditDeltas_ReadsPerSlotPositions()
    {
        string root = Path.Combine(Path.GetTempPath(), "delta-read-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "11.json"),
                "{\"actors\":{\"2\":{\"position\":[1.5,-0.25,3.0]},\"7\":{\"position\":[0,0,-2]}}}");
            bool ok = FFXProjectEditor.Modules.AuroraChamber.AuroraChamber_DataModel
                .TryReadNoclipEditDeltas(root, 11, out var deltas);
            Assert.True(ok);
            Assert.NotNull(deltas);
            Assert.Equal(2, deltas!.Count);
            Assert.Equal((1.5, -0.25, 3.0), deltas[2]);
            Assert.Equal((0.0, 0.0, -2.0), deltas[7]);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void TryReadNoclipEditDeltas_SkipsZeroedAndMissingEntries()
    {
        string root = Path.Combine(Path.GetTempPath(), "delta-zero-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            // slot 3 was reset (0,0,0); slot 9 is out of range; slot 1 has heading only
            File.WriteAllText(Path.Combine(root, "4.json"),
                "{\"actors\":{\"3\":{\"position\":[0,0,0]},\"9\":{\"position\":[1,1,1]},\"1\":{\"heading\":0.5}}}");
            bool ok = FFXProjectEditor.Modules.AuroraChamber.AuroraChamber_DataModel
                .TryReadNoclipEditDeltas(root, 4, out var deltas);
            Assert.False(ok); // nothing consumable
            Assert.Null(deltas);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void TryReadNoclipEditDeltas_ToleratesMissingFileAndMalformedJson()
    {
        string root = Path.Combine(Path.GetTempPath(), "delta-miss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(FFXProjectEditor.Modules.AuroraChamber.AuroraChamber_DataModel
                .TryReadNoclipEditDeltas(root, 99, out _));

            File.WriteAllText(Path.Combine(root, "5.json"), "{not json");
            Assert.False(FFXProjectEditor.Modules.AuroraChamber.AuroraChamber_DataModel
                .TryReadNoclipEditDeltas(root, 5, out _));

            File.WriteAllText(Path.Combine(root, "6.json"),
                "{\"actors\":{\"0\":{\"position\":[1e999,0,0]}}}"); // overflows to non-finite
            Assert.False(FFXProjectEditor.Modules.AuroraChamber.AuroraChamber_DataModel
                .TryReadNoclipEditDeltas(root, 6, out _));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
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
