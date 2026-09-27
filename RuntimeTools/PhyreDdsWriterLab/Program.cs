// PhyreDdsWriterLab — Tier-1 round-trip primitive for PhyreEngine .dds.phyre textures.
//
// The repo already READS .dds.phyre byte-exact (FFXProjectEditor/FfxLib/Ps3/Ps3MagicTextureReader.cs).
// This lab is the missing WRITE half: a SYMMETRIC writer so an edited texture can be re-wrapped into a
// byte-identical-when-unchanged .dds.phyre and land in-game via the External File Loader.
//
// Proved container model (docs/history/PS3DATA_DDS_PHYRE_DECODE_2026-06-01.md,
//                         work/ps3data_agents/Extract-DdsPhyre.ps1):
//
//   .dds.phyre = [ PhyreEngine RYHPT header (~2.7 KB) ] + [ texture buffer: mip0, mip1, ... (largest first) ]
//
//   The real PTexture2D instance is the "PTexture2D" occurrence whose following ASCII token is a real
//   pixel format (ARGB8 / DXT1 / DXT3 / DXT5 / L8).  [PROVED in reader/extractor]
//     width  = U32@(pidx-88), height = U32@(pidx-84)
//     bufferStart = pidx + 11 + formatToken.Length + 38
//     U32@80 = m_maxTextureBufferSize = mip0 byte size (largest mip)
//
// WRITER STRATEGY (byte-exact by construction):
//   A container is split into two verbatim slices: Header = bytes[0 .. bufferStart) and
//   TextureBuffer = bytes[bufferStart .. end). The whole file is exactly Header ++ TextureBuffer.
//   * Re-wrap with an UNCHANGED buffer is trivially byte-identical (it re-emits the same two slices).
//   * Extract->wrap: ExtractDds() emits a standard DDS = [128-B DDS header] ++ TextureBuffer (the FULL
//     buffer, so the entire mip chain survives as DDS surface data). WrapDds() strips the 128-B header
//     and re-uses the surface payload as the new TextureBuffer. So Extract->Wrap of an unchanged file
//     reproduces the original byte-for-byte. Editing = swap the DDS surface (same dims/format), re-wrap.
//
// HONESTY: byte-exactness is asserted, not assumed (--roundtrip / --selftest). The header is preserved
// VERBATIM; this lab does NOT re-author Phyre reflection/header fields (changing dims/format would
// require header edits that are out of scope and UNVERIFIED). Same-shape texture swap is the proved
// Tier-1 capability. On-screen / in-game verification is NOT available this run.

using System.Text;

