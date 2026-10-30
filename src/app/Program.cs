// 进程入口：单实例仲裁、装配 Application 与 AppController、外部「打开设置」信号监听、全局异常兜底。
// 调用方：用户双击 dist\GameClock.exe；再次启动时本文件只负责唤醒已有实例并退出。
// 边界：这里只做装配与兜底，不碰窗口布局、不读写设置；任何异常都先落盘再提示用户。
// 已知坑：单实例用 Local\ 命名互斥量——同一登录会话内唯一，不同用户会话各有一份托盘图标，这是有意为之。
// 线程模型：UI 线程跑 WPF 消息循环；信号监听线程只做 WaitOne + Dispatcher.BeginInvoke，不直接碰 UI。

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

// 程序集元数据：AssemblyVersion 必须与 build.ps1 的 $version 一致（0.0.1），
// 否则「关于」窗口、Release 标签与安装包文件名会互相矛盾。
[assembly: AssemblyTitle("GameClock 桌面时间悬浮窗")]
[assembly: AssemblyDescription("Windows 桌面时间悬浮窗，支持天气、提醒、日程和全球时区")]
[assembly: AssemblyProduct("GameClock 桌面时间悬浮窗")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCompany("GameClock")]
[assembly: AssemblyCopyright("Copyright © 2026 GameClock")]
[assembly: AssemblyVersion("0.0.1.0")]
[assembly: AssemblyFileVersion("0.0.1.0")]
[assembly: ComVisible(false)]

namespace GameClock
{
    /// <summary>
    /// 进程入口与全局兜底：单实例互斥、控制器生命周期、跨进程唤醒、未处理异常记录。
    /// </summary>
    internal static class Program
    {
        // 互斥量名带 Local\ 前缀：只在当前登录会话内生效，多用户同时登录各开一份互不干扰。
        private const string MutexName = "Local\\GameClockOverlay.SingleInstance";

        // 唤醒事件名与互斥量同前缀；第二实例 Set 后由监听线程转成 UI 线程上的 ShowSettings。
        private const string ShowSettingsEventName = "Local\\GameClockOverlay.ShowSettings";

        /// <summary>
        /// 进程主入口：抢单实例锁 → 启动控制器与消息循环 → 释放资源。
        /// 非主实例不启动任何 UI，只发一次唤醒信号就退出。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // createdNew=false 说明锁已被别的进程持有，此时 mutex 只用于握手，不能拿来当主实例锁。
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                // 已有实例在跑：把「打开设置」信号交给它，本进程直接退出，避免双托盘图标。
                if (!createdNew)
                {
                    SignalExistingInstance();
                    return;
                }

                // 主实例第一条日志：版本、编译号与日志路径，后续所有排查都从这里对齐时间线。
                AppLog.Info(
                    "Program",
                    "启动 GameClock " + BuildInfo.Version + "（编译号 " + BuildInfo.BuildNumber + "），日志：" + AppLog.LogPath);

                // 只用纯 WPF Application（不启用 WinForms 消息泵），托盘由 AppController 内部创建。
                Application app = new Application();
                // 显式关闭：关掉设置窗口不能终止进程，常驻托盘要活到用户选「退出」。
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                // 兜底注册早于任何窗口创建，保证控制器构造期的 UI 异常也能被记下来。
                app.DispatcherUnhandledException += OnDispatcherUnhandledException;

