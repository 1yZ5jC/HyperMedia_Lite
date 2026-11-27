using HyperMedia_Lite.Helpers;
using HyperMedia_Lite.Services;
using HyperMedia_Lite.ViewModels;
using System;
using System.Collections.Generic;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 独立设置页：媒体库管理、访问权限、统计与关于。
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private readonly MainViewModel _viewModel = App.MainViewModel;
        private bool _isWindowActive = true;

        public SettingsPage()
        {
            InitializeComponent();
            DataContext = _viewModel;

            try { Window.Current.SetTitleBar(AppTitleBar); } catch { }
            UpdateTitleAppearance();
            Window.Current.CoreWindow.Activated += CoreWindow_Activated;

            SystemMediaControlsSwitch.IsOn = SystemMediaControlsService.IsEnabledByUser;

            CompatibilityText.Text = UwpCompat.HasModernPlayback
                ? "当前系统支持应用内视频播放（MediaPlayerElement / 播放会话）。"
                : "当前为 Windows 10 10240：已启用兼容模式，音频与视频均可应用内播放（视频使用经典 MediaElement 后端）。";

            string saved = ThemeService.CurrentTheme.ToString();
            foreach (var item in ThemeCombo.Items)
            {
                if (item is ComboBoxItem comboItem && comboItem.Tag is string tag && tag == saved)
                {
                    ThemeCombo.SelectedItem = comboItem;
                    break;
                }
            }

            string accentSaved = ThemeService.CurrentAccentSource.ToString();
            foreach (var item in AccentSourceCombo.Items)
            {
                if (item is ComboBoxItem comboItem && comboItem.Tag is string tag && tag == accentSaved)
                {
                    AccentSourceCombo.SelectedItem = comboItem;
                    break;
                }
            }

            string mode = TileService.Mode.ToString();
            foreach (var item in TileModeCombo.Items)
            {
                if (item is ComboBoxItem comboItem && comboItem.Tag is string tag && tag == mode)
                {
                    TileModeCombo.SelectedItem = comboItem;
                    break;
                }
            }

            string interval = TileService.RotationInterval.ToString();
            foreach (var item in TileIntervalCombo.Items)
            {
                if (item is ComboBoxItem comboItem && comboItem.Tag is string tag && tag == interval)
                {
                    TileIntervalCombo.SelectedItem = comboItem;
                    break;
                }
            }
        }

        private void TileModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TileModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out TileMode mode))
            {
                _viewModel.SetTileModeCommand.Execute(mode);
            }
        }

        private void TileIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TileIntervalCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && int.TryParse(tag, out int seconds))
            {
                TileService.RotationInterval = seconds;
            }
        }

        private void AccentSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccentSourceCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out AccentSource source))
            {
                ThemeService.SetAccentSource(source);
            }
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out AppTheme theme))
            {
                ThemeService.SetTheme(theme);
                UpdateTitleAppearance();
            }
        }

        private void SyncAccentSourceCombo()
        {
            string source = ThemeService.CurrentAccentSource.ToString();
            foreach (var item in AccentSourceCombo.Items)
            {
                if (item is ComboBoxItem comboItem && comboItem.Tag is string tag && tag == source)
                {
                    AccentSourceCombo.SelectedItem = comboItem;
                    break;
                }
            }
        }

        private void AccentDefault_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.SetAccent(null);
            SyncAccentSourceCombo();
        }

        private void Accent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string hex && hex.Length == 8)
            {
                Color color = Color.FromArgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16),
                    Convert.ToByte(hex.Substring(6, 2), 16));
                ThemeService.SetAccent(color);
                SyncAccentSourceCombo();
            }
        }

        private async void SystemMediaControlsSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            SystemMediaControlsService.IsEnabledByUser = SystemMediaControlsSwitch.IsOn;
            if (!SystemMediaControlsSwitch.IsOn)
            {
                await SystemMediaControlsService.Current.UpdateAsync(null, false);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Window.Current.CoreWindow.Activated -= CoreWindow_Activated;
            base.OnNavigatedFrom(e);
        }

        private void CoreWindow_Activated(CoreWindow sender, WindowActivatedEventArgs args)
        {
            bool active = args.WindowActivationState != CoreWindowActivationState.Deactivated;
            if (active == _isWindowActive) return;
            _isWindowActive = active;
            UpdateTitleAppearance();
        }

        private void UpdateTitleAppearance()
        {
            try
            {
                AppTitleText.Foreground = new SolidColorBrush(_isWindowActive
                    ? (ThemeService.IsDarkMode ? Colors.White : Colors.Black)
                    : Colors.Gray);
            }
            catch { }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void ClearAccess_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearSavedFiles();
        }

        private async void ExportLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string content = await DebugLog.ReadAsync();
                if (string.IsNullOrEmpty(content))
                {
                    LogStatusText.Text = "日志为空";
                    return;
                }

                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = "hypermedia-log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                };
                picker.FileTypeChoices.Add("日志文件", new List<string> { ".log" });

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await FileIO.WriteTextAsync(file, content);
                    LogStatusText.Text = "已导出到 " + file.Name;
                }
            }
            catch (Exception ex)
            {
                LogStatusText.Text = "导出失败：" + ex.Message;
            }
        }

        private void StartPhotoTile_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.StartPhotoTileCommand.Execute(null);
        }

        private void TestTile_Click(object sender, RoutedEventArgs e)
        {
            TileService.PushTestTile();
            LogStatusText.Text = "已推送测试磁贴，请到开始菜单查看";
        }

        private async void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            await DebugLog.ClearAsync();
            LogStatusText.Text = "日志已清理";
        }
    }
}
