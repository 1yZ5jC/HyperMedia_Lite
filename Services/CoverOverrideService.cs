using HyperMedia_Lite.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 封面覆盖：UWP 无法把专辑图写回音频文件，改为把用户选的图片
    /// 保存到 LocalState\CoverOverrides，并按媒体路径哈希关联。
    /// </summary>
    public static class CoverOverrideService
    {
        private const string FolderName = "CoverOverrides";

        private static string KeyFor(MediaItem item)
        {
            uint hash = 2166136261;
            foreach (char c in item?.Path ?? string.Empty)
            {
                unchecked { hash ^= c; hash *= 16777619; }
            }
            return hash.ToString("X8") + ".png";
        }

        public static async Task<BitmapImage> SaveAsync(MediaItem item, StorageFile imageFile)
        {
            if (item == null || imageFile == null) return null;

            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                FolderName, CreationCollisionOption.OpenIfExists);
            string name = KeyFor(item);
            var target = await imageFile.CopyAsync(folder, name, NameCollisionOption.ReplaceExisting);
            return await LoadBitmapAsync(target);
        }

        public static async Task<BitmapImage> TryLoadAsync(MediaItem item)
        {
            if (item == null) return null;
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.GetFolderAsync(FolderName);
                var file = await folder.GetFileAsync(KeyFor(item));
                return await LoadBitmapAsync(file);
            }
            catch
            {
                return null;
            }
        }

        public static async Task ClearAsync(MediaItem item)
        {
            if (item == null) return;
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.GetFolderAsync(FolderName);
                var file = await folder.GetFileAsync(KeyFor(item));
                await file.DeleteAsync();
            }
            catch { }
        }

        private static async Task<BitmapImage> LoadBitmapAsync(StorageFile file)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            {
                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = 600;
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
        }
    }
}
