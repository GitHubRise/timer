using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace GameClock
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;

        public const long WS_EX_TRANSPARENT = 0x00000020L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_LAYERED = 0x00080000L;
        public const long WS_EX_NOACTIVATE = 0x08000000L;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOOWNERZORDER = 0x0200;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;

        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;
        public const uint WM_SETTINGCHANGE = 0x001A;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        public static bool IsLeftMouseButtonDown()
        {
            return (GetAsyncKeyState(0x01) & 0x8000) != 0;
        }

        /// <summary>
        /// 当前系统是否使用浅色任务栏；用于校验采样到的任务栏背景是否合理。
        /// </summary>
        public static bool IsSystemUsesLightTheme()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "SystemUsesLightTheme",
                    null);
                if (value is int)
                {
                    return (int)value != 0;
                }
            }
            catch (Exception)
            {
            }
            return true;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        /// <summary>
        /// 递归查找指定类名的子窗口；任务栏结构在不同 Windows 版本里层级会变。
        /// </summary>
        public static IntPtr FindDescendantByClass(IntPtr parent, string className)
        {
            if (parent == IntPtr.Zero || string.IsNullOrEmpty(className))
            {
                return IntPtr.Zero;
            }

            IntPtr result = IntPtr.Zero;
            EnumChildWindows(parent, delegate (IntPtr hWnd, IntPtr lParam)
            {
                StringBuilder builder = new StringBuilder(256);
                if (GetClassName(hWnd, builder, builder.Capacity) > 0
                    && string.Equals(builder.ToString(), className, StringComparison.Ordinal))
                {
                    result = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out IntPtr result);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int newLong);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
            {
                return GetWindowLongPtr64(hWnd, nIndex);
            }
            return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
        {
            if (IntPtr.Size == 8)
            {
                return SetWindowLongPtr64(hWnd, nIndex, value);
            }
            return new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
        }

        public static void ApplyOverlayStyles(IntPtr hWnd, bool clickThrough)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            style |= WS_EX_TOOLWINDOW | WS_EX_LAYERED;
            style &= ~WS_EX_NOACTIVATE;
            if (clickThrough)
            {
                style |= WS_EX_TRANSPARENT;
            }
            else
            {
                style &= ~WS_EX_TRANSPARENT;
            }
            SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
        }

        public static void ApplyTaskbarStyles(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            style |= WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
        }

        /// <summary>
        /// 任务栏覆盖窗样式：不做透明分层，保证文字使用 ClearType 并可以接收鼠标。
        /// </summary>
        public static void ApplyTaskbarOverlayStyles(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            style &= ~(WS_EX_LAYERED | WS_EX_TRANSPARENT);
            SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
        }

        public static void MoveWindowPhysical(IntPtr hWnd, int x, int y, int width, int height)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(
                hWnd,
                HWND_TOPMOST,
                x,
                y,
                width,
                height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
        }

        public static void SetClickThrough(IntPtr hWnd, bool enabled)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            style |= WS_EX_LAYERED;
            if (enabled)
            {
                style |= WS_EX_TRANSPARENT;
            }
            else
            {
                style &= ~WS_EX_TRANSPARENT;
            }
            SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
        }

        public static void SetTopmost(IntPtr hWnd, bool topmost)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(
                hWnd,
                topmost ? HWND_TOPMOST : HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        public static void ReassertTopmost(IntPtr hWnd)
        {
            SetTopmost(hWnd, true);
        }

        /// <summary>
        /// 把窗口提到最顶层窗口组的最前面。任务栏也是 TOPMOST 窗口，
        /// 普通 SetWindowPos(HWND_TOPMOST) 在已经置顶时不一定重新排序，
        /// 这里带 SWP_SHOWWINDOW 再提升一次，用于覆盖任务栏的时钟窗。
        /// </summary>
        public static void RaiseTopmost(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(
                hWnd,
                HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        public const int QUNS_PRESENTATION_MODE = 4;

        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        public const int WM_GETFONT = 0x0031;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct LOGFONT
        {
            public int lfHeight;
            public int lfWidth;
            public int lfEscapement;
            public int lfOrientation;
            public int lfWeight;
            public byte lfItalic;
            public byte lfUnderline;
            public byte lfStrikeOut;
            public byte lfCharSet;
            public byte lfOutPrecision;
            public byte lfClipPrecision;
            public byte lfQuality;
            public byte lfPitchAndFamily;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string lfFaceName;
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetObjectW")]
        private static extern int GetObjectW(IntPtr gdiObject, int bufferSize, ref LOGFONT logFont);

        /// <summary>
        /// 读取窗口当前字体。任务栏时钟字体随系统设置变化，覆盖窗据此跟随。
        /// </summary>
        public static bool TryGetWindowFont(IntPtr hWnd, out string faceName, out int pixelHeight)
        {
            faceName = null;
            pixelHeight = 0;
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr font = SendMessage(hWnd, WM_GETFONT, IntPtr.Zero, IntPtr.Zero);
            if (font == IntPtr.Zero)
            {
                return false;
            }

            LOGFONT logFont = new LOGFONT();
            int size = Marshal.SizeOf(typeof(LOGFONT));
            if (GetObjectW(font, size, ref logFont) != size)
            {
                return false;
            }

            pixelHeight = Math.Abs(logFont.lfHeight);
            faceName = logFont.lfFaceName;
            return pixelHeight > 0 && !string.IsNullOrEmpty(faceName);
        }

        public static void TryUseDarkTitleBar(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            try
            {
                int enabled = 1;
                // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Windows 10 2004+), 19 (older builds).
                int result = DwmSetWindowAttribute(hWnd, 20, ref enabled, sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(hWnd, 19, ref enabled, sizeof(int));
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }
    }
}
