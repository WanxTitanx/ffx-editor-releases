using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.Core
{
    /// <summary>
    /// Service that inspects a workspace directory to detect platform,
    /// version, and enumerate available resources.
    /// </summary>
    public sealed class WorkspaceHealthCheck
    {
        /// <summary>
        /// Known file signatures used for platform/version detection.
        /// </summary>
        private static readonly (string RelativePath, string VersionTag, Platform Platform)[] KnownSignatures = new[]
        {
            ("ffx.exe", "PC_STEAM", Platform.PC),
            ("ffx_ps2.iso", "PS2_NTSC", Platform.PS2),
            ("FFX_DATADIR", "PS2_NTSC", Platform.PS2),
            ("PS3HD", "PS3_HD_REMASTER", Platform.PS3),
            ("ffx.dat", "PS2_NTSC", Platform.PS2),
        };

        /// <summary>
        /// Known data file extensions that indicate a valid extracted workspace.
        /// </summary>
        private static readonly string[] DataFileExtensions = new[]
        {
            ".bin", ".dat", ".tbl", ".ebp", ".ebm", ".eff", ".phyre"
        };

        /// <summary>
        /// Known subdirectory names found in extracted FFX workspaces.
        /// </summary>
        private static readonly string[] ExpectedDirectories = new[]
        {
            "master", "monster", "battle", "field", "save"
        };

        /// <summary>
        /// Inspect the given workspace root and return a health summary.
        /// </summary>
        public WorkspaceInspectionResult Inspect(string sourcePath, string? outputPath = null)
        {
            if (!Directory.Exists(sourcePath))
                throw new DirectoryNotFoundException($"Workspace not found: {sourcePath}");

            var platform = DetectPlatform(sourcePath);
            var version = DetectVersion(sourcePath);
            var warnings = new List<string>();
            var resourceSummary = EnumerateResources(sourcePath, warnings);

            return new WorkspaceInspectionResult
            {
                SourcePath = Path.GetFullPath(sourcePath),
                GamePath = Project_Service.TryResolveGameInstallRoot(sourcePath),
                OutputPath = outputPath != null ? Path.GetFullPath(outputPath) : null,
                DetectedVersion = version,
                DetectedPlatform = platform,
                Capabilities = Array.Empty<CapabilityAvailability>(),
                MissingDependencies = Array.Empty<string>(),
                Warnings = warnings,
                ScanTimestamp = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// Detect the platform by probing for known file signatures.
        /// </summary>
        public Platform DetectPlatform(string sourcePath)
        {
            foreach (var (relativePath, _, platform) in KnownSignatures)
            {
                var fullPath = Path.Combine(sourcePath, relativePath);
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                    return platform;
            }
            return Platform.All;
        }

        /// <summary>
        /// Detect the game version string from file signatures.
        /// </summary>
        public string DetectVersion(string sourcePath)
        {
            foreach (var (relativePath, versionTag, _) in KnownSignatures)
            {
                var fullPath = Path.Combine(sourcePath, relativePath);
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                    return versionTag;
            }
            return "UNKNOWN";
        }

        /// <summary>
        /// Enumerate data files and known directories in the workspace.
        /// Populates warnings for missing expected directories.
        /// </summary>
        private WorkspaceResources EnumerateResources(string sourcePath, List<string> warnings)
        {
            var dataFiles = new List<string>();
            foreach (var ext in DataFileExtensions)
            {
                try
                {
                    dataFiles.AddRange(
                        Directory.EnumerateFiles(sourcePath, $"*{ext}", SearchOption.AllDirectories));
                }
                catch (UnauthorizedAccessException) { }
            }

            var presentDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in ExpectedDirectories)
            {
                var fullPath = Path.Combine(sourcePath, dir);
                if (Directory.Exists(fullPath))
                    presentDirs.Add(dir);
                else
                    warnings.Add($"Expected directory not found: {dir}");
            }

            return new WorkspaceResources
            {
                DataFileCount = dataFiles.Count,
                PresentDirectories = presentDirs
            };
        }

        /// <summary>
        /// Aggregate stats from a workspace scan.
        /// </summary>
        private sealed class WorkspaceResources
        {
            public int DataFileCount { get; init; }
            public HashSet<string> PresentDirectories { get; init; } = new();
        }
    }
}
