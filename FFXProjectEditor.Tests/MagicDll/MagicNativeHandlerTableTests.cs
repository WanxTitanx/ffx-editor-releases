using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicNativeHandlerTableTests
{
    [Fact]
    public void TableOrder_ComesFromNativeEntries_NotPhysicalStringPlacement()
    {
        string directory = Path.Combine(TestDataPaths.RepoRoot, "work",
            "magic-editor-completion-20261003", "handler-tests", Guid.NewGuid().ToString("N"));
        TestDirectory.CreatePrivate(directory);
        try
        {
            string path = MagicDllTestFixture.Write(directory, "magic_0021.dll");
            var file = MagicDllFile.Load(path);
            byte[] bytes = file.FileBytes;
            var rdata = file.Sections.Single(s => s.Name == ".rdata");
            var text = file.Sections.Single(s => s.Name == ".text");
            var names = MagicFpHandlerTable.TryExtractFromDll(file)!.Names.Values.ToArray();
            int first = Find(bytes, System.Text.Encoding.ASCII.GetBytes(names[0]+"\0"));
            int second = Find(bytes, System.Text.Encoding.ASCII.GetBytes(names[1]+"\0"));
            int at = file.DataSectionRawPtr + 0x5100;
            uint baseAddress = 0x10000000;
            void U(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
            U(BitConverter.ToInt32(bytes,0x3C)+24+28, baseAddress);
            U(at, baseAddress + (uint)rdata.VirtualAddress + (uint)(second-rdata.RawPtr));
            U(at+8, baseAddress + (uint)text.VirtualAddress);
            U(at+40, baseAddress + (uint)rdata.VirtualAddress + (uint)(first-rdata.RawPtr));
            U(at+48, baseAddress + (uint)text.VirtualAddress);
            // A zero terminator belongs to the table; unrelated later strings are not entries.
            File.WriteAllBytes(path, bytes);
            var table = MagicFpHandlerTable.TryExtractFromDll(MagicDllFile.Load(path))!;
            Assert.Equal(names[1], table.Resolve(0));
            Assert.Equal(names[0], table.Resolve(1));
            Assert.Equal(2, table.Count);
        }
        finally { Directory.Delete(directory,true); }
    }

    private static int Find(byte[] haystack, byte[] needle)
    {
        for (int i=0; i<=haystack.Length-needle.Length; i++)
            if (haystack.AsSpan(i,needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidOperationException("Fixture string missing");
    }
}
