using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyperMedia_Lite.Helpers
{
    /// <summary>歌曲列表行模板选择：专辑标题行 / 普通歌曲行。</summary>
    public sealed class SongRowTemplateSelector : DataTemplateSelector
    {
        public DataTemplate HeaderTemplate { get; set; }
        public DataTemplate ItemTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            return item is ViewModels.AlbumSongGroup ? HeaderTemplate : ItemTemplate;
        }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            return SelectTemplateCore(item);
        }
    }
}
