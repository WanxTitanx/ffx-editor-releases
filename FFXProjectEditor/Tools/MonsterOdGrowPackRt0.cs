using System;
using System.IO;
using FFXProjectEditor.FfxLib.Ability;

namespace FFXProjectEditor.Tools;

public static class MonsterOdGrowPackRt0
{
    public static void Run(string inputPath, string outputDir)
    {
        byte[] bytes = File.ReadAllBytes(inputPath);
        string outputFile = Path.Combine(outputDir, "monmagic2.bin");
        Directory.CreateDirectory(outputDir);

        var steps = new (string Name, Func<byte[], bool, int, MonsterMagicGrowResult> Append)[]
        {
            // Calm Lands (4)
            ("Blaster Cannon", MonsterMagicGrowWriter.AppendBlasterCannon),
            ("Mighty Guard", MonsterMagicGrowWriter.AppendMightyGuard),
            ("Psychic Storm", MonsterMagicGrowWriter.AppendPsychicStorm),
            ("Ogre Smash", MonsterMagicGrowWriter.AppendOgreSmash),
            // Bikanel (3)
            ("Sand Breath", MonsterMagicGrowWriter.AppendSandBreath),
            ("Sonic Storm", MonsterMagicGrowWriter.AppendSonicStorm),
            ("10,000 Needles", MonsterMagicGrowWriter.AppendTenThousandNeedles),
            // Mt. Gagazet (14)
            ("Magic Burst", MonsterMagicGrowWriter.AppendMagicBurst),
            ("Hydro Cannon", MonsterMagicGrowWriter.AppendHydroCannon),
            ("Feral Rush", MonsterMagicGrowWriter.AppendFeralRush),
            ("Dragon Breath", MonsterMagicGrowWriter.AppendDragonBreath),
            ("Shell Shatter", MonsterMagicGrowWriter.AppendShellShatter),
            ("Doom Gaze", MonsterMagicGrowWriter.AppendDoomGaze),
            ("Spike Rain", MonsterMagicGrowWriter.AppendSpikeRain),
            ("Whirlpool", MonsterMagicGrowWriter.AppendWhirlpool),
            ("Full Burst", MonsterMagicGrowWriter.AppendFullBurst),
            ("Knuckle Press", MonsterMagicGrowWriter.AppendKnucklePress),
            ("Dark Eruption", MonsterMagicGrowWriter.AppendDarkEruption),
            ("Savage Rend", MonsterMagicGrowWriter.AppendSavageRend),
            ("Meteor", MonsterMagicGrowWriter.AppendMeteor),
            ("Scream", MonsterMagicGrowWriter.AppendScream),
            // Cavern of Stolen Fayth (3)
            ("Megadeath", MonsterMagicGrowWriter.AppendMegadeath),
            ("Dark Pulse", MonsterMagicGrowWriter.AppendDarkPulse),
            ("Shield Crash", MonsterMagicGrowWriter.AppendShieldCrash),
            // Zanarkand (1)
            ("Guardian Blast", MonsterMagicGrowWriter.AppendGuardianBlast),
        };

        var entries = Ability_Command.ReadList(bytes, hasExtraInfo: false);
        Console.WriteLine($"current entries: {entries.Count} (entry size {MonsterMagicGrowWriter.MonMagicEntrySize:X2})");

        bool allPass = true;
        for (int i = 0; i < steps.Length; i++)
        {
            var (name, append) = steps[i];
            var result = append(bytes, true, -1);
            bool pass = result.ExistingRecordPayloadPreserved
                && result.ExistingTextPoolPrefixPreserved
                && result.RereadCountOk
                && result.RereadNewTextOk;
            Console.WriteLine($"  [{i,2}] {name,-20} id={result.NewId,-3} operand=0x{result.Operand:X4} pass={pass}");
            bytes = result.GrownBytes;
            if (!pass) allPass = false;
        }

        File.WriteAllBytes(outputFile, bytes);
        var finalEntries = Ability_Command.ReadList(bytes, hasExtraInfo: false);
        Console.WriteLine($"wrote: {outputFile} ({bytes.Length} bytes, {finalEntries.Count} entries)");
        Console.WriteLine($"VERDICT: {(allPass ? "PASS" : "FAIL")} - 25 new monmagic2 skills grown + round-trip byte-faithful.");
    }
}
