using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureBesaidScanTests
{
    private readonly ITestOutputHelper _out;
    public TreasureBesaidScanTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Scan_Besaid_Events()
    {
        string eventDir = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\event\obj\bs";
        if (!Directory.Exists(eventDir)) return;

        var sb = new System.Text.StringBuilder();
        foreach (var file in Directory.EnumerateFiles(eventDir, "*.ebp", SearchOption.AllDirectories))
        {
            try
            {
                var result = EventTreasureScanner.Scan(file);
                if (result.Candidates.Count > 0)
                {
                    sb.AppendLine($"{Path.GetFileName(file)}: {result.Candidates.Count} candidatos");
                    foreach (var c in result.Candidates)
                        sb.AppendLine($"  worker {c.WorkerIndex:X2}: treasures=[{string.Join(",", c.TreasureIds)}] models=[{string.Join(",", c.ModelIds.Select(m => m.ToString("X4")))}] pos={c.Positions.Count}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{Path.GetFileName(file)}: ERRO {ex.Message}");
            }
        }

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_besaid_scan.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}