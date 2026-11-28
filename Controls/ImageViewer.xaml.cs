using HyperMedia_Lite.Helpers;
using HyperMedia_Lite.Services;
using System;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperMedia_Lite.Controls
{
    /// <summary>
    /// 图片查看控件（参考 HyperViewer 的 ImageViewer）：
    /// ScrollViewer 渲染级缩放 + 双击/Ctrl+滚轮/捏合 + 拖动平移 + 适应窗口 +
    /// 旋转/翻转 + 换图过渡动画 + 缩放百分比提示 + 低版本自适应。
    /// </summary>
    public sealed partial class ImageViewer : UserControl
    {
        private const float DoubleTapZoomTarget = 2.0f;
        private const float WheelZoomStep = 1.15f;

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(nameof(Source), typeof(BitmapImage),
                typeof(ImageViewer), new PropertyMetadata(null, OnSourceChanged));

        public static readonly DependencyProperty IsPlaceholderProperty =
            DependencyProperty.Register(nameof(IsPlaceholder), typeof(bool),
                typeof(ImageViewer), new PropertyMetadata(false));

        public static readonly DependencyProperty ImageRotationProperty =
            DependencyProperty.Register(nameof(ImageRotation), typeof(double),
                typeof(ImageViewer), new PropertyMetadata(0.0, OnTransformChanged));

        public static readonly DependencyProperty FlipHProperty =
            DependencyProperty.Register(nameof(FlipH), typeof(int),
                typeof(ImageViewer), new PropertyMetadata(1, OnTransformChanged));

        public static readonly DependencyProperty FlipVProperty =
            DependencyProperty.Register(nameof(FlipV), typeof(int),
                typeof(ImageViewer), new PropertyMetadata(1, OnTransformChanged));

        public BitmapImage Source
        {
            get => (BitmapImage)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public bool IsPlaceholder
        {
            get => (bool)GetValue(IsPlaceholderProperty);
            set => SetValue(IsPlaceholderProperty, value);
        }

        public double ImageRotation
        {
            get => (double)GetValue(ImageRotationProperty);
            set => SetValue(ImageRotationProperty, value);
        }

        public int FlipH
        {
            get => (int)GetValue(FlipHProperty);
            set => SetValue(FlipHProperty, value);
        }

        public int FlipV
        {
            get => (int)GetValue(FlipVProperty);
            set => SetValue(FlipVProperty, value);
        }

        public event EventHandler ZoomFactorChanged;
        public event EventHandler ImageChanged;
        public event EventHandler DoubleTappedOccurred;
        /// <summary>图片处于适应窗口状态时向左滑动，请求显示下一张。</summary>
        public event EventHandler SwipeNextRequested;
        /// <summary>图片处于适应窗口状态时向右滑动，请求显示上一张。</summary>
        public event EventHandler SwipePreviousRequested;

        public float CurrentZoomFactor => Scroller.ZoomFactor;

        public bool IsAtFitZoom => _lastFitZoom > 0 && Math.Abs(Scroller.ZoomFactor - _lastFitZoom) < 0.01f;

        private bool _suppressViewChange;
        private float _lastFitZoom = -1f;
        private double _currentRotation;
        private readonly CompositeTransform _imageTransform = new CompositeTransform();
        private readonly ScaleTransform _transitionScale = new ScaleTransform();
        private readonly TranslateTransform _transitionTranslate = new TranslateTransform();
        private readonly TransformGroup _transitionGroup = new TransformGroup();

        private Storyboard _fadeStoryboard;
        private Storyboard _rotationStoryboard;
        private bool _pendingFit;
        private bool _pendingFadeIn;
        private readonly DispatcherTimer _fitRetryTimer;
        private readonly DispatcherTimer _fadeInTimer;
        private DispatcherTimer _zoomBadgeTimer;
        private float _lastBadgeZoom = -1f;
        private readonly Storyboard _zoomBadgeFade = new Storyboard();

        // 鼠标拖动平移放大后的图片。
        private int _panPointerId = -1;
        private Point _panStart;
        private double _panStartX;
        private double _panStartY;
        private bool _panMoved;

        public ImageViewer()
        {
            InitializeComponent();

            _transitionGroup.Children.Add(_imageTransform);
            _transitionGroup.Children.Add(_transitionScale);
            _transitionGroup.Children.Add(_transitionTranslate);
            TheImage.RenderTransform = _transitionGroup;
            TheImage.RenderTransformOrigin = new Point(0.5, 0.5);

            _fitRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _fitRetryTimer.Tick += OnFitRetryTick;
            _fadeInTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _fadeInTimer.Tick += OnFadeInTimerTick;

            _zoomBadgeFade.Completed += (s, args) =>
            {
                if (ZoomBadge.Opacity <= 0.01)
                {
                    ZoomBadge.Visibility = Visibility.Collapsed;
                }
            };
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is ImageViewer viewer))
            {
                return;
            }

            viewer._rotationStoryboard?.Stop();
            viewer._imageTransform.Rotation = viewer.ImageRotation;
            viewer._currentRotation = viewer.ImageRotation;
            viewer.TheImage.Source = e.NewValue as BitmapImage;

            if (viewer.IsPlaceholder)
            {
                // 低清占位：不隐藏、不淡入，高清到达后再走正常流程，避免两次缩放跳变。
                viewer._pendingFit = true;
                viewer._pendingFadeIn = false;
                viewer._fadeInTimer.Stop();
                viewer._lastFitZoom = -1f;
                viewer.ImageChanged?.Invoke(viewer, EventArgs.Empty);
                viewer._fitRetryTimer.Stop();
                viewer._fitRetryTimer.Start();
                return;
            }

            if (!UwpCompat.HasContractV2)
            {
                // 10240：ChangeView 三参动画不可关闭，先隐藏并在动画结束后淡入。
                viewer.TheImage.Opacity = 0;
                viewer._pendingFadeIn = false;
                viewer._fadeInTimer.Stop();
                viewer.LoadingRing.Visibility = Visibility.Visible;
                viewer.LoadingRing.IsActive = true;
            }

            viewer._pendingFit = true;
            viewer._lastFitZoom = -1f;
            viewer.ImageChanged?.Invoke(viewer, EventArgs.Empty);
            viewer._fitRetryTimer.Stop();
            viewer._fitRetryTimer.Start();
        }

        private static void OnTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer viewer)
            {
                viewer.ApplyTransform();
            }
        }

        private void ApplyTransform()
        {
            _imageTransform.ScaleX = FlipH;
            _imageTransform.ScaleY = FlipV;
            _rotationStoryboard?.Stop();

            var animation = new DoubleAnimation
            {
                From = _currentRotation,
                To = ImageRotation,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            Storyboard.SetTarget(animation, _imageTransform);
            Storyboard.SetTargetProperty(animation, "Rotation");

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            _rotationStoryboard = storyboard;
            _currentRotation = ImageRotation;
            storyboard.Begin();
        }

        private void TheImage_ImageOpened(object sender, RoutedEventArgs e)
        {
            _pendingFit = true;
        }

        private void TheImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_pendingFit && TryFit())
            {
                _pendingFit = false;
                if (!UwpCompat.HasContractV2)
                {
                    RequestFadeIn();
                }
            }
        }

        private void OnFitRetryTick(object sender, object e)
        {
            if (!_pendingFit)
            {
                _fitRetryTimer.Stop();
                return;
            }

            if (TryFit())
            {
                _pendingFit = false;
                _fitRetryTimer.Stop();
                if (!UwpCompat.HasContractV2)
                {
                    RequestFadeIn();
                }
            }
        }

        private void RequestFadeIn()
        {
            _pendingFadeIn = true;
            _fadeInTimer.Stop();
            _fadeInTimer.Start();
        }

        private void OnFadeInTimerTick(object sender, object e)
        {
            _fadeInTimer.Stop();
            if (_pendingFadeIn)
            {
                _pendingFadeIn = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                LoadingRing.IsActive = false;
                FadeIn();
            }
        }

        /// <summary>换图过渡动画：Fade / Zoom / Pan / Flicker。</summary>
        public void FadeIn(string transition = null)
        {
            string kind = string.IsNullOrEmpty(transition)
                ? PhotoViewerSettings.Transition : transition;

            _fadeStoryboard?.Stop();
            var storyboard = new Storyboard();

            void Add(string property, double from, double to, double milliseconds, DependencyObject target = null)
            {
                var animation = new DoubleAnimation
                {
                    From = from,
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(milliseconds)
                };
                Storyboard.SetTarget(animation, target ?? (DependencyObject)TheImage);
                Storyboard.SetTargetProperty(animation, property);
                storyboard.Children.Add(animation);
            }

            switch (kind)
            {
                case "Zoom":
                    _transitionScale.ScaleX = _transitionScale.ScaleY = 1.08;
                    Add("Opacity", 0, 1, 220);
                    Add("ScaleX", 1.08, 1, 220, _transitionScale);
                    Add("ScaleY", 1.08, 1, 220, _transitionScale);
                    break;
                case "Pan":
                    _transitionTranslate.X = 48;
                    Add("Opacity", 0, 1, 220);
                    Add("X", 48, 0, 220, _transitionTranslate);
                    break;
                case "Flicker":
                    _transitionScale.ScaleX = _transitionScale.ScaleY = 1.02;
                    Add("Opacity", 0, 0.3, 80);
                    Add("Opacity", 0.3, 1, 135);
                    Add("ScaleX", 1.02, 1, 235, _transitionScale);
                    Add("ScaleY", 1.02, 1, 235, _transitionScale);
                    break;
                default:
                    Add("Opacity", 0, 1, 160);
                    break;
            }

            _fadeStoryboard = storyboard;
            _fadeStoryboard.Begin();
        }

        private bool TryFit()
        {
            if (!(TheImage.Source is BitmapImage bitmap)
                || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                return false;
            }
            if (TheImage.ActualWidth <= 0 || TheImage.ActualHeight <= 0)
            {
                return false;
            }
            if (Scroller.ViewportWidth <= 0 || Scroller.ViewportHeight <= 0)
            {
                return false;
            }

            FitToWindow();
            return true;
        }

        /// <summary>适应窗口：按布局尺寸（含旋转）计算缩放，整图恰好放入视口。</summary>
        public void FitToWindow()
        {
            if (!(TheImage.Source is BitmapImage bitmap)
                || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                return;
            }

            double width = TheImage.ActualWidth;
            double height = TheImage.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            double rotation = ImageRotation % 360;
            if (rotation < 0)
            {
                rotation += 360;
            }
            if (Math.Abs(rotation - 90) < 0.5 || Math.Abs(rotation - 270) < 0.5)
            {
                double swap = width;
                width = height;
                height = swap;
            }

            double viewportWidth = Scroller.ViewportWidth;
            double viewportHeight = Scroller.ViewportHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                return;
            }

            float fit = (float)Math.Min(viewportWidth / width, viewportHeight / height);
            fit = Math.Max(Scroller.MinZoomFactor, Math.Min(Scroller.MaxZoomFactor, fit));
            _lastFitZoom = fit;

            ChangeView(fit);
            LoadingRing.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = false;
            TheImage.Opacity = UwpCompat.HasContractV2 ? 1 : TheImage.Opacity;
        }

        private void ChangeView(float zoom, double? offsetX = null, double? offsetY = null)
        {
            _suppressViewChange = true;
            if (UwpCompat.HasContractV2)
            {
                Scroller.ChangeView(offsetX, offsetY, zoom, true);
            }
            else
            {
                Scroller.ChangeView(offsetX, offsetY, zoom);
            }
            _suppressViewChange = false;
            ZoomFactorChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ResetView()
        {
            _lastFitZoom = -1f;
            _pendingFit = true;
            _fitRetryTimer.Stop();
            _fitRetryTimer.Start();
            TryFit();
        }

        public void SetZoomFactor(float zoom)
        {
            float clamped = Math.Max(Scroller.MinZoomFactor, Math.Min(Scroller.MaxZoomFactor, zoom));
            ChangeView(clamped);
            ShowZoomBadge();
        }

        private void Scroller_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Point position = e.GetPosition(Scroller);
            float target = IsAtFitZoom || _lastFitZoom <= 0
                ? DoubleTapZoomTarget
                : _lastFitZoom;
            ZoomAt(position, target / Scroller.ZoomFactor);
            e.Handled = true;
            DoubleTappedOccurred?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 触摸滑动 / 鼠标拖动翻页：仅在适应窗口时触发，放大后拖动仍然用于平移画面。
        /// </summary>
        private void Scroller_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (!IsAtFitZoom || _lastFitZoom <= 0)
            {
                return;
            }

            double dx = e.Cumulative.Translation.X;
            double dy = e.Cumulative.Translation.Y;
            // UWP 的速度字段叫 Linear（Point，DIP/毫秒），Cumulative / Velocities 都是结构体。
            double vx = e.Velocities.Linear.X;
            bool horizontal = Math.Abs(dx) >= Math.Abs(dy) * 1.3;
            bool dragged = Math.Abs(dx) > 70 && horizontal;
            bool flicked = Math.Abs(vx) > 1.4 && Math.Abs(dx) > 20 && horizontal;

            if (!dragged && !flicked)
            {
                return;
            }

            if (dx < 0)
            {
                SwipeNextRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SwipePreviousRequested?.Invoke(this, EventArgs.Empty);
            }
            e.Handled = true;
        }

        private void Scroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(Scroller);
            if (point.PointerDevice.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse)
            {
                return;
            }

            int delta = point.Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            if (PhotoViewerSettings.MouseWheelMode == "Scroll")
            {
                // 滚轮平移画面；按住 Shift 改为横向平移。
                bool shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift) == CoreVirtualKeyStates.Down;
                double amount = delta > 0 ? -64 : 64;
                if (shift)
                {
                    double maxX = Math.Max(0, Scroller.ExtentWidth - Scroller.ViewportWidth);
                    ChangeView(Scroller.ZoomFactor,
                        Clamp(Scroller.HorizontalOffset + amount, 0, maxX), null);
                }
                else
                {
                    double maxY = Math.Max(0, Scroller.ExtentHeight - Scroller.ViewportHeight);
                    ChangeView(Scroller.ZoomFactor, null,
                        Clamp(Scroller.VerticalOffset + amount, 0, maxY));
                }
                e.Handled = true;
                return;
            }

            // 滚轮缩放；按住 Ctrl 时步长更大，方便快速放大。
            var control = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
            bool fast = control == CoreVirtualKeyStates.Down;
            float step = fast ? WheelZoomStep * WheelZoomStep : WheelZoomStep;
            Point position = point.Position;
            ZoomAt(position, delta > 0 ? step : 1f / step);
            e.Handled = true;
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private bool IsMousePointer(PointerRoutedEventArgs e)
        {
            return e.GetCurrentPoint(Scroller).PointerDevice.PointerDeviceType
                == Windows.Devices.Input.PointerDeviceType.Mouse;
        }

        private void Scroller_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // 适应窗口时没有可平移的内容，保留双击缩放；放大后才接管鼠标拖动。
            if (!IsMousePointer(e) || IsAtFitZoom)
            {
                return;
            }
            var point = e.GetCurrentPoint(Scroller);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _panPointerId = e.Pointer.PointerId;
            _panStart = point.Position;
            _panStartX = Scroller.HorizontalOffset;
            _panStartY = Scroller.VerticalOffset;
            _panMoved = false;
            Scroller.CapturePointer(e.Pointer);
        }

        private void Scroller_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_panPointerId != e.Pointer.PointerId)
            {
                return;
            }

            Point current = e.GetCurrentPoint(Scroller).Position;
            double dx = current.X - _panStart.X;
            double dy = current.Y - _panStart.Y;
            if (!_panMoved && Math.Abs(dx) + Math.Abs(dy) < 3)
            {
                return;
            }
            _panMoved = true;

            double maxX = Math.Max(0, Scroller.ExtentWidth - Scroller.ViewportWidth);
            double maxY = Math.Max(0, Scroller.ExtentHeight - Scroller.ViewportHeight);
            ChangeView(Scroller.ZoomFactor,
                Clamp(_panStartX - dx, 0, maxX),
                Clamp(_panStartY - dy, 0, maxY));
            e.Handled = true;
        }

        private void Scroller_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_panPointerId != e.Pointer.PointerId)
            {
                return;
            }
            _panPointerId = -1;
            if (Scroller.HasPointerCapture(e.Pointer.PointerId))
            {
                Scroller.ReleasePointerCapture(e.Pointer);
            }
        }

        public void ZoomAt(Point center, float factor)
        {
            float oldZoom = Scroller.ZoomFactor;
            float zoom = Math.Max(Scroller.MinZoomFactor,
                Math.Min(Scroller.MaxZoomFactor, oldZoom * factor));
            if (Math.Abs(zoom - oldZoom) < 0.0001f || oldZoom <= 0)
            {
                return;
            }

            double viewportW = Scroller.ViewportWidth;
            double viewportH = Scroller.ViewportHeight;
            double imageW = TheImage.ActualWidth;
            double imageH = TheImage.ActualHeight;

            // 指针下的图片像素在缩放后保持不动。
            double leftOld = (viewportW - imageW * oldZoom) / 2 - Scroller.HorizontalOffset;
            double topOld = (viewportH - imageH * oldZoom) / 2 - Scroller.VerticalOffset;
            double relX = (center.X - leftOld) / oldZoom;
            double relY = (center.Y - topOld) / oldZoom;

            double newW = imageW * zoom;
            double newH = imageH * zoom;
            double leftNew = center.X - relX * zoom;
            double topNew = center.Y - relY * zoom;
            double offsetX = (viewportW - newW) / 2 - leftNew;
            double offsetY = (viewportH - newH) / 2 - topNew;

            offsetX = Math.Max(0, Math.Min(offsetX, Math.Max(0, newW - viewportW)));
            offsetY = Math.Max(0, Math.Min(offsetY, Math.Max(0, newH - viewportH)));
            ChangeView(zoom, offsetX, offsetY);
            ShowZoomBadge();
        }

        private void Scroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            float zoom = Scroller.ZoomFactor;
            if ((e.IsIntermediate || !IsAtFitZoom) && Math.Abs(zoom - _lastBadgeZoom) > 0.005f)
            {
                _lastBadgeZoom = zoom;
                ShowZoomBadge();
            }

            if (_pendingFadeIn && !e.IsIntermediate)
            {
                _pendingFadeIn = false;
                _fadeInTimer.Stop();
                LoadingRing.Visibility = Visibility.Collapsed;
                LoadingRing.IsActive = false;
                FadeIn();
            }
            else if (_pendingFadeIn && e.IsIntermediate)
            {
                _fadeInTimer.Stop();
                _fadeInTimer.Start();
            }

            if (!_suppressViewChange && !e.IsIntermediate)
            {
                ZoomFactorChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnBadgeTimerTick(object sender, object e)
        {
            _zoomBadgeTimer?.Stop();
            _zoomBadgeFade.Stop();
            _zoomBadgeFade.Children.Clear();
            var animation = new DoubleAnimation
            {
                From = ZoomBadge.Opacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(250)
            };
            Storyboard.SetTarget(animation, ZoomBadge);
            Storyboard.SetTargetProperty(animation, "Opacity");
            _zoomBadgeFade.Children.Add(animation);
            _zoomBadgeFade.Begin();
        }

        private void ShowZoomBadge()
        {
            if (_zoomBadgeTimer == null)
            {
                _zoomBadgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                _zoomBadgeTimer.Tick += OnBadgeTimerTick;
            }

            ZoomText.Text = string.Format("{0:0}%", Scroller.ZoomFactor * 100);
            ZoomBadge.Visibility = Visibility.Visible;
            ZoomBadge.Opacity = 1;
            _zoomBadgeFade.Stop();
            _zoomBadgeTimer.Stop();
            _zoomBadgeTimer.Start();
        }
    }
}
