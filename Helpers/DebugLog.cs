using System;
using System.Diagnostics;

namespace HyperMedia_Lite.Helpers
{
    /// <summary>轻量调试日志开关：Debug 构建默认开启，Release 可在设置中打开。</summary>
    public static class DebugLog
    {
        public static bool Enabled { get; set; } =
#if DEBUG
            true;
#else
            false;
#endif

        public static void Write(string tag, string message)
        {
            if (!Enabled) return;
            Debug.WriteLine("[" + tag + "] " + DateTimeOffset.Now.ToString("HH:mm:ss.fff") + " " + message);
        }
    }
}
