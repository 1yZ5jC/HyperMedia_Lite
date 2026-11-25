using HyperMedia_Lite.Helpers;
using HyperMedia_Lite.Models;
using System;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.Xaml;

// 10240 兼容路径需要 MediaPlayer 的旧 API，新平台上仅作回退使用。
#pragma warning disable 618

namespace HyperMedia_Lite.Services
{
    /// <summary>播放顺序模式。</summary>
    public enum PlaybackMode
    {
        Sequential,   // 顺序播放
        Shuffle,      // 随机播放
        RepeatAll,    // 列表循环
        RepeatOne,    // 单曲循环
        PlayOnce      // 只播放本曲
    }

    /// <summary>
    /// 应用内唯一的播放核心：封装 MediaPlayer，维护播放队列与传输控制状态。
    /// 同一个 MediaPlayer 实例会被 MainPage 上的 MediaPlayerElement 绑定，用于显示视频画面。
    /// </summary>
    public sealed class PlaybackService
    {
        private static readonly Lazy<PlaybackService> LazyInstance =
            new Lazy<PlaybackService>(() => new PlaybackService());

        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly Random _random = new Random();
        private readonly CoreDispatcher _dispatcher;
        private readonly bool _useLegacyPlayback = !UwpCompat.HasModernPlayback;
        private readonly DispatcherTimer _positionTimer;

        public static PlaybackService Current => LazyInstance.Value;

        public MediaPlayer Player => _player;

        // 用 object 保存，避免在最低版本 10240 的编译单元中直接引用 14393+ 类型。
        private object _playbackItem;

        /// <summary>是否支持平台内嵌字幕轨选择（14393+ 的 MediaPlaybackItem）。</summary>
        public bool CanSelectEmbeddedSubtitles => !_useLegacyPlayback && _playbackItem != null;

        /// <summary>当前媒体容器中可选择的字幕轨（标签 + 轨道引用）。</summary>
        public IReadOnlyList<Tuple<int, string>> GetEmbeddedSubtitleTracks()
        {
            var result = new List<Tuple<int, string>>();
            if (!CanSelectEmbeddedSubtitles)
            {
                return result;
            }
            try
            {
                var tracks = (System.Collections.IList)_playbackItem.GetType()
                    .GetProperty("TimedMetadataTracks").GetValue(_playbackItem);
                if (tracks == null) return result;

                for (int i = 0; i < tracks.Count; i++)
                {
                    var track = tracks[i];
                    if (track == null) continue;

                    string kind = track.GetType().GetProperty("TimedMetadataKind")?.GetValue(track)?.ToString();
                    if (!string.Equals(kind, "Subtitle", StringComparison.OrdinalIgnoreCase)) continue;

                    string label = track.GetType().GetProperty("Label")?.GetValue(track) as string;
                    if (string.IsNullOrWhiteSpace(label))
                        label = track.GetType().GetProperty("Language")?.GetValue(track) as string;
                    if (string.IsNullOrWhiteSpace(label)) label = "字幕轨 " + i;

                    result.Add(Tuple.Create(i, label));
                }
            }
            catch (Exception)
            {
                // ignored
            }
            return result;
        }

        public bool SelectEmbeddedSubtitle(int trackIndex)
        {
            return SetSubtitlePresentation(trackIndex, true);
        }

        public void ClearEmbeddedSubtitles()
        {
            SetSubtitlePresentation(-1, false);
        }

