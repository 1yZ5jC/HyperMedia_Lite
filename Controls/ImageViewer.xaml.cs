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
            float target = Math.Abs(Scroller.ZoomFactor - 1f) < 0.02f ? DoubleTapZoomTarget : 1f;
            float clamped = Math.Max(Scroller.MinZoomFactor, Math.Min(Scroller.MaxZoomFactor, target));
            double offsetX = position.X * clamped - Math.Max(0, (Scroller.ViewportWidth - TheImage.ActualWidth * clamped) / 2);
            double offsetY = position.Y * clamped - Math.Max(0, (Scroller.ViewportHeight - TheImage.ActualHeight * clamped) / 2);
            ChangeView(clamped, offsetX, offsetY);
            ShowZoomBadge();
            e.Handled = true;
            DoubleTappedOccurred?.Invoke(this, EventArgs.Empty);
        }

        private void Scroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var control = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
            if (control != CoreVirtualKeyStates.Down)
            {
                return;
            }

            int delta = e.GetCurrentPoint(Scroller).Properties.MouseWheelDelta;
            Point position = e.GetCurrentPoint(Scroller).Position;
            ZoomAt(position, delta > 0 ? WheelZoomStep : 1f / WheelZoomStep);
            e.Handled = true;
        }

        public void ZoomAt(Point center, float factor)
        {
            float zoom = Math.Max(Scroller.MinZoomFactor,
                Math.Min(Scroller.MaxZoomFactor, Scroller.ZoomFactor * factor));
            double offsetX = center.X * zoom - Scroller.ViewportWidth / 2;
            double offsetY = center.Y * zoom - Scroller.ViewportHeight / 2;
            ChangeView(zoom, Math.Max(0, offsetX), Math.Max(0, offsetY));
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
