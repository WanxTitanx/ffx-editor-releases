using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>
/// Binds resource type 8 indices to the native registration tables. Profiles identify
/// audited code, not file names or mutable payloads. See PPP_RESOURCE_BINDINGS_20261003.md.
/// </summary>
internal static class MagicPppResourceBindings
{
    private readonly record struct Rule(uint DeclarationOperand, uint TableOperand,
        int ExpectedCount, int ResourceIndex = -1);

    // Operand RVAs refer to relocated pointers in the audited instruction streams.
    // ResourceIndex=-1 denotes the native table-pointer array indexed by resource number.
    private static readonly IReadOnlyDictionary<string, Rule[]> Profiles = new Dictionary<string, Rule[]>
    {
        ["1c1cd421bd3a922dfd419eef518f970c08993fa64b01ba9a55f9634c54e0fd2d"] =
            new[] { new Rule(0x16DD, 0x17EC, 2) }, // 0069 / 0752
        ["cf9726e1d96c59d1e7f948c476d5b9df379c61306ae047dc72af12eed75cfc70"] =
            new[] { new Rule(0x16DD, 0x17EC, 1) }, // 0122: only resource 0 is instantiated
        ["6678a2f7409d5e9bb5bc8d61c0816ae25b1d167800981b129951f11b45d5f3c2"] =
            new[] { new Rule(0x16DD, 0x17EC, 2) }, // 0208
        ["a2ee6f198aff73a05892d4e17961a4e1d10eac4d04c4a8f771a3c2b3d85e9e16"] =
            new[] { new Rule(0x137A, 0x1396, 2, 0), new Rule(0x1446, 0x1462, 2, 1) },
        ["1bd4a898a2d953a5fb231c0c2095a9908014528edd6a957cd48d22b0e9de4707"] =
            new[] { new Rule(0x3821, 0x385F, 3, 0), new Rule(0x38D8, 0x38EE, 3, 1),
                new Rule(0x396A, 0x3980, 3, 2) },
        ["9b09f4cfd28acda50f9f73b04ea31269437da079d5906abbdb49698e5f9f906d"] =
            new[] { new Rule(0x1184, 0x11F5, 1, 0), new Rule(0x13F2, 0x1451, 1, 0) },
        ["dbcecb24886ac9cb2fa4c9484cc8f75a5edaa4100769a40590175389464275b8"] =
            new[] { new Rule(0x3041, 0x307F, 3, 0), new Rule(0x30F8, 0x310E, 3, 1),
                new Rule(0x318A, 0x31A0, 3, 2) },
        ["995e9cc87e804b07449ed86bd6da5ae0810ef4e48b1ea888152c91046d34644c"] =
            new[] { new Rule(0x16DD, 0x17EC, 2) }, // 0640
    };

    internal static IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>> Resolve(
        MagicDllFile file, MagicFpHandlerTable? tables)
    {
        var result = new Dictionary<int, IReadOnlyDictionary<int, string>>();
        if (tables == null || tables.NativeTables.Count < 2 ||
            !TryReadCode(file, out var image, out string identity) ||
            !Profiles.TryGetValue(identity, out var rules)) return result;

        foreach (var rule in rules)
        {
            if (!image!.TryPointer(rule.DeclarationOperand, out uint declaration) ||
                !image.TryPointer(rule.TableOperand, out uint tablePointer)) continue;
            long declarationOffset = (long)declaration - image.ImageBase - file.DataSectionRva;
            if (declarationOffset < 0 || declarationOffset > int.MaxValue ||
                !TryReadDirectory(file.Data, (int)declarationOffset, out var roots) ||
                roots.Length != rule.ExpectedCount) continue;

            var pending = new List<(int Root, IReadOnlyDictionary<int, string> Names)>();
            for (int index = 0; index < roots.Length; index++)
            {
                if (rule.ResourceIndex >= 0 && rule.ResourceIndex != index) continue;
                uint table = tablePointer;
                if (rule.ResourceIndex < 0)
                {
                    long pointerRva = (long)tablePointer - image.ImageBase + index * 4L;
                    if (pointerRva < 0 || pointerRva > uint.MaxValue ||
                        !image.TryPointer((uint)pointerRva, out table)) break;
                }
                if (!tables.NativeTables.TryGetValue(table, out var names)) break;
                pending.Add((roots[index], names));
            }
            if (pending.Count != (rule.ResourceIndex < 0 ? roots.Length : 1)) continue;
            foreach (var binding in pending)
            {
                if (result.TryGetValue(binding.Root, out var previous))
                    result[binding.Root] = previous.Where(p => binding.Names.TryGetValue(p.Key, out var n) && n == p.Value)
                        .ToDictionary(p => p.Key, p => p.Value);
                else result.Add(binding.Root, binding.Names);
            }
        }
        return result;
    }