        private bool SetSubtitlePresentation(int selectedIndex, bool onlySelected)
        {
            if (!CanSelectEmbeddedSubtitles)
            {
                return false;
            }
            try
            {
                var tracks = (System.Collections.IList)_playbackItem.GetType()
                    .GetProperty("TimedMetadataTracks").GetValue(_playbackItem);
                if (tracks == null) return false;

                var modeType = Type.GetType("Windows.Media.Core.TimedMetadataTrackPresentationMode, Windows.Foundation.UniversalApiContract");
                if (modeType == null) return false;

                for (int i = 0; i < tracks.Count; i++)
                {
                    var track = tracks[i];
                    if (track == null) continue;
                    string kind = track.GetType().GetProperty("TimedMetadataKind")?.GetValue(track)?.ToString();
                    if (!string.Equals(kind, "Subtitle", StringComparison.OrdinalIgnoreCase)) continue;

                    bool enable = onlySelected && i == selectedIndex;
                    // PlatformPresented = 0, Disabled = 2
                    int modeValue = enable ? 0 : 2;
                    object mode = Enum.ToObject(modeType, modeValue);
                    tracks.GetType().GetMethod("SetPresentationMode")?.Invoke(tracks,
                        new object[] { (uint)i, mode });
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>10240 上的视频后端（MediaElement）。14393+ 保持为 null。</summary>
        private IVideoBackend _videoBackend;
        public IVideoBackend VideoBackend
        {
            get => _videoBackend;
            set
            {
                if (_videoBackend != null)
                {
                    _videoBackend.StateChanged -= OnBackendStateChanged;
                    _videoBackend.MediaEnded -= OnBackendMediaEnded;
                }
                _videoBackend = value;
                if (_videoBackend != null)
                {
                    _videoBackend.StateChanged += OnBackendStateChanged;
                    _videoBackend.MediaEnded += OnBackendMediaEnded;
                }
                RaiseStateChanged();
            }
        }

        private bool UseVideoBackend =>
            _useLegacyPlayback && _videoBackend != null && CurrentItem?.Kind == MediaKind.Video;

        public ObservableCollection<MediaItem> Queue { get; } = new ObservableCollection<MediaItem>();

        public int CurrentIndex { get; private set; } = -1;

        public MediaItem CurrentItem =>
            CurrentIndex >= 0 && CurrentIndex < Queue.Count ? Queue[CurrentIndex] : null;

        private const string ModeKey = "PlaybackMode";

        public PlaybackMode Mode { get; private set; } = PlaybackMode.Sequential;

        public bool IsShuffle => Mode == PlaybackMode.Shuffle;

        public bool IsRepeat => Mode == PlaybackMode.RepeatAll || Mode == PlaybackMode.RepeatOne;

        public bool IsPlaying => UseVideoBackend
            ? _videoBackend.IsPlaying
            : _useLegacyPlayback
                ? _player.CurrentState == MediaPlayerState.Playing
                : _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        public TimeSpan Position => UseVideoBackend
            ? _videoBackend.Position
            : _useLegacyPlayback
                ? _player.Position
                : _player.PlaybackSession.Position;

        public TimeSpan Duration => UseVideoBackend
            ? _videoBackend.Duration
            : _useLegacyPlayback
                ? _player.NaturalDuration
                : _player.PlaybackSession.NaturalDuration;

        /// <summary>播放状态 / 进度发生变化。</summary>
        public event EventHandler StateChanged;

        /// <summary>当前曲目发生变化。</summary>
        public event EventHandler ItemChanged;

        private PlaybackService()
        {
            // MediaPlayer 的事件可能来自 MTA 线程，UI 绑定更新必须切回 UI 线程。
            _dispatcher = Window.Current?.CoreWindow?.Dispatcher
                ?? CoreApplication.MainView.CoreWindow.Dispatcher;

            _player.AutoPlay = false;

            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(ModeKey, out object rawMode)
                && rawMode is string modeText && Enum.TryParse(modeText, out PlaybackMode savedMode))
            {
                Mode = savedMode;
            }

            if (_useLegacyPlayback)
            {
                // 10240：PlaybackSession 不存在，使用 MediaPlayer 旧 API + 定时器轮询进度。
                _player.CurrentStateChanged += (sender, args) => RaiseStateChanged();
                _player.SeekCompleted += (sender, args) => RaiseStateChanged();
                _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _positionTimer.Tick += (sender, args) =>
                {
                    if (IsPlaying)
                    {
                        RaiseStateChanged();
                    }
                };
                _positionTimer.Start();
            }
            else
            {
                var session = _player.PlaybackSession;
                session.PlaybackStateChanged += (sender, args) => RaiseStateChanged();
                session.PositionChanged += (sender, args) => RaiseStateChanged();
                session.NaturalDurationChanged += (sender, args) => RaiseStateChanged();
            }

            _player.MediaOpened += (sender, args) => RaiseStateChanged();
            _player.MediaFailed += (sender, args) => RaiseStateChanged();
            _player.MediaEnded += (sender, args) => HandleMediaEnded();
        }

        private void OnBackendStateChanged(object sender, EventArgs e) => RaiseStateChanged();

        private void OnBackendMediaEnded(object sender, EventArgs e) => HandleMediaEnded();

        private void HandleMediaEnded()
        {
            switch (Mode)
            {
                case PlaybackMode.RepeatOne:
                    Seek(0);
                    PlayCurrent();
                    break;
                case PlaybackMode.PlayOnce:
                    PauseCurrent();
                    Seek(0);
                    break;
                case PlaybackMode.Shuffle:
                case PlaybackMode.RepeatAll:
                    Next();
                    break;
                default:
                    if (CurrentIndex + 1 < Queue.Count)
                    {
                        Next();
                    }
                    else
                    {
                        PauseCurrent();
                        Seek(0);
                    }
                    break;
            }
        }

        private void PlayCurrent()
        {
            if (UseVideoBackend) _videoBackend.Play(); else _player.Play();
        }

        private void PauseCurrent()
        {
            if (UseVideoBackend) _videoBackend.Pause(); else _player.Pause();
        }

        private void DispatchToUIThread(Action action)
        {
            if (_dispatcher == null || _dispatcher.HasThreadAccess)
            {
                action();
                return;
            }
            _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }

        private void RaiseStateChanged()
        {
            DispatchToUIThread(() => StateChanged?.Invoke(this, EventArgs.Empty));
        }

        private void RaiseItemChanged()
        {
            DispatchToUIThread(() => ItemChanged?.Invoke(this, EventArgs.Empty));
        }

        /// <summary>
        /// 用给定的播放列表替换当前队列，并从 startItem 开始播放。
        /// </summary>
        public async System.Threading.Tasks.Task PlayQueueAsync(IEnumerable<MediaItem> items, MediaItem startItem)
        {
            Queue.Clear();
            foreach (var item in items)
            {
                Queue.Add(item);
            }

            CurrentIndex = startItem != null ? Queue.IndexOf(startItem) : -1;
            if (CurrentIndex < 0 && Queue.Count > 0)
            {
                CurrentIndex = 0;
            }

            await LoadCurrentAsync();
        }

        private async System.Threading.Tasks.Task LoadCurrentAsync()
        {
            var item = CurrentItem;
            if (item == null || item.File == null)
            {
                Stop();
                return;
            }

            try
            {
                if (_useLegacyPlayback && _videoBackend != null && item.Kind == MediaKind.Video)
                {
                    // 10240：视频走 MediaElement 后端，应用内播放。
                    await _videoBackend.PlayAsync(item.File);
                }
                else if (!_useLegacyPlayback)
                {
                    // 14393+：用反射构造 MediaPlaybackItem，避免在 10240 编译单元中引用该类型。
                    var source = MediaSource.CreateFromStorageFile(item.File);
                    try
                    {
                        var itemType = Type.GetType("Windows.Media.Playback.MediaPlaybackItem, Windows.Foundation.UniversalApiContract");
                        if (itemType != null)
                        {
                            _playbackItem = Activator.CreateInstance(itemType, new object[] { source });
                            _player.Source = (Windows.Media.Playback.IMediaPlaybackSource)_playbackItem;
                        }
                        else
                        {
                            _player.Source = source;
                        }
                    }
                    catch (Exception)
                    {
                        _player.Source = source;
                    }
                    _player.Play();
                }
                else
                {
                    _player.Source = MediaSource.CreateFromStorageFile(item.File);
                    _player.Play();
                }
            }
            catch (Exception)
            {
                // 格式不支持或文件不可访问时保留队列，由界面显示当前曲目信息。
            }

            RaiseItemChanged();
            RaiseStateChanged();
        }

        public void TogglePlayPause()
        {
            if (IsPlaying)
            {
                PauseCurrent();
            }
            else if (Queue.Count > 0)
            {
                if (UseVideoBackend)
                {
                    _videoBackend.Play();
                }
                else if (_player.Source == null)
                {
                    _ = LoadCurrentAsync();
                }
                else
                {
                    _player.Play();
                }
            }
        }

        public void Pause()
        {
            if (IsPlaying)
            {
                PauseCurrent();
            }
        }

        public void Next()
        {
            if (Queue.Count == 0)
            {
                return;
            }
            CurrentIndex = IsShuffle ? _random.Next(Queue.Count) : (CurrentIndex + 1) % Queue.Count;
            _ = LoadCurrentAsync();
        }

        public void Previous()
        {
            if (Queue.Count == 0)
            {
                return;
            }

            if (Position > TimeSpan.FromSeconds(3))
            {
                Seek(0);
                return;
            }

            CurrentIndex = IsShuffle
                ? _random.Next(Queue.Count)
                : (CurrentIndex - 1 + Queue.Count) % Queue.Count;
            _ = LoadCurrentAsync();
        }

        public void Seek(double seconds)
        {
            if (seconds < 0)
            {
                return;
            }

            if (UseVideoBackend)
            {
                _videoBackend.Seek(TimeSpan.FromSeconds(seconds));
            }
            else if (_useLegacyPlayback)
            {
                if (_player.CanSeek)
                {
                    _player.Position = TimeSpan.FromSeconds(seconds);
                }
            }
            else
            {
                var session = _player.PlaybackSession;
                if (session.CanSeek)
                {
                    session.Position = TimeSpan.FromSeconds(seconds);
                }
            }
        }

        public void SetVolume(double percent)
        {
            double volume = Math.Max(0, Math.Min(100, percent)) / 100.0;
            _player.Volume = volume;
            if (_videoBackend != null)
            {
                _videoBackend.Volume = volume;
            }
        }

        public double VolumePercent => (UseVideoBackend ? _videoBackend.Volume : _player.Volume) * 100.0;

        public double PlaybackRate
        {
            get => UseVideoBackend
                ? _videoBackend.Rate
                : _useLegacyPlayback ? _player.PlaybackRate : _player.PlaybackSession.PlaybackRate;
            set
            {
                double rate = Math.Max(0.25, Math.Min(4.0, value));
                if (UseVideoBackend)
                {
                    _videoBackend.Rate = rate;
                }
                else if (_useLegacyPlayback)
                {
                    _player.PlaybackRate = rate;
                }
                else
                {
                    _player.PlaybackSession.PlaybackRate = rate;
                }
                RaiseStateChanged();
            }
        }

        public void SetMode(PlaybackMode mode)
        {
            Mode = mode;
            ApplicationData.Current.LocalSettings.Values[ModeKey] = mode.ToString();
            RaiseStateChanged();
        }

        /// <summary>按 顺序 → 随机 → 列表循环 → 单曲循环 → 只播放本曲 依次切换。</summary>
        public void CycleMode()
        {
            SetMode((PlaybackMode)(((int)Mode + 1) % Enum.GetValues(typeof(PlaybackMode)).Length));
        }

        public void Stop()
        {
            // MediaPlayer 没有 Stop：暂停并清空源即可停止播放。
            _videoBackend?.Stop();
            _player.Pause();
            _player.Source = null;
            _playbackItem = null;
            CurrentIndex = -1;
            Queue.Clear();
            RaiseItemChanged();
            RaiseStateChanged();
        }
    }
}
