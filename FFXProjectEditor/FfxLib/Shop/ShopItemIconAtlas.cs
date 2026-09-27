using Avalonia.Media.Imaging;
using FFXProjectEditor.FfxLib.Items;

namespace FFXProjectEditor.FfxLib.Shop
{
    internal static class ShopItemIconAtlas
    {
        public static int ProvedAssetCount => ItemIconAtlas.ProvedAssetCount;

        public static bool HasProvedAsset(ShopItemCatalogEntry? entry)
        {
            return TryResolveAssetUri(entry, out _);
        }

        public static bool TryResolveAssetUri(ShopItemCatalogEntry? entry, out string assetUri)
        {
            assetUri = string.Empty;
            if (entry == null)
                return false;

            return ItemIconAtlas.TryResolveAssetUri(entry.IconId, entry.DisplayLabel, out assetUri);
        }

        public static bool TryResolveBitmap(ShopItemCatalogEntry? entry, out Bitmap? bitmap)
        {
            bitmap = null;
            if (entry == null)
                return false;

            return ItemIconAtlas.TryResolveBitmap(entry.IconId, entry.DisplayLabel, out bitmap);
        }
    }
}
