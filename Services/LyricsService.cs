using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using HyperMedia_Lite.Helpers;
using HyperMedia_Lite.Models;
using HyperMedia_Lite.Services;
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

        /// <summary>由页面设置：返回用户选中的候选，返回 null 表示取消。参数：候选、艺人、歌名、专辑。</summary>
        public static Func<List<LyricCandidate>, string, string, string, Task<LyricCandidate>> CandidatePicker { get; set; }

        private static string Snippet(string text, int max = 1500)
        {
            if (string.IsNullOrEmpty(text)) return "<empty>";
            string normalized = text.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");
            return normalized.Length <= max ? normalized : normalized.Substring(0, max) + "...<truncated>";
        }

        private static string DecodeForLog(byte[] bytes)
        {
            foreach (var encoding in new Encoding[]
            {
                new UTF8Encoding(true, false), Encoding.Unicode, Encoding.BigEndianUnicode
            })
            {
                try { return encoding.GetString(bytes).TrimStart('\uFEFF'); }
                catch { }
            }
            return "<binary>";
        }

        private static void LogParsed(string source, string raw, List<LyricLine> lines)
        {
            if (!DebugLog.Enabled) return;

            DebugLog.Write("LYRIC", $"=== {source} ===");
            DebugLog.Write("LYRIC", $"format={LyricsParser.DetectFormat(raw)}, raw={Snippet(raw)}");
            DebugLog.Write("LYRIC", $"parsed lines={lines.Count}");
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                string wordInfo = line.Words == null || line.Words.Count == 0
                    ? "no-word-times"
                    : $"words={line.Words.Count} first={line.Words[0].StartSeconds:0.00}s last={line.Words[line.Words.Count - 1].StartSeconds:0.00}s";
                DebugLog.Write("LYRIC", $"  [{i:000}] t={line.TimeSeconds:0.00}s {wordInfo} text=\"{Snippet(line.Text, 300)}\"");
            }
        }

        public static async Task<List<LyricLine>> LoadAsync(MediaItem item)
        {
            if (item?.File == null) return new List<LyricLine>();

            // 0) 用户在选词面板里显式选过的歌词优先，避免被自动匹配覆盖。
            try
            {
                object choiceRaw = ApplicationData.Current.LocalSettings.Values["LyricChoice_" + item.Path];
                if (choiceRaw is string choice && !string.IsNullOrEmpty(choice))
                {
                    var chosen = await ReadCacheAsync(item);
                    if (chosen != null && chosen.Count > 0)
                    {
                        DebugLog.Write("LYRIC", "user choice restored from cache");
                        return chosen;
                    }
                }
            }
            catch { }

            // 1) 内嵌歌词 / 本地同名歌词
            var automatic = await LoadEmbeddedOrLocalAsync(item);
            if (automatic.Count > 0) return automatic;

            // 3) 缓存
            var cached = await ReadCacheAsync(item);
            if (cached != null && cached.Count > 0)
            {
                DebugLog.Write("LYRIC", $"cache hit, lines={cached.Count}");
                return cached;
            }

            // 4) 在线
            var online = await LoadOnlineAsync(item);
            if (online != null && online.Count > 0)
            {
                DebugLog.Write("LYRIC", $"online hit, lines={online.Count}");
                await WriteCacheAsync(item, online);
                return online;
            }

            DebugLog.Write("LYRIC", "no lyric found from any source");
            return new List<LyricLine>();
        }

        /// <summary>
        /// 恢复默认歌词：只取内嵌 / 本地同名歌词，不走在线、不弹选择面板。
        /// 用于用户在选词面板里选择「恢复默认歌词」。
        /// </summary>
        public static async Task<List<LyricLine>> LoadDefaultAsync(MediaItem item)
        {
            if (item?.File == null) return new List<LyricLine>();
            return await LoadEmbeddedOrLocalAsync(item);
        }

        private static async Task<List<LyricLine>> LoadEmbeddedOrLocalAsync(MediaItem item)
        {
            // 内嵌歌词
            try
            {
                string embedded = await LyricsParser.TryExtractEmbeddedAsync(item.File);
                if (!string.IsNullOrEmpty(embedded))
                {
                    DebugLog.Write("LYRIC", $"embedded lyric found, chars={embedded.Length}");
                    var lines = LyricsParser.Parse(embedded);
                    LogParsed("embedded", embedded, lines);
                    if (lines.Count > 0) return lines;
                }
            }
            catch { }

            // 本地歌词文件：同目录 / Lyrics 子目录，并兼容「艺人 - 歌名」命名变体
            try
            {
                var localFile = await LocalMediaMatcher.FindAsync(
                    item.File,
                    new[] { ".lrc", ".krc", ".qrc", ".txt", ".srt" },
                    new[] { "Lyrics", "lyrics", "歌词" },
                    item.Title,
                    item.Artist);
                if (localFile != null)
                {
                    var bytes = await ReadAllBytesAsync(localFile);
                    DebugLog.Write("LYRIC", $"local file={localFile.Name}, bytes={bytes.Length}");
                    var lines = ParseBytes(bytes);
                    LogParsed("local:" + localFile.Name, DecodeForLog(bytes), lines);
                    if (lines.Count > 0) return lines;
                }
            }
            catch { }

            return new List<LyricLine>();
        }

        public static List<LyricLine> ParseBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new List<LyricLine>();

            if (LyricsParser.TryDecryptKrc(bytes, out string krc))
            {
                DebugLog.Write("LYRIC", "KRC decrypted");
                return LyricsParser.Parse(krc, LyricFormat.Krc);
            }

            var encodings = new List<Encoding> { new UTF8Encoding(true, false), Encoding.Unicode, Encoding.BigEndianUnicode };
            try { encodings.Add(Encoding.GetEncoding("GB2312")); } catch { }

            foreach (var encoding in encodings)
            {
                try
                {
                    string text = encoding.GetString(bytes).TrimStart('\uFEFF');
                    var lines = LyricsParser.Parse(text);
                    if (lines.Count > 0)
                    {
                        DebugLog.Write("LYRIC", $"decoded with {encoding.WebName}, lines={lines.Count}");
                        return lines;
                    }
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
                chosen = CandidatePicker != null
                    ? await CandidatePicker(candidates, artist, title, album)
                    : candidates[0];

            if (chosen != null && chosen.IsResetRequest)
            {
                await ForgetChoiceAsync(item);
                return await LoadDefaultAsync(item);
            }
            if (chosen == null || string.IsNullOrEmpty(chosen.LyricText)) return null;

            settings["LyricChoice_" + item.Path] = chosen.Source + "|" + chosen.SongId;
            return LyricsParser.Parse(chosen.LyricText, chosen.Format);
        }

        public static async Task ForgetChoiceAsync(MediaItem item)
        {
            ApplicationData.Current.LocalSettings.Values.Remove("LyricChoice_" + item.Path);
            await DeleteCacheAsync(item);
        }

        /// <summary>
        /// 记住用户在选词面板中的选择。同时写缓存，确保下次启动时能原样恢复
        /// （本地歌词文件选择不走在线搜索，必须显式持久化）。
        /// </summary>
        public static async Task PersistChoiceAsync(
            MediaItem item, string source, string songId, List<LyricLine> lines)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values["LyricChoice_" + item.Path] =
                    source + "|" + songId;
                if (lines != null && lines.Count > 0)
                {
                    await WriteCacheAsync(item, lines);
                }
            }
            catch { }
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
