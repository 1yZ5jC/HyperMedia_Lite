using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HyperMedia_Lite.Models;
using Windows.Graphics.Imaging;
using Windows.Storage.FileProperties;
using Windows.UI;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 从专辑封面 / 视频帧提取主色，用于驱动沉浸页强调色。
    /// 使用小尺寸解码 + 色彩量化，后台计算并缓存；失败时回退系统强调色。
    /// </summary>
    public static class AccentColorService
    {
        private const int SampleSize = 32;
        private static readonly Dictionary<string, Color> Cache = new Dictionary<string, Color>();

        public static async Task<Color?> ExtractAsync(MediaItem item)
        {
            if (item?.File == null || string.IsNullOrEmpty(item.Path))
            {
                return null;
            }

            lock (Cache)
            {
                if (Cache.TryGetValue(item.Path, out Color cached))
                {
                    return cached;
                }
            }

            try
            {
                var mode = item.Kind == MediaKind.Video ? ThumbnailMode.VideosView : ThumbnailMode.MusicView;
                using (var thumbnail = await item.File.GetThumbnailAsync(mode, 128, ThumbnailOptions.ResizeThumbnail))
                {
                    if (thumbnail == null || thumbnail.Size == 0)
                    {
                        return null;
                    }

                    var decoder = await BitmapDecoder.CreateAsync(thumbnail);
                    var transform = new BitmapTransform
                    {
                        ScaledWidth = (uint)SampleSize,
                        ScaledHeight = (uint)SampleSize
                    };
                    var data = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.DoNotColorManage);

                    byte[] pixels = data.DetachPixelData();
                    Color? result = Quantize(pixels);
                    if (result.HasValue)
                    {
                        lock (Cache)
                        {
                            Cache[item.Path] = result.Value;
                        }
                    }
                    return result;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Color? Quantize(byte[] pixels)
        {
            try
            {
                const int bucketBits = 4; // 每通道 16 级
                var counts = new Dictionary<int, int>();
                long sumR = 0, sumG = 0, sumB = 0;
                int total = 0;

                for (int i = 0; i + 3 < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];
                    if (a < 16) continue;

                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));
                    if (max < 40 || max - min < 20) continue; // 跳过过暗与近灰像素

                    int key = ((r >> bucketBits) << 8) | ((g >> bucketBits) << 4) | (b >> bucketBits);
                    counts.TryGetValue(key, out int count);
                    counts[key] = count + 1;
                    sumR += r;
                    sumG += g;
                    sumB += b;
                    total++;
                }

                if (counts.Count == 0 || total == 0)
                {
                    return null;
                }

                // 选出现次数最多的量化桶，再在该桶内取平均，得到稳定主色。
                int bestKey = counts.Aggregate((a, b) => b.Value > a.Value ? b : a).Key;
                long br = 0, bg = 0, bb = 0, bc = 0;
                for (int i = 0; i + 3 < pixels.Length; i += 4)
                {
                    byte blue = pixels[i];
                    byte green = pixels[i + 1];
                    byte red = pixels[i + 2];
                    int key = ((red >> bucketBits) << 8) | ((green >> bucketBits) << 4) | (blue >> bucketBits);
                    if (key != bestKey) continue;
                    br += red;
                    bg += green;
                    bb += blue;
                    bc++;
                }

                if (bc == 0)
                {
                    return Color.FromArgb(255, (byte)(sumR / total), (byte)(sumG / total), (byte)(sumB / total));
                }

                byte rr = (byte)Math.Min(255, br / bc + 24);
                byte gg = (byte)Math.Min(255, bg / bc + 24);
                byte bb2 = (byte)Math.Min(255, bb / bc + 24);
                return Color.FromArgb(255, rr, gg, bb2);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void ClearCache()
        {
            lock (Cache)
            {
                Cache.Clear();
            }
        }
    }
}