    internal static bool TryReadDirectory(byte[] data, int declaration, out int[] roots)
    {
        roots = Array.Empty<int>();
        if (declaration < 0 || declaration > data.Length - 96 || U32(data, declaration) != 0) return false;
        uint size = U32(data, declaration + 60);
        long header = (long)declaration + size;
        if (size < 96 || size % 16 != 0 || header > data.Length - 96) return false;
        int at = (int)header;
        if (U32(data, at) != 0 || !data.AsSpan(declaration + 64, 32).SequenceEqual(data.AsSpan(at + 64, 32)))
            return false;
        int count = BitConverter.ToUInt16(data, at + 80);
        uint relative = U32(data, at + 32);
        long directory = header + relative;
        if (count is < 1 or > 64 || relative < 96 || relative % 4 != 0 || directory > data.Length - 4L * count)
            return false;
        var offsets = new int[count];
        for (int i = 0; i < count; i++)
        {
            uint root = U32(data, (int)directory + 4 * i);
            long target = header + root;
            if (root < 96 || root % 4 != 0 || target > data.Length - 32) return false;
            offsets[i] = (int)target;
        }
        roots = offsets;
        return true;
    }

    private static bool TryReadCode(MagicDllFile file, out Image? image, out string identity)
    {
        image = null;
        identity = string.Empty;
        byte[] bytes = file.FileBytes;
        if (bytes.Length < 64) return false;
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        if (pe < 0 || pe > bytes.Length - 168) return false;
        int optional = pe + 24;
        if (BitConverter.ToUInt16(bytes, optional) != 0x10B || U32(bytes, optional + 92) <= 5) return false;
        var view = new Image(file, U32(bytes, optional + 28));
        var text = file.Sections.FirstOrDefault(s => s.Name == ".text");
        if (text == null || text.RawSize is < 1 or > 4 * 1024 * 1024 ||
            !view.TryOffset((uint)text.VirtualAddress, text.RawSize, out int code)) return false;
        uint rva = U32(bytes, optional + 136), length = U32(bytes, optional + 140);
        if (length is < 8 or > 8 * 1024 * 1024 || !view.TryOffset(rva, (int)length, out int relocations)) return false;
        int end = relocations + (int)length;
        var codeRelocations = new List<uint>();
        byte[] normalized = bytes.AsSpan(code, text.RawSize).ToArray();
        for (int block = relocations; block < end;)
        {
            if (block > end - 8) return false;
            uint page = U32(bytes, block), size = U32(bytes, block + 4);
            if (size < 8 || size % 4 != 0 || size > end - block) return false;
            int blockEnd = block + (int)size;
            for (int entry = block + 8; entry < blockEnd; entry += 2)
            {
                ushort value = BitConverter.ToUInt16(bytes, entry);
                if (value >> 12 != 3) continue;
                ulong operand = (ulong)page + (uint)(value & 0xFFF);
                if (operand > uint.MaxValue) return false;
                view.Relocations.Add((uint)operand);
                long offset = (long)operand - text.VirtualAddress;
                if (offset < 0 || offset > normalized.Length - 4) continue;
                codeRelocations.Add((uint)operand);
                Array.Clear(normalized, (int)offset, 4);
            }
            block = blockEnd;
        }
        // Bind the relocation POSITIONS too: adding relocations must not mask code edits.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)text.VirtualAddress);
            writer.Write((uint)text.RawSize);
            writer.Write(normalized);
            foreach (uint operand in codeRelocations.OrderBy(v => v)) writer.Write(operand);
        }
        identity = Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
        image = view;
        return true;
    }

    private static uint U32(byte[] bytes, int at) => BitConverter.ToUInt32(bytes, at);

    private sealed class Image(MagicDllFile file, uint imageBase)
    {
        public uint ImageBase { get; } = imageBase;
        public HashSet<uint> Relocations { get; } = new();

        public bool TryPointer(uint rva, out uint pointer)
        {
            pointer = 0;
            if (!Relocations.Contains(rva) || !TryOffset(rva, 4, out int at)) return false;
            pointer = U32(file.FileBytes, at);
            return true;
        }

        public bool TryOffset(uint rva, int width, out int offset)
        {
            offset = 0;
            foreach (var section in file.Sections)
            {
                long relative = (long)rva - section.VirtualAddress;
                long at = section.RawPtr + relative;
                if (section.RawPtr < 0 || relative < 0 || relative > (long)section.RawSize - width ||
                    at < 0 || at > (long)file.FileBytes.Length - width) continue;
                offset = (int)at;
                return true;
            }
            return false;
        }
    }
}
