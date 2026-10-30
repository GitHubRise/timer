using System;
using System.IO;
using System.Text;

namespace GameClock
{
    /// <summary>
    /// 全屏判断的轻量诊断记录：只在“前台窗口 / 判定结果”发生变化时追加一行，
    /// 方便在真实全屏场景里回看判定依据，不做任何轮询或弹窗。
    /// 日志位置：%APPDATA%\GameClock\fullscreen.log
    /// </summary>
    internal static class FullscreenProbe
    {
        private static readonly object Gate = new object();
        private static string _lastSignature;

        public static void Report(string signature, string detail)
        {
            try
            {
                lock (Gate)
                {
                    if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _lastSignature = signature;

                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "GameClock");
                    Directory.CreateDirectory(dir);

                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        + "  "
                        + detail
                        + Environment.NewLine;
                    File.AppendAllText(Path.Combine(dir, "fullscreen.log"), line, new UTF8Encoding(true));
                }
            }
            catch
            {
                // 诊断日志失败不得影响全屏隐藏本身。
            }
        }
    }
}
