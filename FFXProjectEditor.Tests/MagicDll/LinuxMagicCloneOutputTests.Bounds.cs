using System;
using System.IO;
using System.Security.Cryptography;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.MagicDllEditor;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

// ── Byte-bound refusal is an operational error, not a leaking UI exception ──
// Valid PE trailing overlay bytes can exceed the writer limit while the parser still accepts it.
// MAINT: refuse before creating output; preserve loaded/source bytes and backup provenance.
public sealed partial class LinuxMagicCloneOutputTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void ByteLimit_AcceptsExactBound_AndRejectsOverflowBeforeOutput(int excess, bool accepted)
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string output = Path.Combine(fixture.Base, "bounded-output");
        byte[] bytes = new byte[LinuxOwnedOutputDirectory.MaximumBytes + excess];
        bytes[0] = 13;
        bytes[^1] = 7;
        if (accepted)
        {
            LinuxMagicCloneOutput.Publish(output, "magic_0140.dll", 140, bytes);
            string path = Assert.Single(Directory.GetFileSystemEntries(output));
            using var file = File.OpenRead(path);
            Assert.Equal(bytes.Length, file.Length);
            Assert.Equal(13, file.ReadByte());
            file.Position = file.Length - 1;
            Assert.Equal(7, file.ReadByte());
        }
        else
        {
            var error = Assert.Throws<IOException>(() =>
                LinuxMagicCloneOutput.Publish(output, "magic_0140.dll", 140, bytes));
            Assert.Contains("64 MiB", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
        }
    }

    [Fact]
    public void PublicOversizedClone_ReturnsFalseWithoutCreatingOutputOrChangingDocument()
    {
        if (!CanRunNative()) return;
        using var fixture = new Fixture();
        string source = MagicDllTestFixture.Write(fixture.Base, "magic_0021.dll");
        using (var file = new FileStream(source, FileMode.Open, FileAccess.Write))
            file.SetLength(LinuxOwnedOutputDirectory.MaximumBytes + 1L);
        string originalHash = Hash(source);
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(source, out string loadError), loadError);
        Assert.True(document.HasDocument);
        Assert.Equal(originalHash, document.ShaBefore);
        string output = Path.Combine(fixture.Base, "not-created");
        string destination = Path.Combine(output, "magic_0140.dll");
        Assert.False(document.TryCloneMagic(destination, out string error));
        Assert.Contains("64 MiB", error, StringComparison.Ordinal);
        Assert.True(error.Length < 1024);
        Assert.False(Directory.Exists(output));
        Assert.Equal(originalHash, Hash(source));
        Assert.Equal(originalHash, document.ShaBefore);
        Assert.Equal(originalHash, document.ShaAfter);
        Assert.True(document.HasDocument);
        Assert.Null(document.LastSavedPath);
        Assert.False(document.HasRestorableBackup);
    }

    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        // The public document's SHA contract is lowercase hex, unlike publication receipts.
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
}
