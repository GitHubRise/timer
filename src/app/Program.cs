using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

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
    internal static class Program
    {
        private const string MutexName = "Local\\GameClockOverlay.SingleInstance";
        private const string ShowSettingsEventName = "Local\\GameClockOverlay.ShowSettings";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已有实例在跑：把「打开设置」信号交给它，本进程直接退出，避免双托盘图标。
                    SignalExistingInstance();
                    return;
                }

                AppLog.Info(
                    "Program",
                    "启动 GameClock " + BuildInfo.Version + "（编译号 " + BuildInfo.BuildNumber + "），日志：" + AppLog.LogPath);

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += OnDispatcherUnhandledException;

                AppController controller = null;
                EventWaitHandle showSettingsEvent = null;
                try
                {
                    controller = new AppController(app);

                    bool eventCreated;
                    showSettingsEvent = new EventWaitHandle(
                        false,
                        EventResetMode.AutoReset,
                        ShowSettingsEventName,
                        out eventCreated);

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
                                return;
                            }

                            if (capturedController != null)
                            {
                                capturedApp.Dispatcher.BeginInvoke(
                                    new Action(delegate
                                    {
                                        capturedController.ShowSettingsFromExternal();
                                    }),
                                    DispatcherPriority.Normal);
                            }
                        }
                    });
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
                // 旧实例恰在此时退出：无人接收信号，本次启动随后会自己成为主实例，无需提示用户。
            }
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // UI 线程兜底：记日志后弹窗，但仍标记 Handled，避免一次绘制异常直接杀掉常驻进程。
            AppLog.Error("Program", "UI 线程未处理异常", e.Exception);
            MessageBox.Show(
                "时间悬浮窗遇到一个错误：\n\n" + e.Exception.Message + "\n\n错误详情已写入日志：" + AppLog.ErrorLogPath,
                "时间悬浮窗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private static void ShowFatalError(Exception ex)
        {
            AppLog.Error("Program", "启动失败，进程即将退出", ex);
            MessageBox.Show(
                "时间悬浮窗无法启动：\n\n" + ex.Message + "\n\n错误详情已写入日志：" + AppLog.ErrorLogPath,
                "时间悬浮窗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
