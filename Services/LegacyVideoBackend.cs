using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 视频后端抽象，用于在 14393+ 的 MediaPlayerElement 与 10240 的 MediaElement 之间切换。
    /// </summary>
    public interface IVideoBackend
    {
        Task PlayAsync(StorageFile file);
        void Play();
        void Pause();
        void Stop();
        void Seek(TimeSpan position);
        TimeSpan Position { get; }
        TimeSpan Duration { get; }
        bool IsPlaying { get; }
        double Volume { get; set; }
        event EventHandler StateChanged;
        event EventHandler MediaEnded;
    }

    /// <summary>
    /// 10240 视频播放后端：包装经典 MediaElement（10240 起可用）。
    /// 使用 SetSource(stream, contentType) 播放本地文件，并用 DispatcherTimer 轮询进度。
    /// </summary>
    public sealed class MediaElementVideoBackend : IVideoBackend
    {
        private readonly MediaElement _element;
        private readonly DispatcherTimer _positionTimer;

        public event EventHandler StateChanged;
        public event EventHandler MediaEnded;

        public MediaElementVideoBackend(MediaElement element)
        {
            _element = element ?? throw new ArgumentNullException(nameof(element));
            _element.AutoPlay = false;
            _element.AreTransportControlsEnabled = false;
            _element.Stretch = Stretch.Uniform;

            _element.CurrentStateChanged += (sender, args) => StateChanged?.Invoke(this, EventArgs.Empty);
            _element.MediaOpened += (sender, args) => StateChanged?.Invoke(this, EventArgs.Empty);
            _element.MediaFailed += (sender, args) => StateChanged?.Invoke(this, EventArgs.Empty);
            _element.MediaEnded += (sender, args) => MediaEnded?.Invoke(this, EventArgs.Empty);

            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _positionTimer.Tick += (sender, args) =>
            {
                if (IsPlaying)
                {
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            _positionTimer.Start();
        }

        public async Task PlayAsync(StorageFile file)
        {
            if (file == null)
            {
                return;
            }

            // 流由 MediaElement 持有，不能立即释放。
            var stream = await file.OpenAsync(FileAccessMode.Read);
            _element.SetSource(stream, file.ContentType);
            _element.Play();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Play() => _element.Play();

        public void Pause() => _element.Pause();

        public void Stop() => _element.Stop();

        public void Seek(TimeSpan position)
        {
            if (_element.CanSeek)
            {
                _element.Position = position;
            }
        }

        public TimeSpan Position => _element.Position;

        public TimeSpan Duration => _element.NaturalDuration;

        public bool IsPlaying =>
            _element.CurrentState == MediaElementState.Playing;

        public double Volume
        {
            get => _element.Volume;
            set => _element.Volume = Math.Max(0, Math.Min(1, value));
        }
    }
}
