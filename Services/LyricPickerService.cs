using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 把歌词候选选择页推入当前 Frame，并等待用户结果。
    /// 在 MainPage.Loaded 之后调用，避免过早访问导航栈。
    /// </summary>
    public static class LyricPickerService
    {
        public static async Task<LyricCandidate> PickAsync(List<LyricCandidate> candidates)
        {
            var frame = Window.Current?.Content as Frame;
            if (frame == null)
            {
                return null;
            }

            var completion = new TaskCompletionSource<LyricCandidate>();
            var args = new LyricPickerArgs
            {
                Candidates = candidates,
                Completion = completion
            };

            await frame.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                () => frame.Navigate(typeof(LyricPickerPage), args));

            return await completion.Task;
        }
    }
}
