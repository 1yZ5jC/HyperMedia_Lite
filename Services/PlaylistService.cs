using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HyperMedia_Lite.Models;
using Windows.Data.Json;
using Windows.Storage;

namespace HyperMedia_Lite.Services
{
    /// <summary>播放列表本地持久化（LocalFolder\playlists.json，Windows.Data.Json，10240 可用）。</summary>
    public sealed class PlaylistService
    {
        private const string FileName = "playlists.json";

        public List<Playlist> Playlists { get; } = new List<Playlist>();

        public async Task LoadAsync()
        {
            Playlists.Clear();
            try
            {
                var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(FileName) as StorageFile;
                if (file == null) return;

                string text = await FileIO.ReadTextAsync(file);
                var array = JsonArray.Parse(text);
                for (uint i = 0; i < array.Count; i++)
                {
                    var obj = array.GetObjectAt(i);
                    var playlist = new Playlist
                    {
                        Id = obj.GetNamedString("id", Guid.NewGuid().ToString("N")),
                        Name = obj.GetNamedString("name", "播放列表")
                    };

                    var items = obj.GetNamedArray("items", null);
                    if (items != null)
                    {
                        for (uint j = 0; j < items.Count; j++)
                        {
                            string path = items.GetStringAt(j);
                            if (!string.IsNullOrEmpty(path)) playlist.ItemPaths.Add(path);
                        }
                    }
                    Playlists.Add(playlist);
                }
            }
            catch (Exception)
            {
                // 文件损坏时以空列表启动。
            }
        }

        public async Task SaveAsync()
        {
            try
            {
                var array = new JsonArray();
                foreach (var playlist in Playlists)
                {
                    var obj = new JsonObject();
                    obj.SetNamedValue("id", JsonValue.CreateStringValue(playlist.Id));
                    obj.SetNamedValue("name", JsonValue.CreateStringValue(playlist.Name ?? "播放列表"));

                    var items = new JsonArray();
                    foreach (string path in playlist.ItemPaths)
                    {
                        items.Add(JsonValue.CreateStringValue(path));
                    }
                    obj.SetNamedValue("items", items);
                    array.Add(obj);
                }

                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    FileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, array.Stringify());
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public async Task<Playlist> CreateAsync(string name)
        {
            var playlist = new Playlist
            {
                Name = string.IsNullOrWhiteSpace(name) ? "新建播放列表" : name.Trim()
            };
            Playlists.Add(playlist);
            await SaveAsync();
            return playlist;
        }

        public async Task DeleteAsync(Playlist playlist)
        {
            if (playlist == null) return;
            Playlists.Remove(playlist);
            await SaveAsync();
        }

        public async Task RenameAsync(Playlist playlist, string newName)
        {
            if (playlist == null || string.IsNullOrWhiteSpace(newName)) return;
            playlist.Name = newName.Trim();
            await SaveAsync();
        }

        public async Task ToggleItemAsync(Playlist playlist, string path)
        {
            if (playlist == null || string.IsNullOrEmpty(path))
            {
                return;
            }

            bool removed = playlist.ItemPaths.Remove(path);
            if (!removed)
            {
                playlist.ItemPaths.Add(path);
            }
            await SaveAsync();
        }

        public Playlist Find(string id) => Playlists.FirstOrDefault(p => p.Id == id);
    }
}
