using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.Mods;

internal readonly record struct MonsterRewardRates(int Ap = 1, int Gil = 1)
{
    public static MonsterRewardRates Neutral => new(1, 1);
    public void Validate()
    {
        if (Ap is < 1 or > 1000 || Gil is < 1 or > 1000) throw new InvalidDataException("AP/Gil rates must be integers from 1 to 1000.");
    }
}
internal enum MonsterRewardOrigin { Missing, File, Observation }
internal sealed record MonsterRewardSource(int Id, string Name, ushort? Gil, ushort? Ap, ushort? OverkillAp,
    MonsterRewardOrigin Origin, string? Path, string? Sha256, DateTimeOffset? ObservedAt);
internal sealed record MonsterRewardCalculation(ushort Original, ulong Individual, ulong Combined, uint Applied, bool Clamped);
internal sealed record MonsterRewardPreview(MonsterRewardCalculation? Ap, MonsterRewardCalculation? OverkillAp,
    MonsterRewardCalculation? Gil, MonsterRewardSource Source);

internal sealed class MonsterRewardProfile
{
    public const uint ApLimit = 382494549, GilLimit = 573741824;
    private readonly MonsterRewardRates[] rates = Enumerable.Repeat(MonsterRewardRates.Neutral, 4096).ToArray();
    public MonsterRewardRates this[int id]
    {
        get { ValidateId(id); return rates[id]; }
        set { ValidateId(id); value.Validate(); rates[id] = value; }
    }
    public static void ValidateId(int id) { if (id is < 0 or > 4095) throw new InvalidDataException("Monster file ID must be 0..4095."); }
    public static int FromRawIdentity(int raw) => raw is >= 0x1000 and <= 0x1fff ? raw & 0xfff : throw new InvalidDataException("Not a native monster identity.");
    public void ResetAp(int id) => this[id] = this[id] with { Ap = 1 };
    public void ResetGil(int id) => this[id] = this[id] with { Gil = 1 };
    public void Reset(int id) => this[id] = MonsterRewardRates.Neutral;

    public static MonsterRewardCalculation Calculate(ushort original, int individual, int general, bool gil)
    {
        if (individual is < 1 or > 1000 || general is < 1 or > 100) throw new InvalidDataException("Invalid reward multiplier.");
        ulong own = (ulong)original * (uint)individual, combined = own * (uint)general;
        uint limit = gil ? GilLimit : ApLimit;
        return new(original, own, combined, (uint)Math.Min(combined, limit), combined > limit);
    }

    public MonsterRewardPreview Preview(MonsterRewardSource source, int generalAp, int generalGil, bool apEnabled, bool gilEnabled)
    {
        var own = this[source.Id];
        return new(source.Ap.HasValue ? Calculate(source.Ap.Value, own.Ap, apEnabled ? generalAp : 1, false) : null,
            source.OverkillAp.HasValue ? Calculate(source.OverkillAp.Value, own.Ap, apEnabled ? generalAp : 1, false) : null,
            source.Gil.HasValue ? Calculate(source.Gil.Value, own.Gil, gilEnabled ? generalGil : 1, true) : null, source);
    }
}

