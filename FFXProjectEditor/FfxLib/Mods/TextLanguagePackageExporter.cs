using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record TextLanguageRecipeEntry(string Resource, int Row, int Slot, string Text);
internal sealed record TextLanguageBuild(string Directory, string ReferenceDirectory, string ManifestHash, string RecipeHash, string Report);

internal static class TextLanguageRecipeEditor
{
    public static IReadOnlyList<TextLanguageRecipeEntry> Parse(byte[] bytes)
    {
        if (bytes.Length > 1 << 20) throw new InvalidDataException("Language recipe exceeds 1 MiB.");
        HookJson.RejectDuplicates(bytes); using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
        if (root.EnumerateObject().Select(p => p.Name).OrderBy(x => x).SequenceEqual(new[] { "display_name", "locale", "pack_version", "resources" }) == false ||
            root.GetProperty("locale").GetString() != "pt-BR" || string.IsNullOrWhiteSpace(root.GetProperty("display_name").GetString()) ||
            !Regex.IsMatch(root.GetProperty("pack_version").GetString() ?? "", @"^[0-9]{1,9}\.[0-9]{1,9}\.[0-9]{1,9}$"))
            throw new InvalidDataException("Invalid MOD-006 recipe identity/version.");
        var rows = new List<TextLanguageRecipeEntry>(); var identities = new HashSet<(string, int, int)>();
        foreach (var resource in root.GetProperty("resources").EnumerateObject())
        {
            foreach (var row in resource.Value.EnumerateArray())
            {
                if (!row.EnumerateObject().Select(p => p.Name).OrderBy(x => x).SequenceEqual(new[] { "row", "slot", "text" })) throw new InvalidDataException("Unknown translation field.");
                int index = row.GetProperty("row").GetInt32(), slot = row.GetProperty("slot").GetInt32(); string text = row.GetProperty("text").GetString() ?? "";
                if (index is < 0 or > 65535 || slot is < 0 or > 3 || text.Length > 2048 || !identities.Add((resource.Name, index, slot)))
                    throw new InvalidDataException("Duplicate/invalid translation row/slot.");
                rows.Add(new(resource.Name, index, slot, text));
            }
        }
        if (rows.Count == 0) throw new InvalidDataException("A recipe needs at least one translation.");
        return rows.AsReadOnly();
    }
}

