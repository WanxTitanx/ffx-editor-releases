using System.IO;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;

namespace FFXProjectEditor.Tools;

internal static class SpiraReforgeAerojaPackRt0
{
    public static int Run(string mainInput, string extractedInput, string donorDll, string textureRoot, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Pack output already exists.");
        var main = MonsterMagicGrowWriter.AppendAeroja(File.ReadAllBytes(mainInput));
        var extracted = MonsterMagicGrowWriter.AppendAeroja(File.ReadAllBytes(extractedInput));
        string donors = Directory.Exists(donorDll) ? donorDll : Path.GetDirectoryName(Path.GetFullPath(donorDll))!;
        var receipts = SpiraWindSpellVisuals.BuildTier(SpiraWindSpellVisuals.Aeroja, donors, textureRoot, output);
        Directory.CreateDirectory(Path.Combine(output, "main")); Directory.CreateDirectory(Path.Combine(output, "extracted"));
        File.WriteAllBytes(Path.Combine(output, "main", "monmagic2.bin"), main.GrownBytes);
        File.WriteAllBytes(Path.Combine(output, "extracted", "monmagic2.bin"), extracted.GrownBytes);
        File.WriteAllText(Path.Combine(output, "aeroja-pack.json"), JsonSerializer.Serialize(new
        {
            lifecycle = "ordinary-spell", nativeDonorBuild = 25501027, receipts,
            main.Pass, main.NewEntryCount, aiAssigned = false, runtimeAccepted = false,
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
