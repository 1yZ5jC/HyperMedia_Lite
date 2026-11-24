using System;
using System.Threading.Tasks;
using HyperMedia_Lite.Models;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 系统媒体控制（锁屏 / 通知中心 / 耳机与键盘媒体键）。
    /// 使用 10240 可用的 SystemMediaTransportControls.GetForCurrentView()。
    /// </summary>
    public sealed class SystemMediaControlsService
    {
        private static readonly Lazy<SystemMediaControlsService> LazyInstance =
            new Lazy<SystemMediaControlsService>(() => new SystemMediaControlsService());

        private const string EnabledKey = "SystemMediaControlsEnabled";

        public static SystemMediaControlsService Current => LazyInstance.Value;

        public static bool IsEnabledByUser
        {
            get
            {
                try
                {
                    if (!ApplicationData.Current.LocalSettings.Values.TryGetValue(EnabledKey, out object v))
                    {
                        return true; // 默认开启，但只在有曲目时激活
                    }
                    return v is bool b && b;
                }
                catch { return false; }
            }
            set
            {
                try { ApplicationData.Current.LocalSettings.Values[EnabledKey] = value; }
                catch { }
            }
        }

        private readonly SystemMediaTransportControls _controls;
        private readonly PlaybackService _playback = PlaybackService.Current;
        private bool _initialized;

        private SystemMediaControlsService()
        {
            try
            {
                _controls = SystemMediaTransportControls.GetForCurrentView();
            }
            catch (Exception)
            {
                _controls = null;
            }
        }

        /// <summary>在 UI 线程调用一次，启用按钮并接管媒体键。</summary>
        public void Initialize()
        {
            if (!IsEnabledByUser || _initialized || _controls == null)
            {
                return;
            }
            _initialized = true;

            try
            {
                _controls.IsEnabled = true;
                _controls.IsPlayEnabled = true;
                _controls.IsPauseEnabled = true;
                _controls.IsNextEnabled = true;
                _controls.IsPreviousEnabled = true;
                _controls.ButtonPressed += OnButtonPressed;
            }
            catch (Exception)
            {
                // 系统不支持时静默降级。
            }
        }

        private void OnButtonPressed(SystemMediaTransportControls sender,
            SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            try
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        if (!_playback.IsPlaying) _playback.TogglePlayPause();
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        if (_playback.IsPlaying) _playback.Pause();
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        _playback.Next();
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                        _playback.Previous();
                        break;
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        /// <summary>更新曲名、艺人、专辑与封面缩略图。</summary>
        public async Task UpdateAsync(MediaItem item, bool isPlaying)
        {
            if (!IsEnabledByUser || _controls == null)
            {
                return;
            }

            try
            {
                // 没有曲目时彻底关闭控制器，系统不会弹出媒体控制面板。
                if (item == null || item.File == null)
                {
                    if (_initialized)
                    {
                        _controls.DisplayUpdater.ClearAll();
                        _controls.PlaybackStatus = MediaPlaybackStatus.Stopped;
                        _controls.IsEnabled = false;
                    }
                    return;
                }

                // 只有真正开始播放才初始化并启用 SMTC。
                Initialize();
                if (!_controls.IsEnabled)
                {
                    _controls.IsEnabled = true;
                }

                var updater = _controls.DisplayUpdater;

                updater.Type = item.Kind == MediaKind.Video
                    ? MediaPlaybackType.Video : MediaPlaybackType.Music;

                if (item.Kind == MediaKind.Video)
                {
                    updater.VideoProperties.Title = item.Title;
                }
                else
                {
                    updater.MusicProperties.Title = item.Title;
                    updater.MusicProperties.Artist = item.Subtitle;
                }

                try
                {
                    var thumbnail = await item.File.GetThumbnailAsync(
                        item.Kind == MediaKind.Video ? ThumbnailMode.VideosView : ThumbnailMode.MusicView, 256);
                    if (thumbnail != null && thumbnail.Size > 0)
                    {
                        updater.Thumbnail = RandomAccessStreamReference.CreateFromStream(thumbnail);
                    }
                }
                catch (Exception)
                {
                    // 无缩略图不影响元数据。
                }

                updater.Update();
                _controls.PlaybackStatus = isPlaying
                    ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public void UpdateStatus(bool isPlaying)
        {
            if (!IsEnabledByUser || _controls == null || !_controls.IsEnabled)
            {
                return;
            }
            try
            {
                _controls.PlaybackStatus = isPlaying
                    ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}
