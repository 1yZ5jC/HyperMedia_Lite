using System;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace HyperMedia_Lite.Services
{
    public enum AppTheme
    {
        System,
        Light,
        Dark
    }

    /// <summary>强调色来源，与明暗主题（跟随系统 / 浅色 / 深色）相互独立。</summary>
    public enum AccentSource
    {
        System,   // 系统强调色
        Cover,    // 跟随当前歌曲封面 / 视频帧取色
        Manual    // 用户在设置页手动选择
    }

    /// <summary>
    /// 主题与强调色管理：默认跟随系统，用户可在设置页覆盖。
    /// 通过直接修改 App.xaml 中的画刷颜色，使所有 StaticResource 引用实时更新。
    /// </summary>
    public static class ThemeService
    {
        private const string ThemeKey = "AppTheme";
        private const string AccentKey = "CustomAccent";
        private const string AccentSourceKey = "AccentSource";

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.System;

        /// <summary>当前实际是否为深色（含跟随系统）。</summary>
        public static bool IsDarkMode =>
            CurrentTheme == AppTheme.Dark
            || (CurrentTheme == AppTheme.System && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        public static AccentSource CurrentAccentSource { get; private set; } = AccentSource.System;

        public static Color? CustomAccent { get; private set; }

        private static Color? _dynamicAccent;

        /// <summary>
        /// 应用封面取色（不持久化）。仅在强调色来源为 Cover 时生效；
        /// 但播放页背景始终可用它（背景跟随封面是独立行为）。
        /// </summary>
        public static void SetDynamicAccent(Color? color)
        {
            _dynamicAccent = color;
            Apply();
        }

        public static void SetAccentSource(AccentSource source)
        {
            CurrentAccentSource = source;
            ApplicationData.Current.LocalSettings.Values[AccentSourceKey] = source.ToString();
            Apply();
        }

        public static void Load()
        {
            var values = ApplicationData.Current.LocalSettings.Values;

            if (values.TryGetValue(ThemeKey, out object themeRaw) && themeRaw is string themeText)
            {
                if (Enum.TryParse(themeText, out AppTheme parsed))
                {
                    CurrentTheme = parsed;
                }
            }

            if (values.TryGetValue(AccentSourceKey, out object sourceRaw) && sourceRaw is string sourceText
                && Enum.TryParse(sourceText, out AccentSource parsedSource))
            {
                CurrentAccentSource = parsedSource;
            }

            if (values.TryGetValue(AccentKey, out object accentRaw) && accentRaw is string hex
                && TryParseHex(hex, out Color color))
            {
                CustomAccent = color;
            }

            Apply();
        }

        public static void SetTheme(AppTheme theme)
        {
            CurrentTheme = theme;
            ApplicationData.Current.LocalSettings.Values[ThemeKey] = theme.ToString();
            Apply();
        }

        public static void SetAccent(Color? color)
        {
            CustomAccent = color;
            var values = ApplicationData.Current.LocalSettings.Values;
            if (color.HasValue)
            {
                values[AccentKey] = ToHex(color.Value);
                // 手动选色即切换到 Manual 来源，避免被封面色覆盖。
                CurrentAccentSource = AccentSource.Manual;
                values[AccentSourceKey] = AccentSource.Manual.ToString();
            }
            else
            {
                values.Remove(AccentKey);
                CurrentAccentSource = AccentSource.System;
                values[AccentSourceKey] = AccentSource.System.ToString();
            }
            Apply();
        }

        public static void Apply()
        {
            if (Window.Current?.Content is FrameworkElement root)
            {
                root.RequestedTheme = CurrentTheme == AppTheme.System ? ElementTheme.Default
                    : CurrentTheme == AppTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
            }

            bool dark = CurrentTheme == AppTheme.Dark
                || (CurrentTheme == AppTheme.System && Application.Current.RequestedTheme == ApplicationTheme.Dark);

            Color accent;
            switch (CurrentAccentSource)
            {
                case AccentSource.Cover:
                    accent = _dynamicAccent ?? GetSystemAccent();
                    break;
                case AccentSource.Manual:
                    accent = CustomAccent ?? GetSystemAccent();
                    break;
                default:
                    accent = GetSystemAccent();
                    break;
            }
            Color accentPressed = Darken(accent, 0.82);

            SetColor("AccentBrush", accent);
            SetColor("AccentPressedBrush", accentPressed);

            // 音频播放页背景只跟随封面取色；没有封面色时回落为中性灰。
            Color backdrop = _dynamicAccent.HasValue
                ? Darken(_dynamicAccent.Value, 0.30)
                : Hex(0x242424);
            SetColor("NowPlayingBackdropBrush", backdrop);
            SetColor("SurfaceBrush", dark ? Hex(0x1F1F1F) : Hex(0xFFFFFF));
            SetColor("NavBackgroundBrush", dark ? Hex(0x252525) : Hex(0xF7F7F7));
            SetColor("NavForegroundBrush", dark ? Hex(0xB0B0B0) : Hex(0x6B6B6B));
            SetColor("PageBackgroundBrush", dark ? Hex(0x000000) : Hex(0xF1F1F1));
            SetColor("TextPrimaryBrush", dark ? Hex(0xFFFFFF) : Hex(0x1A1A1A));
            SetColor("TextSecondaryBrush", dark ? Hex(0xB0B0B0) : Hex(0x767676));

            // 填充条、轨道与指示器使用三种明显不同的颜色，保证拨杆始终可辨。
            bool lightAccent = IsLight(accent);
            Color sliderThumb = lightAccent ? Hex(0x1A1A1A) : Hex(0xFFFFFF);
            Color sliderBorder = lightAccent ? Hex(0xFFFFFF) : Hex(0x1A1A1A);
            Color sliderFill = lightAccent ? Hex(0xFFFFFF) : Color.FromArgb(0xCC, 0x00, 0x00, 0x00);
            Color sliderTrack = lightAccent ? Color.FromArgb(0x66, 0x1A, 0x1A, 0x1A)
                                            : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
            SetColor("SliderThumbBrush", sliderThumb);
            SetColor("SliderThumbBackground", sliderThumb);
            SetColor("SliderThumbBackgroundPointerOver", sliderThumb);
            SetColor("SliderThumbBackgroundPressed", sliderThumb);
            SetColor("SliderThumbBorderBrush", sliderBorder);
            SetColor("SliderThumbBackgroundDisabled", sliderThumb);
            SetColor("SliderFillBrush", sliderFill);
            SetColor("SliderTrackBrush", sliderTrack);
            SetColor("SliderTrackFill", sliderTrack);
            SetColor("SliderTrackFillPointerOver", sliderTrack);
            SetColor("SliderTrackFillPressed", sliderTrack);
            SetColor("SliderTrackFillDisabled", sliderTrack);
            SetColor("SliderTrackValueFill", sliderFill);
            SetColor("SliderTrackValueFillPointerOver", sliderFill);
            SetColor("SliderTrackValueFillPressed", sliderFill);
            SetColor("SliderTrackValueFillDisabled", sliderFill);

            App.MainViewModel.RefreshTheme();
        }

        private static Color GetSystemAccent()
        {
            try
            {
                return new UISettings().GetColorValue(UIColorType.Accent);
            }
            catch (Exception)
            {
                return Hex(0x0089D0);
            }
        }

        private static void SetColor(string key, Color color)
        {
            if (Application.Current.Resources[key] is Windows.UI.Xaml.Media.SolidColorBrush brush)
            {
                brush.Color = color;
            }
        }

        private static bool IsLight(Color color)
        {
            // 相对亮度近似值，用于决定滑块使用深色还是白色指示器。
            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            return luminance > 0.6;
        }

        private static Color Darken(Color color, double factor)
        {
            return Color.FromArgb(color.A,
                (byte)(color.R * factor),
                (byte)(color.G * factor),
                (byte)(color.B * factor));
        }

        private static Color Hex(uint rgb)
        {
            return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        private static string ToHex(Color color)
        {
            return color.A.ToString("X2") + color.R.ToString("X2")
                + color.G.ToString("X2") + color.B.ToString("X2");
        }

        private static bool TryParseHex(string hex, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrEmpty(hex) || hex.Length != 8)
            {
                return false;
            }
            try
            {
                color = Color.FromArgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16),
                    Convert.ToByte(hex.Substring(6, 2), 16));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
