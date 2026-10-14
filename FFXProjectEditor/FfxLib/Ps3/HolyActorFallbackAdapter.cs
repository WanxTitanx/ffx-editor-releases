using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FFXProjectEditor.FfxLib.Ps3;

/// <summary>
/// Steam 25501027 Holy clone: a missing field actor may resolve to the same
/// battle actor. The 2026-10-10 dump proves the null field lookup at constructor
/// RVA 0x2630; host slots 0x674/0x35C return compatible character objects.
/// Original successful lookups and all PPP/texture data are retained.
/// </summary>
public static class HolyActorFallbackAdapter
{
    public const short OwnedMagicId = 902;
    public const int ConstructorRva = 0x2630;
    public const int PatchRva = 0x26AB;
    public const int ContinueRva = 0x26B1;
    public const int CaveRva = 0x92D0;
    public const int HostPointerRva = 0x76AB4;
    const string TextHash = "9633d0d826d7ae96a05d31287e126a088954c4ac47c5f95c7d5bfcf9bd74b2f8";
    static readonly byte[] Displaced = [0x89, 0x46, 0x54, 0x83, 0xC4, 0x04];

    public static byte[] Apply(byte[] source, int ownedMagicId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ownedMagicId != OwnedMagicId)
            throw new ArgumentException("Apply only to the owned battle Holy clone.", nameof(ownedMagicId));
        if (source.Length != 0x77A00 || source[0] != 'M' || source[1] != 'Z')
            throw new InvalidDataException("Expected the reviewed Holy PE32 donor.");
        int pe = I32(source, 0x3C);
        if (pe < 0x40 || pe > source.Length - 24 - 0xE0 || U32(source, pe) != 0x4550 || U16(source, pe + 4) != 0x14C
            || U16(source, pe + 6) != 5 || U16(source, pe + 20) != 0xE0
            || U16(source, pe + 24) != 0x10B || U32(source, pe + 8) != 0x6AA222CF
            || U32(source, pe + 24 + 28) != 0x10000000 || U32(source, pe + 24 + 64) != 0)
            throw new InvalidDataException("Unsupported Holy executable profile or checksum contract.");
        int section = checked(pe + 24 + 0xE0);
        if (section > source.Length - 5 * 40 || !source.AsSpan(section, 8).SequenceEqual(".text\0\0\0"u8)
            || U32(source, section + 8) != 0x82D0 || U32(source, section + 12) != 0x1000
            || U32(source, section + 16) != 0x8400 || U32(source, section + 20) != 0x400
            || source.Length < 0x8800)
            throw new InvalidDataException("Holy section layout changed.");
        if (U32(source, pe + 24 + 56) != 0x30A000 || U32(source, pe + 24 + 32) != 0x1000
            || U32(source, pe + 24 + 36) != 0x200 || U32(source, section + 36) != 0x60000020)
            throw new InvalidDataException("Holy image extent/alignment/execute permissions changed.");
        var layout = new[]
        {
            (".text", 0x82D0, 0x1000, 0x8400, 0x400),
            (".rdata", 0x78E, 0xA000, 0x800, 0x8800),
            (".data", 0x2FAE18, 0xB000, 0x6B600, 0x9000),
            (".rsrc", 0x4F0, 0x306000, 0x600, 0x74600),
            (".reloc", 0x2C1C, 0x307000, 0x2E00, 0x74C00),
        };
        for (int i = 0; i < layout.Length; i++)
        {
            int at = section + i * 40; var s = layout[i];
            if (Encoding.ASCII.GetString(source, at, 8).TrimEnd('\0') != s.Item1
                || U32(source, at + 8) != s.Item2 || U32(source, at + 12) != s.Item3
                || U32(source, at + 16) != s.Item4 || U32(source, at + 20) != s.Item5)
                throw new InvalidDataException("Holy section bounds or relocation carrier changed.");
        }
        if (!Convert.ToHexString(SHA256.HashData(source.AsSpan(0x400, 0x8400))).Equals(TextHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Holy native code differs from the witnessed Steam donor.");
        int patch = 0x400 + PatchRva - 0x1000, cave = 0x400 + CaveRva - 0x1000;
        byte[] stub = BuildStub();
        if (!source.AsSpan(patch, Displaced.Length).SequenceEqual(Displaced)
            || source.AsSpan(cave, stub.Length).ToArray().Any(b => b != 0))
            throw new InvalidDataException("Holy instruction witness or unused code padding changed.");
        // PIC call/pop addresses the existing relocated host global without adding
        // an absolute operand or a PE relocation entry. Only zero .text padding is used.
        byte[] result = (byte[])source.Clone();
        result[patch] = 0xE9;
        Put32(result, patch + 1, CaveRva - (PatchRva + 5));
        result[patch + 5] = 0x90;
        stub.CopyTo(result, cave);
        Put32(result, section + 8, CaveRva + stub.Length - 0x1000);
        return result;
    }

    public static byte[] BuildStub()
    {
        var code = new List<byte>();
        void Emit(params byte[] bytes) => code.AddRange(bytes);
        int Reserve32() { int at = code.Count; Emit(0, 0, 0, 0); return at; }
        void Relative(int at, int destination)
        {
            byte[] delta = BitConverter.GetBytes(destination - (CaveRva + at + 4));
            for (int i = 0; i < 4; i++) code[at + i] = delta[i];
        }
        Emit(0x85, 0xC0, 0x0F, 0x85); int found = Reserve32();
        Emit(0xF6, 0x86, 0x90, 0, 0, 0, 1, 0x0F, 0x84); int battle = Reserve32();
        Emit(0xE8, 0, 0, 0, 0); int self = CaveRva + code.Count;
        Emit(0x58, 0x8B, 0x80); int host = Reserve32();
        byte[] hostDelta = BitConverter.GetBytes(HostPointerRva - self);
        for (int i = 0; i < 4; i++) code[host + i] = hostDelta[i];
        Emit(0x8B, 0x80, 0x5C, 0x03, 0, 0, 0xFF, 0xD0);
        int store = CaveRva + code.Count;
        Emit(Displaced); Emit(0xE9); int resume = Reserve32();
        Relative(found, store); Relative(battle, store); Relative(resume, ContinueRva);
        return code.ToArray();
    }

    static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o, 2));
    static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4));
    static int I32(byte[] b, int o) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o, 4));
    static void Put32(byte[] b, int o, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o, 4), value);
}
