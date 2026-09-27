using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Shop
{
    internal static class ShopGearIconAtlas
    {
        const string AssetBaseUri = "avares://FFXProjectEditor/Assets/Shop/GearIcons/";
        static readonly Dictionary<string, Bitmap> BitmapCache = new(StringComparer.Ordinal);
        static readonly IReadOnlyDictionary<(Character_Enum Character, EquipmentStruct.EquipmentType_Enum Type), GearIconSpec> AssetByCharacterAndType = new Dictionary<(Character_Enum Character, EquipmentStruct.EquipmentType_Enum Type), GearIconSpec>
        {
            [(Character_Enum.Tidus, EquipmentStruct.EquipmentType_Enum.Weapon)] = new("TidusWeapon.png", "Tidus longsword"),
            [(Character_Enum.Yuna, EquipmentStruct.EquipmentType_Enum.Weapon)] = new("YunaWeapon.png", "Yuna rod"),
            [(Character_Enum.Auron, EquipmentStruct.EquipmentType_Enum.Weapon)] = new("AuronWeapon.png", "Auron blade"),
            [(Character_Enum.Kimahri, EquipmentStruct.EquipmentType_Enum.Weapon)] = new("KimahriWeapon.png", "Kimahri lance"),
            [(Character_Enum.Wakka, EquipmentStruct.EquipmentType_Enum.Weapon)] = new("WakkaWeapon.png", "Wakka blitzball"),
            [(Character_Enum.Lulu, EquipmentStruct.EquipmentType_Enum.Weapon)] = new("LuluWeapon.png", "Lulu doll"),
            [(Character_Enum.Rikku, EquipmentStruct.EquipmentType_Enum.Weapon)] = new("RikkuWeapon.png", "Rikku claw"),
            [(Character_Enum.Seymour, EquipmentStruct.EquipmentType_Enum.Weapon)] = new("SeymourWeapon.png", "Seymour weapon"),

            [(Character_Enum.Tidus, EquipmentStruct.EquipmentType_Enum.Armor)] = new("TidusArmor.png", "Tidus shield"),
            [(Character_Enum.Yuna, EquipmentStruct.EquipmentType_Enum.Armor)] = new("YunaArmor.png", "Yuna ring"),
            [(Character_Enum.Auron, EquipmentStruct.EquipmentType_Enum.Armor)] = new("AuronArmor.png", "Auron bracer"),
            [(Character_Enum.Kimahri, EquipmentStruct.EquipmentType_Enum.Armor)] = new("KimahriArmor.png", "Kimahri armlet"),
            [(Character_Enum.Wakka, EquipmentStruct.EquipmentType_Enum.Armor)] = new("WakkaArmor.png", "Wakka armguard"),
            [(Character_Enum.Lulu, EquipmentStruct.EquipmentType_Enum.Armor)] = new("LuluArmor.png", "Lulu bangle"),
            [(Character_Enum.Rikku, EquipmentStruct.EquipmentType_Enum.Armor)] = new("RikkuArmor.png", "Rikku targe"),
            [(Character_Enum.Seymour, EquipmentStruct.EquipmentType_Enum.Armor)] = new("SeymourArmor.png", "Seymour armor")
        };

        static readonly IReadOnlyDictionary<EquipmentStruct.EquipmentType_Enum, GearIconSpec> FallbackByType = new Dictionary<EquipmentStruct.EquipmentType_Enum, GearIconSpec>
        {
            [EquipmentStruct.EquipmentType_Enum.Weapon] = new("WeaponType.png", "weapon"),
            [EquipmentStruct.EquipmentType_Enum.Armor] = new("ArmorType.png", "armor")
        };

        public static int ProvedAssetCount => AssetByCharacterAndType.Count;

        public static bool TryResolveBitmap(ShopGearCatalogEntry? entry, out Bitmap? bitmap)
        {
            bitmap = null;
            if (!TryResolveAssetUri(entry, out _, out string assetUri))
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

        public static bool TryResolveDescriptor(ShopGearCatalogEntry? entry, out string descriptor)
        {
            descriptor = string.Empty;
            if (!TryResolveAssetUri(entry, out GearIconSpec? spec, out _))
                return false;

            descriptor = spec.Descriptor;
            return true;
        }

        static bool TryResolveAssetUri(ShopGearCatalogEntry? entry, out GearIconSpec? spec, out string assetUri)
        {
            spec = null;
            assetUri = string.Empty;
            if (entry == null)
                return false;

            if (!AssetByCharacterAndType.TryGetValue((entry.Equipment.Character, entry.Equipment.Type), out GearIconSpec? resolvedSpec)
                && !FallbackByType.TryGetValue(entry.Equipment.Type, out resolvedSpec))
            {
                return false;
            }

            spec = resolvedSpec;
            assetUri = AssetBaseUri + resolvedSpec.AssetFileName;
            return true;
        }

        sealed record GearIconSpec(string AssetFileName, string Descriptor);
    }
}
