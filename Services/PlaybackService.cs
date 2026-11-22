using HyperMedia_Lite.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace HyperMedia_Lite.Services
{
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

        public static PlaybackService Current => LazyInstance.Value;

        public MediaPlayer Player => _player;

        public ObservableCollection<MediaItem> Queue { get; } = new ObservableCollection<MediaItem>();

        public int CurrentIndex { get; private set; } = -1;

        public MediaItem CurrentItem =>
            CurrentIndex >= 0 && CurrentIndex < Queue.Count ? Queue[CurrentIndex] : null;

        public bool IsShuffle { get; private set; }

        public bool IsRepeat { get; private set; }

        public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        public TimeSpan Position => _player.PlaybackSession.Position;

        public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

        /// <summary>播放状态 / 进度发生变化。</summary>
        public event EventHandler StateChanged;

        /// <summary>当前曲目发生变化。</summary>
        public event EventHandler ItemChanged;

        private PlaybackService()
        {
            _player.AutoPlay = false;

            var session = _player.PlaybackSession;
            session.PlaybackStateChanged += (sender, args) => StateChanged?.Invoke(this, EventArgs.Empty);
            session.PositionChanged += (sender, args) => StateChanged?.Invoke(this, EventArgs.Empty);
            session.NaturalDurationChanged += (sender, args) => StateChanged?.Invoke(this, EventArgs.Empty);

            _player.MediaOpened += (sender, args) => StateChanged?.Invoke(this, EventArgs.Empty);
            _player.MediaFailed += (sender, args) => StateChanged?.Invoke(this, EventArgs.Empty);
            _player.MediaEnded += (sender, args) =>
            {
                if (IsRepeat)
                {
                    Seek(0);
                    _player.Play();
                }
                else
                {
                    Next();
                }
            };
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
                _player.Source = MediaSource.CreateFromStorageFile(item.File);
                _player.Play();
            }
            catch (Exception)
            {
                // 格式不支持或文件不可访问时保留队列，由界面显示当前曲目信息。
            }

            ItemChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void TogglePlayPause()
        {
            if (IsPlaying)
            {
                _player.Pause();
            }
            else if (Queue.Count > 0)
            {
                if (_player.Source == null)
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
                _player.Pause();
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
            var session = _player.PlaybackSession;
            if (session.CanSeek && seconds >= 0)
            {
                session.Position = TimeSpan.FromSeconds(seconds);
            }
        }

        public void SetVolume(double percent)
        {
            _player.Volume = Math.Max(0, Math.Min(100, percent)) / 100.0;
        }

        public double VolumePercent => _player.Volume * 100.0;

        public void ToggleShuffle()
        {
            IsShuffle = !IsShuffle;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleShuffle(bool enabled)
        {
            IsShuffle = enabled;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleRepeat()
        {
            IsRepeat = !IsRepeat;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            // MediaPlayer 没有 Stop：暂停并清空源即可停止播放。
            _player.Pause();
            _player.Source = null;
            CurrentIndex = -1;
            Queue.Clear();
            ItemChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
