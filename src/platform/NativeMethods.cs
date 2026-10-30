// 平台层：全仓唯一的 Win32 P/Invoke 入口，其他模块不得自行声明 DllImport。
// 边界：这里的坐标与尺寸一律是物理像素（非 WPF 的 DIP），调用方需自行换算 DPI。
// 边界：hWnd 为空时各封装方法静默返回；窗口句柄失效（关闭/重建）时调用方需重新获取。
// 已知坑：GetWindowLongPtr/SetWindowLongPtr 在 32/64 位下符号不同，必须按 IntPtr.Size 分派。
// 已知坑：DwmSetWindowAttribute 的深色标题栏属性在 Win10 2004 前是 19、之后是 20，需要双写兜底。
// 日志：只在移动窗口、深色标题栏这类外部副作用失败时记一条；SetTopmost 每 3 秒轮询不记，避免刷屏。
using System;
using System.Runtime.InteropServices;

namespace GameClock
{
    /// <summary>
    /// Win32 常量和 P/Invoke 声明的集中地，并在其上提供少量窗口样式/位置的封装方法。
    /// </summary>
    internal static class NativeMethods
    {
        // GetWindowLong/SetWindowLong 的扩展样式索引；负数表示扩展样式而不是普通样式。
        public const int GWL_EXSTYLE = -20;

        // 扩展窗口样式位：点击穿透（TRACK）、不显示在任务栏（TOOLWINDOW）、分层窗口（LAYERED，透明必需）、不抢焦点（NOACTIVATE）。
        public const long WS_EX_TRANSPARENT = 0x00000020L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_LAYERED = 0x00080000L;
        public const long WS_EX_NOACTIVATE = 0x08000000L;

        // SetWindowPos 标志位：保持尺寸/位置/层序不变，显示窗口，且不改变 owner 窗口的 Z 序。
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOOWNERZORDER = 0x0200;

        // 鼠标激活相关消息：WM_MOUSEACTIVATE 返回 MA_NOACTIVATE 可让点击不激活窗口。
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;

        // 窗口显示命令与设置变更广播消息；SW_SHOWNOACTIVATE 用于不抢焦点的恢复显示。
        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;
        public const uint WM_SETTINGCHANGE = 0x001A;

        // SetWindowPos 的特殊 hWndInsertAfter 值：-1 置顶、-2 取消置顶。
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        /// <summary>屏幕坐标点（物理像素），用于 GetCursorPos 输出。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>窗口矩形（物理像素，相对屏幕或父窗口），Right/Bottom 为开区间。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // 取屏幕坐标下的鼠标位置；overlay 判断鼠标是否停留在窗口上时使用。
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        // 按窗口类名/标题查找顶层窗口；查找任务栏 TrayClockWClass 时用类名、标题传 null。
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
        public static extern IntPtr FindWindow(string className, string windowName);

        // 枚举子窗口（TrayClockWClass 是任务栏的子窗口），childAfter 为 IntPtr.Zero 表示从第一个开始。
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        // 取窗口矩形（物理像素）；任务栏子窗口的坐标相对父窗口，恢复位置前要自行换算。
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        // 取父窗口句柄；用于把任务栏子窗口坐标换算回屏幕坐标。
        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        // 显示/隐藏窗口；隐藏系统任务栏时钟用 SW_HIDE，恢复用 SW_SHOWNOACTIVATE。
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int command);

