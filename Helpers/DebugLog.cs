using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyperMedia_Lite.Helpers
{
    /// <summary>
    /// 轻量日志：输出到 VS 输出窗口，并异步写入 LocalFolder\debug.log（最多保留 ~256KB）。
    /// Debug 构建默认开启；Release 默认关闭，可在设置页打开。
    /// </summary>
    public static class DebugLog
    {
        public static bool Enabled { get; set; } =
#if DEBUG
            true;
#else
            false;
#endif

        private const string LogFileName = "debug.log";
        private const long MaxLogBytes = 256 * 1024;

        // 多条日志并发写同一文件会抛 0x80070020（文件正在使用），必须串行化。
        private static readonly SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);
        public static void Write(string tag, string message)
        {
            string line = "[" + tag + "] " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message;
            Debug.WriteLine(line);
            if (Enabled)
            {
                QueueFileWrite(line);
            }
        }

        /// <summary>
        /// 同步写入关键日志（启动/崩溃）。在线程池线程上完成 WinRT 异步 I/O 并等待结果，
        /// 保证进程立即崩溃时日志也已落盘。
        /// </summary>
        public static void WriteStartupStep(string step)
        {
            string line = "[START] " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + step;
            Debug.WriteLine(line);
            try
            {
                Task.Run(async () =>
                {
                    await WriteLock.WaitAsync();
                    try
                    {
                        var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                            LogFileName, CreationCollisionOption.OpenIfExists);
                        string existing = await FileIO.ReadTextAsync(file);
                        if (existing.Length > MaxLogBytes) existing = string.Empty;
                        await FileIO.WriteTextAsync(file, existing + line + Environment.NewLine);
                    }
                    catch { }
                    finally { WriteLock.Release(); }
                }).GetAwaiter().GetResult();
            }
            catch { }
        }

        /// <summary>无论开关如何都写入文件（用于启动/崩溃等关键日志）。</summary>
        public static void WriteAlways(string tag, string message)
        {
            string line = "[" + tag + "] " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message;
            Debug.WriteLine(line);
            QueueFileWrite(line);
        }

        private static void QueueFileWrite(string line)
        {
            _ = Task.Run(async () =>
            {
                await WriteLock.WaitAsync();
                try
                {
                    var folder = ApplicationData.Current.LocalFolder;
                    var file = await folder.CreateFileAsync(LogFileName, CreationCollisionOption.OpenIfExists);

                    string existing = await FileIO.ReadTextAsync(file);
                    if (existing.Length > MaxLogBytes)
                    {
                        existing = string.Empty;
                    }
                    await FileIO.WriteTextAsync(file, existing + line + Environment.NewLine);
                }
                catch (Exception)
                {
                    // 日志失败不影响程序运行
                }
                finally
                {
                    WriteLock.Release();
                }
            });
        }

        /// <summary>清空日志文件。</summary>
        public static async Task ClearAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(LogFileName) as StorageFile;
                if (file != null)
                {
                    await FileIO.WriteTextAsync(file, string.Empty);
                }
            }
            catch { }
        }

        /// <summary>读取日志文件全部内容。</summary>
        public static async Task<string> ReadAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(LogFileName) as StorageFile;
                return file == null ? string.Empty : await FileIO.ReadTextAsync(file);
            }
            catch { return string.Empty; }
        }
    }
}
