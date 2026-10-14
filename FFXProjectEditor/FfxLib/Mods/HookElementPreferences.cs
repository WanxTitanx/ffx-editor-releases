using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace FFXProjectEditor.FfxLib.Mods;

internal static class F7ElementPreferencesCodec
{
    // The rest of a preset is opaque and preserved. Authoring a desired configuration
    // never reports native Apply/restore or an observed battle generation.
    public static byte[] ValidateAndRoundTrip(byte[] bytes, IReadOnlyList<ElementDescriptor> registry)
    {
        HookJson.RejectDuplicates(bytes);
        var root = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Expected F7 preset object.");
        ValidateObject(root, registry);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }
    private static void ValidateObject(JsonNode? node, IReadOnlyList<ElementDescriptor> registry)
    {
        if (node is JsonArray array) { foreach (var item in array) ValidateObject(item, registry); return; }
        if (node is not JsonObject value) return;
        foreach (string prefix in new[] { "", "diff_" })
        {
            int mask = 0;
            foreach (string category in new[] { "elemWeak", "elemResist", "elemAbsorb" })
                if (value.TryGetPropertyValue(prefix + category, out var field))
                {
                    int bits = field?.GetValue<int>() ?? throw new InvalidDataException("Null native element mask.");
                    if (bits is < 0 or > 255 || (mask & bits) != 0) throw new InvalidDataException("F7 categories must be exclusive BYTE masks.");
                    mask |= bits;
                }
            if (value.TryGetPropertyValue(prefix + "elemExtra", out var extras))
            {
                if (extras is not JsonArray entries || entries.Count > 2) throw new InvalidDataException("F7 supports at most two external elements.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in entries)
                {
                    var row = ElementalPackDocument.Object(item);
                    if (row.Count != 2 || row["affinity"] == null) throw new InvalidDataException("Invalid F7 external affinity fields.");
                    string key = ElementalPackDocument.Text(row, "key"); int affinity = row["affinity"]!.GetValue<int>();
                    if (!seen.Add(key) || affinity is < 0 or > 3 || !registry.Any(e => e.Key == key && e.NativeBit == 0))
                        throw new InvalidDataException("Invalid/unknown/duplicate F7 external element.");
                }
            }
        }
        foreach (var property in value) if (property.Value is JsonObject or JsonArray) ValidateObject(property.Value, registry);
    }
}

internal sealed class ScanElementPreferences
{
    private readonly Dictionary<string, int> values = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> Values => values;
    private static readonly Dictionary<string, int> defaults = new(StringComparer.Ordinal)
    {
        ["holy_rgb"] = 16769152, ["dark_rgb"] = 10706159, ["extra_rgb"] = 6279038, ["other_rgb"] = 7321087,
        ["extra_bit"] = 32, ["holy_enabled"] = 1, ["dark_enabled"] = 1, ["extra_enabled"] = 1, ["other_enabled"] = 0
    };
    public void Set(string key, int value, IReadOnlyList<ElementDescriptor> registry)
    {
        if (key.StartsWith("hook.", StringComparison.Ordinal))
        {
            int separator = key.LastIndexOf('.');
            if (separator <= 5) throw new InvalidDataException("Invalid keyed Scan preference.");
            string element = key[5..separator], field = key[(separator + 1)..];
            if (!registry.Any(e => e.Key == element && e.NativeBit == 0) || field is not ("rgb" or "enabled"))
                throw new InvalidDataException("Scan element is unavailable in this package.");
        }
        else if (!defaults.ContainsKey(key)) throw new InvalidDataException("Unknown Scan preference.");
        if (key == "extra_bit" ? value is not (32 or 64) : key.EndsWith("rgb", StringComparison.Ordinal) ? value is < 0 or > 0xffffff : value is < 0 or > 1)
            throw new InvalidDataException("Invalid Scan color/visibility value.");
        values[key] = value;
    }
    public (int Rgb, bool Enabled)? ForExternal(string key, IReadOnlyList<ElementDescriptor> registry)
    {
        var element = registry.SingleOrDefault(e => e.Key == key && e.NativeBit == 0);
        if (element == null) return null;
        return (values.GetValueOrDefault("hook." + key + ".rgb", element.Rgb), values.GetValueOrDefault("hook." + key + ".enabled", 1) == 1);
    }
    public string Serialize() => "[element_scan]\n" + string.Join("\n", values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + " = " + p.Value.ToString(CultureInfo.InvariantCulture))) + "\n";
    public static ScanElementPreferences Parse(string ini, IReadOnlyList<ElementDescriptor> registry)
    {
        var result = new ScanElementPreferences(); bool inSection = false;
        foreach (string raw in ini.Split('\n'))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[')) { inSection = line == "[element_scan]"; continue; }
            if (!inSection) continue;
            int equal = line.IndexOf('=');
            if (equal <= 0 || !int.TryParse(line[(equal + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value)) throw new InvalidDataException("Invalid Scan INI value.");
            string key = line[..equal].Trim(); if (result.values.ContainsKey(key)) throw new InvalidDataException("Duplicate Scan preference.");
            result.Set(key, value, registry);
        }
        return result;
    }
}

internal static class ElementalPreviewService
{
    public static int Effective(int basis, int equipment, int imperil, int ward, bool locked = false, int lockedValue = 10000)
    {
        bool Tier(int n) => n is >= -10000 and <= 25000 && n % 2500 == 0;
        if (!Tier(basis) || !Tier(lockedValue) || equipment % 2500 != 0 || imperil is < 0 or > 4 || ward is < 0 or > 4)
            throw new InvalidDataException("Invalid affinity tier/stacks.");
        if (locked) return lockedValue;
        long exposed = (long)basis + equipment + imperil * 2500L;
        long reduced = exposed - Math.Min(Math.Max(exposed, 0), ward * 2500L);
        return (int)Math.Clamp(reduced, -10000, 25000);
    }
    public static int Resolve(int damage, IReadOnlyList<(int Value, int Weight)> parts, string policy)
    {
        if (parts.Count > 32 || parts.Any(p => p.Value is < -10000 or > 25000 || p.Weight is < 1 or > 1000)) throw new InvalidDataException("Invalid affinity mixture.");
        if (policy is not ("highest_exposure" or "lowest_exposure" or "split_weighted")) throw new InvalidDataException("Use the separate native_exact mask calculation.");
        if (parts.Count == 0) return damage;
        long numerator = policy == "split_weighted" ? parts.Sum(p => (long)p.Value * p.Weight) : policy == "highest_exposure" ? parts.Max(p => p.Value) : parts.Min(p => p.Value);
        long denominator = policy == "split_weighted" ? parts.Sum(p => (long)p.Weight) * 10000 : 10000;
        return (int)Math.Clamp(damage * numerator / denominator, int.MinValue, int.MaxValue);
    }
    public static int NativeExact(int damage, byte mask, byte weak, byte resist, byte nulls, byte absorb)
    {
        if ((mask & weak) != 0) { for (int bit = 1; bit < 256; bit <<= 1) if ((mask & weak & bit) != 0) damage = unchecked(damage * 3) / 2; return damage; }
        if ((mask & ~(resist | nulls | absorb)) != 0) return damage;
        if ((mask & resist & ~(nulls | absorb)) != 0) return damage / 2;
        if ((mask & nulls & ~absorb) != 0) return 0;
        return (mask & absorb) != 0 ? unchecked(-damage) : damage;
    }
    public static int Gravity(int maximumHp, int currentHp, int divisor, bool nonlethal)
    {
        if (divisor != 16 || !nonlethal || maximumHp < 0 || currentHp < 0) throw new InvalidDataException("Only divisor 16 / nonlethal Gravity is admitted.");
        return Math.Min(maximumHp / 16, Math.Max(0, currentHp - 1));
    }
}
