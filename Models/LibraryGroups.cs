using HyperMedia_Lite.Helpers;
using System;
using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperMedia_Lite.Models
{
    /// <summary>
    /// 「专辑」视图的卡片：由真实音频文件的专辑元数据分组而来。
    /// </summary>
    public sealed class AlbumCard : BindableBase
    {
        private BitmapImage _thumbnail;

        public string Title { get; set; }

        public string Subtitle { get; set; }

        public string Initial => string.IsNullOrEmpty(Title) ? "?" : Title.Substring(0, 1);

        public SolidColorBrush FallbackBrush { get; set; }

        public int SongCount { get; set; }

        /// <summary>该专辑包含的全部曲目。</summary>
        public List<MediaItem> Items { get; set; }

        /// <summary>该专辑的第一首曲目，点击卡片时从此曲开始播放整张专辑。</summary>
        public MediaItem FirstItem { get; set; }

        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (SetProperty(ref _thumbnail, value))
                {
                    OnPropertyChanged(nameof(HasThumbnail));
                }
            }
        }

        public bool HasThumbnail => Thumbnail != null;

        public static SolidColorBrush ColorFor(string name)
        {
            int hash = 17;
            foreach (char c in name ?? string.Empty)
            {
                unchecked
                {
                    hash = hash * 31 + c;
                }
            }
            uint h = (uint)hash;
            return new SolidColorBrush(Color.FromArgb(255,
                (byte)(70 + h % 130),
                (byte)(70 + (h / 7) % 130),
                (byte)(70 + (h / 13) % 130)));
        }
    }

    /// <summary>
    /// 「歌手」视图的卡片：由真实音频文件的艺人元数据分组而来。
    /// </summary>
    public sealed class ArtistCard : BindableBase
    {
        public string Name { get; set; }

        public int SongCount { get; set; }

        public string SongCountText => $"{SongCount} 首歌曲";

        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1);

        public SolidColorBrush FallbackBrush { get; set; }

        public static SolidColorBrush ColorFor(string name) => AlbumCard.ColorFor(name);
    }
}
