using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 本地同名媒体文件匹配：在媒体文件所在目录及常见子目录中，
    /// 按多种文件名变体查找歌词 / 字幕文件。
    /// 纯文件 I/O，所用 API 自 10240 起可用。
    /// </summary>
    public static class LocalMediaMatcher
    {
        private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

        /// <summary>
        /// 查找与媒体文件匹配的外部文件，找到第一个即返回。
        /// </summary>
        /// <param name="mediaFile">媒体文件（音频或视频）。</param>
        /// <param name="extensions">候选扩展名，如 .lrc / .srt。</param>
        /// <param name="subfolders">额外搜索的子目录名。</param>
        /// <param name="title">元数据标题，参与文件名变体。</param>
        /// <param name="artist">元数据艺人，参与文件名变体。</param>
        public static async Task<StorageFile> FindAsync(
            StorageFile mediaFile,
            IEnumerable<string> extensions,
            IEnumerable<string> subfolders,
            string title = null,
            string artist = null)
        {
            if (mediaFile == null) return null;

            var folders = new List<StorageFolder>();
            try
            {
                var parent = await mediaFile.GetParentAsync();
                if (parent != null)
                {
                    folders.Add(parent);
                    foreach (string name in subfolders ?? Enumerable.Empty<string>())
                    {
                        try
                        {
                            if (await parent.TryGetItemAsync(name) is StorageFolder sub && !folders.Contains(sub))
                            {
                                folders.Add(sub);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (folders.Count == 0) return null;

            var names = BuildBaseNames(mediaFile.Name, title, artist);
            var exts = (extensions ?? Enumerable.Empty<string>()).ToList();
            if (exts.Count == 0) return null;

            foreach (var folder in folders)
            {
                foreach (string baseName in names)
                {
                    foreach (string ext in exts)
                    {
                        try
                        {
                            var file = await folder.GetFileAsync(baseName + ext);
                            if (file != null) return file;
                        }
                        catch (FileNotFoundException) { }
                        catch { }
                    }
                }
            }
            return null;
        }

        private static List<string> BuildBaseNames(string fileName, string title, string artist)
        {
            var result = new List<string>();

            Action<string> add = value =>
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                string clean = Sanitize(value.Trim());
                if (clean.Length > 0 && !result.Contains(clean, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(clean);
                }
            };

            // 1) 与媒体文件完全一致的文件名（最常见）
            add(Path.GetFileNameWithoutExtension(fileName));

            // 2) 元数据标题
            add(title);

            // 3) 「艺人 - 歌名」两种顺序
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
            {
                add(artist + " - " + title);
                add(title + " - " + artist);
            }

            // 4) 文件名自身是「艺人 - 歌名」时，歌词可能只按歌名命名
            var parsed = HyperMedia_Lite.LyricMatcher.ParseArtistTitleFromFileName(fileName);
            if (!string.IsNullOrWhiteSpace(parsed.Item1))
            {
                add(parsed.Item2);
                add(parsed.Item1 + " - " + parsed.Item2);
            }

            return result;
        }

        private static string Sanitize(string value)
        {
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (InvalidChars.Contains(chars[i])) chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
