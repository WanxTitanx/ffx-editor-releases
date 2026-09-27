// ArenaCatalogValidator — Arena+ catalog v2 + recipe consistency check.
//
// Run via:
//   dotnet run --project RuntimeTools/ArenaMultiBossLab -- --validate
//                                                          [--catalog <path>]
//                                                          [--vanilla-root <btlRoot>]
//
// Checks performed:
//   1. Catalog file exists, parses, declares format=spira-arena-catalog & format_version=2.
//   2. Per row:
//        - required fields present (id, label, token_mode, battle_token, base_template, battle_id,
//          monster_ids, raw_monster_ids, gil_cost, progress_flag, rt2_status, risk, evidence);
//        - progress_flag matches the "arena.dark.<slug>" convention;
//        - battle_token is "0xXXXXXXXX";
//        - raw_monster_ids are 8 "0xXXXX" entries with no gaps;
//        - rt2_status ∈ {proved, lab, pending, partial, blocked};
//        - token_mode ∈ {vanilla, alias, custom-hook, blocked};
//   3. Every unlock_requires flag must be declared as a progress_flag by some other row.
//   4. Every "recipe" pointer must be a file that exists in the repo.
//   5. If --vanilla-root is provided, re-runs ArenaMultiBossLab core flow on every JSON in
//      --recipe-dir as a dry-run (exit 5 if any recipe fails RT0 byte-safety).
//
// Output: human-readable per-row report + summary; exit codes 0/4/5 per Program.cs contract.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ArenaCatalogValidator
{
    private static readonly HashSet<string> AllowedTokenModes =
        new(StringComparer.OrdinalIgnoreCase) { "vanilla", "alias", "custom-hook", "blocked" };

    private static readonly HashSet<string> AllowedRt2Statuses =
        new(StringComparer.OrdinalIgnoreCase) { "proved", "lab", "pending", "partial", "blocked" };

    public static int Run(string? catalogPath, string recipeDir, string? vanillaRoot)
    {
        string resolvedCatalog = ResolveCatalogPath(catalogPath);
        Console.WriteLine($"catalog       : {resolvedCatalog}");
        if (!File.Exists(resolvedCatalog))
        {
            Console.Error.WriteLine($"catalog not found: {resolvedCatalog}");
            return 4;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(resolvedCatalog),
                new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"catalog parse failed: {ex.Message}");
            return 4;
        }
        if (root is not JsonObject obj)
        {
            Console.Error.WriteLine("catalog root must be a JSON object.");
            return 4;
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        string? format = obj["format"]?.GetValue<string>();
        int? version = (int?)obj["format_version"]?.GetValue<int>();
        if (format != "spira-arena-catalog")
            errors.Add($"format must be \"spira-arena-catalog\" (got \"{format ?? "null"}\")");
        if (version != 2)
            errors.Add($"format_version must be 2 (got {version?.ToString() ?? "null"})");

        var allProgressFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowsByProgressFlag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allRows = new List<JsonObject>();

        JsonArray? tiers = obj["tiers"] as JsonArray;
        if (tiers == null)
        {
            errors.Add("tiers[] missing");
        }
        else
        {
            foreach (JsonNode? tierNode in tiers)
            {
                if (tierNode is not JsonObject tier) continue;
                string tierId = tier["id"]?.GetValue<string>() ?? "?";
                JsonArray? rows = tier["rows"] as JsonArray;
                if (rows == null) { warnings.Add($"tier {tierId}: rows[] missing"); continue; }
                foreach (JsonNode? rowNode in rows)
                {
                    if (rowNode is not JsonObject row) continue;
                    allRows.Add(row);
                    string rowId = row["id"]?.GetValue<string>() ?? "?";
                    string? flag = row["progress_flag"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(flag))
                    {
                        if (!rowsByProgressFlag.TryAdd(flag, $"{tierId}/{rowId}"))
                        {
                            warnings.Add($"progress_flag '{flag}' appears in both '{rowsByProgressFlag[flag]}' and '{tierId}/{rowId}'");
                        }
                        allProgressFlags.Add(flag);
                    }
                }
            }
        }

        Console.WriteLine($"rows          : {allRows.Count}");
        Console.WriteLine($"distinct flags: {allProgressFlags.Count}");
        Console.WriteLine();

        // Per-row validation
        foreach (JsonObject row in allRows)
        {
            string rowId = row["id"]?.GetValue<string>() ?? "?";

            void Require(string field)
            {
                if (row[field] is null) errors.Add($"row '{rowId}': missing required field '{field}'");
            }
            Require("id");
            Require("label");
            Require("token_mode");
            Require("battle_token");
            Require("base_template");
            Require("battle_id");
            Require("monster_ids");
            Require("raw_monster_ids");
            Require("gil_cost");
            Require("progress_flag");
            Require("rt2_status");
            Require("risk");
            Require("evidence");

            string? tokenMode = row["token_mode"]?.GetValue<string>();
            if (tokenMode != null && !AllowedTokenModes.Contains(tokenMode))
                errors.Add($"row '{rowId}': token_mode '{tokenMode}' not in {{vanilla,alias,custom-hook,blocked}}");

            string? rt2 = row["rt2_status"]?.GetValue<string>();
            if (rt2 != null && !AllowedRt2Statuses.Contains(rt2))
                errors.Add($"row '{rowId}': rt2_status '{rt2}' not in {{proved,lab,pending,partial,blocked}}");

            string? token = row["battle_token"]?.GetValue<string>();
            if (token != null && !LooksLikeHex32(token))
                errors.Add($"row '{rowId}': battle_token '{token}' must be 0xXXXXXXXX (8 hex digits)");

            string? flag = row["progress_flag"]?.GetValue<string>();
            if (flag != null && !flag.StartsWith("arena.dark.", StringComparison.Ordinal))
                warnings.Add($"row '{rowId}': progress_flag '{flag}' does not follow 'arena.dark.<slug>' convention");

            JsonArray? rawIds = row["raw_monster_ids"] as JsonArray;
            if (rawIds != null)
            {
                if (rawIds.Count != 8)
                {
                    errors.Add($"row '{rowId}': raw_monster_ids must have exactly 8 entries (got {rawIds.Count})");
                }
                else
                {
                    int firstEmpty = -1;
                    for (int i = 0; i < rawIds.Count; i++)
                    {
                        string val = rawIds[i]?.GetValue<string>() ?? "";
                        if (!LooksLikeHex16(val))
                        {
                            errors.Add($"row '{rowId}': raw_monster_ids[{i}] '{val}' must be 0xXXXX (4 hex digits)");
                            continue;
                        }
                        if (string.Equals(val, "0xFFFF", StringComparison.OrdinalIgnoreCase) && firstEmpty < 0)
                            firstEmpty = i;
                        else if (firstEmpty >= 0 && !string.Equals(val, "0xFFFF", StringComparison.OrdinalIgnoreCase))
                            errors.Add($"row '{rowId}': raw_monster_ids has gap (slot {firstEmpty}=FFFF, slot {i}={val})");
                    }
                }
            }

            JsonArray? unlockArr = row["unlock_requires"] as JsonArray;
            if (unlockArr != null)
            {
                foreach (JsonNode? n in unlockArr)
                {
                    string? req = n?.GetValue<string>();
                    if (string.IsNullOrEmpty(req)) continue;
                    if (!allProgressFlags.Contains(req))
                        errors.Add($"row '{rowId}': unlock_requires '{req}' does not match any progress_flag in the catalog");
                }
            }

            string? recipeRel = row["recipe"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(recipeRel))
            {
                string recipeAbs = Path.IsPathRooted(recipeRel)
                    ? recipeRel
                    : Path.Combine(LocateRepoRoot() ?? ".", recipeRel);
                if (!File.Exists(recipeAbs))
                    errors.Add($"row '{rowId}': recipe '{recipeRel}' points to missing file ({recipeAbs})");
            }
        }

        // Cross-tier check: every tier-level unlock.requires also resolves
        if (tiers != null)
        {
            foreach (JsonNode? tNode in tiers)
            {
                if (tNode is not JsonObject t) continue;
                string tierId = t["id"]?.GetValue<string>() ?? "?";
                JsonObject? unlock = t["unlock"] as JsonObject;
                JsonArray? requires = unlock?["requires"] as JsonArray;
                if (requires == null) continue;
                foreach (JsonNode? n in requires)
                {
                    string? req = n?.GetValue<string>();
                    if (string.IsNullOrEmpty(req)) continue;
                    if (!allProgressFlags.Contains(req))
                        errors.Add($"tier '{tierId}': unlock.requires '{req}' does not match any progress_flag in the catalog");
                }
            }
        }

        Console.WriteLine("=== catalog ===");
        foreach (string w in warnings) Console.WriteLine($"  warn: {w}");
        foreach (string e in errors)   Console.WriteLine($"  ERR : {e}");
        Console.WriteLine($"catalog summary: {errors.Count} error(s), {warnings.Count} warning(s)");
        Console.WriteLine();

        int finalCode = errors.Count > 0 ? 4 : 0;

        // Optional recipe sweep
        if (!string.IsNullOrEmpty(vanillaRoot))
        {
            Console.WriteLine("=== recipe sweep (dry-run) ===");
            if (!Directory.Exists(recipeDir))
            {
                Console.Error.WriteLine($"recipe dir not found: {recipeDir}");
                if (finalCode == 0) finalCode = 5;
            }
            else
            {
                int total = 0, ok = 0, fail = 0;
                foreach (string r in Directory.EnumerateFiles(recipeDir, "*.json")
                                              .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    ++total;
                    string name = Path.GetFileNameWithoutExtension(r);
                    int exit = RunRecipeDryRun(name, recipeDir, vanillaRoot);
                    if (exit == 0) { ++ok; Console.WriteLine($"  PASS  {name}"); }
                    else           { ++fail; Console.WriteLine($"  FAIL ({exit}) {name}"); }
                }
                Console.WriteLine($"recipes sweep: {ok}/{total} PASS, {fail} FAIL");
                if (fail > 0 && finalCode == 0) finalCode = 5;
            }
        }
        else
        {
            Console.WriteLine("(recipe sweep skipped — pass --vanilla-root <btlRoot> to enable)");
        }

        Console.WriteLine();
        Console.WriteLine(finalCode == 0
            ? "OK: catalog + recipes coherent."
            : $"FAIL: validator exit code {finalCode}");
        return finalCode;
    }

    private static int RunRecipeDryRun(string recipeName, string recipeDir, string vanillaRoot)
    {
        // Re-invoke this same CLI process with the recipe arguments and capture exit code.
        // Avoids reimplementing the whole pipeline; the child uses --dry-run so nothing is written.
        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "dotnet";
        bool isDotnet = exePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                        exePath.EndsWith("dotnet",      StringComparison.OrdinalIgnoreCase);

        ProcessStartInfo psi;
        if (isDotnet)
        {
            string? assembly = typeof(ArenaCatalogValidator).Assembly.Location;
            psi = new ProcessStartInfo(exePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(assembly ?? "ArenaMultiBossLab.dll");
        }
        else
        {
            psi = new ProcessStartInfo(exePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
        }
        psi.ArgumentList.Add("--recipe");        psi.ArgumentList.Add(recipeName);
        psi.ArgumentList.Add("--recipe-dir");    psi.ArgumentList.Add(recipeDir);
        psi.ArgumentList.Add("--vanilla-root");  psi.ArgumentList.Add(vanillaRoot);
        psi.ArgumentList.Add("--dry-run");

        using var p = Process.Start(psi);
        if (p == null) return -1;
        p.WaitForExit(30_000);
        return p.HasExited ? p.ExitCode : -1;
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

    private static string? LocateRepoRoot()
    {
        // Walk up from BaseDirectory looking for the repo sentinel.
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

    private static bool LooksLikeHex16(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Length != 6) return false;
        for (int i = 2; i < 6; i++)
        {
            char c = s[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
        }
        return true;
    }

    private static bool LooksLikeHex32(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Length != 10) return false;
        for (int i = 2; i < 10; i++)
        {
            char c = s[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
        }
        return true;
    }
}
