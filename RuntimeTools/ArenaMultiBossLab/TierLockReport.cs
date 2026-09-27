// ArenaTierLockReport — Arena+ tier-lock computation (Lane 4 of post-plan work).
//
// Reads the Arena+ catalog v2 + the progress sidecar (spira-arena-progress.json),
// computes the gating state of each row (LOCKED / READY / CLEARED), and emits a
// human report (default) or a strict JSON snapshot (--json or --out).
//
// Gating rules:
//   - CLEARED  => the row's progress_flag is present in the sidecar and flags[flag].cleared == true.
//   - LOCKED   => the row has unlock_requires[] AND at least one entry is not satisfied
//                 (a require is satisfied iff the referenced progress_flag is CLEARED).
//   - READY    => all unlock_requires[] satisfied (or none declared) AND not yet CLEARED.
//
// The output is the same shape that FfxHooksDll will eventually emit when armed via
// arena_plus_tier_lock_export.flag — keeping the schema co-located with the CLI
// means the UI (overlay or in-game) only needs to know one format.
//
// Run examples:
//   dotnet run --project RuntimeTools/ArenaMultiBossLab -- --print-tier-lock
//   dotnet run --project RuntimeTools/ArenaMultiBossLab -- --print-tier-lock --json
//   dotnet run --project RuntimeTools/ArenaMultiBossLab -- --print-tier-lock --out tier.json
//
// Exit codes: 0 OK, 4 catalog inconsistent, 5 sidecar inconsistent.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ArenaTierLockReport
{
    public static int Run(string? catalogPath, string? progressPath, string? outPath, bool jsonStdout)
    {
        string resolvedCatalog = ResolveCatalogPath(catalogPath);
        string resolvedProgress = ResolveProgressPath(progressPath);

        if (!File.Exists(resolvedCatalog))
        {
            Console.Error.WriteLine($"catalog not found: {resolvedCatalog}");
            return 4;
        }

        JsonObject? catalog = ParseJsonObject(resolvedCatalog, out string? catalogErr);
        if (catalog == null)
        {
            Console.Error.WriteLine($"catalog parse failed: {catalogErr}");
            return 4;
        }

        JsonObject? progress = File.Exists(resolvedProgress)
            ? ParseJsonObject(resolvedProgress, out string? _)
            : null;

        var clearedFlags = ExtractClearedFlags(progress);

        var rows = new List<RowReport>();
        JsonArray? tiers = catalog["tiers"] as JsonArray;
        if (tiers == null)
        {
            Console.Error.WriteLine("catalog has no tiers[].");
            return 4;
        }

        foreach (JsonNode? tierNode in tiers)
        {
            if (tierNode is not JsonObject tier) continue;
            string tierId = tier["id"]?.GetValue<string>() ?? "?";
            string tierLabel = tier["label"]?.GetValue<string>() ?? tierId;
            JsonArray? rowsArr = tier["rows"] as JsonArray;
            if (rowsArr == null) continue;
            foreach (JsonNode? rowNode in rowsArr)
            {
                if (rowNode is not JsonObject row) continue;
                RowReport rep = BuildRowReport(tierId, tierLabel, row, clearedFlags);
                rows.Add(rep);
            }
        }

        // Emit JSON either to stdout or to outPath. Otherwise human format.
        bool wantJson = jsonStdout || !string.IsNullOrEmpty(outPath);
        if (wantJson)
        {
            string payload = SerializeJson(resolvedCatalog, resolvedProgress, rows);
            if (!string.IsNullOrEmpty(outPath))
            {
                File.WriteAllText(outPath, payload);
                Console.Error.WriteLine($"tier-lock snapshot written to {outPath}");
            }
            if (jsonStdout || string.IsNullOrEmpty(outPath))
            {
                Console.WriteLine(payload);
            }
        }
        else
        {
            PrintHuman(resolvedCatalog, resolvedProgress, rows);
        }
        return 0;
    }

    private sealed class RowReport
    {
        public string TierId   = "";
        public string TierLabel = "";
        public string RowId    = "";
        public string Label    = "";
        public string ProgressFlag = "";
        public List<string> UnlockRequires = new();
        public List<string> MissingRequires = new();
        public string Rt2Status = "";
        public string Risk = "";
        public string TokenMode = "";
        public bool Cleared = false;
        public bool Locked = false;
        public string State = "READY"; // LOCKED | READY | CLEARED
    }

    private static RowReport BuildRowReport(string tierId, string tierLabel,
                                            JsonObject row, HashSet<string> clearedFlags)
    {
        var r = new RowReport
        {
            TierId       = tierId,
            TierLabel    = tierLabel,
            RowId        = row["id"]?.GetValue<string>() ?? "?",
            Label        = row["label"]?.GetValue<string>() ?? "?",
            ProgressFlag = row["progress_flag"]?.GetValue<string>() ?? "",
            Rt2Status    = row["rt2_status"]?.GetValue<string>() ?? "",
            Risk         = row["risk"]?.GetValue<string>() ?? "",
            TokenMode    = row["token_mode"]?.GetValue<string>() ?? "",
        };

        if (row["unlock_requires"] is JsonArray req)
        {
            foreach (JsonNode? n in req)
            {
                string? s = n?.GetValue<string>();
                if (!string.IsNullOrEmpty(s)) r.UnlockRequires.Add(s);
            }
        }

        r.Cleared = !string.IsNullOrEmpty(r.ProgressFlag) && clearedFlags.Contains(r.ProgressFlag);
        foreach (string need in r.UnlockRequires)
        {
            if (!clearedFlags.Contains(need)) r.MissingRequires.Add(need);
        }
        r.Locked = r.MissingRequires.Count > 0;

        r.State = r.Cleared ? "CLEARED" : (r.Locked ? "LOCKED" : "READY");
        return r;
    }

    private static void PrintHuman(string catalogPath, string progressPath, List<RowReport> rows)
    {
        Console.WriteLine($"catalog       : {catalogPath}");
        Console.WriteLine($"progress      : {progressPath} {(File.Exists(progressPath) ? "" : "(missing — treating as empty)")}");
        Console.WriteLine($"rows total    : {rows.Count}");
        int cleared = rows.Count(r => r.State == "CLEARED");
        int locked  = rows.Count(r => r.State == "LOCKED");
        int ready   = rows.Count(r => r.State == "READY");
        Console.WriteLine($"summary       : CLEARED={cleared}  READY={ready}  LOCKED={locked}");
        Console.WriteLine();

        string? lastTier = null;
        foreach (var r in rows)
        {
            if (r.TierId != lastTier)
            {
                Console.WriteLine($"── {r.TierLabel} [{r.TierId}] ──");
                lastTier = r.TierId;
            }
            Console.Write($"  [{r.State,-7}] {r.RowId,-32}  {r.Label}");
            if (r.State == "LOCKED")
                Console.Write($"  ← needs: {string.Join(", ", r.MissingRequires)}");
            Console.WriteLine();
            if (!string.IsNullOrEmpty(r.Rt2Status) && r.Rt2Status != "proved")
                Console.WriteLine($"           rt2:{r.Rt2Status}  risk:{r.Risk}  token:{r.TokenMode}");
        }
    }

    private static string SerializeJson(string catalogPath, string progressPath, List<RowReport> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"format\": \"spira-arena-tier-lock-state\",");
        sb.AppendLine("  \"format_version\": 1,");
        sb.AppendLine($"  \"generated_utc\": \"{DateTime.UtcNow:O}\",");
        sb.AppendLine($"  \"catalog_path\": {JsonString(catalogPath)},");
        sb.AppendLine($"  \"progress_path\": {JsonString(progressPath)},");
        sb.AppendLine($"  \"summary\": {{");
        sb.AppendLine($"    \"total\": {rows.Count},");
        sb.AppendLine($"    \"cleared\": {rows.Count(r => r.State == "CLEARED")},");
        sb.AppendLine($"    \"ready\":   {rows.Count(r => r.State == "READY")},");
        sb.AppendLine($"    \"locked\":  {rows.Count(r => r.State == "LOCKED")}");
        sb.AppendLine("  },");
        sb.AppendLine("  \"rows\": [");
        for (int i = 0; i < rows.Count; ++i)
        {
            RowReport r = rows[i];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"row_id\": {JsonString(r.RowId)},");
            sb.AppendLine($"      \"tier_id\": {JsonString(r.TierId)},");
            sb.AppendLine($"      \"label\": {JsonString(r.Label)},");
            sb.AppendLine($"      \"progress_flag\": {JsonString(r.ProgressFlag)},");
            sb.AppendLine($"      \"state\": {JsonString(r.State)},");
            sb.AppendLine($"      \"cleared\": {(r.Cleared ? "true" : "false")},");
            sb.AppendLine($"      \"locked\": {(r.Locked ? "true" : "false")},");
            sb.AppendLine($"      \"rt2_status\": {JsonString(r.Rt2Status)},");
            sb.AppendLine($"      \"risk\": {JsonString(r.Risk)},");
            sb.AppendLine($"      \"token_mode\": {JsonString(r.TokenMode)},");
            sb.Append($"      \"unlock_requires\": [{string.Join(", ", r.UnlockRequires.Select(JsonString))}],\n");
            sb.Append($"      \"missing_requires\": [{string.Join(", ", r.MissingRequires.Select(JsonString))}]\n");
            sb.AppendLine(i == rows.Count - 1 ? "    }" : "    },");
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string JsonString(string? s)
    {
        s ??= "";
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.AppendFormat("\\u{0:X4}", (int)c);
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static HashSet<string> ExtractClearedFlags(JsonObject? progress)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (progress == null) return set;
        JsonObject? flags = progress["flags"] as JsonObject;
        if (flags == null) return set;
        foreach (var kvp in flags)
        {
            if (kvp.Value is not JsonObject flagObj) continue;
            bool cleared = flagObj["cleared"]?.GetValue<bool>() ?? false;
            if (cleared) set.Add(kvp.Key);
        }
        return set;
    }

    private static JsonObject? ParseJsonObject(string path, out string? err)
    {
        err = null;
        try
        {
            JsonNode? n = JsonNode.Parse(File.ReadAllText(path),
                new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return n as JsonObject;
        }
        catch (Exception ex)
        {
            err = ex.Message;
            return null;
        }
    }

    private static string ResolveCatalogPath(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;
        string? repo = LocateRepoRoot();
        if (repo != null)
        {
            string candidate = Path.Combine(repo, "mods", "Spira Reforge", "arena", "spira-arena-catalog.json");
            if (File.Exists(candidate)) return candidate;
        }
        return Path.Combine(AppContext.BaseDirectory, "spira-arena-catalog.json");
    }

    private static string ResolveProgressPath(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;
        string? repo = LocateRepoRoot();
        if (repo != null)
        {
            string candidate = Path.Combine(repo, "mods", "Spira Reforge", "arena", "progress", "spira-arena-progress.json");
            if (File.Exists(candidate)) return candidate;
        }
        return Path.Combine(AppContext.BaseDirectory, "spira-arena-progress.json");
    }

    private static string? LocateRepoRoot()
    {
        string? d = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && d != null; i++)
        {
            if (File.Exists(Path.Combine(d, "PORT_STATUS.md")) &&
                Directory.Exists(Path.Combine(d, "mods")))
                return d;
            d = Path.GetDirectoryName(d);
        }
        return null;
    }
}
