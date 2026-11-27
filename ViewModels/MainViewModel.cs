using HyperMedia_Lite;
using HyperMedia_Lite.Helpers;
using HyperMedia_Lite.Models;
using HyperMedia_Lite.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperMedia_Lite.ViewModels
{
    public enum SortMode
    {
        Title,
        DateCreated,
        Duration,
        Album
    }

    /// <summary>歌曲列表里的专辑分组（标题 + 该专辑曲目）。</summary>
    public sealed class AlbumSongGroup
    {
        public string Album { get; set; }
        public ObservableCollection<MediaItem> Items { get; } = new ObservableCollection<MediaItem>();
    }

    public delegate void MediaItemEventHandler(object sender, MediaItem item);

    /// <summary>
    /// 主视图模型：导航、搜索、专辑/歌手/歌曲视图、收藏、排序，以及播放状态转发。
    /// </summary>
    public sealed class MainViewModel : BindableBase
    {
        private const string FavoritesKey = "FavoritePaths";

        private readonly LibraryService _library = new LibraryService();
        private readonly PlaylistService _playlists = new PlaylistService();
        private readonly PlaybackService _playback = PlaybackService.Current;
        private readonly HashSet<string> _favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MediaItem> _allItems = new List<MediaItem>();

        private NavTarget _currentNav = NavTarget.Music;
        private SortMode _sortMode = SortMode.Title;
        private int _selectedTab = 2; // 默认「专辑」，与参考图一致
        private string _searchText = string.Empty;
        private string _artistFilter = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isLoading;
        private bool _isInitialized;

        private IReadOnlyList<LyricLine> _lyrics = new List<LyricLine>();
        private IReadOnlyList<SubtitleLine> _subtitles = new List<SubtitleLine>();
        private int _lyricIndex = -1;
        private int _subtitleIndex = -1;
        private MediaItem _activeItem;

        public ObservableCollection<NavItem> NavItems { get; } = new ObservableCollection<NavItem>();
        public ObservableCollection<MediaItem> CurrentItems { get; } = new ObservableCollection<MediaItem>();
        public ObservableCollection<Playlist> Playlists { get; } = new ObservableCollection<Playlist>();
        public ObservableCollection<MediaItem> Songs { get; } = new ObservableCollection<MediaItem>();
        public ObservableCollection<object> SongRows { get; } = new ObservableCollection<object>();

        private bool _isMultiSelect;
        public bool IsMultiSelect
        {
            get => _isMultiSelect;
            set
            {
                if (SetProperty(ref _isMultiSelect, value))
                {
                    if (!value) SelectedItems.Clear();
                    OnPropertyChanged(nameof(SelectionSummary));
                    NotifySelectionKinds();
                }
            }
        }

        public ObservableCollection<MediaItem> SelectedItems { get; } = new ObservableCollection<MediaItem>();
        public string SelectionSummary => $"已选 {SelectedItems.Count} 项";

        private bool _isQueueMultiSelect;
        public bool IsQueueMultiSelect
        {
            get => _isQueueMultiSelect;
            set
            {
                if (SetProperty(ref _isQueueMultiSelect, value))
                {
                    if (!value) SelectedQueueItems.Clear();
                    OnPropertyChanged(nameof(QueueSelectionSummary));
                }
            }
        }
        public ObservableCollection<MediaItem> SelectedQueueItems { get; } = new ObservableCollection<MediaItem>();
        public string QueueSelectionSummary => $"已选 {SelectedQueueItems.Count} 项";
        public bool IsLibraryMultiSelectAvailable => _currentNav != NavTarget.Playlists;
        public bool IsQueueNav => _currentNav == NavTarget.Queue;
        public bool IsPhotoNav => _currentNav == NavTarget.Photos;
        public bool IsQueueActionAvailable => _currentNav != NavTarget.Photos;

        // 选中项类型决定多选操作条显示哪组按钮；混合类型只保留通用操作。
        public bool ShowMediaSelectionActions => SelectedItems.Count > 0
            && _currentNav != NavTarget.Queue
            && SelectedItems.All(i => i != null && i.Kind != MediaKind.Photo);
        public bool ShowPhotoSelectionActions => SelectedItems.Count > 0
            && _currentNav != NavTarget.Queue
            && SelectedItems.All(i => i != null && i.Kind == MediaKind.Photo);
        public bool ShowQueueSelectionActions => SelectedItems.Count > 0 && IsQueueNav;

        private bool _isPlaylistMultiSelect;
        public bool IsPlaylistMultiSelect
        {
            get => _isPlaylistMultiSelect;
            set
            {
                if (SetProperty(ref _isPlaylistMultiSelect, value))
                {
                    if (!value) SelectedPlaylists.Clear();
                    OnPropertyChanged(nameof(PlaylistSelectionSummary));
                }
            }
        }
        public ObservableCollection<Playlist> SelectedPlaylists { get; } = new ObservableCollection<Playlist>();
        public string PlaylistSelectionSummary => $"已选 {SelectedPlaylists.Count} 个播放列表";
        public ObservableCollection<AlbumCard> Albums { get; } = new ObservableCollection<AlbumCard>();
        public ObservableCollection<ArtistCard> Artists { get; } = new ObservableCollection<ArtistCard>();
        public IReadOnlyList<string> SortOptions { get; } = new[] { "标题", "最近添加", "时长", "专辑" };

        public RelayCommand AddFilesCommand { get; }
        public RelayCommand AddFolderCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand ShuffleAllCommand { get; }
        public RelayCommand TogglePlayPauseCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand PreviousCommand { get; }
        public RelayCommand CyclePlayModeCommand { get; }
        public RelayCommand SetPlayModeCommand { get; }
        public RelayCommand PickLyricsCommand { get; }
        public RelayCommand ReloadLyricsCommand { get; }
        public RelayCommand ToggleDesktopLyricsCommand { get; }
        public RelayCommand SelectEmbeddedSubtitleCommand { get; }
        public RelayCommand ClearSubtitleCommand { get; }
        public RelayCommand PlayQueueItemCommand { get; }
        public RelayCommand RemoveQueueItemCommand { get; }
        public RelayCommand MoveQueueUpCommand { get; }
        public RelayCommand MoveQueueDownCommand { get; }
        public RelayCommand ClearQueueCommand { get; }
        public RelayCommand ToggleQueuePanelCommand { get; }
        public RelayCommand StartPhotoTileCommand { get; }
        public RelayCommand SetTileModeCommand { get; }
        public RelayCommand SeekToLyricCommand { get; }
        public RelayCommand CycleRateCommand { get; }
        public RelayCommand CreatePlaylistCommand { get; }
        public RelayCommand DeletePlaylistCommand { get; }
        public RelayCommand RenamePlaylistCommand { get; }
        public RelayCommand TogglePlaylistItemCommand { get; }
        public RelayCommand PlayPlaylistCommand { get; }
        public RelayCommand ExpandPlayerCommand { get; }
        public RelayCommand ClearArtistFilterCommand { get; }
        public RelayCommand AddSelectedToQueueCommand { get; }
        public RelayCommand FavoriteSelectedCommand { get; }
        public RelayCommand RemoveSelectedCommand { get; }
        public RelayCommand PlaySelectedQueueItemsCommand { get; }
        public RelayCommand MoveSelectedQueueUpCommand { get; }
        public RelayCommand MoveSelectedQueueDownCommand { get; }
        public RelayCommand RemoveSelectedFromQueueCommand { get; }
        public RelayCommand PlaySelectedPlaylistsCommand { get; }
        public RelayCommand DeleteSelectedPlaylistsCommand { get; }
        public RelayCommand SetSelectedAsTileCommand { get; }
        public RelayCommand PlaySelectedItemsCommand { get; }
        public RelayCommand MoveSelectedItemsUpCommand { get; }
        public RelayCommand MoveSelectedItemsDownCommand { get; }
        public RelayCommand RemoveSelectedItemsFromQueueCommand { get; }

        public event MediaItemEventHandler PhotoRequested;

        public MainViewModel()
        {
            AddFilesCommand = new RelayCommand(_ => _ = AddFilesAsync());
            AddFolderCommand = new RelayCommand(_ => _ = AddFolderAsync());
            RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
            ShuffleAllCommand = new RelayCommand(_ => ShuffleAll());
            TogglePlayPauseCommand = new RelayCommand(_ => _playback.TogglePlayPause());
            NextCommand = new RelayCommand(_ => _playback.Next());
            PreviousCommand = new RelayCommand(_ => _playback.Previous());
            CyclePlayModeCommand = new RelayCommand(_ => _playback.CycleMode());
            PickLyricsCommand = new RelayCommand(_ => _ = PickLyricsAsync());
            ReloadLyricsCommand = new RelayCommand(_ => _ = ReloadLyricsAsync());
            ToggleDesktopLyricsCommand = new RelayCommand(_ => _ = DesktopLyricsBridge.ToggleAsync());
            SelectEmbeddedSubtitleCommand = new RelayCommand(parameter =>
            {
                if (parameter is int index)
                {
                    var tracks = _playback.GetEmbeddedSubtitleTracks();
                    if (index >= 0 && index < tracks.Count)
                    {
                        _playback.SelectEmbeddedSubtitle(tracks[index].Item1);
                    }
                }
            });
            ClearSubtitleCommand = new RelayCommand(_ => ClearSubtitles());
            PlayQueueItemCommand = new RelayCommand(item =>
            {
                if (item is MediaItem media) _ = _playback.PlayQueueIndexAsync(_playback.Queue.IndexOf(media));
                else if (item is int index) _ = _playback.PlayQueueIndexAsync(index);
            });
            RemoveQueueItemCommand = new RelayCommand(item =>
            {
                if (item is MediaItem media) _ = _playback.RemoveFromQueueAsync(_playback.Queue.IndexOf(media));
                else if (item is int index) _ = _playback.RemoveFromQueueAsync(index);
            });
            MoveQueueUpCommand = new RelayCommand(item =>
            {
                int index = item is MediaItem media ? _playback.Queue.IndexOf(media)
                    : item is int i ? i : -1;
                if (index > 0) _playback.MoveQueueItem(index, index - 1);
            });
            MoveQueueDownCommand = new RelayCommand(item =>
            {
                int index = item is MediaItem media ? _playback.Queue.IndexOf(media)
                    : item is int i ? i : -1;
                if (index >= 0 && index < _playback.Queue.Count - 1) _playback.MoveQueueItem(index, index + 1);
            });
            ClearQueueCommand = new RelayCommand(_ => _playback.Stop());
            ToggleQueuePanelCommand = new RelayCommand(_ =>
            {
                IsQueuePanelOpen = !IsQueuePanelOpen;
                if (!IsQueuePanelOpen) IsQueueMultiSelect = false;
            });
            StartPhotoTileCommand = new RelayCommand(_ => _ = StartPhotoTileAsync());
            SetTileModeCommand = new RelayCommand(parameter =>
            {
                if (parameter is TileMode mode)
                {
                    TileService.Mode = mode;
                    _ = ApplyTileModeAsync();
                }
                else if (parameter is string text && Enum.TryParse(text, out TileMode parsed))
                {
                    TileService.Mode = parsed;
                    _ = ApplyTileModeAsync();
                }
            });
            CreatePlaylistCommand = new RelayCommand(async parameter =>
            {
                string name = parameter as string;
                var playlist = await _playlists.CreateAsync(name);
                await RefreshPlaylistsAsync();
                Select(NavTarget.Playlists);
            });
            DeletePlaylistCommand = new RelayCommand(async parameter =>
            {
                if (parameter is Playlist playlist)
                {
                    await _playlists.DeleteAsync(playlist);
                    await RefreshPlaylistsAsync();
                }
            });
            RenamePlaylistCommand = new RelayCommand(async parameter =>
            {
                if (parameter is Playlist playlist)
                {
                    await _playlists.RenameAsync(playlist, playlist.Name);
                    await RefreshPlaylistsAsync();
                }
            });
            TogglePlaylistItemCommand = new RelayCommand(async parameter =>
            {
                if (parameter is Playlist playlist && _playback.CurrentItem != null)
                {
                    await _playlists.ToggleItemAsync(playlist, _playback.CurrentItem.Path);
                    await RefreshPlaylistsAsync();
                }
            });
            PlayPlaylistCommand = new RelayCommand(async parameter =>
            {
                if (parameter is Playlist playlist)
                {
                    var items = playlist.ItemPaths
                        .Select(path => _allItems.FirstOrDefault(i => i.Path == path))
                        .Where(i => i != null && i.Kind != MediaKind.Photo)
                        .ToList();
                    if (items.Count == 0) return;
                    await _playback.PlayQueueAsync(items, items[0]);
                    PlayerRequested?.Invoke(this, items[0]);
                    OnPlaybackItemChanged(this, EventArgs.Empty);
                }
            });
            CycleRateCommand = new RelayCommand(_ =>
            {
                double[] rates = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
                double current = _playback.PlaybackRate;
                double next = rates.FirstOrDefault(r => r > current + 0.01);
                _playback.PlaybackRate = next > 0 ? next : rates[0];
            });
            SeekToLyricCommand = new RelayCommand(parameter =>
            {
                if (parameter is LyricLine line)
                {
                    _playback.Seek(line.TimeSeconds);
                }
                else if (parameter is int index && index >= 0 && index < _lyrics.Count)
                {
                    _playback.Seek(_lyrics[index].TimeSeconds);
                }
            });
            SetPlayModeCommand = new RelayCommand(parameter =>
            {
                if (parameter is string text && Enum.TryParse(text, out PlaybackMode mode))
                {
                    _playback.SetMode(mode);
                }
            });
            ExpandPlayerCommand = new RelayCommand(_ => PlayerRequested?.Invoke(this, _playback.CurrentItem));
            ClearArtistFilterCommand = new RelayCommand(_ => ClearArtistFilter());
            AddSelectedToQueueCommand = new RelayCommand(_ => AddSelectedToQueue());
            FavoriteSelectedCommand = new RelayCommand(_ => FavoriteSelected());
            RemoveSelectedCommand = new RelayCommand(_ => RemoveSelected());
            PlaySelectedQueueItemsCommand = new RelayCommand(_ => PlaySelectedQueueItems());
            MoveSelectedQueueUpCommand = new RelayCommand(_ => MoveSelectedQueueUp());
            MoveSelectedQueueDownCommand = new RelayCommand(_ => MoveSelectedQueueDown());
            RemoveSelectedFromQueueCommand = new RelayCommand(_ => _ = RemoveSelectedFromQueueAsync());
            PlaySelectedPlaylistsCommand = new RelayCommand(_ => PlaySelectedPlaylists());
            DeleteSelectedPlaylistsCommand = new RelayCommand(_ => _ = DeleteSelectedPlaylistsAsync());
            SetSelectedAsTileCommand = new RelayCommand(_ => _ = SetSelectedAsTileAsync());
            PlaySelectedItemsCommand = new RelayCommand(_ => PlaySelectedItems());
            MoveSelectedItemsUpCommand = new RelayCommand(_ => MoveSelectedItemsUp());
            MoveSelectedItemsDownCommand = new RelayCommand(_ => MoveSelectedItemsDown());
            RemoveSelectedItemsFromQueueCommand = new RelayCommand(_ => _ = RemoveSelectedItemsFromQueueAsync());

            BuildNavItems();
            LoadFavorites();

            _playback.StateChanged += OnPlaybackStateChanged;
            _playback.ItemChanged += OnPlaybackItemChanged;
        }

        public event MediaItemEventHandler PlayerRequested;

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }
            _isInitialized = true;
            await _playlists.LoadAsync();
            await RefreshPlaylistsAsync();
            await RefreshAsync();
            Select(NavTarget.Music);
        }

        private void BuildNavItems()
        {
            NavItems.Add(new NavItem(NavTarget.Music, "\uE8D6", "音乐"));
            NavItems.Add(new NavItem(NavTarget.Recent, "\uE8B2", "最近添加"));
            NavItems.Add(new NavItem(NavTarget.Stats, "\uE8A5", "统计"));
            NavItems.Add(new NavItem(NavTarget.Queue, "\uE8A6", "播放队列"));
            NavItems.Add(new NavItem(NavTarget.Video, "\uE714", "视频"));
            NavItems.Add(new NavItem(NavTarget.Photos, "\uE722", "图片"));
            NavItems.Add(new NavItem(NavTarget.Favorites, "\uE734", "收藏"));
            NavItems.Add(new NavItem(NavTarget.Playlists, "\uE8FD", "播放列表"));
        }

        private void LoadFavorites()
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(FavoritesKey, out object raw)
                && raw is string saved)
            {
                foreach (string path in saved.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    _favorites.Add(path);
                }
            }
        }

        private void SaveFavorites()
        {
            ApplicationData.Current.LocalSettings.Values[FavoritesKey] = string.Join("\n", _favorites);
        }

        public async Task RefreshAsync()
        {
            IsLoading = true;
            StatusMessage = "正在读取媒体…";
            try
            {
                var saved = await _library.RestoreAsync();
                var scanned = await _library.ScanLibrariesAsync();

                var merged = new List<MediaItem>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in saved.Concat(scanned))
                {
                    if (seen.Add(item.Path))
                    {
                        merged.Add(item);
                    }
                }

                foreach (var item in merged)
                {
                    await item.LoadPropertiesAsync();
                    item.IsFavorite = _favorites.Contains(item.Path);
                }

                _allItems.Clear();
                _allItems.AddRange(merged);

                ApplyViews();
            }
            catch (Exception ex)
            {
                StatusMessage = "读取媒体失败：" + ex.Message;
            }
            finally
            {
                IsLoading = false;
                UpdateStatus();
                UpdateNavCounts();
            }
        }

        private async Task AddFilesAsync()
        {
            var added = await _library.PickFilesAsync();
            await MergeImportedAsync(added, "个媒体文件");
        }

        private async Task AddFolderAsync()
        {
            var added = await _library.PickFolderAsync();
            await MergeImportedAsync(added, "个媒体文件（含子文件夹）");
        }

        private async Task MergeImportedAsync(List<MediaItem> added, string unit)
        {
            if (added.Count == 0)
            {
                return;
            }

            foreach (var item in added)
            {
                await item.LoadPropertiesAsync();
                item.IsFavorite = _favorites.Contains(item.Path);
                if (_allItems.All(x => !string.Equals(x.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    _allItems.Add(item);
                }
            }

            ApplyViews();
            UpdateNavCounts();
            StatusMessage = $"已添加 {added.Count} {unit}";
        }

        private async Task LoadThumbnailsAsync()
        {
            foreach (var item in CurrentItems.Concat(Songs).Concat(Albums.Select(a => a.FirstItem).Where(i => i != null)).Distinct().ToList())
            {
                if (!item.HasThumbnail)
                {
                    await item.LoadThumbnailAsync();
                }

                foreach (var album in Albums.Where(a => a.FirstItem == item))
                {
                    if (album.Thumbnail == null && item.Thumbnail != null)
                    {
                        album.Thumbnail = item.Thumbnail;
                    }
                }
                await Task.Yield();
            }
        }

        public void Select(NavTarget target)
        {
            // 切换界面时收起所有多选状态，避免操作条串到别的视图。
            IsMultiSelect = false;
            IsQueueMultiSelect = false;
            IsPlaylistMultiSelect = false;

            _currentNav = target;
            foreach (var item in NavItems)
            {
                item.IsSelected = item.Target == target;
            }
            PageTitle = target == NavTarget.Music ? "我的音乐"
                : NavItems.FirstOrDefault(n => n.Target == target)?.Label ?? "媒体";
            OnPropertyChanged(nameof(ShowMusicTabs));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(IsLibraryMultiSelectAvailable));
            OnPropertyChanged(nameof(IsQueueNav));
            OnPropertyChanged(nameof(IsPhotoNav));
            OnPropertyChanged(nameof(IsQueueActionAvailable));
            OnPropertyChanged(nameof(IsAlbumsVisible));
            OnPropertyChanged(nameof(IsSongsVisible));
            OnPropertyChanged(nameof(IsArtistsVisible));
            OnPropertyChanged(nameof(IsGenericVisible));
            OnPropertyChanged(nameof(IsStatsVisible));
            OnPropertyChanged(nameof(IsPlaylistsVisible));
            OnPropertyChanged(nameof(StatsText));
            ApplyViews();
        }

        public void SetTab(int index)
        {
            SelectedTab = index;
        }

        public async Task OpenItemAsync(MediaItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.Kind == MediaKind.Photo)
            {
                PhotoRequested?.Invoke(this, item);
                return;
            }

            // 再次点击正在播放的同一项时不要重建播放源：
            // 重复赋值 MediaPlayer.Source 会抛异常并导致崩溃，只需重新打开播放界面。
            if (_playback.CurrentItem == item)
            {
                PlayerRequested?.Invoke(this, item);
                return;
            }

            var queue = _allItems.Where(i => i.Kind == item.Kind).ToList();
            await _playback.PlayQueueAsync(queue, item);
            PlayerRequested?.Invoke(this, item);
        }

        public async Task OpenAlbumAsync(AlbumCard album)
        {
            if (album?.Items == null || album.Items.Count == 0)
            {
                return;
            }
            await _playback.PlayQueueAsync(album.Items, album.FirstItem);
            PlayerRequested?.Invoke(this, album.FirstItem);
        }

        public void OpenArtist(ArtistCard artist)
        {
            if (artist == null)
            {
                return;
            }
            ArtistFilter = artist.Name;
            SelectedTab = 0;
        }

        public void ToggleFavorite(MediaItem item)
        {
            if (item == null)
            {
                return;
            }

            item.IsFavorite = !item.IsFavorite;
            if (item.IsFavorite)
            {
                _favorites.Add(item.Path);
            }
            else
            {
                _favorites.Remove(item.Path);
            }
            SaveFavorites();
            UpdateNavCounts();
            ApplyViews();
        }

        /// <summary>把一项追加到当前播放队列（不立即播放）。</summary>
        public void AddToQueue(MediaItem item)
        {
            if (item == null || item.File == null) return;
            if (item.Kind == MediaKind.Photo) return;
            if (_playback.Queue.Any(i => i.Path == item.Path))
            {
                StatusMessage = "该条目已在播放队列中";
                return;
            }
            _playback.Queue.Add(item);
            UpdateNavCounts();
            StatusMessage = $"已加入播放队列：{item.Title}";
        }

        /// <summary>从媒体库移除一项（只移除列表，不删除磁盘文件）。</summary>
        public void RemoveFromLibrary(MediaItem item)
        {
            if (item == null) return;
            _allItems.Remove(item);
            CurrentItems.Remove(item);
            Songs.Remove(item);
            _ = _playback.RemoveFromQueueAsync(_playback.Queue.IndexOf(item));
            if (item.IsFavorite)
            {
                _favorites.Remove(item.Path);
                SaveFavorites();
            }
            UpdateNavCounts();
            ApplyViews();
            StatusMessage = $"已从媒体库移除：{item.Title}（文件未删除）";
        }

        public void SyncSelection(IEnumerable<MediaItem> added, IEnumerable<MediaItem> removed)
        {
            if (removed != null)
            {
                foreach (var item in removed.ToList()) SelectedItems.Remove(item);
            }
            if (added != null)
            {
                foreach (var item in added)
                {
                    if (item != null && !SelectedItems.Contains(item)) SelectedItems.Add(item);
                }
            }
            OnPropertyChanged(nameof(SelectionSummary));
            NotifySelectionKinds();
            UpdateStatus();
        }

        private void NotifySelectionKinds()
        {
            OnPropertyChanged(nameof(ShowMediaSelectionActions));
            OnPropertyChanged(nameof(ShowPhotoSelectionActions));
            OnPropertyChanged(nameof(ShowQueueSelectionActions));
            OnPropertyChanged(nameof(IsQueueActionAvailable));
        }

        public void SyncQueueSelection(IEnumerable<MediaItem> added, IEnumerable<MediaItem> removed)
        {
            if (removed != null)
                foreach (var item in removed.ToList()) SelectedQueueItems.Remove(item);
            if (added != null)
                foreach (var item in added)
                    if (item != null && !SelectedQueueItems.Contains(item)) SelectedQueueItems.Add(item);
            OnPropertyChanged(nameof(QueueSelectionSummary));
        }

        public void SyncPlaylistSelection(IEnumerable<Playlist> added, IEnumerable<Playlist> removed)
        {
            if (removed != null)
                foreach (var item in removed.ToList()) SelectedPlaylists.Remove(item);
            if (added != null)
                foreach (var item in added)
                    if (item != null && !SelectedPlaylists.Contains(item)) SelectedPlaylists.Add(item);
            OnPropertyChanged(nameof(PlaylistSelectionSummary));
        }

        public async System.Threading.Tasks.Task RemoveSelectedFromQueueAsync()
        {
            foreach (var item in SelectedQueueItems.ToList())
            {
                await _playback.RemoveFromQueueAsync(item);
            }
            SelectedQueueItems.Clear();
            OnPropertyChanged(nameof(QueueSelectionSummary));
            StatusMessage = "已从播放队列移除选中项";
        }

        public void MoveSelectedQueueUp()
        {
            foreach (var item in SelectedQueueItems.ToList())
            {
                int index = _playback.Queue.IndexOf(item);
                if (index > 0) _playback.MoveQueueItem(index, index - 1);
            }
        }

        public void MoveSelectedQueueDown()
        {
            foreach (var item in SelectedQueueItems.Reverse())
            {
                int index = _playback.Queue.IndexOf(item);
                if (index >= 0 && index < _playback.Queue.Count - 1)
                    _playback.MoveQueueItem(index, index + 1);
            }
        }

        public void PlaySelectedQueueItems()
        {
            foreach (var item in SelectedQueueItems.ToList())
            {
                PlayQueueItemCommand.Execute(item);
            }
        }

        public async System.Threading.Tasks.Task DeleteSelectedPlaylistsAsync()
        {
            foreach (var playlist in SelectedPlaylists.ToList())
            {
                await _playlists.DeleteAsync(playlist);
            }
            SelectedPlaylists.Clear();
            OnPropertyChanged(nameof(PlaylistSelectionSummary));
            await RefreshPlaylistsAsync();
            StatusMessage = "已删除选中的播放列表";
        }

        public void PlaySelectedPlaylists()
        {
            foreach (var playlist in SelectedPlaylists.ToList())
            {
                PlayPlaylistCommand.Execute(playlist);
            }
        }

        public void PlaySelectedItems()
        {
            foreach (var item in SelectedItems.ToList())
            {
                PlayQueueItemCommand.Execute(item);
            }
        }

        public void MoveSelectedItemsUp()
        {
            foreach (var item in SelectedItems.ToList())
            {
                int index = _playback.Queue.IndexOf(item);
                if (index > 0) _playback.MoveQueueItem(index, index - 1);
            }
        }

        public void MoveSelectedItemsDown()
        {
            foreach (var item in SelectedItems.Reverse())
            {
                int index = _playback.Queue.IndexOf(item);
                if (index >= 0 && index < _playback.Queue.Count - 1)
                    _playback.MoveQueueItem(index, index + 1);
            }
        }

        public async System.Threading.Tasks.Task RemoveSelectedItemsFromQueueAsync()
        {
            foreach (var item in SelectedItems.ToList())
            {
                await _playback.RemoveFromQueueAsync(item);
            }
            SelectedItems.Clear();
            OnPropertyChanged(nameof(SelectionSummary));
            NotifySelectionKinds();
        }

        private async System.Threading.Tasks.Task SetSelectedAsTileAsync()
        {
            var photos = SelectedItems.Where(i => i != null && i.Kind == MediaKind.Photo).ToList();
            if (photos.Count == 0) return;
            await TileService.StartPhotoSlideshowAsync(photos);
            StatusMessage = $"已将 {photos.Count} 张图片设为磁贴轮播";
        }

        public void AddSelectedToQueue()
        {
            foreach (var item in SelectedItems.ToList()) AddToQueue(item);
        }

        public async System.Threading.Tasks.Task AddSelectedToPlaylist(Playlist playlist)
        {
            if (playlist == null) return;
            int count = 0;
            foreach (var item in SelectedItems.ToList())
            {
                await _playlists.ToggleItemAsync(playlist, item.Path);
                count++;
            }
            await RefreshPlaylistsAsync();
            StatusMessage = $"已添加 {count} 项到播放列表「{playlist.Name}」";
        }

        public void FavoriteSelected()
        {
            foreach (var item in SelectedItems.ToList())
            {
                if (item != null && !item.IsFavorite) ToggleFavorite(item);
            }
        }

        public void RemoveSelected()
        {
            foreach (var item in SelectedItems.ToList()) RemoveFromLibrary(item);
            SelectedItems.Clear();
            OnPropertyChanged(nameof(SelectionSummary));
            NotifySelectionKinds();
        }

        public void ClearSavedFiles()
        {
            _library.ClearSavedFiles();
            StatusMessage = "已清除已保存的文件访问权限";
        }

        /// <summary>
        /// 主题 / 强调色变化后刷新使用转换器的绑定。
        /// </summary>
        public void RefreshTheme()
        {
            foreach (var item in NavItems)
            {
                item.RefreshSelection();
            }
            OnPropertyChanged(nameof(TrackFallbackBrush));
        }

        private void ClearArtistFilter()
        {
            ArtistFilter = string.Empty;
        }

        private void ShuffleAll()
        {
            var items = VisiblePlayableItems();
            if (items.Count == 0)
            {
                StatusMessage = "当前视图没有可播放的媒体";
                return;
            }

            _playback.SetMode(PlaybackMode.Shuffle);
            var start = items[new Random().Next(items.Count)];
            _ = _playback.PlayQueueAsync(items, start);
            PlayerRequested?.Invoke(this, start);
        }

        private List<MediaItem> VisiblePlayableItems()
        {
            if (_currentNav == NavTarget.Music)
            {
                IEnumerable<MediaItem> source = Songs;
                if (_selectedTab == 2)
                {
                    source = Albums.SelectMany(a => a.Items);
                }
                return source.Where(i => i.Kind != MediaKind.Photo).ToList();
            }
            return CurrentItems.Where(i => i.Kind != MediaKind.Photo).ToList();
        }

        private void ApplyViews()
        {
            BuildMusicViews();
            BuildGenericView();
            UpdateStatus();
            _ = LoadThumbnailsAsync();
        }

        private void BuildMusicViews()
        {
            IEnumerable<MediaItem> audio = _allItems.Where(i => i.Kind == MediaKind.Audio);
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string q = _searchText.Trim();
                audio = audio.Where(i => Matches(i, q));
            }
            if (!string.IsNullOrWhiteSpace(_artistFilter))
            {
                audio = audio.Where(i => string.Equals(i.Subtitle, _artistFilter, StringComparison.OrdinalIgnoreCase));
            }

            var list = SortItems(audio).ToList();

            Songs.Clear();
            foreach (var item in list)
            {
                Songs.Add(item);
            }

            // 歌曲列表：仅「专辑」排序时插入专辑标题行，其它排序保持平铺。
            SongRows.Clear();
            if (_sortMode == SortMode.Album)
            {
                foreach (var group in list.GroupBy(KeyForAlbum, StringComparer.OrdinalIgnoreCase))
                {
                    SongRows.Add(new AlbumSongGroup { Album = group.Key.Split('\u0001')[0] });
                    foreach (var item in group)
                    {
                        SongRows.Add(item);
                    }
                }
            }
            else
            {
                foreach (var item in list)
                {
                    SongRows.Add(item);
                }
            }

            // 专辑：按「专辑名 + 艺人」分组
            Albums.Clear();
            foreach (var group in list.GroupBy(KeyForAlbum, StringComparer.OrdinalIgnoreCase))
            {
                var items = group.ToList();
                string title = group.Key.Split('\u0001')[0];
                string artist = items.First().Subtitle;
                Albums.Add(new AlbumCard
                {
                    Title = title,
                    Subtitle = artist,
                    SongCount = items.Count,
                    Items = items,
                    FirstItem = items[0],
                    FallbackBrush = AlbumCard.ColorFor(title),
                    Thumbnail = items[0].Thumbnail
                });
            }

            // 歌手：按艺人分组
            Artists.Clear();
            foreach (var group in list
                .Where(i => !string.IsNullOrWhiteSpace(i.Subtitle))
                .GroupBy(i => i.Subtitle, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                Artists.Add(new ArtistCard
                {
                    Name = group.Key,
                    SongCount = group.Count(),
                    FallbackBrush = ArtistCard.ColorFor(group.Key)
                });
            }
        }

        private void BuildGenericView()
        {
            if (_currentNav == NavTarget.Music || _currentNav == NavTarget.Stats)
            {
                CurrentItems.Clear();
                return;
            }

            IEnumerable<MediaItem> source;
            switch (_currentNav)
            {
                case NavTarget.Video:
                    source = _allItems.Where(i => i.Kind == MediaKind.Video);
                    break;
                case NavTarget.Photos:
                    source = _allItems.Where(i => i.Kind == MediaKind.Photo);
                    break;
                case NavTarget.Favorites:
                    source = _allItems.Where(i => i.IsFavorite);
                    break;
                case NavTarget.Queue:
                    source = _playback.Queue;
                    break;
                default:
                    source = _allItems;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string q = _searchText.Trim();
                source = source.Where(i => Matches(i, q));
            }

            source = _currentNav == NavTarget.Recent
                ? source.OrderByDescending(i => i.File == null ? DateTimeOffset.MinValue : i.File.DateCreated)
                : SortItems(source);

            CurrentItems.Clear();
            foreach (var item in source)
            {
                CurrentItems.Add(item);
            }
        }

        private IEnumerable<MediaItem> SortItems(IEnumerable<MediaItem> source)
        {
            switch (_sortMode)
            {
                case SortMode.DateCreated:
                    return source.OrderByDescending(i => i.File == null ? DateTimeOffset.MinValue : i.File.DateCreated);
                case SortMode.Duration:
                    return source.OrderByDescending(i => i.Duration);
                case SortMode.Album:
                    // 同一专辑排在一起，无专辑名的放最后；专辑内按艺人、标题排序。
                    return source
                        .OrderBy(i => string.IsNullOrWhiteSpace(i.Album) ? "\uffff" : i.Album,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(i => i.Subtitle ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase);
                default:
                    return source.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase);
            }
        }

        private static string KeyForAlbum(MediaItem item)
        {
            string album = string.IsNullOrWhiteSpace(item.Album) ? item.Title : item.Album;
            string artist = item.Subtitle ?? string.Empty;
            return album + "\u0001" + artist;
        }

        private static bool Matches(MediaItem item, string query)
        {
            return (item.Title != null && item.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                || (item.Subtitle != null && item.Subtitle.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                || (item.Album != null && item.Album.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private void UpdateStatus()
        {
            if (IsLoading)
            {
                return;
            }

            if (_currentNav == NavTarget.Music)
            {
                StatusMessage = IsEmpty ? EmptyHint
                    : _selectedTab == 0 ? $"{Songs.Count} 首歌曲"
                    : _selectedTab == 1 ? $"{Artists.Count} 位歌手"
                    : $"{Albums.Count} 张专辑";
            }
            else if (_currentNav == NavTarget.Stats)
            {
                StatusMessage = string.Empty;
            }
            else
            {
                StatusMessage = IsEmpty ? EmptyHint : $"{CurrentItems.Count} 个项目";
            }
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(PlayAllText));
            OnPropertyChanged(nameof(StatsText));
        }

        private string EmptyHint
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    return $"没有匹配“{_searchText.Trim()}”的结果。";
                }
                switch (_currentNav)
                {
                    case NavTarget.Queue:
                        return "播放队列为空。先从音乐或视频中选择一项开始播放。";
                    case NavTarget.Favorites:
                        return "还没有收藏。点击媒体卡片右上角的爱心即可收藏。";
                    case NavTarget.Photos:
                        return "没有找到图片。点击「添加媒体」选择本地图片。";
                    case NavTarget.Video:
                        return "没有找到视频。点击「添加媒体」选择本地视频，或确认已授予视频库访问权限。";
                    default:
                        return "没有找到音乐。点击「添加媒体」选择本地音频，或确认已授予音乐库访问权限。";
                }
            }
        }

        private void UpdateNavCounts()
        {
            SetCount(NavTarget.Music, _allItems.Count(i => i.Kind == MediaKind.Audio));
            SetCount(NavTarget.Video, _allItems.Count(i => i.Kind == MediaKind.Video));
            SetCount(NavTarget.Photos, _allItems.Count(i => i.Kind == MediaKind.Photo));
            SetCount(NavTarget.Favorites, _allItems.Count(i => i.IsFavorite));
            SetCount(NavTarget.Queue, _playback.Queue.Count);
            SetCount(NavTarget.Recent, _allItems.Count);
        }

        private void SetCount(NavTarget target, int count)
        {
            var item = NavItems.FirstOrDefault(n => n.Target == target);
            if (item != null)
            {
                item.CountText = count == 0 ? string.Empty : count.ToString();
            }
        }

        private void OnPlaybackStateChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(PlayPauseGlyph));
            OnPropertyChanged(nameof(IsShuffle));
            OnPropertyChanged(nameof(IsRepeat));
            OnPropertyChanged(nameof(PlayModeGlyph));
            OnPropertyChanged(nameof(PlayModeText));
            OnPropertyChanged(nameof(CurrentTimeText));
            OnPropertyChanged(nameof(TotalTimeText));
            OnPropertyChanged(nameof(PlaybackPosition));
            OnPropertyChanged(nameof(PlaybackDuration));
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(RateText));
            SystemMediaControlsService.Current.UpdateStatus(_playback.IsPlaying);
            if (_tileNowPlaying) TileService.UpdatePlayStatus(_playback.IsPlaying);
            UpdateLyricAndSubtitle(_playback.Position.TotalSeconds);
        }

        private void OnPlaybackItemChanged(object sender, EventArgs e)
        {
            _activeItem = _playback.CurrentItem;
            _ = UpdateTileForTrackAsync();
            _ = ApplyCoverAccentAsync(_activeItem);
            _ = SystemMediaControlsService.Current.UpdateAsync(_activeItem, _playback.IsPlaying);
            _ = LoadLyricsAsync(_activeItem);
            _ = LoadSubtitlesAsync(_activeItem);
            OnPropertyChanged(nameof(IsPlayerVisible));
            OnPropertyChanged(nameof(CurrentTrack));
            OnPropertyChanged(nameof(TrackTitle));
            OnPropertyChanged(nameof(TrackSubtitle));
            OnPropertyChanged(nameof(TrackThumbnail));
            OnPropertyChanged(nameof(HasTrackThumbnail));
            OnPropertyChanged(nameof(TrackFallbackBrush));
            OnPropertyChanged(nameof(TrackInitial));
            OnPropertyChanged(nameof(CurrentTimeText));
            OnPropertyChanged(nameof(TotalTimeText));
            OnPropertyChanged(nameof(PlaybackDuration));
            OnPropertyChanged(nameof(HasEmbeddedSubtitles));
            OnPropertyChanged(nameof(IsCurrentVideo));
            OnPropertyChanged(nameof(IsCurrentAudio));
            OnPropertyChanged(nameof(IsPlayOrderVisible));
            UpdateNavCounts();
            if (_currentNav == NavTarget.Queue)
            {
                ApplyViews();
            }
        }

        private async System.Threading.Tasks.Task LoadLyricsAsync(MediaItem item)
        {
            _lyrics = new List<LyricLine>();
            _lyricIndex = -1;
            OnPropertyChanged(nameof(Lyrics));
            OnPropertyChanged(nameof(HasLyrics));
            OnPropertyChanged(nameof(CurrentLyricText));
            OnPropertyChanged(nameof(NextLyricText));
            DesktopLyricsBridge.Clear();

            if (item == null || item.Kind != MediaKind.Audio)
            {
                return;
            }

            var lines = await LyricsService.LoadAsync(item);
            if (_activeItem != item || lines == null)
            {
                return;
            }
            _lyrics = lines;
            DebugLog.Write("LYRIC",
                $"loaded {lines.Count} lines, firstStart=" +
                (lines.Count > 0 ? lines[0].TimeSeconds.ToString("0.00") : "n/a") + "s");
            OnPropertyChanged(nameof(Lyrics));
            OnPropertyChanged(nameof(HasLyrics));
            UpdateLyricAndSubtitle(_playback.Position.TotalSeconds);
        }

        public async System.Threading.Tasks.Task LoadSubtitleAsync(StorageFile file)
        {
            var lines = await SubtitleService.LoadAsync(file);
            _subtitles = lines ?? new List<SubtitleLine>();
            _subtitleIndex = -1;
            OnPropertyChanged(nameof(Subtitles));
            OnPropertyChanged(nameof(HasSubtitle));
            UpdateLyricAndSubtitle(_playback.Position.TotalSeconds);
        }

        private async System.Threading.Tasks.Task RefreshPlaylistsAsync()
        {
            Playlists.Clear();
            foreach (var playlist in _playlists.Playlists)
            {
                Playlists.Add(playlist);
            }
            OnPropertyChanged(nameof(IsPlaylistsVisible));
            OnPropertyChanged(nameof(PlayAllText));
            SetCount(NavTarget.Playlists, Playlists.Count);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private async System.Threading.Tasks.Task ApplyCoverAccentAsync(MediaItem item)
        {
            if (item == null)
            {
                ThemeService.SetDynamicAccent(null);
                return;
            }
            var color = await AccentColorService.ExtractAsync(item);
            if (_activeItem == item)
            {
                ThemeService.SetDynamicAccent(color);
            }
        }

        private bool _tileNowPlaying = true;

        private async System.Threading.Tasks.Task UpdateTileForTrackAsync()
        {
            var mode = TileService.Mode;
            if (mode == TileMode.Photos)
            {
                _tileNowPlaying = false;
                return;
            }

            // 播放优先：有当前曲目就显示正在播放，没有则回落到图片磁贴。
            if (mode == TileMode.PlaybackPriority && _activeItem == null)
            {
                await ApplyIdleTileAsync();
                _tileNowPlaying = false;
                return;
            }

            bool nowPlaying = mode == TileMode.NowPlaying
                || (mode == TileMode.PlaybackPriority && _activeItem != null)
                || (mode == TileMode.Random && new Random().Next(2) == 0);
            if (nowPlaying)
            {
                await TileService.UpdateNowPlayingAsync(_activeItem, _playback.IsPlaying);
                _tileNowPlaying = true;
            }
            else
            {
                await ApplyIdleTileAsync();
                _tileNowPlaying = false;
            }
        }

        /// <summary>无播放任务 / 应用退出时显示图片磁贴。</summary>
        public async System.Threading.Tasks.Task ApplyIdleTileAsync(bool force = false)
        {
            if (TileService.Mode != TileMode.PlaybackPriority) return;
            // 生成图片期间用户可能已经开始播放：此时必须放弃回落，否则会覆盖正在播放磁贴。
            // force 用于应用退出：即使队列还有文件，也要切回图片磁贴。
            if (!force && _playback.CurrentItem != null) return;
            try
            {
                if (TileService.HasCachedPhotoTile)
                {
                    TileService.ApplyCachedPhotoTile();
                }
                else
                {
                    await StartPhotoTileAsync();
                }
            }
            catch { }
            // 退出时（force）即使队列还有文件也不要补推正在播放，否则刚切好的图片磁贴会被覆盖。
            if (!force && _playback.CurrentItem != null)
            {
                await TileService.UpdateNowPlayingAsync(_playback.CurrentItem, _playback.IsPlaying);
            }
        }

        private async System.Threading.Tasks.Task ApplyTileModeAsync()
        {
            var mode = TileService.Mode;
            if (mode == TileMode.Photos)
            {
                await StartPhotoTileAsync();
                _tileNowPlaying = false;
                return;
            }

            if (mode == TileMode.PlaybackPriority && _activeItem == null)
            {
                await ApplyIdleTileAsync();
                _tileNowPlaying = false;
                return;
            }

            if (mode == TileMode.Random && new Random().Next(2) == 0)
            {
                await StartPhotoTileAsync();
                _tileNowPlaying = false;
                return;
            }

            await TileService.UpdateNowPlayingAsync(_activeItem, _playback.IsPlaying);
            _tileNowPlaying = true;
        }

        private async System.Threading.Tasks.Task StartPhotoTileAsync()
        {
            var photos = _allItems.Where(i => i.Kind == MediaKind.Photo).ToList();
            var favorites = photos.Where(i => i.IsFavorite).ToList();
            var chosen = favorites.Count > 0 ? favorites : photos;
            if (chosen.Count == 0)
            {
                StatusMessage = "媒体库里没有可用图片";
                return;
            }

            if (TileService.RotationInterval > 0)
            {
                await TileService.StartTimedRotationAsync(chosen);
                StatusMessage = $"已按 {TileService.RotationInterval} 秒间隔开始轮播（应用运行时）";
            }
            else
            {
                await TileService.StartPhotoSlideshowAsync(chosen);
                StatusMessage = $"已将 {Math.Min(chosen.Count, 12)} 张图片设为磁贴轮播（系统默认间隔）";
            }
        }

        private void ClearSubtitles()
        {
            _playback.ClearEmbeddedSubtitles();
            _subtitles = new List<SubtitleLine>();
            _subtitleIndex = -1;
            OnPropertyChanged(nameof(Subtitles));
            OnPropertyChanged(nameof(HasSubtitle));
            OnPropertyChanged(nameof(CurrentSubtitleText));
        }

        /// <summary>
        /// 打开歌词选择面板：以当前曲目信息搜索候选，用户选定后替换歌词。
        /// 与「重新搜索」不同，这里不清除已记住的选择。
        /// </summary>
        private async System.Threading.Tasks.Task PickLyricsAsync()
        {
            var item = _activeItem;
            if (item == null || item.File == null)
            {
                return;
            }

            // 优先用元数据；元数据缺失时再从「艺人 - 歌名」形式的文件名解析。
            string display = item.File.DisplayName;
            var parsed = LyricMatcher.ParseArtistTitleFromFileName(display);
            bool fileNameHasArtist = !string.IsNullOrWhiteSpace(parsed.Item1);

            string title = fileNameHasArtist && item.Title == display
                ? parsed.Item2
                : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : parsed.Item2);
            string artist = !string.IsNullOrWhiteSpace(item.Artist) ? item.Artist : parsed.Item1;
            string album = item.Album;

            // 立即打开面板，由面板内部异步搜索，避免按钮点击后长时间等待。
            var candidates = new List<LyricCandidate>();

            var chosen = LyricsService.CandidatePicker != null
                ? await LyricsService.CandidatePicker(candidates, artist, title, album)
                : null;

            if (chosen != null && chosen.IsResetRequest)
            {
                // 清除显式选择，改回内嵌 / 本地同名歌词（不再走在线、不重复弹面板）。
                await LyricsService.ForgetChoiceAsync(item);
                ApplyLyrics(await LyricsService.LoadDefaultAsync(item));
                return;
            }

            if (chosen == null || string.IsNullOrEmpty(chosen.LyricText))
            {
                return;
            }

            var lines = LyricsParser.Parse(chosen.LyricText, chosen.Format);
            await LyricsService.PersistChoiceAsync(item, chosen.Source, chosen.SongId, lines);
            ApplyLyrics(lines);
        }

        private void ApplyLyrics(List<LyricLine> lines)
        {
            _lyrics = lines ?? new List<LyricLine>();
            _lyricIndex = -1;
            OnPropertyChanged(nameof(Lyrics));
            OnPropertyChanged(nameof(HasLyrics));
            OnPropertyChanged(nameof(CurrentLyricText));
            OnPropertyChanged(nameof(NextLyricText));
            DesktopLyricsBridge.Clear();
            UpdateLyricAndSubtitle(_playback.Position.TotalSeconds);
        }

        private async System.Threading.Tasks.Task ReloadLyricsAsync()
        {
            var item = _activeItem;
            if (item == null)
            {
                return;
            }
            // 清除已记住的选择与缓存，重新搜索并弹出候选选择。
            await LyricsService.ForgetChoiceAsync(item);
            await LoadLyricsAsync(item);
        }

        private async System.Threading.Tasks.Task LoadSubtitlesAsync(MediaItem item)
        {
            _subtitles = new List<SubtitleLine>();
            _subtitleIndex = -1;
            OnPropertyChanged(nameof(Subtitles));
            OnPropertyChanged(nameof(CurrentSubtitleText));
            OnPropertyChanged(nameof(HasSubtitle));

            if (item == null || item.Kind != MediaKind.Video || item.File == null)
            {
                return;
            }

            var file = await SubtitleService.FindSiblingAsync(item.File, item);
            if (file == null)
            {
                return;
            }
            var lines = await SubtitleService.LoadAsync(file);
            if (_activeItem != item || lines == null || lines.Count == 0)
            {
                return;
            }
            _subtitles = lines;
            OnPropertyChanged(nameof(Subtitles));
            OnPropertyChanged(nameof(HasSubtitle));
            UpdateLyricAndSubtitle(_playback.Position.TotalSeconds);
        }

        private void UpdateLyricAndSubtitle(double position)
        {
            int lyricIndex = LyricsParser.GetCurrentIndex(_lyrics, position);
            if (lyricIndex != _lyricIndex)
            {
                DebugLog.Write("LYRIC",
                    $"index {_lyricIndex} -> {lyricIndex}, pos={position:0.00}s, " +
                    $"lines={_lyrics.Count}, " +
                    (lyricIndex >= 0 && lyricIndex < _lyrics.Count
                        ? $"lineStart={_lyrics[lyricIndex].TimeSeconds:0.00}s"
                        : "no active line"));
                if (_lyricIndex >= 0 && _lyricIndex < _lyrics.Count)
                {
                    var previous = _lyrics[_lyricIndex];
                    previous.IsCurrent = false;
                    if (previous.Words != null)
                    {
                        foreach (var w in previous.Words) w.IsLineCurrent = false;
                    }
                }
                _lyricIndex = lyricIndex;
                if (_lyricIndex >= 0 && _lyricIndex < _lyrics.Count)
                {
                    var current = _lyrics[_lyricIndex];
                    current.IsCurrent = true;
                    if (current.Words != null)
                    {
                        foreach (var w in current.Words) w.IsLineCurrent = true;
                    }
                }
                OnPropertyChanged(nameof(CurrentLyricText));
                OnPropertyChanged(nameof(CurrentLyricStartTime));
                OnPropertyChanged(nameof(NextLyricText));
                OnPropertyChanged(nameof(PreviousLyricText));
                OnPropertyChanged(nameof(Next2LyricText));

                if (lyricIndex >= 0 && lyricIndex < _lyrics.Count)
                {
                    if (DesktopLyricsBridge.Enabled)
                    {
                        string next = lyricIndex + 1 < _lyrics.Count ? _lyrics[lyricIndex + 1].Text : "";
                        DesktopLyricsBridge.Update(_lyrics[lyricIndex].Text, next);
                    }
                }
                else
                {
                    DesktopLyricsBridge.Clear();
                }
            }

            int subtitleIndex = SubtitleParser.FindCurrentIndex(_subtitles, position);
            if (subtitleIndex != _subtitleIndex)
            {
                _subtitleIndex = subtitleIndex;
                OnPropertyChanged(nameof(CurrentSubtitleText));
                OnPropertyChanged(nameof(CurrentSubtitleFontSize));
                OnPropertyChanged(nameof(CurrentSubtitleBold));
                OnPropertyChanged(nameof(CurrentSubtitleItalic));
                OnPropertyChanged(nameof(CurrentSubtitleColorHex));
                OnPropertyChanged(nameof(CurrentSubtitleAlignment));
            }

            UpdateWordProgress(position);
        }

        private void UpdateWordProgress(double position)
        {
            if (_lyricIndex < 0 || _lyricIndex >= _lyrics.Count)
            {
                return;
            }

            var line = _lyrics[_lyricIndex];
            if (line.Words == null || line.Words.Count == 0)
            {
                return;
            }

            // 未到本行开始时间时不点亮任何字。
            if (position < line.TimeSeconds)
            {
                foreach (var word in line.Words)
                {
                    if (word.IsSung) word.IsSung = false;
                }
                return;
            }

            // 兼容绝对时间与相对行首的字级时间。
            bool relative = line.Words[0].StartSeconds < line.TimeSeconds - 0.01;
            foreach (var word in line.Words)
            {
                double start = relative ? line.TimeSeconds + word.StartSeconds : word.StartSeconds;
                bool sung = position + 0.05 >= start;
                if (word.IsSung != sung)
                {
                    word.IsSung = sung;
                }
            }
        }

        #region 界面绑定属性

        public NavTarget CurrentNav => _currentNav;

        public bool ShowMusicTabs => _currentNav == NavTarget.Music;

        public bool IsPlaylistsVisible => _currentNav == NavTarget.Playlists;

        public bool IsAlbumsVisible => _currentNav == NavTarget.Music && _selectedTab == 2;

        public bool IsSongsVisible => _currentNav == NavTarget.Music && _selectedTab == 0;

        public bool IsArtistsVisible => _currentNav == NavTarget.Music && _selectedTab == 1;

        public bool IsGenericVisible => _currentNav != NavTarget.Music
            && _currentNav != NavTarget.Stats
            && _currentNav != NavTarget.Playlists;

        public bool IsStatsVisible => _currentNav == NavTarget.Stats;

        public bool IsEmpty => !IsLoading && (_currentNav == NavTarget.Stats ? false
            : _currentNav == NavTarget.Music
                ? (_selectedTab == 0 ? Songs.Count == 0 : _selectedTab == 1 ? Artists.Count == 0 : Albums.Count == 0)
                : CurrentItems.Count == 0);

        public string PageTitle { get; private set; } = "我的音乐";

        public string ArtistFilter
        {
            get => _artistFilter;
            private set
            {
                if (SetProperty(ref _artistFilter, value))
                {
                    OnPropertyChanged(nameof(HasArtistFilter));
                    BuildMusicViews();
                    UpdateStatus();
                }
            }
        }

        public bool HasArtistFilter => !string.IsNullOrWhiteSpace(_artistFilter);

        public string PlayAllText => _currentNav == NavTarget.Playlists
            ? $"无序播放所有 ({Playlists.Count})"
            : $"无序播放所有 ({VisiblePlayableItems().Count})";

        public string SortDescription => _sortMode == SortMode.Title ? "标题"
            : _sortMode == SortMode.DateCreated ? "添加时间"
            : _sortMode == SortMode.Duration ? "时长" : "专辑";

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyViews();
                }
            }
        }

        public string StatsText => $"音乐 {_allItems.Count(i => i.Kind == MediaKind.Audio)} 个 · " +
            $"视频 {_allItems.Count(i => i.Kind == MediaKind.Video)} 个 · " +
            $"图片 {_allItems.Count(i => i.Kind == MediaKind.Photo)} 个 · " +
            $"收藏 {_allItems.Count(i => i.IsFavorite)} 个";

        public int SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab != value)
                {
                    _selectedTab = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsEmpty));
                    OnPropertyChanged(nameof(IsAlbumsVisible));
                    OnPropertyChanged(nameof(IsSongsVisible));
                    OnPropertyChanged(nameof(IsArtistsVisible));
                    OnPropertyChanged(nameof(PlayAllText));
                    UpdateStatus();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
        }

        public int SortIndex
        {
            get => (int)_sortMode;
            set
            {
                if ((int)_sortMode != value)
                {
                    _sortMode = (SortMode)value;
                    ApplyViews();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SortDescription));
                }
            }
        }

        public bool IsPlayerVisible => _playback.CurrentItem != null;

        public MediaItem CurrentTrack => _playback.CurrentItem;

        public string TrackTitle => _playback.CurrentItem?.Title ?? "未播放";

        public string TrackSubtitle => _playback.CurrentItem?.Subtitle ?? string.Empty;

        public BitmapImage TrackThumbnail => _playback.CurrentItem?.Thumbnail;

        public bool HasTrackThumbnail => TrackThumbnail != null;

        public SolidColorBrush TrackFallbackBrush =>
            _playback.CurrentItem?.FallbackBrush ?? new SolidColorBrush(Colors.Gray);

        public string TrackInitial =>
            _playback.CurrentItem == null ? "\uE8D6" :
            string.IsNullOrEmpty(_playback.CurrentItem.Title) ? "?" : _playback.CurrentItem.Title.Substring(0, 1);

        public bool IsPlaying => _playback.IsPlaying;

        public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768";

        public bool IsShuffle => _playback.IsShuffle;

        public bool IsRepeat => _playback.IsRepeat;

        // ───────────────────── 歌词 / 字幕 ─────────────────────

        public IReadOnlyList<LyricLine> Lyrics => _lyrics;

        public IList<LyricWord> LyricWords => _lyricIndex >= 0 && _lyricIndex < _lyrics.Count
            ? _lyrics[_lyricIndex].Words : null;

        public double CurrentLyricStartTime => _lyricIndex >= 0 && _lyricIndex < _lyrics.Count
            ? _lyrics[_lyricIndex].TimeSeconds : 0;

        public int LyricIndex => _lyricIndex;

        public bool HasLyrics => _lyrics != null && _lyrics.Count > 0;

        public string CurrentLyricText => _lyricIndex >= 0 && _lyricIndex < _lyrics.Count
            ? _lyrics[_lyricIndex].Text : "";

        public string NextLyricText => _lyricIndex >= 0 && _lyricIndex + 1 < _lyrics.Count
            ? _lyrics[_lyricIndex + 1].Text : "";

        public string PreviousLyricText => _lyricIndex > 0 && _lyricIndex - 1 < _lyrics.Count
            ? _lyrics[_lyricIndex - 1].Text : "";

        public string Next2LyricText => _lyricIndex >= 0 && _lyricIndex + 2 < _lyrics.Count
            ? _lyrics[_lyricIndex + 2].Text : "";

        public IReadOnlyList<string> EmbeddedSubtitleNames =>
            _playback.GetEmbeddedSubtitleTracks().Select(t => t.Item2).ToList();

        public bool HasEmbeddedSubtitles => EmbeddedSubtitleNames.Count > 0;

        public bool IsCurrentVideo => _activeItem != null && _activeItem.Kind == MediaKind.Video;

        public bool IsCurrentAudio => _activeItem != null && _activeItem.Kind == MediaKind.Audio;

        private bool _isCompact;
        public bool IsCompact
        {
            get => _isCompact;
            set
            {
                if (SetProperty(ref _isCompact, value))
                {
                    OnPropertyChanged(nameof(IsPlayOrderVisible));
                }
            }
        }

        public bool IsPlayOrderVisible => IsCurrentAudio && !IsCompact;

        private bool _isQueuePanelOpen;
        public bool IsQueuePanelOpen
        {
            get => _isQueuePanelOpen;
            set => SetProperty(ref _isQueuePanelOpen, value);
        }

        public ObservableCollection<MediaItem> Queue => _playback.Queue;
        public MediaItem CurrentQueueItem => _playback.CurrentItem;
        public int CurrentQueueIndex => _playback.CurrentIndex;

        public IReadOnlyList<SubtitleLine> Subtitles => _subtitles;

        public bool HasSubtitle => _subtitles != null && _subtitles.Count > 0;

        public string CurrentSubtitleText => _subtitleIndex >= 0 && _subtitleIndex < _subtitles.Count
            ? _subtitles[_subtitleIndex].Text : "";

        public double CurrentSubtitleFontSize => _subtitleIndex >= 0 && _subtitleIndex < _subtitles.Count
            ? _subtitles[_subtitleIndex].FontSize : 28;

        public bool CurrentSubtitleBold => _subtitleIndex >= 0 && _subtitleIndex < _subtitles.Count
            && _subtitles[_subtitleIndex].Bold;

        public bool CurrentSubtitleItalic => _subtitleIndex >= 0 && _subtitleIndex < _subtitles.Count
            && _subtitles[_subtitleIndex].Italic;

        public string CurrentSubtitleColorHex => _subtitleIndex >= 0 && _subtitleIndex < _subtitles.Count
            ? _subtitles[_subtitleIndex].ColorHex : "#FFFFFFFF";

        public int CurrentSubtitleAlignment => _subtitleIndex >= 0 && _subtitleIndex < _subtitles.Count
            ? _subtitles[_subtitleIndex].Alignment : 2;

        public string PlayModeGlyph
        {
            get
            {
                switch (_playback.Mode)
                {
                    case PlaybackMode.Shuffle: return "\uE8B1";   // 随机
                    case PlaybackMode.RepeatAll: return "\uE8EE"; // 列表循环
                    case PlaybackMode.RepeatOne: return "\uE8ED"; // 单曲循环
                    case PlaybackMode.PlayOnce: return "\uE71A";  // 本曲后停止
                    default: return "\uE8A6";                     // 顺序
                }
            }
        }

        public string PlayModeText
        {
            get
            {
                switch (_playback.Mode)
                {
                    case PlaybackMode.Shuffle: return "随机";
                    case PlaybackMode.RepeatAll: return "列表循环";
                    case PlaybackMode.RepeatOne: return "单曲循环";
                    case PlaybackMode.PlayOnce: return "只播放本曲";
                    default: return "顺序";
                }
            }
        }

        public string CurrentTimeText => FormatTime(_playback.Position);

        public string TotalTimeText => FormatTime(_playback.Duration);

        public double PlaybackDuration => _playback.Duration.TotalSeconds;

        public double PlaybackPosition
        {
            get => _playback.Position.TotalSeconds;
            set
            {
                if (Math.Abs(value - _playback.Position.TotalSeconds) > 0.75)
                {
                    _playback.Seek(value);
                }
            }
        }

        public double Volume
        {
            get => _playback.VolumePercent;
            set => _playback.SetVolume(value);
        }

        public string RateText => _playback.PlaybackRate.ToString("0.##") + "x";

        #endregion

        private static string FormatTime(TimeSpan value)
        {
            if (value <= TimeSpan.Zero || double.IsNaN(value.TotalSeconds))
            {
                return "0:00";
            }
            return value.TotalHours >= 1
                ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
                : $"{value.Minutes}:{value.Seconds:00}";
        }
    }
}
