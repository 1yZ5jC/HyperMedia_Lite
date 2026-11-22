using HyperMedia_Lite.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperMedia_Lite.Services
{
    /// <summary>图片异步解码与缩略图 LRU 缓存，参考 HyperViewer 的 ImageLoaderService。</summary>
    public static class ImageLoaderService
    {
        private const int ThumbCacheMax = 512;
        private static readonly Dictionary<string, BitmapImage> ThumbCache = new Dictionary<string, BitmapImage>();
        private static readonly List<string> ThumbOrder = new List<string>();
        private static readonly object CacheLock = new object();

        private static string Key(StorageFile file, int size) =>
            file == null ? null : file.Path + "@" + size;

        private static BitmapImage CacheGet(string key)
        {
            lock (CacheLock)
            {
                if (key != null && ThumbCache.TryGetValue(key, out var hit))
                {
                    ThumbOrder.Remove(key);
                    ThumbOrder.Add(key);
                    return hit;
                }
                return null;
            }
        }

        private static void CachePut(string key, BitmapImage bitmap)
        {
            if (key == null || bitmap == null)
            {
                return;
            }
            lock (CacheLock)
            {
                if (ThumbCache.ContainsKey(key))
                {
                    ThumbOrder.Remove(key);
                }
                ThumbCache[key] = bitmap;
                ThumbOrder.Add(key);
                while (ThumbOrder.Count > ThumbCacheMax)
                {
                    string victim = ThumbOrder[0];
                    ThumbOrder.RemoveAt(0);
                    ThumbCache.Remove(victim);
                }
            }
        }

        public static void ClearCache()
        {
            lock (CacheLock)
            {
                ThumbCache.Clear();
                ThumbOrder.Clear();
            }
        }

        /// <summary>加载主图：大于 4K 的静态图降采样，GIF 保留原尺寸。</summary>
        public static async Task<BitmapImage> LoadAsync(MediaItem item)
        {
            if (item?.File == null)
            {
                return null;
            }

            try
            {
                using (var stream = await item.File.OpenAsync(FileAccessMode.Read))
                {
                    var image = new BitmapImage();
                    bool isGif = string.Equals(item.File.FileType, ".gif", StringComparison.OrdinalIgnoreCase);
                    if (!isGif && item.PixelWidth > 4096)
                    {
                        image.DecodePixelWidth = 4096;
                        image.DecodePixelType = DecodePixelType.Logical;
                    }
                    await image.SetSourceAsync(stream);
                    return image;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>加载缩略图（缓存优先），可选择降采样宽度。</summary>
        public static async Task<BitmapImage> LoadThumbnailAsync(StorageFile file, int size)
        {
            string key = Key(file, size);
            BitmapImage cached = CacheGet(key);
            if (cached != null)
            {
                return cached;
            }

            BitmapImage result = null;
            try
            {
                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    result = new BitmapImage();
                    result.DecodePixelWidth = size;
                    result.DecodePixelType = DecodePixelType.Logical;
                    await result.SetSourceAsync(stream);
                }
            }
            catch (Exception)
            {
                result = null;
            }

            if (result != null)
            {
                CachePut(key, result);
            }
            return result;
        }

        /// <summary>受限并发预加载一批缩略图，结果通过回调回到 UI 线程。</summary>
        public static async Task PreloadAsync(
            IReadOnlyList<MediaItem> items,
            int size,
            Action<MediaItem, BitmapImage> onLoaded,
            int maxConcurrency = 4)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            using (var semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency)))
            {
                var tasks = new List<Task>();
                foreach (var item in items)
                {
                    tasks.Add(LoadOneAsync(semaphore, item, size, onLoaded));
                }
                await Task.WhenAll(tasks);
            }
        }

        private static async Task LoadOneAsync(
            SemaphoreSlim semaphore,
            MediaItem item,
            int size,
            Action<MediaItem, BitmapImage> onLoaded)
        {
            await semaphore.WaitAsync();
            try
            {
                var bitmap = await LoadThumbnailAsync(item.File, size);
                if (bitmap == null)
                {
                    return;
                }
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () => onLoaded?.Invoke(item, bitmap));
            }
            catch (Exception)
            {
                // ignored
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
