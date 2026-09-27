using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.FfxLib.Event;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureEventDisasmTests
{
    private readonly ITestOutputHelper _out;
    public TreasureEventDisasmTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Disasm_Bika0200_ShowObtainTreasure()
    {
        string ebp = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\event\obj\bi\bika0200\bika0200.ebp";
        if (!File.Exists(ebp)) return;

        var package = EventPackage.Read(ebp);
        _out.WriteLine($"EventPackage: {package.AtelBytes.Length} atel bytes");

        var insts = EventAtelDisassembler.DisassembleStructured(
            package.AtelBytes,
            start: 0,
            length: package.AtelBytes.Length,
            resolveCall: op => { try { return EventCallGlossary.Entries.TryGetValue(op, out string? n) ? n : $"0x{op:X4}"; } catch { return $"0x{op:X4}"; } });

        _out.WriteLine($"Total instruções: {insts.Count}");

        // Mostra as instruções ao redor de cada obtainTreasure (0x015B)
        var sb = new System.Text.StringBuilder();
        int shown = 0;
        for (int i = 0; i < insts.Count && shown < 3; i++)
        {
            var inst = insts[i];
            if (inst.Mnemonic == "CALL" && inst.Operand == 0x015B)
            {
                int start = Math.Max(0, i - 12);
                int end = Math.Min(insts.Count, i + 3);
                sb.AppendLine($"\n=== obtainTreasure @ instr {i} (offset 0x{inst.Offset:X}) ===");
                for (int j = start; j < end; j++)
                {
                    var x = insts[j];
                    sb.AppendLine($"  [{j}] 0x{x.Offset:X4} {x.Mnemonic} {x.Operand:X4} {x.Operand}");
                }
                shown++;
            }
        }
        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_bika_disasm.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}