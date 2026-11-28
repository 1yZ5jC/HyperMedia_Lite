using HyperMedia_Lite.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;

namespace HyperMedia_Lite.Services
{
    /// <summary>
    /// 播放统计的本地持久化：播放次数 / 跳过次数 / 最后播放时间 / 评分。
    /// 自动记录默认关闭，由用户在设置页开启。
    /// </summary>
    public static class PlayStatsService
    {
        private const string Key = "PlayStats";
        private static Dictionary<string, PlayStats> _data;

        public sealed class PlayStats
        {
            public int PlayCount;
            public int SkipCount;
            public string LastPlayed;
            public uint Rating;
        }

        private static Dictionary<string, PlayStats> Data
        {
            get
            {
                if (_data != null) return _data;
                _data = new Dictionary<string, PlayStats>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string raw = ApplicationData.Current.LocalSettings.Values[Key] as string;
                    if (!string.IsNullOrEmpty(raw))
                    {
                        foreach (string line in raw.Split('\n'))
                        {
                            string[] parts = line.Split('|');
                            if (parts.Length < 4) continue;
                            if (!int.TryParse(parts[1], out int plays)) plays = 0;
                            if (!int.TryParse(parts[2], out int skips)) skips = 0;
                            uint.TryParse(parts[3], out uint rating);
                            _data[parts[0]] = new PlayStats
                            {
                                PlayCount = plays,
                                SkipCount = skips,
                                Rating = rating,
                                LastPlayed = parts.Length > 4 ? parts[4] : string.Empty
                            };
                        }
                    }
                }
                catch { }
                return _data;
            }
        }

        public static PlayStats Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return Data.TryGetValue(path, out var value) ? value : null;
        }

        public static void Apply(MediaItem item)
        {
            if (item == null) return;
            var stats = Get(item.Path);
            if (stats == null) return;
            item.PlayCount = stats.PlayCount;
            item.SkipCount = stats.SkipCount;
            item.LastPlayed = stats.LastPlayed;
            if (stats.Rating > 0) item.Rating = stats.Rating;
        }

        public static void RecordPlay(MediaItem item)
        {
            if (item == null) return;
            var stats = Get(item.Path) ?? new PlayStats();
            stats.PlayCount++;
            stats.LastPlayed = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm");
            stats.Rating = item.Rating;
            Data[item.Path] = stats;
            Apply(item);
            Save();
        }

        public static void SetRating(MediaItem item, uint rating)
        {
            if (item == null) return;
            var stats = Get(item.Path) ?? new PlayStats();
            stats.Rating = rating;
            Data[item.Path] = stats;
            item.Rating = rating;
            Save();
        }

        private static void Save()
        {
            try
            {
                string raw = string.Join("\n", Data.Select(kv =>
                    kv.Key.Replace('|', '_') + "|" + kv.Value.PlayCount + "|" + kv.Value.SkipCount
                    + "|" + kv.Value.Rating + "|" + (kv.Value.LastPlayed ?? string.Empty)));
                ApplicationData.Current.LocalSettings.Values[Key] = raw;
            }
            catch { }
        }
    }
}
