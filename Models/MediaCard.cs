using HyperMedia_Lite.Helpers;
using System;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace HyperMedia_Lite.Models
{
    /// <summary>
    /// 媒体卡片（专辑 / 视频 / 图片统一展示模型，首页演示用）。
    /// </summary>
    public class MediaCard
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public Color CoverColor { get; set; }

        public string Initial => string.IsNullOrEmpty(Title) ? "?" : Title.Substring(0, 1);

        private SolidColorBrush _coverBrush;
        public SolidColorBrush CoverBrush =>
            _coverBrush ?? (_coverBrush = new SolidColorBrush(CoverColor));
    }

    /// <summary>
    /// 左侧导航项。
    /// </summary>
    public class NavItem : BindableBase
    {
        private bool _isSelected;

        public string Glyph { get; set; }
        public string Label { get; set; }
        public string AutomationName { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
