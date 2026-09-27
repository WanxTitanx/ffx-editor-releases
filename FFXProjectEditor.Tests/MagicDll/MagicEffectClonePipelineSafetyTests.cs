using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FFXProjectEditor.FfxLib.Magic;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Modules.Extras;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Integrity gates for the Browser's clone/deploy path. The selected roots can point at a
    /// user's live installation, so an occupied target is never an implicit overwrite target.
    /// </summary>
    public sealed class MagicEffectClonePipelineSafetyTests
    {
        [Fact]
        public void DeployClone_WhenOnlyTargetDllExists_RefusesWithoutCreatingMods()
        {
            using var fixture = new CloneFixture();
            fixture.CreateSources();
            string targetDll = fixture.CreateExistingDllTarget();
            string dllBefore = Sha256(targetDll);
            Assert.False(Directory.Exists(fixture.TargetModsFolder));

            IOException error = Assert.Throws<IOException>(() => fixture.Deploy());

            Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(dllBefore, Sha256(targetDll));
            Assert.False(Directory.Exists(fixture.TargetModsFolder));
            Assert.Empty(fixture.FindStagingArtifacts());
        }

        [Fact]
        public void DeployClone_WhenTargetModsFolderExists_RefusesWithoutCreatingTheDll()
        {
            using var fixture = new CloneFixture();
            fixture.CreateSources();
            string targetMods = fixture.CreateExistingModsTarget();
            Dictionary<string, string> modsBefore = HashTree(targetMods);

            IOException error = Assert.Throws<IOException>(() => fixture.Deploy());

            Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(fixture.TargetDll));
            Assert.Equal(modsBefore, HashTree(targetMods));
            Assert.Empty(fixture.FindStagingArtifacts());
        }

        [Fact]
        public void DeployClone_WhenTargetsAreNew_RewritesInternalMagicNameAndCommitsPayloads()
        {
            using var fixture = new CloneFixture();
            fixture.CreateSources();
            Dictionary<string, string> sourceTree = HashTree(fixture.SourcePs3Folder);
            byte[] expectedDll = MagicDllNameRewriter.RewriteMagicNameStrings(
                File.ReadAllBytes(fixture.SourceDll),
                CloneFixture.SourceId,
                CloneFixture.TargetId);

            MagicEffectCloneDeployResult result = fixture.Deploy();

            Assert.True(result.Pass);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(expectedDll)), Sha256(result.TargetDll));
            Assert.Equal(0, MagicDllNameRewriter.CountMagicNameStrings(
                File.ReadAllBytes(result.TargetDll), CloneFixture.SourceId));
            Assert.True(MagicDllNameRewriter.CountMagicNameStrings(
                File.ReadAllBytes(result.TargetDll), CloneFixture.TargetId) > 0);
            Assert.Equal(sourceTree, HashTree(result.DeployedPs3Folder));
            Assert.Empty(fixture.FindStagingArtifacts());
        }

        [Fact]
        public void DeployClone_WhenSourceHasNoInternalMagicIdentity_RefusesWithoutOutputs()
        {
            using var fixture = new CloneFixture();
            fixture.CreateSources(includeInternalMagicName: false);

            InvalidDataException error = Assert.Throws<InvalidDataException>(() => fixture.Deploy());

            Assert.Contains("magic_0082", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(fixture.TargetDll));
            Assert.False(Directory.Exists(fixture.TargetModsFolder));
            Assert.Empty(fixture.FindStagingArtifacts());
        }

        [Fact]
        public void DeployClone_WhenTheSelectedSourceUsesLegacyPadding_UsesThatExactDll()
        {
            using var fixture = new CloneFixture();
            fixture.CreateSources();
            string legacySource = fixture.RenameSourceDllToLegacyPadding();

            MagicEffectCloneDeployResult result = fixture.Deploy(legacySource);

            Assert.True(result.Pass);
            Assert.Equal(Path.GetFullPath(legacySource), result.SourceDll);
            Assert.True(File.Exists(result.TargetDll));
            Assert.Empty(fixture.FindStagingArtifacts());
        }

        [Fact]
        public void DeployClone_WhenLegacyTargetNameOwnsTheLogicalId_RefusesDuplicate()
        {
            using var fixture = new CloneFixture();
            fixture.CreateSources();
            string legacyTarget = fixture.CreateLegacyExistingDllTarget();
            string legacyHash = Sha256(legacyTarget);

            IOException error = Assert.Throws<IOException>(() => fixture.Deploy());

            Assert.Contains("magic id 716", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(legacyHash, Sha256(legacyTarget));
            Assert.False(File.Exists(fixture.TargetDll));
            Assert.False(Directory.Exists(fixture.TargetModsFolder));
            Assert.Empty(fixture.FindStagingArtifacts());
        }

        [Fact]
        public void BrowserDeploy_UsesTheSelectedDllAsSourceAndTheCloneFieldAsNewTarget()
        {
            Assert.True(MagicDllBrowser_DataModel.TryResolveCloneDeployIds(
                selectedId: 82,
                targetText: "0716",
                out int sourceId,
                out int targetId,
                out string error), error);
            Assert.Equal(82, sourceId);
            Assert.Equal(716, targetId);

            Assert.False(MagicDllBrowser_DataModel.TryResolveCloneDeployIds(
                selectedId: 716,
                targetText: "0716",
                out _,
                out _,
                out string duplicateError));
            Assert.Contains("different", duplicateError, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> HashTree(string root) =>
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                    Sha256,
                    StringComparer.OrdinalIgnoreCase);

        private static string Sha256(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        private sealed class CloneFixture : IDisposable
        {
            public const int SourceId = 82;
            public const int TargetId = 716;
            private readonly string _root = Path.Combine(
                Path.GetTempPath(),
                "ffx_magic_deploy_" + Guid.NewGuid().ToString("N"));

            public string Ps3Root => Path.Combine(_root, "ps3");
            public string GameRoot => Path.Combine(_root, "game");
            public string DllRoot => Path.Combine(_root, "magicFiles", "FFX");
            public string SourcePs3Folder => Path.Combine(Ps3Root, $"magic_{SourceId:D4}");
            public string SourceDll => Path.Combine(DllRoot, $"magic_{SourceId:D4}.dll");
            public string TargetDll => Path.Combine(DllRoot, $"magic_{TargetId:D4}.dll");
            public string TargetModsFolder => MagicEffectClonePipeline.ResolveModsPs3MagicFolder(GameRoot, TargetId);

            public CloneFixture() => Directory.CreateDirectory(_root);

            public void CreateSources(bool includeInternalMagicName = true)
            {
                Directory.CreateDirectory(Path.Combine(SourcePs3Folder, "nested"));
                File.WriteAllBytes(Path.Combine(SourcePs3Folder, "effect.dds.phyre"), new byte[] { 1, 2, 3, 4 });
                File.WriteAllBytes(Path.Combine(SourcePs3Folder, "nested", "payload.bin"), new byte[] { 5, 6, 7, 8 });
                Directory.CreateDirectory(DllRoot);
                if (includeInternalMagicName)
                {
                    File.Copy(typeof(MagicDllDecompiler).Assembly.Location, SourceDll, overwrite: false);
                    using FileStream stream = new(SourceDll, FileMode.Append, FileAccess.Write, FileShare.None);
                    byte[] identity = Encoding.Unicode.GetBytes("magic_0082.dll\0magic_0082\0");
                    stream.Write(identity);
                    stream.Flush(flushToDisk: true);
                }
                else
                {
                    File.WriteAllBytes(SourceDll, new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03 });
                }
            }

            public string CreateExistingModsTarget()
            {
                Directory.CreateDirectory(Path.Combine(TargetModsFolder, "nested"));
                File.WriteAllBytes(Path.Combine(TargetModsFolder, "nested", "payload.bin"), new byte[] { 0xAA, 0xBB });
                return TargetModsFolder;
            }

            public string CreateExistingDllTarget()
            {
                File.WriteAllBytes(TargetDll, new byte[] { 0xCC, 0xDD, 0xEE });
                return TargetDll;
            }

            public string CreateLegacyExistingDllTarget()
            {
                string legacyTarget = Path.Combine(DllRoot, $"magic_{TargetId}.dll");
                File.WriteAllBytes(legacyTarget, new byte[] { 0x4C, 0x45, 0x47, 0x41, 0x43, 0x59 });
                return legacyTarget;
            }

            public string RenameSourceDllToLegacyPadding()
            {
                string legacySource = Path.Combine(DllRoot, $"magic_{SourceId}.dll");
                File.Move(SourceDll, legacySource);
                return legacySource;
            }

            public MagicEffectCloneDeployResult Deploy(string? selectedSourceDll = null) =>
                MagicEffectClonePipeline.DeployClone(
                    SourceId,
                    TargetId,
                    Ps3Root,
                    GameRoot,
                    DllRoot,
                    selectedSourceDll: selectedSourceDll);

            public IEnumerable<string> FindStagingArtifacts() =>
                Directory.EnumerateFileSystemEntries(_root, "*.ffxms-staging-*", SearchOption.AllDirectories);

            public void Dispose()
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
        }
    }
}
