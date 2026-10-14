using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Services.Extras
{
    internal sealed record VbfExtractProbe(
        string Label,
        string ToolPath,
        string VbfPath,
        string DictionaryPath,
        string OutputRoot,
        bool ToolExists,
        bool VbfExists,
        bool DictionaryExists,
        long VbfSize,
        int DictionaryLineCount)
    {
        public bool Ready => ToolExists && VbfExists && DictionaryExists && !string.IsNullOrWhiteSpace(OutputRoot);
        public string SizeSummary => VbfExists ? FormatBytes(VbfSize) : "-";
        public string DictionarySummary => DictionaryExists ? $"{DictionaryLineCount:N0} candidate paths" : "-";
        public string Summary =>
            $"{Label}: tool={(ToolExists ? "OK" : "missing")} · vbf={(VbfExists ? SizeSummary : "missing")} · list={(DictionaryExists ? DictionarySummary : "missing")} · output={OutputRoot}";

        static string FormatBytes(long bytes)
        {
            double value = bytes;
            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            int suffix = 0;
            while (value >= 1024 && suffix < suffixes.Length - 1)
            {
                value /= 1024;
                suffix++;
            }

            return $"{value:0.##} {suffixes[suffix]}";
        }
    }

    internal sealed record VbfExtractRunResult(
        int ExitCode,
        int ExtractedCount,
        int TotalCount,
        string OutputRoot,
        IReadOnlyList<string> Lines)
    {
        public bool Pass => ExitCode == 0 && (TotalCount == 0 || ExtractedCount > 0);
        public string Summary => TotalCount > 0
            ? $"Exit {ExitCode} · extracted {ExtractedCount:N0}/{TotalCount:N0} files -> {OutputRoot}"
            : $"Exit {ExitCode} -> {OutputRoot}";
    }

    internal static class VbfExtract_Service
    {
        private static string ExtractedOutputRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "Extracted");

        public static string DefaultSteamDataRoot => PortablePathResolver.GameInstallRoot is string root
            ? Path.Combine(root, "data")
            : string.Empty;
        public static string DefaultExtractedFfxRoot => PortablePathResolver.ExtractedFfxRoot
            ?? Path.Combine(ExtractedOutputRoot, "FFX");
        public static string DefaultExtractedFfx2Root => Path.Combine(ExtractedOutputRoot, "FFX2");
        public static string DefaultExtractedMetaRoot => Path.Combine(ExtractedOutputRoot, "MetaMenu");

        static string[] KnownToolPaths =>
        [
            Environment.GetEnvironmentVariable("FFX_VBFEXTRACT") ?? string.Empty,
            Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ModsRoot, @"VBFExtract-master\VBFExtract-master\vbfextract.exe"),
            Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ModsRoot, @"vbfextract_0.0.2\vbfextract.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "vbfextract", "vbfextract.exe"),
            Path.Combine(AppContext.BaseDirectory, "vbfextract.exe")
        ];

        public static VbfExtractProbe DetectFfx() => Detect("FFX_Data", DefaultExtractedFfxRoot);
        public static VbfExtractProbe DetectFfx2() => Detect("FFX2_Data", DefaultExtractedFfx2Root);
        public static VbfExtractProbe DetectMetaMenu() => Detect("metamenu", DefaultExtractedMetaRoot);

        public static VbfExtractProbe BuildProbe(string label, string toolPath, string vbfPath, string dictionaryPath, string outputRoot)
        {
            bool toolExists = File.Exists(toolPath);
            bool vbfExists = File.Exists(vbfPath);
            bool dictionaryExists = File.Exists(dictionaryPath);
            long vbfSize = 0;
            int dictionaryLineCount = 0;

            if (vbfExists)
                vbfSize = new FileInfo(vbfPath).Length;
            if (dictionaryExists)
                dictionaryLineCount = CountLines(dictionaryPath);

            return new VbfExtractProbe(
                label,
                toolPath,
                vbfPath,
                dictionaryPath,
                outputRoot,
                toolExists,
                vbfExists,
                dictionaryExists,
                vbfSize,
                dictionaryLineCount);
        }

        public static async Task<VbfExtractRunResult> RunAsync(
            VbfExtractProbe probe,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            if (!probe.Ready)
                throw new InvalidOperationException("VBF extraction is not ready. Check tool, VBF, dictionary, and output paths.");

            Directory.CreateDirectory(probe.OutputRoot);
            List<string> lines = [];
            void Capture(string line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;
                lock (lines)
                {
                    lines.Add(line);
                }
                progress?.Report(line);
            }

            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = probe.ToolPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(probe.ToolPath) ?? Environment.CurrentDirectory
            };
            process.StartInfo.ArgumentList.Add("-o");
            process.StartInfo.ArgumentList.Add(probe.OutputRoot);
            process.StartInfo.ArgumentList.Add("-f");
            process.StartInfo.ArgumentList.Add(probe.DictionaryPath);
            process.StartInfo.ArgumentList.Add(probe.VbfPath);

            process.OutputDataReceived += (_, e) => { if (e.Data != null) Capture(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Capture("ERR: " + e.Data); };

            Capture("Running: " + BuildCommandPreview(probe));
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            int extracted = 0;
            int total = 0;
            lock (lines)
            {
                foreach (string line in lines)
                {
                    Match match = Regex.Match(line, @"Extracted\s+(?<ok>\d+)\s*/\s*(?<total>\d+)\s+files", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        extracted = int.Parse(match.Groups["ok"].Value, CultureInfo.InvariantCulture);
                        total = int.Parse(match.Groups["total"].Value, CultureInfo.InvariantCulture);
                    }
                }
            }

            return new VbfExtractRunResult(process.ExitCode, extracted, total, probe.OutputRoot, lines.ToList());
        }

        public static string BuildCommandPreview(VbfExtractProbe probe) =>
            $"\"{probe.ToolPath}\" -o \"{probe.OutputRoot}\" -f \"{probe.DictionaryPath}\" \"{probe.VbfPath}\"";

        static VbfExtractProbe Detect(string basename, string defaultOutputRoot)
        {
            string tool = KnownToolPaths.FirstOrDefault(File.Exists)
                ?? Path.Combine(AppContext.BaseDirectory, "tools", "vbfextract", "vbfextract.exe");
            string vbf = string.IsNullOrWhiteSpace(DefaultSteamDataRoot)
                ? string.Empty
                : Path.Combine(DefaultSteamDataRoot, basename + ".vbf");
            string dictionary = ResolveDictionary(tool, basename);
            return BuildProbe(basename, tool, vbf, dictionary, defaultOutputRoot);
        }

        static string ResolveDictionary(string toolPath, string basename)
        {
            string? toolDir = Path.GetDirectoryName(toolPath);
            List<string> candidates =
            [
                toolDir == null ? string.Empty : Path.Combine(toolDir, basename + ".txt"),
                Path.Combine(AppContext.BaseDirectory, "tools", "vbfextract", basename + ".txt"),
                Path.Combine(Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ModsRoot, @"VBFExtract-master\VBFExtract-master"), basename + ".txt"),
                Path.Combine(Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ModsRoot, @"vbfextract_0.0.2"), basename + ".txt")
            ];

            return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                ?? candidates.First(path => !string.IsNullOrWhiteSpace(path));
        }

        static int CountLines(string path)
        {
            int count = 0;
            using StreamReader reader = File.OpenText(path);
            while (reader.ReadLine() != null)
                count++;
            return count;
        }
    }
}
