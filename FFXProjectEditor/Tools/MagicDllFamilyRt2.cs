using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.Tools
{
    internal static class MagicDllFamilyRt2
    {
        const string DefaultCsv = @"scripts\final_581_classification_v2.csv";
        const string DefaultOutputDir = @"work\magic_dll_family_rt2";

        public static int Run(string[] args)
        {
            try
            {
                string repoRoot = Directory.GetCurrentDirectory();
                string magicRoot = args.Length > 1 ? args[1] : MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
                string csvPath = args.Length > 2 ? args[2] : Path.Combine(repoRoot, DefaultCsv);
                string outputDir = args.Length > 3 ? args[3] : Path.Combine(repoRoot, DefaultOutputDir);

                Console.WriteLine("=== Magic DLL Family Classifier RT2 ===");
                Console.WriteLine($"repo   : {repoRoot}");
                Console.WriteLine($"dlls   : {magicRoot}");
                Console.WriteLine($"csv    : {csvPath}");
                Console.WriteLine($"output : {outputDir}");

                if (!File.Exists(csvPath))
                {
                    Console.WriteLine($"FAIL: reference CSV not found: {csvPath}");
                    return 1;
                }

                if (!Directory.Exists(magicRoot))
                {
                    Console.WriteLine($"FAIL: magicFiles root not found: {magicRoot}");
                    return 1;
                }

                Directory.CreateDirectory(outputDir);

                List<CsvRow> reference = LoadCsv(csvPath);
                List<CompareRow> rows = [];
                int match = 0;
                int mismatch = 0;
                int missing = 0;
                int errors = 0;

                foreach (CsvRow row in reference)
                {
                    string dllPath = Path.Combine(magicRoot, row.Dll);
                    if (!File.Exists(dllPath))
                    {
                        missing++;
                        rows.Add(new CompareRow(row.Mid, row.Dll, row.Family, row.Method, "?", "missing_file", string.Empty, false));
                        continue;
                    }

                    try
                    {
                        MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                        MagicDllFamilyClassification cls = MagicDllFamilyClassifier.Classify(inspection);
                        string expected = NormalizeReferenceFamily(row.Family);
                        string actual = NormalizeDetectedFamily(cls.Family);
                        string offsets = string.Join(",", cls.Slot0DiscriminantOffsets.Select(o => $"0x{o:X}"));
                        bool ok = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
                        if (ok) match++;
                        else mismatch++;
                        rows.Add(new CompareRow(row.Mid, row.Dll, expected, row.Method, actual, cls.Method.ToString(), offsets, ok));
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        rows.Add(new CompareRow(row.Mid, row.Dll, NormalizeReferenceFamily(row.Family), row.Method, "?", "exception", ex.Message, false));
                    }
                }

                string mismatchCsv = Path.Combine(outputDir, "mismatches.csv");
                string fullCsv = Path.Combine(outputDir, "full_compare.csv");
                string summaryPath = Path.Combine(outputDir, "SUMMARY.md");

                WriteFullCsv(fullCsv, rows);
                WriteMismatchCsv(mismatchCsv, rows.Where(r => !r.Match).ToList());
                WriteSummary(summaryPath, reference.Count, match, mismatch, missing, errors, rows);

                Console.WriteLine($"rows      : {reference.Count}");
                Console.WriteLine($"match     : {match}");
                Console.WriteLine($"mismatch  : {mismatch}");
                Console.WriteLine($"missing   : {missing}");
                Console.WriteLine($"errors    : {errors}");
                Console.WriteLine($"full      : {fullCsv}");
                Console.WriteLine($"mismatches: {mismatchCsv}");
                Console.WriteLine($"summary   : {summaryPath}");

                foreach (IGrouping<string, CompareRow> g in rows.Where(r => !r.Match).GroupBy(r => $"{r.Expected}->{r.Actual}").OrderByDescending(g => g.Count()).Take(12))
                    Console.WriteLine($"  drift {g.Key}: {g.Count()}");

                bool pass = mismatch == 0 && missing == 0 && errors == 0 && reference.Count > 0;
                Console.WriteLine(pass
                    ? "VERDICT: PASS - C# MagicDllFamilyClassifier matches final_581_classification_v2.csv for all DLLs."
                    : "VERDICT: FAIL - classifier drift vs reference CSV.");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static string NormalizeReferenceFamily(string family) => family.Trim().ToUpperInvariant() switch
        {
            "A" => "A",
            "B" or "B_EXTENDED" => "B",
            "C" or "C_HEAVY" or "C_LIGHT" => "C",
            "D" => "D",
            _ => "?"
        };

        static string NormalizeDetectedFamily(MagicDllEffectFamily family) => family switch
        {
            MagicDllEffectFamily.A_ParticleSelfContained => "A",
            MagicDllEffectFamily.B_RootRecordInterpreter => "B",
            MagicDllEffectFamily.C_RootSelfGovernedParam => "C",
            MagicDllEffectFamily.D_EgoTasklist => "D",
            _ => "?"
        };

        static List<CsvRow> LoadCsv(string path)
        {
            using StreamReader sr = new(path);
            string? header = sr.ReadLine();
            if (header == null)
                return [];

            List<CsvRow> rows = [];
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                string[] parts = SplitCsvLine(line);
                if (parts.Length < 5)
                    continue;
                rows.Add(new CsvRow(parts[0], parts[1], parts[2], parts[3], parts[4]));
            }
            return rows;
        }

        static string[] SplitCsvLine(string line)
        {
            List<string> values = [];
            StringBuilder current = new();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (c == ',' && !quoted)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            values.Add(current.ToString());
            return values.ToArray();
        }

        static void WriteFullCsv(string path, IReadOnlyList<CompareRow> rows)
        {
            StringBuilder sb = new();
            sb.AppendLine("mid,dll,expected,ref_method,actual,csharp_method,slot0_offsets,match");
            foreach (CompareRow r in rows)
                sb.AppendLine($"{r.Mid},{r.Dll},{r.Expected},{r.RefMethod},{r.Actual},{r.CSharpMethod},{EscapeCsv(r.Slot0Offsets)},{r.Match}");
            File.WriteAllText(path, sb.ToString());
        }

        static void WriteMismatchCsv(string path, IReadOnlyList<CompareRow> rows)
        {
            StringBuilder sb = new();
            sb.AppendLine("mid,dll,expected,ref_method,actual,csharp_method,slot0_offsets");
            foreach (CompareRow r in rows)
                sb.AppendLine($"{r.Mid},{r.Dll},{r.Expected},{r.RefMethod},{r.Actual},{r.CSharpMethod},{EscapeCsv(r.Slot0Offsets)}");
            File.WriteAllText(path, sb.ToString());
        }

        static void WriteSummary(string path, int total, int match, int mismatch, int missing, int errors, IReadOnlyList<CompareRow> rows)
        {
            StringBuilder sb = new();
            sb.AppendLine("# Magic DLL Family Classifier RT2");
            sb.AppendLine();
            sb.AppendLine($"- total: {total}");
            sb.AppendLine($"- match: {match}");
            sb.AppendLine($"- mismatch: {mismatch}");
            sb.AppendLine($"- missing: {missing}");
            sb.AppendLine($"- errors: {errors}");
            sb.AppendLine();
            sb.AppendLine("## Family counts (C# actual)");
            foreach (IGrouping<string, CompareRow> g in rows.GroupBy(r => r.Actual).OrderBy(g => g.Key, StringComparer.Ordinal))
                sb.AppendLine($"- {g.Key}: {g.Count()}");
            sb.AppendLine();
            if (mismatch > 0)
            {
                sb.AppendLine("## Top drifts");
                foreach (IGrouping<string, CompareRow> g in rows.Where(r => !r.Match).GroupBy(r => $"{r.Expected}->{r.Actual}").OrderByDescending(g => g.Count()))
                    sb.AppendLine($"- {g.Key}: {g.Count()}");
            }
            File.WriteAllText(path, sb.ToString());
        }

        static string EscapeCsv(string value)
        {
            if (value.Contains(',') || value.Contains('"'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        sealed record CsvRow(string Mid, string Dll, string Family, string HostOffsets, string Method);
        sealed record CompareRow(string Mid, string Dll, string Expected, string RefMethod, string Actual, string CSharpMethod, string Slot0Offsets, bool Match);
    }
}
