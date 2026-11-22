using HyperMedia_Lite.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.Storage.Search;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 负责从系统媒体库 / 文件选择器获取真实媒体文件，并记住用户通过选择器授予的访问权限。
    /// </summary>
    public sealed class LibraryService
    {
        private static readonly string[] AudioExtensions =
        {
            ".mp3", ".m4a", ".aac", ".wav", ".wma", ".flac", ".ogg", ".opus", ".aiff", ".alac"
        };

        private static readonly string[] VideoExtensions =
        {
            ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".mpg", ".mpeg", ".webm"
        };

        private static readonly string[] PhotoExtensions =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".heic", ".tif", ".tiff"
        };

        /// <summary>
        /// 根据扩展名判断媒体类型；不支持的类型返回 null。
        /// </summary>
        public static MediaKind? GetKind(string fileType)
        {
            if (string.IsNullOrEmpty(fileType))
            {
                return null;
            }
            string ext = fileType.ToLowerInvariant();
            if (AudioExtensions.Contains(ext)) return MediaKind.Audio;
            if (VideoExtensions.Contains(ext)) return MediaKind.Video;
            if (PhotoExtensions.Contains(ext)) return MediaKind.Photo;
            return null;
        }

        /// <summary>
        /// 扫描音乐 / 视频 / 图片库。任何库被拒绝访问时跳过该库，不影响其他库。
        /// </summary>
        public async Task<List<MediaItem>> ScanLibrariesAsync()
        {
            var items = new List<MediaItem>();
            await ScanFolderAsync(KnownFolders.MusicLibrary, MediaKind.Audio, AudioExtensions, items);
            await ScanFolderAsync(KnownFolders.VideosLibrary, MediaKind.Video, VideoExtensions, items);
            await ScanFolderAsync(KnownFolders.PicturesLibrary, MediaKind.Photo, PhotoExtensions, items);
            return items;
        }

        private static async Task ScanFolderAsync(
            StorageFolder folder,
            MediaKind kind,
            IEnumerable<string> extensions,
            List<MediaItem> output)
        {
            try
            {
                var options = new QueryOptions(CommonFileQuery.OrderByName, extensions);
                var files = await folder.CreateFileQueryWithOptions(options).GetFilesAsync();
                foreach (var file in files)
                {
                    output.Add(new MediaItem(file, kind));
                }
            }
            catch (Exception)
            {
                // 未授予库权限、库不存在或文件被占用：忽略该库。
            }
        }

        /// <summary>
        /// 打开文件选择器，允许多选任意位置的媒体文件，并把访问权限保存到未来访问列表。
        /// </summary>
        public async Task<List<MediaItem>> PickFilesAsync()
        {
            var result = new List<MediaItem>();

            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            foreach (var ext in AudioExtensions.Concat(VideoExtensions).Concat(PhotoExtensions))
            {
                picker.FileTypeFilter.Add(ext);
            }

            var files = await picker.PickMultipleFilesAsync();
            if (files == null)
            {
                return result;
            }

            foreach (var file in files)
            {
                MediaKind? kind = GetKind(file.FileType);
                if (kind == null)
                {
                    continue;
                }

                try
                {
                    StorageApplicationPermissions.FutureAccessList.AddOrReplace(TokenFor(file), file);
                }
                catch (Exception)
                {
                    // 无法持久化时仍可在当前会话中使用。
                }

                result.Add(new MediaItem(file, kind.Value));
            }

            return result;
        }

        /// <summary>
        /// 恢复之前通过文件选择器添加的媒体。
        /// </summary>
        public async Task<List<MediaItem>> RestoreAsync()
        {
            var result = new List<MediaItem>();

            foreach (var entry in StorageApplicationPermissions.FutureAccessList.Entries)
            {
                try
                {
                    var file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(entry.Token);
                    MediaKind? kind = GetKind(file.FileType);
                    if (kind != null)
                    {
                        result.Add(new MediaItem(file, kind.Value));
                    }
                }
                catch (Exception)
                {
                    // 文件已被移动或删除：忽略失效条目。
                }
            }

            return result;
        }

        /// <summary>
        /// 清除所有已记住的文件访问权限。
        /// </summary>
        public void ClearSavedFiles()
        {
            StorageApplicationPermissions.FutureAccessList.Clear();
        }

        /// <summary>
        /// 为由选择器添加的文件生成稳定的访问令牌（FutureAccessList 要求不超过 64 字符）。
        /// </summary>
        private static string TokenFor(StorageFile file)
        {
            string source = string.IsNullOrEmpty(file.Path) ? file.Name : file.Path;
            uint hash = 2166136261;
            foreach (char c in source)
            {
                unchecked
                {
                    hash ^= c;
                    hash *= 16777619;
                }
            }
            return "hm" + hash.ToString("X8");
        }
    }
}
