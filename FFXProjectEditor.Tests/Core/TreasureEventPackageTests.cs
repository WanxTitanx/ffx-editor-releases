using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureEventPackageTests
{
    private readonly ITestOutputHelper _out;
    public TreasureEventPackageTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Parse_Bika0200_EventPackage()
    {
        string ebp = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\event\obj\bi\bika0200\bika0200.ebp";
        if (!File.Exists(ebp)) return;

        var pkg = EventPackage.Read(ebp);
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"EventPackage: {pkg.Chunks.Count} chunks");
        for (int i = 0; i < pkg.Chunks.Count; i++)
        {
            var c = pkg.Chunks[i];
            string magic = c.Bytes.Length >= 4 ? System.Text.Encoding.ASCII.GetString(c.Bytes, 0, 4) : "?";
            sb.AppendLine($"  chunk {i}: {c.Bytes.Length} bytes, magic={magic}");
            if (c.Bytes.Length > 0 && c.Bytes.Length < 200)
                sb.AppendLine($"    hex: {c.Bytes.Take(64).Select(x => x.ToString("x2")).Aggregate((a, b) => a + " " + b)}");
        }

        // ATEL chunk (primeiro)
        var atel = pkg.AtelBytes;
        sb.AppendLine($"\nATEL chunk: {atel.Length} bytes");
        sb.AppendLine($"  magic: {System.Text.Encoding.ASCII.GetString(atel, 0, 4)}");
        if (atel.Length > 0x38)
        {
            int scriptStart = BitConverter.ToInt32(atel, 0x30);
            int workerCount = BitConverter.ToInt16(atel, 0x36);
            sb.AppendLine($"  scriptStart: 0x{scriptStart:X} workerCount: {workerCount}");
        }

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_ebp_parsed.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }

    private static string hex(int v) => $"0x{v:X}";
}