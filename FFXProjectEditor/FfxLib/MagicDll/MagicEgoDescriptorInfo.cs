using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>Static Ego/Egd name headers and relocation-backed code cells.
/// The word at +16 is retained without assigning an instance-count meaning.
/// Discovery grants no authority to change callbacks, links or runtime data.</summary>
public sealed record MagicEgoDescriptorInfo(string Name, uint Rva, uint HeaderWord16,
    uint LinkRva, string? LinkedDescriptorName, IReadOnlyList<uint> CallbackRvas)
{
    public IReadOnlyList<MagicEgoCallbackKind> CallbackKinds { get; init; } = Array.Empty<MagicEgoCallbackKind>();

    public static IReadOnlyList<MagicEgoDescriptorInfo> Read(MagicDllFile file)
    {
        byte[] bytes = file.FileBytes;
        var empty = Array.Empty<MagicEgoDescriptorInfo>();
        if (bytes.Length < 64) return empty;
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        if (pe < 64 || pe > bytes.Length - 168 || BitConverter.ToUInt16(bytes, pe + 4) != 0x14C ||
            BitConverter.ToUInt16(bytes, pe + 24) != 0x10B || U32(pe + 116) <= 5)
            return empty;
        uint imageBase = U32(pe + 52);
        bool Offset(uint rva, int width, out int at, string? name = null)
        {
            at = -1;
            foreach (var section in file.Sections)
            {
                if (name != null && section.Name != name) continue;
                long relative = (long)rva - section.VirtualAddress;
                long raw = (long)section.RawPtr + relative;
                if (section.RawPtr < 0 || relative < 0 || relative > (long)section.RawSize - width ||
                    relative > (long)section.VirtualSize - width || raw < 0 || raw > bytes.Length - (long)width)
                    continue;
                at = (int)raw;
                return true;
            }
            return false;
        }
        uint U32(int at) => BitConverter.ToUInt32(bytes, at);
        uint relocRva = U32(pe + 160), relocSize = U32(pe + 164);
        if (relocSize is < 8 or > 8 * 1024 * 1024 || !Offset(relocRva, (int)relocSize, out int relocAt))
            return empty;
        var relocations = new HashSet<uint>();
        int end = relocAt + (int)relocSize;
        for (int at = relocAt; at < end;)
        {
            if (at > end - 8) return empty;
            uint page = U32(at), size = U32(at + 4);
            if (size < 8 || size % 4 != 0 || size > end - at) return empty;
            for (int entry = at + 8; entry < at + size; entry += 2)
            {
                ushort value = BitConverter.ToUInt16(bytes, entry);
                if (value >> 12 != 3) continue;
                ulong rva = (ulong)page + (uint)(value & 0xFFF);
                if (rva > uint.MaxValue) return empty;
                relocations.Add((uint)rva);
            }
            at += (int)size;
        }
        var data = file.Sections.FirstOrDefault(s => s.Name == ".data");
        if (data == null || data.RawSize is < 28 or > 64 * 1024 * 1024 ||
            !Offset((uint)data.VirtualAddress, Math.Min(data.RawSize, data.VirtualSize), out int start, ".data"))
            return empty;
        int length = Math.Min(data.RawSize, data.VirtualSize);
        var found = new List<MagicEgoDescriptorInfo>();
        for (int relative = 0; relative <= length - 28; relative += 4)
        {
            int at = start + relative;
            if (!bytes.AsSpan(at, 3).SequenceEqual("Ego"u8) && !bytes.AsSpan(at, 3).SequenceEqual("Egd"u8))
                continue;
            int count = Array.IndexOf(bytes, (byte)0, at, 16) - at;
            if (count is < 4 or > 15) continue;
            bool validName = true;
            for (int i = 3; i < count; i++)
                if (bytes[at + i] is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z')
                    and not (>= (byte)'0' and <= (byte)'9') and not (byte)'_')
                    validName = false;
            if (!validName || bytes.AsSpan(at + count, 16 - count).ContainsAnyExcept((byte)0)) continue;
            uint word = U32(at + 16), link = U32(at + 20);
            if (word is < 1 or > 8 || link < imageBase || relative > length - (24 + 4 * word)) continue;
            uint linkRva = link - imageBase;
            if (!file.Sections.Any(s => s.Name is ".data" or ".rdata" &&
                (long)linkRva >= s.VirtualAddress && (long)linkRva < (long)s.VirtualAddress + s.VirtualSize))
                continue;
            uint descriptor = (uint)(data.VirtualAddress + relative);
            if (!relocations.Contains(descriptor + 20)) continue;
            var callbacks = new uint[word];
            bool valid = true;
            for (uint index = 0; index < word; index++)
            {
                uint address = U32(at + 24 + (int)index * 4);
                if (address < imageBase || !relocations.Contains(descriptor + 24 + index * 4) ||
                    !Offset(address - imageBase, 1, out _, ".text"))
                { valid = false; break; }
                callbacks[index] = address - imageBase;
            }
            if (valid)
                found.Add(new MagicEgoDescriptorInfo(Encoding.ASCII.GetString(bytes, at, count), descriptor,
                    word, linkRva, null, Array.AsReadOnly(callbacks)));
            if (found.Count > 512) return empty;
        }
        var names = found.ToDictionary(t => t.Rva, t => t.Name);
        return MagicEgoCallbackContracts.Annotate(file, found.Select(t => t with
            { LinkedDescriptorName = names.GetValueOrDefault(t.LinkRva) }).ToArray(), relocations, imageBase);
    }
}
