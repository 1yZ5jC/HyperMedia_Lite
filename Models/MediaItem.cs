using HyperMedia_Lite.Helpers;
using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperMedia_Lite.Models
{
    /// <summary>
    /// 媒体类型。
    /// </summary>
    public enum MediaKind
    {
        Audio,
        Video,
        Photo
    }

    /// <summary>
    /// 一个真实的本地媒体文件。负责读取自身的元数据、缩略图与收藏状态。
    /// </summary>
    public sealed class MediaItem : BindableBase
    {
        private bool _isFavorite;
        private BitmapImage _thumbnail;
        private TimeSpan _duration;
        private string _durationText = string.Empty;
        private string _title;
        private string _subtitle;
        private string _album = string.Empty;

        public StorageFile File { get; }

        public MediaKind Kind { get; }

        public string Path => File?.Path ?? string.Empty;

        public string KindGlyph =>
            Kind == MediaKind.Audio ? "\uE8D6" :
            Kind == MediaKind.Video ? "\uE714" : "\uE722";

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public string Subtitle
        {
            get => _subtitle;
            private set => SetProperty(ref _subtitle, value);
        }

        /// <summary>所属专辑（用于「专辑 / 歌手」视图分组）。</summary>
        public string Album
        {
            get => _album;
            private set => SetProperty(ref _album, value);
        }

        public TimeSpan Duration
        {
            get => _duration;
            private set
            {
                if (SetProperty(ref _duration, value))
                {
                    DurationText = FormatDuration(value);
                }
            }
        }

        public string DurationText
        {
            get => _durationText;
            private set => SetProperty(ref _durationText, value);
        }

        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            private set
            {
                if (SetProperty(ref _thumbnail, value))
                {
                    OnPropertyChanged(nameof(HasThumbnail));
                }
            }
        }

        public bool HasThumbnail => Thumbnail != null;

        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetProperty(ref _isFavorite, value);
        }

        /// <summary>
        /// 无缩略图时的纯色底，按标题生成，保证同一文件颜色稳定。
        /// </summary>
        public SolidColorBrush FallbackBrush { get; }

        public string Initial => string.IsNullOrEmpty(Title) ? "?" : Title.Substring(0, 1);

        public MediaItem(StorageFile file, MediaKind kind)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            Kind = kind;
            _title = file.DisplayName;
            _subtitle = file.FileType.TrimStart('.').ToUpperInvariant() + " 文件";
            FallbackBrush = new SolidColorBrush(ColorFromTitle(file.DisplayName));
        }

        /// <summary>
        /// 读取系统元数据（标题、艺人/分辨率、时长），失败时保留由文件名推导的默认值。
        /// </summary>
        public async Task LoadPropertiesAsync()
        {
            try
            {
                if (Kind == MediaKind.Audio)
                {
                    var p = await File.Properties.GetMusicPropertiesAsync();
                    if (!string.IsNullOrWhiteSpace(p.Title))
                    {
                        Title = p.Title;
                    }
                    Album = p.Album ?? string.Empty;

                    string artist = p.Artist ?? string.Empty;
                    string album = p.Album ?? string.Empty;
                    Subtitle = !string.IsNullOrWhiteSpace(artist) ? artist
                        : !string.IsNullOrWhiteSpace(album) ? album
                        : File.FileType.TrimStart('.').ToUpperInvariant() + " 音频";
                    Duration = p.Duration;
                }
                else if (Kind == MediaKind.Video)
                {
                    var p = await File.Properties.GetVideoPropertiesAsync();
                    if (!string.IsNullOrWhiteSpace(p.Title))
                    {
                        Title = p.Title;
                    }
                    Subtitle = p.Width > 0 && p.Height > 0
                        ? $"{p.Width} × {p.Height}"
                        : File.FileType.TrimStart('.').ToUpperInvariant() + " 视频";
                    Duration = p.Duration;
                }
                else
                {
                    var p = await File.Properties.GetImagePropertiesAsync();
                    if (!string.IsNullOrWhiteSpace(p.Title))
                    {
                        Title = p.Title;
                    }
                    Subtitle = p.Width > 0 && p.Height > 0
                        ? $"{p.Width} × {p.Height}"
                        : File.FileType.TrimStart('.').ToUpperInvariant() + " 图片";
                }
            }
            catch (Exception)
            {
                // 部分文件类型不支持属性读取，保留默认值即可。
            }
        }

        /// <summary>
        /// 读取系统缩略图（音乐封面 / 视频首帧 / 图片预览）。
        /// </summary>
        public async Task LoadThumbnailAsync()
        {
            try
            {
                ThumbnailMode mode = Kind == MediaKind.Audio ? ThumbnailMode.MusicView
                    : Kind == MediaKind.Video ? ThumbnailMode.VideosView
                    : ThumbnailMode.PicturesView;

                using (var stream = await File.GetThumbnailAsync(mode, 240, ThumbnailOptions.ResizeThumbnail))
                {
                    if (stream != null && stream.Size > 0)
                    {
                        var image = new BitmapImage();
                        await image.SetSourceAsync(stream);
                        Thumbnail = image;
                    }
                }
            }
            catch (Exception)
            {
                // 无缩略图时显示 FallbackBrush + 首字母。
            }
        }

        /// <summary>
        /// 加载图片原图，供灯箱查看使用。
        /// </summary>
        public async Task<BitmapImage> LoadFullImageAsync()
        {
            var stream = await File.OpenAsync(FileAccessMode.Read);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
        }

        private static string FormatDuration(TimeSpan value)
        {
            if (value <= TimeSpan.Zero)
            {
                return string.Empty;
            }
            return value.TotalHours >= 1
                ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
                : $"{value.Minutes}:{value.Seconds:00}";
        }

        private static Color ColorFromTitle(string title)
        {
            int hash = 17;
            foreach (char c in title ?? string.Empty)
            {
                unchecked
                {
                    hash = hash * 31 + c;
                }
            }
            uint h = (uint)hash;
            byte r = (byte)(70 + h % 130);
            byte g = (byte)(70 + (h / 7) % 130);
            byte b = (byte)(70 + (h / 13) % 130);
            return Color.FromArgb(255, r, g, b);
        }
    }
}
