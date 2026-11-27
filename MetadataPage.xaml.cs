using HyperMedia_Lite.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia_Lite
{
    /// <summary>音乐元数据查看 / 编辑器：基于 MusicProperties，15063 起无需守卫。</summary>
    public sealed partial class MetadataPage : Page
    {
        private MediaItem _item;
        private MusicProperties _properties;
        private bool _isWindowActive = true;

        public MetadataPage()
        {
            InitializeComponent();
            try { Window.Current.SetTitleBar(AppTitleBar); } catch { }
            Window.Current.CoreWindow.Activated += CoreWindow_Activated;
            UpdateTitleAppearance();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Window.Current.CoreWindow.Activated -= CoreWindow_Activated;
            base.OnNavigatedFrom(e);
        }

        private void CoreWindow_Activated(CoreWindow sender, WindowActivatedEventArgs args)
        {
            _isWindowActive = args.WindowActivationState != CoreWindowActivationState.Deactivated;
            UpdateTitleAppearance();
        }

        private void UpdateTitleAppearance()
        {
            try
            {
                AppTitleText.Foreground = new SolidColorBrush(_isWindowActive ? Colors.Black : Colors.Gray);
            }
            catch { }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _item = e.Parameter as MediaItem;
            if (_item == null || _item.File == null)
            {
                StatusText.Text = "没有可用的音乐文件";
                SaveButton.IsEnabled = false;
                return;
            }

            try
            {
                _properties = await _item.File.Properties.GetMusicPropertiesAsync();
                FileLabel.Text = _item.File.Name;
                PathLabel.Text = _item.Path;
                InfoLabel.Text = $"时长 {FormatDuration(_properties.Duration)} · 码率 {_properties.Bitrate / 1000} kbps";

                TitleBox.Text = _properties.Title ?? string.Empty;
                ArtistBox.Text = _properties.Artist ?? string.Empty;
                AlbumBox.Text = _properties.Album ?? string.Empty;
                AlbumArtistBox.Text = _properties.AlbumArtist ?? string.Empty;
                SubtitleBox.Text = _properties.Subtitle ?? string.Empty;
                TrackBox.Text = _properties.TrackNumber > 0 ? _properties.TrackNumber.ToString() : string.Empty;
                YearBox.Text = _properties.Year > 0 ? _properties.Year.ToString() : string.Empty;
                RatingBox.Text = _properties.Rating.ToString();
                GenreBox.Text = string.Join(", ", _properties.Genre);
                ComposerBox.Text = string.Join(", ", _properties.Composers);
                PublisherBox.Text = _properties.Publisher ?? string.Empty;
                ConductorBox.Text = string.Join(", ", _properties.Conductors);
                ProducerBox.Text = string.Join(", ", _properties.Producers);

                if (_item.Thumbnail != null)
                {
                    CoverImage.Source = _item.Thumbnail;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "读取元数据失败：" + ex.Message;
                SaveButton.IsEnabled = false;
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_properties == null || _item?.File == null) return;

            SaveButton.IsEnabled = false;
            StatusText.Text = "正在保存…";
            try
            {
                _properties.Title = TitleBox.Text.Trim();
                _properties.Artist = ArtistBox.Text.Trim();
                _properties.Album = AlbumBox.Text.Trim();
                _properties.AlbumArtist = AlbumArtistBox.Text.Trim();
                _properties.Subtitle = SubtitleBox.Text.Trim();

                if (uint.TryParse(TrackBox.Text.Trim(), out uint track)) _properties.TrackNumber = track;
                if (uint.TryParse(YearBox.Text.Trim(), out uint year)) _properties.Year = year;
                if (uint.TryParse(RatingBox.Text.Trim(), out uint rating)) _properties.Rating = rating;

                _properties.Publisher = PublisherBox.Text.Trim();
                ReplaceList(_properties.Genre, GenreBox.Text);
                ReplaceList(_properties.Composers, ComposerBox.Text);
                ReplaceList(_properties.Conductors, ConductorBox.Text);
                ReplaceList(_properties.Producers, ProducerBox.Text);

                await _properties.SavePropertiesAsync();
                await _item.LoadPropertiesAsync();
                StatusText.Text = "已保存";
            }
            catch (Exception ex)
            {
                StatusText.Text = "保存失败：" + ex.Message;
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        private static void ReplaceList(IList<string> target, string text)
        {
            target.Clear();
            foreach (string part in text.Split(new[] { ',', '，', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = part.Trim();
                if (value.Length > 0) target.Add(value);
            }
        }

        private static string FormatDuration(TimeSpan value) =>
            value.TotalHours >= 1
                ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
                : $"{value.Minutes}:{value.Seconds:00}";

        private async void ChangeCover_Click(object sender, RoutedEventArgs e)
        {
            if (_item?.File == null) return;
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
                };
                foreach (string ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
                {
                    picker.FileTypeFilter.Add(ext);
                }

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                var bitmap = await Services.CoverOverrideService.SaveAsync(_item, file);
                CoverImage.Source = bitmap;
                await _item.LoadThumbnailAsync();
                StatusText.Text = "封面已替换（应用内生效，不会写回音频文件）";
            }
            catch (Exception ex)
            {
                StatusText.Text = "替换封面失败：" + ex.Message;
            }
        }

        private async void ResetCover_Click(object sender, RoutedEventArgs e)
        {
            if (_item?.File == null) return;
            try
            {
                await Services.CoverOverrideService.ClearAsync(_item);
                await _item.LoadThumbnailAsync();
                CoverImage.Source = _item.Thumbnail;
                StatusText.Text = "已恢复文件自带封面";
            }
            catch (Exception ex)
            {
                StatusText.Text = "恢复封面失败：" + ex.Message;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
