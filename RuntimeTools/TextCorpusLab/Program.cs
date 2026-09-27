using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using System.Text.Json;

namespace TextCorpusLab;

/// <summary>
/// Fase 2 do pipeline de i18n (Jarvis-CLINE, 2026-08-02):
/// extrai TODOS os textos de batalha de TODAS as 8 regioes do jogo para E:\Text\game_{regiao}\.
/// Regioes: uspc/new_uspc (EN), jppc/new_jppc (JA), new_sppc (ES), new_frpc (FR), new_depc (DE),
///          new_itpc (IT), new_krpc (KO), new_chpc (ZH).
/// SAIDA: E:\Text\game_{regiao}\{arquivo}.json -> [{"index":int,"slot":int?,"text":string}]
/// Decoders: latinas = UsDecoder; ja = JpDecoder; ko/zh = UsDecoder (fallback, glyphs viram tokens).
/// </summary>
internal static class Program
{
    private const string CorpusRoot = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
    private const string OutRoot = @"E:\Text";

    private static readonly (string Dir, string Label, Dictionary<byte, char> Decoder)[] Regions =
    [
        ("uspc", "en", FfxEncoding.UsDecoder),
        ("new_uspc", "en_new", FfxEncoding.UsDecoder),
        ("new_sppc", "es", FfxEncoding.UsDecoder),
        ("new_frpc", "fr", FfxEncoding.UsDecoder),
        ("new_depc", "de", FfxEncoding.UsDecoder),
        ("new_itpc", "it", FfxEncoding.UsDecoder),
        ("jppc", "ja", FfxEncoding.JpDecoder),
        ("new_jppc", "ja_new", FfxEncoding.JpDecoder),
        ("new_krpc", "ko", FfxEncoding.UsDecoder),
        ("new_chpc", "zh", FfxEncoding.UsDecoder),
    ];

