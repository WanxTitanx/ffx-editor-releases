using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.FfxLib.Mods;

internal sealed record HookAbilityDefinition(string Key, int DefaultId, string Name, int Kind, int[] Owners,
    string Definition, string PayloadMode, string PayloadHex, string Description, int Price, bool WorkshopOnly)
{
    public bool IsDefined => Definition == "Defined";
    public ushort Word(int id) => checked((ushort)(0x8000 + id));
    public bool Allows(int owner, int kind) => IsDefined && Kind == kind && Owners.Contains(owner);
}
internal sealed record VanguardFeature(string Key, string Label, string Group, string Help);

internal static class HookAbilityRegistry
{
    public static IReadOnlyList<HookAbilityDefinition> Abilities { get; }
    public static IReadOnlyList<VanguardFeature> Features { get; }
    static HookAbilityRegistry()
    {
        using var stream = typeof(HookAbilityRegistry).Assembly.GetManifestResourceStream("FFXProjectEditor.HookCatalog") ?? throw new InvalidOperationException("Hook catalog missing.");
        using var json = JsonDocument.Parse(stream); var root = json.RootElement;
        if (root.GetProperty("revision").GetString() != HookCompatibilityProfile.SourceRevision) throw new InvalidDataException("Hook catalog revision mismatch.");
        Abilities = Array.AsReadOnly(root.GetProperty("abilities").EnumerateArray().Select(r => new HookAbilityDefinition(
            r.GetProperty("key").GetString()!, r.GetProperty("default_id").GetInt32(), r.GetProperty("name").GetString()!,
            r.GetProperty("kind").GetInt32(), r.GetProperty("owners").EnumerateArray().Select(o => o.GetInt32()).ToArray(),
            r.GetProperty("definition").GetString()!, r.GetProperty("payload_mode").GetString()!, r.GetProperty("payload_hex").GetString()!,
            r.GetProperty("description").GetString()!, r.GetProperty("price").GetInt32(), r.GetProperty("workshop_only").GetBoolean())).ToArray());
        Features = Array.AsReadOnly(root.GetProperty("features").EnumerateArray().Select(r => new VanguardFeature(
            r.GetProperty("key").GetString()!, r.GetProperty("label").GetString()!, r.GetProperty("group").GetString()!, r.GetProperty("help").GetString()!)).ToArray());
        if (Abilities.Count != 40 || Features.Count != 31) throw new InvalidDataException("Incomplete Hook catalog.");
    }
    public static HookAbilityDefinition Get(string key) => Abilities.SingleOrDefault(a => a.Key == key) ?? throw new InvalidDataException($"Unknown ability key: {key}.");
    public static HookAbilityDefinition? Identify(AutoAbilityEntry entry)
    {
        if (entry.RawBytes.Length != 108 || entry.Index is < 135 or > 4095) return null;
        string name=entry.NameText.Replace('’','\'');
        return Abilities.FirstOrDefault(a =>
            (entry.Index>=175 || (a.DefaultId<148 ? entry.Index<=147 : entry.Index==a.DefaultId)) &&
            (name==a.Name || name==a.DefaultId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || HookAbilityLocalizedNames.MatchesDisplay(a.DefaultId, entry.NameText)) &&
            PayloadMatches(a, entry.RawBytes.AsSpan(16)));
    }
    internal static bool PayloadMatches(HookAbilityDefinition definition, ReadOnlySpan<byte> payload)
    {
        byte[] expected = Convert.FromHexString(definition.PayloadHex);
        if (payload.Length != expected.Length) return false;
        bool captureAllowed = definition.IsDefined || definition.Definition == "NativeBaseOnly";
        for (int i = 0; i < expected.Length; i++)
        {
            if (payload[i] == expected[i]) continue;
            // Spira's universal Capture adds only native bit34 (row+0x66).
            // Other flags and pending definitions retain exact admission.
            if (captureAllowed && i == 0x66 - 16 && payload[i] == (expected[i] | 0x04)) continue;
            return false;
        }
        return true;
    }
    public static Dictionary<string, int> Mapping(IReadOnlyDictionary<string, int>? overrides = null)
    {
        var result = Abilities.ToDictionary(a => a.Key, a => a.DefaultId, StringComparer.Ordinal);
        if (overrides != null) foreach (var pair in overrides)
        {
            var definition = Get(pair.Key); int id = pair.Value;
            bool allowed = id is >= 175 and <= 4095 || (definition.DefaultId < 148 ? id is >= 135 and <= 147 : id == definition.DefaultId);
            if (!allowed) throw new InvalidDataException($"Reserved autoability ID {id} for {pair.Key}.");
            result[pair.Key] = id;
        }
        if (result.Values.Distinct().Count() != result.Count) throw new InvalidDataException("Autoability mappings collide across Vanguard/Aeon/Spira.");
        return result;
    }

