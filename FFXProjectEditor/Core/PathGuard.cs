using System;
using System.IO;
using FFXProjectEditor.Modules.Common.ViewerHub;

namespace FFXProjectEditor.Core
{
    // ── Native path policy, before filesystem capabilities ──
    // Component boundaries and host case rules prevent lexical escapes after workstation migration.
    // MAINT: ancestor inspection is an early refusal, not a TOCTOU-safe I/O authorization. Protected
    // consumers must still open and use retained FileSystemReparseGuard capabilities.
    /// <summary>
    /// Path safety enforcement for workspace source/output separation,
    /// reparse point (symlink/junction) detection, and traversal blocking.
    /// </summary>
    public static class PathGuard
    {
        /// <summary>
        /// Validate that a file path is safe to read from the source workspace.
        /// Blocks traversal attempts, reparse points, and out-of-bounds reads.
        /// </summary>
        public static PathValidationResult ValidateSourcePath(string sourceRoot, string targetPath)
        {
            var errors = new System.Collections.Generic.List<string>();

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                errors.Add("Target path is null or empty.");
                return new PathValidationResult(false, errors);
            }

            if (HasParentComponent(targetPath))
            {
                errors.Add($"Path escapes source root: {targetPath}");
                return new PathValidationResult(false, errors);
            }
            if (HasForeignSyntax(targetPath))
            {
                errors.Add("Invalid native path syntax.");
                return new PathValidationResult(false, errors);
            }

            // Validate both inputs inside the failure boundary; a malformed root must not throw.
            string fullPath;
            string normalizedRoot;
            try
            {
                normalizedRoot = CanonicalAbsolutePath(sourceRoot);
                fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, targetPath));
            }
            catch (Exception ex)
            {
                errors.Add($"Invalid path: {ex.Message}");
                return new PathValidationResult(false, errors);
            }

            if (!IsSameOrDescendant(normalizedRoot, fullPath))
            {
                errors.Add($"Path escapes source root: {fullPath}");
                return new PathValidationResult(false, errors);
            }

            if (FileSystemReparseGuard.ContainsReparsePointInExistingChain(fullPath))
            {
                errors.Add("Target or ancestor is a reparse point or inaccessible. Blocked for safety.");
                return new PathValidationResult(false, errors);
            }

            return new PathValidationResult(true, Array.Empty<string>());
        }

        /// <summary>
        /// Validate that an output path is safe and separate from the source workspace.
        /// </summary>
        public static PathValidationResult ValidateOutputPath(string sourceRoot, string outputPath)
        {
            var errors = new System.Collections.Generic.List<string>();

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                errors.Add("Output path is null or empty.");
                return new PathValidationResult(false, errors);
            }

            string fullOutput;
            string fullSource;
            try
            {
                fullSource = CanonicalAbsolutePath(sourceRoot);
                fullOutput = CanonicalAbsolutePath(outputPath);
            }
            catch (Exception ex)
            {
                errors.Add($"Invalid output path: {ex.Message}");
                return new PathValidationResult(false, errors);
            }

            // Output must not be inside source root
            if (IsSameOrDescendant(fullSource, fullOutput))
            {
                errors.Add("Output path must not be inside the source workspace.");
                return new PathValidationResult(false, errors);
            }

            // Source must not be inside output
            if (IsSameOrDescendant(fullOutput, fullSource))
            {
                errors.Add("Source workspace must not be inside the output directory.");
                return new PathValidationResult(false, errors);
            }

            if (FileSystemReparseGuard.ContainsReparsePointInExistingChain(fullSource) ||
                FileSystemReparseGuard.ContainsReparsePointInExistingChain(fullOutput))
            {
                errors.Add("Source/output ancestor is a reparse point or inaccessible. Blocked for safety.");
                return new PathValidationResult(false, errors);
            }

            return new PathValidationResult(true, Array.Empty<string>());
        }

        /// <summary>
        /// Check if a path is likely a dangerous system directory.
        /// </summary>
        public static bool IsSystemDirectory(string path)
        {
            try
            {
                string full = NormalizeSystemCandidate(path);
                if (full.Length >= 3 && char.IsAsciiLetter(full[0]) && full[1] == ':' && full[2] == '/')
                {
                    string first = full[3..].Split('/')[0];
                    return first.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                        first.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                        first.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
                        first.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase);
                }
                if (full == "/") return true;
                foreach (string system in new[] { "/usr", "/bin", "/sbin", "/etc" })
                {
                    if (full.Equals(system, StringComparison.Ordinal) ||
                        full.StartsWith(system + "/", StringComparison.Ordinal)) return true;
                }
                return false;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                return true;
            }
        }

        private static bool HasParentComponent(string path) =>
            Array.Exists(path.Split(new[] { '/', '\\' }), component => component == "..");

        private static bool HasForeignSyntax(string path) =>
            !OperatingSystem.IsWindows() &&
            (path.Contains('\\') || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'));

        private static string CanonicalAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || HasForeignSyntax(path) ||
                HasParentComponent(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("An unambiguous absolute native path is required.");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        private static bool IsSameOrDescendant(string root, string candidate)
        {
            string relative = Path.GetRelativePath(root, candidate);
            return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        }

        // Imported Windows paths need lexical classification even on Linux; this never grants
        // permission to open them. POSIX components remain case-sensitive on either host.
        private static string NormalizeSystemCandidate(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
                throw new ArgumentException("Path is empty or invalid.");
            string normalized = path.Replace('\\', '/');
            bool driveAbsolute = normalized.Length >= 3 && char.IsAsciiLetter(normalized[0]) &&
                normalized[1] == ':' && normalized[2] == '/';
            bool posixAbsolute = normalized.StartsWith('/') && !normalized.StartsWith("//", StringComparison.Ordinal);
            if (driveAbsolute || posixAbsolute)
            {
                int prefixLength = driveAbsolute ? 3 : 1;
                var components = new System.Collections.Generic.List<string>();
                foreach (string component in normalized[prefixLength..].Split('/'))
                {
                    if (component is "" or ".") continue;
                    if (component == "..")
                    {
                        if (components.Count > 0) components.RemoveAt(components.Count - 1);
                    }
                    else components.Add(component);
                }
                return normalized[..prefixLength] + string.Join('/', components);
            }
            return Path.GetFullPath(path).Replace('\\', '/');
        }
    }

    /// <summary>
    /// Result of a path validation check.
    /// </summary>
    public sealed record PathValidationResult
    {
        public bool IsValid { get; init; }
        public System.Collections.Generic.IReadOnlyList<string> Errors { get; init; }

        public PathValidationResult(bool isValid, System.Collections.Generic.IReadOnlyList<string> errors)
        {
            IsValid = isValid;
            Errors = errors;
        }
    }
}
