using HyperMedia_Lite.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.UI.Notifications;
using Windows.UI.Xaml;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 动态磁贴：正在播放（封面 + 歌名）与图片轮播（通知队列自动轮播）。
    /// TileUpdateManager 自 10240 起可用，无需版本守卫。
    /// </summary>
    public static class TileService
    {
        private const string TileImageFolder = "TileImages";
        private const string CurrentImage = "ms-appdata:///local/TileImages/nowplaying.png";
        private static string _currentTileTitle;
        private static string _currentTileArtist;

        private const string IntervalKey = "PhotoTileIntervalSeconds";
        private static DispatcherTimer _rotationTimer;
        private static List<string> _rotationImages = new List<string>();
        private static int _rotationIndex;

        /// <summary>0 = 使用系统 Photos 模板（应用关闭后仍轮播，间隔由系统决定）；>0 = 应用内定时推送的间隔秒数。</summary>
        public static int RotationInterval
        {
            get
            {
                try
                {
                    object raw = ApplicationData.Current.LocalSettings.Values[IntervalKey];
                    return raw is int value ? value : 0;
                }
                catch { return 0; }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[IntervalKey] = value;
                    if (_rotationTimer != null && value > 0)
                    {
                        _rotationTimer.Interval = TimeSpan.FromSeconds(value);
                    }
                }
                catch { }
            }
        }

        /// <summary>更新为正在播放磁贴。</summary>
        public static async Task UpdateNowPlayingAsync(MediaItem item, bool isPlaying)
        {
            if (item == null || item.File == null)
            {
                Clear();
                return;
            }
            try
            {
                Helpers.DebugLog.WriteAlways("TILE", "UpdateNowPlaying: " + item.Title);
                StopTimedRotation();
                string image = await SaveThumbnailAsync(item, "nowplaying.png");
                _currentTileTitle = XmlEscape(item.Title ?? item.File.DisplayName);
                _currentTileArtist = XmlEscape(item.Subtitle ?? string.Empty);
                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(false);
                updater.Clear();
                updater.Update(CreateNotification(BuildXml(image, isPlaying, true)));
                Helpers.DebugLog.WriteAlways("TILE", "now playing tile pushed");
            }
            catch (Exception ex)
            {
                Helpers.DebugLog.WriteAlways("TILE", "now playing failed: " + ex.Message);
            }
        }

        /// <summary>只更新播放/暂停状态，复用已保存的封面。</summary>
        public static void UpdatePlayStatus(bool isPlaying)
        {
            if (_currentTileTitle == null) return;
            try
            {
                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.Update(CreateNotification(BuildXml(CurrentImage, isPlaying, true)));
            }
            catch (Exception ex)
            {
                Helpers.DebugLog.WriteAlways("TILE", "status update failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 图片磁贴：用专门的 Photos 模板（hint-presentation="photos"），
        /// 由 Windows 原生缩放 + 淡入淡出循环播放，最多 12 张，所有尺寸都支持。
        /// </summary>
        public static async Task StartPhotoSlideshowAsync(IEnumerable<MediaItem> photos)
        {
            var list = photos?.Where(p => p != null && p.Kind == MediaKind.Photo && p.File != null)
                .Take(12).ToList();
            if (list == null || list.Count == 0) return;

            try
            {
                _currentTileTitle = null;
                _currentTileArtist = null;

                var images = new List<string>();
                for (int i = 0; i < list.Count; i++)
                {
                    images.Add(await SaveThumbnailAsync(list[i], "slide" + i + ".png"));
                }

                var notification = CreateNotification(BuildPhotosXml(images));
                notification.ExpirationTime = DateTimeOffset.Now.AddDays(7);

                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(false);
                updater.Clear();
                updater.Update(notification);

                Helpers.DebugLog.WriteAlways("TILE", "photo tile pushed, images=" + images.Count);
            }
            catch (Exception ex)
            {
                Helpers.DebugLog.WriteAlways("TILE", "slideshow failed: " + ex.Message);
            }
        }

        /// <summary>按自定义间隔在应用内定时轮换图片（关闭应用后停止）。</summary>
        public static async Task StartTimedRotationAsync(IEnumerable<MediaItem> photos)
        {
            var list = photos?.Where(p => p != null && p.Kind == MediaKind.Photo && p.File != null)
                .Take(12).ToList();
            if (list == null || list.Count == 0) return;

            try
            {
                StopTimedRotation();
                _currentTileTitle = null;
                _currentTileArtist = null;

                _rotationImages = new List<string>();
                for (int i = 0; i < list.Count; i++)
                {
                    _rotationImages.Add(await SaveThumbnailAsync(list[i], "slide" + i + ".png"));
                }
                _rotationIndex = 0;
                PushCurrentRotationImage();

                int seconds = RotationInterval;
                if (seconds <= 0) seconds = 10;

                _rotationTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(seconds)
                };
                _rotationTimer.Tick += (s, e) => PushCurrentRotationImage();
                _rotationTimer.Start();

                Helpers.DebugLog.WriteAlways("TILE", "timed rotation started, interval=" + seconds + "s");
            }
            catch (Exception ex)
            {
                Helpers.DebugLog.WriteAlways("TILE", "timed rotation failed: " + ex.Message);
            }
        }

        public static void StopTimedRotation()
        {
            if (_rotationTimer == null) return;
            _rotationTimer.Stop();
            _rotationTimer = null;
            _rotationImages.Clear();
            Helpers.DebugLog.WriteAlways("TILE", "timed rotation stopped");
        }

        private static void PushCurrentRotationImage()
        {
            if (_rotationImages.Count == 0) return;
            string image = _rotationImages[_rotationIndex % _rotationImages.Count];
            _rotationIndex = (_rotationIndex + 1) % _rotationImages.Count;

            try
            {
                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(false);
                updater.Update(CreateNotification(BuildSingleImageXml(image)));
            }
            catch (Exception ex)
            {
                Helpers.DebugLog.WriteAlways("TILE", "rotation push failed: " + ex.Message);
            }
        }

        /// <summary>诊断用：推送一张包内已知可用的图片，验证磁贴通道本身。</summary>
        public static void PushTestTile()
        {
            try
            {
                string image = "ms-appx:///Assets/LargeTile.scale-100.png";
                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(false);
                updater.Clear();
                updater.Update(CreateNotification(BuildPhotosXml(new[] { image })));
                Helpers.DebugLog.WriteAlways("TILE", "test tile pushed");
            }
            catch (Exception ex)
            {
                Helpers.DebugLog.WriteAlways("TILE", "test tile failed: " + ex.Message);
            }
        }

        /// <summary>清除磁贴内容。</summary>
        public static void Clear()
        {
            try
            {
                StopTimedRotation();
                _currentTileTitle = null;
                _currentTileArtist = null;
                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(false);
                updater.Clear();
            }
            catch { }
        }

        private static string BuildXml(string image, bool isPlaying, bool includeLarge)
        {
            string status = isPlaying ? "正在播放" : "已暂停";
            string title = _currentTileTitle ?? string.Empty;
            string artist = _currentTileArtist ?? string.Empty;

            return "<tile><visual version=\"3\" branding=\"name\">" +
                "<binding template=\"TileSmall\">" +
                "  <image src=\"" + image + "\" placement=\"background\"/>" +
                "</binding>" +
                "<binding template=\"TileMedium\" hint-overlay=\"30\" hint-textStacking=\"top\">" +
                "  <image src=\"" + image + "\" placement=\"background\"/>" +
                "  <group><subgroup>" +
                "    <text hint-style=\"caption\">" + title + "</text>" +
                "    <text hint-style=\"captionSubtle\">" + artist + "</text>" +
                "    <text hint-style=\"captionSubtle\">" + status + "</text>" +
                "  </subgroup></group>" +
                "</binding>" +
                "<binding template=\"TileWide\" hint-overlay=\"30\" hint-textStacking=\"top\">" +
                "  <image src=\"" + image + "\" placement=\"background\"/>" +
                "  <group><subgroup>" +
                "    <text hint-style=\"caption\">" + title + "</text>" +
                "    <text hint-style=\"captionSubtle\">" + artist + " · " + status + "</text>" +
                "  </subgroup></group>" +
                "</binding>" +
                (includeLarge
                    ? "<binding template=\"TileLarge\" hint-overlay=\"35\" hint-textStacking=\"bottom\">" +
                      "  <image src=\"" + image + "\" placement=\"background\"/>" +
                      "  <group><subgroup hint-weight=\"1\">" +
                      "    <text hint-style=\"title\" hint-maxLines=\"1\">" + title + "</text>" +
                      "    <text hint-style=\"captionSubtle\" hint-maxLines=\"1\">" + artist + " · " + status + "</text>" +
                      "  </subgroup></group>" +
                      "</binding>"
                    : string.Empty) +
                "</visual></tile>";
        }

        private static string BuildSingleImageXml(string image)
        {
            return "<tile><visual version=\"3\" branding=\"none\">" +
                "<binding template=\"TileSmall\"><image src=\"" + image + "\" placement=\"background\"/></binding>" +
                "<binding template=\"TileMedium\"><image src=\"" + image + "\" placement=\"background\"/></binding>" +
                "<binding template=\"TileWide\"><image src=\"" + image + "\" placement=\"background\"/></binding>" +
                "<binding template=\"TileLarge\"><image src=\"" + image + "\" placement=\"background\"/></binding>" +
                "</visual></tile>";
        }

        private static string BuildPhotosXml(IEnumerable<string> images)
        {
            string items = string.Empty;
            foreach (string image in images)
            {
                items += "<image src=\"" + image + "\"/>";
            }

            return "<tile><visual version=\"3\" branding=\"none\">" +
                "<binding template=\"TileSmall\" hint-presentation=\"photos\">" + items + "</binding>" +
                "<binding template=\"TileMedium\" hint-presentation=\"photos\">" + items + "</binding>" +
                "<binding template=\"TileWide\" hint-presentation=\"photos\">" + items + "</binding>" +
                "<binding template=\"TileLarge\" hint-presentation=\"photos\">" + items + "</binding>" +
                "</visual></tile>";
        }

        private static async Task<string> SaveThumbnailAsync(MediaItem item, string name)
        {
            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                TileImageFolder, CreationCollisionOption.OpenIfExists);
            var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);

            ThumbnailMode mode = item.Kind == MediaKind.Audio ? ThumbnailMode.MusicView
                : item.Kind == MediaKind.Video ? ThumbnailMode.VideosView : ThumbnailMode.PicturesView;

            using (var thumbnail = await item.File.GetThumbnailAsync(mode, 400, ThumbnailOptions.ResizeThumbnail))
            using (var input = thumbnail.AsStreamForRead())
            using (var output = await file.OpenStreamForWriteAsync())
            {
                await input.CopyToAsync(output);
                Helpers.DebugLog.WriteAlways("TILE", "thumbnail " + name + " size=" + output.Position);
            }
            return "ms-appdata:///local/" + TileImageFolder + "/" + name;
        }

        private static TileNotification CreateNotification(string xml)
        {
            var document = new Windows.Data.Xml.Dom.XmlDocument();
            document.LoadXml(xml);
            return new TileNotification(document);
        }

        private static string XmlEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
