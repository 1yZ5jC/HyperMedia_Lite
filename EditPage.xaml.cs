using HyperMedia_Lite.Models;
using HyperMedia_Lite.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 图片编辑器：裁剪、旋转/翻转、亮度/对比度/饱和度、白平衡、滤镜、模糊/锐化/暗角与另存。
    /// 图像处理核心移植自 HyperViewer 的 ImageEditService。
    /// </summary>
    public sealed partial class EditPage : Page
    {
        private const int MaxUndoSteps = 12;

        private MediaItem _sourceItem;
        private EditableImage _original;
        private EditableImage _current;
        private readonly List<EditableImage> _undo = new List<EditableImage>();
        private readonly List<EditableImage> _redo = new List<EditableImage>();
        private bool _suppressSliderEvents;
        private bool _cropMode;
        private bool _dragging;
        private Point _cropStart;
        private Point _cropEnd;

        public EditPage()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _sourceItem = e.Parameter as MediaItem;
            if (_sourceItem == null)
            {
                StatusText.Text = "没有可编辑的图片";
                return;
            }

            _original = await ImageEditService.LoadAsync(_sourceItem.File);
            if (_original == null)
            {
                StatusText.Text = "图片解码失败";
                return;
            }
            _current = _original.Clone();
            StatusText.Text = $"{_current.Width} × {_current.Height}";
            await RefreshPreviewAsync();
            UpdateUndoRedoButtons();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void PushUndo()
        {
            _undo.Add(_current.Clone());
            if (_undo.Count > MaxUndoSteps)
            {
                _undo.RemoveAt(0);
            }
            _redo.Clear();
            UpdateUndoRedoButtons();
        }

        private void UpdateUndoRedoButtons()
        {
            UndoButton.IsEnabled = _undo.Count > 0;
            RedoButton.IsEnabled = _redo.Count > 0;
        }

        private async Task RefreshPreviewAsync()
        {
            if (_current == null)
            {
                return;
            }

            EditableImage preview = ImageEditService.Downsample(_current, 1600).Clone();
            ApplyParameters(preview.Pixels);
            PreviewImage.Source = await preview.ToBitmapAsync();
        }

        private AdjustFilter CurrentFilter
        {
            get
            {
                switch (FilterCombo.SelectedIndex)
                {
                    case 1: return AdjustFilter.Grayscale;
                    case 2: return AdjustFilter.Invert;
                    case 3: return AdjustFilter.Sepia;
                    case 4: return AdjustFilter.Warm;
                    case 5: return AdjustFilter.Cool;
                    case 6: return AdjustFilter.Film;
                    default: return AdjustFilter.None;
                }
            }
        }

        private void ApplyParameters(byte[] pixels)
        {
            ImageEditService.ApplyAdjustments(pixels,
                (int)BrightnessSlider.Value,
                (int)ContrastSlider.Value,
                (int)SaturationSlider.Value,
                CurrentFilter);
            ImageEditService.ApplyWhiteBalance(pixels,
                (int)TemperatureSlider.Value,
                (int)TintSlider.Value);

            int blur = (int)BlurSlider.Value;
            if (blur > 0)
            {
                ImageEditService.ApplyBoxBlur(pixels, _current.Width, _current.Height, blur);
            }
            double sharpen = SharpenSlider.Value / 100.0;
            if (sharpen > 0.01)
            {
                ImageEditService.ApplySharpen(pixels, _current.Width, _current.Height, sharpen);
            }
            float vignette = (float)(VignetteSlider.Value / 100.0);
            if (vignette > 0)
            {
                ImageEditService.ApplyVignette(pixels, _current.Width, _current.Height, vignette);
            }
        }

        private void Adjustment_Changed(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressSliderEvents)
            {
                return;
            }
            _ = RefreshPreviewAsync();
        }

        private void ResetSliders()
        {
            _suppressSliderEvents = true;
            BrightnessSlider.Value = 0;
            ContrastSlider.Value = 0;
            SaturationSlider.Value = 0;
            TemperatureSlider.Value = 0;
            TintSlider.Value = 0;
            BlurSlider.Value = 0;
            SharpenSlider.Value = 0;
            VignetteSlider.Value = 0;
            FilterCombo.SelectedIndex = 0;
            _suppressSliderEvents = false;
        }

        private async void ApplyAdjustments_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null)
            {
                return;
            }
            PushUndo();
            ApplyParameters(_current.Pixels);
            ResetSliders();
            await RefreshPreviewAsync();
        }

        private async void RotateCw_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            PushUndo();
            _current = ImageEditService.Rotate(_current, true);
            await RefreshPreviewAsync();
        }

        private async void RotateCcw_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            PushUndo();
            _current = ImageEditService.Rotate(_current, false);
            await RefreshPreviewAsync();
        }

        private async void FlipH_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            PushUndo();
            _current = ImageEditService.Flip(_current, true);
            await RefreshPreviewAsync();
        }

        private async void FlipV_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            PushUndo();
            _current = ImageEditService.Flip(_current, false);
            await RefreshPreviewAsync();
        }

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (_original == null) return;
            PushUndo();
            _current = _original.Clone();
            ResetSliders();
            await RefreshPreviewAsync();
        }

        private async void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undo.Count == 0) return;
            _redo.Add(_current.Clone());
            _current = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            UpdateUndoRedoButtons();
            await RefreshPreviewAsync();
        }

        private async void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (_redo.Count == 0) return;
            _undo.Add(_current.Clone());
            _current = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            UpdateUndoRedoButtons();
            await RefreshPreviewAsync();
        }

        private void CropMode_Click(object sender, RoutedEventArgs e)
        {
            _cropMode = !_cropMode;
            CropActions.Visibility = _cropMode ? Visibility.Visible : Visibility.Collapsed;
            CropRectangle.Visibility = Visibility.Collapsed;
            CropHint.Text = _cropMode ? "在图片上按住鼠标拖动以框选区域" : string.Empty;
        }

        private void CancelCrop_Click(object sender, RoutedEventArgs e)
        {
            _cropMode = false;
            CropActions.Visibility = Visibility.Collapsed;
            CropRectangle.Visibility = Visibility.Collapsed;
            CropHint.Text = string.Empty;
        }

        private void PreviewArea_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_cropMode || _current == null)
            {
                return;
            }
            _dragging = true;
            _cropStart = e.GetCurrentPoint(PreviewArea).Position;
            _cropEnd = _cropStart;
            UpdateCropRectangle();
        }

        private void PreviewArea_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }
            _cropEnd = e.GetCurrentPoint(PreviewArea).Position;
            UpdateCropRectangle();
        }

        private void PreviewArea_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _dragging = false;
        }

        private void UpdateCropRectangle()
        {
            double left = Math.Min(_cropStart.X, _cropEnd.X);
            double top = Math.Min(_cropStart.Y, _cropEnd.Y);
            double width = Math.Abs(_cropStart.X - _cropEnd.X);
            double height = Math.Abs(_cropStart.Y - _cropEnd.Y);

            CropRectangle.HorizontalAlignment = HorizontalAlignment.Left;
            CropRectangle.VerticalAlignment = VerticalAlignment.Top;
            CropRectangle.Margin = new Thickness(left, top, 0, 0);
            CropRectangle.Width = width;
            CropRectangle.Height = height;
            CropRectangle.Visibility = width > 4 && height > 4 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || CropRectangle.Visibility != Visibility.Visible)
            {
                return;
            }

            double areaWidth = PreviewArea.ActualWidth;
            double areaHeight = PreviewArea.ActualHeight;
            if (areaWidth <= 0 || areaHeight <= 0)
            {
                return;
            }

            double scale = Math.Min(areaWidth / _current.Width, areaHeight / _current.Height);
            double offsetX = (areaWidth - _current.Width * scale) / 2;
            double offsetY = (areaHeight - _current.Height * scale) / 2;

            int x = (int)Math.Max(0, Math.Min(_current.Width - 1, (CropRectangle.Margin.Left - offsetX) / scale));
            int y = (int)Math.Max(0, Math.Min(_current.Height - 1, (CropRectangle.Margin.Top - offsetY) / scale));
            int width = (int)Math.Max(1, Math.Min(_current.Width - x, CropRectangle.Width / scale));
            int height = (int)Math.Max(1, Math.Min(_current.Height - y, CropRectangle.Height / scale));

            PushUndo();
            _current = ImageEditService.Crop(_current, x, y, width, height);
            StatusText.Text = $"{_current.Width} × {_current.Height}";
            CancelCrop_Click(this, new RoutedEventArgs());
            await RefreshPreviewAsync();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null)
            {
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = (_sourceItem?.Title ?? "image") + "_edited"
            };
            bool jpeg = true;
            picker.FileTypeChoices.Add("JPEG 图片", new List<string> { ".jpg" });
            picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });

            StorageFile file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                return;
            }
            jpeg = string.Equals(file.FileType, ".jpg", StringComparison.OrdinalIgnoreCase);

            StatusText.Text = "正在保存…";
            try
            {
                await ImageEditService.SaveAsync(file, _current, jpeg);
                StatusText.Text = "已保存：" + file.Name;
            }
            catch (Exception exception)
            {
                StatusText.Text = "保存失败：" + exception.Message;
            }
        }
    }
}
