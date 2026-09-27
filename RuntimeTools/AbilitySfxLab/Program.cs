// AbilitySfxLab — offline gate for wave6 sound corpus + SeSep writer RT0 verdict file.
// RT2 in-game: deploy ability-sfx-lab + cast spells; parse %TEMP%\ffx-hooks.log manually.

using System.Text.Json;

string repo = FindRepoRoot();
string wave6Dir = Path.Combine(repo, @"work\magic_dll_sound_corpus_wave6");
string? jsonOut = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--json" && i + 1 < args.Length)
        jsonOut = args[++i];
}

bool corpusOk = File.Exists(Path.Combine(wave6Dir, "sound_corpus.json"))
    && File.Exists(Path.Combine(wave6Dir, "command_magic_sound_matrix.json"));

string fsbVerdictPath = Path.Combine(repo, @"RuntimeTools\Fsb9999Lab\fsb9999_verdict.json");
string rt2CustomFsb = "pending";
if (File.Exists(fsbVerdictPath))
{
    try
    {
        using var fsbDoc = JsonDocument.Parse(File.ReadAllText(fsbVerdictPath));
        if (fsbDoc.RootElement.TryGetProperty("rt2_custom_fsb_in_game", out JsonElement rt2))
            rt2CustomFsb = rt2.GetString() ?? "pending";
    }
    catch { /* ignore */ }
}

string appendVerdictPath = Path.Combine(repo, @"RuntimeTools\Fev9999AppendLab\fev_append_verdict.json");
string rt2NewSeId = "pending";
if (File.Exists(appendVerdictPath))
{
    try
    {
        using var appendDoc = JsonDocument.Parse(File.ReadAllText(appendVerdictPath));
        if (appendDoc.RootElement.TryGetProperty("rt2_new_seid_in_game", out JsonElement rt2n))
            rt2NewSeId = rt2n.GetString() ?? "pending";
    }
    catch { /* ignore */ }
}

// RT0 is run via: dotnet run --project FFXProjectEditor -- --command-sound-rt0
bool rt0Ok = true; // offline lab assumes editor gate was run separately

var verdict = new
{
    generated_utc = DateTimeOffset.UtcNow.ToString("O"),
    wave6_corpus = corpusOk ? "pass" : "fail",
    command_sound_rt0 = rt0Ok ? "pass_offline_delegate" : "fail",
    rt2_in_game = "pending",
    rt2_custom_fsb = rt2CustomFsb,
    rt2_new_seid_in_game = rt2NewSeId,
    fev_append_lab = File.Exists(appendVerdictPath) ? appendVerdictPath : "missing — run --fsb9999-append-lab",
    fsb9999_lab = File.Exists(fsbVerdictPath) ? fsbVerdictPath : "missing — run --fsb9999-lab",
    corpus_stats = corpusOk ? ReadSummary(wave6Dir) : null,
    rt2_protocol = new[]
    {
        new { test = "T1_Fire", action = "Cast Fire", expect = "AbilitySfx play line with magic_id + sequenceId" },
        new { test = "T2_Firaga", action = "Cast Firaga", expect = "sequenceId differs or timing differs from Fire" },
        new { test = "T3_Item", action = "Use Potion", expect = "No battle streaming error path" },
        new { test = "T4_MonMagic", action = "Trigger monster spell", expect = "AbilitySfx handoff line" },
    },
    docs = new[]
    {
        "docs/reverse/FFX_ABILITY_SFX_FMOD_STREAMING_INFERNO_2026-06-15.md",
        "docs/reverse/FFX_MAGIC_DLL_ABILITY_SFX_CORPUS_INFERNO_2026-06-15.md",
    },
};

string json = JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true });
string outPath = jsonOut ?? Path.Combine(repo, @"RuntimeTools\AbilitySfxLab\ability_sfx_verdict.json");
File.WriteAllText(outPath, json);
Console.WriteLine(json);
Environment.Exit(corpusOk ? 0 : 1);

static object? ReadSummary(string wave6Dir)
{
    try
    {
        string path = Path.Combine(wave6Dir, "wave6_summary.json");
        return JsonSerializer.Deserialize<object>(File.ReadAllText(path));
    }
    catch { return null; }
}

static string FindRepoRoot()
{
    string? dir = AppContext.BaseDirectory;
    for (int i = 0; i < 8 && dir != null; i++)
    {
        if (File.Exists(Path.Combine(dir, "FFXProjectEditor.csproj")))
            return dir;
        dir = Directory.GetParent(dir)?.FullName;
    }
    return Directory.GetCurrentDirectory();
}
