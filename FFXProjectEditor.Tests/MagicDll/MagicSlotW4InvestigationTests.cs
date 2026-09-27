using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Investiga o campo +4 do slot (argument_relative): hipótese 2026-08-02 —
    /// o u16 BAIXO de +4 é o WIDTH do record (32=U1, 16=payload, 48=stride PppMem,
    /// 96=KeThRes96...); o u16 ALTO (1..4) é modo/índice. Se lo == RecordWidth,
    /// o runtime usa o +4 como step de callback e o editor pode usá-lo como largura
    /// nativa (em vez do schema). Relatório em stdout — não é assert de regressão.
    /// </summary>
    public class MagicSlotW4InvestigationTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;

        [Fact]
        public void Investigate_SlotW4_Records()
        {
            string path = Path.Combine(CorpusDir, "magic_0021.dll");
            var parser = new MagicDllParser();
            MagicDllFile dll = parser.Parse(path);

            var rows = dll.Roots
                .SelectMany(r => r.Programs)
                .SelectMany(p => p.Slots)
                .Select(s => new
                {
                    s.HandlerTableIndex,
                    s.SlotAbs,
                    s.RecordOffset,
                    s.RecordWidth,
                    W4Raw = ReadW4(path, s.SlotAbs),
                })
                .ToList();

            int match = 0;
            foreach (var r in rows)
            {
                int lo = r.W4Raw & 0xFFFF;
                int hi = (r.W4Raw >> 16) & 0xFFFF;
                bool eq = lo == r.RecordWidth;
                if (eq) match++;
                Console.WriteLine(
                    $"h={r.HandlerTableIndex,3} slot={r.SlotAbs:X} hi={hi} lo={lo,3} (0x{lo:X}) RecordWidth={r.RecordWidth,3} lo==width:{eq}");
            }
            Console.WriteLine($"MATCH lo==RecordWidth: {match}/{rows.Count}");
        }

        private static int ReadW4(string path, int slotAbs)
        {
            byte[] data = File.ReadAllBytes(path);
            // .data raw ptr do PE
            int pe = BitConverter.ToInt32(data, 0x3C);
            int coff = pe + 4;
            int num = BitConverter.ToUInt16(data, coff + 2);
            int opt = BitConverter.ToUInt16(data, coff + 16);
            int st = coff + 20 + opt;
            int dp = 0;
            for (int i = 0; i < num; i++)
            {
                int so = st + i * 40;
                string nm = System.Text.Encoding.ASCII.GetString(data, so, 8).TrimEnd('\0');
                if (nm is ".data" or "DATA")
                {
                    dp = BitConverter.ToInt32(data, so + 20);
                    break;
                }
            }
            return BitConverter.ToInt32(data, dp + slotAbs + 4);
        }
    }
}
