// 进程入口：单实例守护、WPF 应用启动与全局异常兜底。
// 启动顺序：抢命名互斥量 → 建 Application → 起“显示设置”监听线程 → 订阅 Startup → Run。
// 第二实例不重复启动，而是给已运行实例的命名事件发信号，让前者弹设置窗口。
// 异常兜底分两层：Dispatcher 未处理异常弹窗后继续；启动失败则写日志并退出。
// 约束：日志只追加到设置目录下的 error.log，本类不建窗口、不持有控制器以外的引用。

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

// 程序集元数据：版本号与写入 exe 属性页的信息，构建脚本不替换这些值。
[assembly: AssemblyTitle("GameClock 桌面时间悬浮窗")]
[assembly: AssemblyDescription("Windows 桌面时间悬浮窗，支持天气、提醒、日程和全球时区")]
[assembly: AssemblyProduct("GameClock 桌面时间悬浮窗")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCompany("GameClock")]
[assembly: AssemblyCopyright("Copyright © 2026 GameClock")]
[assembly: AssemblyVersion("0.0.3.0")]
[assembly: AssemblyFileVersion("0.0.3.0")]
[assembly: ComVisible(false)]

namespace GameClock
{

    /// <summary>
    /// 进程入口：保证单实例、装配 WPF 应用、转发第二实例的请求并兜住致命异常。
    /// 典型生命周期是 <see cref="Main"/> 一次执行到底，退出时释放 <see cref="AppController"/>。
    /// </summary>
    internal static class Program
    {

        // ===== 单实例与进程间通信 =====

        /// <summary>单实例互斥量名；<c>Local\</c> 前缀表示只在当前登录会话内互斥，不跨用户会话。</summary>
        private const string MutexName = "Local\\GameClockOverlay.SingleInstance";

        /// <summary>“显示设置”命名事件；第二实例置位，首实例的监听线程收到后弹出设置窗口。</summary>
        private const string ShowSettingsEventName = "Local\\GameClockOverlay.ShowSettings";

        /// <summary>
        /// 应用主入口：先抢互斥量，抢不到就唤醒已运行实例并退出。
        /// 抢到后建 Application 与控制器，把启动与异常处理挂到 WPF 事件上，最后进入消息循环。
        /// 任一环节抛异常都会写日志 + 弹窗，finally 里释放控制器与事件句柄。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                // 已有实例在跑：不重复启动，只通知它把设置窗口弹出来。
                if (!createdNew)
                {
                    SignalExistingInstance();
                    return;
                }

                Application app = new Application();
                // 窗口可能全部隐藏到托盘，必须在显式 Shutdown 前保持进程存活。
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += OnDispatcherUnhandledException;

                AppController controller = null;
                EventWaitHandle showSettingsEvent = null;
                try
                {
                    controller = new AppController(app);

                    bool eventCreated;
                    // AutoReset：一次信号只唤醒一次监听循环，避免设置窗口被连续弹出多次。
                    showSettingsEvent = new EventWaitHandle(
                        false,
                        EventResetMode.AutoReset,
                        ShowSettingsEventName,
                        out eventCreated);

                    // 监听线程不能直接碰 WPF 对象，先捕获引用，再通过 Dispatcher 切回 UI 线程。
                    AppController capturedController = controller;
                    Application capturedApp = app;
                    Thread listener = new Thread(delegate()
                    {
                        while (true)
                        {
                            try
                            {
                                showSettingsEvent.WaitOne();
                            }
                            catch (ObjectDisposedException)
                            {
                                // 退出时事件句柄先被关闭，这里的异常就是退出信号，直接结束线程。
                                return;
                            }

                            if (capturedController != null)
                            {
                                // 用 BeginInvoke 而不是直接调用：监听线程无权访问窗口。
                                capturedApp.Dispatcher.BeginInvoke(
                                    new Action(delegate
                                    {
                                        capturedController.ShowSettingsFromExternal();
                                    }),
                                    DispatcherPriority.Normal);
                            }
                        }
                    });
                    // 后台线程不阻止进程退出，避免忘记停止监听导致关机卡住。
                    listener.IsBackground = true;
                    listener.Name = "GameClock Settings Listener";
                    listener.Start();

                    app.Startup += delegate
                    {
                        try
                        {
                            controller.Start();
                        }
                        catch (Exception ex)
                        {
                            // 首屏初始化失败属于致命错误：提示后直接退出，不留在半初始化状态。
                            ShowFatalError(ex);
                            app.Shutdown();
                        }
                    };
                    app.Run();
                }
                catch (Exception ex)
                {
                    ShowFatalError(ex);
                }
                finally
                {
                    // 控制器负责保存设置与释放窗口；即使初始化中途失败也要调用（内部逐项判空）。
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
        /// 通知已运行实例显示设置窗口：打开同名事件并置位。
        /// 事件不存在（首实例尚未建好或正在退出）时打开会抛异常，此时静默放弃即可。
        /// </summary>
        private static void SignalExistingInstance()
        {
            try
            {
                using (EventWaitHandle showSettingsEvent = EventWaitHandle.OpenExisting(ShowSettingsEventName))
                {
                    showSettingsEvent.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // 首实例还没创建事件或已在退出，唤醒失败属于正常竞态，不需要提示用户。
            }
        }

        /// <summary>
        /// Dispatcher 未处理异常：写日志 → 弹一次说明 → 标记已处理。
        /// 标记 <c>Handled</c> 是为了避免悬浮窗因个别回调异常直接崩溃，让用户能继续使用或手动退出。
        /// </summary>
        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException(e.Exception);
            MessageBox.Show(
                "时间悬浮窗遇到一个错误：\n\n" + e.Exception.Message + "\n\n错误详情已写入日志：" + GetLogPath(),
                "时间悬浮窗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        /// <summary>启动失败的统一提示：先写日志再弹窗，保证用户看到的路径里一定有内容。</summary>
        private static void ShowFatalError(Exception ex)
        {
            LogException(ex);
            MessageBox.Show(
                "时间悬浮窗无法启动：\n\n" + ex.Message + "\n\n错误详情已写入日志：" + GetLogPath(),
                "时间悬浮窗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        /// <summary>
        /// 把异常追加到设置目录下的 error.log，带时间戳与完整堆栈。
        /// 写日志本身再失败（目录无权限、磁盘满）时只能吞掉，否则会从异常处理里再抛异常。
        /// </summary>
        private static void LogException(Exception ex)
        {
            try
            {
                // 首次运行或用户清过目录时目录可能不存在，先确保它存在。
                Directory.CreateDirectory(SettingsStore.FolderPath);
                StringBuilder text = new StringBuilder();
                text.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]");
                text.AppendLine(ex.ToString());
                text.AppendLine();
                File.AppendAllText(GetLogPath(), text.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
                // 日志是最后的排查手段，自身失败不能再影响主流程，只能静默丢弃。
            }
        }

        /// <summary>错误日志路径：固定在设置目录下，与 settings.json 同级，便于用户打包反馈。</summary>
        private static string GetLogPath()
        {
            return Path.Combine(SettingsStore.FolderPath, "error.log");
        }
    }
}
