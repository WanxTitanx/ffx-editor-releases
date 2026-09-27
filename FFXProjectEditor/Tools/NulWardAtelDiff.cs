using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;

namespace FFXProjectEditor.Tools
{
    /// <summary>Hex diff command.bin rows for P01 ATEL research (NulTide vs Radiant).</summary>
    internal static class NulWardAtelDiff
    {
        const string DefaultKernel =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\command.bin";

        const string DefaultOutputDir = @"work\nul_ward_pack";

        static readonly int[] DefaultIds = { 48, 49, 320, 321 };

        public static int Run(string[] args)
        {
            try
            {
                string kernelPath = ArgValue(args, "--kernel") ?? DefaultKernel;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                int[] ids = ParseIds(ArgValue(args, "--ids")) ?? DefaultIds;

                Console.WriteLine("=== Nul Ward ATEL / row hex diff (P01) ===");
                Console.WriteLine($"kernel : {kernelPath}");
                Console.WriteLine($"output : {outputDir}");
                Console.WriteLine($"ids    : {string.Join(", ", ids)}");

                if (!File.Exists(kernelPath))
                {
                    Console.WriteLine($"FAIL: kernel missing: {kernelPath}");
                    return 2;
                }

                byte[] bytes = File.ReadAllBytes(kernelPath);
                var rows = Ability_Command.ReadList(bytes, hasExtraInfo: true);
                Directory.CreateDirectory(outputDir);

                var dumps = new List<object>();
                var diffs = new List<object>();

                foreach (int id in ids)
                {
                    if (id < 0 || id >= rows.Count)
                    {
                        Console.WriteLine($"WARN: id {id} out of range (count={rows.Count})");
                        continue;
                    }

                    byte[] row = ExtractRow(bytes, id);
                    string hexPath = Path.Combine(outputDir, $"command_row_{id:D3}.hex");
                    string binPath = Path.Combine(outputDir, $"command_row_{id:D3}.bin");
                    File.WriteAllBytes(binPath, row);
                    File.WriteAllText(hexPath, ToHexDump(row, id));

                    dumps.Add(new { id, binPath, hexPath, length = row.Length });
                    Console.WriteLine($"wrote id {id}: {binPath}");
                }

                if (dumps.Count >= 2)
                {
                    for (int i = 0; i < ids.Length - 1; i++)
                    {
                        int a = ids[i];
                        int b = ids[i + 1];
                        if (a >= rows.Count || b >= rows.Count) continue;
                        byte[] rowA = ExtractRow(bytes, a);
                        byte[] rowB = ExtractRow(bytes, b);
                        var diffOffsets = DiffOffsets(rowA, rowB);
                        string diffPath = Path.Combine(outputDir, $"atel_diff_{a}_{b}.json");
                        var diffPayload = new { a, b, diffOffsets, diffCount = diffOffsets.Count };
                        File.WriteAllText(diffPath, JsonSerializer.Serialize(diffPayload, new JsonSerializerOptions { WriteIndented = true }));
                        diffs.Add(diffPayload);
                        Console.WriteLine($"diff {a} vs {b}: {diffOffsets.Count} byte offsets -> {diffPath}");
                    }
                }

                var summary = new
                {
                    generated = DateTime.UtcNow.ToString("o"),
                    kernelPath,
                    entrySize = CommandGrowWriter.CommandEntrySize,
                    rowCount = rows.Count,
                    dumps,
                    diffs,
                    p01Note = "Compare row 49 (NulTide) vs 320 (Radiant) for ATEL opcode 51→56 hypothesis; perform script lives outside this 0x60 row.",
                };

                string summaryPath = Path.Combine(outputDir, "atel_diff_summary.json");
                File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"summary: {summaryPath}");
                Console.WriteLine("VERDICT: PASS (offline dump)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static byte[] ExtractRow(byte[] file, int id)
        {
            var list = EntryListFile.Unpack(file);
            int offset = id * CommandGrowWriter.CommandEntrySize;
            if (offset + CommandGrowWriter.CommandEntrySize > list.FirstFile.Length)
                throw new InvalidDataException($"Row {id} past end of first file.");
            byte[] row = new byte[CommandGrowWriter.CommandEntrySize];
            Array.Copy(list.FirstFile, offset, row, 0, row.Length);
            return row;
        }

        static List<int> DiffOffsets(byte[] a, byte[] b)
        {
            int len = Math.Min(a.Length, b.Length);
            var list = new List<int>();
            for (int i = 0; i < len; i++)
            {
                if (a[i] != b[i]) list.Add(i);
            }
            return list;
        }

        static string ToHexDump(byte[] row, int id)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# command.bin row id={id} size=0x{row.Length:X}");
            for (int i = 0; i < row.Length; i += 16)
            {
                sb.Append($"{i:X4}: ");
                for (int j = 0; j < 16 && i + j < row.Length; j++)
                    sb.Append($"{row[i + j]:X2} ");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static int[]? ParseIds(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.Parse(s))
                .ToArray();
        }

        static string? ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }
}
