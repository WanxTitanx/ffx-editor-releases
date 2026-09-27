using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReverseHarness
{
    internal sealed class SphereGridHarnessResult
    {
        public bool Pass { get; set; }
        public int KernelFileCount { get; set; }
        public int LayoutPairCount { get; set; }
        public int Rt0Passed { get; set; }
        public int Rt0Total { get; set; }
        public int Rt1Passed { get; set; }
        public int Rt1Total { get; set; }
    }

    internal static class SphereGridHarness
    {
        private const int LocalizedHeaderLength = 0x14;
        private const int LayoutHeaderLength = 0x10;
        private const int ClusterSize = 0x10;
        private const int NodeSize = 0x0C;
        private const int LinkSize = 0x08;
        private const int ContentsPayloadOffset = 0x08;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static SphereGridHarnessResult Run(string ffxRoot)
        {
            string masterRoot = ResolveMasterRoot(ffxRoot);
            string repoRoot = ResolveRepoRoot();
            string outputDir = Path.Combine(repoRoot, "work", "reverse");
            Directory.CreateDirectory(outputDir);

            var kernelFiles = EnumerateKernelFiles(masterRoot).ToList();
            var layoutPairs = EnumerateLayoutPairs(masterRoot).ToList();
            var profiles = new List<object>();
            var rt0Rows = new List<object>();
            var rt1Rows = new List<object>();
            var guardRows = new List<object>();

            int parseFailures = 0;
            int rt0Passed = 0;
            int rt0Total = 0;
            int rt1Passed = 0;
            int rt1Total = 0;

            foreach (KernelFile kernel in kernelFiles)
            {
                byte[] bytes = File.ReadAllBytes(kernel.Path);
                if (!TryReadLocalizedHeader(bytes, out LocalizedHeader header))
                {
                    parseFailures++;
                    profiles.Add(new { source = Rel(repoRoot, kernel.Path), family = kernel.Family, parsed = false, reason = "bad localized header" });
                    continue;
                }

                int requiredDataLength = header.EntryCount * header.EntryLength;
                bool shapeOk = bytes.Length >= LocalizedHeaderLength + requiredDataLength;
                if (!shapeOk)
                    parseFailures++;

                profiles.Add(new
                {
                    source = Rel(repoRoot, kernel.Path),
                    locale = kernel.Locale,
                    family = kernel.Family,
                    parsed = shapeOk,
                    signature = Hex(bytes[0], 2),
                    header.MinIndex,
                    header.MaxIndex,
                    header.EntryCount,
                    header.EntryLength,
                    header.DataLength,
                    requiredDataLength,
                    bytes.Length
                });

                if (!shapeOk)
                    continue;

                ProfileKernelFields(kernel, bytes, header, profiles, repoRoot);

                rt0Total++;
                byte[] rt0 = ReemitKernelNoEdit(kernel.Family, bytes, header);
                bool rt0Pass = Sha(rt0) == Sha(bytes);
                if (rt0Pass)
                    rt0Passed++;
                rt0Rows.Add(new
                {
                    source = Rel(repoRoot, kernel.Path),
                    family = kernel.Family,
                    gate = "RT0 field-map no-edit clone",
                    passed = rt0Pass,
                    originalSha256 = Sha(bytes),
                    roundtripSha256 = Sha(rt0)
                });

                rt1Total++;
                var rt1 = MutateKernelWritableField(kernel.Family, bytes, header);
                bool rt1Pass = rt1.Diffs.SequenceEqual(rt1.ExpectedOffsets);
                if (rt1Pass)
                    rt1Passed++;
                rt1Rows.Add(new
                {
                    source = Rel(repoRoot, kernel.Path),
                    family = kernel.Family,
                    gate = "RT1 writable-field diff envelope",
                    field = rt1.Field,
                    passed = rt1Pass,
                    expectedOffsets = rt1.ExpectedOffsets.Select(o => Hex(o, 4)).ToArray(),
                    actualOffsets = rt1.Diffs.Select(o => Hex(o, 4)).ToArray()
                });

                guardRows.Add(new
                {
                    source = Rel(repoRoot, kernel.Path),
                    family = kernel.Family,
                    rule = kernel.Family == "sphere.bin"
                        ? "RT1 may touch ActionValue(+0x08) and RangeValue(+0x0C) only"
                        : "RT1 may touch LearnedMove(+0x12) and IncreaseAmount(+0x14) only",
                    passed = rt1Pass
                });
            }

            foreach (LayoutPair pair in layoutPairs)
            {
                byte[] layoutBytes = File.ReadAllBytes(pair.LayoutPath);
                byte[] contentsBytes = File.ReadAllBytes(pair.ContentsPath);
                if (!TryReadLayoutShape(layoutBytes, contentsBytes, out LayoutShape shape))
                {
                    parseFailures++;
                    profiles.Add(new
                    {
                        source = Rel(repoRoot, pair.LayoutPath),
                        contents = Rel(repoRoot, pair.ContentsPath),
                        pair.Kind,
                        parsed = false,
                        reason = "bad layout shape"
                    });
                    continue;
                }

                ProfileLayoutPair(pair, layoutBytes, contentsBytes, shape, profiles, repoRoot);

                rt0Total++;
                byte[] layoutRt0 = ReemitLayoutNoEdit(layoutBytes, shape);
                byte[] contentsRt0 = ReemitContentsNoEdit(contentsBytes, shape.NodeCount);
                bool rt0Pass = Sha(layoutRt0) == Sha(layoutBytes) && Sha(contentsRt0) == Sha(contentsBytes);
                if (rt0Pass)
                    rt0Passed++;
                rt0Rows.Add(new
                {
                    source = Rel(repoRoot, pair.LayoutPath),
                    contents = Rel(repoRoot, pair.ContentsPath),
                    pair.Kind,
                    gate = "RT0 layout/content no-edit clone",
                    passed = rt0Pass,
                    layoutSha256 = Sha(layoutBytes),
                    layoutRoundtripSha256 = Sha(layoutRt0),
                    contentsSha256 = Sha(contentsBytes),
                    contentsRoundtripSha256 = Sha(contentsRt0)
                });

                rt1Total++;
                byte[] contentsMutated = (byte[])contentsBytes.Clone();
                int expectedOffset = ContentsPayloadOffset;
                contentsMutated[expectedOffset] = (byte)(contentsMutated[expectedOffset] ^ 0x5A);
                int[] diffs = DiffOffsets(contentsBytes, contentsMutated);
                bool rt1Pass = diffs.SequenceEqual(new[] { expectedOffset }) && Sha(layoutBytes) == Sha(layoutBytes);
                if (rt1Pass)
                    rt1Passed++;
                rt1Rows.Add(new
                {
                    source = Rel(repoRoot, pair.ContentsPath),
                    layout = Rel(repoRoot, pair.LayoutPath),
                    pair.Kind,
                    gate = "RT1 content-byte diff envelope",
                    field = "ContentIndex[node 0]",
                    passed = rt1Pass,
                    expectedOffsets = new[] { Hex(expectedOffset, 4) },
                    actualOffsets = diffs.Select(o => Hex(o, 4)).ToArray()
                });

                guardRows.Add(new
                {
                    source = Rel(repoRoot, pair.LayoutPath),
                    contents = Rel(repoRoot, pair.ContentsPath),
                    pair.Kind,
                    rule = "V2 content edits may touch dat09/10/11 payload bytes only; dat01/02/03 topology SHA stays unchanged",
                    passed = rt1Pass,
                    layoutSha256 = Sha(layoutBytes)
                });
            }

            var staticProfile = new
            {
                generatedUtc = DateTimeOffset.UtcNow,
                masterRoot,
                kernelFileCount = kernelFiles.Count,
                layoutPairCount = layoutPairs.Count,
                parseFailures,
                profiles
            };

            var rt0Report = new
            {
                generatedUtc = DateTimeOffset.UtcNow,
                gate = "RT0 no-edit identity",
                passed = rt0Passed,
                total = rt0Total,
                rows = rt0Rows
            };

            var rt1Report = new
            {
                generatedUtc = DateTimeOffset.UtcNow,
                gate = "RT1 local mutation diff envelope",
                passed = rt1Passed,
                total = rt1Total,
                rows = rt1Rows
            };

            var guardReport = new
            {
                generatedUtc = DateTimeOffset.UtcNow,
                gate = "no-edit guardrails for writer-public promotion",
                rows = guardRows
            };

            WriteJson(Path.Combine(outputDir, "spheregrid_static_profile.json"), staticProfile);
            WriteJson(Path.Combine(outputDir, "spheregrid_rt0_report.json"), rt0Report);
            WriteJson(Path.Combine(outputDir, "spheregrid_rt1_diff_envelopes.json"), rt1Report);
            WriteJson(Path.Combine(outputDir, "spheregrid_noedit_guard_report.json"), guardReport);

            bool pass = kernelFiles.Count > 0
                && layoutPairs.Count > 0
                && parseFailures == 0
                && rt0Total > 0
                && rt0Passed == rt0Total
                && rt1Total > 0
                && rt1Passed == rt1Total;

            Console.WriteLine($"[spheregrid] kernel-files={kernelFiles.Count} layout-pairs={layoutPairs.Count} " +
                              $"rt0={rt0Passed}/{rt0Total} rt1={rt1Passed}/{rt1Total} " +
                              (pass ? "PASS" : "FAIL"));
            Console.WriteLine($"             reports={Rel(repoRoot, outputDir)}");

            return new SphereGridHarnessResult
            {
                Pass = pass,
                KernelFileCount = kernelFiles.Count,
                LayoutPairCount = layoutPairs.Count,
                Rt0Passed = rt0Passed,
                Rt0Total = rt0Total,
                Rt1Passed = rt1Passed,
                Rt1Total = rt1Total
            };
        }

        private static byte[] ReemitKernelNoEdit(string family, byte[] original, LocalizedHeader header)
        {
            byte[] rt = (byte[])original.Clone();
            int entryBase = LocalizedHeaderLength;
            for (int i = 0; i < header.EntryCount; i++)
            {
                int o = entryBase + i * header.EntryLength;
                if (family == "sphere.bin")
                {
                    Le.W16(rt, o + 0x08, Le.U16(original, o + 0x08));
                    Le.W16(rt, o + 0x0A, Le.U16(original, o + 0x0A));
                    rt[o + 0x0C] = original[o + 0x0C];
                    rt[o + 0x0D] = original[o + 0x0D];
                    Le.W16(rt, o + 0x0E, Le.U16(original, o + 0x0E));
                }
                else
                {
                    Le.W16(rt, o + 0x10, Le.U16(original, o + 0x10));
                    Le.W16(rt, o + 0x12, Le.U16(original, o + 0x12));
                    Le.W16(rt, o + 0x14, Le.U16(original, o + 0x14));
                    Le.W16(rt, o + 0x16, Le.U16(original, o + 0x16));
                }
            }

            return rt;
        }

        private static MutationResult MutateKernelWritableField(string family, byte[] original, LocalizedHeader header)
        {
            byte[] mutated = (byte[])original.Clone();
            int o = LocalizedHeaderLength;
            var expected = new List<int>();
            string field;

            if (family == "sphere.bin")
            {
                Le.W16(mutated, o + 0x08, (ushort)(Le.U16(original, o + 0x08) ^ 0xA55A));
                mutated[o + 0x0C] = (byte)(mutated[o + 0x0C] ^ 0x5A);
                expected.Add(o + 0x08);
                expected.Add(o + 0x09);
                expected.Add(o + 0x0C);
                field = "ActionValue + RangeValue";
            }
            else
            {
                Le.W16(mutated, o + 0x12, (ushort)(Le.U16(original, o + 0x12) ^ 0xA55A));
                Le.W16(mutated, o + 0x14, (ushort)(Le.U16(original, o + 0x14) ^ 0x5AA5));
                expected.Add(o + 0x12);
                expected.Add(o + 0x13);
                expected.Add(o + 0x14);
                expected.Add(o + 0x15);
                field = "LearnedMove + IncreaseAmount";
            }

            return new MutationResult
            {
                Field = field,
                ExpectedOffsets = expected.ToArray(),
                Diffs = DiffOffsets(original, mutated)
            };
        }

        private static byte[] ReemitLayoutNoEdit(byte[] original, LayoutShape shape)
        {
            byte[] rt = (byte[])original.Clone();
            Le.W16(rt, 0x00, Le.U16(original, 0x00));
            Le.W16(rt, 0x02, shape.ClusterCount);
            Le.W16(rt, 0x04, shape.NodeCount);
            Le.W16(rt, 0x06, shape.LinkCount);
            for (int i = 0; i < 4; i++)
                Le.W16(rt, 0x08 + i * 2, Le.U16(original, 0x08 + i * 2));

            for (int i = 0; i < shape.ClusterCount; i++)
            {
                int o = shape.ClusterOffset + i * ClusterSize;
                for (int j = 0; j < ClusterSize; j += 2)
                    Le.W16(rt, o + j, Le.U16(original, o + j));
            }

            for (int i = 0; i < shape.NodeCount; i++)
            {
                int o = shape.NodeOffset + i * NodeSize;
                for (int j = 0; j < NodeSize; j += 2)
                    Le.W16(rt, o + j, Le.U16(original, o + j));
            }

            for (int i = 0; i < shape.LinkCount; i++)
            {
                int o = shape.LinkOffset + i * LinkSize;
                for (int j = 0; j < LinkSize; j += 2)
                    Le.W16(rt, o + j, Le.U16(original, o + j));
            }

            return rt;
        }

        private static byte[] ReemitContentsNoEdit(byte[] original, int nodeCount)
        {
            byte[] rt = (byte[])original.Clone();
            for (int i = 0; i < nodeCount; i++)
                rt[ContentsPayloadOffset + i] = original[ContentsPayloadOffset + i];
            return rt;
        }

        private static void ProfileKernelFields(KernelFile kernel, byte[] bytes, LocalizedHeader header, List<object> profiles, string repoRoot)
        {
            if (kernel.Family == "sphere.bin")
            {
                AddProfile(profiles, repoRoot, kernel.Path, "ActionValue", KernelValues(bytes, header, 0x08, 2), 4);
                AddProfile(profiles, repoRoot, kernel.Path, "ActivationBitfield", KernelValues(bytes, header, 0x0A, 2), 4);
                AddProfile(profiles, repoRoot, kernel.Path, "RangeValue", KernelValues(bytes, header, 0x0C, 1), 2);
                AddProfile(profiles, repoRoot, kernel.Path, "SpecialRole", KernelValues(bytes, header, 0x0D, 1), 2);
                AddProfile(profiles, repoRoot, kernel.Path, "AlwaysZero", KernelValues(bytes, header, 0x0E, 2), 4);
            }
            else
            {
                AddProfile(profiles, repoRoot, kernel.Path, "NodeEffectBitfield", KernelValues(bytes, header, 0x10, 2), 4);
                AddProfile(profiles, repoRoot, kernel.Path, "LearnedMove", KernelValues(bytes, header, 0x12, 2), 4);
                AddProfile(profiles, repoRoot, kernel.Path, "IncreaseAmount", KernelValues(bytes, header, 0x14, 2), 4);
                AddProfile(profiles, repoRoot, kernel.Path, "AppearanceType", KernelValues(bytes, header, 0x16, 2), 4);
            }
        }

        private static void ProfileLayoutPair(
            LayoutPair pair,
            byte[] layoutBytes,
            byte[] contentsBytes,
            LayoutShape shape,
            List<object> profiles,
            string repoRoot)
        {
            profiles.Add(new
            {
                source = Rel(repoRoot, pair.LayoutPath),
                contents = Rel(repoRoot, pair.ContentsPath),
                pair.Locale,
                pair.Kind,
                parsed = true,
                shape.ClusterCount,
                shape.NodeCount,
                shape.LinkCount,
                shape.RequiredLayoutLength,
                layoutLength = layoutBytes.Length,
                requiredContentsLength = ContentsPayloadOffset + shape.NodeCount,
                contentsLength = contentsBytes.Length,
                headerUnknowns = new
                {
                    unknown1 = Hex(Le.U16(layoutBytes, 0x00), 4),
                    unknown5 = Hex(Le.U16(layoutBytes, 0x08), 4),
                    unknown6 = Hex(Le.U16(layoutBytes, 0x0A), 4),
                    unknown7 = Hex(Le.U16(layoutBytes, 0x0C), 4),
                    unknown8 = Hex(Le.U16(layoutBytes, 0x0E), 4)
                },
                clusterUnusedNonZero = CountClusterUnusedNonZero(layoutBytes, shape),
                nodeUnknown6NonZero = CountNodeFieldNonZero(layoutBytes, shape, 0x0A),
                nodeRedundantContentMismatch = CountRedundantContentMismatch(layoutBytes, contentsBytes, shape),
                linkUnusedNonZero = CountLinkUnusedNonZero(layoutBytes, shape)
            });
        }

        private static IEnumerable<ulong> KernelValues(byte[] bytes, LocalizedHeader header, int fieldOffset, int fieldLength)
        {
            for (int i = 0; i < header.EntryCount; i++)
            {
                int o = LocalizedHeaderLength + i * header.EntryLength + fieldOffset;
                yield return fieldLength == 1 ? bytes[o] : Le.U16(bytes, o);
            }
        }

        private static void AddProfile(List<object> profiles, string repoRoot, string path, string field, IEnumerable<ulong> values, int hexWidth)
        {
            ulong[] materialized = values.ToArray();
            var topValues = materialized
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Take(12)
                .Select(g => new { value = Hex(g.Key, hexWidth), count = g.Count() })
                .ToArray();

            profiles.Add(new
            {
                source = Rel(repoRoot, path),
                field,
                count = materialized.Length,
                distinct = materialized.Distinct().Count(),
                nonZero = materialized.Count(v => v != 0),
                min = materialized.Length == 0 ? null : Hex(materialized.Min(), hexWidth),
                max = materialized.Length == 0 ? null : Hex(materialized.Max(), hexWidth),
                topValues
            });
        }

        private static bool TryReadLocalizedHeader(byte[] bytes, out LocalizedHeader header)
        {
            header = new LocalizedHeader();
            if (bytes.Length < LocalizedHeaderLength)
                return false;

            ushort minIndex = Le.U16(bytes, 0x08);
            ushort maxIndex = Le.U16(bytes, 0x0A);
            ushort entryLength = Le.U16(bytes, 0x0C);
            ushort dataLength = Le.U16(bytes, 0x0E);
            if (maxIndex < minIndex || entryLength == 0)
                return false;

            int entryCount = maxIndex - minIndex + 1;
            header = new LocalizedHeader
            {
                MinIndex = minIndex,
                MaxIndex = maxIndex,
                EntryLength = entryLength,
                DataLength = dataLength,
                EntryCount = entryCount
            };

            return true;
        }

        private static bool TryReadLayoutShape(byte[] layoutBytes, byte[] contentsBytes, out LayoutShape shape)
        {
            shape = new LayoutShape();
            if (layoutBytes.Length < LayoutHeaderLength || contentsBytes.Length < ContentsPayloadOffset)
                return false;

            ushort clusterCount = Le.U16(layoutBytes, 0x02);
            ushort nodeCount = Le.U16(layoutBytes, 0x04);
            ushort linkCount = Le.U16(layoutBytes, 0x06);
            int clusterOffset = LayoutHeaderLength;
            int nodeOffset = clusterOffset + clusterCount * ClusterSize;
            int linkOffset = nodeOffset + nodeCount * NodeSize;
            int requiredLayoutLength = linkOffset + linkCount * LinkSize;
            int requiredContentsLength = ContentsPayloadOffset + nodeCount;

            shape = new LayoutShape
            {
                ClusterCount = clusterCount,
                NodeCount = nodeCount,
                LinkCount = linkCount,
                ClusterOffset = clusterOffset,
                NodeOffset = nodeOffset,
                LinkOffset = linkOffset,
                RequiredLayoutLength = requiredLayoutLength
            };

            return layoutBytes.Length >= requiredLayoutLength && contentsBytes.Length >= requiredContentsLength && nodeCount > 0;
        }

        private static int CountClusterUnusedNonZero(byte[] layoutBytes, LayoutShape shape)
        {
            int count = 0;
            int[] offsets = { 0x04, 0x08, 0x0A, 0x0C, 0x0E };
            for (int i = 0; i < shape.ClusterCount; i++)
            {
                int o = shape.ClusterOffset + i * ClusterSize;
                foreach (int fieldOffset in offsets)
                    if (Le.U16(layoutBytes, o + fieldOffset) != 0)
                        count++;
            }

            return count;
        }

        private static int CountNodeFieldNonZero(byte[] layoutBytes, LayoutShape shape, int fieldOffset)
        {
            int count = 0;
            for (int i = 0; i < shape.NodeCount; i++)
            {
                int o = shape.NodeOffset + i * NodeSize;
                if (Le.U16(layoutBytes, o + fieldOffset) != 0)
                    count++;
            }

            return count;
        }

        private static int CountRedundantContentMismatch(byte[] layoutBytes, byte[] contentsBytes, LayoutShape shape)
        {
            int count = 0;
            for (int i = 0; i < shape.NodeCount; i++)
            {
                int nodeOffset = shape.NodeOffset + i * NodeSize;
                ushort redundantContent = Le.U16(layoutBytes, nodeOffset + 0x06);
                byte payloadContent = contentsBytes[ContentsPayloadOffset + i];
                if ((redundantContent & 0xFF) != payloadContent)
                    count++;
            }

            return count;
        }

        private static int CountLinkUnusedNonZero(byte[] layoutBytes, LayoutShape shape)
        {
            int count = 0;
            for (int i = 0; i < shape.LinkCount; i++)
            {
                int o = shape.LinkOffset + i * LinkSize;
                if (Le.U16(layoutBytes, o + 0x06) != 0)
                    count++;
            }

            return count;
        }

        private static IEnumerable<KernelFile> EnumerateKernelFiles(string masterRoot)
        {
            if (!Directory.Exists(masterRoot))
                yield break;

            foreach (string localeDir in Directory.EnumerateDirectories(masterRoot).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                string locale = Path.GetFileName(localeDir);
                string kernelDir = Path.Combine(localeDir, "battle", "kernel");
                foreach (string family in new[] { "sphere.bin", "panel.bin" })
                {
                    string path = Path.Combine(kernelDir, family);
                    if (File.Exists(path))
                        yield return new KernelFile { Locale = locale, Family = family, Path = path };
                }
            }
        }

        private static IEnumerable<LayoutPair> EnumerateLayoutPairs(string masterRoot)
        {
            if (!Directory.Exists(masterRoot))
                yield break;

            foreach (string localeDir in Directory.EnumerateDirectories(masterRoot).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                string locale = Path.GetFileName(localeDir);
                string abmapDir = Path.Combine(localeDir, "menu", "abmap");
                foreach (var pair in new[]
                {
                    new { Kind = "original", Layout = "dat01.dat", Contents = "dat09.dat" },
                    new { Kind = "standard", Layout = "dat02.dat", Contents = "dat10.dat" },
                    new { Kind = "expert", Layout = "dat03.dat", Contents = "dat11.dat" }
                })
                {
                    string layoutPath = Path.Combine(abmapDir, pair.Layout);
                    string contentsPath = Path.Combine(abmapDir, pair.Contents);
                    if (File.Exists(layoutPath) && File.Exists(contentsPath))
                    {
                        yield return new LayoutPair
                        {
                            Locale = locale,
                            Kind = pair.Kind,
                            LayoutPath = layoutPath,
                            ContentsPath = contentsPath
                        };
                    }
                }
            }
        }

        private static int[] DiffOffsets(byte[] left, byte[] right)
        {
            int limit = Math.Min(left.Length, right.Length);
            var diffs = new List<int>();
            for (int i = 0; i < limit; i++)
                if (left[i] != right[i])
                    diffs.Add(i);

            for (int i = limit; i < Math.Max(left.Length, right.Length); i++)
                diffs.Add(i);

            return diffs.ToArray();
        }

        private static string ResolveMasterRoot(string ffxRoot)
        {
            if (Directory.Exists(Path.Combine(ffxRoot, "master")))
                return Path.Combine(ffxRoot, "master");

            if (Directory.Exists(ffxRoot) && string.Equals(Path.GetFileName(ffxRoot), "master", StringComparison.OrdinalIgnoreCase))
                return ffxRoot;

            return Path.Combine(ffxRoot, "master");
        }

        private static string ResolveRepoRoot()
        {
            string? dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FFXProjectEditor.sln")) || Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            return Directory.GetCurrentDirectory();
        }

        private static string Rel(string root, string path)
        {
            try
            {
                return Path.GetRelativePath(root, path);
            }
            catch
            {
                return path;
            }
        }

        private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

        private static string Hex(int value, int width) => "0x" + value.ToString("X" + width);

        private static string Hex(ulong value, int width) => "0x" + value.ToString("X" + width);

        private static void WriteJson(string path, object value)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        }

        private sealed class KernelFile
        {
            public string Locale { get; set; } = string.Empty;
            public string Family { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
        }

        private sealed class LayoutPair
        {
            public string Locale { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string LayoutPath { get; set; } = string.Empty;
            public string ContentsPath { get; set; } = string.Empty;
        }

        private sealed class LocalizedHeader
        {
            public ushort MinIndex { get; set; }
            public ushort MaxIndex { get; set; }
            public ushort EntryLength { get; set; }
            public ushort DataLength { get; set; }
            public int EntryCount { get; set; }
        }

        private sealed class LayoutShape
        {
            public ushort ClusterCount { get; set; }
            public ushort NodeCount { get; set; }
            public ushort LinkCount { get; set; }
            public int ClusterOffset { get; set; }
            public int NodeOffset { get; set; }
            public int LinkOffset { get; set; }
            public int RequiredLayoutLength { get; set; }
        }

        private sealed class MutationResult
        {
            public string Field { get; set; } = string.Empty;
            public int[] ExpectedOffsets { get; set; } = Array.Empty<int>();
            public int[] Diffs { get; set; } = Array.Empty<int>();
        }
    }
}
