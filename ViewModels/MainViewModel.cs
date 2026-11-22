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
        Duration
    }

    public delegate void MediaItemEventHandler(object sender, MediaItem item);

    /// <summary>
    /// 主视图模型：导航、搜索、专辑/歌手/歌曲视图、收藏、排序，以及播放状态转发。
    /// </summary>
    public sealed class MainViewModel : BindableBase
    {
        private const string FavoritesKey = "FavoritePaths";

        private readonly LibraryService _library = new LibraryService();
        private readonly PlaybackService _playback = PlaybackService.Current;
        private readonly HashSet<string> _favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MediaItem> _allItems = new List<MediaItem>();

        private NavTarget _currentNav = NavTarget.Music;
        private SortMode _sortMode = SortMode.Title;
        private int _selectedTab = 2; // 默认「专辑」，与参考图一致
        private string _pageTitle = "我的音乐";
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
        public ObservableCollection<MediaItem> Songs { get; } = new ObservableCollection<MediaItem>();
        public ObservableCollection<AlbumCard> Albums { get; } = new ObservableCollection<AlbumCard>();
        public ObservableCollection<ArtistCard> Artists { get; } = new ObservableCollection<ArtistCard>();
        public IReadOnlyList<string> SortOptions { get; } = new[] { "标题", "最近添加", "时长" };

        public RelayCommand AddFilesCommand { get; }
        public RelayCommand AddFolderCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand ShuffleAllCommand { get; }
        public RelayCommand TogglePlayPauseCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand PreviousCommand { get; }
        public RelayCommand CyclePlayModeCommand { get; }
        public RelayCommand SetPlayModeCommand { get; }
        public RelayCommand ChangeLyricsCommand { get; }
        public RelayCommand ToggleDesktopLyricsCommand { get; }
        public RelayCommand SelectEmbeddedSubtitleCommand { get; }
        public RelayCommand ClearSubtitleCommand { get; }
        public RelayCommand SeekToLyricCommand { get; }
        public RelayCommand CycleRateCommand { get; }
        public RelayCommand ExpandPlayerCommand { get; }
        public RelayCommand ClearArtistFilterCommand { get; }

        public event MediaItemEventHandler PhotoRequested;
        public event EventHandler<AlbumCard> AlbumRequested;
        public event EventHandler<ArtistCard> ArtistRequested;

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
            ChangeLyricsCommand = new RelayCommand(_ => _ = ReloadLyricsAsync());
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
            _currentNav = target;
            foreach (var item in NavItems)
            {
                item.IsSelected = item.Target == target;
            }
            PageTitle = target == NavTarget.Music ? "我的音乐"
                : NavItems.FirstOrDefault(n => n.Target == target)?.Label ?? "媒体";
            OnPropertyChanged(nameof(ShowMusicTabs));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(IsAlbumsVisible));
            OnPropertyChanged(nameof(IsSongsVisible));
            OnPropertyChanged(nameof(IsArtistsVisible));
            OnPropertyChanged(nameof(IsGenericVisible));
            OnPropertyChanged(nameof(IsStatsVisible));
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
            UpdateLyricAndSubtitle(_playback.Position.TotalSeconds);
        }

        private void OnPlaybackItemChanged(object sender, EventArgs e)
        {
            _activeItem = _playback.CurrentItem;
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

        private void ClearSubtitles()
        {
            _playback.ClearEmbeddedSubtitles();
            _subtitles = new List<SubtitleLine>();
            _subtitleIndex = -1;
            OnPropertyChanged(nameof(Subtitles));
            OnPropertyChanged(nameof(HasSubtitle));
            OnPropertyChanged(nameof(CurrentSubtitleText));
        }

        private async System.Threading.Tasks.Task ReloadLyricsAsync()
        {
            var item = _activeItem;
            if (item == null)
            {
                return;
            }
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

            var file = await SubtitleService.FindSiblingAsync(item.File);
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
                _lyricIndex = lyricIndex;
                OnPropertyChanged(nameof(CurrentLyricText));
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

        private int _wordStyledLine = -1;

        private void UpdateWordProgress(double position)
        {
            if (_lyricIndex < 0 || _lyricIndex >= _lyrics.Count)
            {
                return;
            }
            var line = _lyrics[_lyricIndex];
            if (line.Words == null || line.Words.Count == 0 || !IsPlaying)
            {
                return;
            }
            // 逐字进度由页面根据 CurrentLyricText 与 Words 自行渲染；这里仅通知刷新。
            OnPropertyChanged(nameof(LyricWords));
        }

        #region 界面绑定属性

        public NavTarget CurrentNav => _currentNav;

        public bool ShowMusicTabs => _currentNav == NavTarget.Music;

        public bool IsAlbumsVisible => _currentNav == NavTarget.Music && _selectedTab == 2;

        public bool IsSongsVisible => _currentNav == NavTarget.Music && _selectedTab == 0;

        public bool IsArtistsVisible => _currentNav == NavTarget.Music && _selectedTab == 1;

        public bool IsGenericVisible => _currentNav != NavTarget.Music && _currentNav != NavTarget.Stats;

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

        public string PlayAllText => $"无序播放所有 ({VisiblePlayableItems().Count})";

        public string SortDescription => _sortMode == SortMode.Title ? "标题"
            : _sortMode == SortMode.DateCreated ? "添加时间" : "时长";

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
