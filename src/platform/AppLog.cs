using System;
using System.IO;
using System.Text;

namespace GameClock
{
    /// <summary>
    /// 运行日志：统一写 %APPDATA%\GameClock\app.log，单文件超过 1 MB 滚动备份 3 份；
    /// Error 级别除写 app.log 外，额外把堆栈追加到 error.log 供用户反馈时提供。
    /// 设计约束：
    /// 1. 只在状态跃迁、外部副作用（配置/注册表/网络/系统窗口操作）与失败回退处记录；
    ///    每秒 tick、每次重绘、每次滚动等高频路径不得调用，避免日志淹没真正线索。
    /// 2. 写日志自身失败必须静默：日志是观测手段，不得反过来中断主流程。
    /// 3. 消息用中文，模块名用英文短标识（Program / Settings / Weather / Taskbar / Clock 等），
    ///    因为模块名要能直接 grep 源码目录。
    /// </summary>
    internal static class AppLog
    {
        private const string AppLogName = "app.log";
        private const string ErrorLogName = "error.log";

        /// <summary>单个日志文件的大小上限：1 MB。</summary>
        private const long MaxBytes = 1024L * 1024L;

        /// <summary>滚动保留的备份份数：app.1.log ~ app.3.log。</summary>
        private const int BackupCount = 3;

        /// <summary>写文件串行化；日志可能被 UI 线程与后台天气线程同时写入。</summary>
        private static readonly object Gate = new object();

        /// <summary>当前运行日志路径（%APPDATA%\GameClock\app.log）。</summary>
        public static string LogPath
        {
            get { return Path.Combine(SettingsStore.FolderPath, AppLogName); }
        }

        /// <summary>错误日志路径（%APPDATA%\GameClock\error.log），供错误对话框展示给用户。</summary>
        public static string ErrorLogPath
        {
            get { return Path.Combine(SettingsStore.FolderPath, ErrorLogName); }
        }

        /// <summary>记录常规流程节点，如启动、设置应用、显示面切换。</summary>
        public static void Info(string module, string message)
        {
            Write("INFO", module, message, null);
        }

        /// <summary>记录可恢复的异常情况，如网络失败、注册表被拒、配置回退。</summary>
        public static void Warn(string module, string message)
        {
            Write("WARN", module, message, null);
        }

        /// <summary>
        /// 记录可恢复异常并附异常对象：警告本身不影响可用性（有回退路径），
        /// 但堆栈对排障有价值，因此与 Error 一样同时写入 error.log。
        /// </summary>
        public static void Warn(string module, string message, Exception exception)
        {
            Write("WARN", module, message, exception);
        }

        /// <summary>记录失败并附异常；堆栈同时进入 error.log，保持与错误对话框提示一致。</summary>
        public static void Error(string module, string message, Exception exception)
        {
            Write("ERROR", module, message, exception);
        }

        private static void Write(string level, string module, string message, Exception exception)
        {
            try
            {
                string line = BuildLine(level, module, message, exception);
                lock (Gate)
                {
                    Directory.CreateDirectory(SettingsStore.FolderPath);
                    RotateIfNeeded();
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                    if (exception != null)
                    {
                        File.AppendAllText(ErrorLogPath, line, Encoding.UTF8);
                    }
                }
            }
            catch (Exception)
            {
                // 按设计静默：磁盘不可写、路径被占用时不得让日志拖垮主流程。
            }
        }

        private static string BuildLine(string level, string module, string message, Exception exception)
        {
            StringBuilder text = new StringBuilder();
            text.Append('[');
            text.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            text.Append("] [");
            text.Append(level);
            text.Append("] [");
            text.Append(string.IsNullOrEmpty(module) ? "App" : module);
            text.Append("] ");
            text.Append(message ?? string.Empty);
            text.Append(Environment.NewLine);
            if (exception != null)
            {
                text.Append(exception.ToString());
                text.Append(Environment.NewLine);
            }
            return text.ToString();
        }

        /// <summary>
        /// 按大小滚动：app.log 超限时依次改名，丢弃最旧的一份。
        /// 不按日期滚动，因为桌面小工具可能连续运行数周而不重启。
        /// </summary>
        private static void RotateIfNeeded()
        {
            FileInfo info = new FileInfo(LogPath);
            if (!info.Exists || info.Length < MaxBytes)
            {
                return;
            }

            string oldest = GetBackupPath(BackupCount);
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (int index = BackupCount - 1; index >= 1; index--)
            {
                string source = GetBackupPath(index);
                if (File.Exists(source))
                {
                    File.Move(source, GetBackupPath(index + 1));
                }
            }

            File.Move(LogPath, GetBackupPath(1));
        }

        private static string GetBackupPath(int index)
        {
            return Path.Combine(SettingsStore.FolderPath, "app." + index + ".log");
        }
    }
}
