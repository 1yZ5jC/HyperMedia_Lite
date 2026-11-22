using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Web.Http;

namespace HyperMedia_Lite
{
    public sealed class LyricCandidate
    {
        public string Source;
        public string SongId;
        public string Title;
        public string Artist;
        public string Album;
        public List<string> Aliases = new List<string>();
        public bool StrictMatch;
        public double Score;
        public int Order;
        public LyricFormat Format;
        public string LyricText;
    }

    /// <summary>标题/艺人归一化与打分（移植自 HyperMedia）。</summary>
    public static class LyricMatcher
    {
        public static string NormalizeTitle(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            bool inParen = false;
            foreach (char c in s.ToLowerInvariant())
            {
                if (c == '(' || c == '（' || c == '[' || c == '【') { inParen = true; continue; }
                if (c == ')' || c == '）' || c == ']' || c == '】') { inParen = false; continue; }
                if (!inParen && !char.IsWhiteSpace(c)) sb.Append(c);
            }
            string norm = sb.ToString();
            int i = 0;
            while (i < norm.Length && char.IsDigit(norm[i])) i++;
            if (i > 0 && i < norm.Length)
            {
                char sep = norm[i];
                bool glued = char.IsLetter(sep);
                bool cjk = sep >= 0x4E00 && sep <= 0x9FFF;
                if (sep == '.' || sep == '-' || sep == '_' || sep == '：' || sep == ':' || (glued && cjk))
                    return norm.Substring(i + 1);
            }
            return norm;
        }

