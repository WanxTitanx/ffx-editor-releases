using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class AutoAbilityPreservingTextTests
{
    private static byte[] Bank(string name, System.Collections.Generic.Dictionary<byte, char> decoder)
    {
        const int count = 135;
        byte[] script = TextBinary_Util.EncodeScriptToBytes(name, decoder);
        var bytes = new byte[20 + count * 108 + script.Length + 2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), count - 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 108);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), count * 108);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 20);
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20 + i * 108), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20 + i * 108 + 8), 1);
            bytes[20 + i * 108 + 0x67] = 0xA5;
        }
        script.CopyTo(bytes, 20 + count * 108 + 1);
        return bytes;
    }

    [Fact]
    public void NoEditPreservingPool_IsByteIdentical()
    {
        var bytes = Bank("Original", FfxEncoding.UsDecoder);
        var table = NameDescriptionTextPrefixTable_File.Read(bytes, Customization_File.ReadHeader(bytes), FfxEncoding.UsDecoder, "test");
        Assert.Equal(bytes, table.WritePreservingPool(FfxEncoding.UsDecoder));
    }

    [Fact]
    public void EditedText_PreservesOtherEntriesAndEntireOriginalPool()
    {
        var bytes = Bank("Original", FfxEncoding.UsDecoder);
        var table = NameDescriptionTextPrefixTable_File.Read(bytes, Customization_File.ReadHeader(bytes), FfxEncoding.UsDecoder, "test");
        table.Entries[23].NameText = "Break Limits";
        var output = table.WritePreservingPool(FfxEncoding.UsDecoder);
        for (var i = 0; i < 135; i++)
            if (i != 23) Assert.Equal(bytes.AsSpan(20 + i * 108, 108).ToArray(), output.AsSpan(20 + i * 108, 108).ToArray());
        Assert.Equal(bytes.AsSpan(20 + 135 * 108).ToArray(), output.AsSpan(20 + 135 * 108, bytes.Length - 20 - 135 * 108).ToArray());
        Assert.Equal(bytes.AsSpan(20 + 23 * 108 + 2, 106).ToArray(), output.AsSpan(20 + 23 * 108 + 2, 106).ToArray());
        var readback = NameDescriptionTextPrefixTable_File.Read(output, Customization_File.ReadHeader(output), FfxEncoding.UsDecoder, "test");
        Assert.Equal("Break Limits", readback.Entries[23].NameText);
        Assert.Equal("Original", readback.Entries[122].NameText);
    }

    [Fact]
    public void JapaneseDecoder_PreservesGlyphsAndReadsEditedKana()
    {
        var bytes = Bank("リボン", FfxEncoding.JpDecoder);
        var table = NameDescriptionTextPrefixTable_File.Read(bytes, Customization_File.ReadHeader(bytes), FfxEncoding.JpDecoder, "test");
        table.Entries[134].NameText = "エレメントイーター";
        var output = table.WritePreservingPool(FfxEncoding.JpDecoder);
        var after = NameDescriptionTextPrefixTable_File.Read(output, Customization_File.ReadHeader(output), FfxEncoding.JpDecoder, "test");
        Assert.Equal("エレメントイーター", after.Entries[134].NameText);
        Assert.Equal("リボン", after.Entries[122].NameText);
    }

    [Fact]
    public void PoolOverflow_IsRefusedWithoutChangingSource()
    {
        var bytes = Bank("Original", FfxEncoding.UsDecoder);
        var immutable = (byte[])bytes.Clone();
        var table = NameDescriptionTextPrefixTable_File.Read(bytes, Customization_File.ReadHeader(bytes), FfxEncoding.UsDecoder, "test");
        table.Entries[23].NameText = new string('A', 65535);
        Assert.Throws<InvalidDataException>(() => table.WritePreservingPool(FfxEncoding.UsDecoder));
        Assert.Equal(immutable, bytes);
    }

    [Fact]
    public void NativeCjkExtension_PreservesExactScriptAndOtherRows()
    {
        var bytes = Bank("Original", FfxEncoding.UsDecoder);
        var table = NameDescriptionTextPrefixTable_File.Read(bytes, Customization_File.ReadHeader(bytes), FfxEncoding.UsDecoder, "test");
        byte[] script = [0x04, 0x2C, 0x48, 0x2D, 0xCA];
        var edits = new System.Collections.Generic.Dictionary<(int, int), byte[]> { [(23, 0)] = script };
        var output = table.WritePreservingPool(FfxEncoding.UsDecoder, edits);
        var after = NameDescriptionTextPrefixTable_File.Read(output, Customization_File.ReadHeader(output), FfxEncoding.UsDecoder, "test");
        Assert.Equal(script, after.Entries[23].NameScriptBytes);
        Assert.Equal(bytes.AsSpan(20 + 24 * 108, 108).ToArray(), output.AsSpan(20 + 24 * 108, 108).ToArray());
        Assert.Equal(bytes.AsSpan(20 + 135 * 108).ToArray(), output.AsSpan(20 + 135 * 108, bytes.Length - 20 - 135 * 108).ToArray());
        Assert.Equal(output, after.WritePreservingPool(FfxEncoding.UsDecoder));
    }

    [Fact]
    public void EncodedEdit_RejectsNonTextFieldsUnknownRowsAndEmbeddedNul()
    {
        var bytes = Bank("Original", FfxEncoding.UsDecoder);
        var table = NameDescriptionTextPrefixTable_File.Read(bytes, Customization_File.ReadHeader(bytes), FfxEncoding.UsDecoder, "test");
        foreach (var edits in new[] {
            new System.Collections.Generic.Dictionary<(int, int), byte[]> { [(23, 2)] = [0x50] },
            new System.Collections.Generic.Dictionary<(int, int), byte[]> { [(135, 0)] = [0x50] },
            new System.Collections.Generic.Dictionary<(int, int), byte[]> { [(23, 0)] = [0x50, 0, 0x51] } })
            Assert.Throws<InvalidDataException>(() => table.WritePreservingPool(FfxEncoding.UsDecoder, edits));
        Assert.Equal(bytes, table.WriteIdentity());
    }
}
