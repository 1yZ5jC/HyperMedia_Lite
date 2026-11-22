using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using HyperMedia_Lite.Models;
using Windows.Storage;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 字幕加载与自动匹配（不依赖 VLC / 平台字幕引擎）：
    /// 同名外挂字幕自动发现 + 自绘字幕层数据源。
    /// </summary>
    public static class SubtitleService
    {
        private static readonly string[] Extensions = { ".srt", ".vtt", ".ass", ".ssa" };

        /// <summary>查找与视频同名的字幕文件，找到第一个即返回。</summary>
        public static async Task<StorageFile> FindSiblingAsync(StorageFile videoFile)
        {
            try
            {
                var folder = await videoFile.GetParentAsync();
                if (folder == null) return null;
                string baseName = Path.GetFileNameWithoutExtension(videoFile.Name);
                foreach (string ext in Extensions)
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
            catch { }
            return null;
        }

        public static async Task<List<SubtitleLine>> LoadAsync(StorageFile file)
        {
            var result = new List<SubtitleLine>();
            if (file == null) return result;
            try
            {
                string content = await ReadTextBestEffortAsync(file);
                if (string.IsNullOrEmpty(content)) return result;
                return SubtitleParser.Parse(content, file.FileType);
            }
            catch { return result; }
        }

        private static async Task<string> ReadTextBestEffortAsync(StorageFile file)
        {
            try
            {
                return await FileIO.ReadTextAsync(file);
            }
            catch { }

            try
            {
                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                using (var netStream = stream.AsStreamForRead())
                using (var memory = new MemoryStream())
                {
                    await netStream.CopyToAsync(memory);
                    byte[] bytes = memory.ToArray();
                    var encodings = new List<Encoding>
                    {
                        new UTF8Encoding(true, false), Encoding.Unicode, Encoding.BigEndianUnicode
                    };
                    try { encodings.Add(Encoding.GetEncoding("GB2312")); } catch { }
                    foreach (var encoding in encodings)
                    {
                        try
                        {
                            string text = encoding.GetString(bytes).TrimStart('\uFEFF');
                            if (text.IndexOf(':') > 0 || text.IndexOf("-->", StringComparison.Ordinal) > 0)
                                return text;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
