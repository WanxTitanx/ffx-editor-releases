using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.FfxLib.Mods;

/// <summary>Exact regional display identities; never substitutes payload or ID proof.</summary>
internal static class HookAbilityLocalizedNames
{
    private sealed record Alias(int Id, string Locale, byte[] Bytes);
    private static readonly IReadOnlyList<Alias> Aliases;

    static HookAbilityLocalizedNames()
    {
        using var stream = typeof(HookAbilityLocalizedNames).Assembly
            .GetManifestResourceStream("FFXProjectEditor.HookLocalizedNames")
            ?? throw new InvalidOperationException("Localized Hook identities missing.");
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.GetProperty("schema").GetInt32() != 1)
            throw new InvalidDataException("Unsupported Hook identity catalog.");
        Aliases = doc.RootElement.GetProperty("aliases").EnumerateArray().Select(a =>
            new Alias(a.GetProperty("id").GetInt32(), a.GetProperty("locale").GetString()!,
                Convert.FromHexString(a.GetProperty("hex").GetString()!))).ToArray();
        if (Aliases.Count != 400 || Aliases.Any(a => a.Id is < 135 or > 174
            || !Mod002Authoring.Locales.Contains(a.Locale) || a.Bytes.Length == 0 || a.Bytes.Contains((byte)0))
            || Aliases.Select(a => (a.Id, a.Locale)).Distinct().Count() != 400)
            throw new InvalidDataException("Incomplete or invalid localized Hook identities.");
    }

    public static byte[] Name(int defaultId, string locale) => Aliases
        .Single(a => a.Id == defaultId && a.Locale == locale).Bytes.ToArray();

    public static bool Matches(int defaultId, string locale, ReadOnlySpan<byte> script)
    {
        foreach (var alias in Aliases)
            if (alias.Id == defaultId && alias.Locale == locale && script.SequenceEqual(alias.Bytes)) return true;
        return false;
    }

    public static bool MatchesDisplay(int defaultId, string display)
    {
        foreach (var alias in Aliases.Where(a => a.Id == defaultId))
            if (display == TextBinary_Util.DecodeScriptToString(alias.Bytes, FfxEncoding.UsDecoder, true)
                || display == TextBinary_Util.DecodeScriptToString(alias.Bytes, FfxEncoding.JpDecoder, true)) return true;
        return false;
    }
}