        private static int EditDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? 0 : b.Length;
            if (string.IsNullOrEmpty(b)) return a.Length;
            int[] prev = new int[b.Length + 1];
            int[] curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    curr[j] = Math.Min(curr[j - 1] + 1,
                        Math.Min(prev[j] + 1, prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1)));
                var temp = prev; prev = curr; curr = temp;
            }
            return prev[b.Length];
        }

        public static double ScoreName(string queryTitle, string candidateTitle)
        {
            string q = NormalizeTitle(queryTitle);
            string c = NormalizeTitle(candidateTitle);
            if (q.Length == 0 || c.Length == 0) return 0.2;
            if (q == c) return 1.0;
            if (q.Contains(c) || c.Contains(q)) return 0.8;
            int d = EditDistance(q, c);
            int max = Math.Max(q.Length, c.Length);
            if (max > 0 && d <= Math.Max(2, max / 3)) return 0.6;
            return 0.3;
        }

        public static double ScoreTrack(string title, string artist, string album,
            string candidateTitle, string candidateArtist, string candidateAlbum, List<string> aliases)
        {
            double t = ScoreName(title, candidateTitle);
            if (aliases != null)
            {
                double best = aliases.Aggregate(0.0, (acc, a) => Math.Max(acc, ScoreName(title, a)));
                t = Math.Max(t, best * 0.92);
            }
            string qa = NormalizeTitle(artist);
            string ca = NormalizeTitle(candidateArtist);
            double aScore = qa.Length == 0 ? 0.8 : ca.Length == 0 ? 0.7
                : qa == ca ? 1.0 : qa.Contains(ca) || ca.Contains(qa) ? 0.9 : 0.5;
            double score = t * 0.7 + aScore * 0.3;

            string qal = NormalizeTitle(album);
            string cal = NormalizeTitle(candidateAlbum);
            if (qal.Length > 0 && cal.Length > 0 && (qal == cal || qal.Contains(cal) || cal.Contains(qal)))
                score = Math.Min(1.0, score + 0.1);
            return score;
        }

        public static string CleanSearchTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;
            string t = title;
            foreach (string cut in new[] { "(feat", "(ft", "(with", " ft.", " - live", " - remix", "(live", "(instrumental", "(伴奏" })
            {
                int i = t.IndexOf(cut, StringComparison.OrdinalIgnoreCase);
                if (i >= 0) { t = t.Substring(0, i).Trim(); break; }
            }
            var sb = new StringBuilder();
            bool inParen = false;
            foreach (char c in t)
            {
                if (c == '(' || c == '（' || c == '[' || c == '【') { inParen = true; continue; }
                if (c == ')' || c == '）' || c == ']' || c == '】') { inParen = false; continue; }
                if (!inParen) sb.Append(c);
            }
            string r = sb.ToString().Trim();
            return r.Length > 0 ? r : title;
        }

        public static List<string> BuildSearchQueries(string artist, string title, string album)
        {
            var list = new List<string>();
            string t = CleanSearchTitle(title);
            string a = CleanSearchTitle(artist ?? "");
            string al = CleanSearchTitle(album ?? "");
            string q1 = string.IsNullOrEmpty(a) ? t : a + " " + t;
            if (!string.IsNullOrEmpty(q1)) list.Add(q1);
            if (!string.IsNullOrEmpty(al) &&
                !string.Equals(al, t, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(al, q1, StringComparison.OrdinalIgnoreCase))
            {
                string q2 = (string.IsNullOrEmpty(a) ? "" : a + " ") + t + " " + al;
                if (!list.Contains(q2)) list.Add(q2);
            }
            if (!string.Equals(t, title, StringComparison.OrdinalIgnoreCase)) list.Add(t);
            return list;
        }

        public static bool TitlesMatch(string candidate, string title)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(title)) return false;
            string a = NormalizeTitle(candidate);
            string b = NormalizeTitle(title);
            if (a.Length == 0 || a != b) return false;
            if (HasParen(candidate) && !HasParen(title)) return false;
            return true;
        }

        private static bool HasParen(string s) =>
            s.IndexOf('(') >= 0 || s.IndexOf('（') >= 0 || s.IndexOf('[') >= 0 || s.IndexOf('【') >= 0;

        public static bool ArtistsMatch(IEnumerable<string> artists, string artist)
        {
            string n = NormalizeTitle(artist);
            if (n.Length == 0) return false;
            return artists.Any(name => NormalizeTitle(name) == n);
        }

        public static IEnumerable<string> NeteaseArtists(JsonObject song)
        {
            var names = new List<string>();
            try
            {
                var arr = song.GetNamedArray("artists", null);
                if (arr != null)
                    for (uint i = 0; i < arr.Count; i++) names.Add(arr.GetObjectAt(i).GetNamedString("name", ""));
            }
            catch { }
            return names;
        }

        public static IEnumerable<string> QqArtists(JsonObject item)
        {
            var names = new List<string>();
            try
            {
                var arr = item.GetNamedArray("singer", null);
                if (arr != null)
                    for (uint i = 0; i < arr.Count; i++) names.Add(arr.GetObjectAt(i).GetNamedString("name", ""));
            }
            catch { }
            return names;
        }

        public static Tuple<string, string> ParseArtistTitleFromFileName(string fileName)
        {
            string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName ?? "");
            string artist = "";
            string title = baseName;
            foreach (string sep in new[] { " - ", "–", "—", "-", "_" })
            {
                int idx = baseName.IndexOf(sep, StringComparison.Ordinal);
                if (idx > 0)
                {
                    string left = baseName.Substring(0, idx).Trim();
                    string right = baseName.Substring(idx + sep.Length).Trim();
                    if (left.Length > 0 && right.Length > 0) { artist = left; title = right; break; }
                }
            }
            return Tuple.Create(artist, title);
        }
    }

    /// <summary>在线歌词搜索（网易云 + QQ 音乐），移植自 HyperMedia。</summary>
    public static class OnlineLyrics
    {
        private static readonly HttpClient Http = CreateClient();
        private static int _generation;

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.TryParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/80.0.3987.132 Safari/537.36");
            client.DefaultRequestHeaders.TryAppendWithoutValidation("Accept", "application/json, text/plain, */*");
            return client;
        }

        private static async Task<string> GetAsync(string url, Dictionary<string, string> headers = null)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url)))
                {
                    if (headers != null)
                        foreach (var kv in headers) request.Headers.TryAppendWithoutValidation(kv.Key, kv.Value);
                    using (var response = await Http.SendRequestAsync(request))
                        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
                }
            }
            catch { return null; }
        }

        public static async Task<List<LyricCandidate>> SearchAllAsync(string artist, string title, string album)
        {
            int generation = ++_generation;
            try
            {
                var all = new List<LyricCandidate>();
                all.AddRange(await CollectNetease(artist, title, album));
                if (generation != _generation) return null;
                all.AddRange(await CollectQq(artist, title, album));
                if (generation != _generation) return null;

                var usable = all.Where(c => !string.IsNullOrEmpty(c.LyricText)).ToList();
                usable.Sort((a, b) =>
                {
                    if (a.StrictMatch != b.StrictMatch) return b.StrictMatch.CompareTo(a.StrictMatch);
                    if (Math.Abs(a.Score - b.Score) > 0.001) return b.Score.CompareTo(a.Score);
                    int sa = a.Source == "Netease" ? 0 : 1;
                    int sb = b.Source == "Netease" ? 0 : 1;
                    if (sa != sb) return sa.CompareTo(sb);
                    return a.Order.CompareTo(b.Order);
                });
                return usable.Count > 0 ? usable : null;
            }
            catch { return null; }
        }

        private static async Task<List<LyricCandidate>> CollectNetease(string artist, string title, string album)
        {
            var result = new List<LyricCandidate>();
            try
            {
                JsonArray songs = null;
                foreach (string q in LyricMatcher.BuildSearchQueries(artist, title, album))
                {
                    string json = await GetAsync("https://music.163.com/api/search/get/web?s=" +
                        Uri.EscapeDataString(q) + "&type=1&offset=0&limit=10");
                    if (string.IsNullOrEmpty(json)) continue;
                    var root = JsonObject.Parse(json);
                    var r = root.GetNamedObject("result", null);
                    songs = r?.GetNamedArray("songs", null);
                    if (songs != null && songs.Count > 0) break;
                }
                if (songs == null) return result;

                int order = 0;
                var seen = new HashSet<string>();
                for (uint i = 0; i < songs.Count; i++)
                {
                    var song = songs.GetObjectAt(i);
                    if (!song.ContainsKey("id")) continue;
                    string id = ((long)song.GetNamedNumber("id")).ToString();
                    if (!seen.Add(id)) continue;
                    string name = song.GetNamedString("name", "");
                    string artistName = "";
                    string albumName = "";
                    var aliases = new List<string>();
                    try
                    {
                        var arts = song.GetNamedArray("artists", null);
                        if (arts != null && arts.Count > 0) artistName = arts.GetObjectAt(0).GetNamedString("name", "");
                        var alb = song.GetNamedObject("album", null);
                        if (alb != null && alb.ContainsKey("name")) albumName = alb.GetNamedString("name", "");
                        var aliasArr = song.GetNamedArray("alias", null);
                        if (aliasArr != null)
                            for (uint k = 0; k < aliasArr.Count; k++) aliases.Add(aliasArr.GetStringAt(k));
                    }
                    catch { }

                    result.Add(new LyricCandidate
                    {
                        Source = "Netease", SongId = id, Title = name, Artist = artistName,
                        Album = albumName, Aliases = aliases,
                        StrictMatch = LyricMatcher.TitlesMatch(name, title) &&
                            (string.IsNullOrEmpty(artist) || LyricMatcher.ArtistsMatch(LyricMatcher.NeteaseArtists(song), artist)),
                        Score = LyricMatcher.ScoreTrack(title, artist, album, name, artistName, albumName, aliases),
                        Order = order++, Format = LyricFormat.Lrc
                    });
                }

                var probe = result.Where(c => c.StrictMatch).Concat(result.Where(c => !c.StrictMatch)).ToList();
                int usable = 0;
                for (int i = 0; i < probe.Count && usable < 6; i += 3)
                {
                    var batch = probe.Skip(i).Take(3).ToList();
                    await Task.WhenAll(batch.Select(LoadNeteaseLyric));
                    usable += batch.Count(c => !string.IsNullOrEmpty(c.LyricText));
                }
            }
            catch { }
            return result;
        }

        private static async Task LoadNeteaseLyric(LyricCandidate c)
        {
            try
            {
                string json = await GetAsync("https://music.163.com/api/song/lyric?id=" + c.SongId + "&lv=1&kv=1&tv=-1");
                if (string.IsNullOrEmpty(json)) return;
                var lrc = JsonObject.Parse(json).GetNamedObject("lrc", null);
                if (lrc == null || !lrc.ContainsKey("lyric")) return;
                string text = lrc.GetNamedString("lyric", "");
                if (string.IsNullOrEmpty(text)) return;
                c.LyricText = text;
                c.Format = LyricsParser.DetectFormat(text);
            }
            catch { }
        }

        private static async Task<List<LyricCandidate>> CollectQq(string artist, string title, string album)
        {
            var result = new List<LyricCandidate>();
            try
            {
                JsonArray list = null;
                foreach (string q in LyricMatcher.BuildSearchQueries(artist, title, album))
                {
                    string json = await GetAsync("https://c.y.qq.com/soso/fcgi-bin/client_search_cp?p=1&n=10&w=" +
                        Uri.EscapeDataString(q) + "&format=json");
                    if (string.IsNullOrEmpty(json)) continue;
                    var data = JsonObject.Parse(json).GetNamedObject("data", null);
                    var song = data?.GetNamedObject("song", null);
                    list = song?.GetNamedArray("list", null);
                    if (list != null && list.Count > 0) break;
                }
                if (list == null) return result;

                int order = 0;
                var seen = new HashSet<string>();
                for (uint i = 0; i < list.Count; i++)
                {
                    var item = list.GetObjectAt(i);
                    if (!item.ContainsKey("songmid")) continue;
                    string mid = item.GetNamedString("songmid", "");
                    if (!seen.Add(mid)) continue;
                    string name = item.GetNamedString("songname", "");
                    string artistName = "";
                    string albumName = "";
                    try
                    {
                        var singers = item.GetNamedArray("singer", null);
                        if (singers != null && singers.Count > 0) artistName = singers.GetObjectAt(0).GetNamedString("name", "");
                        if (item.ContainsKey("albumname")) albumName = item.GetNamedString("albumname", "");
                    }
                    catch { }

                    result.Add(new LyricCandidate
                    {
                        Source = "QQ", SongId = mid, Title = name, Artist = artistName,
                        Album = albumName,
                        StrictMatch = LyricMatcher.TitlesMatch(name, title) &&
                            (string.IsNullOrEmpty(artist) || LyricMatcher.ArtistsMatch(LyricMatcher.QqArtists(item), artist)),
                        Score = LyricMatcher.ScoreTrack(title, artist, album, name, artistName, albumName, null),
                        Order = order++, Format = LyricFormat.Lrc
                    });
                }

                var probe = result.Where(c => c.StrictMatch).Concat(result.Where(c => !c.StrictMatch)).ToList();
                int usable = 0;
                for (int i = 0; i < probe.Count && usable < 6; i += 3)
                {
                    var batch = probe.Skip(i).Take(3).ToList();
                    await Task.WhenAll(batch.Select(LoadQqLyric));
                    usable += batch.Count(c => !string.IsNullOrEmpty(c.LyricText));
                }
            }
            catch { }
            return result;
        }

        private static async Task LoadQqLyric(LyricCandidate c)
        {
            try
            {
                var headers = new Dictionary<string, string> { { "Referer", "https://y.qq.com" } };
                string json = await GetAsync("https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid=" +
                    c.SongId + "&format=json", headers);
                if (string.IsNullOrEmpty(json) || !JsonObject.Parse(json).ContainsKey("lyric")) return;
                string base64 = JsonObject.Parse(json).GetNamedString("lyric", "");
                if (string.IsNullOrEmpty(base64)) return;
                byte[] bytes = Convert.FromBase64String(base64);
                string text = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                if (string.IsNullOrEmpty(text)) return;
                c.LyricText = text;
                c.Format = LyricsParser.DetectFormat(text);
            }
            catch { }
        }
    }
}
