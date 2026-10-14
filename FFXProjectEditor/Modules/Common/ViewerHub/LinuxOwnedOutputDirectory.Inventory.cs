using FFXProjectEditor.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using static FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── Read-only inventory for bounded recovery admission ──
// Names and metadata come from the retained directory, never Directory.EnumerateFiles(path).
// MAINT: stable observations are not an atomic directory snapshot or byte/hash verification.
// Consumers still hold their cooperative write lease and recheck each artifact before changing it.
internal sealed partial class LinuxOwnedOutputDirectory
{
    // Inspection consumes current observations through the retained capability. This never
    // creates a directory or acquires authority from a persisted identity.
    internal LinuxOutputFileSystem.OutputObservation ReadDirectoryMetadata()
    {
        lock (_gate)
        {
            EnsureUsable();
            VerifyRoot();
            var observed = LinuxOutputFileSystem.Observe(_directory, DirectoryType);
            RequireOwnedDirectory(observed);
            VerifyRoot();
            return observed;
        }
    }

    internal IReadOnlyList<string> ListChildNames(int maximumEntries = LinuxDirectoryInventory.MaximumEntries)
    {
        LinuxDirectoryInventory.ValidateLimit(maximumEntries);
        lock (_gate)
        {
            EnsureUsable();
            VerifyRoot();
            var before = LinuxOutputFileSystem.Observe(_directory, DirectoryType);
            BeforeOperationForTests?.Invoke("inventory-before-read", FullPath);
            var names = LinuxDirectoryInventory.ReadNames(_directory, maximumEntries);
            BeforeOperationForTests?.Invoke("inventory-read", FullPath);
            if (LinuxOutputFileSystem.Observe(_directory, DirectoryType) != before)
                throw new IOException("Linux directory changed during its bounded inventory.");
            VerifyRoot();
            DebugLog.Info(LogCategory, $"Observed {names.Count} direct Linux output names.");
            return names;
        }
    }

    internal LinuxOutputFileSystem.OutputObservation? ReadFileMetadata(string leaf)
    {
        LinuxOutputFileSystem.ValidateLeaf(leaf);
        lock (_gate)
        {
            EnsureUsable();
            VerifyRoot();
            Microsoft.Win32.SafeHandles.SafeFileHandle file;
            try { file = OpenRead(_directory, leaf); }
            catch (Exception error) when (IsMissing(error))
            {
                VerifyRoot();
                return null;
            }
            using (file)
            {
                var before = LinuxOutputFileSystem.Observe(file, RegularFileType);
                RequireOwnedFile(before, expectedLinks: 1, requirePrivateMode: false);
                BeforeOperationForTests?.Invoke("metadata-open", Path.Combine(FullPath, leaf));
                if (LinuxOutputFileSystem.Observe(file, RegularFileType) != before)
                    throw new IOException("Linux output metadata changed during observation.");
                BeforeOperationForTests?.Invoke("metadata-before-named", Path.Combine(FullPath, leaf));
                using var named = OpenRead(_directory, leaf);
                if (LinuxOutputFileSystem.Observe(named, RegularFileType) != before)
                    throw new IOException("Linux output leaf no longer names its observed metadata.");
                VerifyRoot();
                return before; // No payload read: even oversized recovery files can be accounted for.
            }
        }
    }
}
