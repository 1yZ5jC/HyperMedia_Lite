using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyperMedia_Lite
{
    // ───────────────────────── Models ─────────────────────────

    public sealed class LyricLine : HyperMedia_Lite.Helpers.BindableBase
    {
        public double TimeSeconds { get; set; }
        public string Text { get; set; }
        public List<LyricWord> Words { get; set; }

        private bool _isCurrent;
        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetProperty(ref _isCurrent, value);
        }
    }

    public sealed class LyricWord
    {
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }
        public string Text { get; set; }
    }

    public enum LyricFormat { Unknown, Lrc, Krc, Qrc }

    // ───────────────────────── Parser ─────────────────────────

    /// <summary>
    /// LRC / KRC / QRC 解析 + MP4/M4A 内嵌歌词提取（移植自 HyperMedia）。
    /// 全部使用 10240 可用 API。
    /// </summary>
    public static class LyricsParser
    {
        private static readonly Regex TimestampRegex =
            new Regex(@"\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

        private static readonly Regex OffsetRegex =
            new Regex(@"\[\s*offset\s*:\s*(-?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RgxKrcLine = new Regex(@"\[\s*\d+\s*,\s*\d+\s*\]", RegexOptions.Compiled);

        private static readonly Regex RgxWordTag =
            new Regex(@"<\s*\d+:\d+(?:\.\d+)?\s*[,:]\s*\d+:\d+(?:\.\d+)?\s*>", RegexOptions.Compiled);

        private static readonly byte[] KrcKey =
        {
            64,71,70,76,77,66,75,69,50,48,52,49,53,59,57,55,
            56,54,58,51,47,45,44,43,42,41,40,39,38,37,36,35,
            34,33,32,31,30,29,28,27,26,25,24,23,22,21,20,19,
            18,17,16,15,14,13,12,11,10,9,8,7,6,5,4,3,2,1
        };

        public static LyricFormat DetectFormat(string text)
        {
            if (string.IsNullOrEmpty(text)) return LyricFormat.Unknown;
            string t = text.TrimStart('\uFEFF');
            if (RgxKrcLine.IsMatch(t)) return LyricFormat.Krc;
            if (RgxWordTag.IsMatch(t)) return LyricFormat.Qrc;
            return LyricFormat.Lrc;
        }

        public static List<LyricLine> Parse(string text) => Parse(text, DetectFormat(text));

        public static List<LyricLine> Parse(string text, LyricFormat format)
        {
            switch (format)
            {
                case LyricFormat.Krc: return ParseKrc(text);
                case LyricFormat.Qrc: return ParseQrc(text);
                default: return ParseLrc(text);
            }
        }

        public static bool TryDecryptKrc(byte[] data, out string text)
        {
            text = null;
            try
            {
                if (data == null || data.Length < 8) return false;
                if (data[0] != (byte)'k' || data[1] != (byte)'r' || data[2] != (byte)'c') return false;

                var buffer = new byte[data.Length];
                Array.Copy(data, buffer, data.Length);
                for (int i = 4; i < buffer.Length; i++)
                    buffer[i] ^= KrcKey[(i - 4) % KrcKey.Length];

                int start = buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
                text = Encoding.UTF8.GetString(buffer, start, buffer.Length - start);
                return text.IndexOf('[') >= 0;
            }
            catch { return false; }
        }

        private static List<LyricLine> ParseKrc(string text)
        {
            var result = new List<LyricLine>();
            if (string.IsNullOrEmpty(text)) return result;

            double offset = 0;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] != '[') continue;

                if (line.StartsWith("[offset:"))
                {
                    int end = line.IndexOf(']', 8);
                    if (end > 8 && double.TryParse(line.Substring(8, end - 8),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out double v)) offset = v;
                    continue;
                }
                if (line.StartsWith("[ti:") || line.StartsWith("[ar:") || line.StartsWith("[al:") ||
                    line.StartsWith("[by:") || line.StartsWith("[hash:") || line.StartsWith("[total:") ||
                    line.StartsWith("[language:") || line.StartsWith("[kst:") || line.StartsWith("[ksc:") ||
                    line.StartsWith("[kt:") || line.StartsWith("[ve:") || line.StartsWith("[sign:")) continue;

                int close = line.IndexOf(']');
                if (close <= 1) continue;
                string[] parts = line.Substring(1, close - 1).Split(',');
                if (parts.Length != 2 ||
                    !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double startMs) ||
                    !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double durMs)) continue;

                string content = line.Substring(close + 1);
                string plain = Regex.Replace(content, @"<\d+(?:\.\d+)?>", "");
                if (plain.Length == 0) continue;

                var words = new List<LyricWord>();
                double wordStart = startMs;
                foreach (Match m in Regex.Matches(content, @"([^<]+)(?:<(\d+(?:\.\d+)?)>)?"))
                {
                    string word = m.Groups[1].Value;
                    if (word.Length == 0) continue;
                    double dur = 0;
                    if (m.Groups[2].Success)
                        double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out dur);
                    words.Add(new LyricWord
                    {
                        StartSeconds = Math.Max(0, (wordStart + offset) / 1000.0),
                        EndSeconds = Math.Max(0, (wordStart + dur + offset) / 1000.0),
                        Text = word
                    });
                    wordStart += dur;
                }

                result.Add(new LyricLine
                {
                    TimeSeconds = (startMs + offset) / 1000.0,
                    Text = plain,
                    Words = words
                });
            }
            result.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            return result;
        }

        private static List<LyricLine> ParseQrc(string text)
        {
            var result = new List<LyricLine>();
            if (string.IsNullOrEmpty(text)) return result;

            double offset = 0;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("[offset:"))
                {
                    int end = line.IndexOf(']', 8);
                    if (end > 8 && double.TryParse(line.Substring(8, end - 8),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out double v)) offset = v;
                    continue;
                }

                int open = line.IndexOf('[');
                int close = line.IndexOf(']', open + 1);
                if (open < 0 || close < 0) continue;
                double lineMs = ParseLrcTimestamp(line.Substring(open + 1, close - open - 1));
                if (lineMs < 0) continue;
                string content = line.Substring(close + 1);

                var tag = RgxWordTag.Match(content);
                if (tag.Success && TryParseWordTag(tag.Value, out double ws, out double we)) lineMs = ws;
                if (lineMs >= 0) lineMs += offset;

                string plain = RgxWordTag.Replace(content, "").Trim();
                if (plain.Length == 0) continue;

                var words = new List<LyricWord>();
                foreach (Match m in Regex.Matches(content, @"(" + RgxWordTag + @")([^<]*)"))
                {
                    string word = m.Groups[2].Value;
                    if (word.Length == 0) continue;
                    if (!TryParseWordTag(m.Groups[1].Value, out double wstart, out double wend)) continue;
                    words.Add(new LyricWord
                    {
                        StartSeconds = Math.Max(0, (wstart + offset) / 1000.0),
                        EndSeconds = Math.Max(0, (wend + offset) / 1000.0),
                        Text = word
                    });
                }

                result.Add(new LyricLine
                {
                    TimeSeconds = lineMs / 1000.0,
                    Text = plain,
                    Words = words.Count > 0 ? words : null
                });
            }
            result.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            return result;
        }

        private static bool TryParseWordTag(string tag, out double start, out double end)
        {
            start = -1; end = -1;
            int sep = tag.IndexOf(',');
            if (sep < 0) sep = tag.IndexOf(':');
            if (sep < 0) return false;
            double s = ParseLrcTimestamp(tag.Substring(0, sep).Trim());
            double e = ParseLrcTimestamp(tag.Substring(sep + 1).Trim());
            if (s < 0 || e < 0 || e < s) return false;
            start = s; end = e;
            return true;
        }

        private static List<LyricLine> ParseLrc(string text)
        {
            var lines = new List<LyricLine>();
            if (string.IsNullOrWhiteSpace(text)) return lines;

            double offset = 0;
            var om = OffsetRegex.Match(text);
            if (om.Success && double.TryParse(om.Groups[1].Value, out double offMs)) offset = offMs / 1000.0;

            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                var times = new List<double>();
                foreach (Match t in TimestampRegex.Matches(raw))
                {
                    if (!int.TryParse(t.Groups[1].Value, out int min)) continue;
                    if (!int.TryParse(t.Groups[2].Value, out int sec)) continue;
                    double frac = 0;
                    if (t.Groups[3].Success && double.TryParse(t.Groups[3].Value, out double f))
                        frac = f / Math.Pow(10, t.Groups[3].Value.Length);
                    times.Add(min * 60 + sec + frac - offset);
                }
                if (times.Count == 0) continue;

                int close = raw.LastIndexOf(']');
                string content = (close >= 0 ? raw.Substring(close + 1) : raw).Trim();
                if (content.Length == 0) continue;

                foreach (double t in times)
                    lines.Add(new LyricLine { TimeSeconds = t, Text = content });
            }
            lines.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            return lines;
        }

        private static double ParseLrcTimestamp(string s)
        {
            string[] parts = s.Split(':');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) return -1;
            if (!int.TryParse(parts[0], out int min)) return -1;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double sec)) return -1;
            double ms = min * 60000 + sec * 1000;
            return ms >= 0 ? ms : -1;
        }

        public static int GetCurrentIndex(IReadOnlyList<LyricLine> lines, double position)
        {
            if (lines == null || lines.Count == 0) return -1;

            // 必须初始化为 -1：若播放位置早于第一行开始时间，不应有当前行。
            int idx = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].TimeSeconds <= position) idx = i;
                else break;
            }
            return idx;
        }

        // ───────────────────── MP4 内嵌歌词 ─────────────────────

        public static async Task<string> TryExtractEmbeddedAsync(StorageFile file)
        {
            try
            {
                if (file == null) return null;
                byte[] bytes = await ReadFileBytesAsync(file);
                return bytes == null || bytes.Length < 8 ? null : ParseMp4Lyrics(bytes);
            }
            catch { return null; }
        }

        private static async Task<byte[]> ReadFileBytesAsync(StorageFile file)
        {
            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            using (var netStream = stream.AsStreamForRead())
            {
                long size = (long)stream.Size;
                long first = Math.Min(size, 4L * 1024 * 1024);
                long last = Math.Min(size, 8L * 1024 * 1024);
                if (first + last > size) last = size - first;

                var bytes = new byte[first + last];
                int total = 0;
                while (total < first)
                {
                    int read = netStream.Read(bytes, total, (int)(first - total));
                    if (read == 0) break;
                    total += read;
                }
                if (last > 0)
                {
                    netStream.Seek(size - last, SeekOrigin.Begin);
                    while (total < bytes.Length)
                    {
                        int read = netStream.Read(bytes, total, bytes.Length - total);
                        if (read == 0) break;
                        total += read;
                    }
                }
                return bytes;
            }
        }

        private static string ParseMp4Lyrics(byte[] data)
        {
            int pos = 0;
            return FindAtom(data, ref pos, data.Length, new[] { "moov", "udta", "meta", "ilst" }, 0);
        }

        private static string FindAtom(byte[] data, ref int pos, int end, string[] path, int depth)
        {
            if (depth >= path.Length) return null;
            string target = path[depth];
            while (pos + 8 <= end)
            {
                int size = ReadInt32BigEndian(data, pos);
                if (size == 1 && pos + 16 <= end) size = (int)ReadInt64BigEndian(data, pos + 8);
                if (size < 8 || pos + size > end) break;

                string type = DecodeAtomType(data, pos + 4);
                int contentStart = pos + 8;
                int contentEnd = pos + size;

                if (type == target)
                {
                    if (depth == path.Length - 1)
                    {
                        string found = SearchIlst(data, contentStart, contentEnd);
                        if (!string.IsNullOrEmpty(found)) return found;
                    }
                    else
                    {
                        int child = contentStart + (type == "meta" ? 4 : 0);
                        string found = FindAtom(data, ref child, contentEnd, path, depth + 1);
                        if (!string.IsNullOrEmpty(found)) return found;
                    }
                }
                pos += size;
            }
            return null;
        }

        private static string SearchIlst(byte[] data, int start, int end)
        {
            int pos = start;
            while (pos + 8 <= end)
            {
                int size = ReadInt32BigEndian(data, pos);
                if (size == 1 && pos + 16 <= end) size = (int)ReadInt64BigEndian(data, pos + 8);
                if (size < 8 || pos + size > end) break;

                bool isLyric =
                    (data[pos + 4] == 0xA9 && data[pos + 5] == 0x6C && data[pos + 6] == 0x79 && data[pos + 7] == 0x72) ||
                    (data[pos + 4] == 0x6C && data[pos + 5] == 0x79 && data[pos + 6] == 0x72 && data[pos + 7] == 0x63);
                if (isLyric)
                {
                    string text = ExtractDataAtomText(data, pos + 8, pos + size);
                    if (!string.IsNullOrEmpty(text)) return text;
                }
                pos += size;
            }
            return null;
        }

        private static string ExtractDataAtomText(byte[] data, int start, int atomEnd)
        {
            int pos = start;
            while (pos + 8 <= atomEnd)
            {
                int size = ReadInt32BigEndian(data, pos);
                if (size == 1 && pos + 16 <= atomEnd) size = (int)ReadInt64BigEndian(data, pos + 8);
                if (size < 8 || pos + size > atomEnd) break;

                if (data[pos + 4] == 0x64 && data[pos + 5] == 0x61 && data[pos + 6] == 0x74 && data[pos + 7] == 0x61)
                {
                    int textStart = pos + 8 + 8;
                    if (textStart < pos + size)
                    {
                        string text = Encoding.UTF8.GetString(data, textStart, pos + size - textStart).TrimEnd('\0');
                        if (!string.IsNullOrEmpty(text)) return text;
                    }
                }
                pos += size;
            }
            return null;
        }

        private static string DecodeAtomType(byte[] data, int offset) =>
            new string(new[] { (char)data[offset], (char)data[offset + 1], (char)data[offset + 2], (char)data[offset + 3] });

        private static long ReadInt64BigEndian(byte[] data, int offset)
        {
            if (offset + 8 > data.Length) return 0;
            return ((long)data[offset] << 56) | ((long)data[offset + 1] << 48) |
                   ((long)data[offset + 2] << 40) | ((long)data[offset + 3] << 32) |
                   ((long)data[offset + 4] << 24) | ((long)data[offset + 5] << 16) |
                   ((long)data[offset + 6] << 8) | data[offset + 7];
        }

        private static int ReadInt32BigEndian(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }
    }
}
