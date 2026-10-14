using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace FFXProjectEditor.FfxLib.Ps3;

/// <summary>Offline repair of the reviewed, owned Demolishing Fist overlay only.</summary>
public static class SinDemolishingFistVisualAdapter
{
    public const int OwnedMagicId = 814;

    public static byte[] Adapt(byte[] source, int ownedMagicId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ownedMagicId != OwnedMagicId)
            throw new ArgumentOutOfRangeException(nameof(ownedMagicId), "Only the owned Demolishing Fist effect is admitted.");

        using var stream = new MemoryStream(source, writable: false);
        using var pe = new PEReader(stream);
        var headers = pe.PEHeaders;
        var optional = headers.PEHeader ?? throw new InvalidDataException("Missing PE header.");
        if (headers.CoffHeader.Machine != Machine.I386 || optional.Magic != PEMagic.PE32 ||
            optional.ImageBase != 0x10000000 || optional.SizeOfImage != 0x342000 || optional.CheckSum != 0)
            throw new InvalidDataException("Unsupported Demolishing Fist image profile.");
        if (source.AsSpan().IndexOf(Encoding.Unicode.GetBytes("magic_0814")) < 0)
            throw new InvalidDataException("The visual must already have the owned magic_0814 identity.");

        int Offset(int rva, int size)
        {
            foreach (var section in headers.SectionHeaders)
            {
                long delta = (long)rva - section.VirtualAddress;
                long start = section.PointerToRawData + delta;
                if (delta >= 0 && size >= 0 && delta + size <= section.SizeOfRawData &&
                    start >= 0 && start + size <= source.Length)
                    return checked((int)start);
            }
            throw new InvalidDataException("Visual RVA is outside its file-backed section.");
        }

        var code = headers.SectionHeaders.Single(s => s.Name == ".text");
        if (code.VirtualAddress != 0x1000 || code.SizeOfRawData != 35840)
            throw new InvalidDataException("Unexpected Demolishing Fist code section.");
        string hash = Convert.ToHexString(SHA256.HashData(source.AsSpan(Offset(0x1000, 35840), 35840)));
        if (hash is not ("2788F4F751F3B2DC9AE68F43715A8C298F879D34AF3ADE8C5B12D51F396E17D3" or
                         "C5F1A518069F5AE8FB6B95C0C8E63AA1AD8102A0AB546CEA670977759AB3E71A"))
            throw new InvalidDataException("Demolishing Fist code is unreviewed or already adapted.");

        const int operandRva = 0x79E7;
        const int callRva = 0x79EB;
        int operand = Offset(operandRva, 4), call = Offset(callRva, 8);
        if (!source.AsSpan(operand, 4).SequenceEqual(Convert.FromHexString("50BD0B10")) ||
            !source.AsSpan(call, 8).SequenceEqual(Convert.FromHexString("8B80240B0000FFD0")))
            throw new InvalidDataException("The reviewed particle reuse site is missing.");

        // The pointer operand keeps its existing HIGHLOW relocation. The replacement
        // call is relative; admitting a relocation there would corrupt instructions.
        var reloc = optional.BaseRelocationTableDirectory;
        if (reloc.Size <= 0) throw new InvalidDataException("Missing visual relocations.");
        int cursor = Offset(reloc.RelativeVirtualAddress, reloc.Size), end = checked(cursor + reloc.Size);
        int operandRelocations = 0;
        while (cursor < end)
        {
            if (end - cursor < 8) throw new InvalidDataException("Truncated relocation block.");
            int page = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(cursor, 4));
            int size = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(cursor + 4, 4));
            if (page < 0 || size < 8 || (size & 1) != 0 || size > end - cursor)
                throw new InvalidDataException("Invalid relocation block.");
            for (int at = cursor + 8; at < cursor + size; at += 2)
            {
                ushort entry = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(at, 2));
                int type = entry >> 12;
                long rva = (long)page + (entry & 0xFFF);
                if (rva == operandRva && type == 3) ++operandRelocations;
                else if (type != 0 && rva < callRva + 8 && rva + 4 > operandRva)
                    throw new InvalidDataException("Unexpected relocation overlaps the particle reuse patch.");
            }
            cursor += size;
        }
        if (operandRelocations != 1)
            throw new InvalidDataException("The particle pointer must have exactly one HIGHLOW relocation.");

        byte[] result = (byte[])source.Clone();
        // Phase 32 reused node +BBCF0 while still linked under +33CC80. Calling
        // only ResourceRelease(node+60) left the parent links/count intact, and
        // the next constructor inserted the node into itself. Use the overlay's
        // own destructor (+48E0): release once AND detach before reconstruction.
        // The existing bit-4 guard already skips naturally completed particles.
        // See docs/reverse/magic_dlls/SIN_OGRE_SOFTLOCK_2026-10-09.md and native RT0.
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(operand, 4), 0x100BBCF0);
        Convert.FromHexString("8BCCE8EECEFFFF90").CopyTo(result, call); // mov ecx,esp; call +48E0; nop
        return result;
    }
}
