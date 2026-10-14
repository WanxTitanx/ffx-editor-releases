using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FFXProjectEditor.FfxLib.Mods;
using Xunit;

namespace FFXProjectEditor.Tests.Core;
public sealed class HookExportTransactionTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"ffx-export-tests-"+Guid.NewGuid().ToString("N"));
    private static readonly HookProjectProfile Mod=new(){Mode=HookProjectMode.OnlyMod};
    public HookExportTransactionTests()=>Directory.CreateDirectory(root);
    public void Dispose()=>Directory.Delete(root,true);
    [Fact]
    public void ExplicitProjectModePersistsAndSwitchingBackDoesNotWriteGameSources()
    {
        string project=Path.Combine(root,"source"),settings=Path.Combine(root,"metadata");Directory.CreateDirectory(project);
        Mod.Save(project,settings,null);Assert.Equal(HookProjectMode.OnlyMod,HookProjectProfile.Load(project,settings).Mode);
        string path=HookProjectProfile.ProfilePath(project,settings),hash=HookExportTransaction.CurrentHash(path)!;
        (Mod with {Mode=HookProjectMode.Vanilla}).Save(project,settings,hash);
        Assert.Equal(HookProjectMode.Vanilla,HookProjectProfile.Load(project,settings).Mode);
        Assert.Empty(Directory.GetFileSystemEntries(project));
        Assert.Throws<IOException>(()=>Mod.Save(project,settings,hash));
    }
    [Fact]
    public void SnapshotPreviewRejectsEveryRelevantChangeAndWritesOnlyConfirmedPackage()
    {
        string sourceDir=Path.Combine(root,"source"),dest=Path.Combine(root,"package");Directory.CreateDirectory(sourceDir);
        string source=Path.Combine(sourceDir,"original.bin");File.WriteAllBytes(source,new byte[]{1,2,3});
        var snapshot=HookExportTransaction.Snapshot(source);var sources=new[]{snapshot};
        var artifacts=new[]{new HookArtifact("monster-rewards-v1.tsv",Encoding.ASCII.GetBytes("ffx.monster-rewards.v1\n1\t2\t3\n"),"reward-rates",null,"monster-rewards.v1",source)};
        var preview=HookExportTransaction.Preview(Mod,dest,sources,artifacts);
        artifacts[0].Bytes[^2]=(byte)'4';
        Assert.Throws<InvalidDataException>(()=>HookExportTransaction.Export(Mod,dest,sources,artifacts,preview.Fingerprint));
        Assert.False(Directory.Exists(dest));
        preview=HookExportTransaction.Preview(Mod,dest,sources,artifacts);
        File.AppendAllText(source,"drift");Assert.Throws<InvalidDataException>(()=>HookExportTransaction.Export(Mod,dest,sources,artifacts,preview.Fingerprint));
        File.WriteAllBytes(source,new byte[]{1,2,3});
        HookExportTransaction.Export(Mod,dest,sources,artifacts,preview.Fingerprint);
        Assert.Equal(new byte[]{1,2,3},File.ReadAllBytes(source));
        Assert.Equal(artifacts[0].Bytes,File.ReadAllBytes(Path.Combine(dest,artifacts[0].RelativePath)));
        foreach(string leaf in new[]{"checksums.sha256","source-inventory.json","output-inventory.json","hook-project.json","report.md"})Assert.True(File.Exists(Path.Combine(dest,leaf)));
        Assert.Throws<IOException>(()=>HookExportTransaction.Export(Mod,dest,sources,artifacts,preview.Fingerprint));
        Assert.Empty(Directory.EnumerateDirectories(root,".hook-export-*"));
    }
    [Theory]
    [InlineData("../escape")][InlineData("a/../b")][InlineData("/absolute")][InlineData("a\\b")][InlineData("a//b")][InlineData("a:stream")]
    public void UnsafeDestinationComponentsAreRejected(string path)=>Assert.Throws<InvalidDataException>(()=>HookExportTransaction.Resolve(root,path));
    [Fact]
    public void RollbackInventoryKeepsOriginalDataAndNeverCopiesTheExecutable()
    {
        string sourceDir=Path.Combine(root,"source"),dest=Path.Combine(root,"with-backup");Directory.CreateDirectory(sourceDir);
        string path=Path.Combine(sourceDir,"bank.bin");File.WriteAllBytes(path,new byte[]{3,4,5});var source=HookExportTransaction.Snapshot(path);
        var artifacts=new[]{new HookArtifact("data/bank.bin",new byte[]{3,9,5},"bank","us","mod007.registry.v1",path)};
        var preview=HookExportTransaction.Preview(Mod,dest,new[]{source},artifacts);HookExportTransaction.Export(Mod,dest,new[]{source},artifacts,preview.Fingerprint);
        string backup=Path.Combine(dest,"backups-of-authored-inputs",source.Sha256,"bank.bin");Assert.Equal(new byte[]{3,4,5},File.ReadAllBytes(backup));
        Assert.Contains("rollback-data",File.ReadAllText(Path.Combine(dest,"output-inventory.json")));Assert.Contains("data/bank.bin",File.ReadAllText(Path.Combine(dest,"rollback.json")));
    }
    [Fact]
    public void SourceRootPreventsExportIntoASiblingOfAnInputFile()
    {
        string source=Path.Combine(root,"source"),nested=Path.Combine(source,"nested");Directory.CreateDirectory(nested);
        string input=Path.Combine(nested,"input.bin");File.WriteAllBytes(input,new byte[]{1,2});
        var snapshot=HookExportTransaction.Snapshot(input,sourceRoot:source);
        Assert.Throws<InvalidDataException>(()=>HookExportTransaction.Preview(Mod,Path.Combine(source,"package"),new[]{snapshot},Array.Empty<HookArtifact>()));
    }
    [Fact]
    public void CancellationLeavesNoPartiallyValidPackage()
    {
        string dest=Path.Combine(root,"cancelled");var artifacts=new[]{new HookArtifact("rates.tsv",new byte[]{1},"test",null,null,"fixture")};
        var sources=Array.Empty<HookSourceSnapshot>();var preview=HookExportTransaction.Preview(Mod,dest,sources,artifacts);
        Assert.Throws<OperationCanceledException>(()=>HookExportTransaction.Export(Mod,dest,sources,artifacts,preview.Fingerprint,new CancellationToken(true)));
        Assert.False(Directory.Exists(dest));Assert.Empty(Directory.EnumerateDirectories(root,".hook-export-*"));
    }
    [LinuxSymlinkFact]
    public void LinkedOrDanglingDestinationsAreRejectedBeforeCanonicalization()
    {
        string alias=Path.Combine(root,"alias");File.CreateSymbolicLink(alias,Path.Combine(root,"not-created"));
        Assert.Throws<InvalidDataException>(()=>ModAuthoringFiles.RejectLinks(alias));
        Assert.Throws<InvalidDataException>(()=>ModAuthoringFiles.RejectLinks(alias+"/../file"));
        Assert.Throws<InvalidDataException>(()=>HookExportTransaction.Preview(Mod,alias+"/../package",Array.Empty<HookSourceSnapshot>(),Array.Empty<HookArtifact>()));
    }
}
