using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.Utils.Encoding;
using Xe.BinaryMapper;
using static FFXProjectEditor.FfxLib.Ability.Ability_Structs;

namespace FFXProjectEditor.FfxLib.Ability
{
    public sealed class CommandGrowAppendResult
    {
        public required byte[] OriginalBytes { get; init; }
        public required byte[] GrownBytes { get; init; }
        public required int OriginalEntryCount { get; init; }
        public required int NewEntryCount { get; init; }
        public required int AppendedCount { get; init; }
        public required int OriginalLength { get; init; }
        public required int GrownLength { get; init; }
        public required bool ExistingRecordPayloadPreserved { get; init; }
        public required bool ExistingTextPoolPrefixPreserved { get; init; }
        public required bool RereadCountOk { get; init; }
        public required bool PreserveWriteOk { get; init; }

        public bool Pass => ExistingRecordPayloadPreserved
            && ExistingTextPoolPrefixPreserved
            && RereadCountOk
            && PreserveWriteOk;
    }

    /// <summary>Shared append pipeline for grown <c>command.bin</c> rows (0x60 + text pool).</summary>
    public static class CommandBinGrowCore
    {
        public const int VanillaRowCount = 320;

        public static CommandGrowAppendResult AppendRows(
            byte[] originalBytes,
            IReadOnlyList<Ability_Command> toAppend,
            int minRowCount = VanillaRowCount)
        {
            ArgumentNullException.ThrowIfNull(originalBytes);
            ArgumentNullException.ThrowIfNull(toAppend);

            if (toAppend.Count == 0)
                throw new ArgumentException("At least one row must be appended.", nameof(toAppend));

            EntryListFile original = EntryListFile.Unpack(originalBytes);
            if (original.Header.EntrySize != CommandGrowWriter.CommandEntrySize)
            {
                throw new InvalidDataException(
                    $"Expected command.bin entry size 0x{CommandGrowWriter.CommandEntrySize:X}, got 0x{original.Header.EntrySize:X}.");
            }

            List<Ability_Command> entries = Ability_Command.ReadList(originalBytes, hasExtraInfo: true);
            if (entries.Count < minRowCount)
            {
                throw new InvalidDataException(
                    $"Expected at least {minRowCount} rows before grow, got {entries.Count}.");
            }

            byte[] textPool = original.SecondFile.ToArray();
            byte[] grownRows = original.FirstFile.ToArray();
            foreach (Ability_Command command in toAppend)
                grownRows = grownRows.Concat(BuildNewRow(command, ref textPool)).ToArray();

            byte[] grownBytes = EntryListFile.Pack(
                entrySize: CommandGrowWriter.CommandEntrySize,
                entryCount: checked((short)(entries.Count + toAppend.Count)),
                firstFile: grownRows,
                secondFile: textPool);

            List<Ability_Command> reread;
            try
            {
                reread = Ability_Command.ReadList(grownBytes, hasExtraInfo: true);
            }
            catch (Exception ex)
            {
                EntryListFile dbg = EntryListFile.Unpack(grownBytes);
                throw new InvalidDataException(
                    $"ReadList failed after grow ({entries.Count} + {toAppend.Count} rows): "
                    + $"first=0x{dbg.FirstFile?.Length:X ?? 0} second=0x{dbg.SecondFile?.Length:X ?? 0} "
                    + $"textPool=0x{textPool.Length:X} headerEntryTableSize=0x{dbg.Header.EntryTableSize:X}",
                    ex);
            }

            bool existingRowsPreserved = grownRows.Length >= original.FirstFile.Length
                && original.FirstFile.AsSpan().SequenceEqual(grownRows.AsSpan(0, original.FirstFile.Length));
            bool existingTextPreserved = textPool.Length >= original.SecondFile.Length
                && original.SecondFile.AsSpan().SequenceEqual(textPool.AsSpan(0, original.SecondFile.Length));

            return new CommandGrowAppendResult
            {
                OriginalBytes = originalBytes,
                GrownBytes = grownBytes,
                OriginalEntryCount = entries.Count,
                NewEntryCount = reread.Count,
                AppendedCount = toAppend.Count,
                OriginalLength = originalBytes.Length,
                GrownLength = grownBytes.Length,
                ExistingRecordPayloadPreserved = existingRowsPreserved,
                ExistingTextPoolPrefixPreserved = existingTextPreserved,
                RereadCountOk = reread.Count == entries.Count + toAppend.Count,
                PreserveWriteOk = Ability_Command.WriteList(reread, hasExtraInfo: true).SequenceEqual(grownBytes),
            };
        }

        public static byte[] BuildNewRow(Ability_Command command, ref byte[] textPool)
        {
            Ability_CommandStruct row = new() { AbilityInfo = command };
            if (command.ExtraInfo == null)
                command.ExtraInfo = new Ability_Command.ExtraCommandInfo();

            AppendScript(ref textPool, row.NameTSInfo, command.NameScriptBytes ?? Array.Empty<byte>(), command.NameScriptId);
            AppendScript(ref textPool, row.UnusedText1TSInfo, command.JapaneseOnlyText1ScriptBytes ?? Array.Empty<byte>(), command.JapaneseOnlyText1ScriptId);
            AppendScript(ref textPool, row.DescriptionTSInfo, command.DescriptionScriptBytes ?? Array.Empty<byte>(), command.DescriptionScriptId);
            AppendScript(ref textPool, row.UnusedText2TSInfo, command.JapaneseOnlyText2ScriptBytes ?? Array.Empty<byte>(), command.JapaneseOnlyText2ScriptId);

            using MemoryStream stream = new();
            BinaryMapping.WriteObject(stream, row);
            BinaryMapping.WriteObject(stream, command.ExtraInfo);
            byte[] rowBytes = stream.ToArray();
            if (rowBytes.Length != CommandGrowWriter.CommandEntrySize)
            {
                throw new InvalidDataException(
                    $"New command row serialized to 0x{rowBytes.Length:X}; expected 0x{CommandGrowWriter.CommandEntrySize:X}.");
            }

            return rowBytes;
        }

        static void AppendScript(ref byte[] textPool, CommonStructs.TextScriptInfo info, byte[] scriptBytes, ushort scriptId)
        {
            if (textPool.Length > ushort.MaxValue)
                throw new InvalidDataException($"Text pool is too large for a 16-bit script offset: 0x{textPool.Length:X}.");

            info.Offset = (ushort)textPool.Length;
            info.ScriptId = scriptId;
            textPool = FfxEncoding.WriteBytesIntoTextFile(textPool, scriptBytes);
        }
    }
}
