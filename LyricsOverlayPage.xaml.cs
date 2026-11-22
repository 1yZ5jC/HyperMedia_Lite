using System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia_Lite
{
    /// <summary>桌面歌词悬浮窗：轮询 DesktopLyricsBridge.Version 更新两行文字。</summary>
    public sealed partial class LyricsOverlayPage : Page
    {
        private const int PollIntervalMs = 150;

        private DispatcherTimer _timer;
        private long _lastVersion = -1;
        private WindowClosedEventHandler _windowClosed;

        public LyricsOverlayPage()
        {
            InitializeComponent();
            ApplyLines();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _lastVersion = DesktopLyricsBridge.Version;
            ApplyLines();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollIntervalMs) };
            _timer.Tick += OnPollTick;
            _timer.Start();

            try
            {
                _windowClosed = OnOverlayWindowClosed;
                Window.Current.Closed += _windowClosed;
            }
            catch { }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnPollTick;
                _timer = null;
            }
            try
            {
                if (_windowClosed != null && Window.Current != null)
                    Window.Current.Closed -= _windowClosed;
            }
            catch { }
            _windowClosed = null;
        }

        private void OnOverlayWindowClosed(object sender, CoreWindowEventArgs e)
        {
            try
            {
                DesktopLyricsBridge.HandleOverlayClosed(
                    Windows.UI.ViewManagement.ApplicationView.GetForCurrentView());
            }
            catch { }
        }

        private void OnPollTick(object sender, object e)
        {
            long version = DesktopLyricsBridge.Version;
            if (version == _lastVersion) return;
            _lastVersion = version;
            ApplyLines();
        }

        private void ApplyLines()
        {
            try
            {
                string line1 = DesktopLyricsBridge.Line1 ?? string.Empty;
                string line2 = DesktopLyricsBridge.Line2 ?? string.Empty;
                Line1Under.Text = line1;
                Line1Over.Text = line1;
                Line2Text.Text = line2;
                LyricBubble.Visibility = line1.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        private async void Close_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DesktopLyricsBridge.HandleOverlayClosed(
                    Windows.UI.ViewManagement.ApplicationView.GetForCurrentView());
                await Windows.UI.ViewManagement.ApplicationView.GetForCurrentView().TryConsolidateAsync();
            }
            catch { }
        }
    }
}
