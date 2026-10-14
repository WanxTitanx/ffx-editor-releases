using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.Ps3;

/// <summary>Opt-in, offline adaptation of the reviewed owned monster throw effects.</summary>
public static class SinMonsterThrowVisualAdapter
{
    public static bool SupportsOwnedMagicId(int id) => id is 790 or 791 or 802 or 803 or 807 or 815 or 829 or 837 or 845 or 852 or 864;
    // These clones inherited a seven-character timing/bone lookup from Use/Mix.
    // Enemy actor slots cannot index those tables. Keep their particles, textures,
    // target selection and completion code; start the throw at the caster origin.
    public static byte[] Adapt(byte[] source, int ownedMagicId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!SupportsOwnedMagicId(ownedMagicId))
            throw new ArgumentOutOfRangeException(nameof(ownedMagicId), "Only reviewed owned SIN effects can be adapted.");
        using var stream = new MemoryStream(source, writable: false);
        using var pe = new PEReader(stream);
        var headers = pe.PEHeaders;
        var optional = headers.PEHeader ?? throw new InvalidDataException("Missing PE header.");
        if (headers.CoffHeader.Machine != Machine.I386 || optional.Magic != PEMagic.PE32 ||
            optional.ImageBase != 0x10000000 || optional.SizeOfImage != 0x1D9000)
            throw new InvalidDataException("Unsupported visual image profile.");
        var code = headers.SectionHeaders.Single(s => s.Name == ".text");
        string hash = Convert.ToHexString(SHA256.HashData(source.AsSpan(code.PointerToRawData, code.SizeOfRawData)));
        if (hash is not ("6DD922796070A2E546B53B5B1011E035E1D32D7F270223A57F1D46DA289DAEFD" or
                         "F1C752A00E0B14BEC3A74E819451C4DF3B63158FB0FC32AB78EF9CC4FE9178B8"))
            throw new InvalidDataException("Visual code differs from the reviewed legacy/Steam throw family.");
        int Offset(int rva, int length)
        {
            foreach (var section in headers.SectionHeaders)
            {
                long delta = (long)rva - section.VirtualAddress;
                if (delta < 0 || delta + length > section.SizeOfRawData) continue;
                long offset = section.PointerToRawData + delta;
                if (offset < 0 || offset + length > source.Length) break;
                return checked((int)offset);
            }
            throw new InvalidDataException("Visual patch is outside backed section bytes.");
        }
        byte[] timerBefore = Convert.FromHexString("8B1C8534500010");
        byte[] originBefore = Convert.FromHexString("A198041D106880041D106A00FF3485FC540010A1F0000910FF3570511D108B80B4030000FFD0");
        int timer = Offset(0x1397, 7), origin = Offset(0x18CA, 38);
        if (!source.AsSpan(timer, 7).SequenceEqual(timerBefore) ||
            !source.AsSpan(origin, 38).SequenceEqual(originBefore))
            throw new InvalidDataException("Visual instruction signatures changed.");
        var result = (byte[])source.Clone();
        // mov ebx,28; nop; nop -- one deterministic throw delay, no actor-indexed read.
        Convert.FromHexString("BB1C0000009090").CopyTo(result, timer);
        // GetPosition(caster,&origin.x,&origin.y,&origin.z), cdecl. The original
        // caller still removes 16 stack bytes. Existing w=1 and trajectory follow.
        Convert.FromHexString("6888041D106884041D106880041D10FF3570511D10A1F00009108B8018040000FFD090909090")
            .CopyTo(result, origin);
        var replacements = new Dictionary<int, int?> {
            [0x139A] = null, [0x18CB] = 0x18CB, [0x18D0] = 0x18D0,
            [0x18D9] = 0x18D5, [0x18DE] = 0x18DB, [0x18E4] = 0x18E0
        };
        var seen = new HashSet<int>();
        var reloc = optional.BaseRelocationTableDirectory;
        int cursor = Offset(reloc.RelativeVirtualAddress, reloc.Size), end = checked(cursor + reloc.Size);
        while (cursor < end)
        {
            if (end - cursor < 8) throw new InvalidDataException("Truncated relocation block.");
            int page = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(cursor, 4));
            int size = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(cursor + 4, 4));
            if (size < 8 || (size & 1) != 0 || size > end - cursor)
                throw new InvalidDataException("Invalid relocation block.");
            for (int at = cursor + 8; at < cursor + size; at += 2)
            {
                ushort entry = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(at, 2));
                int rva = checked(page + (entry & 0xFFF));
                if (!replacements.TryGetValue(rva, out var replacement)) continue;
                if ((entry >> 12) != 3 || !seen.Add(rva))
                    throw new InvalidDataException("Expected one HIGHLOW relocation for each patched operand.");
                ushort value = replacement.HasValue ? checked((ushort)(0x3000 | (replacement.Value - page))) : (ushort)0;
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at, 2), value);
            }
            cursor += size;
        }
        if (seen.Count != replacements.Count)
            throw new InvalidDataException("Required operand relocation is missing.");
        return result;
    }
}
