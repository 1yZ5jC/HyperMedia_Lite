using HyperMedia_Lite.Models;
using HyperMedia_Lite.ViewModels;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 主页：左侧导航 + 媒体内容区 + 底部常驻播放条。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly MainViewModel _viewModel;

        public MainPage()
        {
            this.InitializeComponent();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // 默认进入「专辑」页签，展示媒体网格
            SetTab(2);
        }

        #region 导航

        private void NavList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is NavItem item)
            {
                int index = _viewModel.NavItems.IndexOf(item);
                _viewModel.Select(index);
            }
        }

        #endregion

        #region 选项卡 / 内容切换

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag && int.TryParse(tag, out int index))
            {
                SetTab(index);
            }
        }

        private void SetTab(int index)
        {
            // 内容区域：仅「专辑」显示网格，其余显示空状态
            bool showAlbums = index == 2;
            AlbumGrid.Visibility = showAlbums ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = showAlbums ? Visibility.Collapsed : Visibility.Visible;

            // 选项卡下划线与文字颜色
            ApplyTabStyle(TabSongs, UnderlineSongs, index == 0);
            ApplyTabStyle(TabArtists, UnderlineArtists, index == 1);
            ApplyTabStyle(TabAlbums, UnderlineAlbums, index == 2);
        }

        private static void ApplyTabStyle(Button tab, Rectangle underline, bool selected)
        {
            underline.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;

            var resources = Application.Current.Resources;
            var brushKey = selected ? "TextPrimaryBrush" : "TextSecondaryBrush";
            if (resources[brushKey] is SolidColorBrush brush)
            {
                tab.Foreground = brush;
            }
        }

        #endregion
    }
}
