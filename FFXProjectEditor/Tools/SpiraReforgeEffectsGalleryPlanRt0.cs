using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools;

internal static class SpiraReforgeEffectsGalleryPlanRt0
{
    // The gallery starts at Flan Flood as requested. Names/selectors come from
    // actual command records, so the gallery cannot drift into a design-only list.
    public static int Run(string monsterBin, string playerBin, string output)
    {
        var monsters = Ability_Command.ReadList(File.ReadAllBytes(monsterBin), false);
        var players = Ability_Command.ReadList(File.ReadAllBytes(playerBin), true);
        var records = monsters.Select((row,id)=>(row,id,kind:"monster",bank:0x6000))
            .Where(x=>x.id>=249)
            .Concat(players.Select((row,id)=>(row,id,kind:"player",bank:0x3000)).Where(x=>x.id>=367))
            .Select(x=>new { key=$"{x.kind}-{x.id}", x.kind, row=x.id, operand=$"0x{x.bank+x.id:X4}",
                name=FfxEncoding.DecodeScript(x.row.NameScriptBytes).GetString(FfxEncoding.UsDecoder,true),
                effects=new[]{(int)x.row.Anim1Id,x.row.Anim2Id}.Where(id=>id>0&&id<=9999).Distinct().ToArray() });
        File.WriteAllText(output,JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
}
