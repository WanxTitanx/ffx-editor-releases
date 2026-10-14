using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Text;

namespace FFXProjectEditor.FfxLib.Items
{
    internal readonly record struct ItemIconDescriptor(int Index, string DisplayLabel, string Description, byte IconId);

    internal static class ItemIconAtlas
    {
        const string AssetBaseUri = "avares://FFXProjectEditor/Assets/Shop/ItemIcons/";

        static readonly Dictionary<string, Bitmap> BitmapCache = new(StringComparer.Ordinal);

        static readonly IReadOnlyDictionary<byte, string> AssetByIconId = new Dictionary<byte, string>
        {
            [0x16] = "ItemIcon16_PotionGroup.png",
            [0x17] = "ItemIcon17_EtherGroup.png",
            [0x18] = "ItemIcon18_ElixirGroup.png",
            [0x19] = "ItemIcon19_StatusCureGroup.png",
            [0x1A] = "ItemIcon1A_PhoenixGroup.png",
            [0x1D] = "ItemIcon1D_BattleItemGroup.png",
            [0x1E] = "ItemIcon1E_AlBhedPotionGroup.png",
            [0x1F] = "ItemIcon1F_SupportUseGroup.png",
            [0x22] = "ItemIcon22_Sphere.png",
            [0x23] = "ItemIcon23_YellowSphere.png",
            [0x26] = "ItemIcon26_PurpleSphere.png",
            [0x27] = "ItemIcon27_BlackSphere.png",
            [0x28] = "ItemIcon28_BlueSphere.png",
            [0x29] = "ItemIcon29_KeySphere.png",
            [0x2A] = "ItemIcon2A_KeyItem.png",
            [0x2C] = "ItemIcon2C_Map.png"
        };

        static readonly IReadOnlyDictionary<string, string> AssetByNormalizedLabel = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["albhedpotion"] = "ItemIcon1E_AlBhedPotionGroup.png",
            ["antidote"] = "ItemIcon19_StatusCureGroup.png",
            ["clearsphere"] = "ItemIcon27_BlackSphere.png",
            ["elixir"] = "ItemIcon18_ElixirGroup.png",
            ["ether"] = "ItemIcon17_EtherGroup.png",
            ["friendsphere"] = "ItemIcon28_BlueSphere.png",
            ["grenade"] = "ItemIcon1D_BattleItemGroup.png",
            ["hipotion"] = "ItemIcon16_PotionGroup.png",
            ["keysphere"] = "ItemIcon29_KeySphere.png",
            ["map"] = "ItemIcon2C_Map.png",
            ["megalixir"] = "ItemIcon18_ElixirGroup.png",
            ["megaelixir"] = "ItemIcon18_ElixirGroup.png",
            ["megaphoenix"] = "ItemIcon1A_PhoenixGroup.png",
            ["megapotion"] = "ItemIcon16_PotionGroup.png",
            ["phoenixdown"] = "ItemIcon1A_PhoenixGroup.png",
            ["potion"] = "ItemIcon16_PotionGroup.png",
            ["powerdistiller"] = "ItemIcon1F_SupportUseGroup.png",
            ["returnsphere"] = "ItemIcon28_BlueSphere.png",
            ["turboether"] = "ItemIcon17_EtherGroup.png"
        };

        public static int ProvedAssetCount => AssetByIconId.Count;

        public static bool HasProvedAsset(ItemIconDescriptor? entry)
        {
            return TryResolveAssetUri(entry, out _);
        }

        public static bool HasProvedAsset(byte iconId, string? displayLabel)
        {
            return TryResolveAssetUri(iconId, displayLabel, out _);
        }

        public static bool TryResolveAssetUri(ItemIconDescriptor? entry, out string assetUri)
        {
            assetUri = string.Empty;
            if (entry == null)
                return false;

            return TryResolveAssetUri(entry.Value.IconId, entry.Value.DisplayLabel, out assetUri);
        }

        public static bool TryResolveAssetUri(byte iconId, string? displayLabel, out string assetUri)
        {
            assetUri = string.Empty;
            if (!TryResolveAssetFileName(iconId, displayLabel, out string assetFileName))
                return false;

            assetUri = AssetBaseUri + assetFileName;
            return true;
        }

        public static bool TryResolveBitmap(ItemIconDescriptor? entry, out Bitmap? bitmap)
        {
            bitmap = null;
            if (entry == null)
                return false;

            return TryResolveBitmap(entry.Value.IconId, entry.Value.DisplayLabel, out bitmap);
        }

        public static bool TryResolveBitmap(byte iconId, string? displayLabel, out Bitmap? bitmap)
        {
            bitmap = null;
            if (!TryResolveAssetUri(iconId, displayLabel, out string assetUri))
                return false;

            if (BitmapCache.TryGetValue(assetUri, out Bitmap? cached))
            {
                bitmap = cached;
                return true;
            }

            try
            {
                using var stream = AssetLoader.Open(new Uri(assetUri));
                bitmap = new Bitmap(stream);
                BitmapCache[assetUri] = bitmap;
                return true;
            }
            catch
            {
                bitmap = null;
                return false;
            }
        }

        static bool TryResolveAssetFileName(byte iconId, string? displayLabel, out string assetFileName)
        {
            if (AssetByIconId.TryGetValue(iconId, out string? iconAssetFileName))
            {
                assetFileName = iconAssetFileName;
                return true;
            }

            if (AssetByNormalizedLabel.TryGetValue(NormalizeLabel(displayLabel), out string? labelAssetFileName))
            {
                assetFileName = labelAssetFileName;
                return true;
            }

            assetFileName = string.Empty;
            return false;
        }

        static string NormalizeLabel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            StringBuilder builder = new(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                    builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }
    }
}
