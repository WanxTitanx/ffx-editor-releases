using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record ArcanaCardReference(int Id, string Key, string Name, string Description, string Asset);
internal sealed record ArcanaSelectedAsset(string Path, string Role, string Origin, string License, string Sha256, int Width, int Height);
internal sealed record ArcanaAssetSet(HookArtifact[] Artifacts, HookSourceSnapshot[] Sources);

internal static class ArcanaAssetAuthoring
{
    public static IReadOnlyList<ArcanaCardReference> Catalog { get; } = Load();
    private static IReadOnlyList<ArcanaCardReference> Load()
    {
        using var stream = typeof(ArcanaAssetAuthoring).Assembly.GetManifestResourceStream("FFXProjectEditor.HookCatalog")!;
        using var doc = JsonDocument.Parse(stream);
        var cards = doc.RootElement.GetProperty("arcana").EnumerateArray().Select(r => new ArcanaCardReference(r.GetProperty("id").GetInt32(),
            r.GetProperty("key").GetString()!, r.GetProperty("name").GetString()!, r.GetProperty("description").GetString()!, r.GetProperty("asset").GetString()!)).ToArray();
        if (cards.Length != 78 || cards.Select(c => c.Id).Distinct().Count() != 78) throw new InvalidDataException("Compiled Arcana reference is incomplete.");
        return Array.AsReadOnly(cards);
    }
    public static ArcanaAssetSet Prepare(byte[] manifest, HookProjectProfile profile)
    {
        profile.Require("arcana.compiled.v1"); HookJson.RejectDuplicates(manifest);
        var selected = JsonSerializer.Deserialize<ArcanaSelectedAsset[]>(manifest, HookProjectProfile.JsonOptions) ?? throw new InvalidDataException("Missing selected assets.");
        if (selected.Length is < 1 or > 80) throw new InvalidDataException("Select at most 78 card assets and the two shared assets.");
        var artifacts = new List<HookArtifact>(); var sources = new List<HookSourceSnapshot>(); var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in selected)
        {
            var card = Catalog.SingleOrDefault(c => c.Key == asset.Role);
            string expected = card?.Asset ?? (asset.Role == "shared.icon" ? "tarot-icon-v1.png" : asset.Role == "shared.back" ? "card-back-v1.png" : "");
            if (expected.Length == 0 || !roles.Add(asset.Role) || System.IO.Path.GetFileName(asset.Path) != expected || string.IsNullOrWhiteSpace(asset.Origin) || string.IsNullOrWhiteSpace(asset.License))
                throw new InvalidDataException("Unknown/duplicate compiled asset role, basename or missing provenance/license.");
            byte[] bytes = ModAuthoringFiles.Read(asset.Path, 16 << 20);
            if (bytes.Length < 33 || !bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) ||
                !bytes.AsSpan(12,4).SequenceEqual(new byte[]{73,72,68,82}) || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)) != asset.Width ||
                BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)) != asset.Height || asset.Width is < 1 or > 8192 || asset.Height is < 1 or > 8192 ||
                ModAuthoringFiles.Sha256(bytes) != asset.Sha256)
                throw new InvalidDataException("Asset pixels/header/dimensions/hash differ from the selected manifest.");
            string relative = "optional-assets/mods/arcana/" + (card == null ? "shared/" : "cards/") + expected;
            artifacts.Add(new(relative,bytes,"arcana-presentation",null,"arcana.compiled.v1",asset.Origin));
            sources.Add(new(System.IO.Path.GetFullPath(asset.Path),asset.Sha256,bytes.Length));
        }
        artifacts.Add(new("arcana-assets.json",manifest,"asset-provenance",null,"arcana.compiled.v1","Editor"));
        return new(artifacts.ToArray(),sources.ToArray());
    }
}
