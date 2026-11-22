using HyperMedia_Lite.Helpers;
using HyperMedia_Lite.Models;
using HyperMedia_Lite.Services;
using HyperMedia_Lite.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Media.Playback;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;
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
        private double _photoRotation;
        private int _flipH = 1;
        private int _flipV = 1;
        private bool _updatingZoomSlider;
        private bool _infoOpen;
        private bool _slideRunning;
        private double _slideElapsed;
        private int _photoLoadId;
        private DispatcherTimer _slideTimer;
        private MediaPlayerElement _videoElement;
        private MediaElement _legacyVideoElement;
        private MediaItem _externalVideoItem;
        private DispatcherTimer _videoIdleTimer;

        public MainPage()
        {
            InitializeComponent();

            _viewModel = App.MainViewModel;
            DataContext = _viewModel;

            // 14393+：动态创建 MediaPlayerElement 并绑定同一个 MediaPlayer；10240 上不存在该类型。
            SetupVideoSurface();

            _viewModel.PhotoRequested += OnPhotoRequested;
            _viewModel.PlayerRequested += OnPlayerRequested;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
            Window.Current.SizeChanged += OnWindowSizeChanged;

            Loaded += MainPage_Loaded;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainPage_Loaded;
            await _viewModel.InitializeAsync();

            // 从编辑页返回时恢复图片查看器。
            if (App.PhotoToRestore != null)
            {
                var item = App.PhotoToRestore;
                App.PhotoToRestore = null;
                if (!_viewModel.CurrentItems.Any(i => i == item))
                {
                    _viewModel.Select(NavTarget.Photos);
                }
                OnPhotoRequested(this, item);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // 视图模型是全局共享的，离开页面时解除订阅，避免返回后重复注册。
            _viewModel.PhotoRequested -= OnPhotoRequested;
            _viewModel.PlayerRequested -= OnPlayerRequested;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
            Window.Current.SizeChanged -= OnWindowSizeChanged;
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

        private void SetSortTitle_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SortIndex = 0;
        }

        private void SetSortDate_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SortIndex = 1;
        }

        private void SetSortDuration_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SortIndex = 2;
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

        private void SetupVideoSurface()
        {
            if (!UwpCompat.HasMediaPlayerElement)
            {
                // 10240：用经典 MediaElement 作为视频后端，仍可应用内播放。
                try
                {
                    _legacyVideoElement = new MediaElement
                    {
                        Stretch = Windows.UI.Xaml.Media.Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };
                    VideoHost.Children.Add(_legacyVideoElement);
                    _playback.VideoBackend = new MediaElementVideoBackend(_legacyVideoElement);
                    return;
                }
                catch (Exception)
                {
                    _legacyVideoElement = null;
                }

                // 兜底：MediaElement 不可用时，交系统播放器。
                var panel = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(new TextBlock
                {
                    Text = "当前系统无法在应用内播放视频",
                    FontSize = 16,
                    Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                var openButton = new Button
                {
                    Content = "用系统播放器打开",
                    Margin = new Thickness(0, 16, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                openButton.Click += (s, e) =>
                {
                    if (_externalVideoItem != null)
                    {
                        _ = Launcher.LaunchFileAsync(_externalVideoItem.File);
                    }
                };
                panel.Children.Add(openButton);
                VideoHost.Children.Add(panel);
                return;
            }

            _videoElement = new MediaPlayerElement
            {
                AreTransportControlsEnabled = false,
                Stretch = Windows.UI.Xaml.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _videoElement.SetMediaPlayer(_playback.Player);
            VideoHost.Children.Add(_videoElement);
        }

        private void OnPlayerRequested(object sender, MediaItem item)
        {
            bool isVideo = item != null && item.Kind == MediaKind.Video;

            if (isVideo && !UwpCompat.HasMediaPlayerElement && _playback.VideoBackend == null)
            {
                // 极端兜底：MediaElement 也不可用时才交系统播放器。
                _externalVideoItem = item;
                _ = Launcher.LaunchFileAsync(item.File);
                return;
            }

            if (_videoElement != null)
            {
                _videoElement.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_legacyVideoElement != null)
            {
                _legacyVideoElement.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
            }
            AudioPanel.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
            PlayerOverlay.Visibility = Visibility.Visible;

            if (isVideo)
            {
                ShowVideoBars();
            }
            else
            {
                _videoIdleTimer?.Stop();
                VideoTopBar.Visibility = Visibility.Visible;
                VideoBottomBar.Visibility = Visibility.Visible;
            }
        }

        private void ShowVideoBars()
        {
            VideoTopBar.Visibility = Visibility.Visible;
            VideoBottomBar.Visibility = Visibility.Visible;

            if (_videoIdleTimer == null)
            {
                _videoIdleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _videoIdleTimer.Tick += (s, e) =>
                {
                    if (PlayerOverlay.Visibility == Visibility.Visible &&
                        _playback.CurrentItem?.Kind == MediaKind.Video)
                    {
                        VideoTopBar.Visibility = Visibility.Collapsed;
                        VideoBottomBar.Visibility = Visibility.Collapsed;
                    }
                    _videoIdleTimer?.Stop();
                };
            }
            _videoIdleTimer.Stop();
            _videoIdleTimer.Start();
        }

        private void PlayerOverlay_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_playback.CurrentItem?.Kind == MediaKind.Video)
            {
                ShowVideoBars();
            }
        }

        private void OpenPlayer_Click(object sender, RoutedEventArgs e)
        {
            // 点击迷你播放器的封面直接展开播放页面。
            OnPlayerRequested(this, _playback.CurrentItem);
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var view = ApplicationView.GetForCurrentView();
                if (view.IsFullScreenMode)
                {
                    view.ExitFullScreenMode();
                }
                else
                {
                    view.TryEnterFullScreenMode();
                }
            }
            catch { }
        }

        private void ClosePlayer_Click(object sender, RoutedEventArgs e)
        {
            _videoIdleTimer?.Stop();
            PlayerOverlay.Visibility = Visibility.Collapsed;
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CurrentLyricText")
            {
                UpdateLyricHighlight();
            }
            else if (e.PropertyName == "PlaybackPosition")
            {
                UpdateWordHighlight();
            }
        }

        private void OnWindowSizeChanged(object sender, Windows.UI.Core.WindowSizeChangedEventArgs e)
        {
            UpdateLyricHighlight();
        }

        private TextBlock ActiveLyricBlock =>
            Window.Current.Bounds.Width < 641 ? NarrowCurrentLyric : CurrentLyricBlock;

        private TextBlock[] ActiveLyricBlocks =>
            Window.Current.Bounds.Width < 641
                ? new[] { NarrowPrevLyric, NarrowCurrentLyric, NarrowNextLyric, NarrowNext2Lyric }
                : new[] { PrevLyricBlock, CurrentLyricBlock, NextLyricBlock, Next2LyricBlock };

        private void UpdateLyricHighlight()
        {
            try
            {
                bool narrow = Window.Current.Bounds.Width < 641;
                double baseSize = narrow ? 18 : 22;
                double activeSize = narrow ? 28 : 40;
                var blocks = ActiveLyricBlocks;
                Brush dim = new SolidColorBrush(Windows.UI.Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF));

                // 所有句子默认保持同样字号；只有播放到的当前句才放大。
                foreach (var block in blocks)
                {
                    if (block == null) continue;
                    block.FontSize = baseSize;
                    block.FontWeight = Windows.UI.Text.FontWeights.Normal;
                    block.Foreground = dim;
                }

                if (_viewModel.LyricIndex >= 0 && blocks[1] != null &&
                    !string.IsNullOrEmpty(_viewModel.CurrentLyricText))
                {
                    var active = blocks[1];
                    active.FontWeight = Windows.UI.Text.FontWeights.SemiBold;
                    active.Foreground = new SolidColorBrush(Windows.UI.Colors.White);
                    AnimateFontSize(active, baseSize, activeSize);
                }

                UpdateWordHighlight();
                if (FullLyricPanel.Visibility == Visibility.Visible)
                {
                    ScrollFullLyricToCurrent();
                }
            }
            catch { }
        }

        private void AnimateFontSize(TextBlock target, double from, double to)
        {
            try
            {
                var animation = new DoubleAnimation
                {
                    From = from,
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(220)
                };
                Storyboard.SetTarget(animation, target);
                Storyboard.SetTargetProperty(animation, "FontSize");
                var storyboard = new Storyboard();
                storyboard.Children.Add(animation);
                storyboard.Begin();
            }
            catch
            {
                target.FontSize = to;
            }
        }

        private int _wordStyledLine = -1;
        private TextBlock _wordStyledTarget;

        private void UpdateWordHighlight()
        {
            try
            {
                int index = _viewModel.LyricIndex;
                var words = _viewModel.LyricWords;
                var target = ActiveLyricBlock;
                if (target == null) return;

                // 布局切换时，先还原上一个目标上的逐字 Inlines。
                if (_wordStyledTarget != null && !ReferenceEquals(_wordStyledTarget, target))
                {
                    _wordStyledTarget.Text = _viewModel.CurrentLyricText;
                    _wordStyledTarget = null;
                    _wordStyledLine = -1;
                }

                if (index < 0 || words == null || words.Count == 0 ||
                    PlayerOverlay.Visibility != Visibility.Visible)
                {
                    if (_wordStyledLine != -1)
                    {
                        target.Text = _viewModel.CurrentLyricText;
                        _wordStyledLine = -1;
                        _wordStyledTarget = null;
                    }
                    return;
                }

                if (_wordStyledLine != index || target.Inlines.Count == 0)
                {
                    target.Inlines.Clear();
                    foreach (var word in words)
                        target.Inlines.Add(new Run { Text = word.Text });
                    _wordStyledLine = index;
                    _wordStyledTarget = target;
                }

                double position = _viewModel.PlaybackPosition;
                Brush sung = new SolidColorBrush(Windows.UI.Colors.White);
                Brush pending = new SolidColorBrush(Windows.UI.Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF));
                for (int i = 0; i < words.Count && i < target.Inlines.Count; i++)
                {
                    if (target.Inlines[i] is Run run)
                        run.Foreground = position + 0.05 >= words[i].StartSeconds ? sung : pending;
                }
            }
            catch { }
        }

        private void ToggleFullLyric_Click(object sender, RoutedEventArgs e)
        {
            FullLyricPanel.Visibility = FullLyricPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
            if (FullLyricPanel.Visibility == Visibility.Visible)
            {
                ScrollFullLyricToCurrent();
            }
        }

        private void CloseFullLyric_Click(object sender, RoutedEventArgs e)
        {
            FullLyricPanel.Visibility = Visibility.Collapsed;
        }

        private void FullLyric_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is LyricLine line)
            {
                _viewModel.SeekToLyricCommand.Execute(line);
            }
        }

        private void ScrollFullLyricToCurrent()
        {
            try
            {
                int index = _viewModel.LyricIndex;
                if (index >= 0 && index < FullLyricList.Items.Count)
                {
                    FullLyricList.ScrollIntoView(FullLyricList.Items[index]);
                }
            }
            catch { }
        }

        private void Subtitle_Click(object sender, RoutedEventArgs e)
        {
            var menu = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };

            var clear = new MenuFlyoutItem { Text = "关闭字幕" };
            clear.Click += (s, args) => _viewModel.ClearSubtitleCommand.Execute(null);
            menu.Items.Add(clear);

            var tracks = _playback.GetEmbeddedSubtitleTracks();
            if (tracks.Count > 0)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                for (int i = 0; i < tracks.Count; i++)
                {
                    var item = new MenuFlyoutItem { Text = "[内嵌] " + tracks[i].Item2 };
                    int index = i;
                    item.Click += (s, args) => _viewModel.SelectEmbeddedSubtitleCommand.Execute(index);
                    menu.Items.Add(item);
                }
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            var load = new MenuFlyoutItem { Text = "加载外部字幕..." };
            load.Click += async (s, args) =>
            {
                try
                {
                    var picker = new Windows.Storage.Pickers.FileOpenPicker
                    {
                        SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary
                    };
                    foreach (string ext in new[] { ".srt", ".vtt", ".ass", ".ssa" })
                        picker.FileTypeFilter.Add(ext);

                    var file = await picker.PickSingleFileAsync();
                    if (file != null)
                    {
                        _playback.ClearEmbeddedSubtitles();
                        await _viewModel.LoadSubtitleAsync(file);
                    }
                }
                catch { }
            };
            menu.Items.Add(load);

            if (sender is Button button)
            {
                menu.ShowAt(button);
            }
        }

        #endregion

        #region 图片灯箱

        private void OnPhotoRequested(object sender, MediaItem item)
        {
            _photoItems = _viewModel.CurrentItems.Where(i => i.Kind == MediaKind.Photo).ToList();
            _photoIndex = _photoItems.IndexOf(item);
            PhotoOverlay.Visibility = Visibility.Visible;
            Filmstrip.ItemsSource = _photoItems;
            if (_photoIndex >= 0 && _photoIndex < _photoItems.Count)
            {
                Filmstrip.ScrollIntoView(_photoItems[_photoIndex]);
            }
            _ = ShowPhotoAsync();
        }

        private async System.Threading.Tasks.Task ShowPhotoAsync()
        {
            if (_photoIndex < 0 || _photoIndex >= _photoItems.Count)
            {
                return;
            }

            int loadId = ++_photoLoadId;
            var item = _photoItems[_photoIndex];
            PhotoTitle.Text = item.Title;
            PhotoCounter.Text = $"{_photoIndex + 1} / {_photoItems.Count}";
            _photoRotation = 0;
            _flipH = 1;
            _flipV = 1;
            PhotoViewer.ImageRotation = 0;
            PhotoViewer.FlipH = 1;
            PhotoViewer.FlipV = 1;
            PhotoFailedPanel.Visibility = Visibility.Collapsed;
            PhotoLoadingRing.IsActive = true;
            PhotoLoadingRing.Visibility = Visibility.Visible;

            // 先用系统缩略图作为占位，高清解码完成后替换，避免空白等待。
            PhotoViewer.IsPlaceholder = item.Thumbnail != null;
            PhotoViewer.Source = item.Thumbnail;

            var fullImage = await ImageLoaderService.LoadAsync(item);
            if (loadId != _photoLoadId)
            {
                return;
            }

            PhotoLoadingRing.IsActive = false;
            PhotoLoadingRing.Visibility = Visibility.Collapsed;

            if (fullImage == null)
            {
                PhotoFailedPanel.Visibility = Visibility.Visible;
                return;
            }

            PhotoViewer.IsPlaceholder = false;
            PhotoViewer.Source = fullImage;
            PhotoViewer.ResetView();
            SyncZoomSlider();
            await LoadPhotoInfoAsync(item, loadId);
        }

        private async System.Threading.Tasks.Task LoadPhotoInfoAsync(MediaItem item, int loadId)
        {
            var info = await ImageInfoService.LoadAsync(item);
            if (loadId != _photoLoadId || info == null)
            {
                return;
            }
            InfoList.ItemsSource = info.BuildRows();
        }

        private void PhotoRetry_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowPhotoAsync();
        }

        private void PhotoViewer_ZoomFactorChanged(object sender, EventArgs e)
        {
            SyncZoomSlider();
        }

        private void SyncZoomSlider()
        {
            _updatingZoomSlider = true;
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum,
                Math.Min(ZoomSlider.Maximum, PhotoViewer.CurrentZoomFactor * 100));
            _updatingZoomSlider = false;
        }

        private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (!_updatingZoomSlider && PhotoOverlay.Visibility == Visibility.Visible)
            {
                PhotoViewer.SetZoomFactor((float)(e.NewValue / 100.0));
            }
        }

        private void Fit_Click(object sender, RoutedEventArgs e)
        {
            PhotoViewer.ResetView();
            SyncZoomSlider();
        }

        private void Rotate_Click(object sender, RoutedEventArgs e)
        {
            _photoRotation = (_photoRotation + 90) % 360;
            PhotoViewer.ImageRotation = _photoRotation;
            PhotoViewer.ResetView();
        }

        private void RotateBack_Click(object sender, RoutedEventArgs e)
        {
            _photoRotation = (_photoRotation - 90 + 360) % 360;
            PhotoViewer.ImageRotation = _photoRotation;
            PhotoViewer.ResetView();
        }

        private void FlipH_Click(object sender, RoutedEventArgs e)
        {
            _flipH = -_flipH;
            PhotoViewer.FlipH = _flipH;
            PhotoViewer.ResetView();
        }

        private void FlipV_Click(object sender, RoutedEventArgs e)
        {
            _flipV = -_flipV;
            PhotoViewer.FlipV = _flipV;
            PhotoViewer.ResetView();
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (_photoIndex < 0 || _photoIndex >= _photoItems.Count)
            {
                return;
            }
            StopSlideShow();
            App.PhotoToRestore = _photoItems[_photoIndex];
            Frame.Navigate(typeof(EditPage), App.PhotoToRestore);
        }

        private void Info_Click(object sender, RoutedEventArgs e)
        {
            _infoOpen = !_infoOpen;
            InfoPanel.Visibility = _infoOpen ? Visibility.Visible : Visibility.Collapsed;
            double panelWidth = Math.Min(300, Math.Max(180, Window.Current.Bounds.Width - 260));
            InfoColumn.Width = new GridLength(_infoOpen ? panelWidth : 0);
            PhotoViewer.ResetView();
        }

        private void PhotoViewer_DoubleTappedOccurred(object sender, EventArgs e)
        {
            // 双击交由控件处理缩放，这里仅同步滑条（缩放事件已在控件内触发）。
            SyncZoomSlider();
        }

        private void Filmstrip_ItemClick(object sender, ItemClickEventArgs e)
        {
            int index = _photoItems.IndexOf(e.ClickedItem as MediaItem);
            if (index < 0 || index == _photoIndex)
            {
                return;
            }
            _photoIndex = index;
            _ = ShowPhotoAsync();
        }

        private void SlideShow_Click(object sender, RoutedEventArgs e)
        {
            if (_slideRunning)
            {
                StopSlideShow();
            }
            else
            {
                StartSlideShow();
            }
        }

        private void StartSlideShow()
        {
            _slideRunning = true;
            _slideElapsed = 0;
            SlideProgressHost.Visibility = Visibility.Visible;
            SlideProgress.Value = 0;

            _slideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _slideTimer.Tick += SlideTimer_Tick;
            _slideTimer.Start();
        }

        private void StopSlideShow()
        {
            _slideRunning = false;
            _slideTimer?.Stop();
            _slideTimer = null;
            SlideProgressHost.Visibility = Visibility.Collapsed;
        }

        private void SlideTimer_Tick(object sender, object e)
        {
            _slideElapsed += 0.05;
            double seconds = PhotoViewerSettings.SlideShowSeconds;
            SlideProgress.Value = Math.Min(1, _slideElapsed / seconds);
            if (_slideElapsed >= seconds && _photoItems.Count > 0)
            {
                _slideElapsed = 0;
                PhotoNext_Click(this, new RoutedEventArgs());
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
            StopSlideShow();
            PhotoOverlay.Visibility = Visibility.Collapsed;
        }

        private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            if (PhotoOverlay.Visibility != Visibility.Visible)
            {
                return;
            }

            switch (args.VirtualKey)
            {
                case VirtualKey.Escape:
                    ClosePhoto_Click(this, new RoutedEventArgs());
                    args.Handled = true;
                    break;
                case VirtualKey.Left:
                    PhotoPrevious_Click(this, new RoutedEventArgs());
                    args.Handled = true;
                    break;
                case VirtualKey.Right:
                    PhotoNext_Click(this, new RoutedEventArgs());
                    args.Handled = true;
                    break;
                case VirtualKey.Number0:
                    Fit_Click(this, new RoutedEventArgs());
                    args.Handled = true;
                    break;
                case VirtualKey.Add:
                    PhotoViewer.SetZoomFactor(PhotoViewer.CurrentZoomFactor * 1.25f);
                    args.Handled = true;
                    break;
                case VirtualKey.Subtract:
                    PhotoViewer.SetZoomFactor(PhotoViewer.CurrentZoomFactor / 1.25f);
                    args.Handled = true;
                    break;
                case VirtualKey.R:
                    Rotate_Click(this, new RoutedEventArgs());
                    args.Handled = true;
                    break;
                case VirtualKey.F:
                    Fit_Click(this, new RoutedEventArgs());
                    args.Handled = true;
                    break;
                case VirtualKey.I:
                    Info_Click(this, new RoutedEventArgs());
                    args.Handled = true;
                    break;
                case VirtualKey.Space:
                    SlideShow_Click(this, new RoutedEventArgs());
                    args.Handled = true;
                    break;
            }
        }

        #endregion
    }
}