                AppController controller = null;
                EventWaitHandle showSettingsEvent = null;
                try
                {
                    // 控制器构造只做字段与计时器初始化，真正的启动在 Startup 事件里。
                    controller = new AppController(app);

                    // AutoReset：一次 Set 只唤醒一次 WaitOne，连续点两次启动不会堆叠重复弹窗。
                    bool eventCreated;
                    showSettingsEvent = new EventWaitHandle(
                        false,
                        EventResetMode.AutoReset,
                        ShowSettingsEventName,
                        out eventCreated);

                    // 局部捕获：匿名委托里不能直接用 out 变量/会被置空的字段，先固化引用避免竞态读到 null。
                    AppController capturedController = controller;
                    Application capturedApp = app;
                    Thread listener = new Thread(delegate()
                    {
                        // 常驻循环：进程退出时由 finally 里的 Close 触发 ObjectDisposedException 结束线程。
                        while (true)
                        {
                            try
                            {
                                showSettingsEvent.WaitOne();
                            }
                            catch (ObjectDisposedException)
                            {
                                // 主实例正在退出，事件句柄已关闭；监听线程随进程一起结束，无需再兜底。
                                return;
                            }

                            // 控制器可能已释放（退出流程中），此时忽略这次唤醒，不让它把窗口拉回前台。
                            if (capturedController != null)
                            {
                                // 必须切回 UI 线程：ShowSettings 会创建/激活窗口，跨线程直接调用会抛异常。
                                capturedApp.Dispatcher.BeginInvoke(
                                    new Action(delegate
                                    {
                                        capturedController.ShowSettingsFromExternal();
                                    }),
                                    DispatcherPriority.Normal);
                            }
                        }
                    });
                    // 后台线程不阻止进程退出；命名只影响调试器里的线程列表可读性。
                    listener.IsBackground = true;
                    listener.Name = "GameClock Settings Listener";
                    listener.Start();

                    // 延后到 Startup 再启动业务：此时 Dispatcher 已在跑，托盘与窗口才能正常创建。
                    app.Startup += delegate
                    {
                        try
                        {
                            controller.Start();
                        }
                        catch (Exception ex)
                        {
                            // 启动失败属于致命错误：先落盘+弹窗说明原因，再结束进程，避免留下半初始化的托盘。
                            ShowFatalError(ex);
                            app.Shutdown();
                        }
                    };
                    // 阻塞直到 Application.Shutdown；正常退出时从这里返回，进入 finally 释放资源。
                    app.Run();
                }
                catch (Exception ex)
                {
                    // 构造阶段（控制器、事件、线程）失败：没有可用 UI 循环，弹窗后直接退出。
                    ShowFatalError(ex);
                }
                finally
                {
                    // 释放顺序：先让控制器停计时器、关窗口、移除托盘图标，再关跨进程事件句柄。
                    if (controller != null)
                    {
                        controller.Dispose();
                    }
                    if (showSettingsEvent != null)
                    {
                        showSettingsEvent.Close();
                    }
                }
            }
        }

        /// <summary>
        /// 通知已运行的主实例打开设置窗口；找不到事件说明对方正在退出，直接放弃。
        /// </summary>
        private static void SignalExistingInstance()
        {
            try
            {
                // 只 Set 不等待：主实例收到后自行切回 UI 线程，第二实例没有别的事要做。
                using (EventWaitHandle showSettingsEvent = EventWaitHandle.OpenExisting(ShowSettingsEventName))
                {
                    showSettingsEvent.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // 旧实例恰在此时退出：无人接收信号，本次启动随后会自己成为主实例，无需提示用户。
            }
        }

        /// <summary>
        /// UI 线程兜底：记录异常并提示用户，同时标记已处理，避免一次绘制异常杀掉常驻进程。
        /// </summary>
        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // 先落盘再弹窗：弹窗可能因系统资源不足而失败，日志是最可靠的证据。
            AppLog.Error("Program", "UI 线程未处理异常", e.Exception);
            MessageBox.Show(
                "时间悬浮窗遇到一个错误：\n\n" + e.Exception.Message + "\n\n错误详情已写入日志：" + AppLog.ErrorLogPath,
                "时间悬浮窗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            // Handled=true 是刻意选择：悬浮窗是常驻工具，宁可带着一次绘制偏差继续跑，也不要静默退出。
            e.Handled = true;
        }

        /// <summary>
        /// 启动失败提示：写入 error.log 并弹窗告知日志位置，调用方负责随后退出进程。
        /// </summary>
        private static void ShowFatalError(Exception ex)
        {
            // 错误日志路径由 AppLog 统一提供，避免对话框与日志文件两处路径规则不一致。
            AppLog.Error("Program", "启动失败，进程即将退出", ex);
            MessageBox.Show(
                "时间悬浮窗无法启动：\n\n" + ex.Message + "\n\n错误详情已写入日志：" + AppLog.ErrorLogPath,
                "时间悬浮窗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
