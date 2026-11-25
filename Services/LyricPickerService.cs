using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 把歌词候选选择页推入 MainPage 内的专用 Frame（覆盖在「正在播放」之上），
    /// 并等待用户结果。这样关闭面板后仍停留在正在播放，而不是回到主页。
    /// </summary>
    public static class LyricPickerService
    {
        /// <summary>由 MainPage 在构造时提供页面内的 Frame。</summary>
        public static Frame PickerFrame { get; set; }

        /// <summary>由 MainPage 提供，控制覆盖层的显示与隐藏。</summary>
        public static Action<bool> OverlayVisibilityChanged { get; set; }

        public static async Task<LyricCandidate> PickAsync(
            List<LyricCandidate> candidates, string artist = null, string title = null, string album = null)
        {
            var frame = PickerFrame;
            if (frame == null)
            {
                return null;
            }

            var completion = new TaskCompletionSource<LyricCandidate>();
            var args = new LyricPickerArgs
            {
                Candidates = candidates,
                Artist = artist,
                Title = title,
                Album = album,
                Completion = completion
            };

            OverlayVisibilityChanged?.Invoke(true);
            await frame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                () =>
                {
                    // 每次都是全新会话，清掉旧的后退记录避免反复打开后栈无限增长。
                    frame.BackStack.Clear();
                    frame.Navigate(typeof(LyricPickerPage), args);
                });

            var result = await completion.Task;

            OverlayVisibilityChanged?.Invoke(false);
            return result;
        }
    }
}
