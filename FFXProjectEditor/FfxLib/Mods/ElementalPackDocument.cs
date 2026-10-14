using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record ElementDescriptor(string Key, string LabelKey, string Label, int Rgb, byte NativeBit);
internal sealed record ElementalInput(string Key, string Path, string Locale, string OutputRelativePath);
internal sealed record ElementalAdmission(HookAdmission Native, string Fingerprint, HookArtifact[] Artifacts, HookSourceSnapshot[] Sources);

/// <summary>Editable JSON document, admitted by the exact C++ consumer in a separate process.</summary>
internal sealed class ElementalPackDocument
{
    private readonly JsonObject root;
    public ElementalPackDocument(byte[] bytes)
    {
        if (bytes.Length > 1 << 20) throw new InvalidDataException("Elemental manifest exceeds 1 MiB.");
        HookJson.RejectDuplicates(bytes);
        root = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Expected an Elemental object.");
        if (Text(root, "schema") != "ffx.mod007.elements.v1" || Text(root, "exe_sha256") != HookCompatibilityProfile.SupportedExecutable ||
            Text(root, "fallback") != "native-unmodified") throw new InvalidDataException("Elemental schema/executable/fallback mismatch.");
        _ = Elements;
        // The actual PE32 producer at RVA 0x4AC2B0 returns one token for every
        // loaded bank. Source folders and the virtual pt-BR text locale are separate.
        var locales=Array(root,"banks").Select(Object).Select(b=>Text(b,"locale")).Distinct(StringComparer.Ordinal).ToArray();
        string[] nativeLocales={"jp","us","fr","es","de","it","kr","ch","uk"};
        if(locales.Length>1||locales.Any(locale=>!nativeLocales.Contains(locale,StringComparer.Ordinal)))
            throw new InvalidDataException("banks.locale must be the same supported native token (jp/us/fr/es/de/it/kr/ch/uk), not a source-folder or virtual text locale.");
    }
    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
    public IReadOnlyList<ElementDescriptor> Elements
    {
        get
        {
            var array = Array(root, "elements");
            if (array.Count is < 8 or > 10) throw new InvalidDataException("This Editor profile supports eight native and up to two external elements.");
            var result = new List<ElementDescriptor>();
            foreach (var node in array)
            {
                var value = Object(node); string key = Text(value, "key"), labelKey = Text(value, "label_key"), label = Text(value, "label");
                int bit = value["native_bit"]?.GetValue<int>() ?? 0, rgb = value["rgb"]!.GetValue<int>();
                if (!ValidKey(key) || !ValidKey(labelKey, 128) || label.Length is < 1 or > 64 || label.Any(c => c is < ' ' or > '~') ||
                    rgb is < 0 or > 0xffffff || bit is < 0 or > 128 || (bit & (bit - 1)) != 0)
                    throw new InvalidDataException($"Invalid element descriptor: {key}.");
                if (result.Any(e => e.Key == key || (bit != 0 && e.NativeBit == bit))) throw new InvalidDataException("Duplicate element key/native bit.");
                result.Add(new(key, labelKey, label, rgb, (byte)bit));
            }
            for (int bit = 1; bit <= 128; bit <<= 1) if (!result.Any(e => e.NativeBit == bit)) throw new InvalidDataException("All eight native bits require a descriptor.");
            return result.AsReadOnly();
        }
    }
    internal static bool ValidKey(string key, int maximum = 64) => key.Length <= maximum && Regex.IsMatch(key, "^[a-z][a-z0-9_-]*(?:\\.[a-z0-9][a-z0-9_-]*)+$", RegexOptions.CultureInvariant);
    internal static JsonObject Object(JsonNode? node) => node as JsonObject ?? throw new InvalidDataException("Expected object.");
    internal static JsonArray Array(JsonObject value, string key) => value[key] as JsonArray ?? throw new InvalidDataException($"Expected array: {key}.");
    internal static string Text(JsonObject value, string key) => value[key]?.GetValue<string>() ?? throw new InvalidDataException($"Expected string: {key}.");

    public static ElementalPackDocument Create()
    {
        var elements = new JsonArray();
        foreach (var (key, label, bit, rgb) in new[] { ("fire", "Fire", 1, 0xff6633), ("ice", "Ice", 2, 0x66ccff),
            ("thunder", "Thunder", 4, 0xffff66), ("water", "Water", 8, 0x4488ff), ("holy", "Holy", 16, 16769152),
            ("dark", "Darkness", 128, 10706159), ("custom1", "Custom 1", 32, 6279038), ("custom2", "Custom 2", 64, 7321087) })
            elements.Add(new JsonObject { ["key"] = "native." + key, ["label_key"] = "element." + key, ["label"] = label, ["rgb"] = rgb, ["native_bit"] = bit });
        return new(JsonSerializer.SerializeToUtf8Bytes(new JsonObject
        {
            ["schema"] = "ffx.mod007.elements.v1", ["package_id"] = "editor.elements", ["version"] = 1,
            ["exe_sha256"] = HookCompatibilityProfile.SupportedExecutable, ["fallback"] = "native-unmodified",
            ["requires"] = new JsonArray("mod007.registry.v1"), ["elements"] = elements,
            ["banks"] = new JsonArray(), ["commands"] = new JsonArray(), ["profiles"] = new JsonArray(), ["equipment"] = new JsonArray()
        }));
    }

    private static (int Offset, int Width) Row(JsonObject bank, int index)
    {
        var sections = Array(bank, "sections");
        foreach (var item in sections)
        {
            var section = Object(item); int first = section["first"]!.GetValue<int>(), last = section["last"]!.GetValue<int>();
            int width = section["width"]!.GetValue<int>(), offset = section["offset"]!.GetValue<int>();
            if (index >= first && index <= last) return (checked(offset + checked((index - first) * width)), width);
        }
        throw new InvalidDataException($"Row {index} is outside bank sections.");
    }