    private static int Main()
    {
        Console.WriteLine("=== TextCorpusLab — extracao de corpus de texto por regiao ===");
        int totalFiles = 0;
        int totalLines = 0;

        foreach ((string dir, string label, var decoder) in Regions)
        {
            string regionRoot = Path.Combine(CorpusRoot, dir, "battle", "kernel");
            if (!Directory.Exists(regionRoot))
            {
                Console.WriteLine($"[SKIP] {dir} nao existe ({regionRoot})");
                continue;
            }

            string outDir = Path.Combine(OutRoot, $"game_{label}");
            Directory.CreateDirectory(outDir);

            var files = new (string Name, Func<byte[], Dictionary<byte, char>, List<CorpusEntry>> Extractor)[]
            {
                ("btl_txt.bin", ExtractBtlTxt),
                ("name_txt.bin", ExtractNameDesc),
                ("item_txt.bin", ExtractNameDesc),
                ("menu_txt.bin", ExtractNameDesc),
                ("status_txt.bin", ExtractNameDesc),
                ("summon_txt.bin", ExtractNameDesc),
                ("arms_txt.bin", ExtractNameDesc),
                ("mmain_txt.bin", ExtractNameDesc),
                ("save_txt.bin", ExtractNameDesc),
                ("config_txt.bin", ExtractNameDesc),
                ("build_txt.bin", ExtractNameDesc),
                ("btlend_txt.bin", ExtractNameDesc),
                ("w_name.bin", ExtractNameDescPrefix),
            };

            foreach ((string name, var extractor) in files)
            {
                string path = Path.Combine(regionRoot, name);
                if (!File.Exists(path))
                {
                    Console.WriteLine($"[SKIP] {dir}/{name} nao existe");
                    continue;
                }

                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    List<CorpusEntry> entries = extractor(bytes, decoder);
                    string outPath = Path.Combine(outDir, name.Replace(".bin", ".json"));
                    File.WriteAllText(outPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    }));
                    totalFiles++;
                    totalLines += entries.Count;
                    Console.WriteLine($"[OK] {dir}/{name}: {entries.Count} linhas -> {outPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FAIL] {dir}/{name}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"=== TOTAL: {totalFiles} arquivos, {totalLines} linhas extraidas ===");
        return 0;
    }

    private static List<CorpusEntry> ExtractBtlTxt(byte[] bytes, Dictionary<byte, char> decoder)
    {
        var file = BtlTextTable_File.Read(bytes, decoder);
        var result = new List<CorpusEntry>();
        foreach (var entry in file.Entries)
        {
            foreach (var word in entry.Words)
            {
                if (string.IsNullOrWhiteSpace(word.Text))
                    continue;
                result.Add(new CorpusEntry { Index = entry.Index, Slot = word.Slot, Text = word.Text });
            }
        }
        return result;
    }

    private static List<CorpusEntry> ExtractTextTable(byte[] bytes, Dictionary<byte, char> decoder)
    {
        var file = TextTable_File.Read(bytes, decoder);
        var result = new List<CorpusEntry>();
        foreach (var entry in file.Entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.RegularText))
                result.Add(new CorpusEntry { Index = entry.Index, Slot = 0, Text = entry.RegularText });
            if (!string.IsNullOrWhiteSpace(entry.SimplifiedText) && entry.SimplifiedText != entry.RegularText)
                result.Add(new CorpusEntry { Index = entry.Index, Slot = 1, Text = entry.SimplifiedText });
        }
        return result;
    }

    private static List<CorpusEntry> ExtractNameDesc(byte[] bytes, Dictionary<byte, char> decoder)
    {
        var file = NameDescriptionTextTable_File.Read(bytes, decoder);
        var result = new List<CorpusEntry>();
        foreach (var entry in file.Entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.NameText))
                result.Add(new CorpusEntry { Index = entry.Index, Slot = 0, Text = entry.NameText });
            if (!string.IsNullOrWhiteSpace(entry.DescriptionText))
                result.Add(new CorpusEntry { Index = entry.Index, Slot = 1, Text = entry.DescriptionText });
        }
        return result;
    }

    private static List<CorpusEntry> ExtractNameDescPrefix(byte[] bytes, Dictionary<byte, char> decoder)
    {
        // Layout (provado no parser internal NameDescriptionTextPrefixTable_File):
        // header 0x14; entries EntryLength (w_name = 0x48) com nameOffset @ +0x00,
        // descriptionOffset @ +0x08 (offsets relativos ao string pool após o data block).
        int entryLength = bytes[0x0C] | (bytes[0x0D] << 8);
        int dataBlockLength = bytes[0x0E] | (bytes[0x0F] << 8);
        int entryCount = entryLength > 0 ? (dataBlockLength / entryLength) : 0;
        int poolStart = 0x14 + dataBlockLength;
        byte[] pool = bytes[poolStart..];

        var result = new List<CorpusEntry>();
        for (int i = 0; i < entryCount; i++)
        {
            int entryOffset = 0x14 + (i * entryLength);
            if (entryOffset + 0x0A > bytes.Length)
                break;
            ushort nameOff = (ushort)(bytes[entryOffset + 0x00] | (bytes[entryOffset + 0x01] << 8));
            ushort descOff = (ushort)(bytes[entryOffset + 0x08] | (bytes[entryOffset + 0x09] << 8));
            string name = ReadPoolString(pool, nameOff, decoder);
            string desc = ReadPoolString(pool, descOff, decoder);
            if (!string.IsNullOrWhiteSpace(name))
                result.Add(new CorpusEntry { Index = i, Slot = 0, Text = name });
            if (!string.IsNullOrWhiteSpace(desc))
                result.Add(new CorpusEntry { Index = i, Slot = 1, Text = desc });
        }
        return result;
    }

    private static string ReadPoolString(byte[] pool, ushort offset, Dictionary<byte, char> decoder)
    {
        if (offset >= pool.Length)
            return "";
        int end = offset;
        while (end < pool.Length && pool[end] != 0)
            end++;
        byte[] script = pool[offset..end];
        return script.Length == 0 ? "" : FfxEncoding.DecodeScriptLossless(script, decoder);
    }

    private sealed class CorpusEntry
    {
        public int Index { get; init; }
        public int? Slot { get; init; }
        public string Text { get; init; } = "";
    }
}

