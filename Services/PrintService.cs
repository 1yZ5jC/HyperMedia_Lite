using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Printing;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Printing;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 图片打印：使用 UWP XAML 打印管线（PrintManager + PrintDocument）。
    /// 所用 API 自 UniversalApiContract 1.0 起可用，15063 无需额外守卫。
    /// </summary>
    public static class PrintService
    {
        private static PrintDocument _document;
        private static IPrintDocumentSource _documentSource;
        private static CoreDispatcher _dispatcher;
        private static BitmapImage _image;
        private static string _title = "图片";
        private static bool _registered;

        private static Size _pageSize = new Size(816, 1056);
        private static Rect _imageable = new Rect(48, 48, 720, 960);

        public static bool IsRegistered => _registered;

        public static void Register()
        {
            if (_registered) return;
            try
            {
                // 都在 UI 线程完成：PrintDocument 及其 DocumentSource 有线程亲和性，
                // 不能在打印回调的后台线程上访问。
                _dispatcher = Window.Current?.CoreWindow?.Dispatcher;
                _document = new PrintDocument();
                _document.Paginate += OnPaginate;
                _document.GetPreviewPage += OnGetPreviewPage;
                _document.AddPages += OnAddPages;
                _documentSource = _document.DocumentSource;
                PrintManager.GetForCurrentView().PrintTaskRequested += OnPrintTaskRequested;
                _registered = true;
            }
            catch
            {
                _registered = false;
            }
        }

        public static void Unregister()
        {
            if (!_registered) return;
            try
            {
                PrintManager.GetForCurrentView().PrintTaskRequested -= OnPrintTaskRequested;
                _document.Paginate -= OnPaginate;
                _document.GetPreviewPage -= OnGetPreviewPage;
                _document.AddPages -= OnAddPages;
            }
            catch { }

            _document = null;
            _documentSource = null;
            _dispatcher = null;
            _image = null;
            _registered = false;
        }

        /// <summary>显示系统打印面板，打印指定图片。</summary>
        public static async Task<bool> PrintAsync(StorageFile file, string title)
        {
            if (file == null) return false;
            Register();
            if (!_registered || _document == null) return false;

            try
            {
                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    var image = new BitmapImage();
                    await image.SetSourceAsync(stream);
                    _image = image;
                }
                _title = string.IsNullOrWhiteSpace(title) ? file.DisplayName : title;
                return await PrintManager.ShowPrintUIAsync();
            }
            catch
            {
                return false;
            }
        }

        private static void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
        {
            var deferral = args.Request.GetDeferral();
            try
            {
                if (_image == null || _documentSource == null) return;

                args.Request.CreatePrintTask("打印 " + _title, requested =>
                {
                    // 此回调运行在后台线程；SetSource 必须回到创建 PrintDocument 的 UI 线程，
                    // 否则会抛 RPC_E_WRONG_THREAD (0x8001010E)。
                    var sourceDeferral = requested.GetDeferral();
                    var dispatcher = _dispatcher;

                    if (dispatcher != null)
                    {
                        var ignored = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            try { requested.SetSource(_documentSource); }
                            catch { }
                            finally { sourceDeferral.Complete(); }
                        });
                    }
                    else
                    {
                        try { requested.SetSource(_documentSource); }
                        catch { }
                        finally { sourceDeferral.Complete(); }
                    }
                });
            }
            catch { }
            finally
            {
                deferral.Complete();
            }
        }

        private static void OnPaginate(object sender, PaginateEventArgs e)
        {
            try
            {
                var desc = e.PrintTaskOptions.GetPageDescription(0);
                _pageSize = desc.PageSize;
                if (desc.ImageableRect.Width > 0 && desc.ImageableRect.Height > 0)
                {
                    _imageable = desc.ImageableRect;
                }
                else
                {
                    _imageable = new Rect(48, 48, _pageSize.Width - 96, _pageSize.Height - 96);
                }
            }
            catch { }

            _document.SetPreviewPageCount(1, PreviewPageCountType.Final);
        }

        private static void OnGetPreviewPage(object sender, GetPreviewPageEventArgs e)
        {
            _document.SetPreviewPage(e.PageNumber, BuildPage());
        }

        private static void OnAddPages(object sender, AddPagesEventArgs e)
        {
            _document.AddPage(BuildPage());
            _document.AddPagesComplete();
        }

        private static UIElement BuildPage()
        {
            var root = new Grid
            {
                Width = _pageSize.Width,
                Height = _pageSize.Height,
                Background = new SolidColorBrush(Colors.White)
            };

            if (_image != null)
            {
                double left = Math.Max(0, _imageable.X);
                double top = Math.Max(0, _imageable.Y);
                double right = Math.Max(0, _pageSize.Width - _imageable.X - _imageable.Width);
                double bottom = Math.Max(0, _pageSize.Height - _imageable.Y - _imageable.Height);

                root.Children.Add(new Image
                {
                    Source = _image,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(left, top, right, bottom)
                });
            }

            return root;
        }
    }
}
