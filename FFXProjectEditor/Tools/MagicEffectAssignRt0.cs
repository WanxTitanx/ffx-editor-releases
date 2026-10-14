using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    // Narrow authoring gate: change the visual effect selectors of one command row only.
    // This is the data-level way to make a newly appended ability cast a different existing
    // magic_#### effect without replacing any old ability.
    internal static class MagicEffectAssignRt0
    {
        const string DefaultAbilityFile = @"work\monster_magic_grow_pilot\monmagic2.bin";
        const string DefaultOutputDir = @"work\monster_magic_effect_variant";

        public static int Run(string[] args)
        {
            try
            {
                string abilityFile = args.Length > 1 ? args[1] : DefaultAbilityFile;
                string idArg = args.Length > 2 ? args[2] : "last";
                int anim1Id = args.Length > 3 ? ParseId(args[3]) : 82;
                int anim2Id = args.Length > 4 ? ParseId(args[4]) : 86;
                string outputDir = args.Length > 5 ? args[5] : DefaultOutputDir;
                bool hasExtraInfo = InferHasExtraInfo(abilityFile);

                Console.WriteLine("=== Magic Effect ASSIGN RT0 (set Anim1Id/Anim2Id on one row) ===");
                Console.WriteLine($"input  : {abilityFile}");
                Console.WriteLine($"id     : {idArg}");
                Console.WriteLine($"effect : magic_{anim1Id:D4}/magic_{anim2Id:D4}");
                Console.WriteLine($"output : {outputDir}");

                if (!File.Exists(abilityFile))
                {
                    Console.WriteLine("FAIL: ability/monmagic file not found.");
                    return 2;
                }
                if (anim1Id is < 0 or > short.MaxValue || anim2Id is < 0 or > short.MaxValue)
                {
                    Console.WriteLine("FAIL: Anim IDs must fit in signed 16-bit command fields.");
                    return 2;
                }

                byte[] originalBytes = File.ReadAllBytes(abilityFile);
                EntryListFile originalListFile = EntryListFile.Unpack(originalBytes);
                List<Ability_Command> entries = Ability_Command.ReadList(originalBytes, hasExtraInfo);
                int commandId = ResolveCommandId(idArg, entries.Count);
                if (commandId < 0 || commandId >= entries.Count)
                {
                    Console.WriteLine($"FAIL: command id {commandId} is outside 0..{entries.Count - 1}.");
                    return 2;
                }

                Ability_Command target = entries[commandId];
                short oldAnim1 = target.Anim1Id;
                short oldAnim2 = target.Anim2Id;
                string name = DecodeUs(target.NameScriptBytes);

                target.Anim1Id = checked((short)anim1Id);
                target.Anim2Id = checked((short)anim2Id);

                byte[] outputBytes = Ability_Command.WriteList(entries, hasExtraInfo);
                EntryListFile outputListFile = EntryListFile.Unpack(outputBytes);
                List<Ability_Command> reread = Ability_Command.ReadList(outputBytes, hasExtraInfo);
                Ability_Command rereadTarget = reread[commandId];

                int rowSize = originalListFile.Header.EntrySize;
                int rowStart = commandId * rowSize;
                int rowEnd = rowStart + rowSize;
                bool prefixRowsOk = originalListFile.FirstFile.AsSpan(0, rowStart)
                    .SequenceEqual(outputListFile.FirstFile.AsSpan(0, rowStart));
                bool suffixRowsOk = originalListFile.FirstFile.AsSpan(rowEnd)
                    .SequenceEqual(outputListFile.FirstFile.AsSpan(rowEnd));
                bool targetRowChanged = !originalListFile.FirstFile.AsSpan(rowStart, rowSize)
                    .SequenceEqual(outputListFile.FirstFile.AsSpan(rowStart, rowSize));
                bool textPoolPreserved = originalListFile.SecondFile.SequenceEqual(outputListFile.SecondFile);
                bool countPreserved = reread.Count == entries.Count
                    && outputListFile.Header.RealEntryCount == originalListFile.Header.RealEntryCount;
                bool rereadOk = rereadTarget.Anim1Id == anim1Id && rereadTarget.Anim2Id == anim2Id;
                bool pass = prefixRowsOk
                    && suffixRowsOk
                    && targetRowChanged
                    && textPoolPreserved
                    && countPreserved
                    && rereadOk
                    && outputBytes.Length == originalBytes.Length;

                Directory.CreateDirectory(outputDir);
                string outputFile = Path.Combine(outputDir, Path.GetFileName(abilityFile));
                string jsonFile = Path.Combine(outputDir, "magic_effect_assign_rt0.json");
                string mdFile = Path.Combine(outputDir, "MAGIC_EFFECT_ASSIGN_RT0.md");
                File.WriteAllBytes(outputFile, outputBytes);

                var payload = new
                {
                    source = abilityFile,
                    output = outputFile,
                    sourceSha256 = Sha256Hex(originalBytes),
                    outputSha256 = Sha256Hex(outputBytes),
                    commandId,
                    name,
                    oldAnim1,
                    oldAnim2,
                    newAnim1 = anim1Id,
                    newAnim2 = anim2Id,
                    originalLength = originalBytes.Length,
                    outputLength = outputBytes.Length,
                    rowSize,
                    prefixRowsOk,
                    suffixRowsOk,
                    targetRowChanged,
                    textPoolPreserved,
                    countPreserved,
                    rereadOk,
                    pass
                };
                File.WriteAllText(jsonFile, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(mdFile, BuildMarkdown(outputFile, commandId, name, oldAnim1, oldAnim2, anim1Id, anim2Id, payload.pass));

                Console.WriteLine($"command : {commandId} `{name}`");
                Console.WriteLine($"anim    : {oldAnim1}/{oldAnim2} -> {anim1Id}/{anim2Id}");
                Console.WriteLine("checks  : "
                    + $"prefixRows={prefixRowsOk}, "
                    + $"suffixRows={suffixRowsOk}, "
                    + $"targetChanged={targetRowChanged}, "
                    + $"textPool={textPoolPreserved}, "
                    + $"count={countPreserved}, "
                    + $"reread={rereadOk}");
                Console.WriteLine($"wrote   : {outputFile}");
                Console.WriteLine($"json    : {jsonFile}");
                Console.WriteLine($"runbook : {mdFile}");
                Console.WriteLine(pass
                    ? "VERDICT: PASS - changed only the selected row's visual effect ids."
                    : "VERDICT: FAIL - effect assignment did not satisfy the narrow row-edit contract.");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static string BuildMarkdown(string outputFile, int commandId, string name, short oldAnim1, short oldAnim2, int anim1Id, int anim2Id, bool pass)
        {
            return $@"# Magic Effect ASSIGN RT0

Status: {(pass ? "PASS" : "FAIL")}

Changed command:

- File: `{outputFile}`
- Command id: `{commandId}`
- Name: `{name}`
- Old effect ids: `magic_{oldAnim1:D4}` / `magic_{oldAnim2:D4}`
- New effect ids: `magic_{anim1Id:D4}` / `magic_{anim2Id:D4}`

Follow-up inventory command:

```powershell
dotnet FFXProjectEditor.dll --magic-effect-link-rt0 ""{outputFile}"" {commandId}
```

Meaning:

- This is the data-level selector edit.
- It does not edit `magic_####.dll`.
- It does not edit texture payloads.
- It makes this command row point at different existing visual effect asset ids.
";
        }

        static bool InferHasExtraInfo(string abilityFile)
        {
            string name = Path.GetFileName(abilityFile);
            return name.Equals("command.bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("command2.bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("item.bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("item2.bin", StringComparison.OrdinalIgnoreCase);
        }

        static int ResolveCommandId(string value, int count)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Equals("last", StringComparison.OrdinalIgnoreCase))
                return count - 1;

            return ParseId(value);
        }

        static int ParseId(string value)
        {
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.Parse(value[2..], System.Globalization.NumberStyles.HexNumber);

            return int.Parse(value);
        }

        static string DecodeUs(byte[] bytes) =>
            FfxEncoding.DecodeScript(bytes).GetString(FfxEncoding.UsDecoder, withControlCodes: true);

        static string Sha256Hex(byte[] bytes)
        {
            byte[] hash = SHA256.HashData(bytes);
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }
    }
}
