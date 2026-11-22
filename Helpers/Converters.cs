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