    // Fingerprints are calculated from final bytes. Paths are Editor metadata and never
    // inserted into the closed native manifest schema.
    public ElementalPackDocument RefreshFingerprints(IReadOnlyList<ElementalInput> inputs)
    {
        var copy = new ElementalPackDocument(Serialize());
        var banks = Array(copy.root, "banks").Select(Object).ToDictionary(b => Text(b, "key"), StringComparer.Ordinal);
        foreach (var bank in banks.Values)
        {
            var input = inputs.SingleOrDefault(i => i.Key == "bank:" + Text(bank, "key")) ?? throw new InvalidDataException("Missing bank source.");
            byte[] bytes = ModAuthoringFiles.Read(input.Path, Text(bank, "kind") == "autoability" ? 65535 : 8 << 20);
            if (input.Locale != Text(bank, "locale")) throw new InvalidDataException("Source runtime locale does not match the bank token.");
            bank["bytes"] = bytes.Length; bank["sha256"] = ModAuthoringFiles.Sha256(bytes);
            foreach (var binding in Array(copy.root, "commands").Concat(Array(copy.root, "equipment")).Select(Object).Where(b => Text(b, "bank") == Text(bank, "key")))
            {
                var row = Row(bank, binding["index"]!.GetValue<int>());
                if (row.Offset < 0 || row.Width <= 0 || row.Offset > bytes.Length - row.Width) throw new InvalidDataException("Binding row is outside final bank.");
                binding["row_sha256"] = ModAuthoringFiles.Sha256(bytes.AsSpan(row.Offset, row.Width).ToArray());
            }
        }
        foreach (var profile in Array(copy.root, "profiles").Select(Object).Where(p => Text(p, "kind") == "monster"))
        {
            var input = inputs.SingleOrDefault(i => i.Key == "monster:" + Text(profile, "key")) ?? throw new InvalidDataException("Missing full monster source.");
            byte[] bytes = ModAuthoringFiles.Read(input.Path, 8 << 20);
            profile["file_bytes"] = bytes.Length; profile["file_sha256"] = ModAuthoringFiles.Sha256(bytes);
        }
        return copy;
    }

    public async Task<ElementalAdmission> AdmitAsync(HookProjectProfile project, IReadOnlyList<ElementalInput> inputs, HookOfflineValidator validator, CancellationToken cancellationToken)
    {
        project.Require("mod007.registry.v1");
        foreach (var requirement in Array(root, "requires")) project.Consumer.Require(requirement!.GetValue<string>());
        var artifacts = new List<HookArtifact>(); var sources = new List<HookSourceSnapshot>(); var args = new List<string>();
        string stage = Path.Combine(Path.GetTempPath(), "ffx-elemental-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            byte[] manifest = Serialize(); string manifestPath = Path.Combine(stage, "elemental-pack.json"); await File.WriteAllBytesAsync(manifestPath, manifest, cancellationToken);
            void AddInput(string key, string? locale)
            {
                var input = inputs.SingleOrDefault(i => i.Key == key) ?? throw new InvalidDataException($"Missing source: {key}.");
                if (locale != null && input.Locale != locale) throw new InvalidDataException($"Runtime locale mismatch: {key}.");
                byte[] bytes = ModAuthoringFiles.Read(input.Path, 8 << 20);
                string path = Path.Combine(stage, "input-" + args.Count + ".bin"); File.WriteAllBytes(path, bytes);
                args.Add(path); if (locale != null) args.Add(locale);
                sources.Add(new(Path.GetFullPath(input.Path), ModAuthoringFiles.Sha256(bytes), bytes.Length));
                HookExportTransaction.Resolve(stage, input.OutputRelativePath);
                artifacts.Add(new(input.OutputRelativePath, bytes, key.StartsWith("bank:") ? "bank" : "monster", input.Locale, "mod007.registry.v1", input.Path));
            }
            foreach (var bank in Array(root, "banks").Select(Object)) AddInput("bank:" + Text(bank, "key"), Text(bank, "locale"));
            foreach (var profile in Array(root, "profiles").Select(Object).Where(p => Text(p, "kind") == "monster"))
            {
                string key = "monster:" + Text(profile, "key");
                var input = inputs.SingleOrDefault(i => i.Key == key) ?? throw new InvalidDataException("Missing monster identity proof.");
                int raw = profile["id"]!.GetValue<int>();
                if (MonsterRewardProfile.FromRawIdentity(raw) != MonsterRewardSourceResolver.FileId(input.Path))
                    throw new InvalidDataException("Monster raw identity does not match the selected complete file.");
                AddInput(key, null);
            }
            if (inputs.Count != sources.Count) throw new InvalidDataException("Unused or duplicate Elemental sources.");
            var native = await validator.ValidateAsync("elemental", manifestPath, args, cancellationToken);
            if (!native.Admitted) throw new InvalidDataException(native.Report);
            foreach (var source in sources) if (HookExportTransaction.Snapshot(source.Path) != source) throw new IOException("Source changed during native admission.");
            artifacts.Add(new("elemental-pack.json", manifest, "elemental-manifest", null, "mod007.registry.v1", "Editor"));
            string fingerprint = ModAuthoringFiles.Sha256(Encoding.UTF8.GetBytes(string.Join("\n", artifacts.Select(a => a.RelativePath + ":" + ModAuthoringFiles.Sha256(a.Bytes)))));
            return new(native, fingerprint, artifacts.ToArray(), sources.ToArray());
        }
        finally { Directory.Delete(stage, true); }
    }
}
