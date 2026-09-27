using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Utils.Encoding;

var path = args.Length > 0 ? args[0] : @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\monmagic2.bin";
var hasExtra = args.Length > 1 && args[1] == "extra";
var bytes = File.ReadAllBytes(path);
var entries = Ability_Command.ReadList(bytes, hasExtra);
Console.WriteLine($"File: {path}");
Console.WriteLine($"Total entries: {entries.Count}");
Console.WriteLine();
for (int i = 0; i < entries.Count; i++) {
    var e = entries[i];
    var name = FfxEncoding.DecodeScript(e.NameScriptBytes).GetString(FfxEncoding.UsDecoder, withControlCodes: true);
    Console.WriteLine($"{i,3} | anim1={e.Anim1Id,5} anim2={e.Anim2Id,5} | pwr={e.AttackPower,3} hits={e.HitCount} mp={e.CostMp,3} | {name}");
}
