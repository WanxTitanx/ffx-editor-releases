using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.FfxLib.MagicDll;

namespace FFXProjectEditor.Tools;

internal static class SpiraReforgeAeroTimingRt0
{
    internal static readonly int[] Frames = [45,60,90];
    private static readonly string[] OriginalHashes = [
        "bd4043500fbc2255e82fcd5eaf81307171c32ef89382eefe426d8d99a07cdb00",
        "830b482a2c28284f0fb811007505c02ee583780db87bb0dcb7c858044c291314",
        "7ac4d7a5a466cbc2ab30d2a78ec78a05442df16f1f658e514579b518d70bb0f3"
    ];

    internal static MagicPppTimingRewriter.Result Rewrite(MagicDllFile file)
    {
        int tier=file.MagicId-866;
        if (tier is <0 or >2) throw new InvalidDataException("Timing recipe is limited to the three owned Aero DLLs.");
        return MagicPppTimingRewriter.Compress(file,Frames[tier],OriginalHashes[tier]);
    }

    public static int Run(string sourceDllRoot,string output)
    {
        if (Directory.Exists(output)||File.Exists(output)) throw new IOException("Timing output already exists.");
        var parser=new MagicDllParser(new MagicDllParserOptions{FieldMap=MagicFieldMap.LoadEmbedded()});
        // Validate all three inputs before creating output.
        var results=Enumerable.Range(866,3).Select(id=> {
            var parsed=parser.Parse(Path.Combine(sourceDllRoot,$"magic_{id:D4}.dll"));
            return (Id:id,Result:Rewrite(parsed));
        }).ToArray();
        string dllRoot=Path.Combine(output,"magicFiles","FFX"); Directory.CreateDirectory(dllRoot);
        foreach(var entry in results)
        {
            string path=Path.Combine(dllRoot,$"magic_{entry.Id:D4}.dll");
            File.WriteAllBytes(path,entry.Result.Bytes);
            var reread=parser.Parse(path);
            if (reread.Roots.Count!=1 || reread.Roots[0].PrimarySections.Count!=5 ||
                !reread.Roots.SelectMany(root=>root.PrimarySections.Select(relative=>
                    (int)(BitConverter.ToUInt32(reread.Data,root.RootAbs+relative)/4096))).SequenceEqual(entry.Result.AfterFrames) ||
                !reread.Serialize().AsSpan().SequenceEqual(entry.Result.Bytes))
                throw new InvalidDataException("Timing round-trip or resource structure changed.");
            Console.WriteLine($"magic_{entry.Id:D4}: {entry.Result.BeforeFrames[0]} -> {entry.Result.AfterFrames[0]} frames; {entry.Result.Edits.Count} edits.");
        }
        File.WriteAllText(Path.Combine(output,"aero-timing.json"),JsonSerializer.Serialize(results.Select(entry=>new {
            id=entry.Id, beforeSha256=OriginalHashes[entry.Id-866],
            sha256=Convert.ToHexString(SHA256.HashData(entry.Result.Bytes)).ToLowerInvariant(),
            beforeFrames=entry.Result.BeforeFrames, afterFrames=entry.Result.AfterFrames,
            edits=entry.Result.Edits
        }),new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
}
