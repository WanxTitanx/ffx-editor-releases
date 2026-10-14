using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record Mod002Ability(string Key, int Id, string Name, string GearType);

internal sealed class Mod002Manifest
{
    public IReadOnlyList<Mod002Ability> Abilities { get; }
    public string Json { get; }
    private Mod002Manifest(string json, IReadOnlyList<Mod002Ability> abilities) { Json = json; Abilities = abilities; }

    public static Mod002Manifest Default()
    {
        using var stream = typeof(Mod002Manifest).Assembly.GetManifestResourceStream("FFXProjectEditor.Mod002Defaults")
            ?? throw new InvalidOperationException("Missing MOD-002 defaults.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), false);
    }

    public static Mod002Manifest Parse(string json) => Parse(json, true);

    private static Mod002Manifest Parse(string json, bool checkKeys)
    {
        if (json.Length > 64_000) throw new InvalidDataException("Oversized MOD-002 manifest.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RejectDuplicateProperties(root);
        if (root.GetProperty("schema").GetInt32() != 1) throw new InvalidDataException("Unsupported MOD-002 schema.");
        var abilities = root.GetProperty("abilities").EnumerateArray().Select(row => new Mod002Ability(
            row.GetProperty("key").GetString() ?? "", row.GetProperty("default_id").GetInt32(),
            row.GetProperty("name").GetString() ?? "", row.GetProperty("gear_type").GetString() ?? "")).ToArray();
        if (abilities.Length != 13 || abilities.Any(a => string.IsNullOrWhiteSpace(a.Key) || string.IsNullOrWhiteSpace(a.Name)
                || !((a.Id >= 135 && a.Id <= 147) || (a.Id >= 175 && a.Id <= 4095)) || a.GearType is not ("weapon" or "armor"))
            || abilities.Select(a => a.Key).Distinct(StringComparer.Ordinal).Count() != abilities.Length
            || abilities.Select(a => a.Id).Distinct().Count() != abilities.Length)
            throw new InvalidDataException("Missing, invalid or colliding MOD-002 effect keys/IDs.");
        if (checkKeys && !Default().Abilities.Select(a => (a.Key, a.GearType)).OrderBy(a => a.Key)
                .SequenceEqual(abilities.Select(a => (a.Key, a.GearType)).OrderBy(a => a.Key)))
            throw new InvalidDataException("The MOD-002 effect-key contract does not match.");
        return new Mod002Manifest(json, Array.AsReadOnly(abilities));
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate manifest property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
}
