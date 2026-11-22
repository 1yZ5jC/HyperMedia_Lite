using HyperMedia_Lite.Models;
using HyperMedia_Lite.Services;
using HyperMedia_Lite.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 主页：Groove 风格窄导航 + 汉堡菜单 + 媒体库 + 全局播放层 + 图片灯箱。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly MainViewModel _viewModel;
        private readonly PlaybackService _playback = PlaybackService.Current;

        private List<MediaItem> _photoItems = new List<MediaItem>();
        private int _photoIndex = -1;

        public MainPage()
        {
            InitializeComponent();

            _viewModel = App.MainViewModel;
            DataContext = _viewModel;

            // 视频画面由同一个 MediaPlayer 渲染，音频播放时该元素自动隐藏。
            VideoSurface.SetMediaPlayer(_playback.Player);

            _viewModel.PhotoRequested += OnPhotoRequested;
            _viewModel.PlayerRequested += OnPlayerRequested;

            Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainPage_Loaded;
            await _viewModel.InitializeAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // 视图模型是全局共享的，离开页面时解除订阅，避免返回后重复注册。
            _viewModel.PhotoRequested -= OnPhotoRequested;
            _viewModel.PlayerRequested -= OnPlayerRequested;
            base.OnNavigatedFrom(e);
        }

        #region 汉堡菜单 / 导航

        private void Hamburger_Click(object sender, RoutedEventArgs e)
        {
            if (MenuOverlay.Visibility == Visibility.Visible)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            OpenMenu();
            SearchBox.Focus(FocusState.Programmatic);
        }

        private void ClosePane_Click(object sender, RoutedEventArgs e)
        {
            CloseMenu();
        }

        private void ClickAway_Tapped(object sender, TappedRoutedEventArgs e)
        {
            CloseMenu();
        }

        private void OpenMenu()
        {
            MenuOverlay.Visibility = Visibility.Visible;
            ClickAway.Visibility = Visibility.Visible;
        }

        private void CloseMenu()
        {
            MenuOverlay.Visibility = Visibility.Collapsed;
            ClickAway.Visibility = Visibility.Collapsed;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (PlayerOverlay.Visibility == Visibility.Visible)
            {
                PlayerOverlay.Visibility = Visibility.Collapsed;
            }
            else if (PhotoOverlay.Visibility == Visibility.Visible)
            {
                PhotoOverlay.Visibility = Visibility.Collapsed;
            }
            else if (MenuOverlay.Visibility == Visibility.Visible)
            {
                CloseMenu();
            }
        }

        private void NavList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is NavItem item)
            {
                _viewModel.Select(item.Target);
                CloseMenu();
            }
        }

        #endregion

        #region 内容区

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag && int.TryParse(tag, out int index))
            {
                _viewModel.SetTab(index);
            }
        }

        private void AlbumGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            _ = _viewModel.OpenAlbumAsync(e.ClickedItem as AlbumCard);
        }

        private void SongList_ItemClick(object sender, ItemClickEventArgs e)
        {
            _ = _viewModel.OpenItemAsync(e.ClickedItem as MediaItem);
        }

        private void ArtistGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            _viewModel.OpenArtist(e.ClickedItem as ArtistCard);
        }

        private void MediaGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            _ = _viewModel.OpenItemAsync(e.ClickedItem as MediaItem);
        }

        private void Favorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is MediaItem item)
            {
                _viewModel.ToggleFavorite(item);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _viewModel.SearchText = SearchBox.Text;
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            Frame.Navigate(typeof(SettingsPage));
        }

        #endregion

        #region 播放界面

        private void OnPlayerRequested(object sender, MediaItem item)
        {
            bool isVideo = item != null && item.Kind == MediaKind.Video;
            VideoSurface.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
            AudioPanel.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
            PlayerOverlay.Visibility = Visibility.Visible;
        }

        private void ClosePlayer_Click(object sender, RoutedEventArgs e)
        {
            PlayerOverlay.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region 图片灯箱

        private void OnPhotoRequested(object sender, MediaItem item)
        {
            _photoItems = _viewModel.CurrentItems.Where(i => i.Kind == MediaKind.Photo).ToList();
            _photoIndex = _photoItems.IndexOf(item);
            PhotoOverlay.Visibility = Visibility.Visible;
            _ = ShowPhotoAsync();
        }

        private async System.Threading.Tasks.Task ShowPhotoAsync()
        {
            if (_photoIndex < 0 || _photoIndex >= _photoItems.Count)
            {
                return;
            }

            var item = _photoItems[_photoIndex];
            PhotoTitle.Text = item.Title;
            try
            {
                PhotoImage.Source = await item.LoadFullImageAsync();
            }
            catch (Exception)
            {
                PhotoImage.Source = null;
                PhotoTitle.Text = item.Title + "（无法打开此图片）";
            }
        }

        private void PhotoPrevious_Click(object sender, RoutedEventArgs e)
        {
            if (_photoItems.Count == 0)
            {
                return;
            }
            _photoIndex = (_photoIndex - 1 + _photoItems.Count) % _photoItems.Count;
            _ = ShowPhotoAsync();
        }

        private void PhotoNext_Click(object sender, RoutedEventArgs e)
        {
            if (_photoItems.Count == 0)
            {
                return;
            }
            _photoIndex = (_photoIndex + 1) % _photoItems.Count;
            _ = ShowPhotoAsync();
        }

        private void ClosePhoto_Click(object sender, RoutedEventArgs e)
        {
            PhotoOverlay.Visibility = Visibility.Collapsed;
        }

        #endregion
    }
}
