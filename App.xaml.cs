using HyperMedia_Lite.Services;
using HyperMedia_Lite.ViewModels;
using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 提供特定于应用程序的行为，以补充默认的应用程序类。
    /// </summary>
    sealed partial class App : Application
    {
        private static MainViewModel _mainViewModel;

        /// <summary>应用是否处于后台（用于后台音频的内存/状态管理）。</summary>
        private bool _isInBackgroundMode;

        /// <summary>从编辑器返回时需要恢复的图片灯箱项。</summary>
        public static Models.MediaItem PhotoToRestore { get; set; }

        /// <summary>
        /// 全局共享的视图模型，保证在主页与设置页之间切换时媒体库和播放状态不丢失。
        /// </summary>
        public static MainViewModel MainViewModel =>
            _mainViewModel ?? (_mainViewModel = new MainViewModel());

        /// <summary>
        /// 初始化单一实例应用程序对象。这是执行的创作代码的第一行，
        /// 已执行，逻辑上等同于 main() 或 WinMain()。
        /// </summary>
        public App()
        {
            HyperMedia_Lite.Helpers.DebugLog.WriteStartupStep("App ctor begin");
            this.InitializeComponent();
            HyperMedia_Lite.Helpers.DebugLog.WriteStartupStep("App ctor InitializeComponent done");
            this.Suspending += OnSuspending;
            this.EnteredBackground += App_EnteredBackground;
            this.LeavingBackground += App_LeavingBackground;
            this.UnhandledException += OnUnhandledException;
            HyperMedia_Lite.Helpers.DebugLog.WriteStartupStep("UnhandledException subscribed");
        }

        private void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // 记录异常，便于定位低版本系统上的兼容性崩溃。
            try
            {
                var ex = e.Exception;
                HyperMedia_Lite.Helpers.DebugLog.WriteStartupStep("CRASH: " +
                    (ex == null ? "unknown" : ex.ToString()));
            }
            catch { }
        }

        /// <summary>
        /// 在应用程序由最终用户正常启动时进行调用。
        /// 将在启动应用程序以打开特定文件等情况下使用。
        /// </summary>
        /// <param name="e">有关启动请求和过程的详细信息。</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            HyperMedia_Lite.Helpers.DebugLog.WriteStartupStep("OnLaunched entered");
            LogStartup("OnLaunched begin");

            Frame rootFrame = Window.Current.Content as Frame;

            // 不要在窗口已包含内容时重复应用程序初始化，
            // 只需确保窗口处于活动状态
            if (rootFrame == null)
            {
                // 创建要充当导航上下文的框架，并导航到第一页
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                // 缓存页面：从歌词选择面板返回时保留「正在播放」浮层状态。
                rootFrame.CacheSize = 3;

                // 将框架放在当前窗口中
                Window.Current.Content = rootFrame;

                LogStartup("frame created, applying theme");
                // 应用主题与强调色（默认跟随系统）
                ThemeService.Load();
                LogStartup("theme applied");
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    LogStartup("navigating to MainPage");
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                    LogStartup("MainPage navigated");
                }
                // 确保当前窗口处于活动状态
                Window.Current.Activate();
                LogStartup("window activated");
            }
        }

        private static void LogStartup(string step)
        {
            try
            {
                string info = $"{step} | os={Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamilyVersion}" +
                    $" | contractV2={HyperMedia_Lite.Helpers.UwpCompat.HasContractV2}" +
                    $" | mediaElement={HyperMedia_Lite.Helpers.UwpCompat.HasMediaPlayerElement}";
                HyperMedia_Lite.Helpers.DebugLog.WriteStartupStep(info);
            }
            catch { }
        }

        /// <summary>
        /// 导航到特定页失败时调用
        /// </summary>
        ///<param name="sender">导航失败的框架</param>
        ///<param name="e">有关导航失败的详细信息</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// 在将要挂起应用程序执行时调用。  在不知道应用程序
        /// 无需知道应用程序会被终止还是会恢复，
        /// 并让内存内容保持不变。
        /// </summary>
        /// <param name="sender">挂起的请求的源。</param>
        /// <param name="e">有关挂起请求的详细信息。</param>
        private void App_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
        {
            // 后台音频由 manifest 的 backgroundMediaPlayback 能力维持，
            // 这里只标记状态，不做长时间工作，也不暂停播放。
            _isInBackgroundMode = true;
        }

        private void App_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
        {
            _isInBackgroundMode = false;
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            // 不要在这里暂停播放：声明了后台音频能力后，
            // 即使窗口最小化 / 应用挂起，音乐也应继续播放。
            deferral.Complete();
        }
    }
}
