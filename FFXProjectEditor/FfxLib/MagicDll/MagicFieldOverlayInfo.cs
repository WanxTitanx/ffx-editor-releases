using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>Audited field-overlay interface whose initializer registers no embedded PPP
/// roots. This describes the verified callback path, not universal runtime unreachability.</summary>
public sealed class MagicFieldOverlayInfo
{
    public int RegisteredPppCount => 0;
    public uint CallbackTableRva => 0x74130;
    public IReadOnlyList<uint> CallbackRvas { get; }
    private MagicFieldOverlayInfo(uint[] callbacks) => CallbackRvas = Array.AsReadOnly(callbacks);

    internal static MagicFieldOverlayInfo? TryRead(MagicDllFile file)
    {
        byte[] bytes = file.FileBytes;
        if (bytes.Length < 64) return null;
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        if (pe < 0 || pe > bytes.Length - 248 || BitConverter.ToUInt16(bytes, pe + 24) != 0x10B ||
            BitConverter.ToUInt32(bytes, pe + 52) != 0x10000000) return null;
        var text = file.Sections.FirstOrDefault(s => s.Name == ".text");
        if (text is not { VirtualAddress: 0x1000, RawSize: 4096 } || text.RawPtr < 0 ||
            text.RawPtr > bytes.Length - text.RawSize) return null;
        string hash = Convert.ToHexString(SHA256.HashData(bytes.AsSpan(text.RawPtr, text.RawSize)));
        if (hash is not ("89FDC86680EDEBCB707F1E71FD9EBC2EF8ED7C80EFDED53E91EC875F78E536A7" or
            "275F04BC5C702B55AF805F3EC94F83BDD6050411BB55242888D25D26D8E89666")) return null;

        bool Offset(uint rva, int size, out int at)
        {
            at = -1;
            foreach (var section in file.Sections)
            {
                long delta = (long)rva - section.VirtualAddress;
                if (delta < 0 || delta > section.RawSize - (long)size) continue;
                long raw = section.RawPtr + delta;
                if (raw < 0 || raw > bytes.Length - (long)size) return false;
                at = (int)raw;
                return true;
            }
            return false;
        }
        uint U32(int at) => BitConverter.ToUInt32(bytes, at);
        uint exportRva = U32(pe + 120);
        if (!Offset(exportRva, 40, out int export)) return null;
        uint functions = U32(export + 20), names = U32(export + 24);
        if (functions is < 1 or > 4096 || names is < 1 or > 4096 ||
            !Offset(U32(export + 28), checked((int)functions * 4), out int addresses) ||
            !Offset(U32(export + 32), checked((int)names * 4), out int nameTable) ||
            !Offset(U32(export + 36), checked((int)names * 2), out int ordinals)) return null;
        bool getter = false, initializer = false;
        for (int i = 0; i < names; i++)
        {
            if (!Offset(U32(nameTable + 4 * i), 22, out int nameAt)) continue;
            ushort ordinal = BitConverter.ToUInt16(bytes, ordinals + 2 * i);
            if (ordinal >= functions) return null;
            uint target = U32(addresses + 4 * ordinal);
            getter |= bytes.AsSpan(nameAt, 22).SequenceEqual("GetEffectOverlayTable\0"u8) && target == 0x11C0;
            initializer |= bytes.AsSpan(nameAt, 13).SequenceEqual("InitMagicPRX\0"u8) && target == 0x11E0;
        }
        if (!getter || !initializer || !Offset(0x74130, 64, out int table)) return null;
        uint[] expected = { 0x10D0, 0x1130, 0x1150, 0x1170, 0x1190, 0x11B0, 0x11B0, 0,
            0x11B0, 0x11B0, 0x11B0, 0x11B0, 0x11B0, 0x11B0, 0x11B0, 0x11B0 };
        for (int i = 0; i < expected.Length; i++)
            if (U32(table + 4 * i) != (expected[i] == 0 ? 0 : 0x10000000u + expected[i])) return null;
        return new MagicFieldOverlayInfo(expected);
    }
}
