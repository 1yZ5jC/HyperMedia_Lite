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
    /// 负责从系统媒体库 / 文件选择器 / 文件夹选择器获取真实媒体文件，
    /// 并记住用户授予的访问权限。所有扫描均递归包含子文件夹。
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

        private static readonly string[] AllExtensions =
            AudioExtensions.Concat(VideoExtensions).Concat(PhotoExtensions).ToArray();

        /// <summary>根据扩展名判断媒体类型；不支持的类型返回 null。</summary>
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
        /// 扫描音乐 / 视频 / 图片库，递归包含所有子文件夹。
        /// 任何库被拒绝访问时跳过该库，不影响其他库。
        /// </summary>
        public async Task<List<MediaItem>> ScanLibrariesAsync()
        {
            var items = new List<MediaItem>();
            await ScanFolderAsync(KnownFolders.MusicLibrary, items);
            await ScanFolderAsync(KnownFolders.VideosLibrary, items);
            await ScanFolderAsync(KnownFolders.PicturesLibrary, items);
            return items;
        }

        /// <summary>
        /// 递归扫描一个文件夹（包含所有子文件夹）中的受支持媒体文件。
        /// </summary>
        private static async Task ScanFolderAsync(StorageFolder folder, List<MediaItem> output)
        {
            try
            {
                // DefaultQuery + FolderDepth.Deep 可对任意文件夹做深层递归查询。
                var options = new QueryOptions(CommonFileQuery.DefaultQuery, AllExtensions)
                {
                    FolderDepth = FolderDepth.Deep
                };
                var files = await folder.CreateFileQueryWithOptions(options).GetFilesAsync();

                foreach (var file in files)
                {
                    MediaKind? kind = GetKind(file.FileType);
                    if (kind != null)
                    {
                        output.Add(new MediaItem(file, kind.Value));
                    }
                }
            }
            catch (Exception)
            {
                // 访问被拒绝、文件夹被移动或文件被占用：忽略。
            }
        }

        /// <summary>
        /// 打开文件选择器，允许多选任意位置的媒体文件。
        /// </summary>
        public async Task<List<MediaItem>> PickFilesAsync()
        {
            var result = new List<MediaItem>();

            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            foreach (var ext in AllExtensions)
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
        /// 打开文件夹选择器，选择一个文件夹并递归导入其中所有媒体文件。
        /// 文件夹访问权限会被记住，下次启动自动重新扫描其子文件夹。
        /// </summary>
        public async Task<List<MediaItem>> PickFolderAsync()
        {
            var result = new List<MediaItem>();

            var picker = new FolderPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                CommitButtonText = "选择文件夹"
            };
            foreach (var ext in AllExtensions)
            {
                picker.FileTypeFilter.Add(ext);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
            {
                return result;
            }

            try
            {
                StorageApplicationPermissions.FutureAccessList.AddOrReplace(FolderTokenFor(folder), folder);
            }
            catch (Exception)
            {
                // 无法持久化时仍可在当前会话中扫描。
            }

            await ScanFolderAsync(folder, result);
            return result;
        }

        /// <summary>
        /// 恢复之前通过选择器添加的文件和文件夹（文件夹会重新递归扫描）。
        /// </summary>
        public async Task<List<MediaItem>> RestoreAsync()
        {
            var result = new List<MediaItem>();

            foreach (var entry in StorageApplicationPermissions.FutureAccessList.Entries)
            {
                try
                {
                    if (entry.Token.StartsWith("hmd"))
                    {
                        var folder = await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(entry.Token);
                        await ScanFolderAsync(folder, result);
                    }
                    else
                    {
                        var file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(entry.Token);
                        MediaKind? kind = GetKind(file.FileType);
                        if (kind != null)
                        {
                            result.Add(new MediaItem(file, kind.Value));
                        }
                    }
                }
                catch (Exception)
                {
                    // 文件 / 文件夹已被移动或删除：忽略失效条目。
                }
            }

            return result;
        }

        /// <summary>清除所有已记住的文件与文件夹访问权限。</summary>
        public void ClearSavedFiles()
        {
            StorageApplicationPermissions.FutureAccessList.Clear();
        }

        private static string TokenFor(StorageFile file)
        {
            return "hm" + HashFor(string.IsNullOrEmpty(file.Path) ? file.Name : file.Path);
        }

        private static string FolderTokenFor(StorageFolder folder)
        {
            return "hmd" + HashFor(string.IsNullOrEmpty(folder.Path) ? folder.Name : folder.Path);
        }

        private static string HashFor(string source)
        {
            uint hash = 2166136261;
            foreach (char c in source)
            {
                unchecked
                {
                    hash ^= c;
                    hash *= 16777619;
                }
            }
            return hash.ToString("X8");
        }
    }
}
