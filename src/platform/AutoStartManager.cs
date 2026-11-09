// 开机自启的唯一入口：只操作当前用户（HKCU）的 Run 键。
// 键值名固定为 GameClock，值内容是可执行文件完整路径并带引号，兼容安装路径含空格。
// 全部方法用 bool 表示“写入成功 / 当前已启用”，权限不足或键被策略锁定时静默返回 false，
// 由设置界面自行提示用户，本类不抛异常、不弹窗、不写事件日志。
// 约束：只增删自己的值，不改别人的启动项；不写 HKLM、不建计划任务、不注册服务。

using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace GameClock
{

    /// <summary>
    /// 开机自启的注册表封装（HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run）。
    /// 调用方：设置窗口的“开机自启”开关（写入）与启动时的状态回读（<see cref="IsEnabled"/>）。
    /// 写 HKCU 不需要管理员权限，但也受组策略限制，因此每次调用都必须能安全失败。
    /// </summary>
    internal static class AutoStartManager
    {

        /// <summary>当前用户启动项键路径；相对 HKCU 展开。</summary>
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        /// <summary>本程序在 Run 键下的值名，固定不随版本变化，升级后仍指向新的 exe 路径。</summary>
        private const string ValueName = "GameClock";

        /// <summary>
        /// 当前可执行文件的完整路径，供写入启动项使用。
        /// 正常取 <see cref="Assembly.Location"/>；单文件打包或宿主加载等取不到时，
        /// 退回 %LOCALAPPDATA%\GameClock\GameClock.exe 这一约定安装位置。
        /// 两个分支都返回绝对路径，调用方无需再补全目录。
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
                    // 取不到程序集路径时不能把异常抛给设置界面，退回约定安装路径即可。
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GameClock\\GameClock.exe");
                }
            }
        }

        /// <summary>
        /// 打开或关闭开机自启：<paramref name="enabled"/> 为 true 时写入带引号的 exe 路径，
        /// 为 false 时删除本程序的值（值不存在不报错）。
        /// 返回 true 表示注册表操作完成；权限不足、键被锁定或键句柄为空时返回 false，
        /// 由调用方决定是否提示用户。
        /// </summary>
        public static bool SetEnabled(bool enabled)
        {
            try
            {
                // CreateSubKey 在键不存在时会创建；组策略禁止写 Run 键时可能返回 null。
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    // 拿不到可写句柄（被策略拦截）就直接失败，不要继续往 null 上调用。
                    if (key == null)
                    {
                        return false;
                    }

                    // 开启写值、关闭删值；两条分支都在同一个可写句柄上完成，最后统一返回成功。
                    if (enabled)
                    {
                        // 路径加引号：Program Files 等含空格的目录必须带引号才能被正确解析。
                        string command = "\"" + ExecutablePath + "\"";
                        key.SetValue(ValueName, command, RegistryValueKind.String);
                    }
                    else
                    {
                        // 第二个参数 false = 值不存在时不抛异常，关闭自启天然幂等。
                        key.DeleteValue(ValueName, false);
                    }
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 组策略或安全软件禁止写 Run 键：视为“设置失败”，交给界面提示。
                return false;
            }
            catch (IOException)
            {
                // 注册表 hive 被卸载或磁盘错误等异常情况，同样只报告失败。
                return false;
            }
            catch (System.Security.SecurityException)
            {
                // 受限信任环境没有注册表权限；不能让异常冒泡打断设置窗口。
                return false;
            }
        }

        /// <summary>
        /// 回读当前是否已启用开机自启：本程序的值存在即为启用。
        /// 只读打开（writable = false），避免为了查询而要求写权限；
        /// 键不存在、值不存在或无权限时统一返回 false（视为未启用）。
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                // 只读打开，查询失败按“未启用”处理，不做任何写入或修复动作。
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    // Run 键不存在（极少见）说明从未配置过自启。
                    if (key == null)
                    {
                        return false;
                    }
                    // 只要值存在就算启用，不校验值内容是否指向当前 exe（用户可能手工改过）。
                    return key.GetValue(ValueName) != null;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 读权限被拒时按未启用处理，避免设置界面初始化阶段抛异常。
                return false;
            }
            catch (System.Security.SecurityException)
            {
                // 受限信任环境读注册表同样失败；静默降级为未启用。
                return false;
            }
        }
    }
}
