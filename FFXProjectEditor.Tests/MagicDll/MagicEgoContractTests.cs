using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using FFXProjectEditor.Modules.MagicDllEditor;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicEgoContractTests
{
    private static MagicDllFile Load() => MagicDllFile.Load(Path.Combine(TestDataPaths.MagicCorpus, "magic_0387.dll"));

    private static MagicDllFile Copy(MagicDllFile source, byte[] bytes) => new("renamed.dll", bytes,
        bytes.AsSpan(source.DataSectionRawPtr, source.DataSectionSize).ToArray(),
        source.DataSectionRawPtr, source.DataSectionSize, source.DataSectionRva, source.Sections, source.Roots);

    [Fact]
    public void ExactNativeCode_DistinguishesIntegerFloatTraversalAndCleanup()
    {
        var file = Load();
        byte[] original = (byte[])file.FileBytes.Clone();
        var types = MagicEgoDescriptorInfo.Read(file);
        MagicEgoCallbackKind Kind(string name, int cell) => Assert.Single(types.Where(t => t.Name == name)).CallbackKinds[cell];
        Assert.Equal(MagicEgoCallbackKind.AddIntegerVector, Kind("EgoAddIVec", 1));
        Assert.Equal(MagicEgoCallbackKind.AddFloatVector, Kind("EgoAddVec", 1));
        Assert.Equal(MagicEgoCallbackKind.VisitChildren, Kind("EgoVec", 1));
        Assert.Equal(MagicEgoCallbackKind.VisitTask, Kind("EgoTask", 2));
        Assert.Equal(MagicEgoCallbackKind.ReleaseParticles, Kind("EgoNdPart", 0));
        Assert.Equal(original, file.FileBytes);
    }

    [Fact]
    public void ChangedCode_RemovesAnnotationsInsteadOfTrustingDescriptorNames()
    {
        var file = Load();
        byte[] bytes = (byte[])file.FileBytes.Clone();
        var text = Assert.Single(file.Sections.Where(s => s.Name == ".text"));
        bytes[text.RawPtr] ^= 1;
        var types = MagicEgoDescriptorInfo.Read(Copy(file, bytes));
        Assert.Equal(49, types.Count);
        Assert.All(types, type =>
        {
            Assert.Equal(type.CallbackRvas.Count, type.CallbackKinds.Count);
            Assert.All(type.CallbackKinds, kind => Assert.Equal(MagicEgoCallbackKind.Unknown, kind));
        });
    }

    [Fact]
    public void ChangedCallbackCell_RemovesOnlyItsNativeOperation()
    {
        var file = Load();
        byte[] bytes = (byte[])file.FileBytes.Clone();
        BitConverter.GetBytes(0x10001100u).CopyTo(bytes, file.RvaToOffset(0x110BC + 28));
        var types = MagicEgoDescriptorInfo.Read(Copy(file, bytes));
        Assert.Equal(MagicEgoCallbackKind.Unknown, Assert.Single(types.Where(t => t.Name == "EgoAddIVec")).CallbackKinds[1]);
        Assert.Equal(MagicEgoCallbackKind.AddFloatVector, Assert.Single(types.Where(t => t.Name == "EgoAddVec")).CallbackKinds[1]);
    }

    [Fact]
    public void PayloadChangeAndRenamedCopy_RetainUnchangedNativeContract()
    {
        var document = new MagicDllDocument_Wrapper();
        Assert.True(document.TryLoad(Path.Combine(TestDataPaths.MagicCorpus, "magic_0003.dll"), out var error), error);
        var file = document.ParsedFile!;
        byte[] bytes = (byte[])file.FileBytes.Clone();
        var record = file.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .First(s => s.NativeRecordWidth >= 12 && s.Keyframes.Count > 0).Keyframes[0];
        int payload = file.DataSectionRawPtr + record.RecordOffset + 8;
        Assert.InRange(payload, file.DataSectionRawPtr, file.DataSectionRawPtr + file.DataSectionSize - 1);
        bytes[payload] ^= 1;
        var types = MagicEgoDescriptorInfo.Read(Copy(file, bytes));
        Assert.Equal(MagicEgoCallbackKind.AddIntegerVector,
            Assert.Single(types.Where(t => t.Name == "EgoAddIVec")).CallbackKinds[1]);
        Assert.Equal(file.FileBytes, File.ReadAllBytes(file.SourcePath));
    }

    [Fact]
    public void ChangedPreferredImageBase_DoesNotReuseOriginalCodeMeaning()
    {
        var file = Load();
        byte[] bytes = (byte[])file.FileBytes.Clone();
        var originals = MagicEgoDescriptorInfo.Read(file);
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        BitConverter.GetBytes(0x20000000u).CopyTo(bytes, pe + 52);
        foreach (var descriptor in originals)
        {
            int at = file.RvaToOffset((int)descriptor.Rva);
            BitConverter.GetBytes(BitConverter.ToUInt32(bytes, at + 20) + 0x10000000u).CopyTo(bytes, at + 20);
            for (int cell = 0; cell < descriptor.CallbackRvas.Count; cell++)
                BitConverter.GetBytes(0x20000000u + descriptor.CallbackRvas[cell]).CopyTo(bytes, at + 24 + 4 * cell);
        }
        var types = MagicEgoDescriptorInfo.Read(Copy(file, bytes));
        Assert.Equal(49, types.Count);
        Assert.All(types.SelectMany(t => t.CallbackKinds), kind => Assert.Equal(MagicEgoCallbackKind.Unknown, kind));
    }

    [Fact]
    public void MissingCodeRelocation_RemovesContractEvenWithIdenticalCodeBytes()
    {
        var file = Load();
        byte[] bytes = (byte[])file.FileBytes.Clone();
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        int at = file.RvaToOffset((int)BitConverter.ToUInt32(bytes, pe + 160));
        int end = at + (int)BitConverter.ToUInt32(bytes, pe + 164);
        var text = Assert.Single(file.Sections.Where(s => s.Name == ".text"));
        bool removed = false;
        while (at < end && !removed)
        {
            uint page = BitConverter.ToUInt32(bytes, at);
            int size = (int)BitConverter.ToUInt32(bytes, at + 4);
            for (int slot = at + 8; slot < at + size; slot += 2)
            {
                ushort value = BitConverter.ToUInt16(bytes, slot);
                uint rva = page + (uint)(value & 0xFFF);
                if (value >> 12 != 3 || rva < text.VirtualAddress || rva >= text.VirtualAddress + text.VirtualSize) continue;
                BitConverter.GetBytes((ushort)(value & 0xFFF)).CopyTo(bytes, slot);
                removed = true;
                break;
            }
            at += size;
        }
        Assert.True(removed);
        var types = MagicEgoDescriptorInfo.Read(Copy(file, bytes));
        Assert.Equal(49, types.Count);
        Assert.All(types, type =>
        {
            Assert.Equal(type.CallbackRvas.Count, type.CallbackKinds.Count);
            Assert.All(type.CallbackKinds, kind => Assert.Equal(MagicEgoCallbackKind.Unknown, kind));
        });
    }
}
