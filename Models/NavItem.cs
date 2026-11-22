using HyperMedia_Lite.Helpers;

namespace HyperMedia_Lite.Models
{
    /// <summary>
    /// 汉堡菜单导航目标。每一项都对应一个可用的内容视图。
    /// </summary>
    public enum NavTarget
    {
        Music,
        Recent,
        Stats,
        Queue,
        Video,
        Photos,
        Favorites,
        Playlists
    }

    public sealed class NavItem : BindableBase
    {
        private bool _isSelected;
        private string _countText = string.Empty;

        public NavTarget Target { get; }

        public string Glyph { get; }

        public string Label { get; }

        public string CountText
        {
            get => _countText;
            set => SetProperty(ref _countText, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public NavItem(NavTarget target, string glyph, string label)
        {
            Target = target;
            Glyph = glyph;
            Label = label;
        }

        public void RefreshSelection()
        {
            OnPropertyChanged(nameof(IsSelected));
        }
    }
}
