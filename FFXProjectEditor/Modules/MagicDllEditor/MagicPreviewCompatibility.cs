using System;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.MagicDll;

namespace FFXProjectEditor.Modules.MagicDllEditor;

internal static class MagicPreviewCompatibility
{
    // NoClip excludes Bushido 392..397 from its auxiliary flipbook decoder. A cloned
    // Tornado has the same auxiliary geometry but a new ID; the old ID-only branch
    // asserted on TEX0.cpsm before any particle was rendered. Match this PC family
    // by immutable native code AND resource layout, never by the clone's filename.
    // This is preview compatibility, not evidence that the native cast is portable.
    internal static bool SkipExtraFlipbooks(MagicDllFile file, ReadOnlySpan<byte> working)
    {
        if (file.DataSectionSize != 997376 || file.Roots.Count != 1 ||
            file.Roots[0].RootAbs != 0x1D1F0)
            return false;
        var code = file.Sections.SingleOrDefault(s => s.Name == ".text");
        if (code == null || code.RawSize != 27648 || code.RawPtr < 0 ||
            code.RawPtr > working.Length - code.RawSize)
            return false;
        string hash = Convert.ToHexString(SHA256.HashData(working.Slice(code.RawPtr, code.RawSize)));
        // Steam BuildID 25501027 rebuilt native code/IAT references while preserving
        // Tornado's .data bytes and rooted layout. Both complete code witnesses were
        // checked against the exact old/current donors; see STEAM_UPDATE_CUSTOM_EFFECTS_20261002.md.
        return hash is
            "03DAF1E7139283BD174AF547D3A69D2EE55E82470B8DA3E330716D7188D8C8BF" or
            "310AA825E53D63C5C1425179F9C40078DE8547B40B6F833A0C9FD408E840B959";
    }
}
