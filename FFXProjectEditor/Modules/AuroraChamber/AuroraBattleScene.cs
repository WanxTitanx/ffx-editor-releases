namespace FFXProjectEditor.Modules.AuroraChamber;

/// <summary>
/// btl.bin uses bit 0x400 for a separate battle arena (NoClip folder 1a).
/// Otherwise it names a field scene (folder 13), including indices above 255
/// used by story bosses. See the pinned renderer's selectEncounter/BattleScene.
/// </summary>
internal readonly record struct AuroraBattleScene(int Index, bool IsBattleMap)
{
    internal string DataFolder => IsBattleMap ? "1a" : "13";
    internal string Query => $"map={Index}&mapKind={(IsBattleMap ? "battle" : "field")}";

    internal static AuroraBattleScene? FromBattlefield(int battlefield) => battlefield switch
    {
        >= 0 and <= 0x3ff => new(battlefield, false),
        >= 0x400 and <= 0x4ff => new(battlefield & 0xff, true),
        _ => null,
    };
}
