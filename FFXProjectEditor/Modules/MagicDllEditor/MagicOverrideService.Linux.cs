// WHY: Native previews are ephemeral; selected files and app-owned staging remain untouched.
// MAINT: Check the bound before taking a producer snapshot. Hash/map that same snapshot, clear it
// in finally, and release a lease on any failed handoff. Server budgets exclude this transient copy.
using System;
using System.IO;
using System.Security.Cryptography;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

internal static partial class MagicOverrideService
{
    private const int MaxNativePreviewBytes = 64 * 1024 * 1024;

    private static MagicOverrideResult StageNativeMagicOverride(
        int magicId, byte[] customBytes, string magicDirectory, StudioWebServer? server)
    {
        // Common empty/id validation already ran in StageMagicOverride. Support is an ABI
        // check; actual read refusals, including unsupported syscalls, still fail closed below.
        if (!LinuxReadFileSystem.IsSupported || server is not { IsRunning: true } ||
            customBytes.Length > MaxNativePreviewBytes)
            return NativePreviewUnavailable("Native preview admission was refused.");

        byte[]? snapshot = null;
        StudioWebServer.ExactMemoryLease? lease = null;
        bool handedOff = false;
        try
        {
            string requestPath = FormattableString.Invariant(
                $"/data/FinalFantasyX/11/{magicId:x4}.bin");
            string root = Path.GetFullPath(magicDirectory);
            string candidate = Path.Combine(root, FormattableString.Invariant($"{magicId:x4}.bin"));
            snapshot = customBytes.AsSpan().ToArray();

            // Copy before the selected-file probe so a later caller mutation cannot make the
            // digest and served content disagree. This is not an atomic snapshot of concurrent
            // writes DURING ToArray, and the selected read does not authenticate vanilla bytes.
            var opened = NoclipHexDataRead.TryOpen(root, candidate, requestPath, out var selected);
            if (opened == FileSystemReparseGuard.VerifiedOpenResult.NotFound)
            {
                // The managed bootstrap seeds one file per directory; unseeded bins stream in via
                // the server's CDN fetch-through. Probe the route itself — a 2xx caches the file
                // locally, a 404 keeps the missing-bin refusal honest.
                selected?.Dispose();
                selected = null;
                if (TryFetchViaLoopbackProbe(server, requestPath))
                    opened = NoclipHexDataRead.TryOpen(root, candidate, requestPath, out selected);
            }
            using (selected)
            {
                if (opened != FileSystemReparseGuard.VerifiedOpenResult.Success || selected == null)
                {
                    if (opened == FileSystemReparseGuard.VerifiedOpenResult.NotFound)
                    {
                        DebugLog.Warn("Magic.Override", "Selected Magic preview bin was not found.");
                        return new(false, string.Format(Strings.U_Md_OverrideBinMissing, magicId));
                    }
                    return NativePreviewUnavailable("Selected Magic preview capability was refused.");
                }
            }

            string hash = Convert.ToHexString(SHA256.HashData(snapshot));
            if (!server.IsRunning ||
                !server.TryMapExactBytes(requestPath, snapshot, out lease) || lease == null)
                return NativePreviewUnavailable("Exact-memory preview registration was refused.");
            if (!server.IsRunning)
                return NativePreviewUnavailable("The preview server stopped before handoff.");

            var result = new MagicOverrideResult(
                true, string.Format(Strings.U_Md_MemoryPreviewApplied, magicId),
                requestPath, null, hash, lease);
            DebugLog.Info("Magic.Override",
                $"Registered native Magic preview ({snapshot.Length} bytes, sha256={hash}); selected data unchanged.");
            handedOff = true;
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            DebugLog.Error("Magic.Override", "Native Magic preview was refused.", error);
            return new(false, Strings.U_Md_MemoryPreviewUnavailable);
        }
        finally
        {
            if (!handedOff) lease?.Dispose();
            if (snapshot != null) Array.Clear(snapshot, 0, snapshot.Length);
        }
    }

    private static MagicOverrideResult NativePreviewUnavailable(string diagnostic)
    {
        DebugLog.Warn("Magic.Override", diagnostic);
        return new(false, Strings.U_Md_MemoryPreviewUnavailable);
    }
}