namespace PhyreDdsWriterLab;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                PrintUsage();
                return 0;
            }

            switch (args[0])
            {
                case "--roundtrip":
                    if (args.Length < 2)
                    {
                        Console.Error.WriteLine("error: --roundtrip needs a <file> path");
                        return 2;
                    }
                    return RoundTrip(args[1]) ? 0 : 1;

                case "--selftest":
                    return SelfTest() ? 0 : 1;

                case "--extract":
                    if (args.Length < 3)
                    {
                        Console.Error.WriteLine("error: --extract needs <in.dds.phyre> <out.dds>");
                        return 2;
                    }
                    return ExtractCli(args[1], args[2]) ? 0 : 1;

                case "--wrap":
                    if (args.Length < 4)
                    {
                        Console.Error.WriteLine("error: --wrap needs <template.dds.phyre> <in.dds> <out.dds.phyre>");
                        return 2;
                    }
                    return WrapCli(args[1], args[2], args[3]) ? 0 : 1;

                default:
                    Console.Error.WriteLine($"error: unknown command '{args[0]}'");
                    PrintUsage();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            return 3;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("PhyreDdsWriterLab — symmetric .dds.phyre round-trip writer (Tier-1)");
        Console.WriteLine();
        Console.WriteLine("  --roundtrip <file.dds.phyre>           read->write reproduces input byte-for-byte (asserts)");
        Console.WriteLine("  --selftest                             synthesize an ARGB8 container in-memory and round-trip it");
        Console.WriteLine("  --extract <in.dds.phyre> <out.dds>     extract the texture surface to a standard .dds");
        Console.WriteLine("  --wrap <template.dds.phyre> <in.dds> <out.dds.phyre>");
        Console.WriteLine("                                         re-wrap a .dds surface into a .dds.phyre using template's header");
    }

    // ---------------------------------------------------------------- --roundtrip
    private static bool RoundTrip(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"error: file not found: {path}");
            return false;
        }

        byte[] original = File.ReadAllBytes(path);
        Console.WriteLine($"input        : {Path.GetFileName(path)} ({original.Length:N0} bytes)");

        PhyreDdsContainer container = PhyreDdsContainer.Read(original);
        Console.WriteLine($"format       : {container.Format}");
        Console.WriteLine($"dims (header): {container.Width}x{container.Height}");
        Console.WriteLine($"pidx         : {container.PTextureInstanceOffset}");
        Console.WriteLine($"bufferStart  : {container.BufferStart}");
        Console.WriteLine($"headerBytes  : {container.Header.Length:N0}");
        Console.WriteLine($"textureBytes : {container.TextureBuffer.Length:N0}");
        Console.WriteLine($"mip0 size    : {container.Mip0Size:N0}");
        Console.WriteLine($"mipped       : {(container.TextureBuffer.Length > container.Mip0Size + 16 ? "yes (mip chain present)" : "no (single surface)")}");

        return AssertRoundTrip(container, original);
    }

    /// <summary>Runs both round-trip assertions against the original bytes. Returns true on full byte-identity.</summary>
    private static bool AssertRoundTrip(PhyreDdsContainer container, byte[] original)
    {
        bool ok = true;

        // (1) Identity re-wrap: Header ++ unchanged TextureBuffer must reproduce the input.
        byte[] reWrapped = container.WriteUnchanged();
        bool identity = BytesEqual(original, reWrapped, out int idDiff);
        Console.WriteLine(identity
            ? "PASS [PROVED] identity re-wrap   : Header ++ TextureBuffer == input (byte-for-byte)"
            : $"FAIL identity re-wrap   : first diff at byte {idDiff} (len {original.Length} vs {reWrapped.Length})");
        ok &= identity;

        // (2) Extract -> Wrap: emit a standard DDS, then re-wrap its surface. Unchanged => byte-identical.
        byte[] dds = container.ExtractDds();
        byte[] viaDds = PhyreDdsContainer.WrapDds(container, dds);
        bool viaDdsOk = BytesEqual(original, viaDds, out int ddsDiff);
        Console.WriteLine(viaDdsOk
            ? "PASS [PROVED] extract->wrap      : ExtractDds() then WrapDds() == input (byte-for-byte)"
            : $"FAIL extract->wrap      : first diff at byte {ddsDiff} (len {original.Length} vs {viaDds.Length})");
        ok &= viaDdsOk;

        // (3) DDS sanity: starts with the 'DDS ' magic and a 124-byte header descriptor.
        bool ddsMagic = dds.Length >= 128
                        && dds[0] == (byte)'D' && dds[1] == (byte)'D' && dds[2] == (byte)'S' && dds[3] == (byte)' '
                        && BitConverter.ToUInt32(dds, 4) == 124u;
        Console.WriteLine(ddsMagic
            ? $"PASS [STRUCT] dds magic+header   : 'DDS ' + dwSize=124 ({dds.Length:N0} bytes, surface {dds.Length - 128:N0})"
            : "FAIL dds magic/header   : extracted .dds is not a well-formed DDS");
        ok &= ddsMagic;

        Console.WriteLine(ok ? "RESULT       : ROUNDTRIP OK (byte-exact)" : "RESULT       : ROUNDTRIP FAILED");
        return ok;
    }

    // ---------------------------------------------------------------- --selftest
    private static bool SelfTest()
    {
        Console.WriteLine("selftest     : synthesizing an in-memory ARGB8 .dds.phyre-shaped container (no external sample)");
        // 8x4 ARGB8 surface => mip0 = 8*4*4 = 128 bytes. Plus a tiny fake mip1 (4x2*4=32) to exercise mip-chain preservation.
        byte[] synthetic = SyntheticContainer.BuildArgb8(width: 8, height: 4, includeMip1: true);
        Console.WriteLine($"synthetic    : {synthetic.Length:N0} bytes");

        PhyreDdsContainer container = PhyreDdsContainer.Read(synthetic);
        Console.WriteLine($"format       : {container.Format}");
        Console.WriteLine($"dims (header): {container.Width}x{container.Height}");
        Console.WriteLine($"bufferStart  : {container.BufferStart}");
        Console.WriteLine($"textureBytes : {container.TextureBuffer.Length:N0}");

        bool ok = AssertRoundTrip(container, synthetic);

        // Extra: prove an EDIT path is symmetric — extract, mutate one surface byte, re-wrap, re-read,
        // confirm the edit landed and everything else is preserved.
        byte[] dds = container.ExtractDds();
        int surfaceByte = 128 + 7;           // some pixel byte inside mip0
        byte before = dds[surfaceByte];
        dds[surfaceByte] = (byte)(before ^ 0xFF);
        byte[] edited = PhyreDdsContainer.WrapDds(container, dds);
        PhyreDdsContainer reread = PhyreDdsContainer.Read(edited);
        bool editLanded = reread.TextureBuffer[7] == (byte)(before ^ 0xFF);
        bool headerPreserved = BytesEqual(container.Header, reread.Header, out _);
        bool sizePreserved = edited.Length == synthetic.Length;
        bool editOk = editLanded && headerPreserved && sizePreserved;
        Console.WriteLine(editOk
            ? "PASS [PROVED] edit path          : extract->mutate->wrap landed the edit, header+size preserved"
            : $"FAIL edit path          : landed={editLanded} headerPreserved={headerPreserved} sizePreserved={sizePreserved}");
        ok &= editOk;

        Console.WriteLine(ok ? "RESULT       : SELFTEST OK" : "RESULT       : SELFTEST FAILED");
        return ok;
    }

    // ---------------------------------------------------------------- --extract / --wrap CLI
    private static bool ExtractCli(string inPath, string outPath)
    {
        byte[] bytes = File.ReadAllBytes(inPath);
        PhyreDdsContainer container = PhyreDdsContainer.Read(bytes);
        byte[] dds = container.ExtractDds();
        File.WriteAllBytes(outPath, dds);
        Console.WriteLine($"extracted    : {container.Format} {container.Width}x{container.Height} -> {outPath} ({dds.Length:N0} bytes)");
        return true;
    }

    private static bool WrapCli(string templatePath, string ddsPath, string outPath)
    {
        byte[] template = File.ReadAllBytes(templatePath);
        PhyreDdsContainer container = PhyreDdsContainer.Read(template);
        byte[] dds = File.ReadAllBytes(ddsPath);
        byte[] wrapped = PhyreDdsContainer.WrapDds(container, dds);
        File.WriteAllBytes(outPath, wrapped);
        Console.WriteLine($"wrapped      : {ddsPath} into template '{Path.GetFileName(templatePath)}' -> {outPath} ({wrapped.Length:N0} bytes)");
        return true;
    }

    // ---------------------------------------------------------------- helpers
    internal static bool BytesEqual(byte[] a, byte[] b, out int firstDiff)
    {
        firstDiff = -1;
        if (a.Length != b.Length)
        {
            firstDiff = Math.Min(a.Length, b.Length);
            return false;
        }
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) { firstDiff = i; return false; }
        }
        return true;
    }
}

