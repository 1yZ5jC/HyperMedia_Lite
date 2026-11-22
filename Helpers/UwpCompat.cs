using Windows.Foundation.Metadata;

namespace HyperMedia_Lite.Helpers
{
    /// <summary>
    /// API 版本守卫：部分 XAML / WinRT API 是 14393+ 才引入，编译不报错但低版本运行会崩溃。
    /// </summary>
    public static class UwpCompat
    {
        public static bool HasContractV2 =>
            ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 2);

        public static bool HasContractV4 =>
            ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 4);

        /// <summary>
        /// MediaPlayerElement / MediaPlaybackSession 均随 UniversalApiContract v2 (14393) 引入。
        /// 10240 上 XAML 里不能出现 MediaPlayerElement，播放状态需走 MediaPlayer 的旧 API。
        /// </summary>
        public static bool HasModernPlayback => HasContractV2;

        public static bool HasMediaPlayerElement =>
            HasContractV2 && ApiInformation.IsTypePresent("Windows.UI.Xaml.Controls.MediaPlayerElement");
    }
}
