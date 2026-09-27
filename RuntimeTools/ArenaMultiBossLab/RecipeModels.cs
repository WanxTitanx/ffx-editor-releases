using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArenaMultiBossLab;

internal static class JsonOpts
{
    public static readonly JsonSerializerOptions Instance = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };
}

internal sealed class Recipe
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("tier")] public string Tier { get; set; } = "";
    [JsonPropertyName("source_battle_id")] public string SourceBattleIdRaw { get; set; } = "";
    [JsonPropertyName("output_battle_id")] public string OutputBattleIdRaw { get; set; } = "";
    [JsonPropertyName("alias_battle_id")] public string AliasBattleId { get; set; } = "";
    [JsonPropertyName("token_f7")] public string TokenF7 { get; set; } = "";
    [JsonPropertyName("base_template")] public string BaseTemplate { get; set; } = "";
    [JsonPropertyName("bosses")] public List<string> Bosses { get; set; } = new();
    [JsonPropertyName("chunk2_slots")] public List<string> Chunk2Slots { get; set; } = new();
    [JsonPropertyName("chunk3_mode")] public string Chunk3Mode { get; set; } = "preserve";
    [JsonPropertyName("chunk3_monster_live")] public List<float[]> Chunk3MonsterLive { get; set; } = new();
    [JsonPropertyName("note")] public string Note { get; set; } = "";

    [JsonIgnore]
    public string SourceBattleId =>
        !string.IsNullOrWhiteSpace(SourceBattleIdRaw) ? SourceBattleIdRaw
        : !string.IsNullOrWhiteSpace(AliasBattleId) ? AliasBattleId
        : "";

    [JsonIgnore]
    public string OutputBattleId =>
        !string.IsNullOrWhiteSpace(OutputBattleIdRaw) ? OutputBattleIdRaw
        : SourceBattleId;
}

internal static class HexUtil
{
    public static ushort ParseU16(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) throw new ArgumentException("empty hex");
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return Convert.ToUInt16(s, 16);
    }

    public static string FormatU16(ushort v) => $"0x{v:X4}";
}
