using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.FfxLib.Event;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureWorkerDisasmTests
{
    private readonly ITestOutputHelper _out;
    public TreasureWorkerDisasmTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Disasm_Worker3F_Bika0200()
    {
        string ebp = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\event\obj\bi\bika0200\bika0200.ebp";
        if (!File.Exists(ebp)) return;

        var package = EventPackage.Read(ebp);
        var atel = package.AtelBytes;
        int scriptStart = BitConverter.ToInt32(atel, 0x30);
        int codeLen = BitConverter.ToInt32(atel, 0x00);
        var insts = EventAtelDisassembler.DisassembleStructured(
            atel,
            start: scriptStart,
            length: codeLen,
            resolveCall: op => { try { return EventCallGlossary.Entries.TryGetValue(op, out string? n) ? n : $"0x{op:X4}"; } catch { return $"0x{op:X4}"; } });

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Total instruções: {insts.Count}");

        // Usa o SplitWorkers do scanner para achar os workers
        var workers = EventTreasureScanner.SplitWorkersForTest(insts, package.AtelBytes);
        sb.AppendLine($"Workers: {workers.Count}");

        // Disassemble worker 0x3F (61)
        var target = workers.FirstOrDefault(w => w.Index == 0x3F);
        if (target != null)
        {
            sb.AppendLine($"\n=== WORKER 0x3F ({target.Instructions.Count} instrs) ===");
            foreach (var x in target.Instructions)
                sb.AppendLine($"  0x{x.Offset:X4} {x.Mnemonic} {x.Operand:X4} {x.Operand}");
        }
        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_bika_worker3f.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}