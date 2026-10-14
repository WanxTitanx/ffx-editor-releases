using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.Resources;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    /// <summary>
    /// Bridge Field Scout lab output into WalkManifest + MapViewer overlays (in-process refresh; PS1 publish only).
    /// </summary>
    internal static class AuroraFieldExplorer_WalkPublishService
    {
        public sealed class OperationResult
        {
            public bool Ok { get; init; }
            public string Message { get; init; } = "";
            public int RefreshedOverlays { get; init; }
            public int WalkedFields { get; init; }
        }

        public static string? FindRepoRoot()
        {
            foreach (string seed in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrWhiteSpace(seed))
                    continue;

                try
                {
                    DirectoryInfo? cursor = new(seed);
                    while (cursor != null)
                    {
                        if (Directory.Exists(Path.Combine(cursor.FullName, "RuntimeTools")) &&
                            Directory.Exists(Path.Combine(cursor.FullName, "FFXProjectEditor")))
                        {
                            return cursor.FullName;
                        }

                        cursor = cursor.Parent;
                    }
                }
                catch
                {
                    /* ignore bad paths */
                }
            }

            return null;
        }

        public static async Task<OperationResult> PublishFromLabAsync(bool copyMapExports = false)
        {
            return await Task.Run(() => PublishFromLab(copyMapExports)).ConfigureAwait(false);
        }

        static OperationResult PublishFromLab(bool copyMapExports)
        {
            string? repo = FindRepoRoot();
            if (repo == null)
            {
                return new OperationResult
                {
                    Ok = false,
                    Message = Strings.U_Au_WalkPublishRepoRootNotFound,
                };
            }

            OperationResult? ingest = TryIngestLatestSession(repo);
            if (ingest != null && !ingest.Ok)
                return ingest;

            string script = Path.Combine(repo, "RuntimeTools", "FieldScoutLab", "publish-scout-to-editor.ps1");
            if (!File.Exists(script))
            {
                return new OperationResult
                {
                    Ok = false,
                    Message = string.Format(Strings.U_Au_ScriptNotFound, script),
                };
            }

            string workRoot = Path.Combine(repo, "work", "field_scout");
            if (!Directory.Exists(workRoot) ||
                (!File.Exists(Path.Combine(workRoot, "walk-field-catalog.json")) &&
                 !File.Exists(Path.Combine(workRoot, "scout-report.json"))))
            {
                return new OperationResult
                {
                    Ok = false,
                    Message = Strings.U_Au_NoScoutData,
                };
            }

            var args = new StringBuilder();
            args.Append("-NoProfile -ExecutionPolicy Bypass -File ");
            args.Append('"').Append(script).Append('"');
            args.Append(" -SkipOverlayRefresh");
            if (copyMapExports)
                args.Append(" -CopyMapExports");

            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = args.ToString(),
                        WorkingDirectory = repo,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                if (!proc.Start())
                {
                    return new OperationResult { Ok = false, Message = Strings.U_Au_PowerShellStartFailed };
                }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    string tail = TailLines(stderr.Length > 0 ? stderr.ToString() : stdout.ToString(), 6);
                    return new OperationResult
                    {
                        Ok = false,
                        Message = string.Format(Strings.U_Au_PublishFailed, proc.ExitCode, tail),
                    };
                }

                OperationResult refresh = RefreshOverlays(null);
                string ingestNote = ingest?.Ok == true && !string.IsNullOrWhiteSpace(ingest.Message)
                    ? ingest.Message + " · "
                    : "";
                return new OperationResult
                {
                    Ok = true,
                    Message = ingestNote + string.Format(Strings.U_Au_WalkManifestPublished, refresh.RefreshedOverlays),
                    RefreshedOverlays = refresh.RefreshedOverlays,
                    WalkedFields = refresh.WalkedFields,
                };
            }
            catch (Exception ex)
            {
                return new OperationResult { Ok = false, Message = ex.Message };
            }
        }

        public static async Task<OperationResult> RefreshOverlaysAsync(BattleMapCatalog_File? btlmapCatalog = null)
        {
            return await Task.Run(() => RefreshOverlays(btlmapCatalog)).ConfigureAwait(false);
        }

        public static OperationResult RefreshOverlays(BattleMapCatalog_File? btlmapCatalog)
        {
            try
            {
                int refreshed = AuroraFieldExplorer_FieldRenderer.RefreshWalkEncountersOverlays(btlmapCatalog);
                AuroraFieldExplorer_WalkManifest.WalkBundle? bundle = AuroraFieldExplorer_WalkManifest.TryLoad();
                int walked = bundle?.FieldCount ?? 0;

                if (bundle == null)
                {
                    return new OperationResult
                    {
                        Ok = false,
                        Message = Strings.U_Au_WalkManifestNotFound,
                        RefreshedOverlays = refreshed,
                        WalkedFields = 0,
                    };
                }

                return new OperationResult
                {
                    Ok = true,
                    Message = refreshed > 0
                        ? string.Format(Strings.U_Au_ScoutMarkersUpdatedCount, refreshed, walked)
                        : walked > 0
                            ? string.Format(Strings.U_Au_BundleOkNoMounted, walked)
                            : Strings.U_Au_WalkManifestEmpty,
                    RefreshedOverlays = refreshed,
                    WalkedFields = walked,
                };
            }
            catch (Exception ex)
            {
                return new OperationResult { Ok = false, Message = ex.Message };
            }
        }

        static OperationResult? TryIngestLatestSession(string repo)
        {
            string labRoot = Path.Combine(repo, "work", "field_scout");
            string? gameModules = ReadWalkGameModules(labRoot);
            if (gameModules == null)
                return null;

            string scoutDir = Path.Combine(gameModules, "field-scout");
            if (!Directory.Exists(scoutDir))
                return null;

            string? latestSession = Directory.GetFiles(scoutDir, "session-*.jsonl")
                .Where(p => !p.Contains("-trace.jsonl", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (latestSession == null)
                return null;

            string reportPath = Path.Combine(labRoot, "scout-report.json");
            if (File.Exists(reportPath) &&
                File.GetLastWriteTimeUtc(reportPath) >= File.GetLastWriteTimeUtc(latestSession))
            {
                return null;
            }

            string ingestScript = Path.Combine(repo, "RuntimeTools", "FieldScoutLab", "process-scout-session.ps1");
            if (!File.Exists(ingestScript))
            {
                return new OperationResult
                {
                    Ok = false,
                    Message = string.Format(Strings.U_Au_SessionDetectedButIngestMissing, ingestScript),
                };
            }

            string args =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{ingestScript}\" " +
                $"-SessionPath \"{latestSession}\" " +
                $"-GameModules \"{gameModules}\" " +
                $"-OutputRoot \"{labRoot}\"";

            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = args,
                        WorkingDirectory = repo,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                var stderr = new StringBuilder();
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                if (!proc.Start())
                {
                    return new OperationResult { Ok = false, Message = Strings.U_Au_ScoutSessionIngestFailed };
                }

                proc.BeginErrorReadLine();
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    return new OperationResult
                    {
                        Ok = false,
                        Message = string.Format(Strings.U_Au_IngestFailed, proc.ExitCode, TailLines(stderr.ToString(), 4)),
                    };
                }

                return new OperationResult
                {
                    Ok = true,
                    Message = Strings.U_Au_ScoutSessionIngested,
                };
            }
            catch (Exception ex)
            {
                return new OperationResult { Ok = false, Message = ex.Message };
            }
        }

        static string? ReadWalkGameModules(string labRoot)
        {
            string configPath = Path.Combine(labRoot, "walk-config.json");
            if (!File.Exists(configPath))
                return null;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
                if (doc.RootElement.TryGetProperty("gameModules", out System.Text.Json.JsonElement gm))
                {
                    string? path = gm.GetString();
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                        return path;
                }
            }
            catch
            {
                /* ignore */
            }

            return null;
        }

        static string TailLines(string text, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length <= maxLines)
                return text.Trim();

            return string.Join(Environment.NewLine, lines[^maxLines..]);
        }
    }
}
