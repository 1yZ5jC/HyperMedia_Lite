using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HyperMedia_Lite
{
    /// <summary>一条自绘字幕（时间单位：秒）。</summary>
    public sealed class SubtitleLine
    {
        public double Start;
        public double End;
        public string Text = "";
        public bool Bold;
        public bool Italic;
        public double FontSize = 28;
        public string ColorHex = "#FFFFFFFF";
        public int Alignment = 2;
    }

    /// <summary>
    /// SRT / VTT / ASS / SSA 字幕解析（自绘字幕层使用，不依赖平台字幕引擎）。
    /// 全程不抛异常，解析失败只返回较少条目。
    /// </summary>
    public static class SubtitleParser
    {
        public static List<SubtitleLine> Parse(string content, string extension)
        {
            if (string.IsNullOrEmpty(content)) return new List<SubtitleLine>();
            if (string.Equals(extension, ".ass", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".ssa", StringComparison.OrdinalIgnoreCase))
                return ParseAss(content);
            return ParseSrt(content);
        }

        // ───────────────────── SRT / WebVTT ─────────────────────

        private static readonly Regex TimeRegex =
            new Regex(@"(\d{1,3}):(\d{2}):(\d{2})[.,](\d{1,3})\s*-->\s*(\d{1,3}):(\d{2}):(\d{2})[.,](\d{1,3})");

        public static List<SubtitleLine> ParseSrt(string content)
        {
            var result = new List<SubtitleLine>();
            try
            {
                string[] blocks = Regex.Split(content.Replace("\r\n", "\n").Replace('\r', '\n'), @"\n\s*\n");
                foreach (string block in blocks)
                {
                    var match = TimeRegex.Match(block);
                    if (!match.Success) continue;

                    double start = ParseSrtTime(match.Groups[1].Value, match.Groups[2].Value,
                        match.Groups[3].Value, match.Groups[4].Value);
                    double end = ParseSrtTime(match.Groups[5].Value, match.Groups[6].Value,
                        match.Groups[7].Value, match.Groups[8].Value);
                    if (start < 0 || end <= start) continue;

                    string text = block.Substring(match.Index + match.Length);
                    text = Regex.Replace(text, @"\{\\[^}]*\}", ""); // VTT 内联标签
                    text = text.Replace("<c>", "").Replace("</c>", "").Trim();
                    if (text.Length == 0) continue;

                    result.Add(new SubtitleLine { Start = start, End = end, Text = text });
                }
                result.Sort((a, b) => a.Start.CompareTo(b.Start));
            }
            catch { }
            return result;
        }

        private static double ParseSrtTime(string h, string m, string s, string fraction)
        {
            try
            {
                int hh = int.Parse(h, CultureInfo.InvariantCulture);
                int mm = int.Parse(m, CultureInfo.InvariantCulture);
                int ss = int.Parse(s, CultureInfo.InvariantCulture);
                double frac = double.Parse(fraction, CultureInfo.InvariantCulture) / Math.Pow(10, fraction.Length);
                return hh * 3600 + mm * 60 + ss + frac;
            }
            catch { return -1; }
        }

        // ───────────────────── ASS / SSA ─────────────────────

        public static List<SubtitleLine> ParseAss(string content)
        {
            var result = new List<SubtitleLine>();
            try
            {
                string section = "";
                string[] styleFormat = null;
                string[] eventFormat = null;
                double styleFontSize = -1;
                string styleColor = null;

                foreach (string rawLine in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0) continue;
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                        continue;
                    }

                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string key = line.Substring(0, colon).Trim().ToLowerInvariant();
                    string value = line.Substring(colon + 1).Trim();

                    if (section.Contains("style"))
                    {
                        if (key == "format") styleFormat = SplitFields(value);
                        else if (key == "style" && (styleFontSize <= 0 || styleColor == null))
                            ApplyStyle(value, styleFormat, ref styleFontSize, ref styleColor);
                    }
                    else if (section.Contains("event"))
                    {
                        if (key == "format") eventFormat = SplitFields(value);
                        else if (key == "dialogue")
                        {
                            var parsed = ParseDialogue(value, eventFormat,
                                styleFontSize > 0 ? styleFontSize : 28, styleColor);
                            if (parsed != null) result.Add(parsed);
                        }
                    }
                }
                result.Sort((a, b) => a.Start.CompareTo(b.Start));
            }
            catch { }
            return result;
        }

        private static string[] SplitFields(string value)
        {
            try
            {
                string[] parts = value.Split(',');
                for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim().ToLowerInvariant();
                return parts;
            }
            catch { return null; }
        }

        private static int FieldIndex(string[] format, string name)
        {
            if (format == null) return -1;
            for (int i = 0; i < format.Length; i++) if (format[i] == name) return i;
            return -1;
        }

        private static void ApplyStyle(string value, string[] format, ref double fontSize, ref string color)
        {
            try
            {
                string[] fields = value.Split(',');
                int fs = FieldIndex(format, "fontsize");
                if (fontSize <= 0 && fs >= 0 && fs < fields.Length &&
                    double.TryParse(fields[fs].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0)
                    fontSize = v;

                int pc = FieldIndex(format, "primarycolour") >= 0
                    ? FieldIndex(format, "primarycolour") : FieldIndex(format, "primarycolor");
                if (color == null && pc >= 0 && pc < fields.Length) color = AssColorToHex(fields[pc]);
            }
            catch { }
        }

        private static SubtitleLine ParseDialogue(string value, string[] format, double defSize, string defColor)
        {
            try
            {
                int total = format != null ? format.Length : 10;
                int startIndex = format != null ? FieldIndex(format, "start") : 1;
                int endIndex = format != null ? FieldIndex(format, "end") : 2;
                int textIndex = format != null ? FieldIndex(format, "text") : 9;
                if (total <= 0 || startIndex < 0 || endIndex < 0 || textIndex < 0 || textIndex >= total) return null;

                string[] fields = value.Split(new[] { ',' }, total);
                if (fields.Length < total) return null;
                if (!TryAssTime(fields[startIndex], out double start)) return null;
                if (!TryAssTime(fields[endIndex], out double end)) end = start + 1;

                var line = new SubtitleLine { Start = start, End = Math.Max(start, end) };
                string text = ProcessText(string.Join(",", fields, textIndex, fields.Length - textIndex),
                    line, defSize, defColor);
                line.Text = text.Replace("\\N", "\n").Replace("\\n", "\n").TrimEnd();
                return line.Text.Length == 0 ? null : line;
            }
            catch { return null; }
        }

        private static string ProcessText(string raw, SubtitleLine line, double defSize, string defColor)
        {
            var sb = new StringBuilder(raw.Length);
            bool bold = false, italic = false;
            double size = -1;
            string color = null;
            bool snapshot = false;
            int i = 0;
            while (i < raw.Length)
            {
                char c = raw[i];
                if (c == '{')
                {
                    int close = raw.IndexOf('}', i + 1);
                    int end = close >= 0 ? close : raw.Length;
                    ApplyOverride(raw.Substring(i + 1, end - i - 1), ref bold, ref italic, ref size, ref color);
                    i = end + 1;
                    continue;
                }
                if (!snapshot && !char.IsWhiteSpace(c))
                {
                    snapshot = true;
                    line.Bold = bold;
                    line.Italic = italic;
                    line.FontSize = size > 0 ? size : defSize;
                    line.ColorHex = color ?? defColor ?? "#FFFFFFFF";
                }
                sb.Append(c);
                i++;
            }
            if (!snapshot)
            {
                line.Bold = bold;
                line.Italic = italic;
                line.FontSize = size > 0 ? size : defSize;
                line.ColorHex = color ?? defColor ?? "#FFFFFFFF";
            }
            return sb.ToString();
        }

        private static void ApplyOverride(string body, ref bool bold, ref bool italic, ref double size, ref string color)
        {
            try
            {
                int i = 0;
                while (i < body.Length)
                {
                    if (body[i] != '\\') { i++; continue; }
                    int next = body.IndexOf('\\', i + 1);
                    if (next < 0) next = body.Length;
                    string tag = body.Substring(i + 1, next - i - 1).Trim().ToLowerInvariant();
                    if (tag == "b1") bold = true;
                    else if (tag == "b0") bold = false;
                    else if (tag == "i1") italic = true;
                    else if (tag == "i0") italic = false;
                    else if (tag.StartsWith("fs") && double.TryParse(tag.Substring(2), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double v) && v > 0) size = v;
                    else if ((tag.StartsWith("c&h") || tag.StartsWith("1c&h")))
                    {
                        int amp = tag.IndexOf("&h");
                        if (amp >= 0) color = AssColorToHex(tag.Substring(amp + 2));
                    }
                    i = next;
                }
            }
            catch { }
        }

        private static string AssColorToHex(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value)) return null;
                string hex = value.Trim().TrimStart('&').TrimStart('H', 'h').TrimEnd('&');
                if (hex.Length > 6) hex = hex.Substring(hex.Length - 6);
                if (hex.Length != 6) return null;
                foreach (char ch in hex)
                {
                    bool isHex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
                    if (!isHex) return null;
                }
                return "#" + hex.Substring(4, 2) + hex.Substring(2, 2) + hex.Substring(0, 2);
            }
            catch { return null; }
        }

        private static bool TryAssTime(string text, out double seconds)
        {
            seconds = 0;
            try
            {
                string[] parts = text.Split('.');
                string[] hms = parts[0].Split(':');
                if (hms.Length != 3) return false;
                int h = int.Parse(hms[0].Trim(), CultureInfo.InvariantCulture);
                int m = int.Parse(hms[1].Trim(), CultureInfo.InvariantCulture);
                int s = int.Parse(hms[2].Trim(), CultureInfo.InvariantCulture);
                int cs = parts.Length > 1 ? int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture) : 0;
                seconds = h * 3600 + m * 60 + s + cs / 100.0;
                return true;
            }
            catch { return false; }
        }

        public static int FindCurrentIndex(IReadOnlyList<SubtitleLine> lines, double position)
        {
            if (lines == null || lines.Count == 0) return -1;
            int lo = 0, hi = lines.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (lines[mid].Start <= position) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best >= 0 && position < lines[best].End ? best : -1;
        }
    }
}
