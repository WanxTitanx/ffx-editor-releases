// WHY: Process-global FD counts include unrelated runtime assembly/network initialization.
// MAINT: Only synchronous, unmoved private fixtures are in scope. This is a test oracle,
// not product authority, a socket-leak audit, or a guarantee against concurrent fd reuse.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FFXProjectEditor.Tests.Infrastructure;

internal static class LinuxFixtureDescriptors
{
    internal readonly record struct Descriptor(int Number, string Target);

    internal static Descriptor[] Capture(string fixtureRoot)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Fixture descriptor snapshots require procfs.");
        ArgumentException.ThrowIfNullOrEmpty(fixtureRoot);
        if (!Path.IsPathFullyQualified(fixtureRoot))
            throw new ArgumentException("The fixture root must be absolute.", nameof(fixtureRoot));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fixtureRoot));
        if (root == "/")
            throw new ArgumentException("A fixture cannot be the filesystem root.", nameof(fixtureRoot));

        var result = new List<Descriptor>();
        // Materialization closes our enumeration FD before readlink. An ambient FD may have
        // closed meanwhile; null is not a live target. Other I/O exceptions are not hidden.
        foreach (string path in Directory.GetFileSystemEntries("/proc/self/fd"))
        {
            string? target = new FileInfo(path).LinkTarget;
            if (target is null || !IsWithinRoot(root, target)) continue;
            int number = int.Parse(Path.GetFileName(path), CultureInfo.InvariantCulture);
            result.Add(new Descriptor(number, target));
        }
        result.Sort((left, right) => left.Number.CompareTo(right.Number));
        return result.ToArray();
    }

    // Do not resolve links: unlinked files and O_TMPFILE handles still have raw "(deleted)"
    // targets. The separator prevents sibling prefixes from being mistaken for ownership.
    internal static bool IsWithinRoot(string root, string target) =>
        string.Equals(target, root, StringComparison.Ordinal) ||
        string.Equals(target, root + " (deleted)", StringComparison.Ordinal) ||
        target.StartsWith(root + "/", StringComparison.Ordinal);
}