/// <summary>
/// A parsed .dds.phyre container split into a VERBATIM header slice and a VERBATIM texture-buffer slice.
/// The whole file is exactly Header ++ TextureBuffer. All writes preserve the header byte-for-byte.
/// </summary>
internal sealed class PhyreDdsContainer
{
    public required byte[] Header { get; init; }          // bytes [0 .. BufferStart)  — preserved verbatim
    public required byte[] TextureBuffer { get; init; }   // bytes [BufferStart .. end) — mip0, mip1, ...
    public required string Format { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int PTextureInstanceOffset { get; init; }
    public required int BufferStart { get; init; }

    public int Mip0Size => Mip0SizeFor(Width, Height, Format);

    private static readonly string[] KnownFormats = { "ARGB8", "DXT1", "DXT3", "DXT5", "L8" };

    public static PhyreDdsContainer Read(byte[] buffer)
    {
        if (buffer.Length < 96
            || buffer[0] != (byte)'R' || buffer[1] != (byte)'Y' || buffer[2] != (byte)'H'
            || buffer[3] != (byte)'P' || buffer[4] != (byte)'T')
        {
            throw new InvalidDataException("Not a RYHPT/.phyre container (magic mismatch).");
        }

        int scanLimit = Math.Min(buffer.Length, 16384);
        int pidx = -1;
        string format = string.Empty;
        int from = 0;
        while (true)
        {
            int hit = FindAscii(buffer, "PTexture2D", from, scanLimit);
            if (hit < 0) break;
            string token = ReadToken(buffer, hit + 10);
            if (Array.IndexOf(KnownFormats, token) >= 0)
            {
                pidx = hit;
                format = token;
                break;
            }
            from = hit + 1;
        }
        if (pidx < 0)
            throw new InvalidDataException("No PTexture2D instance with a known pixel format token (atlas/other class?).");

        int width = (int)ReadU32(buffer, pidx - 88);
        int height = (int)ReadU32(buffer, pidx - 84);
        if (width is < 1 or > 8192 || height is < 1 or > 8192)
            throw new InvalidDataException($"Header dims implausible ({width}x{height}).");

        int bufferStart = pidx + 11 + format.Length + 38;
        int mip0 = Mip0SizeFor(width, height, format);
        if (bufferStart < 0 || mip0 <= 0 || (long)bufferStart + mip0 > buffer.Length)
            throw new InvalidDataException($"mip0 region out of range (W={width} H={height} fmt={format} bufStart={bufferStart} mip0={mip0} len={buffer.Length}).");

        byte[] header = new byte[bufferStart];
        Array.Copy(buffer, 0, header, 0, bufferStart);
        byte[] texture = new byte[buffer.Length - bufferStart];
        Array.Copy(buffer, bufferStart, texture, 0, texture.Length);

        return new PhyreDdsContainer
        {
            Header = header,
            TextureBuffer = texture,
            Format = format,
            Width = width,
            Height = height,
            PTextureInstanceOffset = pidx,
            BufferStart = bufferStart
        };
    }

    /// <summary>Re-emit the container with the stored (unchanged) texture buffer. Byte-identical to the read input.</summary>
    public byte[] WriteUnchanged() => Concat(Header, TextureBuffer);

    /// <summary>Re-emit the container with a NEW texture buffer (must be the same byte length to keep the header valid).</summary>
    public byte[] WriteWithBuffer(byte[] newBuffer)
    {
        if (newBuffer.Length != TextureBuffer.Length)
            throw new InvalidOperationException(
                $"Replacement buffer length {newBuffer.Length} != original {TextureBuffer.Length}. " +
                "Header carries the buffer size; a different length needs header re-authoring (out of scope / UNVERIFIED).");
        return Concat(Header, newBuffer);
    }

    /// <summary>
    /// Emit a standard .dds = [128-byte DDS header] ++ the FULL texture buffer (entire mip chain as surface data).
    /// Round-trippable: WrapDds() strips the 128-byte header and re-uses the surface payload as the texture buffer.
    /// </summary>
    public byte[] ExtractDds()
    {
        byte[] ddsHeader = BuildDdsHeader(Width, Height, Format, TextureBuffer.Length, Mip0Size);
        return Concat(ddsHeader, TextureBuffer);
    }

    /// <summary>
    /// Re-wrap a standard .dds (128-byte header + surface) back into a .dds.phyre using <paramref name="template"/>'s
    /// verbatim header. The surface payload becomes the new texture buffer. Unchanged => byte-identical to the template.
    /// </summary>
    public static byte[] WrapDds(PhyreDdsContainer template, byte[] dds)
    {
        if (dds.Length < 128
            || dds[0] != (byte)'D' || dds[1] != (byte)'D' || dds[2] != (byte)'S' || dds[3] != (byte)' ')
            throw new InvalidDataException("Not a DDS file (missing 'DDS ' magic).");
        uint dwSize = BitConverter.ToUInt32(dds, 4);
        if (dwSize != 124u)
            throw new InvalidDataException($"Unexpected DDS header dwSize={dwSize} (expected 124).");

        // Surface = everything after the 128-byte header (no DX10 header is emitted by this lab).
        byte[] surface = new byte[dds.Length - 128];
        Array.Copy(dds, 128, surface, 0, surface.Length);
        return template.WriteWithBuffer(surface);
    }

    // ---- DDS header (mirrors work/ps3data_agents/Extract-DdsPhyre.ps1 New-DdsHeader, with full mip-chain sizing) ----
    private static byte[] BuildDdsHeader(int w, int h, string fmt, int totalSurfaceBytes, int mip0)
    {
        byte[] hdr = new byte[128];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(hdr, 0);
        void S(int off, long val) => BitConverter.GetBytes((uint)(val & 0xFFFFFFFFL)).CopyTo(hdr, off);

        S(4, 124);        // dwSize
        S(76, 32);        // ddspf.dwSize
        S(12, h);         // dwHeight
        S(16, w);         // dwWidth
        S(24, 1);         // dwDepth
        S(108, 0x1000);   // dwCaps = DDSCAPS_TEXTURE

        // mipMapCount + flags: if the buffer is larger than mip0, declare a mip chain so the whole surface survives.
        bool mipped = totalSurfaceBytes > mip0;
        int mipCount = mipped ? CountMips(w, h, fmt, totalSurfaceBytes, mip0) : 1;
        S(28, mipCount);

        if (fmt == "ARGB8")
        {
            // flags: CAPS|HEIGHT|WIDTH|PIXELFORMAT|PITCH (|MIPMAPCOUNT) ; pf flags: ALPHAPIXELS|RGB
            S(8, mipped ? 0x2100F : 0x100F);
            S(20, w * 4);                 // dwPitchOrLinearSize = pitch (bytes per row of mip0)
            S(80, 0x41);                  // DDPF_ALPHAPIXELS | DDPF_RGB
            S(88, 32);                    // RGB bit count
            S(92, 0x00FF0000);            // R mask
            S(96, 0x0000FF00);            // G mask
            S(100, 0x000000FF);           // B mask
            S(104, 0xFF000000);           // A mask
            if (mipped) S(108, 0x401008); // CAPS_COMPLEX | TEXTURE | MIPMAP
        }
        else // DXT1/DXT3/DXT5 (BC1/BC2/BC3)
        {
            S(8, mipped ? 0xA0007 : 0x80007); // CAPS|HEIGHT|WIDTH|PIXELFORMAT|LINEARSIZE (|MIPMAPCOUNT)
            S(20, mip0);                       // dwPitchOrLinearSize = linear size of mip0
            S(80, 0x4);                        // DDPF_FOURCC
            Encoding.ASCII.GetBytes(fmt).CopyTo(hdr, 84); // 'DXT1'/'DXT3'/'DXT5'
            if (mipped) S(108, 0x401008);
        }
        return hdr;
    }

    private static int CountMips(int w, int h, string fmt, int totalSurfaceBytes, int mip0)
    {
        int count = 0, mw = w, mh = h, consumed = 0;
        while (consumed < totalSurfaceBytes && (mw >= 1 || mh >= 1))
        {
            int sz = Mip0SizeFor(Math.Max(1, mw), Math.Max(1, mh), fmt);
            if (consumed + sz > totalSurfaceBytes) break;
            consumed += sz;
            count++;
            if (mw == 1 && mh == 1) break;
            mw = Math.Max(1, mw / 2);
            mh = Math.Max(1, mh / 2);
        }
        return Math.Max(1, count);
    }

    internal static int Mip0SizeFor(int w, int h, string fmt) => fmt switch
    {
        "ARGB8" => w * h * 4,
        "DXT1" => ((w + 3) / 4) * ((h + 3) / 4) * 8,
        "DXT3" or "DXT5" => ((w + 3) / 4) * ((h + 3) / 4) * 16,
        "L8" => w * h,
        _ => 0
    };

    private static byte[] Concat(byte[] a, byte[] b)
    {
        byte[] o = new byte[a.Length + b.Length];
        Array.Copy(a, 0, o, 0, a.Length);
        Array.Copy(b, 0, o, a.Length, b.Length);
        return o;
    }

    private static int FindAscii(byte[] buffer, string needle, int start, int limit)
    {
        byte[] nb = Encoding.ASCII.GetBytes(needle);
        int end = Math.Min(buffer.Length - nb.Length, limit);
        for (int i = Math.Max(0, start); i <= end; i++)
        {
            bool match = true;
            for (int k = 0; k < nb.Length; k++)
            {
                if (buffer[i + k] != nb[k]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    private static string ReadToken(byte[] buffer, int start)
    {
        int o = start;
        while (o < buffer.Length && buffer[o] == 0) o++;
        StringBuilder sb = new();
        while (o < buffer.Length && buffer[o] >= 32 && buffer[o] < 127)
        {
            sb.Append((char)buffer[o]);
            o++;
        }
        return sb.ToString();
    }

    private static uint ReadU32(byte[] buffer, int offset) =>
        offset < 0 || offset + 4 > buffer.Length ? 0u : BitConverter.ToUInt32(buffer, offset);
}

/// <summary>
/// Builds a minimal, well-formed-for-this-lab .dds.phyre-shaped buffer so --selftest validates the
/// round-trip with NO external sample. It reproduces the exact anchor layout the proved reader keys on:
///   ... RYHPT magic ... [class table prefix] ... U32 width @ (pidx-88), U32 height @ (pidx-84) ...
///   "PTexture2D\0" + FORMAT + "\0" + 38-byte trailer + texture buffer.
/// It is a STRUCTURAL fixture (not a real Phyre file); its only contract is that PhyreDdsContainer.Read
/// parses it the same way it parses real shipped textures.
/// </summary>
internal static class SyntheticContainer
{
    public static byte[] BuildArgb8(int width, int height, bool includeMip1)
    {
        const string format = "ARGB8";
        // Anchor math: width@(pidx-88), height@(pidx-84). So pidx must be >= 88.
        // Layout: [0..) RYHPT + filler up to (pidx-88), then dims, then filler up to pidx, then instance.
        int pidx = 128; // comfortably > 88 and gives room for magic + a fake class-table region.

        int mip0 = width * height * 4;
        int mip1 = includeMip1 ? Math.Max(1, width / 2) * Math.Max(1, height / 2) * 4 : 0;

        // bufferStart = pidx + 11 + len(FORMAT) + 38
        int bufferStart = pidx + 11 + format.Length + 38;
        int total = bufferStart + mip0 + mip1;
        byte[] buf = new byte[total];

        // RYHPT magic + a platform marker (cosmetic; reader only checks the first 5 bytes).
        // Byte map (pidx=128): 0..11 magic, 12..15 "11XD", 16..30 decoy, dims @40/@44, marker @80, instance @128.
        Encoding.ASCII.GetBytes("RYHPT\0\0\0").CopyTo(buf, 0);
        Encoding.ASCII.GetBytes("11XD").CopyTo(buf, 12);

        // A decoy "PTexture2DBase" in the class-table region whose following token is NOT a real format —
        // exercises the reader's instance-selection loop (it must skip this and find the real instance).
        Encoding.ASCII.GetBytes("PTexture2DBase\0").CopyTo(buf, 16);

        // Dimensions at the proved offsets relative to the REAL instance (pidx-88=40, pidx-84=44).
        BitConverter.GetBytes((uint)width).CopyTo(buf, pidx - 88);
        BitConverter.GetBytes((uint)height).CopyTo(buf, pidx - 84);

        // m_maxTextureBufferSize (U32@80 in real files) = mip0 size — cosmetic here, preserved verbatim.
        BitConverter.GetBytes((uint)mip0).CopyTo(buf, 80);
        BitConverter.GetBytes((uint)0x01020304).CopyTo(buf, 84); // m_phyreMarker (byte-order)

        // The real PTexture2D instance + format token + 38-byte trailer.
        Encoding.ASCII.GetBytes("PTexture2D\0").CopyTo(buf, pidx);          // 11 bytes incl. NUL
        Encoding.ASCII.GetBytes(format).CopyTo(buf, pidx + 11);             // FORMAT (no trailing NUL needed; trailer follows)
        // trailer (38 bytes) left as zeros — preserved verbatim by the writer regardless of content.

        // Texture buffer: deterministic, non-trivial pattern so an edit is detectable.
        for (int i = 0; i < mip0 + mip1; i++)
            buf[bufferStart + i] = (byte)((i * 37 + 11) & 0xFF);

        return buf;
    }
}
