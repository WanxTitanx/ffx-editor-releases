using Avalonia.Media.Imaging;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Items;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Services
{
    internal static class ItemIcon_Service
    {
        static string? cachedItemPath;
        static DateTime cachedWriteTimeUtc;
        static IReadOnlyDictionary<int, ItemIconDescriptor> cachedEntries = new Dictionary<int, ItemIconDescriptor>();

        public static void Invalidate()
        {
            cachedItemPath = null;
            cachedWriteTimeUtc = DateTime.MinValue;
            cachedEntries = new Dictionary<int, ItemIconDescriptor>();
        }

        public static bool TryResolveItem(ushort itemIndex, out ItemIconDescriptor descriptor)
        {
            IReadOnlyDictionary<int, ItemIconDescriptor> entries = EnsureEntries();
            if (entries.TryGetValue(itemIndex, out descriptor))
                return true;

            if (Item_Dictionary.Instance.TryGetValue(itemIndex, out string? fallbackName))
            {
                descriptor = new ItemIconDescriptor(itemIndex, fallbackName, string.Empty, 0);
                return true;
            }

            descriptor = default;
            return false;
        }

        public static bool TryResolveBitmap(ushort itemIndex, out Bitmap? bitmap)
        {
            bitmap = null;
            return TryResolveItem(itemIndex, out ItemIconDescriptor descriptor)
                && ItemIconAtlas.TryResolveBitmap(descriptor, out bitmap);
        }

        public static bool TryResolveBitmap(byte category, ushort itemIndex, out Bitmap? bitmap)
        {
            bitmap = null;
            return category == (byte)GameCategory_Enum.Items
                && TryResolveBitmap(itemIndex, out bitmap);
        }

        public static string BuildTooltip(ushort itemIndex)
        {
            if (!TryResolveItem(itemIndex, out ItemIconDescriptor descriptor))
                return $"Item #{itemIndex:D3}";

            string detail = ItemIconAtlas.HasProvedAsset(descriptor)
                ? "icon wired"
                : "icon asset not wired";
            return $"{descriptor.DisplayLabel} · item #{itemIndex:D3} · IconId {descriptor.IconId:X2}h · {detail}";
        }

        static IReadOnlyDictionary<int, ItemIconDescriptor> EnsureEntries()
        {
            string? itemPath = Project_Service.Instance.IsProjectLoaded
                ? Project_Service.Instance.Path_KernelItemUs
                : null;

            DateTime writeTimeUtc = !string.IsNullOrWhiteSpace(itemPath) && File.Exists(itemPath)
                ? File.GetLastWriteTimeUtc(itemPath)
                : DateTime.MinValue;

            if (string.Equals(cachedItemPath, itemPath, StringComparison.OrdinalIgnoreCase) &&
                cachedWriteTimeUtc == writeTimeUtc)
            {
                return cachedEntries;
            }

            cachedItemPath = itemPath;
            cachedWriteTimeUtc = writeTimeUtc;
            cachedEntries = LoadEntries(itemPath);
            return cachedEntries;
        }

        static IReadOnlyDictionary<int, ItemIconDescriptor> LoadEntries(string? itemPath)
        {
            if (string.IsNullOrWhiteSpace(itemPath) || !File.Exists(itemPath))
                return BuildFallbackEntries();

            try
            {
                List<Ability_Command> commands = Ability_Command.ReadList(File.ReadAllBytes(itemPath), hasExtraInfo: true);
                Dictionary<int, ItemIconDescriptor> entries = new(commands.Count);

                for (int index = 0; index < commands.Count; index++)
                {
                    Ability_Command command = commands[index];
                    string displayLabel = NormalizeText(FfxEncoding.DecodeScript(command.NameScriptBytes).GetString(FfxEncoding.UsDecoder));
                    if (string.IsNullOrWhiteSpace(displayLabel))
                        displayLabel = Item_Dictionary.Instance.TryGetValue((ushort)index, out string? fallbackLabel)
                            ? fallbackLabel
                            : $"Item {index:D3}";

                    string description = NormalizeText(FfxEncoding.DecodeScript(command.DescriptionScriptBytes).GetString(FfxEncoding.UsDecoder));

                    entries[index] = new ItemIconDescriptor(
                        index,
                        displayLabel,
                        description,
                        command.IconId);
                }

                return entries;
            }
            catch
            {
                return BuildFallbackEntries();
            }
        }

        static IReadOnlyDictionary<int, ItemIconDescriptor> BuildFallbackEntries()
        {
            return Item_Dictionary.Instance
                .ToDictionary(
                    pair => (int)pair.Key,
                    pair => new ItemIconDescriptor(pair.Key, pair.Value, string.Empty, 0));
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
}
