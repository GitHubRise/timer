using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GameClock
{
    /// <summary>
    /// Loads GameClockTray.dll into explorer.exe and sends the task bar clock
    /// configuration to it. The DLL subclasses TrayClockWClass and repaints it.
    /// </summary>
    internal static class TrayClockInjector
    {
        internal const string DllFileName = "GameClockTray.dll";

        private const uint ConfigMagic = 0x31544347;
        private const int WmApp = 0x8000;
        private const int UninstallMessage = WmApp + 1;
        private const int QueryMessage = WmApp + 2;
        private const int WmCopyData = 0x004A;
        private const int ConfigSize = 656;

        private const uint ProcessCreateThread = 0x0002;
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessVmOperation = 0x0008;
        private const uint ProcessVmWrite = 0x0020;
        private const uint ProcessVmRead = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct CopyDataStruct
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, int message, IntPtr wParam, ref CopyDataStruct lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, IntPtr size, uint allocationType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, IntPtr stackSize, IntPtr startAddress, IntPtr parameter, uint flags, out uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, IntPtr size, uint freeType);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private const uint SmtoAbortIfHung = 0x0002;
        private const uint SendTimeoutMs = 250;

        private static readonly object Sync = new object();
        private static int _injectedProcessId;

        /// <summary>Finds the Windows task bar clock window. Returns zero when it is missing.</summary>
        internal static IntPtr FindClock()
        {
            IntPtr shell = FindWindow("Shell_TrayWnd", null);
            if (shell == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr tray = FindWindowEx(shell, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray != IntPtr.Zero)
            {
                IntPtr clock = FindWindowEx(tray, IntPtr.Zero, "TrayClockWClass", null);
                if (clock != IntPtr.Zero)
                {
                    return clock;
                }
            }

            return FindWindowEx(shell, IntPtr.Zero, "TrayClockWClass", null);
        }

        internal static bool IsInstalled(IntPtr clock)
        {
            if (clock == IntPtr.Zero)
            {
                return false;
            }
            IntPtr result;
            return SendMessageTimeout(clock, QueryMessage, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, SendTimeoutMs, out result) != IntPtr.Zero && result == new IntPtr(1);
        }

        internal static bool EnsureInstalled(IntPtr clock, out string error)
        {
            error = null;
            if (clock == IntPtr.Zero)
            {
                error = "没有找到任务栏时钟窗口。";
                return false;
            }

            uint processId;
            GetWindowThreadProcessId(clock, out processId);
            if (processId == 0)
            {
                error = "无法识别任务栏所属进程。";
                return false;
            }

            lock (Sync)
            {
                if (_injectedProcessId == (int)processId && IsInstalled(clock))
                {
                    return true;
                }
            }

            string dllPath;
            if (!EnsureLocalDll(out dllPath, out error))
            {
                return false;
            }

            if (!Inject((int)processId, dllPath, out error))
            {
                return false;
            }

            for (int i = 0; i < 15; i++)
            {
                if (IsInstalled(clock))
                {
                    lock (Sync)
                    {
                        _injectedProcessId = (int)processId;
                    }
                    return true;
                }
                System.Threading.Thread.Sleep(100);
            }

            error = "任务栏时钟组件加载后没有响应。";
            return false;
        }

        internal static bool SendConfig(IntPtr clock, string fontFace, int fontHeight, bool bold, bool italic,
            System.Windows.Media.Color color, string line1, string line2)
        {
            if (clock == IntPtr.Zero)
            {
                return false;
            }

            byte[] data = BuildConfig(fontFace, fontHeight, bold, italic, color, line1, line2);
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                CopyDataStruct cds = new CopyDataStruct();
                cds.dwData = new IntPtr(unchecked((int)ConfigMagic));
                cds.cbData = data.Length;
                cds.lpData = handle.AddrOfPinnedObject();
                IntPtr result;
                return SendMessageTimeout(clock, WmCopyData, IntPtr.Zero, ref cds, SmtoAbortIfHung, SendTimeoutMs, out result) != IntPtr.Zero;
                return true;
            }
            finally
            {
                handle.Free();
            }
        }

        internal static void Uninstall(IntPtr clock)
        {
            if (clock == IntPtr.Zero)
            {
                return;
            }
            IntPtr result;
            SendMessageTimeout(clock, UninstallMessage, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, SendTimeoutMs, out result);
        }

        internal static void ResetProcessCache()
        {
            lock (Sync)
            {
                _injectedProcessId = 0;
            }
        }

        private static byte[] BuildConfig(string fontFace, int fontHeight, bool bold, bool italic,
            System.Windows.Media.Color color, string line1, string line2)
        {
            byte[] data = new byte[ConfigSize];
            using (MemoryStream stream = new MemoryStream(data, true))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode))
            {
                uint flags = 0;
                if (bold)
                {
                    flags |= 1;
                }
                if (italic)
                {
                    flags |= 2;
                }

                uint colorRef = (uint)(color.R | (color.G << 8) | (color.B << 16));
                writer.Write(ConfigMagic);
                writer.Write(flags);
                writer.Write(colorRef);
                writer.Write((uint)(fontHeight > 0 ? fontHeight : 12));
                WriteFixed(writer, fontFace, 64);
                WriteFixed(writer, line1, 128);
                WriteFixed(writer, line2, 128);
            }
            return data;
        }

        private static void WriteFixed(BinaryWriter writer, string value, int capacity)
        {
            string text = value ?? string.Empty;
            int count = Math.Min(text.Length, capacity - 1);
            for (int i = 0; i < capacity; i++)
            {
                writer.Write(i < count ? text[i] : '\0');
            }
        }

        private static bool EnsureLocalDll(out string localPath, out string error)
        {
            localPath = null;
            error = null;

            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DllFileName);
            if (!File.Exists(source))
            {
                error = "缺少 " + DllFileName + "，无法启用任务栏时钟。";
                return false;
            }

            string folder = SettingsStore.FolderPath;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            try
            {
                string hash = ComputeHash(source);
                localPath = Path.Combine(folder, "GameClockTray-" + hash.Substring(0, 8) + ".dll");
                if (File.Exists(localPath))
                {
                    return true;
                }

                File.Copy(source, localPath, false);
                CleanupOldCopies(folder, localPath);
                return true;
            }
            catch (Exception ex)
            {
                error = "复制任务栏组件失败：" + ex.Message;
                return false;
            }
        }

        private static string ComputeHash(string path)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static void CleanupOldCopies(string folder, string current)
        {
            try
            {
                string[] files = Directory.GetFiles(folder, "GameClockTray-*.dll");
                for (int i = 0; i < files.Length; i++)
                {
                    if (string.Equals(files[i], current, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    try
                    {
                        File.Delete(files[i]);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static bool Inject(int processId, string dllPath, out string error)
        {
            error = null;
            IntPtr process = OpenProcess(
                ProcessCreateThread | ProcessQueryInformation | ProcessVmOperation | ProcessVmWrite | ProcessVmRead,
                false,
                processId);
            if (process == IntPtr.Zero)
            {
                error = "无法打开 explorer 进程（错误码 " + Marshal.GetLastWin32Error() + "）。";
                return false;
            }

            IntPtr remote = IntPtr.Zero;
            IntPtr thread = IntPtr.Zero;
            try
            {
                byte[] pathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
                remote = VirtualAllocEx(process, IntPtr.Zero, new IntPtr(pathBytes.Length), 0x3000, 0x04);
                if (remote == IntPtr.Zero)
                {
                    error = "无法在 explorer 中分配内存（错误码 " + Marshal.GetLastWin32Error() + "）。";
                    return false;
                }

                IntPtr written;
                if (!WriteProcessMemory(process, remote, pathBytes, new IntPtr(pathBytes.Length), out written))
                {
                    error = "无法写入任务栏组件路径（错误码 " + Marshal.GetLastWin32Error() + "）。";
                    return false;
                }

                IntPtr loadLibrary = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
                if (loadLibrary == IntPtr.Zero)
                {
                    error = "无法找到 LoadLibraryW。";
                    return false;
                }

                uint threadId;
                thread = CreateRemoteThread(process, IntPtr.Zero, IntPtr.Zero, loadLibrary, remote, 0, out threadId);
                if (thread == IntPtr.Zero)
                {
                    error = "无法在 explorer 中启动加载线程（错误码 " + Marshal.GetLastWin32Error() + "）。";
                    return false;
                }

                WaitForSingleObject(thread, 5000);
                return true;
            }
            finally
            {
                if (thread != IntPtr.Zero)
                {
                    CloseHandle(thread);
                }
                if (remote != IntPtr.Zero)
                {
                    VirtualFreeEx(process, remote, IntPtr.Zero, 0x8000);
                }
                CloseHandle(process);
            }
        }
    }
}
