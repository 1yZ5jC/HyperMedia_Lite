using HyperMedia_Lite.Models;
using System;
using System.Collections.ObjectModel;
using Windows.UI;

namespace HyperMedia_Lite.ViewModels
{
    /// <summary>
    /// 首页 ViewModel：演示用示例数据 + 导航选中状态。
    /// 后续接入真实媒体库扫描后，仅替换数据来源即可。
    /// </summary>
    public sealed class MainViewModel
    {
        public ObservableCollection<NavItem> NavItems { get; } = new ObservableCollection<NavItem>();
        public ObservableCollection<MediaCard> Albums { get; } = new ObservableCollection<MediaCard>();

        // 顶部工具条
        public int TotalSongCount { get; set; } = 210;
        public string PlayAllText { get; set; } = "无序播放所有 (210)";
        public string SortDescription { get; set; } = "添加时间";

        // 底部播放条（示例曲目）
        public string TrackTitle { get; set; } = "二人";
        public string TrackArtist { get; set; } = "aiko";
        public string CurrentTimeText { get; set; } = "0:50";
        public string TotalTimeText { get; set; } = "4:59";

        public MainViewModel()
        {
            BuildNavItems();
            BuildAlbums();
        }

        private void BuildNavItems()
        {
            // Glyph 取自 Segoe MDL2 Assets 字体（私有使用区码位）
            NavItems.Add(new NavItem { Glyph = "\uE700", Label = "菜单", AutomationName = "菜单" });
            NavItems.Add(new NavItem { Glyph = "\uE721", Label = "搜索", AutomationName = "搜索" });
            NavItems.Add(new NavItem { Glyph = "\uE8D6", Label = "音乐", AutomationName = "音乐" });
            NavItems.Add(new NavItem { Glyph = "\uE8B2", Label = "最近", AutomationName = "最近播放" });
            NavItems.Add(new NavItem { Glyph = "\uE8A5", Label = "统计", AutomationName = "统计" });
            NavItems.Add(new NavItem { Glyph = "\uE8A6", Label = "列表", AutomationName = "播放列表" });
            NavItems.Add(new NavItem { Glyph = "\uE710", Label = "添加", AutomationName = "添加媒体" });

            Select(2); // 默认选中「音乐」
        }

        private void BuildAlbums()
        {
            Albums.Add(new MediaCard
            {
                Title = "Midnights (3am Edition)",
                Subtitle = "Taylor Swift",
                CoverColor = ColorFromHex(0x5B, 0x4E, 0x9E)
            });
            Albums.Add(new MediaCard
            {
                Title = "最伟大的作品",
                Subtitle = "周杰伦",
                CoverColor = ColorFromHex(0x4E, 0x8C, 0xC7)
            });
            Albums.Add(new MediaCard
            {
                Title = "すずめの戸締まり",
                Subtitle = "RADWIMPS",
                CoverColor = ColorFromHex(0x3F, 0xA7, 0xA0)
            });
            Albums.Add(new MediaCard
            {
                Title = "果てしない二人",
                Subtitle = "aiko",
                CoverColor = ColorFromHex(0xE0, 0x7A, 0x5F)
            });
            Albums.Add(new MediaCard
            {
                Title = "素颜",
                Subtitle = "许嵩 & 何曼婷",
                CoverColor = ColorFromHex(0xD9, 0x8C, 0xB3)
            });
            Albums.Add(new MediaCard
            {
                Title = "夏恋のライブ",
                Subtitle = "aiko",
                CoverColor = ColorFromHex(0x9F, 0xB8, 0x4A)
            });
            Albums.Add(new MediaCard
            {
                Title = "花の塔",
                Subtitle = "さユり",
                CoverColor = ColorFromHex(0xB5, 0x54, 0x4A)
            });
            Albums.Add(new MediaCard
            {
                Title = "TVアニメ「であいもん」",
                Subtitle = "ayaho",
                CoverColor = ColorFromHex(0x4A, 0x5D, 0x8F)
            });
            Albums.Add(new MediaCard
            {
                Title = "ねがう夜",
                Subtitle = "aiko",
                CoverColor = ColorFromHex(0x7A, 0x8B, 0x7A)
            });
            Albums.Add(new MediaCard
            {
                Title = "失恋ソング沢山聴いて",
                Subtitle = "りりあ。",
                CoverColor = ColorFromHex(0x8C, 0x6D, 0x5B)
            });
        }

        /// <summary>
        /// 选中指定索引的导航项。
        /// </summary>
        public void Select(int index)
        {
            if (index < 0 || index >= NavItems.Count)
            {
                return;
            }

            for (int i = 0; i < NavItems.Count; i++)
            {
                NavItems[i].IsSelected = i == index;
            }
        }

        private static Color ColorFromHex(byte r, byte g, byte b)
        {
            return Color.FromArgb(255, r, g, b);
        }
    }
}
