using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 桌面歌词悬浮窗的跨视图桥：主窗口写 Line1/Line2/Version，
    /// 悬浮窗用定时器轮询 Version，避免跨线程 UI 调用。
    /// 移植自 HyperMedia，保持 10240 兼容。
    /// </summary>
    public static class DesktopLyricsBridge
    {
        public static string Line1;
        public static string Line2;
        public static long Version;

        private const string SettingsKey = "DesktopLyrics";

        private static CoreApplicationView _overlayCoreView;
        private static ApplicationView _overlayView;
        private static bool _busy;

        public static void Update(string line1, string line2)
        {
            try
            {
                Line1 = line1 ?? string.Empty;
                Line2 = line2 ?? string.Empty;
                Version++;
            }
            catch (Exception ex) { Debug.WriteLine("[DesktopLyrics] Update failed: " + ex.Message); }
        }

        public static void Clear()
        {
            try
            {
                Line1 = string.Empty;
                Line2 = string.Empty;
                Version++;
            }
            catch (Exception ex) { Debug.WriteLine("[DesktopLyrics] Clear failed: " + ex.Message); }
        }

        public static bool Enabled
        {
            get
            {
                try
                {
                    return ApplicationData.Current.LocalSettings.Values.TryGetValue(SettingsKey, out object v)
                        && v is bool b && b;
                }
                catch { return false; }
            }
            set
            {
                try { ApplicationData.Current.LocalSettings.Values[SettingsKey] = value; }
                catch (Exception ex) { Debug.WriteLine("[DesktopLyrics] persist failed: " + ex.Message); }
            }
        }

        public static async Task ToggleAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                if (_overlayView != null)
                {
                    await CloseInternalAsync(_overlayView);
                    _overlayCoreView = null;
                    _overlayView = null;
                    Enabled = false;
                    return;
                }

                var coreView = CoreApplication.CreateNewView();
                int viewId = -1;
                await coreView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        var frame = new Frame();
                        frame.Navigate(typeof(LyricsOverlayPage));
                        Window.Current.Content = frame;
                        Window.Current.Activate();

                        var view = ApplicationView.GetForCurrentView();
                        try { view.SetPreferredMinSize(new Size(360, 90)); } catch { }

                        _overlayCoreView = coreView;
                        _overlayView = view;
                        viewId = view.Id;
                    }
                    catch (Exception ex) { Debug.WriteLine("[DesktopLyrics] build view failed: " + ex); }
                });

                if (viewId == -1 || _overlayView == null)
                {
                    _overlayCoreView = null;
                    _overlayView = null;
                    Enabled = false;
                    return;
                }

                bool shown = await ApplicationViewSwitcher.TryShowAsStandaloneAsync(
                    viewId, ViewSizePreference.Default);
                if (!shown)
                {
                    await CloseInternalAsync(_overlayView);
                    _overlayCoreView = null;
                    _overlayView = null;
                    Enabled = false;
                    return;
                }

                await _overlayCoreView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        var view = ApplicationView.GetForCurrentView();
                        // IsAlwaysOnTop 是 15063+：反射设置，低版本安全跳过。
                        try
                        {
                            var property = typeof(ApplicationView).GetRuntimeProperty("IsAlwaysOnTop");
                            if (property != null && property.CanWrite) property.SetValue(view, true);
                        }
                        catch { }
                        view.TryResizeView(new Size(420, 96));
                    }
                    catch (Exception ex) { Debug.WriteLine("[DesktopLyrics] configure failed: " + ex.Message); }
                });

                Enabled = true;
            }
            catch (Exception ex) { Debug.WriteLine("[DesktopLyrics] ToggleAsync failed: " + ex); }
            finally { _busy = false; }
        }

        internal static void HandleOverlayClosed(ApplicationView view)
        {
            if (view != null && ReferenceEquals(_overlayView, view))
            {
                _overlayCoreView = null;
                _overlayView = null;
                Enabled = false;
            }
        }

        private static async Task CloseInternalAsync(ApplicationView view)
        {
            try { await view.TryConsolidateAsync(); }
            catch (Exception ex) { Debug.WriteLine("[DesktopLyrics] consolidate failed: " + ex.Message); }
        }
    }
}
