using System;
using System.IO;

namespace FFXProjectEditor.Utils
{
    // Best-effort crash/diagnostic logger. Writes unhandled exceptions (and caught-but-notable
    // failures) to %LocalAppData%\FFXProjectEditor\crash.log so a GUI crash leaves a trace instead
    // of vanishing silently. Never throws (logging must not be able to crash the app).
    public static class CrashLog
    {
        public static string Path { get; } = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "crash.log");

        public static void Write(string context, Exception? ex)
        {
            try
            {
                string? dir = System.IO.Path.GetDirectoryName(Path);
                if (dir != null) Directory.CreateDirectory(dir);
                string entry =
                    $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===={Environment.NewLine}" +
                    $"{context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(Path, entry);
            }
            catch { /* logging must never crash */ }
        }
    }
}