/// <summary>Uses canonical encoding, token/choice preservation, FTC width, atlas and
/// recipient admission tools. It never edits the original VBF or changes native locale/audio.</summary>
internal static class TextLanguagePackageExporter
{
    public static async Task<TextLanguageBuild> BuildAsync(HookProjectProfile project, string python, string hooksRoot,
        string vbf, byte[] recipe, string destination, string nativeValidator, CancellationToken token)
    {
        project.Require("ffx.text-locale.api2"); _ = TextLanguageRecipeEditor.Parse(recipe);
        foreach (string path in new[] { hooksRoot, vbf, destination, nativeValidator }) ModAuthoringFiles.RejectLinks(path);
        destination = Path.GetFullPath(destination); vbf = Path.GetFullPath(vbf);
        if (Directory.Exists(destination) || File.Exists(destination) || ModAuthoringFiles.IsWithin(destination, Path.GetDirectoryName(vbf)!) ||
            ModAuthoringFiles.IsWithin(vbf, destination)) throw new InvalidDataException("Choose a new language export outside the source/installation.");
        string parent = Path.GetDirectoryName(destination)!; if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        string stage = Path.Combine(parent, ".language-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            string sourceHash = await StreamHashAsync(vbf,token), validatorHash = await StreamHashAsync(nativeValidator,token);
            string recipePath = Path.Combine(stage, "recipe.json"), pack = Path.Combine(stage, "languages", "pt-BR"), reference = Path.Combine(stage, "private-reference");
            await File.WriteAllBytesAsync(recipePath, recipe, token);
            string toolRoot = Path.Combine(hooksRoot, "tools", "text_languages");
            var tools = Directory.EnumerateFiles(toolRoot,"*.py",SearchOption.TopDirectoryOnly)
                .OrderBy(p=>p,StringComparer.Ordinal).Select(p => HookExportTransaction.Snapshot(p)).ToArray();
            var built = await HookOfflineValidator.RunAsync(python, new[] { Path.Combine(toolRoot, "pack.py"), "--vbf", vbf,
                "--edits", recipePath, "--output", pack, "--reference", reference }, token);
            if (built.ExitCode != 0) throw new InvalidDataException(built.Output);
            var admitted = await HookOfflineValidator.RunAsync(nativeValidator, new[] { pack, reference }, token);
            if (admitted.ExitCode != 0 || !admitted.Output.Contains("ADMITTED RT0", StringComparison.Ordinal)) throw new InvalidDataException(admitted.Output);
            var negative = await HookOfflineValidator.RunAsync(python, new[] { Path.Combine(toolRoot, "check_received_pack.py"), "--validator", nativeValidator,
                "--pack", pack, "--reference", reference, "--output", Path.Combine(stage, "preflight") }, token);
            if (negative.ExitCode != 0) throw new InvalidDataException(negative.Output);
            ValidateManifest(ModAuthoringFiles.Read(Path.Combine(pack, "manifest.json"), 1 << 20));
            if (sourceHash != await StreamHashAsync(vbf,token) || validatorHash != await StreamHashAsync(nativeValidator,token) || tools.Any(t => HookExportTransaction.Snapshot(t.Path) != t))
                throw new IOException("Language build inputs changed; discard preview and rebuild.");
            var inventory = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Select(p => new
                { path = Path.GetRelativePath(stage, p).Replace('\\', '/'), bytes = new FileInfo(p).Length, sha256 = StreamHash(p),
                    role = ModAuthoringFiles.IsWithin(p, reference) ? "private-reference-do-not-install" : "authored-output" }).ToArray();
            string report = "RT0 only; restart required. Native English and audio unchanged.\n" + admitted.Output + "\n" + negative.Output;
            await File.WriteAllTextAsync(Path.Combine(stage, "report.txt"), report, token);
            await File.WriteAllBytesAsync(Path.Combine(stage, "inventory.json"), JsonSerializer.SerializeToUtf8Bytes(new { source = new { vbf, sha256 = sourceHash }, validatorHash, tools, files = inventory }, HookProjectProfile.JsonOptions), token);
            string manifestHash = StreamHash(Path.Combine(pack, "manifest.json"));
            token.ThrowIfCancellationRequested(); ModAuthoringFiles.RejectLinks(destination); Directory.Move(stage, destination);
            return new(destination, Path.Combine(destination, "private-reference"), manifestHash, ModAuthoringFiles.Sha256(recipe), report);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    internal static string StreamHash(string path)
    {
        ModAuthoringFiles.RejectLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static async Task<string> StreamHashAsync(string path,CancellationToken token)
    {
        ModAuthoringFiles.RejectLinks(path);
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,131072,FileOptions.Asynchronous|FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).ToLowerInvariant();
    }
    public static void ValidateManifest(byte[] manifest)
    {
        HookJson.RejectDuplicates(manifest); using var doc = JsonDocument.Parse(manifest); var root = doc.RootElement;
        int schema = root.GetProperty("schema_version").GetInt32(), api = root.GetProperty("hook_api").GetInt32();
        if (!((schema == 1 && api == 1) || (schema == 2 && api == 2)) || root.GetProperty("capability").GetString() != "ffx.text-locale" ||
            root.GetProperty("locale").GetString() != "pt-BR" || root.GetProperty("base_locale").GetInt32() != 1 || root.GetProperty("fallback").GetString() != "native" ||
            root.GetProperty("executable_sha256").GetString() != HookCompatibilityProfile.SupportedExecutable || root.GetProperty("activation").GetString() != "restart")
            throw new InvalidDataException("Incompatible text package contract.");
        if (root.GetProperty("coverage").EnumerateObject().Any(p => p.Value.GetString() == "complete")) throw new InvalidDataException("Complete coverage requires a separate comprehensive coverage proof.");
        if (root.GetProperty("fonts").GetArrayLength() == 0) throw new InvalidDataException("A text package requires admitted fonts/atlases.");
    }
}