        // 查询窗口是否可见；系统时钟是否被我们移走前先判断，避免重复操作。
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        // 同步发送窗口消息（广播设置变更时用），调用方不关心返回值。
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // 带超时的同步发送；广播 WM_SETTINGCHANGE 时防止某个窗口无响应卡住整个应用。
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out IntPtr result);

        // 设置窗口位置/尺寸/层序；返回 false 表示失败，错误码用 Marshal.GetLastWin32Error() 读取。
        // 必须声明 SetLastError=true：否则运行时不会在线程上保存 Win32 错误码，GetLastWin32Error 可能读到上一次调用的陈旧值。
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        // 64 位进程读取窗口样式（GetWindowLongPtrW 实际从 Vista 起才导出，但 Win10/11 均可用）。
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        // 32 位进程读取窗口样式（GetWindowLongW），返回值按 int 处理。
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        // 64 位进程写入窗口样式；返回旧值，0 也可能是合法旧值，不能单凭返回值判断失败。
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newLong);

        // 32 位进程写入窗口样式（SetWindowLongW）。
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int newLong);

        // 设置 DWM 窗口属性；返回 HRESULT，0 为 S_OK，非 0 表示系统不认可该属性。
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>
        /// 按当前进程位数分派读取窗口样式，统一返回 IntPtr；调用方通常再 ToInt64() 做位运算。
        /// </summary>
        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            // 64 位进程必须用 GetWindowLongPtrW，GetWindowLongW 会截断高位样式。
            if (IntPtr.Size == 8)
            {
                return GetWindowLongPtr64(hWnd, nIndex);
            }
            return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        /// <summary>
        /// 按当前进程位数分派写入窗口样式；返回写入前的旧值（仅用于诊断，不用于判错）。
        /// </summary>
        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
        {
            // 与读取一致：64 位走 Ptr 版本，避免高位样式位丢失。
            if (IntPtr.Size == 8)
            {
                return SetWindowLongPtr64(hWnd, nIndex, value);
            }
            return new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
        }

        /// <summary>
        /// 给悬浮窗套扩展样式：不进任务栏、分层透明、不抢焦点；clickThrough 时追加点击穿透位。
        /// 只能改样式位，句柄失效时调用方需重新获取窗口句柄。
        /// </summary>
        public static void ApplyOverlayStyles(IntPtr hWnd, bool clickThrough)
        {
            // 句柄为空说明窗口尚未创建或已销毁，静默跳过；调用方负责处理窗口生命周期。
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            // 始终保证 TOOLWINDOW（不占任务栏）与 LAYERED（WPF 透明必需），并清掉 NOACTIVATE。
            style |= WS_EX_TOOLWINDOW | WS_EX_LAYERED;
            style &= ~WS_EX_NOACTIVATE;
            // 点击穿透由设置决定：开启时鼠标事件直接落到下层窗口，关闭时恢复可交互。
            if (clickThrough)
            {
                style |= WS_EX_TRANSPARENT;
            }
            else
            {
                style &= ~WS_EX_TRANSPARENT;
            }
            // 返回值是旧样式，0 也可能是合法值，无法据此判断成败，因此不做失败分支。
            SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
        }

        /// <summary>
        /// 给任务栏覆盖窗套扩展样式：不进任务栏、分层透明、点击穿透且永不激活，
        /// 保证点击任务栏时钟区域仍由系统任务栏处理。
        /// </summary>
        public static void ApplyTaskbarStyles(IntPtr hWnd)
        {
            // 句柄为空时没有可操作的窗口，直接返回，避免把样式写到 0 句柄上。
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            // 任务栏覆盖窗需要四个位同时成立，否则会出现任务栏多出按钮或抢焦点。
            style |= WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(style));
        }

        /// <summary>
        /// 把窗口移动到指定物理像素矩形并置顶，同时显示窗口。
        /// 任务栏时钟定位与悬浮窗位置应用使用；坐标为物理像素，调用方负责 DPI 换算。
        /// </summary>
        public static void MoveWindowPhysical(IntPtr hWnd, int x, int y, int width, int height)
        {
            // 窗口未创建/已销毁时移动没有意义，静默跳过一次即可。
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            bool moved = SetWindowPos(
                hWnd,
                HWND_TOPMOST,
                x,
                y,
                width,
                height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
            // 移动失败通常意味着句柄失效或桌面拒绝置顶，记录错误码便于定位。
            if (!moved)
            {
                int error = Marshal.GetLastWin32Error();
                AppLog.Warn("Platform", "移动窗口失败（x=" + x + "，y=" + y + "，宽=" + width + "，高=" + height + "），Win32 错误码=" + error + "（0x" + error.ToString("X8") + "）");
            }
        }

        /// <summary>
        /// 单独切换点击穿透样式位，保留其他扩展样式；设置窗口预览穿透效果时使用。
        /// </summary>
        public static void SetClickThrough(IntPtr hWnd, bool enabled)
        {
            // 句柄无效时无法修改样式，交由上层在窗口重建后重新调用。
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            // LAYERED 是逐像素透明的前提，这里无条件补上，避免只开穿透导致窗口不透明。
            style |= WS_EX_LAYERED;
            // TRANSPARENT 位决定点击是否穿透；关闭时清位恢复鼠标交互。
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

        /// <summary>
        /// 置顶/取消置顶窗口；只改 Z 序，不动位置与尺寸。
        /// 注意：该方法是 ClockWindow 每 3 秒的置顶复位路径，失败不记日志以免刷屏。
        /// </summary>
        public static void SetTopmost(IntPtr hWnd, bool topmost)
        {
            // 失效句柄（窗口关闭中）直接跳过，下次置顶复位会在新句柄上重试。
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

        /// <summary>
        /// 把窗口重新拉回置顶层；其他置顶程序抢 Z 序后由定时器周期调用。
        /// </summary>
        public static void ReassertTopmost(IntPtr hWnd)
        {
            // 复用 SetTopmost，语义等同「保持置顶」，不额外判断当前是否已置顶。
            SetTopmost(hWnd, true);
        }

        // 用户通知状态：3 = 全屏 D3D 应用运行中，4 = 演示模式；两者都触发悬浮窗隐藏。
        public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        public const int QUNS_PRESENTATION_MODE = 4;

        /// <summary>
        /// 查询当前用户通知状态（Shell 提供）；返回 3/4 表示应隐藏悬浮窗，返回值非 0 时 out 参数不可信。
        /// </summary>
        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        /// <summary>
        /// 尝试把窗口标题栏切到深色（设置窗口用）；旧版 Windows 或不支持时静默回退到系统默认外观。
        /// </summary>
        public static void TryUseDarkTitleBar(IntPtr hWnd)
        {
            // 无窗口句柄时没有标题栏可改，静默跳过。
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            // dwmapi 在个别精简系统上缺失，DllImport 会在调用时才抛异常，故整段兜底。
            try
            {
                int enabled = 1;
                // DWMWA_USE_IMMERSIVE_DARK_MODE = 20（Win10 2004+），旧版本用 19；先试新值，失败再退回旧值。
                int result = DwmSetWindowAttribute(hWnd, 20, ref enabled, sizeof(int));
                // 新属性不被识别时（HRESULT 非 0）再试旧属性号，兼容 2004 之前的 Win10。
                if (result != 0)
                {
                    result = DwmSetWindowAttribute(hWnd, 19, ref enabled, sizeof(int));
                    // 两个属性号都失败说明系统不支持深色标题栏，记录一次便于排查外观问题。
                    if (result != 0)
                    {
                        AppLog.Warn("Platform", "设置深色标题栏失败，属性 20/19 均不被支持，最终 HRESULT=0x" + result.ToString("X8"));
                    }
                }
            }
            catch (DllNotFoundException)
            {
                // 精简系统可能没有 dwmapi.dll；深色标题栏只是外观增强，缺失时保持系统默认即可。
                AppLog.Info("Platform", "系统缺少 dwmapi.dll，跳过深色标题栏设置");
            }
            catch (EntryPointNotFoundException)
            {
                // 老版本 dwmapi 可能不导出 DwmSetWindowAttribute，同样降级为默认标题栏。
                AppLog.Warn("Platform", "当前系统不支持 DwmSetWindowAttribute，跳过深色标题栏设置");
            }
        }
    }
}
