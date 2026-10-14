using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Shop
{
    internal static class ShopItemCatalog_File
    {
        public static ShopItemCatalog Read(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            List<Ability_Command> commands = Ability_Command.ReadList(bytes, hasExtraInfo: true);
            Dictionary<int, ShopItemCatalogEntry> entriesByIndex = new(commands.Count);

            for (int index = 0; index < commands.Count; index++)
            {
                Ability_Command command = commands[index];
                string displayLabel = NormalizeText(FfxEncoding.DecodeScript(command.NameScriptBytes).GetString(FfxEncoding.UsDecoder));
                if (string.IsNullOrWhiteSpace(displayLabel))
                    displayLabel = Item_Dictionary.Instance.TryGetValue((ushort)index, out string? fallbackLabel)
                        ? fallbackLabel
                        : $"Item {index:D3}";

                string description = NormalizeText(FfxEncoding.DecodeScript(command.DescriptionScriptBytes).GetString(FfxEncoding.UsDecoder));
                if (string.IsNullOrWhiteSpace(description))
                    description = "No item description is available in the loaded item.bin sample.";

                entriesByIndex[index] = new ShopItemCatalogEntry
                {
                    Index = index,
                    DisplayLabel = displayLabel,
                    Description = description,
                    IconId = command.IconId
                };
            }

            return new ShopItemCatalog
            {
                EntriesByIndex = entriesByIndex,
                EntryCount = commands.Count,
                DistinctIconCount = entriesByIndex.Values.Select(entry => entry.IconId).Distinct().Count(),
                MaxIconId = entriesByIndex.Count == 0 ? (byte)0 : entriesByIndex.Values.Max(entry => entry.IconId)
            };
        }

        static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
        }
    }

    internal sealed class ShopItemCatalog
    {
        public required IReadOnlyDictionary<int, ShopItemCatalogEntry> EntriesByIndex { get; init; }
        public required int EntryCount { get; init; }
        public required int DistinctIconCount { get; init; }
        public required byte MaxIconId { get; init; }
    }

    internal sealed class ShopItemCatalogEntry
    {
        public required int Index { get; init; }
        public required string DisplayLabel { get; init; }
        public required string Description { get; init; }
        public required byte IconId { get; init; }
    }
}
