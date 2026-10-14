using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;

namespace FFXProjectEditor.Tools;

/// <summary>Stages ordinary spells; the Tornado recipe was retired after the live duration report.</summary>
internal static class SpiraReforgeAeroPackRt0
{
    public static int Run(string input, string donorDll, string textureRoot, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Pack output already exists.");
        var grown = SpiraReforgeAeroCommandWriter.Append(File.ReadAllBytes(input));
        string donors = Directory.Exists(donorDll) ? donorDll : Path.GetDirectoryName(Path.GetFullPath(donorDll))!;
        var receipts = SpiraWindSpellVisuals.PlayerTiers.SelectMany(tier =>
            SpiraWindSpellVisuals.BuildTier(tier, donors, textureRoot, output)).ToArray();
        File.WriteAllBytes(Path.Combine(output, "command.bin"), grown.GrownBytes);
        File.WriteAllText(Path.Combine(output, "aero-pack.json"), JsonSerializer.Serialize(new
        {
            lifecycle = "ordinary-spell", nativeDonorBuild = 25501027, receipts,
            grown.OriginalEntryCount, grown.NewEntryCount, grown.Pass, runtimeAccepted = false,
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
