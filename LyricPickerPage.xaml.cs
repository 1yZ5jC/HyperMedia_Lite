using HyperMedia_Lite.Services;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 歌词选择面板：可切换来源、自定义搜索、查看候选预览并选定歌词。
    /// 通过 LyricsService.CandidatePicker 回调把结果交回加载管线。
    /// </summary>
    public sealed partial class LyricPickerPage : Page
    {
        private TaskCompletionSource<LyricCandidate> _completion;
        private bool _completionSent;
        private List<LyricCandidate> _candidates = new List<LyricCandidate>();
        private string _artist;
        private string _title;
        private string _album;

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
                _artist = args.Artist;
                _title = args.Title;
                _album = args.Album;

                TitleBox.Text = _title ?? string.Empty;
                ArtistBox.Text = _artist ?? string.Empty;
                _candidates = args.Candidates ?? new List<LyricCandidate>();

                if (_candidates.Count > 0)
                {
                    ShowResults(_candidates, $"已找到 {_candidates.Count} 个结果");
                }
                else if (!string.IsNullOrWhiteSpace(_title))
                {
                    // 面板先显示出来，再异步搜索，避免点击按钮后长时间无响应。
                    _ = DoSearchAsync();
                }
                else
                {
                    ShowResults(_candidates, "请输入歌名并搜索");
                }
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await DoSearchAsync();
        }

        private async Task DoSearchAsync()
        {
            string title = TitleBox.Text.Trim();
            string artist = ArtistBox.Text.Trim();
            if (string.IsNullOrEmpty(title))
            {
                StatusText.Text = "请输入歌名";
                return;
            }

            string source = (SourceCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";

            SearchButton.IsEnabled = false;
            LoadingBar.Visibility = Visibility.Visible;
            StatusText.Text = "正在搜索…";
            CandidateList.ItemsSource = null;
            PreviewText.Text = string.Empty;
            ConfirmButton.IsEnabled = false;

            try
            {
                var result = await OnlineLyrics.SearchAsync(artist, title, _album, source);
                _candidates = result ?? new List<LyricCandidate>();
                ShowResults(_candidates, _candidates.Count == 0 ? "未找到歌词" : $"找到 {_candidates.Count} 个结果");
            }
            catch (Exception ex)
            {
                StatusText.Text = "搜索失败：" + ex.Message;
            }
            finally
            {
                SearchButton.IsEnabled = true;
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowResults(List<LyricCandidate> candidates, string status)
        {
            CandidateList.ItemsSource = candidates;
            StatusText.Text = status;
            PreviewText.Text = string.Empty;
            ConfirmButton.IsEnabled = false;
            if (candidates.Count > 0)
            {
                CandidateList.SelectedIndex = 0;
            }
        }

        private void CandidateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var candidate = CandidateList.SelectedItem as LyricCandidate;
            ConfirmButton.IsEnabled = candidate != null && !string.IsNullOrEmpty(candidate.LyricText);
            PreviewText.Text = candidate == null ? string.Empty : Preview(candidate.LyricText);
        }

        private static string Preview(string lyricText)
        {
            if (string.IsNullOrEmpty(lyricText)) return "(无歌词内容)";

            string text = Regex.Replace(lyricText, @"\[\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?\]", "");
            text = Regex.Replace(text, @"<\d{1,3}:\d{1,2}(?:\.\d{1,3})?>", "");

            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join("\n", lines);
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
            // 页面被替换时等同于取消，避免回调悬挂。
            Complete(null);
            base.OnNavigatedFrom(e);
        }

        private void Complete(LyricCandidate result)
        {
            if (_completionSent) return;
            _completionSent = true;

            var completion = _completion;
            _completion = null;
            completion?.TrySetResult(result);
            // 覆盖层由 LyricPickerService 在收到结果后隐藏，这里不做导航。
        }

        private async void UseLocal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary
                };
                foreach (string ext in new[] { ".lrc", ".krc", ".qrc", ".txt" })
                    picker.FileTypeFilter.Add(ext);

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                StatusText.Text = "正在读取本地歌词…";
                string text = await Windows.Storage.FileIO.ReadTextAsync(file);
                if (string.IsNullOrWhiteSpace(text))
                {
                    StatusText.Text = "文件内容为空";
                    return;
                }

                var candidate = new LyricCandidate
                {
                    Source = "本地",
                    SongId = file.Path,
                    Title = file.DisplayName,
                    Artist = _artist ?? string.Empty,
                    LyricText = text,
                    Format = LyricsParser.DetectFormat(text),
                    StrictMatch = true
                };
                Complete(candidate);
            }
            catch (Exception ex)
            {
                StatusText.Text = "读取失败：" + ex.Message;
            }
        }
    }

    /// <summary>导航参数。</summary>
    public sealed class LyricPickerArgs
    {
        public List<LyricCandidate> Candidates { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        public string Album { get; set; }
        public TaskCompletionSource<LyricCandidate> Completion { get; set; }
    }
}