    public static byte[] CanonicalName(HookAbilityDefinition definition, bool numeric)
    {
        if (numeric) return Encoding.ASCII.GetBytes(definition.DefaultId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // SpiraAbilityCatalog::Glyph is an identity proof, not the general UI codec.
        // In particular ASCII apostrophe is the canonical byte 65, regardless of
        // duplicate typographic glyphs in the Editor's locale decoder.
        return definition.Name.Select(c => checked((byte)(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' ? c + 15 :
            c is >= '0' and <= '9' ? c : c switch
            { ' ' => 58, '%' => 63, '\'' => 65, '+' => 69, '-' => 71, '.' => 72, '/' => 73, _ => throw new InvalidDataException("Unsupported canonical identity glyph.") }))).ToArray();
    }

    public static void ValidateBank(byte[] abilities, byte[] prices, IReadOnlyDictionary<string, int> mapping, string locale)
    {
        ValidateLayout(abilities);
        var ids = Mapping(mapping);
        var table = ReadBankView(abilities, prices);
        int last=BinaryPrimitives.ReadUInt16LittleEndian(abilities.AsSpan(10)),length=BinaryPrimitives.ReadUInt16LittleEndian(abilities.AsSpan(14));
        if (!table.HasFullPriceCoverage) throw new InvalidDataException("Price coverage is incomplete.");
        if (!Mod002Authoring.Locales.Contains(locale, StringComparer.Ordinal)) throw new InvalidDataException("Unsupported bank locale.");
        bool numeric = Array.IndexOf(Mod002Authoring.Locales, locale) >= 6;
        foreach (var definition in Abilities)
        {
            int id = ids[definition.Key];
            if (id > last) throw new InvalidDataException($"Missing autoability row: {definition.Key} / {id}.");
            ReadOnlySpan<byte> row = abilities.AsSpan(20 + id * 108, 108);
            if (!PayloadMatches(definition, row[16..])) throw new InvalidDataException($"Payload mismatch: {definition.Key}.");
            byte[] expected = CanonicalName(definition, numeric);
            int at = 20 + length + BinaryPrimitives.ReadUInt16LittleEndian(row);
            int end = at < abilities.Length ? Array.IndexOf(abilities, (byte)0, at) : -1;
            bool legacy = at <= abilities.Length - expected.Length - 1
                && abilities.AsSpan(at, expected.Length).SequenceEqual(expected) && abilities[at + expected.Length] == 0;
            if (!legacy && (end < at || !HookAbilityLocalizedNames.Matches(definition.DefaultId, locale, abilities.AsSpan(at, end - at))))
                throw new InvalidDataException($"Identity mismatch: {definition.Key} (placeholder follows default identity, not remapped row).");
        }
    }
    internal static void ValidateLayout(byte[] abilities)
    {
        if (abilities.Length > 65535 || abilities.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(abilities) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(abilities.AsSpan(8)) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(abilities.AsSpan(16)) != 20)
            throw new InvalidDataException("Autoability bank exceeds or differs from the native consumer layout.");
        int last = BinaryPrimitives.ReadUInt16LittleEndian(abilities.AsSpan(10));
        int width = BinaryPrimitives.ReadUInt16LittleEndian(abilities.AsSpan(12));
        int length = BinaryPrimitives.ReadUInt16LittleEndian(abilities.AsSpan(14));
        if (width != 108 || length != (last + 1) * 108 || length > abilities.Length - 20) throw new InvalidDataException("Invalid autoability section.");
        for(int id=0;id<=last;id++)foreach(int field in new[]{0,4,8,12})
        {
            int at=20+length+BinaryPrimitives.ReadUInt16LittleEndian(abilities.AsSpan(20+id*108+field));
            if(at>=abilities.Length||Array.IndexOf(abilities,(byte)0,at)<0)throw new InvalidDataException("Invalid/unterminated autoability text reference.");
        }
    }
    internal static AutoAbilityTable ReadBankView(byte[] abilities, byte[] prices)
    {
        ValidateLayout(abilities);
        if (prices.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(prices) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(prices.AsSpan(8)) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(prices.AsSpan(12)) != 4 ||
            BinaryPrimitives.ReadUInt32LittleEndian(prices.AsSpan(16)) != 20) throw new InvalidDataException("Invalid Hook price bank layout.");
        int needed=BinaryPrimitives.ReadUInt16LittleEndian(abilities.AsSpan(10))+1, actual=BinaryPrimitives.ReadUInt16LittleEndian(prices.AsSpan(10))+1;
        if(actual<needed||actual*4!=BinaryPrimitives.ReadUInt16LittleEndian(prices.AsSpan(14))||20+actual*4>prices.Length)
            throw new InvalidDataException("Incomplete or invalid Hook price coverage.");
        // The vanilla parser deliberately rejects a longer price table. Keep that
        // contract unchanged; use a temporary read-only view for the Hook's >= coverage.
        byte[] view=actual==needed?prices:EntryListFile.Pack(4,checked((short)needed),prices.AsSpan(20,needed*4).ToArray(),prices.AsSpan(20+actual*4).ToArray());
        return AutoAbility_File.Read(abilities,view);
    }
}

internal sealed record HookAbilityBankResult(byte[] Abilities, byte[] Prices, string Report);

internal static class HookAbilityBankAuthoring
{
    public static async Task<HookAdmission> AdmitAsync(HookAbilityBankResult bank, string locale, IReadOnlyDictionary<string,int> mapping, HookOfflineValidator validator, CancellationToken token)
    {
        HookAbilityRegistry.ValidateBank(bank.Abilities,bank.Prices,mapping,locale);
        string stage=Path.Combine(Path.GetTempPath(),"ffx-ability-admit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try
        {
            string abilities=Path.Combine(stage,"a_ability.bin"),prices=Path.Combine(stage,"arms_rate.bin"),ids=Path.Combine(stage,"ids.json");
            await File.WriteAllBytesAsync(abilities,bank.Abilities,token);await File.WriteAllBytesAsync(prices,bank.Prices,token);
            await File.WriteAllBytesAsync(ids,JsonSerializer.SerializeToUtf8Bytes(HookAbilityRegistry.Mapping(mapping)),token);
            var result=await validator.ValidateAsync("abilities",abilities,new[]{ids,Array.IndexOf(Mod002Authoring.Locales,locale)>=6?"numeric":"latin",prices},token);
            if(!result.Admitted)throw new InvalidDataException(result.Report);return result;
        }
        finally {Directory.Delete(stage,true);}
    }
    public static HookAbilityBankResult Generate(byte[] abilities, byte[] prices, string locale, IReadOnlyDictionary<string, int>? overrides = null)
    {
        if (abilities.Length > 65535 || !Mod002Authoring.Locales.Contains(locale, StringComparer.Ordinal)) throw new InvalidDataException("Unsupported bank size or locale.");
        HookAbilityRegistry.ValidateLayout(abilities);
        var ids = HookAbilityRegistry.Mapping(overrides); var source = HookAbilityRegistry.ReadBankView(abilities, prices);
        if (!source.HasFullPriceCoverage || BinaryPrimitives.ReadUInt32LittleEndian(abilities.AsSpan(16)) != 20) throw new InvalidDataException("Invalid source bank/price layout.");
        int count = Math.Max(source.Entries.Count, ids.Values.Max() + 1);
        if (20L + count * 108 > 65535) throw new InvalidDataException("Mapped rows cannot fit the native WORD-sized bank.");
        var rows = new byte[count * 108];
        foreach (var entry in source.Entries) entry.RawBytes.CopyTo(rows, entry.Index * 108);
        int oldPool = 20 + source.AbilityHeader.TotalDataLength;
        using var pool = new MemoryStream(); pool.Write(abilities.AsSpan(oldPool));
        if (pool.Length == 0) pool.WriteByte(0);
        int oldPriceCount=BinaryPrimitives.ReadUInt16LittleEndian(prices.AsSpan(10))+1;
        if(prices.Length<20+oldPriceCount*4||BinaryPrimitives.ReadUInt32LittleEndian(prices)!=1||
            BinaryPrimitives.ReadUInt16LittleEndian(prices.AsSpan(8))!=0||BinaryPrimitives.ReadUInt16LittleEndian(prices.AsSpan(12))!=4||
            BinaryPrimitives.ReadUInt16LittleEndian(prices.AsSpan(14))!=oldPriceCount*4||BinaryPrimitives.ReadUInt32LittleEndian(prices.AsSpan(16))!=20)
            throw new InvalidDataException("Invalid price-table layout.");
        int priceCount=Math.Max(count,oldPriceCount);var newPrices = new byte[priceCount * 4];
        prices.AsSpan(20,oldPriceCount*4).CopyTo(newPrices);
        bool numeric = Array.IndexOf(Mod002Authoring.Locales, locale) >= 6;
        foreach (var definition in HookAbilityRegistry.Abilities)
        {
            int id = ids[definition.Key];
            if (id < source.Entries.Count)
            {
                var current = source.Entries[id];
                // A populated row is replaceable only when its identity/payload already belongs
                // to this effect. Blank expansion rows are the only other admissible source.
                bool blank = current.RawBytes.All(b => b == 0);
                int oldName = oldPool + BinaryPrimitives.ReadUInt16LittleEndian(current.RawBytes);
                byte[] expectedName = HookAbilityRegistry.CanonicalName(definition, numeric);
                int nameEnd = oldName < abilities.Length ? Array.IndexOf(abilities, (byte)0, oldName) : -1;
                bool named = oldName <= abilities.Length - expectedName.Length - 1 && abilities.AsSpan(oldName, expectedName.Length).SequenceEqual(expectedName) && abilities[oldName + expectedName.Length] == 0;
                named |= nameEnd >= oldName && HookAbilityLocalizedNames.Matches(definition.DefaultId, locale, abilities.AsSpan(oldName, nameEnd - oldName));
                if (!blank && (!named || !HookAbilityRegistry.PayloadMatches(definition, current.RawBytes.AsSpan(16))))
                    throw new InvalidDataException($"Refusing to replace occupied/nonmatching row {id}: {definition.Key}.");
                // A previously authored, admitted identity already satisfies this operation.
                // Keep its description, auxiliary text and exact bytes rather than replacing
                // them with catalog defaults when another missing row is being added.
                if (!blank) continue;
            }
            Span<byte> row = rows.AsSpan(id * 108, 108);
            Convert.FromHexString(definition.PayloadHex).CopyTo(row[16..]);
            void Append(int field, byte[] text)
            {
                if (pool.Length + text.Length + 1 + 20L + rows.Length > 65535) throw new InvalidDataException("Autoability bank/pool exceeds native WORD length.");
                BinaryPrimitives.WriteUInt16LittleEndian(rows.AsSpan(id * 108 + field), checked((ushort)pool.Length));
                pool.Write(text); pool.WriteByte(0);
            }
            Append(0, HookAbilityRegistry.CanonicalName(definition, numeric));
            Append(8, numeric ? Array.Empty<byte>() : TextBinary_Util.EncodeScriptToBytes(definition.Description, FfxEncoding.UsDecoder));
            if (id >= oldPriceCount) BinaryPrimitives.WriteInt32LittleEndian(newPrices.AsSpan(id * 4), definition.Price);
        }
        byte[] result = EntryListFile.Pack(108, checked((short)count), rows, pool.ToArray());
        abilities.AsSpan(4,4).CopyTo(result.AsSpan(4));
        // Preserve price trailing data rather than rebuilding an unrelated section.
        int priceEnd = 20 + oldPriceCount * 4;
        byte[] priceResult = EntryListFile.Pack(4, checked((short)priceCount), newPrices, prices.AsSpan(priceEnd).ToArray());
        prices.AsSpan(4,4).CopyTo(priceResult.AsSpan(4));
        HookAbilityRegistry.ValidateBank(result, priceResult, ids, locale);
        return new(result, priceResult, string.Join("\n", HookAbilityRegistry.Abilities.Select(a => $"{a.Key}: {ids[a.Key]} / 0x{a.Word(ids[a.Key]):X4} / {a.Definition}")));
    }
}
