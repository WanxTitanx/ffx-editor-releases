using FFXProjectEditor.FfxLib.Ability;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Services.Extras
{
    /// <summary>Install fsbankcl.exe into tools/fsbankcl for bundling (user-supplied or FMOD SDK).</summary>
    public static class FfxFsbankClImport_Service
    {
        static readonly string[] SdkSearchRoots = BuildSdkSearchRoots();

        static readonly string[] FsbankExeNames = ["fsbankexcl.exe", "fsbankcl.exe", "fsbankex.exe"];

        public static IReadOnlyList<string> SdkSearchRootsPublic => SdkSearchRoots;

        static string[] BuildSdkSearchRoots()
        {
            return new[]
            {
                Environment.GetEnvironmentVariable("FFX_FMOD_SDK_ROOT") ?? Environment.GetEnvironmentVariable("FMOD_SDK_ROOT"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FMOD SoundSystem"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FMOD SoundSystem"),
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        }

        public static string BundledDir
        {
            get
            {
                string? repo = CommandSoundCorpusLoader.FindRepoRoot();
                return repo == null
                    ? Path.Combine(AppContext.BaseDirectory, "tools", "fsbankcl")
                    : Path.Combine(repo, "tools", "fsbankcl");
            }
        }

        public static string BundledPath => Path.Combine(BundledDir, "fsbankcl.exe");

        public static (bool Ok, string Message) TryCopyFromSdkInstall()
        {
            string? found = FindOnDisk();
            if (found == null)
                return (false, "fsbankcl/fsbankexcl not found. Install FMOD FSBANK EX or use Import fsbankcl…");

            return InstallFromPath(found);
        }

        public static (bool Ok, string Message) InstallFromPath(string sourceExe)
        {
            if (!File.Exists(sourceExe))
                return (false, "Source executable not found.");

            string fileName = Path.GetFileName(sourceExe);
            if (!FsbankExeNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                return (false, $"Expected fsbankexcl.exe / fsbankcl.exe, got {fileName}");

            string destDir = BundledDir;
            try
            {
                Directory.CreateDirectory(destDir);
                string srcDir = Path.GetDirectoryName(sourceExe)!;

                CopyBundleFromDirectory(srcDir, destDir);

                // Ensure editor locator names exist.
                string primary = Path.Combine(destDir, fileName);
                if (!File.Exists(primary))
                    File.Copy(sourceExe, primary, overwrite: true);

                string fsbankCl = Path.Combine(destDir, "fsbankcl.exe");
                string fsbankExCl = Path.Combine(destDir, "fsbankexcl.exe");
                if (fileName.Equals("fsbankexcl.exe", StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(primary, fsbankCl, overwrite: true);
                    File.Copy(primary, fsbankExCl, overwrite: true);
                }
                else if (fileName.Equals("fsbankcl.exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(fsbankExCl))
                        File.Copy(primary, fsbankExCl, overwrite: true);
                }

                SyncBesideEditorExe(destDir);
                return (true, $"Installed fsbank bundle: {destDir}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static string? FindOnDisk()
        {
            string? bundled = FfxAudioToolsLocator.LocateFsbankCl();
            if (bundled != null && File.Exists(bundled))
                return bundled;

            foreach (string root in SdkSearchRoots)
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (string name in FsbankExeNames)
                {
                    try
                    {
                        foreach (string hit in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
                            return hit;
                    }
                    catch { /* ignore */ }
                }
            }

            return null;
        }

        static void CopyBundleFromDirectory(string srcDir, string destDir)
        {
            foreach (string file in Directory.EnumerateFiles(srcDir))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    File.Copy(file, Path.Combine(destDir, name), overwrite: true);
            }
        }

        static void SyncBesideEditorExe(string installedDir)
        {
            string exeTools = Path.Combine(AppContext.BaseDirectory, "tools", "fsbankcl");
            if (string.Equals(Path.GetFullPath(installedDir), Path.GetFullPath(exeTools), StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                Directory.CreateDirectory(exeTools);
                CopyBundleFromDirectory(installedDir, exeTools);
            }
            catch { /* ignore */ }
        }
    }
}
