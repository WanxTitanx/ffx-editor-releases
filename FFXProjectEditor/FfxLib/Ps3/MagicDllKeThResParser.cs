using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Parse Family D PPP KeThRes opcode catalog from <c>magic_####.dll</c> and correlate PS3 phyre sheets.
    /// Static <c>Thundaga_KeThResBlob</c> buffers are zero in the PE — runtime fill via host+2840 (IDA 0094).
    /// </summary>
    internal static class MagicDllKeThResParser
    {
        public const uint PppEntryMagic = 0x55D4456F;
        public const uint ImageBase = 0x10000000;
        public const int FullEntryHeaderBytes = 0x24;

        static readonly Regex KeThResNameRegex = new(
            @"^pppKeThRes(?<base>\d+)(?:x(?<pack>\d+))?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public sealed record PppOpcodeEntry(
            int FileOffset,
            string RvaHex,
            uint TypeId,
            uint StructSize,
            string DataRvaAHex,
            string DataRvaBHex,
            string HandlerAHex,
            string HandlerBHex,
            string Name,
            bool HasInlineName,
            string StaticDataNote);

        public sealed record KeThResResourceSpec(
            string Name,
            int? TileWidth,
            int? PackHeight,
            PppOpcodeEntry Entry);

        public sealed record PhyreMatch(
            string FileName,
            string RelativePath,
            int NameWidth,
            int NameHeight,
            int? DecodedWidth,
            int? DecodedHeight,
            string Format,
            int Score,
            string Note);

        public sealed record MagicDllKeThResParseResult(
            int MagicId,
            string DllPath,
            string Family,
            IReadOnlyList<PppOpcodeEntry> Opcodes,
            IReadOnlyList<KeThResResourceSpec> KeThResResources,
            KeThResResourceSpec? PrimaryBoltResource,
            IReadOnlyList<PhyreMatch> PhyreMatches,
            IReadOnlyList<string> Notes);

        public static MagicDllKeThResParseResult Parse(
            MagicDllInspection inspection,
            string? ps3MagicRoot = null,
            int maxPhyreMatches = 16)
        {
            byte[] bytes = File.ReadAllBytes(inspection.FilePath);
            MagicDllFamilyClassification family = MagicDllFamilyClassifier.Classify(inspection);
            IReadOnlyList<PppOpcodeEntry> opcodes = ScanPppOpcodeEntries(bytes, inspection);
            IReadOnlyList<KeThResResourceSpec> keTh = BuildKeThResSpecs(opcodes);
            KeThResResourceSpec? primary = keTh.FirstOrDefault(r =>
                r.Name.Equals("pppKeThRes32x4", StringComparison.OrdinalIgnoreCase));

            IReadOnlyList<PhyreMatch> phyre = ScorePhyreMatches(
                inspection.MagicId ?? 0,
                ps3MagicRoot,
                primary ?? keTh.FirstOrDefault(),
                maxPhyreMatches);

            var notes = new List<string>
            {
                "PPP entry magic 0x55D4456F — type 0x0C entries carry inline opcode names + handler RVAs.",
                "dataRvaA/B regions are zero/BSS in the on-disk PE; host+2840 fills Thundaga_KeThResBlob at runtime.",
                "Bolt draw bind: Thundaga_PppKeThResBind_0094 (host+2856) — see FFX_THUNDAFIRA_KETHRES_DRAW_PATH_IDA_2026-06-14.md.",
            };

            if (phyre.Count == 0)
                notes.Add("No PS3 phyre folder found — pass --ps3-root or deploy magic_#### tex pack.");

            return new MagicDllKeThResParseResult(
                inspection.MagicId ?? 0,
                inspection.FilePath,
                family.Family.ToString(),
                opcodes,
                keTh,
                primary,
                phyre,
                notes);
        }

        public static string BuildMarkdown(MagicDllKeThResParseResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# KeThRes PPP parse — `magic_{r.MagicId:D4}.dll`");
            sb.AppendLine();
            sb.AppendLine($"- **Family:** {r.Family}");
            sb.AppendLine($"- **DLL:** `{r.DllPath}`");
            sb.AppendLine($"- **PPP opcodes (magic entries):** {r.Opcodes.Count}");
            sb.AppendLine($"- **KeThRes resources:** {r.KeThResResources.Count}");
            sb.AppendLine();

            if (r.PrimaryBoltResource != null)
            {
                KeThResResourceSpec p = r.PrimaryBoltResource;
                sb.AppendLine("## Primary bolt resource (`pppKeThRes32x4`)");
                sb.AppendLine();
                sb.AppendLine($"| Field | Value |");
                sb.AppendLine($"|-------|-------|");
                sb.AppendLine($"| Tile hint | {p.TileWidth}x{p.PackHeight} |");
                sb.AppendLine($"| TypeId | `0x{p.Entry.TypeId:X}` |");
                sb.AppendLine($"| StructSize | `{p.Entry.StructSize}` |");
                sb.AppendLine($"| dataA | `{p.Entry.DataRvaAHex}` |");
                sb.AppendLine($"| dataB | `{p.Entry.DataRvaBHex}` |");
                sb.AppendLine($"| handlerA | `{p.Entry.HandlerAHex}` |");
                sb.AppendLine($"| handlerB | `{p.Entry.HandlerBHex}` |");
                sb.AppendLine($"| static | {p.Entry.StaticDataNote} |");
                sb.AppendLine();
            }

            sb.AppendLine("## KeThRes catalog");
            sb.AppendLine();
            sb.AppendLine("| Name | Tile | Type | dataA | handlers |");
            sb.AppendLine("|------|------|------|-------|----------|");
            foreach (KeThResResourceSpec k in r.KeThResResources)
            {
                sb.AppendLine($"| `{k.Name}` | {k.TileWidth}x{k.PackHeight} | `0x{k.Entry.TypeId:X}` | `{k.Entry.DataRvaAHex}` | `{k.Entry.HandlerAHex}` |");
            }

            sb.AppendLine();
            sb.AppendLine("## PS3 phyre matches (scored)");
            sb.AppendLine();
            if (r.PhyreMatches.Count == 0)
            {
                sb.AppendLine("_No phyre scored — missing PS3 folder._");
            }
            else
            {
                sb.AppendLine("| Score | File | Name dims | Decoded | Format | Note |");
                sb.AppendLine("|------:|------|-----------|---------|--------|------|");
                foreach (PhyreMatch m in r.PhyreMatches)
                {
                    string nameDims = m.NameWidth > 0 ? $"{m.NameWidth}x{m.NameHeight}" : "?";
                    string decoded = m.DecodedWidth is > 0 ? $"{m.DecodedWidth}x{m.DecodedHeight}" : "-";
                    sb.AppendLine($"| {m.Score} | `{m.FileName}` | {nameDims} | {decoded} | {m.Format} | {m.Note} |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine();
            foreach (string n in r.Notes)
                sb.AppendLine($"- {n}");

            return sb.ToString();
        }

        static IReadOnlyList<PppOpcodeEntry> ScanPppOpcodeEntries(byte[] bytes, MagicDllInspection inspection)
        {
            var list = new List<PppOpcodeEntry>();
            byte[] magic = BitConverter.GetBytes(PppEntryMagic);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int offset = 0; offset <= bytes.Length - 28; offset++)
            {
                if (!MatchMagic(bytes, offset, magic))
                    continue;

                uint typeId = BitConverter.ToUInt32(bytes, offset + 8);
                uint structSize = BitConverter.ToUInt32(bytes, offset + 12);
                uint dataA = BitConverter.ToUInt32(bytes, offset + 16);
                uint dataB = BitConverter.ToUInt32(bytes, offset + 20);

                if (typeId != 0x0C || structSize != 0x10 || offset + FullEntryHeaderBytes >= bytes.Length)
                    continue;

                uint hA = BitConverter.ToUInt32(bytes, offset + 0x1C);
                uint hB = BitConverter.ToUInt32(bytes, offset + 0x20);
                string staticNote = DescribeStaticData(bytes, inspection, dataA, dataB);

                int nameOffset = offset + FullEntryHeaderBytes;
                while (nameOffset < bytes.Length)
                {
                    string name = ReadAsciiZ(bytes, nameOffset, 64);
                    if (string.IsNullOrEmpty(name) || !name.StartsWith("ppp", StringComparison.Ordinal))
                        break;

                    if (seenNames.Add(name))
                    {
                        list.Add(new PppOpcodeEntry(
                            nameOffset,
                            TryFormatRva(inspection, nameOffset),
                            typeId,
                            structSize,
                            FormatVa(dataA),
                            FormatVa(dataB),
                            FormatVa(hA),
                            FormatVa(hB),
                            name,
                            true,
                            staticNote));
                    }

                    nameOffset += name.Length + 1;
                }
            }

            // Fallback: strings in PE scan without inline opcode header (labels only).
            foreach (MagicDllAsciiString s in inspection.Strings)
            {
                if (!s.Value.StartsWith("pppKeThRes", StringComparison.Ordinal))
                    continue;
                if (!seenNames.Add(s.Value))
                    continue;

                list.Add(new PppOpcodeEntry(
                    s.FileOffset,
                    $"0x{s.Rva + (int)ImageBase:X}",
                    0,
                    0,
                    "-",
                    "-",
                    "-",
                    "-",
                    s.Value,
                    false,
                    "string table only — shares runtime host+2844 load"));
            }

            return list.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        }

        static IReadOnlyList<KeThResResourceSpec> BuildKeThResSpecs(IReadOnlyList<PppOpcodeEntry> opcodes)
        {
            PppOpcodeEntry? sharedEntry = opcodes.FirstOrDefault(o =>
                o.Name.Equals("pppKeThRes32x4", StringComparison.OrdinalIgnoreCase) && o.HasInlineName);

            var list = new List<KeThResResourceSpec>();
            foreach (PppOpcodeEntry entry in opcodes)
            {
                if (!entry.Name.StartsWith("pppKeThRes", StringComparison.OrdinalIgnoreCase))
                    continue;

                PppOpcodeEntry effective = entry.HasInlineName ? entry : sharedEntry ?? entry;
                Match m = KeThResNameRegex.Match(entry.Name);
                int? tile = null;
                int? pack = null;
                if (m.Success)
                {
                    tile = int.Parse(m.Groups["base"].Value, CultureInfo.InvariantCulture);
                    if (m.Groups["pack"].Success)
                        pack = int.Parse(m.Groups["pack"].Value, CultureInfo.InvariantCulture);
                }

                list.Add(new KeThResResourceSpec(entry.Name, tile, pack, effective));
            }

            return list;
        }

        static IReadOnlyList<PhyreMatch> ScorePhyreMatches(
            int magicId,
            string? ps3MagicRoot,
            KeThResResourceSpec? primary,
            int maxResults)
        {
            if (magicId <= 0 || primary == null)
                return [];

            string folder = ResolvePs3Folder(magicId, ps3MagicRoot);
            if (!Directory.Exists(folder))
                return [];

            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*.dds.phyre", SearchOption.AllDirectories);
            }
            catch
            {
                return [];
            }

            var matches = new List<PhyreMatch>();
            foreach (string path in files)
            {
                string fileName = Path.GetFileName(path);
                (int nameW, int nameH) = ParseNameDims(fileName);
                int? decW = null, decH = null;
                string format = "?";
                string decodeNote = "not decoded";

                try
                {
                    Ps3PhyreDecode decode = Ps3MagicTextureReader.Decode(path);
                    if (decode.Width > 0 && decode.Height > 0)
                    {
                        decW = decode.Width;
                        decH = decode.Height;
                        format = decode.Format;
                        decodeNote = decode.State.ToString();
                    }
                }
                catch
                {
                    decodeNote = "decode error";
                }

                int score = ScorePhyre(primary, fileName, nameW, nameH, decW, decH);
                if (score <= 0)
                    continue;

                matches.Add(new PhyreMatch(
                    fileName,
                    Path.GetRelativePath(folder, path),
                    nameW,
                    nameH,
                    decW,
                    decH,
                    format,
                    score,
                    BuildPhyreNote(primary, nameW, nameH, decW, decH, decodeNote)));
            }

            return matches
                .OrderByDescending(m => m.Score)
                .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToList();
        }

        static int ScorePhyre(
            KeThResResourceSpec primary,
            string fileName,
            int nameW,
            int nameH,
            int? decW,
            int? decH)
        {
            int score = 0;
            int tile = primary.TileWidth ?? 0;
            int pack = primary.PackHeight ?? 0;

            // KeThRes32x4 — atlas hypotheses (EgoSeInit uses 256x64 in IDA)
            if (tile == 32 && pack == 4)
            {
                if (nameW == 128 && nameH == 64) score += 70;
                if (nameW == 128 && nameH == 128) score += 40;
                if (nameW == 256 && nameH == 128) score += 35;
                if (decW == 128 && decH == 64) score += 50;
                if (decW == 32 && decH == 4) score += 90;
                if (fileName.Contains("128_64", StringComparison.Ordinal)) score += 30;
            }

            if (tile > 0 && (nameW == tile || decW == tile))
                score += 20;
            if (pack > 0 && (nameH == pack || decH == pack))
                score += 15;

            // Penalize known non-bolt sheets from RT2
            if (fileName.Contains("128_128", StringComparison.Ordinal)) score -= 40;
            if (fileName.Contains("512_256", StringComparison.Ordinal)) score -= 25;

            return score;
        }

        static string BuildPhyreNote(
            KeThResResourceSpec primary,
            int nameW,
            int nameH,
            int? decW,
            int? decH,
            string decodeNote)
        {
            if (nameW == 128 && nameH == 128)
                return "RT2: ground flash, not bolts";
            if (nameW == 128 && nameH == 64)
                return $"Anim1-only candidate for {primary.Name}";
            if (decW == 32 && decH == 4)
                return "exact 32x4 decoded — top tint target";
            return decodeNote;
        }

        static string DescribeStaticData(byte[] bytes, MagicDllInspection inspection, uint dataA, uint dataB)
        {
            bool aZero = IsRegionZeroOrUnmapped(bytes, inspection, dataA, 64);
            bool bZero = IsRegionZeroOrUnmapped(bytes, inspection, dataB, 64);
            if (aZero && bZero)
                return "runtime/BSS (zero on disk)";
            if (aZero)
                return "dataA runtime; dataB has static bytes";
            if (bZero)
                return "dataB runtime; dataA has static bytes";
            return "both regions have static bytes";
        }

        static bool IsRegionZeroOrUnmapped(byte[] bytes, MagicDllInspection inspection, uint rvaOrVa, int len)
        {
            try
            {
                int off = inspection.RvaToFileOffset(NormalizeRva(rvaOrVa));
                if (off < 0 || off + len > bytes.Length)
                    return true;

                for (int i = 0; i < len; i++)
                {
                    if (bytes[off + i] != 0)
                        return false;
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        static int NormalizeRva(uint rvaOrVa)
        {
            if (rvaOrVa >= ImageBase)
                return (int)(rvaOrVa - ImageBase);
            return (int)rvaOrVa;
        }

        static string FormatVa(uint rvaOrVa)
        {
            if (rvaOrVa >= ImageBase)
                return $"0x{rvaOrVa:X}";
            return $"0x{ImageBase + rvaOrVa:X}";
        }

        static string TryFormatRva(MagicDllInspection inspection, int fileOffset)
        {
            try
            {
                MagicDllSection? sec = inspection.Sections.FirstOrDefault(s =>
                    fileOffset >= s.RawPointer && fileOffset < s.RawPointer + s.RawSize);
                if (sec == null)
                    return $"0x{fileOffset:X}";
                int rva = sec.VirtualAddress + (fileOffset - sec.RawPointer);
                return $"0x{rva + (int)ImageBase:X}";
            }
            catch
            {
                return $"0x{fileOffset:X}";
            }
        }

        static string ResolvePs3Folder(int magicId, string? ps3MagicRoot)
        {
            if (!string.IsNullOrWhiteSpace(ps3MagicRoot))
            {
                string direct = Path.Combine(ps3MagicRoot, $"magic_{magicId:D4}");
                if (Directory.Exists(direct))
                    return direct;
                return Path.Combine(ps3MagicRoot, $"magic_{magicId:D4}");
            }

            return MagicDllPs3PhyreResolver.ResolveMagicFolder(magicId, null);
        }

        static (int Width, int Height) ParseNameDims(string fileName)
        {
            string core = fileName;
            int marker = core.IndexOf(".dds.phyre", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
                core = core[..marker];

            string[] parts = core.Split('_');
            if (parts.Length >= 6
                && int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                && int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                && w is > 0 and <= 8192
                && h is > 0 and <= 8192)
            {
                return (w, h);
            }

            return (0, 0);
        }

        static bool MatchMagic(byte[] bytes, int offset, byte[] magic)
        {
            for (int i = 0; i < magic.Length; i++)
            {
                if (bytes[offset + i] != magic[i])
                    return false;
            }

            return true;
        }

        static string ReadAsciiZ(byte[] bytes, int offset, int maxLen)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < maxLen && offset + i < bytes.Length; i++)
            {
                byte b = bytes[offset + i];
                if (b == 0)
                    break;
                if (b < 32 || b > 126)
                    return string.Empty;
                sb.Append((char)b);
            }

            return sb.ToString();
        }
    }
}
