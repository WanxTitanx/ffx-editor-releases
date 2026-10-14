using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.Ps3;

public sealed record SpiraWindTier(string Name, int CastDonor, int ImpactDonor,
    short CastId, short ImpactId, float Red, float Green, float Blue);
public sealed record SpiraSpellVisualReceipt(string Name, int Donor, int MagicId,
    string DonorSha256, string DllSha256, int Textures, bool NativeCodePreserved);

/// <summary>Ordinary cast/impact lifecycles replace Tornado's Overdrive scheduler.</summary>
public static class SpiraWindSpellVisuals
{
    public static IReadOnlyList<SpiraWindTier> PlayerTiers { get; } = Array.AsReadOnly(new[]
    {
        new SpiraWindTier("Aero", 80, 81, 866, 898, .55f, .88f, .63f),
        new SpiraWindTier("Aerora", 88, 89, 867, 899, .55f, .82f, 1f),
        new SpiraWindTier("Aeroga", 96, 97, 868, 900, .78f, .90f, 1f),
    });
    public static SpiraWindTier Aeroja { get; } = new("Aeroja", 96, 97, 869, 901, .78f, .58f, 1f);
    // Complete official donors from Steam 25501027 beside the crashed executable.
    // A new build must receive a new profile review instead of reusing old code.
    static readonly IReadOnlyDictionary<int, string> DonorHashes = new Dictionary<int, string>
    {
        [80] = "465365df5ce80d279da3c38f576f2357e4159188480ea3488fbf6d1c72a18e8d",
        [81] = "0ebc8b4a81799020690a7e405df3b15b147c8c5cf33e0b3545969d65f735cdde",
        [88] = "9093e3df1d932cadf857b912bb5b324ba8f19158adc9683453bc6de818347df2",
        [89] = "db63e5675b9187911b79380c3554f3c67f088252e7c4f8e4e9f44b250d41ae9d",
        [96] = "bb4ade17a6d2ccfafbf7587baf822646d3a547e495ab92d43f147c377d06e4a3",
        [97] = "b6e1efc257e6a234ded95328870f22769cecd576ee6c3af278f26fa95e83e253",
        [333] = "04754b4e46ed3af4a31a1c613629763daec34fe9af2ff50bb7d49d54d739da45",
        [146] = "07d73a69c446e8d801d89d99bf0eeed79706822a8c986f00d618f58b834fca65",
    };

    public static IReadOnlyList<SpiraSpellVisualReceipt> BuildTier(SpiraWindTier tier,
        string donorDllRoot, string donorAssetRoot, string stage)
    {
        if (!PlayerTiers.Contains(tier) && tier != Aeroja) throw new ArgumentException("Unknown wind tier.");
        return new[]
        {
            Clone(tier.Name + " cast", tier.CastDonor, tier.CastId, donorDllRoot, donorAssetRoot, stage,
                (tier.Red, tier.Green, tier.Blue)),
            Clone(tier.Name + " impact", tier.ImpactDonor, tier.ImpactId, donorDllRoot, donorAssetRoot, stage,
                (tier.Red, tier.Green, tier.Blue)),
        };
    }

    public static SpiraSpellVisualReceipt BuildAntiAero(string donorDllRoot, string donorAssetRoot, string stage)
        // Mortibody Water repeats its effect for each party member.
        => Clone("Anti-Aero", 333, 897, donorDllRoot, donorAssetRoot, stage, (.58f, .16f, .88f));

    public static SpiraSpellVisualReceipt BuildHoly(string donorDllRoot, string donorAssetRoot, string stage)
        => Clone("Holy actor fallback", 146, HolyActorFallbackAdapter.OwnedMagicId,
            donorDllRoot, donorAssetRoot, stage, null);

    static SpiraSpellVisualReceipt Clone(string name, int donor, int target,
        string dllRoot, string assetRoot, string stage, (float R, float G, float B)? tint)
    {
        string source = Path.Combine(dllRoot, $"magic_{donor:D4}.dll");
        byte[] sourceBytes = File.ReadAllBytes(source);
        if (!DonorHashes.TryGetValue(donor, out string? expected) || !Hash(sourceBytes).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unreviewed Steam magic donor {donor}.");
        string targetRoot = Path.Combine(stage, "magicFiles", "FFX");
        Directory.CreateDirectory(targetRoot);
        string privateDonor = Path.Combine(targetRoot, $"magic_{donor:D4}.dll");
        if (File.Exists(privateDonor)) throw new IOException("Private donor path is already occupied.");
        File.WriteAllBytes(privateDonor, sourceBytes);
        MagicEffectCloneDeployResult clone;
        try { clone = MagicEffectClonePipeline.DeployClone(donor, target, assetRoot, stage, targetRoot); }
        finally { File.Delete(privateDonor); }
        if (tint is { } color)
            SpiraNativeBossVisuals.TintPalette(clone.TargetDll, clone.DeployedPs3Folder, color.R, color.G, color.B);
        bool preserved = SpiraNativeBossVisuals.HasSameExecutableCode(source, clone.TargetDll);
        if (!preserved) throw new InvalidDataException("Cloning/tint changed native code.");
        if (target == HolyActorFallbackAdapter.OwnedMagicId)
        {
            byte[] patched = HolyActorFallbackAdapter.Apply(File.ReadAllBytes(clone.TargetDll), target);
            File.WriteAllBytes(clone.TargetDll, patched);
            if (!File.ReadAllBytes(clone.TargetDll).AsSpan().SequenceEqual(patched))
                throw new IOException("Holy adapter readback failed.");
            preserved = false;
        }
        return new(name, donor, target, Hash(sourceBytes), Hash(File.ReadAllBytes(clone.TargetDll)),
            clone.DeployedTextureCount, preserved);
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
