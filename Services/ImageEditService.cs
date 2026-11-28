using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperMedia_Lite.Services
{
    public enum AdjustFilter
    {
        None,
        Grayscale,
        Invert,
        Sepia,
        Warm,
        Cool,
        Film
    }

    /// <summary>可编辑图像：BGRA8 像素缓冲 + 尺寸信息。</summary>
    public sealed class EditableImage
    {
        public byte[] Pixels { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public uint SourceWidth { get; set; }
        public uint SourceHeight { get; set; }

        public EditableImage(byte[] pixels, int width, int height, uint sourceWidth, uint sourceHeight)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
        }

        public EditableImage Clone() =>
            new EditableImage((byte[])Pixels.Clone(), Width, Height, SourceWidth, SourceHeight);

        public async Task<WriteableBitmap> ToBitmapAsync()
        {
            var bitmap = new WriteableBitmap(Width, Height);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                await stream.WriteAsync(Pixels, 0, Pixels.Length);
            }
            return bitmap;
        }
    }

    /// <summary>
    /// 零依赖像素级图像处理：解码 → BGRA8 → 裁剪/调色 → 保存。
    /// 移植自 HyperViewer 的 ImageEditService。
    /// </summary>
    public static class ImageEditService
    {
        private const uint MaxDecodeLongEdge = 8192;

        public static async Task<EditableImage> LoadAsync(StorageFile file)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                uint sourceWidth = decoder.PixelWidth;
                uint sourceHeight = decoder.PixelHeight;
                uint decodeWidth = sourceWidth;
                uint decodeHeight = sourceHeight;

                var transform = new BitmapTransform();
                if (Math.Max(sourceWidth, sourceHeight) > MaxDecodeLongEdge)
                {
                    double scale = (double)MaxDecodeLongEdge / Math.Max(sourceWidth, sourceHeight);
                    decodeWidth = (uint)Math.Max(1, Math.Round(sourceWidth * scale));
                    decodeHeight = (uint)Math.Max(1, Math.Round(sourceHeight * scale));
                    transform.ScaledWidth = decodeWidth;
                    transform.ScaledHeight = decodeHeight;
                }

                var data = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                byte[] pixels = data.DetachPixelData();
                int orientation = await ReadOrientationAsync(decoder);
                var oriented = ApplyOrientation(pixels, (int)decodeWidth, (int)decodeHeight, orientation);
                return new EditableImage(oriented.Pixels, oriented.Width, oriented.Height, sourceWidth, sourceHeight);
            }
        }

        private static async Task<int> ReadOrientationAsync(BitmapDecoder decoder)
        {
            try
            {
                var props = await decoder.BitmapProperties.GetPropertiesAsync(new[] { "System.Photo.Orientation" });
                if (props.TryGetValue("System.Photo.Orientation", out var value) && value.Value != null)
                {
                    return Convert.ToInt32(value.Value);
                }
            }
            catch (Exception)
            {
                // 无 EXIF 方向
            }
            return 1;
        }

        private struct OrientedPixels
        {
            public byte[] Pixels;
            public int Width;
            public int Height;

            public OrientedPixels(byte[] pixels, int width, int height)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
            }
        }

        private static OrientedPixels ApplyOrientation(byte[] src, int width, int height, int orientation)
        {
            switch (orientation)
            {
                case 2: return new OrientedPixels(Flip(src, width, height, true), width, height);
                case 3: return new OrientedPixels(Rotate180Bytes(src, width, height), width, height);
                case 4: return new OrientedPixels(Flip(src, width, height, false), width, height);
                case 5: return new OrientedPixels(Transpose(src, width, height, false), height, width);
                case 6: return new OrientedPixels(Rotate90Bytes(src, width, height, true), height, width);
                case 7: return new OrientedPixels(Transpose(src, width, height, true), height, width);
                case 8: return new OrientedPixels(Rotate90Bytes(src, width, height, false), height, width);
                default: return new OrientedPixels(src, width, height);
            }
        }

        private static byte[] Rotate180Bytes(byte[] src, int w, int h)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int source = ((h - 1 - y) * w + (w - 1 - x)) * 4;
                    int target = (y * w + x) * 4;
                    dst[target] = src[source];
                    dst[target + 1] = src[source + 1];
                    dst[target + 2] = src[source + 2];
                    dst[target + 3] = src[source + 3];
                }
            }
            return dst;
        }

        private static byte[] Rotate90Bytes(byte[] src, int w, int h, bool clockwise)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int dx = clockwise ? h - 1 - y : y;
                    int dy = clockwise ? x : w - 1 - x;
                    int source = (y * w + x) * 4;
                    int target = (dy * h + dx) * 4;
                    dst[target] = src[source];
                    dst[target + 1] = src[source + 1];
                    dst[target + 2] = src[source + 2];
                    dst[target + 3] = src[source + 3];
                }
            }
            return dst;
        }

        private static byte[] Transpose(byte[] src, int w, int h, bool anti)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int dx = anti ? h - 1 - y : y;
                    int dy = anti ? w - 1 - x : x;
                    int si = (y * w + x) * 4;
                    int di = (dy * h + dx) * 4;
                    dst[di] = src[si];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
            return dst;
        }

        public static EditableImage Flip(EditableImage image, bool horizontal)
        {
            var pixels = Flip(image.Pixels, image.Width, image.Height, horizontal);
            return new EditableImage(pixels, image.Width, image.Height, image.SourceWidth, image.SourceHeight);
        }

        private static byte[] Flip(byte[] src, int w, int h, bool horizontal)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sx = horizontal ? w - 1 - x : x;
                    int sy = horizontal ? y : h - 1 - y;
                    int si = (sy * w + sx) * 4;
                    int di = (y * w + x) * 4;
                    dst[di] = src[si];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
            return dst;
        }

        public static EditableImage Rotate(EditableImage image, bool clockwise, int quarterTurns = 1)
        {
            var result = image;
            for (int i = 0; i < quarterTurns; i++)
            {
                result = Rotate90(result.Pixels, result.Width, result.Height, clockwise);
            }
            return result;
        }

        private static EditableImage Rotate90(byte[] src, int w, int h, bool clockwise)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int dx = clockwise ? h - 1 - y : y;
                    int dy = clockwise ? x : w - 1 - x;
                    int si = (y * w + x) * 4;
                    int di = (dy * h + dx) * 4;
                    dst[di] = src[si];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = src[si + 3];
                }
            }
            return new EditableImage(dst, h, w, (uint)w, (uint)h);
        }

        public static EditableImage Crop(EditableImage image, int x, int y, int width, int height)
        {
            width = Math.Max(1, Math.Min(width, image.Width - x));
            height = Math.Max(1, Math.Min(height, image.Height - y));
            var pixels = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                int sourceOffset = ((y + row) * image.Width + x) * 4;
                Array.Copy(image.Pixels, sourceOffset, pixels, row * width * 4, width * 4);
            }
            return new EditableImage(pixels, width, height, (uint)width, (uint)height);
        }

        public static async Task SaveAsync(StorageFile target, EditableImage image, bool jpeg, double quality = 0.95)
        {
            Guid encoderId = jpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId;
            using (var stream = await target.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    (uint)image.Width,
                    (uint)image.Height,
                    96,
                    96,
                    image.Pixels);

                if (jpeg)
                {
                    var properties = new BitmapPropertySet();
                    properties.Add("ImageQuality", new BitmapTypedValue(
                        Math.Max(0.0, Math.Min(1.0, quality)), PropertyType.Single));
                    await encoder.BitmapProperties.SetPropertiesAsync(properties);
                }
                await encoder.FlushAsync();
            }
        }

        public static void ApplyAdjustments(byte[] pixels, int brightness, int contrast, int saturation, AdjustFilter filter)
        {
            if (pixels == null || pixels.Length == 0)
            {
                return;
            }

            float b = brightness * 2.55f;
            float cs = 1f + contrast / 100f;
            float ss = 1f + saturation / 100f;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                float bl = pixels[i];
                float gr = pixels[i + 1];
                float rd = pixels[i + 2];

                bl = (bl - 128f) * cs + 128f + b;
                gr = (gr - 128f) * cs + 128f + b;
                rd = (rd - 128f) * cs + 128f + b;

                if (Math.Abs(ss - 1f) > 0.001f)
                {
                    float luma = 0.299f * rd + 0.587f * gr + 0.114f * bl;
                    rd = luma + (rd - luma) * ss;
                    gr = luma + (gr - luma) * ss;
                    bl = luma + (bl - luma) * ss;
                }

                switch (filter)
                {
                    case AdjustFilter.Grayscale:
                        float gray = 0.299f * rd + 0.587f * gr + 0.114f * bl;
                        rd = gray;
                        gr = gray;
                        bl = gray;
                        break;
                    case AdjustFilter.Invert:
                        rd = 255f - rd;
                        gr = 255f - gr;
                        bl = 255f - bl;
                        break;
                    case AdjustFilter.Sepia:
                        float sr = 0.393f * rd + 0.769f * gr + 0.189f * bl;
                        float sg = 0.349f * rd + 0.686f * gr + 0.168f * bl;
                        float sb = 0.272f * rd + 0.534f * gr + 0.131f * bl;
                        rd = sr;
                        gr = sg;
                        bl = sb;
                        break;
                    case AdjustFilter.Warm:
                        rd *= 1.10f;
                        bl *= 0.92f;
                        break;
                    case AdjustFilter.Cool:
                        rd *= 0.92f;
                        bl *= 1.10f;
                        break;
                    case AdjustFilter.Film:
                        float avg = (rd + gr + bl) / 3f;
                        rd = 255f * ((avg * 0.82f + rd * 0.18f) / 255f * 0.92f + 18f / 255f);
                        gr = avg * 0.82f + gr * 0.18f;
                        bl = avg * 0.82f + bl * 0.18f + 8f;
                        break;
                }

                pixels[i] = Clamp(bl);
                pixels[i + 1] = Clamp(gr);
                pixels[i + 2] = Clamp(rd);
            }
        }

        public static void ApplyWhiteBalance(byte[] pixels, int temperature, int tint)
        {
            if (pixels == null || pixels.Length == 0)
            {
                return;
            }
            float tr = 1f + temperature * 0.0016f;
            float tb = 1f - temperature * 0.0022f;
            float tR = 1f + tint * 0.0012f;
            float tB = 1f + tint * 0.0012f;
            float tG = 1f - tint * 0.0020f;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = Clamp(pixels[i] * tb * tB);
                pixels[i + 1] = Clamp(pixels[i + 1] * tG);
                pixels[i + 2] = Clamp(pixels[i + 2] * tr * tR);
            }
        }

        public static void ApplyBoxBlur(byte[] pixels, int width, int height, int radius)
        {
            if (pixels == null || pixels.Length == 0)
            {
                return;
            }
            radius = Math.Max(1, Math.Min(radius, Math.Min(64, (Math.Min(width, height) - 1) / 2)));
            var temp = new byte[pixels.Length];
            BlurPass(pixels, temp, width, height, radius, true);
            BlurPass(temp, pixels, width, height, radius, false);
        }

        private static void BlurPass(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
        {
            int span = r * 2 + 1;
            int inner = horizontal ? w : h;
            int outer = horizontal ? h : w;
            for (int o = 0; o < outer; o++)
            {
                long b = 0, g = 0, red = 0, a = 0;
                for (int k = -r; k <= r; k++)
                {
                    int idx = horizontal
                        ? (o * w + ClampIndex(k, inner)) * 4
                        : (ClampIndex(k, inner) * w + o) * 4;
                    b += src[idx];
                    g += src[idx + 1];
                    red += src[idx + 2];
                    a += src[idx + 3];
                }
                for (int i = 0; i < inner; i++)
                {
                    int outIndex = horizontal ? (o * w + i) * 4 : (i * w + o) * 4;
                    dst[outIndex] = (byte)(b / span);
                    dst[outIndex + 1] = (byte)(g / span);
                    dst[outIndex + 2] = (byte)(red / span);
                    dst[outIndex + 3] = (byte)(a / span);

                    int remove = ClampIndex(i - r, inner);
                    int add = ClampIndex(i + r + 1, inner);
                    int removeIndex = horizontal ? (o * w + remove) * 4 : (remove * w + o) * 4;
                    int addIndex = horizontal ? (o * w + add) * 4 : (add * w + o) * 4;
                    b += src[addIndex] - src[removeIndex];
                    g += src[addIndex + 1] - src[removeIndex + 1];
                    red += src[addIndex + 2] - src[removeIndex + 2];
                    a += src[addIndex + 3] - src[removeIndex + 3];
                }
            }
        }

        private static int ClampIndex(int value, int count)
        {
            if (value < 0) return 0;
            if (value >= count) return count - 1;
            return value;
        }

        public static void ApplySharpen(byte[] pixels, int width, int height, double amount)
        {
            if (pixels == null || pixels.Length == 0 || amount <= 0.01)
            {
                return;
            }
            var blur = new byte[pixels.Length];
            Array.Copy(pixels, blur, pixels.Length);
            ApplyBoxBlur(blur, width, height, 1);
            for (int i = 0; i < pixels.Length; i++)
            {
                int difference = pixels[i] - blur[i];
                pixels[i] = Clamp(pixels[i] + (float)(difference * amount));
            }
        }

        public static void ApplyVignette(byte[] pixels, int width, int height, float strength)
        {
            if (pixels == null || pixels.Length == 0 || strength <= 0f)
            {
                return;
            }
            double cx = (width - 1) / 2.0;
            double cy = (height - 1) / 2.0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double dx = (x - cx) / cx;
                    double dy = (y - cy) / cy;
                    double distance = dx * dx + dy * dy;
                    if (distance < 0.6)
                    {
                        continue;
                    }
                    double factor = 1.0 - strength * Math.Min(1.0, (distance - 0.6) / 1.4);
                    int index = (y * width + x) * 4;
                    pixels[index] = Clamp((float)(pixels[index] * factor));
                    pixels[index + 1] = Clamp((float)(pixels[index + 1] * factor));
                    pixels[index + 2] = Clamp((float)(pixels[index + 2] * factor));
                }
            }
        }

        /// <summary>把已栅格化的文字像素叠加到图片上（水印）。</summary>
        public static EditableImage ApplyWatermark(
            EditableImage image, byte[] textPixels, int tw, int th, int x, int y)
        {
            if (image == null || textPixels == null || tw <= 0 || th <= 0) return image;

            var result = image.Clone();
            int w = result.Width;
            int h = result.Height;
            int destX = x < 0 ? w - tw + x : x;
            int destY = y < 0 ? h - th + y : y;

            for (int ty = 0; ty < th; ty++)
            {
                int py = destY + ty;
                if (py < 0 || py >= h) continue;
                for (int tx = 0; tx < tw; tx++)
                {
                    int px = destX + tx;
                    if (px < 0 || px >= w) continue;

                    int si = (ty * tw + tx) * 4;
                    byte a = textPixels[si + 3];
                    if (a == 0) continue;
                    int di = (py * w + px) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        result.Pixels[di + c] = (byte)(
                            (textPixels[si + c] * a + result.Pixels[di + c] * (255 - a)) / 255);
                    }
                }
            }
            return result;
        }

        /// <summary>给图片加纯色边框（相框）。</summary>
        public enum FramePosition { All, Sides, TopBottom }

        public static EditableImage ApplyFrame(
            EditableImage image, Color color, int thickness, FramePosition position = FramePosition.All)
        {
            if (image == null || thickness <= 0) return image;
            var result = image.Clone();
            int w = result.Width;
            int h = result.Height;
            int t = Math.Min(thickness, Math.Min(w, h) / 2);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool insideX = x >= t && x < w - t;
                    bool insideY = y >= t && y < h - t;
                    bool inside = position == FramePosition.All ? insideX && insideY
                        : position == FramePosition.Sides ? insideX
                        : insideY;
                    if (inside) continue;
                    int i = (y * w + x) * 4;
                    result.Pixels[i] = color.B;
                    result.Pixels[i + 1] = color.G;
                    result.Pixels[i + 2] = color.R;
                    result.Pixels[i + 3] = color.A;
                }
            }
            return result;
        }

        public static EditableImage Downsample(EditableImage image, int maxEdge)
        {
            int width = image.Width;
            int height = image.Height;
            byte[] current = image.Pixels;

            while (Math.Max(width, height) > maxEdge && width > 1 && height > 1)
            {
                int newWidth = (width + 1) / 2;
                int newHeight = (height + 1) / 2;
                var next = new byte[newWidth * newHeight * 4];
                for (int y = 0; y < newHeight; y++)
                {
                    for (int x = 0; x < newWidth; x++)
                    {
                        int b = 0, g = 0, r = 0, a = 0, count = 0;
                        for (int dy = 0; dy < 2 && y * 2 + dy < height; dy++)
                        {
                            for (int dx = 0; dx < 2 && x * 2 + dx < width; dx++)
                            {
                                int source = ((y * 2 + dy) * width + x * 2 + dx) * 4;
                                b += current[source];
                                g += current[source + 1];
                                r += current[source + 2];
                                a += current[source + 3];
                                count++;
                            }
                        }
                        int target = (y * newWidth + x) * 4;
                        next[target] = (byte)(b / count);
                        next[target + 1] = (byte)(g / count);
                        next[target + 2] = (byte)(r / count);
                        next[target + 3] = (byte)(a / count);
                    }
                }
                current = next;
                width = newWidth;
                height = newHeight;
            }

            return new EditableImage(current, width, height, image.SourceWidth, image.SourceHeight);
        }

        private static byte Clamp(float value)
        {
            if (value < 0f) return 0;
            if (value > 255f) return 255;
            return (byte)value;
        }
    }
}
