using System;
using System.Collections.Generic;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.FfxLib.Ability
{
    /// <summary>Append / clone / delete rows in command.bin and monmagic*.bin tables.</summary>
    public static class KernelCommandListMutator
    {
        public static Ability_Command CloneEntry(Ability_Command donor, bool hasExtraInfo) =>
            donor.CloneDeep(hasExtraInfo);

        public static int AppendClone(List<Ability_Command> entries, int donorIndex, bool hasExtraInfo, string? nameOverride = null)
        {
            ArgumentNullException.ThrowIfNull(entries);
            if (entries.Count == 0)
                throw new InvalidOperationException("Cannot append to an empty command table.");

            if (donorIndex < 0 || donorIndex >= entries.Count)
                throw new ArgumentOutOfRangeException(nameof(donorIndex));

            Ability_Command clone = CloneEntry(entries[donorIndex], hasExtraInfo);
            if (nameOverride != null)
                clone.NameScriptBytes = EncodeUs(nameOverride);
            else
            {
                string baseName = DecodeUs(clone.NameScriptBytes);
                if (string.IsNullOrWhiteSpace(baseName))
                    baseName = "Command";
                clone.NameScriptBytes = EncodeUs($"{baseName} Copy");
            }

            entries.Add(clone);
            return entries.Count - 1;
        }

        public static int AppendFromEntryZero(List<Ability_Command> entries, bool hasExtraInfo)
        {
            int newIndex = AppendClone(entries, 0, hasExtraInfo, nameOverride: $"New Command {entries.Count}");
            Ability_Command row = entries[newIndex];
            row.DescriptionScriptBytes = EncodeUs("Author in Kernel Commands.");
            return newIndex;
        }

        public static void RemoveAt(List<Ability_Command> entries, int index)
        {
            ArgumentNullException.ThrowIfNull(entries);
            if (entries.Count <= 1)
                throw new InvalidOperationException("Cannot delete the last remaining command row.");

            if (index < 0 || index >= entries.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            entries.RemoveAt(index);
        }

        static byte[] EncodeUs(string text) => FfxEncoding.EncodeString(text, FfxEncoding.UsEncoder).ByteArray;

        static string DecodeUs(byte[] bytes) =>
            FfxEncoding.DecodeScript(bytes).GetString(FfxEncoding.UsDecoder, withControlCodes: true);
    }

}
