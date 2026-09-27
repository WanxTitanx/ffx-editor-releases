using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.IO;
using FFXProjectEditor.Core;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    public class PathGuardTests : IDisposable
    {
        private readonly string _tempDir;

        public PathGuardTests()
        {
            _tempDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"pathguard_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public void ValidateSourcePath_NullPath_ReturnsInvalid()
        {
            var result = FFXProjectEditor.Core.PathGuard.ValidateSourcePath(_tempDir, null!);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("null or empty"));
        }

        [Fact]
        public void ValidateSourcePath_EmptyPath_ReturnsInvalid()
        {
            var result = FFXProjectEditor.Core.PathGuard.ValidateSourcePath(_tempDir, "");
            Assert.False(result.IsValid);
        }

        [Fact]
        public void ValidateSourcePath_RelativePath_StaysInsideRoot()
        {
            var subDir = Path.Combine(_tempDir, "subdir");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "test.bin"), "data");

            var result = FFXProjectEditor.Core.PathGuard.ValidateSourcePath(_tempDir, "subdir/test.bin");
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidateSourcePath_TraversalAttack_ReturnsInvalid()
        {
            var result = FFXProjectEditor.Core.PathGuard.ValidateSourcePath(_tempDir, "../../../etc/passwd");
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("escapes source root"));
        }

        [Fact]
        public void ValidateSourcePath_WindowsTraversal_ReturnsInvalid()
        {
            var result = FFXProjectEditor.Core.PathGuard.ValidateSourcePath(_tempDir, @"..\..\Windows\System32");
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("escapes source root"));
        }

        [Fact]
        public void ValidateOutputPath_InsideSource_ReturnsInvalid()
        {
            var result = FFXProjectEditor.Core.PathGuard.ValidateOutputPath(_tempDir, Path.Combine(_tempDir, "output"));
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("inside the source workspace"));
        }

        [Fact]
        public void ValidateOutputPath_SeparateFromSource_ReturnsValid()
        {
            var outputDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"output_{Guid.NewGuid():N}");
            try
            {
                var result = FFXProjectEditor.Core.PathGuard.ValidateOutputPath(_tempDir, outputDir);
                Assert.True(result.IsValid);
                Assert.Empty(result.Errors);
            }
            finally
            {
                if (Directory.Exists(outputDir))
                    Directory.Delete(outputDir, recursive: true);
            }
        }

        [Fact]
        public void ValidateOutputPath_NullPath_ReturnsInvalid()
        {
            var result = FFXProjectEditor.Core.PathGuard.ValidateOutputPath(_tempDir, null!);
            Assert.False(result.IsValid);
        }

        [Fact]
        public void IsSystemDirectory_WindowsPath_ReturnsTrue()
        {
            Assert.True(FFXProjectEditor.Core.PathGuard.IsSystemDirectory(@"C:\Windows\System32"));
        }

        [Fact]
        public void IsSystemDirectory_NormalPath_ReturnsFalse()
        {
            Assert.False(FFXProjectEditor.Core.PathGuard.IsSystemDirectory(_tempDir));
        }

        [Fact]
        public void ValidateSourcePath_AbsolutePathInsideRoot_ReturnsValid()
        {
            var file = Path.Combine(_tempDir, "data.bin");
            File.WriteAllText(file, "test");

            var result = FFXProjectEditor.Core.PathGuard.ValidateSourcePath(_tempDir, file);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void PathValidationResult_RecordEquality_Works()
        {
            var a = new FFXProjectEditor.Core.PathValidationResult(true, Array.Empty<string>());
            var b = new FFXProjectEditor.Core.PathValidationResult(true, Array.Empty<string>());
            Assert.Equal(a, b);
        }

        // ── Native component policy regressions ──
        // Lexical policy must reject sibling prefixes and inspect ancestors, not just leaves.
        // These tests do not claim that policy strings authorize later pathname-based I/O.
        [Fact]
        public void ValidateSourcePath_PrefixSiblingIsOutsideRoot()
        {
            Assert.False(PathGuard.ValidateSourcePath(_tempDir, _tempDir + "-sibling/data.bin").IsValid);
        }

        [Fact]
        public void ValidateOutputPath_PrefixSiblingIsSeparate()
        {
            Assert.True(PathGuard.ValidateOutputPath(_tempDir, _tempDir + "-output").IsValid);
        }

        [Fact]
        public void ValidateOutputPath_EqualAndAncestorAreRejected()
        {
            Assert.False(PathGuard.ValidateOutputPath(_tempDir, _tempDir).IsValid);
            Assert.False(PathGuard.ValidateOutputPath(_tempDir, Path.GetDirectoryName(_tempDir)!).IsValid);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("relative-root")]
        [InlineData("bad\0root")]
        public void MalformedSourceRoot_ReturnsInvalidWithoutThrowing(string? source)
        {
            Assert.False(PathGuard.ValidateSourcePath(source!, "data.bin").IsValid);
            Assert.False(PathGuard.ValidateOutputPath(source!, _tempDir + "-output").IsValid);
        }

        [Fact]
        public void Containment_UsesHostCaseRules()
        {
            string root = Path.Combine(_tempDir, "CaseRoot");
            string differentCase = Path.Combine(_tempDir, "caseroot");
            Assert.Equal(OperatingSystem.IsWindows(),
                PathGuard.ValidateSourcePath(root, Path.Combine(differentCase, "file.bin")).IsValid);
            Assert.Equal(!OperatingSystem.IsWindows(),
                PathGuard.ValidateOutputPath(root, differentCase).IsValid);
        }

        [Theory]
        [InlineData("nested/../data.bin")]
        [InlineData(@"nested\..\data.bin")]
        public void ValidateSourcePath_RejectsParentComponentsBeforeNormalization(string target)
        {
            Assert.False(PathGuard.ValidateSourcePath(_tempDir, target).IsValid);
        }

        [Fact]
        public void ValidateSourcePath_ForeignSeparatorsUseExplicitHostPolicy()
        {
            Assert.Equal(OperatingSystem.IsWindows(),
                PathGuard.ValidateSourcePath(_tempDir, @"child\file.bin").IsValid);
        }

        [Fact]
        public void ValidateSourcePath_RejectsLinkedAncestorAndLeaf()
        {
            string actual = Directory.CreateDirectory(Path.Combine(_tempDir, "actual")).FullName;
            string file = Path.Combine(actual, "file.bin");
            File.WriteAllText(file, "fixture");
            string linkedDirectory = Path.Combine(_tempDir, "linked-dir");
            string linkedFile = Path.Combine(_tempDir, "linked-file.bin");
            Directory.CreateSymbolicLink(linkedDirectory, actual);
            File.CreateSymbolicLink(linkedFile, file);
            try
            {
                Assert.False(PathGuard.ValidateSourcePath(_tempDir, Path.Combine(linkedDirectory, "file.bin")).IsValid);
                Assert.False(PathGuard.ValidateSourcePath(_tempDir, linkedFile).IsValid);
            }
            finally
            {
                File.Delete(linkedFile);
                Directory.Delete(linkedDirectory);
            }
        }

        [Fact]
        public void ValidateOutputPath_RejectsLinkedParentsOnEitherSide()
        {
            string source = Directory.CreateDirectory(Path.Combine(_tempDir, "source")).FullName;
            string output = Directory.CreateDirectory(Path.Combine(_tempDir, "output")).FullName;
            string sourceLink = Path.Combine(_tempDir, "source-link");
            string outputLink = Path.Combine(_tempDir, "output-link");
            Directory.CreateSymbolicLink(sourceLink, source);
            Directory.CreateSymbolicLink(outputLink, output);
            try
            {
                Assert.False(PathGuard.ValidateOutputPath(sourceLink, output).IsValid);
                Assert.False(PathGuard.ValidateOutputPath(source, Path.Combine(outputLink, "new-child")).IsValid);
                Assert.True(PathGuard.ValidateOutputPath(source, Path.Combine(output, "new-child")).IsValid);
            }
            finally
            {
                Directory.Delete(sourceLink);
                Directory.Delete(outputLink);
            }
        }

        [Theory]
        [InlineData("/usr/lib", true)]
        [InlineData("/usr-local/data", false)]
        [InlineData("/etc-backup/data", false)]
        [InlineData("/etc/hosts", true)]
        [InlineData(@"C:\WindowsBackup", false)]
        [InlineData(@"D:\Program Files (x86)\Editor", true)]
        [InlineData(@"C:\safe\..\Windows\System32", true)]
        [InlineData("/", true)]
        [InlineData(null, true)]
        [InlineData("", true)]
        public void IsSystemDirectory_ClassifiesWholeComponentsAcrossPathDialects(string? path, bool expected)
        {
            Assert.Equal(expected, PathGuard.IsSystemDirectory(path!));
        }
    }
}
