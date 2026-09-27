using FFXProjectEditor.Diagnostics;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using static FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── Private descendants of an already retained Linux root ──
// Fixed application subdirectories must not restart traversal from a mutable absolute path.
// MAINT: children borrow the parent's lifetime, validate its anchor on every operation, and
// never repair permissions or remove a directory after an uncertain acquisition.
internal sealed partial class LinuxOwnedOutputDirectory
{
    // The inspector may retain an existing private journal, but must not create it while inspecting.
    internal LinuxOwnedOutputDirectory OpenExistingChild(string leaf) => OpenChild(leaf, createMissing: false);

    internal LinuxOwnedOutputDirectory OpenOrCreateChild(string leaf) => OpenChild(leaf, createMissing: true);

    private LinuxOwnedOutputDirectory OpenChild(string leaf, bool createMissing)
    {
        LinuxOutputFileSystem.ValidateLeaf(leaf);
        lock (_gate)
        {
            EnsureUsable();
            if (_childDepth >= 8) throw new IOException("Linux owned child depth exceeds eight levels.");
            VerifyRoot();
            string path = Path.Combine(FullPath, leaf);
            BeforeOperationForTests?.Invoke("child-before-open", path);
            VerifyRoot();
            SafeFileHandle? child = null;
            try
            {
                try { child = LinuxOutputFileSystem.OpenOwnedChildDirectory(_directory, leaf); }
                catch (Exception error) when (createMissing && IsMissing(error))
                {
                    VerifyRoot();
                    LinuxOutputFileSystem.CreateDirectoryComponent(_directory, leaf);
                    BeforeOperationForTests?.Invoke("child-created", path);
                    LinuxOutputFileSystem.Sync(_directory);
                    child = LinuxOutputFileSystem.OpenOwnedChildDirectory(_directory, leaf);
                }
                LinuxOutputFileSystem.VerifyFileSystem(child);
                var observed = LinuxOutputFileSystem.Observe(child, DirectoryType);
                RequirePrivateChild(observed);
                BeforeOperationForTests?.Invoke("child-opened", path);
                VerifyRoot();
                var result = new LinuxOwnedOutputDirectory(path, child, observed.File.Identity, this);
                result.VerifyRoot();
                DebugLog.Info(LogCategory, "Retained private Linux child directory.");
                return result;
            }
            catch
            {
                child?.Dispose();
                throw;
            }
        }
    }

    private void VerifyChildAnchor(string leaf, LinuxObjectIdentity identity)
    {
        lock (_gate)
        {
            EnsureUsable();
            VerifyRoot();
            using var named = LinuxOutputFileSystem.OpenOwnedChildDirectory(_directory, leaf);
            var observed = LinuxOutputFileSystem.Observe(named, DirectoryType);
            RequirePrivateChild(observed);
            if (observed.File.Identity != identity)
                throw new IOException("Linux child no longer belongs to its retained parent.");
        }
    }

    private static void RequirePrivateChild(LinuxOutputFileSystem.OutputObservation value)
    {
        RequireOwnedDirectory(value);
        if ((value.Mode & 0xFFF) != 0x1C0)
            throw new IOException("Linux application child directory must have private 0700 permissions.");
    }
}
