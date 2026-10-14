using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.FfxLib.Atel;
using FFXProjectEditor.FfxLib.Event;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureIntPoolTests
{
    private readonly ITestOutputHelper _out;
    public TreasureIntPoolTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Dump_Worker3F_IntPool()
    {
        string ebp = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\event\obj\bi\bika0200\bika0200.ebp";
        if (!File.Exists(ebp)) return;

        var package = EventPackage.Read(ebp);
        var atel = package.AtelBytes;

        // Pools pelo worker-offset table
        var pools = AtelScriptParser.ReadWorkers(atel, BitConverter.ToUInt16(atel, 0x34), 0x38);
        if (pools.Count == 0)
            pools = AtelScriptParser.ReadWorkers(atel, BitConverter.ToUInt16(atel, 0x36), 0x38);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Workers com pools: {pools.Count}");

        // Worker 0x3F (63 = index 63? ou offset?) — o scanner usa Index
        var w = pools.FirstOrDefault(p => p.Index == 0x3F);
        if (w == null) { w = pools.Skip(0x3F).FirstOrDefault(); }
        if (w != null)
        {
            sb.AppendLine($"\n=== WORKER index={w.Index} header=0x{w.HeaderOffset:X} ===");
            sb.AppendLine($"EventType={w.EventType} VarCount={w.VariableCount}");
            sb.AppendLine($"IntConstCount={w.IntegerConstantCount} FloatConstCount={w.FloatConstantCount}");
            sb.AppendLine($"IntOffset=0x{w.IntegerConstantOffset:X} FloatOffset=0x{w.FloatConstantOffset:X}");
            sb.AppendLine($"\nINT constants ({w.IntegerConstantCount}):");
            for (int i = 0; i < w.IntegerConstantCount; i++)
                sb.AppendLine($"  [{i}] = {w.IntegerConstantValues[i]} (0x{w.IntegerConstantValues[i]:X4})");
        }
        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_bika_intpool.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}