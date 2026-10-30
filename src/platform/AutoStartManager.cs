// 开机自启管理：向当前用户注册表 Run 键写入 "完整 exe 路径"（带引号，路径含空格时必须），
// 关闭时删除该值；只操作 HKCU，不碰 HKLM，也不需要管理员权限。
// 边界：本类不弹窗、不改 AppSettings，只返回是否成功并记日志，是否提示用户由调用方决定；
// 路径与值名属于 Windows 侧约定，不要在其它模块重复拼装。

using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace GameClock
{
    /// <summary>
    /// 当前用户开机自启的读写：通过 Run 键写入/删除 "GameClock" 启动项。
    /// 失败一律返回 false 而不抛出，避免设置窗口因权限问题崩溃。
    /// </summary>
    internal static class AutoStartManager
    {
        /// <summary>当前用户 Run 键路径（HKCU\Software\Microsoft\Windows\CurrentVersion\Run）。</summary>
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        /// <summary>启动项值名；固定不随版本变化，升级覆盖后仍复用同一个自启项。</summary>
        private const string ValueName = "GameClock";

        /// <summary>
        /// 当前进程可执行文件路径；取不到时回退到 LocalAppData 下的约定安装路径。
        /// 返回值直接用于自启命令行，调用方不要再补引号。
        /// </summary>
        public static string ExecutablePath
        {
            get
            {
                try
                {
                    return Assembly.GetExecutingAssembly().Location;
                }
                catch (Exception)
                {
                    // 单文件打包或宿主环境裁剪时 Location 可能抛异常；回退路径只影响
                    // 自启命令，不中断本次运行，因此降级处理并留一条 Warn。
                    AppLog.Warn("AutoStart", "读取可执行文件路径失败，回退到 LocalAppData\\GameClock\\GameClock.exe");
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GameClock\\GameClock.exe");
                }
            }
        }

        /// <summary>
        /// 打开或关闭开机自启；成功写入注册表返回 true，权限不足或注册表不可用返回 false。
        /// 关闭时删除不存在的值不算失败（DeleteValue 已显式忽略缺失值）。
        /// </summary>
        /// <param name="enabled">true 写入启动项，false 删除启动项。</param>
        /// <returns>注册表操作是否成功，调用方据此决定是否提示用户。</returns>
        public static bool SetEnabled(bool enabled)
        {
            try
            {
                // 只创建当前用户下的 Run 子键：HKCU 不需要提权，也不会污染系统级启动项。
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        // 受限账户或注册表被策略重定向时 CreateSubKey 返回 null，按失败上报。
                        AppLog.Warn("AutoStart", "无法创建当前用户 Run 注册表项，开机自启未生效");
                        return false;
                    }

                    if (enabled)
                    {
                        // 路径整体加引号：Program Files 等含空格的路径不加引号会被 Windows 截断。
                        string command = "\"" + ExecutablePath + "\"";
                        key.SetValue(ValueName, command, RegistryValueKind.String);
                    }
                    else
                    {
                        // 第二个参数 false 表示值不存在时不抛异常，关闭自启可重复调用。
                        key.DeleteValue(ValueName, false);
                    }

                    AppLog.Info(
                        "AutoStart",
                        enabled ? "已开启开机自启：写入 Run 键值 GameClock" : "已关闭开机自启：已删除 Run 键值 GameClock");
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 组策略或安全软件限制 Run 键写入：静默失败，由调用方提示用户检查权限。
                AppLog.Warn("AutoStart", "写入开机自启被拒绝：当前用户对 Run 键没有写权限");
                return false;
            }
            catch (IOException)
            {
                // 注册表文件被占用或损坏等底层 IO 错误，通常重启后自愈，不阻断退出流程。
                AppLog.Warn("AutoStart", "写入开机自启失败：注册表 IO 错误");
                return false;
            }
            catch (System.Security.SecurityException)
            {
                // 受限信任级别（如网络位置运行）访问注册表会抛 SecurityException，等同无权限。
                AppLog.Warn("AutoStart", "写入开机自启被安全策略拒绝");
                return false;
            }
        }

        /// <summary>
        /// 查询开机自启是否已写入注册表；只读探测，失败按「未启用」处理，不影响设置窗口加载。
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        // Run 键不存在说明从未开通过自启，属于正常状态，不记日志避免刷屏。
                        return false;
                    }
                    return key.GetValue(ValueName) != null;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 只读探测被拒时按未启用处理，留一条 Warn 便于排查「设置显示未勾选」的疑惑。
                AppLog.Warn("AutoStart", "读取开机自启状态失败（权限不足），按未启用处理");
                return false;
            }
            catch (System.Security.SecurityException)
            {
                AppLog.Warn("AutoStart", "读取开机自启状态失败（安全策略限制），按未启用处理");
                return false;
            }
        }
    }
}
