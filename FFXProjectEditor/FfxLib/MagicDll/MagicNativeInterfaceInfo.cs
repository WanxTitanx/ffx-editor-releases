using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>Static selector-zero overlay interface. Discovery grants no write or
/// registration authority; entry seven is data rather than a callback.</summary>
public sealed class MagicNativeInterfaceInfo
{
    public uint GetterRva { get; }
    public uint InitializerRva { get; }
    public uint TableRva { get; }
    public IReadOnlyList<MagicNativeInterfaceEntry> Entries { get; }

    private MagicNativeInterfaceInfo(uint getter, uint initializer, uint table,
        IReadOnlyList<MagicNativeInterfaceEntry> entries)
    {
        GetterRva = getter;
        InitializerRva = initializer;
        TableRva = table;
        Entries = entries;
    }

    internal static MagicNativeInterfaceInfo? TryRead(MagicDllFile file)
    {
        byte[] bytes = file.FileBytes;
        if (bytes.Length < 64) return null;
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        if (pe < 64 || pe > bytes.Length - 128 ||
            BitConverter.ToUInt16(bytes, pe + 4) != 0x14C ||
            BitConverter.ToUInt16(bytes, pe + 20) < 104 ||
            BitConverter.ToUInt16(bytes, pe + 24) != 0x10B || U32(pe + 116) == 0)
            return null;
        uint imageBase = U32(pe + 52);

        bool Offset(uint rva, int size, out int at, string? sectionName = null)
        {
            at = -1;
            if (size < 0) return false;
            foreach (var section in file.Sections)
            {
                if (sectionName != null && section.Name != sectionName) continue;
                long delta = (long)rva - section.VirtualAddress;
                long raw = (long)section.RawPtr + delta;
                if (section.RawPtr < 0 || delta < 0 || delta > (long)section.RawSize - size ||
                    delta > (long)section.VirtualSize - size || raw < 0 || raw > bytes.Length - (long)size)
                    continue;
                at = (int)raw;
                return true;
            }
            return false;
        }
        uint U32(int at) => BitConverter.ToUInt32(bytes, at);
        bool Named(uint rva, ReadOnlySpan<byte> name)
        {
            return Offset(rva, name.Length, out int at) && bytes.AsSpan(at, name.Length).SequenceEqual(name);
        }
        bool DataPointer(uint address, out uint rva)
        {
            rva = 0;
            if (address < imageBase) return false;
            rva = address - imageBase;
            foreach (var section in file.Sections)
                if (section.Name is ".data" or ".rdata" && (long)rva >= section.VirtualAddress &&
                    (long)rva < (long)section.VirtualAddress + section.VirtualSize) return true;
            return false;
        }

        uint exportRva = U32(pe + 120), exportSize = U32(pe + 124);
        if (exportSize < 40 || !Offset(exportRva, 40, out int export)) return null;
        uint functionCount = U32(export + 20), nameCount = U32(export + 24);
        if (functionCount is < 1 or > 4096 || nameCount is < 1 or > 4096 ||
            !Offset(U32(export + 28), (int)functionCount * 4, out int functions) ||
            !Offset(U32(export + 32), (int)nameCount * 4, out int names) ||
            !Offset(U32(export + 36), (int)nameCount * 2, out int ordinals)) return null;
        uint? getter = null, initializer = null;
        for (int i = 0; i < nameCount; ++i)
        {
            uint name = U32(names + i * 4);
            bool isGetter = Named(name, "GetEffectOverlayTable\0"u8);
            bool isInitializer = Named(name, "InitMagicPRX\0"u8);
            if (!isGetter && !isInitializer) continue;
            ushort ordinal = BitConverter.ToUInt16(bytes, ordinals + i * 2);
            if (ordinal >= functionCount) return null;
            uint target = U32(functions + ordinal * 4);
            // An export forwarder is a string, not a local executable entry.
            if (target >= exportRva && (ulong)target < (ulong)exportRva + exportSize ||
                !Offset(target, 1, out _, ".text")) return null;
            if (isGetter)
            {
                if (getter != null) return null;
                getter = target;
            }
            else
            {
                if (initializer != null) return null;
                initializer = target;
            }
        }
        if (getter == null || initializer == null || !Offset(getter.Value, 18, out int code, ".text"))
            return null;
        // push ebp; mov ebp,esp; xor ecx,ecx; cmp [ebp+8],ecx;
        // mov eax,table; cmovne eax,ecx; pop ebp; ret. Only the VA is variable.
        ReadOnlySpan<byte> head = [0x55, 0x8B, 0xEC, 0x33, 0xC9, 0x39, 0x4D, 0x08, 0xB8];
        ReadOnlySpan<byte> tail = [0x0F, 0x45, 0xC1, 0x5D, 0xC3];
        if (!bytes.AsSpan(code, head.Length).SequenceEqual(head) ||
            !bytes.AsSpan(code + 13, tail.Length).SequenceEqual(tail) ||
            !DataPointer(U32(code + 9), out uint table) || !Offset(table, 64, out int tableAt))
            return null;
        var entries = new MagicNativeInterfaceEntry[16];
        for (int i = 0; i < entries.Length; ++i)
        {
            uint pointer = U32(tableAt + i * 4);
            uint? rva = null;
            if (pointer != 0)
            {
                if (i == 7)
                {
                    if (!DataPointer(pointer, out uint dataRva)) return null;
                    rva = dataRva;
                }
                else
                {
                    if (pointer < imageBase || !Offset(pointer - imageBase, 1, out _, ".text")) return null;
                    rva = pointer - imageBase;
                }
            }
            entries[i] = new MagicNativeInterfaceEntry(i, i == 7, rva);
        }
        return new MagicNativeInterfaceInfo(getter.Value, initializer.Value, table, Array.AsReadOnly(entries));
    }
}

public sealed record MagicNativeInterfaceEntry(int Index, bool IsDataPointer, uint? Rva);
