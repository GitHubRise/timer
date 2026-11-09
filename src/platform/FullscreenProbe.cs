// 全屏判定的诊断记录器：判定结果只在这里落盘，不参与任何显示逻辑。
// NativeMethods.IsFullscreenForeground 每次轮询都会调用 Report，
// 但只有“结果 + 前台窗口”签名变化时才真正追加一行，避免 1 秒轮询把日志写爆。
// 日志路径固定为 %APPDATA%\GameClock\fullscreen.log，UTF-8 带 BOM，只追加不覆盖。
// 约束：任何 IO 异常都必须吞掉——写日志失败不能影响全屏隐藏本身。

using System;
using System.IO;
using System.Text;

namespace GameClock
{

    /// <summary>
    /// 全屏判断的轻量诊断记录：只在“前台窗口 / 判定结果”发生变化时追加一行，
    /// 方便在真实全屏场景里回看判定依据，不做任何轮询或弹窗。
    /// 日志位置：%APPDATA%\GameClock\fullscreen.log
    /// 调用方：<see cref="NativeMethods.IsFullscreenForeground"/>，在每次判定结束时调用一次。
    /// </summary>
    internal static class FullscreenProbe
    {

        /// <summary>写文件用的锁：判定在 UI 线程轮询，仍用它保证“比较签名 + 写盘”是原子的。</summary>
        private static readonly object Gate = new object();

        /// <summary>上一次写入的签名；相同则跳过写入。静态字段跨调用保留，作为去重依据。</summary>
        private static string _lastSignature;

        /// <summary>
        /// 按需追加一行诊断日志。
        /// <paramref name="signature"/> 与上次相同则直接返回（判定没变化，不重复记录）；
        /// <paramref name="detail"/> 是给人看的明细（命中原因、进程、类名、窗口矩形）。
        /// 无返回值、不抛异常：写盘失败时签名已更新，下一次变化才会再尝试。
        /// </summary>
        public static void Report(string signature, string detail)
        {
            try
            {
                // 锁住整段流程，避免两处同时判定时写出重复行或写坏同一行。
                lock (Gate)
                {
                    // 轮询期间绝大多数调用签名不变，这条快路径保证日志只在状态切换时增长。
                    if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
                    {
                        return;
                    }

                    // 先记签名再写盘：即使下面抛异常，也不会在同一次变化上反复重试刷屏。
                    _lastSignature = signature;

                    // 日志目录跟随用户漫游配置，与 settings.json 同目录，便于整体打包排查。
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "GameClock");
                    Directory.CreateDirectory(dir);

                    // 每行：毫秒级本地时间 + 两个空格 + 明细，方便和用户描述的现场时间对齐。
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        + "  "
                        + detail
                        + Environment.NewLine;
                    // UTF8Encoding(true) 让文件首行带 BOM，记事本等工具才能正确识别为 UTF-8。
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
