using System;
using System.Collections.Generic;

namespace HyperMedia_Lite.Models
{
    /// <summary>本地播放列表（仅保存媒体路径）。</summary>
    public sealed class Playlist
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新建播放列表";
        public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
        public List<string> ItemPaths { get; set; } = new List<string>();

        public int Count => ItemPaths?.Count ?? 0;

        public string CountText => Count == 0 ? "空列表" : Count + " 项";
    }
}
