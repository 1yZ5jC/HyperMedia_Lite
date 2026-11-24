using System;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyperMedia_Lite.Controls
{
    /// <summary>
    /// 紧凑自动换行面板：字块从左到右紧密排列，超出可用宽度时换到下一行。
    /// 用于逐字歌词，避免出现 ItemsWrapGrid 的均匀拉伸空隙。
    /// </summary>
    public sealed class WordWrapPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            double maxWidth = 0;
            double totalHeight = 0;
            double rowWidth = 0;
            double rowHeight = 0;
            double limit = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;

            foreach (UIElement child in Children)
            {
                child.Measure(new Size(limit, availableSize.Height));
                Size desired = child.DesiredSize;

                if (rowWidth > 0 && rowWidth + desired.Width > limit)
                {
                    maxWidth = Math.Max(maxWidth, rowWidth);
                    totalHeight += rowHeight;
                    rowWidth = 0;
                    rowHeight = 0;
                }

                rowWidth += desired.Width;
                rowHeight = Math.Max(rowHeight, desired.Height);
            }

            maxWidth = Math.Max(maxWidth, rowWidth);
            totalHeight += rowHeight;

            if (double.IsInfinity(availableSize.Width))
            {
                return new Size(maxWidth, totalHeight);
            }
            return new Size(Math.Min(maxWidth, availableSize.Width), totalHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double x = 0;
            double y = 0;
            double rowHeight = 0;

            foreach (UIElement child in Children)
            {
                Size desired = child.DesiredSize;

                if (x > 0 && x + desired.Width > finalSize.Width)
                {
                    x = 0;
                    y += rowHeight;
                    rowHeight = 0;
                }

                child.Arrange(new Rect(x, y, desired.Width, desired.Height));
                x += desired.Width;
                rowHeight = Math.Max(rowHeight, desired.Height);
            }

            return finalSize;
        }
    }
}
