using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;

namespace HyperMedia_Lite.Helpers
{
    /// <summary>
    /// bool -> Visibility。传入 ConverterParameter="Invert" 可反转。
    /// </summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool visible = value is bool b && b;
            if (parameter is string p && p == "Invert")
            {
                visible = !visible;
            }
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// bool -> 强调色 / 普通前景画刷（取自 App.xaml 资源）。
    /// </summary>
    public sealed class BoolToAccentBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var resources = Application.Current.Resources;
            var key = (value is bool b && b) ? "AccentBrush" : "NavForegroundBrush";
            return resources[key] as Brush ?? new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// bool -> 主/次文字画刷（用于选项卡选中态）。
    /// </summary>
    public sealed class BoolToTextBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var resources = Application.Current.Resources;
            var key = (value is bool b && b) ? "TextPrimaryBrush" : "TextSecondaryBrush";
            return resources[key] as Brush ?? new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>"#RRGGBB" / "#AARRGGBB" -> SolidColorBrush（字幕颜色）。</summary>
    public sealed class HexColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            try
            {
                string hex = (value as string ?? "").Trim().TrimStart('#');
                if (hex.Length == 6) hex = "FF" + hex;
                if (hex.Length == 8)
                {
                    byte a = System.Convert.ToByte(hex.Substring(0, 2), 16);
                    byte r = System.Convert.ToByte(hex.Substring(2, 2), 16);
                    byte g = System.Convert.ToByte(hex.Substring(4, 2), 16);
                    byte b = System.Convert.ToByte(hex.Substring(6, 2), 16);
                    return new SolidColorBrush(Color.FromArgb(a, r, g, b));
                }
            }
            catch { }
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// bool -> Opacity（用于随机 / 循环的激活态）。
    /// </summary>
    public sealed class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool b && b) ? 1.0 : 0.45;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 非空字符串 -> Visibility（用于可选角标 / 副标题）。
    /// </summary>
    public sealed class TextToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool hasText = value is string s && !string.IsNullOrEmpty(s);
            if (parameter is string p && p == "Invert")
            {
                hasText = !hasText;
            }
            return hasText ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
