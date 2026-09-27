using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.IO;
using System.Runtime.CompilerServices;
using FFXProjectEditor.Modules.MagicDllEditor;

namespace FFXProjectEditor.Tests.MagicDll;

/// <summary>
/// Test-only staging ownership for Magic DLL parsing. Production deliberately keeps its
/// LocalAppData path fail-closed; the test assembly uses a unique Temp path so a host
/// profile reparse point cannot turn unrelated parser tests into infrastructure failures.
/// </summary>
internal static class MagicDllTestStaging
{
    private static readonly string DefaultRoot = CreateRoot();

    [ModuleInitializer]
    internal static void Initialize()
    {
        MagicDllDocument_Wrapper.StagingRootOverrideForTests = DefaultRoot;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDeleteExactNonReparseEmptyRoot(DefaultRoot);
    }

    internal static IDisposable PushCustomRoot(out string root)
    {
        root = CreateRoot();
        return new StagingRootScope(root);
    }

    private static string CreateRoot()
    {
        string root;
        do
        {
            root = Path.Combine(
                FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot + "/work",
                "FFXProjectEditor.Tests",
                "MagicDll",
                $"staging-{Environment.ProcessId}-{Guid.NewGuid():N}");
        }
        while (Directory.Exists(root) || File.Exists(root));

        return root;
    }

    private sealed class StagingRootScope : IDisposable
    {
        private readonly string? _previousRoot;
        private readonly string _root;
        private bool _disposed;

        internal StagingRootScope(string root)
        {
            _previousRoot = MagicDllDocument_Wrapper.StagingRootOverrideForTests;
            _root = root;
            MagicDllDocument_Wrapper.StagingRootOverrideForTests = _root;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            MagicDllDocument_Wrapper.StagingRootOverrideForTests = _previousRoot;
            TryDeleteExactNonReparseEmptyRoot(_root);
        }
    }

    // Cleanup is deliberately non-recursive. A test that replaces a root must leave that
    // replacement untouched rather than allowing cleanup to traverse an attacker-controlled path.
    private static void TryDeleteExactNonReparseEmptyRoot(string expectedRoot)
    {
        try
        {
            string fullRoot = Path.GetFullPath(expectedRoot);
            if (!string.Equals(expectedRoot, fullRoot, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(fullRoot) ||
                (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
                return;

            Directory.Delete(fullRoot, recursive: false);
        }
        catch
        {
            // Process-exit and test cleanup are best-effort only.
        }
    }
}
