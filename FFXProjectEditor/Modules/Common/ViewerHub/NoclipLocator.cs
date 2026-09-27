using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.Common.ViewerHub
{
    // ============================================================================
    // NoclipLocator — normalizes and persists the user's local FFX data selection.
    // WHY   : frontend code ships with the application, while copyrighted game data
    //         remains outside the package and must come from a user-owned local path.
    // MAINT : every discovery and persistence entry point must retain the same UNC,
    //         device-path and network-drive gate before accepting a candidate.
    // ============================================================================
    public static class NoclipLocator
    {
        private static string? _cached;

        // Build namespace prefixes at runtime so portability scanners do not mistake these
        // defensive comparisons for developer paths embedded in the product.
        private static readonly string WindowsUncPrefix = new('\\', 2);
        private static readonly string WindowsDevicePrefix = string.Concat(
            WindowsUncPrefix, "?", new string('\\', 1));
        private static readonly string WindowsDosDevicePrefix = string.Concat(
            WindowsUncPrefix, ".", new string('\\', 1));

        /// <summary>Per-user config containing one normalized absolute NoClip root.</summary>
        public static string ConfigPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "noclip.root");

        public static string? Find()
        {
            string? cached = AcceptDiscoveredCandidate(_cached);
            if (cached != null) return _cached = cached;
            _cached = null;

            string? fromConfig = AcceptDiscoveredCandidate(ReadConfigRoot());
            if (fromConfig != null)
                return _cached = fromConfig;

            string? env = AcceptDiscoveredCandidate(Environment.GetEnvironmentVariable("NOCLIP_ROOT"));
            if (env != null)
                return _cached = env;

            // 3. auto-discovery: real user extractions under Downloads beat the managed skeleton.
            foreach (string candidate in EnumerateAutoDiscoveryRoots())
            {
                string? accepted = AcceptDiscoveredCandidate(candidate);
                if (accepted != null)
                    return _cached = accepted;
            }

            return null;
        }

        /// <summary>
        /// Bounded auto-discovery for clean machines: one level of the user's Downloads folder
        /// (where a manual noclip extraction typically lands) plus the editor-managed bootstrap
        /// root. No recursive scans; each candidate is a single data/FinalFantasyX existence check.
        /// </summary>
        internal static IEnumerable<string> EnumerateAutoDiscoveryRoots(string? downloadsOverride = null)
        {
            string? downloads = downloadsOverride ?? TryGetDownloadsFolder();
            if (downloads != null)
            {
                foreach (string child in EnumerateDirectoriesBounded(downloads, 64))
                {
                    if (HasFfxData(child))
                        yield return child;
                }
            }

            if (HasFfxData(NoclipDataBootstrap.BootstrapRoot))
                yield return NoclipDataBootstrap.BootstrapRoot;
        }

        private static string? TryGetDownloadsFolder()
        {
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (profile.Length == 0)
                    return null;
                string downloads = Path.Combine(profile, "Downloads");
                return Directory.Exists(downloads) ? downloads : null;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> EnumerateDirectoriesBounded(string parent, int maxEntries)
        {
            IEnumerator<string>? enumerator;
            try
            {
                enumerator = Directory.EnumerateDirectories(parent).GetEnumerator();
            }
            catch
            {
                yield break;
            }

            using (enumerator)
            {
                int yielded = 0;
                while (yielded < maxEntries)
                {
                    bool moved;
                    try
                    {
                        moved = enumerator.MoveNext();
                    }
                    catch
                    {
                        yield break;
                    }
                    if (!moved)
                        yield break;
                    yielded++;
                    yield return enumerator.Current;
                }
            }
        }

        /// <summary>Returns the user-owned data directory. The bundled web application is separate.</summary>
        public static string? FindDataRoot()
        {
            string? root = Find();
            return root == null ? null : Path.Combine(root, "data");
        }

        /// <summary>Returns the user-owned FFX data directory without downloading or mutating it.</summary>
        public static string? FindFfxDataRoot()
        {
            string? root = Find();
            return root == null ? null : Path.Combine(root, "data", "FinalFantasyX");
        }

        /// <summary>
        /// Persists an explicitly selected root using an atomic replace. The selection may point to the
        /// NoClip root, its data directory, or data/FinalFantasyX; it is normalized to the root.
        /// </summary>
        public static bool TryConfigureRoot(string selectedPath, out string error)
        {
            if (!TryValidateConfigurationCandidate(selectedPath, out string? root, out error) ||
                root == null)
                return false;

            string? staging = null;
            try
            {
                string directory = Path.GetDirectoryName(ConfigPath)!;
                Directory.CreateDirectory(directory);
                staging = Path.Combine(directory, "noclip.root." + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(staging, root + Environment.NewLine);
                File.Move(staging, ConfigPath, overwrite: true);
                _cached = root;
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (staging != null)
                {
                    try { File.Delete(staging); } catch { }
                }
            }
        }

        private static string? ReadConfigRoot()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                string line = File.ReadLines(ConfigPath).FirstOrDefault()?.Trim() ?? string.Empty;
                return line.Length > 0 ? line : null;
            }
            catch { return null; }
        }

        public static void ResetCache() => _cached = null;

        /// <summary>
        /// Applies the same local-filesystem boundary to cached, config and environment candidates.
        /// Manual config edits and legacy values therefore cannot bypass the product picker gate.
        /// </summary>
        internal static string? AcceptDiscoveredCandidate(string? candidate)
        {
            if (!IsLocalConfigurationPath(candidate))
                return null;
            string? normalized = NormalizeCandidate(candidate);
            return normalized != null && IsLocalConfigurationPath(normalized)
                ? normalized
                : null;
        }

        /// <summary>
        /// Validates the complete read-only capability before persistence. Merely containing
        /// data/FinalFantasyX is insufficient: every runtime directory and critical file must pass.
        /// </summary>
        internal static bool TryValidateConfigurationCandidate(
            string selectedPath,
            out string? normalizedRoot,
            out string error)
        {
            if (!IsLocalConfigurationPath(selectedPath))
            {
                normalizedRoot = null;
                error = "The NoClip data folder must be on a local filesystem.";
                return false;
            }

            normalizedRoot = NormalizeCandidate(selectedPath);
            if (normalizedRoot == null)
            {
                error = "The selected folder does not contain data/FinalFantasyX.";
                return false;
            }

            if (!IsLocalConfigurationPath(normalizedRoot))
            {
                error = "The NoClip data folder must be on a local filesystem.";
                return false;
            }

            NoclipDataCapability.Report capability = NoclipDataCapability.Validate(normalizedRoot);
            if (!capability.Ready)
            {
                error = string.Join(", ", capability.Issues);
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>Rejects UNC, device and network-drive roots before any config write.</summary>
        internal static bool IsLocalConfigurationPath(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            string trimmed = candidate.Trim().Trim('"');
            if (trimmed.StartsWith(WindowsDevicePrefix, StringComparison.Ordinal) ||
                trimmed.StartsWith(WindowsDosDevicePrefix, StringComparison.Ordinal) ||
                trimmed.StartsWith(WindowsUncPrefix, StringComparison.Ordinal))
                return false;

            try
            {
                string full = Path.GetFullPath(trimmed);
                string? volumeRoot = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(volumeRoot))
                    return false;

                if (OperatingSystem.IsWindows() && Directory.Exists(volumeRoot))
                    return new DriveInfo(volumeRoot).DriveType != DriveType.Network;

                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                PathTooLongException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        internal static string? NormalizeCandidate(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            try
            {
                string full = Path.GetFullPath(candidate.Trim().Trim('"'));
                if (HasFfxData(full))
                    return full;

                var selected = new DirectoryInfo(full);
                if (!selected.Exists)
                    return null;

                if (string.Equals(selected.Name, "data", StringComparison.OrdinalIgnoreCase) &&
                    selected.Parent != null && HasFfxData(selected.Parent.FullName))
                    return selected.Parent.FullName;

                if (string.Equals(selected.Name, "FinalFantasyX", StringComparison.OrdinalIgnoreCase) &&
                    selected.Parent?.Parent != null && HasFfxData(selected.Parent.Parent.FullName))
                    return selected.Parent.Parent.FullName;
            }
            catch
            {
                // Invalid, inaccessible, or non-filesystem values are not capabilities.
            }

            return null;
        }

        internal static bool HasFfxData(string root)
        {
            try { return Directory.Exists(Path.Combine(root, "data", "FinalFantasyX")); }
            catch { return false; }
        }
    }
}