internal static class MonsterRewardSettingsCodec
{
    public const string Header = "ffx.monster-rewards.v1";
    public static MonsterRewardProfile Parse(byte[] bytes)
    {
        if (bytes.Length > 65535 || bytes.Any(b => b > 127)) throw new InvalidDataException("Reward settings must be ASCII and at most 65535 bytes.");
        string text = Encoding.ASCII.GetString(bytes);
        if (!text.EndsWith('\n')) throw new InvalidDataException("Truncated reward settings: final newline missing.");
        string[] lines = text.Split('\n');
        if (lines[0].TrimEnd('\r') != Header || lines[0].Count(c => c == '\r') > 1) throw new InvalidDataException("Invalid reward settings header.");
        var result = new MonsterRewardProfile(); var seen = new HashSet<int>();
        for (int line = 1; line < lines.Length - 1; line++)
        {
            string row = lines[line]; if (row.EndsWith('\r')) row = row[..^1];
            var fields = row.Split('\t'); var values = new int[3];
            if (fields.Length != 3) throw new InvalidDataException($"Line {line + 1}: expected three TAB-separated columns.");
            for (int column = 0; column < 3; column++)
                if (fields[column].Length == 0 || fields[column].Any(c => c is < '0' or > '9') ||
                    !int.TryParse(fields[column], NumberStyles.None, CultureInfo.InvariantCulture, out values[column]))
                    throw new InvalidDataException($"Line {line + 1}, column {column + 1}: expected unsigned decimal integer.");
            try
            {
                if (!seen.Add(values[0])) throw new InvalidDataException("Duplicate monster ID.");
                result[values[0]] = new(values[1], values[2]);
            }
            catch (InvalidDataException ex) { throw new InvalidDataException($"Line {line + 1}: {ex.Message}", ex); }
        }
        return result;
    }
    public static byte[] Serialize(MonsterRewardProfile profile)
    {
        var text = new StringBuilder(Header + "\n");
        for (int id = 0; id < 4096; id++)
        {
            var rate = profile[id]; rate.Validate();
            if (rate != MonsterRewardRates.Neutral) text.Append(CultureInfo.InvariantCulture, $"{id}\t{rate.Ap}\t{rate.Gil}\n");
        }
        byte[] bytes = Encoding.ASCII.GetBytes(text.ToString());
        if (bytes.Length > 65535) throw new InvalidDataException("Reward settings exceed native size limit.");
        return bytes;
    }
    public static void Save(string path, MonsterRewardProfile rates, HookProjectProfile project, string? importedHash)
    {
        project.Require("monster-rewards.v1");
        byte[] bytes = Serialize(rates); Parse(bytes);
        HookExportTransaction.ReplaceFile(path, bytes, importedHash);
    }
}

internal static class MonsterRewardSourceResolver
{
    internal static int FileId(string path)
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(path);
        string digits = stem.StartsWith("monster_", StringComparison.Ordinal) ? stem[8..].Split('_')[0] :
            stem.StartsWith('m') ? stem[1..].Split('_')[0] : "";
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int id)) throw new InvalidDataException("Unrecognized monster file identity.");
        MonsterRewardProfile.ValidateId(id); return id;
    }
    public static MonsterRewardSource Missing(int id, string name)
    { MonsterRewardProfile.ValidateId(id); return new(id, name, null, null, null, MonsterRewardOrigin.Missing, null, null, null); }

    public static MonsterRewardSource FromFile(int id, string name, string path)
    {
        MonsterRewardProfile.ValidateId(id);
        // Both extraction conventions occur. Never infer an ID from a display name.
        if (FileId(path) != id)
            throw new InvalidDataException("Selected monster file does not match the requested file ID.");
        byte[] bytes = ModAuthoringFiles.Read(path, 8 << 20);
        // MonsterHeaderFile: signature + seven section offsets, then the total size.
        if (bytes.Length < 0x30) throw new InvalidDataException("Truncated monster header.");
        int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x20));
        int loot = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x14));
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes) != 8 || size != bytes.Length || loot < 0x30 || loot > bytes.Length - 6)
            throw new InvalidDataException("Invalid monster loot range.");
        for (int field = 4; field < 0x20; field += 4)
        {
            int pointer = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(field));
            if (pointer != 0 && (pointer < 0x30 || pointer > size || (field != 0x14 && pointer >= loot && pointer < loot + 6)))
                throw new InvalidDataException("Invalid or overlapping monster section.");
        }
        ushort Read(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(loot + offset));
        return new(id, name, Read(0), Read(2), Read(4), MonsterRewardOrigin.File, System.IO.Path.GetFullPath(path), ModAuthoringFiles.Sha256(bytes), null);
    }
}
