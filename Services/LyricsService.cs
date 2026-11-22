using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using HyperMedia_Lite.Models;
using Windows.Storage;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 歌词获取管线：内嵌 → 同名文件 → 在线 → 缓存。移植并适配自 HyperMedia。
    /// 在线候选择器由页面注入，避免服务层依赖 UI。
    /// </summary>
    public static class LyricsService
    {
        private const string CacheFolder = "lyrics";

        /// <summary>由页面设置：返回用户选中的候选，返回 null 表示取消。</summary>
        public static Func<List<LyricCandidate>, Task<LyricCandidate>> CandidatePicker { get; set; }

        public static async Task<List<LyricLine>> LoadAsync(MediaItem item)
        {
            if (item?.File == null) return new List<LyricLine>();

            // 1) 音频容器内嵌歌词
            string embedded = await LyricsParser.TryExtractEmbeddedAsync(item.File);
            if (!string.IsNullOrEmpty(embedded))
            {
                var lines = LyricsParser.Parse(embedded);
                if (lines.Count > 0) return lines;
            }

            // 2) 同名本地歌词文件（含编码探测）
            try
            {
                var folder = await item.File.GetParentAsync();
                string baseName = Path.GetFileNameWithoutExtension(item.File.Name);
                foreach (string ext in new[] { ".lrc", ".krc", ".qrc", ".txt", ".srt" })
                {
                    try
                    {
                        var file = await folder.GetFileAsync(baseName + ext);
                        var bytes = await ReadAllBytesAsync(file);
                        var lines = ParseBytes(bytes);
                        if (lines.Count > 0) return lines;
                    }
                    catch (FileNotFoundException) { }
                    catch { }
                }
            }
            catch { }

            // 3) 缓存
            var cached = await ReadCacheAsync(item);
            if (cached != null && cached.Count > 0) return cached;

            // 4) 在线
            var online = await LoadOnlineAsync(item);
            if (online != null && online.Count > 0)
            {
                await WriteCacheAsync(item, online);
                return online;
            }
            return new List<LyricLine>();
        }

        public static List<LyricLine> ParseBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new List<LyricLine>();

            if (LyricsParser.TryDecryptKrc(bytes, out string krc))
                return LyricsParser.Parse(krc, LyricFormat.Krc);

            var encodings = new List<Encoding> { new UTF8Encoding(true, false), Encoding.Unicode, Encoding.BigEndianUnicode };
            try { encodings.Add(Encoding.GetEncoding("GB2312")); } catch { }

            foreach (var encoding in encodings)
            {
                try
                {
                    string text = encoding.GetString(bytes).TrimStart('\uFEFF');
                    var lines = LyricsParser.Parse(text);
                    if (lines.Count > 0) return lines;
                }
                catch { }
            }
            return new List<LyricLine>();
        }

        private static async Task<byte[]> ReadAllBytesAsync(StorageFile file)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            using (var netStream = stream.AsStreamForRead())
            using (var memory = new MemoryStream())
            {
                await netStream.CopyToAsync(memory);
                return memory.ToArray();
            }
        }

        private static async Task<List<LyricLine>> LoadOnlineAsync(MediaItem item)
        {
            try
            {
                var parsed = LyricMatcher.ParseArtistTitleFromFileName(item.File.DisplayName);
                string artist = parsed.Item1;
                string title = parsed.Item2;
                if (!string.IsNullOrWhiteSpace(item.Album))
                {
                    var candidates = await OnlineLyrics.SearchAllAsync(artist, title, item.Album);
                    return await PickAsync(item, candidates, artist, title, item.Album);
                }
                var result = await OnlineLyrics.SearchAllAsync(artist, title, item.Album);
                return await PickAsync(item, result, artist, title, item.Album);
            }
            catch { return null; }
        }

        private static async Task<List<LyricLine>> PickAsync(MediaItem item, List<LyricCandidate> candidates,
            string artist, string title, string album)
        {
            if (candidates == null || candidates.Count == 0) return null;

            var settings = ApplicationData.Current.LocalSettings.Values;
            string remembered = settings["LyricChoice_" + item.Path] as string;
            LyricCandidate chosen = null;
            if (!string.IsNullOrEmpty(remembered))
                chosen = candidates.FirstOrDefault(c => c.Source + "|" + c.SongId == remembered);
            if (chosen == null)
                chosen = CandidatePicker != null ? await CandidatePicker(candidates) : candidates[0];
            if (chosen == null || string.IsNullOrEmpty(chosen.LyricText)) return null;

            settings["LyricChoice_" + item.Path] = chosen.Source + "|" + chosen.SongId;
            return LyricsParser.Parse(chosen.LyricText, chosen.Format);
        }

        public static async Task ForgetChoiceAsync(MediaItem item)
        {
            ApplicationData.Current.LocalSettings.Values.Remove("LyricChoice_" + item.Path);
            await DeleteCacheAsync(item);
        }

        private static string CacheKey(MediaItem item)
        {
            uint hash = 2166136261;
            foreach (char c in item.Path)
            {
                unchecked { hash ^= c; hash *= 16777619; }
            }
            return hash.ToString("X8");
        }

        private static async Task<StorageFolder> EnsureFolderAsync()
        {
            var local = ApplicationData.Current.LocalFolder;
            return await local.CreateFolderAsync(CacheFolder, CreationCollisionOption.OpenIfExists);
        }

        private static async Task WriteCacheAsync(MediaItem item, List<LyricLine> lines)
        {
            try
            {
                var folder = await EnsureFolderAsync();
                var file = await folder.CreateFileAsync(CacheKey(item) + ".lrc", CreationCollisionOption.ReplaceExisting);
                var sb = new StringBuilder();
                foreach (var line in lines)
                {
                    sb.Append(line.TimeSeconds.ToString("0.###")).Append('\t').Append(line.Text ?? "");
                    if (line.Words != null)
                    {
                        sb.Append('\t');
                        foreach (var w in line.Words)
                            sb.Append(w.StartSeconds.ToString("0.###")).Append(',')
                              .Append(w.EndSeconds.ToString("0.###")).Append(',')
                              .Append((w.Text ?? "").Replace('\t', ' ')).Append(';');
                    }
                    sb.Append('\n');
                }
                await FileIO.WriteTextAsync(file, sb.ToString());
            }
            catch { }
        }

        private static async Task<List<LyricLine>> ReadCacheAsync(MediaItem item)
        {
            try
            {
                var folder = await EnsureFolderAsync();
                var file = await folder.TryGetItemAsync(CacheKey(item) + ".lrc") as StorageFile;
                if (file == null) return null;
                string text = await FileIO.ReadTextAsync(file);
                var lines = new List<LyricLine>();
                foreach (string raw in text.Split('\n'))
                {
                    if (string.IsNullOrEmpty(raw)) continue;
                    string[] fields = raw.Split('\t');
                    if (fields.Length < 2) continue;
                    if (!double.TryParse(fields[0], out double time)) continue;
                    var line = new LyricLine { TimeSeconds = time, Text = fields[1] };
                    if (fields.Length >= 3 && !string.IsNullOrEmpty(fields[2]))
                    {
                        var words = new List<LyricWord>();
                        foreach (string part in fields[2].Split(';'))
                        {
                            string[] wf = part.Split(',');
                            if (wf.Length < 3) continue;
                            if (!double.TryParse(wf[0], out double ws) || !double.TryParse(wf[1], out double we)) continue;
                            words.Add(new LyricWord { StartSeconds = ws, EndSeconds = we, Text = wf[2] });
                        }
                        if (words.Count > 0) line.Words = words;
                    }
                    lines.Add(line);
                }
                return lines;
            }
            catch { return null; }
        }

        private static async Task DeleteCacheAsync(MediaItem item)
        {
            try
            {
                var folder = await EnsureFolderAsync();
                var file = await folder.TryGetItemAsync(CacheKey(item) + ".lrc");
                if (file != null) await file.DeleteAsync();
            }
            catch { }
        }
    }
}
