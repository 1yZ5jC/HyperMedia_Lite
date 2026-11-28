using Windows.Storage;

namespace HyperMedia_Lite.Services
{
    /// <summary>图片查看器的轻量设置（本地保存）。</summary>
    public static class PhotoViewerSettings
    {
        private const string TransitionKey = "ImageTransition";
        private const string SlideSecondsKey = "SlideShowSeconds";
        private const string MouseWheelModeKey = "ImageMouseWheelMode";

        /// <summary>换图过渡：Fade / Zoom / Pan / Flicker。</summary>
        public static string Transition
        {
            get => ApplicationData.Current.LocalSettings.Values.TryGetValue(TransitionKey, out object value)
                && value is string text ? text : "Fade";
            set => ApplicationData.Current.LocalSettings.Values[TransitionKey] = value;
        }

        public static int SlideShowSeconds
        {
            get => ApplicationData.Current.LocalSettings.Values.TryGetValue(SlideSecondsKey, out object value)
                && value is int seconds ? seconds : 3;
            set => ApplicationData.Current.LocalSettings.Values[SlideSecondsKey] = value;
        }

        /// <summary>鼠标滚轮行为：Zoom = 缩放，Scroll = 平移画面，SwitchPhoto = 切换图片。</summary>
        public static string MouseWheelMode
        {
            get => ApplicationData.Current.LocalSettings.Values.TryGetValue(MouseWheelModeKey, out object value)
                && value is string text ? text : "Zoom";
            set => ApplicationData.Current.LocalSettings.Values[MouseWheelModeKey] = value;
        }
    }
}
