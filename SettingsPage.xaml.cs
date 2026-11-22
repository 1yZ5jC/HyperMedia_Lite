using HyperMedia_Lite.Helpers;
using HyperMedia_Lite.Services;
using HyperMedia_Lite.ViewModels;
using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 独立设置页：媒体库管理、访问权限、统计与关于。
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private readonly MainViewModel _viewModel = App.MainViewModel;

        public SettingsPage()
        {
            InitializeComponent();
            DataContext = _viewModel;

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
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
                && Enum.TryParse(tag, out AppTheme theme))
            {
                ThemeService.SetTheme(theme);
            }
        }

        private void AccentDefault_Click(object sender, RoutedEventArgs e)
        {
            ThemeService.SetAccent(null);
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
            }
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
    }
}
