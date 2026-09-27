using System;
using System.Diagnostics;
using System.IO;

namespace FFXProjectEditor.Services.Extras
{
    internal static class ExtrasFileOpen_Service
    {
        public static bool TryOpenInExplorer(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                string normalized = Path.GetFullPath(path);
                if (!File.Exists(normalized) && !Directory.Exists(normalized))
                    return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{normalized}\"",
                    UseShellExecute = true
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string ReadTextPreview(string? path, int maxCharacters = 1600)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "No local file preview available.";

            try
            {
                string text = File.ReadAllText(path);
                if (text.Length <= maxCharacters)
                    return text;

                return text[..maxCharacters] + Environment.NewLine + Environment.NewLine + "...";
            }
            catch (IOException)
            {
                return "Preview blocked by file I/O failure.";
            }
            catch (UnauthorizedAccessException)
            {
                return "Preview blocked by access restrictions.";
            }
        }
    }
}
