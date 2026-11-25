using HyperMedia_Lite.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 在线歌词候选选择页：列出多个来源的匹配结果，预览歌词后由用户选择。
    /// 通过 LyricsService.CandidatePicker 回调把选择结果交回加载管线。
    /// </summary>
    public sealed partial class LyricPickerPage : Page
    {
        private TaskCompletionSource<LyricCandidate> _completion;
        private List<LyricCandidate> _candidates = new List<LyricCandidate>();

        public LyricPickerPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is LyricPickerArgs args)
            {
                _completion = args.Completion;
                _candidates = args.Candidates ?? new List<LyricCandidate>();
                StatusText.Text = $"找到 {_candidates.Count} 个结果";
                CandidateList.ItemsSource = _candidates;
                if (_candidates.Count > 0)
                {
                    CandidateList.SelectedIndex = 0;
                }
            }
        }

        private void CandidateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var candidate = CandidateList.SelectedItem as LyricCandidate;
            ConfirmButton.IsEnabled = candidate != null && !string.IsNullOrEmpty(candidate.LyricText);
            PreviewText.Text = candidate == null
                ? string.Empty
                : Preview(candidate.LyricText);
        }

        private static string Preview(string lyricText)
        {
            if (string.IsNullOrEmpty(lyricText)) return "(无歌词内容)";

            // 去掉时间标签，只保留文字，最多 40 行。
            string text = System.Text.RegularExpressions.Regex.Replace(lyricText, @"\[\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?\]", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<\d{1,3}:\d{1,2}(?:\.\d{1,3})?>", "");

            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int count = Math.Min(lines.Length, 40);
            string result = string.Join("\n", lines, 0, count);
            return lines.Length > count ? result + "\n…" : result;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Complete(CandidateList.SelectedItem as LyricCandidate);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Complete(null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // 系统返回键 / 导航离开等同于取消，避免回调悬挂。
            Complete(null);
            base.OnNavigatedFrom(e);
        }

        private void Complete(LyricCandidate result)
        {
            if (_completion == null) return;
            var completion = _completion;
            _completion = null;
            completion.TrySetResult(result);
        }
    }

    /// <summary>导航参数。</summary>
    public sealed class LyricPickerArgs
    {
        public List<LyricCandidate> Candidates { get; set; }
        public TaskCompletionSource<LyricCandidate> Completion { get; set; }
    }
}
