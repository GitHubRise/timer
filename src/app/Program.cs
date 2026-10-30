using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

[assembly: AssemblyTitle("GameClock 桌面时间悬浮窗")]
[assembly: AssemblyDescription("Windows 桌面时间悬浮窗，支持天气、提醒、日程和全球时区")]
[assembly: AssemblyProduct("GameClock 桌面时间悬浮窗")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCompany("GameClock")]
[assembly: AssemblyCopyright("Copyright © 2026 GameClock")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
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
                    SignalExistingInstance();
                    return;
                }

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
            }
        }

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

        private static void ShowFatalError(Exception ex)
        {
            LogException(ex);
            MessageBox.Show(
                "时间悬浮窗无法启动：\n\n" + ex.Message + "\n\n错误详情已写入日志：" + GetLogPath(),
                "时间悬浮窗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static void LogException(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.FolderPath);
                StringBuilder text = new StringBuilder();
                text.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]");
                text.AppendLine(ex.ToString());
                text.AppendLine();
                File.AppendAllText(GetLogPath(), text.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        private static string GetLogPath()
        {
            return Path.Combine(SettingsStore.FolderPath, "error.log");
        }
    }
}
