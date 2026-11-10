// Win32 互操作集中地：本项目所有 DllImport 声明与 Win32 结构体只允许出现在本文件，
// 其它模块（clock/、app/、settings/）一律调用这里封装好的方法，不自己碰句柄与 P/Invoke。
// 职责：窗口扩展样式（透明穿透、工具窗、置顶）、任务栏时钟窗口查找与覆盖定位、
//       全屏判定、DWM 窗口属性、窗口字体读取、鼠标按键与系统主题查询、打开通知中心。
// 约束：句柄由调用方创建与释放，本类只借用句柄读写属性，不持有、不销毁任何窗口句柄。
// 失败约定：Win32 调用失败时返回 false / 0 / 零句柄，本类不抛异常，调用方需自行判空。

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32;

namespace GameClock
{

    /// <summary>
    /// 本项目唯一的 Win32 P/Invoke 门面，按用途分组：常量、结构体、窗口与显示器查询、
    /// 任务栏时钟查找、窗口样式读写、全屏判定、字体与 DWM。
    /// 上层模块（桌面悬浮窗、任务栏时钟、设置窗口）只调用这里的封装方法；
    /// 新增系统调用必须加在本文件并放进对应分组，其它文件不得再写 <c>DllImport</c>。
    /// </summary>
    internal static class NativeMethods
    {
        // ===== 窗口样式常量 =====

        /// <summary>扩展样式索引（GWL_EXSTYLE），配合 GetWindowLongPtr / SetWindowLongPtr 读写。</summary>
        public const int GWL_EXSTYLE = -20;

        /// <summary>鼠标穿透：命中测试落到下层窗口，悬浮窗开启后不阻挡桌面点击。</summary>
        public const long WS_EX_TRANSPARENT = 0x00000020L;

        /// <summary>工具窗：不出现在 Alt+Tab 列表与任务栏按钮中。</summary>
        public const long WS_EX_TOOLWINDOW = 0x00000080L;

        /// <summary>分层窗：WPF 透明窗口的基础，透明度与逐像素 Alpha 都依赖它。</summary>
        public const long WS_EX_LAYERED = 0x00080000L;

        /// <summary>点击不激活：窗口能收到鼠标消息但不抢前台焦点，任务栏覆盖窗必须带。</summary>
        public const long WS_EX_NOACTIVATE = 0x08000000L;

        // ===== SetWindowPos 标志 =====

        /// <summary>保持当前宽度与高度。</summary>
        public const uint SWP_NOSIZE = 0x0001;

        /// <summary>保持当前位置。</summary>
        public const uint SWP_NOMOVE = 0x0002;

        /// <summary>不改变 Z 序，只用于移动或改尺寸。</summary>
        public const uint SWP_NOZORDER = 0x0004;

        /// <summary>不激活窗口，避免抢焦点。</summary>
        public const uint SWP_NOACTIVATE = 0x0010;

        /// <summary>显示窗口；全屏隐藏期间禁用——带上它会把已 Hide() 的窗口重新显示。</summary>
        public const uint SWP_SHOWWINDOW = 0x0040;

        /// <summary>不改变属主窗口的 Z 序。</summary>
        public const uint SWP_NOOWNERZORDER = 0x0200;

        // ===== 窗口消息与显示命令 =====

        /// <summary>WM_MOUSEACTIVATE：窗口被点击时询问是否激活。</summary>
        public const int WM_MOUSEACTIVATE = 0x0021;

        /// <summary>MA_NOACTIVATE：WM_MOUSEACTIVATE 的返回值，处理点击但不激活窗口。</summary>
        public const int MA_NOACTIVATE = 3;

        /// <summary>ShowWindow 命令：隐藏窗口。</summary>
        public const int SW_HIDE = 0;

        /// <summary>ShowWindow 命令：显示窗口但不激活，用于全屏恢复。</summary>
        public const int SW_SHOWNOACTIVATE = 4;

        /// <summary>WM_SETTINGCHANGE：系统设置（主题、字体、任务栏）变化广播。</summary>
        public const uint WM_SETTINGCHANGE = 0x001A;

        /// <summary>Z 序伪句柄：置于所有非置顶窗口之上。</summary>
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        /// <summary>Z 序伪句柄：取消置顶，放在普通窗口组。</summary>
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        // ===== 结构体 =====

        /// <summary>屏幕坐标点（像素），由 <see cref="GetCursorPos"/> 填充、交给 <see cref="WindowFromPoint"/> 使用。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {

            /// <summary>屏幕 X 坐标（像素，可为负，多显示器左侧）。</summary>
            public int X;

            /// <summary>屏幕 Y 坐标（像素）。</summary>
            public int Y;
        }

        /// <summary>
        /// 矩形（像素），Win32 约定 <c>Right</c>/<c>Bottom</c> 为开区间边界。
        /// 同时用于窗口矩形（<see cref="GetWindowRect"/>）、DWM 扩展边框与显示器矩形，
        /// 判定全屏时按 left/top/right/bottom 与显示器矩形逐边比较。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {

            /// <summary>左边界 X。</summary>
            public int Left;

            /// <summary>上边界 Y。</summary>
            public int Top;

            /// <summary>右边界 X（不含该像素列）。</summary>
            public int Right;

            /// <summary>下边界 Y（不含该像素行）。</summary>
            public int Bottom;
        }

        /// <summary>
        /// 显示器信息（MONITORINFO）。调用前必须把 <c>cbSize</c> 填成结构体大小，
        /// 否则 <see cref="GetMonitorInfo"/> 会因版本校验失败返回 false。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {

            /// <summary>结构体字节数，调用方填 <c>Marshal.SizeOf(typeof(MONITORINFO))</c>。</summary>
            public int cbSize;

            /// <summary>显示器完整矩形（物理像素，含任务栏区域），全屏判定就用它。</summary>
            public RECT rcMonitor;

            /// <summary>显示器工作区矩形（不含任务栏与停靠栏）。</summary>
            public RECT rcWork;

            /// <summary>标志位；1 = 主显示器（MONITORINFOF_PRIMARY）。</summary>
            public uint dwFlags;
        }

        // ===== 前台窗口与显示器 =====

        /// <summary>取当前前台窗口句柄；无前台窗口（锁屏、切换桌面）时返回 <see cref="IntPtr.Zero"/>，不持有句柄。</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        /// <summary>取窗口所属进程 ID 与创建它的线程 ID；用于排除本进程自己的窗口。</summary>
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>取窗口所在显示器句柄；<paramref name="flags"/> 传 2（MONITOR_DEFAULTTONEAREST）保证总能拿到最近显示器。</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        /// <summary>取显示器矩形信息；需先填好 <c>info.cbSize</c>，返回 false 表示参数或显示器句柄无效。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        /// <summary>取当前进程 ID；与前台窗口的进程 ID 比较，用于跳过自己（设置窗、面板、时钟）。</summary>
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentProcessId();

        // ===== 鼠标与命中测试 =====

        /// <summary>取光标屏幕坐标（物理像素）；返回 false 表示当前桌面不允许查询。</summary>
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        /// <summary>取坐标点处的最上层窗口；返回的是子窗口，需再用 <see cref="GetAncestor"/> 上升到顶层窗口。</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        /// <summary>取窗口的祖先；<paramref name="flags"/> 传 2（GA_ROOT）得到顶层窗口，用于把子窗口归一。</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        /// <summary>取系统度量（如屏幕宽高、任务栏尺寸）；索引非法时返回 0，本类只用于诊断。</summary>
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        /// <summary>异步查询按键状态（高字节 0x80 表示按下）；只在封装方法里用，外部不要直接调。</summary>
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        /// <summary>
        /// 左键当前是否按下（<c>VK_LBUTTON</c> = 0x01）。
        /// 用于拖动前判断真实按键状态，避免 WPF 事件与 Win32 状态不一致时误触发；
        /// 取的是全局按键状态，与焦点无关。
        /// </summary>
        public static bool IsLeftMouseButtonDown()
        {
            return (GetAsyncKeyState(0x01) & 0x8000) != 0;
        }

        /// <summary>
        /// 当前系统是否使用浅色任务栏；用于校验采样到的任务栏背景是否合理。
        /// 读 HKCU 的 SystemUsesLightTheme（0 = 深色）；键缺失、值非 int 或读取失败时按浅色处理。
        /// </summary>
        public static bool IsSystemUsesLightTheme()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "SystemUsesLightTheme",
                    null);
                // 只有拿到 int 才能判断；字符串等异常类型继续走默认值。
                if (value is int)
                {
                    return (int)value != 0;
                }
            }
            catch (Exception)
            {
                // 读主题键失败（键不存在、被策略限制）时按浅色兜底：调用方只把它当参考。
            }
            return true;
        }

        // ===== 任务栏时钟窗口查找 =====

        /// <summary>按类名/标题查找顶层窗口；标题传 null 表示不限制。任务栏时钟窗口类名为 <c>TrayClockWClass</c>。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
        public static extern IntPtr FindWindow(string className, string windowName);

        /// <summary>在指定父窗口下按类名查找同级子窗口；<paramref name="childAfter"/> 传零表示从第一个子窗口开始。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        /// <summary>EnumChildWindows 的回调签名；返回 false 表示停止枚举。</summary>
        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>枚举父窗口的所有子窗口（递归），直到回调返回 false。</summary>
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

        /// <summary>取窗口类名到 <see cref="StringBuilder"/>；返回字符数，0 表示失败或缓冲区不足。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        /// <summary>
        /// 递归查找指定类名的子窗口；任务栏结构在不同 Windows 版本里层级会变。
        /// 找不到或参数为空时返回 <see cref="IntPtr.Zero"/>，返回的句柄属于系统，调用方只管读取属性。
        /// </summary>
        public static IntPtr FindDescendantByClass(IntPtr parent, string className)
        {
            // 父窗口为空（任务栏未就绪）或类名为空时直接放弃，避免无意义的枚举。
            if (parent == IntPtr.Zero || string.IsNullOrEmpty(className))
            {
                return IntPtr.Zero;
            }

            IntPtr result = IntPtr.Zero;
            // 命中即把句柄记到 result 并返回 false 停止枚举；类名缓冲 256 字符足够覆盖系统类名。
            EnumChildWindows(parent, delegate (IntPtr hWnd, IntPtr lParam)
            {
                StringBuilder builder = new StringBuilder(256);
                // 类名读取成功且与目标完全一致才算命中；序数比较区分大小写，系统类名是固定写法。
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

        // ===== 窗口几何与消息 =====

        /// <summary>
        /// 在 <paramref name="parent"/> 的后代里找“紧贴 <paramref name="anchor"/> 右侧、同一行”的指定类名窗口。
        /// 任务栏的「操作中心」按钮就在时钟右边一个像素都不隔，用它把覆盖区向右扩出可用宽度。
        /// 找不到（旧版 Windows 没有该按钮、被系统隐藏）时返回 <see cref="IntPtr.Zero"/>。
        /// </summary>
        /// <param name="parent">枚举起点，通常是任务栏根窗口。</param>
        /// <param name="className">目标类名，如 <c>TrayButton</c>。</param>
        /// <param name="anchor">定位基准矩形；要求目标窗口与其上下边界基本对齐。</param>
        /// <param name="maxGap">允许的水平间隙（物理像素）；超过视为不是紧邻按钮。</param>
        public static IntPtr FindChildToRight(IntPtr parent, string className, RECT anchor, int maxGap)
        {
            // 参数不全时直接返回，避免无意义的枚举。
            if (parent == IntPtr.Zero || string.IsNullOrEmpty(className))
            {
                return IntPtr.Zero;
            }

            IntPtr best = IntPtr.Zero;
            int bestLeft = int.MaxValue;
            EnumChildWindows(parent, delegate (IntPtr hWnd, IntPtr lParam)
            {
                StringBuilder builder = new StringBuilder(256);
                if (GetClassName(hWnd, builder, builder.Capacity) <= 0
                    || !string.Equals(builder.ToString(), className, StringComparison.Ordinal))
                {
                    return true;
                }

                RECT candidate;
                if (!GetWindowRect(hWnd, out candidate) || !IsWindowVisible(hWnd))
                {
                    return true;
                }

                // 只要紧邻右侧那一个：左边缘落在一小段间隙内，且上下边界与时钟同高。
                int gap = candidate.Left - anchor.Right;
                if (gap < -2 || gap > maxGap)
                {
                    return true;
                }
                if (Math.Abs(candidate.Top - anchor.Top) > 4 || Math.Abs(candidate.Bottom - anchor.Bottom) > 4)
                {
                    return true;
                }

                // 取最靠左的那个（正常只有一个），避免误命中右侧更远的同名按钮。
                if (candidate.Left < bestLeft)
                {
                    bestLeft = candidate.Left;
                    best = hWnd;
                }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        /// <summary>取窗口矩形（屏幕坐标，含不可见边框）；返回 false 表示句柄无效。</summary>
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        /// <summary>取父窗口句柄；顶层窗口返回 <see cref="IntPtr.Zero"/>。</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        /// <summary>按命令显示/隐藏窗口（<c>SW_HIDE</c> / <c>SW_SHOWNOACTIVATE</c>）；不激活的前提下切换可见性。</summary>
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int command);

        /// <summary>窗口当前是否可见；全屏隐藏与置顶提升前都用它做判断。</summary>
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>同步发送窗口消息并返回结果；跨进程时可能阻塞，本类只用于取字体等轻量查询。</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>限时广播消息并取返回码；超时或目标无响应时返回 0，避免卡住调用线程。</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out IntPtr result);

        /// <summary>改变窗口位置、尺寸与 Z 序；<paramref name="flags"/> 决定哪几项保持不变。</summary>
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        // ===== 窗口样式读写（32/64 位分支） =====

        /// <summary>64 位版的 GetWindowLongPtrW；SetLastError 便于失败时用 <c>Marshal.GetLastWin32Error</c> 排查。</summary>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        /// <summary>32 位进程没有 GetWindowLongPtr，退回 GetWindowLongW。</summary>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        /// <summary>64 位版的 SetWindowLongPtrW；失败返回 0，需配合 GetLastError 判断（0 也可能是合法旧值）。</summary>
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr newLong);

        /// <summary>32 位版的 SetWindowLongW。</summary>
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int newLong);

        /// <summary>设置 DWM 窗口属性（如深色标题栏）；返回 HRESULT，非 0 表示系统或窗口不支持该属性。</summary>
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>读取 DWM 窗口属性；本类只用它取扩展边框矩形（DWMWA_EXTENDED_FRAME_BOUNDS = 9）。</summary>
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

        /// <summary>
        /// 按进程位数读取窗口扩展样式（只读）。
        /// 64 位走 GetWindowLongPtrW、32 位走 GetWindowLongW；句柄无效时返回零，调用方需判空。
        /// </summary>
        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            // 按指针宽度选择入口点，这是 WPF 与 Win32 互操作的标准写法。
            if (IntPtr.Size == 8)
            {
                return GetWindowLongPtr64(hWnd, nIndex);
            }
            return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        /// <summary>
        /// 按进程位数写入窗口扩展样式，返回旧值（用于需要时恢复）。
        /// 只做位运算后的整体写回，调用方负责决定要置位/清位哪些标志。
        /// </summary>
        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
        {
            if (IntPtr.Size == 8)
            {
                return SetWindowLongPtr64(hWnd, nIndex, value);
            }
            return new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
        }

        // ===== 窗口样式封装 =====

        /// <summary>
        /// 桌面悬浮窗样式：工具窗 + 分层，清掉“不激活”以便设置窗口能正常获得焦点；
        /// <paramref name="clickThrough"/> 决定是否加鼠标穿透（穿透时点击直接落到桌面）。
        /// 句柄为空时静默返回，供窗口尚未创建完成时的调用路径使用。
        /// </summary>
        public static void ApplyOverlayStyles(IntPtr hWnd, bool clickThrough)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            style |= WS_EX_TOOLWINDOW | WS_EX_LAYERED;
            style &= ~WS_EX_NOACTIVATE;
            // 穿透与非穿透是互斥状态，必须显式清位，否则关掉开关后窗口仍然点不到。
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

        /// <summary>
        /// 旧版任务栏覆盖窗样式：工具窗 + 分层 + 穿透 + 不激活（纯显示，不可交互）。
        /// 保留给历史调用路径；当前任务栏时钟使用 <see cref="ApplyTaskbarOverlayStyles"/>。
        /// </summary>
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
        /// 因此要显式清掉 LAYERED 与 TRANSPARENT 两位，覆盖窗才能悬停、右键弹菜单。
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

        /// <summary>
        /// 按物理像素把窗口移动到 (x, y) 并置顶显示，坐标与任务栏矩形同为屏幕像素。
        /// 带 SWP_SHOWWINDOW：这是“显式显示”路径，全屏恢复时用它重新显示覆盖窗；
        /// 不要把它当作每 250 ms 的置顶调用（那条路径见 <see cref="RaiseTopmost"/>）。
        /// </summary>
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

        /// <summary>
        /// 单独切换鼠标穿透位（保留 LAYERED）：悬浮窗解锁拖动时关掉，锁定位置时打开。
        /// 与 <see cref="ApplyOverlayStyles"/> 的区别是不碰 TOOLWINDOW / NOACTIVATE。
        /// </summary>
        public static void SetClickThrough(IntPtr hWnd, bool enabled)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            long style = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            style |= WS_EX_LAYERED;
            // 开启穿透后点击不再落到悬浮窗，关闭时清位恢复可点击。
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
        /// 切换置顶状态：只改 Z 序，不动位置与尺寸（SWP_NOMOVE | SWP_NOSIZE）。
        /// 置顶用 HWND_TOPMOST，取消置顶用 HWND_NOTOPMOST；不激活窗口以免抢焦点。
        /// </summary>
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

        /// <summary>
        /// 重新声明置顶：等价于 <see cref="SetTopmost"/>(hWnd, true)，供设置变更后统一调用。
        /// 句柄判空交由 <see cref="SetTopmost"/> 处理。
        /// </summary>
        public static void ReassertTopmost(IntPtr hWnd)
        {
            SetTopmost(hWnd, true);
        }

        /// <summary>
        /// 把窗口提到「任务栏正上方」这一层：插到 <c>Shell_TrayWnd</c> 的上一个 Z 序位置。
        /// 不能直接 SetWindowPos(HWND_TOPMOST)：那会顶到 TOPMOST 组最前面，
        /// 把随后弹出的任务栏提示框、右键菜单压到下面（用户反馈的“置顶优先级太高”）。
        /// 插到任务栏上方后，其它后出现的顶层窗口天然保持在这之上。
        /// 任务栏找不到时退回纯置顶。注意不能带 SWP_SHOWWINDOW：任务栏时钟每 250 ms 调一次，
        /// 带上它会把已 Hide() 的窗口重新显示出来，导致全屏隐藏失效。
        /// 窗口不可见时直接返回，避免和 1 秒一次的全屏隐藏互相打架。
        /// </summary>
        public static void RaiseTopmost(IntPtr hWnd)
        {
            // 仅在可见时提升；不可见说明正在全屏隐藏，此时置顶会把窗口重新拉出来。
            if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd))
            {
                return;
            }

            // 找任务栏及其上方那一层：把窗口插在“任务栏之上、其它顶层窗口之下”。
            IntPtr shell = FindWindow("Shell_TrayWnd", null);
            IntPtr insertAfter = shell != IntPtr.Zero ? GetWindow(shell, GW_HWNDPREV) : IntPtr.Zero;
            // 已经就在任务栏正上方时无需再动，省掉一次无效的 Z 序调整。
            if (insertAfter == hWnd)
            {
                return;
            }

            SetWindowPos(
                hWnd,
                insertAfter != IntPtr.Zero ? insertAfter : HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        /// <summary>Z 序下行：<c>GW_HWNDNEXT</c>，取当前窗口下方（更低一层）的那个窗口。</summary>
        public const uint GW_HWNDNEXT = 0x0002;

        /// <summary>Z 序上行：<c>GW_HWNDPREV</c>，取当前窗口上方（更高一层）的那个窗口。</summary>
        public const uint GW_HWNDPREV = 0x0003;

        /// <summary>按 Z 序取相邻窗口；<paramref name="uCmd"/> 用 <see cref="GW_HWNDNEXT"/>。</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        /// <summary>
        /// 判断 <paramref name="candidate"/> 是否排在 <paramref name="reference"/> 之上（Z 序更靠前、更“顶层”）。
        /// 从 candidate 沿 <see cref="GW_HWNDNEXT"/> 往下走，先遇到 reference 就说明 candidate 压在它上面。
        /// 任务栏时钟用它区分「确实被任务栏抢回去了，需要重新抬顶」和「已经在任务栏之上，别再动 Z 序」——
        /// 后者若也无条件 <see cref="RaiseTopmost"/>，就会把任务栏的提示框、右键菜单重新压到下面。
        /// 只走 64 跳：正常链长远小于这个值，超限按“不在其上”处理，宁可多抬一次也不死循环。
        /// </summary>
        public static bool IsAbove(IntPtr candidate, IntPtr reference)
        {
            // 任一句柄无效都无从比较。
            if (candidate == IntPtr.Zero || reference == IntPtr.Zero)
            {
                return false;
            }

            IntPtr current = candidate;
            for (int i = 0; i < 64; i++)
            {
                current = GetWindow(current, GW_HWNDNEXT);
                if (current == IntPtr.Zero)
                {
                    return false;
                }
                if (current == reference)
                {
                    return true;
                }
            }
            return false;
        }

        // ===== 系统操作 =====

        /// <summary>左 Win 键（<c>VK_LWIN</c>）。</summary>
        private const byte VK_LWIN = 0x5B;

        /// <summary>字母 N（<c>VK_N</c>），与 Win 组合即通知中心快捷键。</summary>
        private const byte VK_N = 0x4E;

        /// <summary>键抬起标志（<c>KEYEVENTF_KEYUP</c>）。</summary>
        private const uint KEYEVENTF_KEYUP = 0x0002;

        /// <summary>合成一次键按下/抬起。仅用于模拟系统快捷键，不用于输入文本。</summary>
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        /// <summary>
        /// 打开 Windows 通知中心（异步）：立即返回，真正的调用丢到线程池。
        /// UI 自动化是跨进程同步调用，explorer 忙时一次 <c>Invoke</c> 可能耗时数百毫秒到数秒，
        /// 若在菜单点击处理里同步执行，整个界面会卡住，所以这里绝不占用界面线程。
        /// </summary>
        public static void OpenActionCenter()
        {
            ThreadPool.QueueUserWorkItem(delegate (object state) { OpenActionCenterCore(); });
        }

        /// <summary>
        /// 通知中心的真正调用路径，跑在后台线程。实测耗时（本机 1920×1080/125%）：
        /// 桌面全量 <c>FindAll(Descendants, ClassName="TrayButton")</c> 要约 <b>12.7 秒</b>（这就是早期卡顿的来源），
        /// 而把搜索限定在任务栏的 <c>TrayNotifyWnd</c> 子树里只要约 <b>14 毫秒</b>；
        /// <c>Invoke()</c> 本身要约 2.4 秒（阻塞到 explorer 把通知中心画出来），所以它必须在后台线程跑。
        /// 顺序：① TrayNotifyWnd 子树里按类名找按钮并 Invoke；② 失败就模拟 <c>Win+N</c>。
        /// 快捷键顺序必须是「按下 Win → 按下 N → 抬起 N → 抬起 Win」，提前抬起 Win 会变成单独按 N。
        /// </summary>
        private static void OpenActionCenterCore()
        {
            IntPtr shell = FindWindow("Shell_TrayWnd", null);
            // 通知区域的容器；「取消固定/全部通知」等兄弟类都在别处，这里只包含时钟、通知按钮等托盘项。
            IntPtr trayNotify = FindDescendantByClass(shell, "TrayNotifyWnd");
            if (trayNotify != IntPtr.Zero)
            {
                try
                {
                    AutomationElement host = AutomationElement.FromHandle(trayNotify);
                    if (host != null)
                    {
                        Condition byClass = new PropertyCondition(AutomationElement.ClassNameProperty, "TrayButton");
                        AutomationElementCollection buttons = host.FindAll(TreeScope.Descendants, byClass);
                        foreach (AutomationElement candidate in buttons)
                        {
                            string name = candidate.Current.Name;
                            if (string.IsNullOrEmpty(name) || !name.StartsWith("操作中心", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            object pattern;
                            if (candidate.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                            {
                                ((InvokePattern)pattern).Invoke();
                                return;
                            }
                        }
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // 任务栏正在重建：元素句柄失效，落到快捷键兜底。
                }
                catch (InvalidOperationException)
                {
                    // UIA 服务拒绝调用：同样走兜底。
                }
                catch (COMException)
                {
                    // explorer 无响应时 UIA 调用会抛 COM 异常，兜底即可，不能让它冒到后台线程外面。
                }
            }

            // 兜底：模拟 Win+N。
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_N, 0, 0, UIntPtr.Zero);
            keybd_event(VK_N, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // ===== 系统日历面板 =====

        /// <summary>左键按下（<c>MOUSEEVENTF_LEFTDOWN</c>）。</summary>
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;

        /// <summary>左键抬起（<c>MOUSEEVENTF_LEFTUP</c>）。</summary>
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        /// <summary>把光标移到屏幕物理像素坐标 (x, y)。</summary>
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        /// <summary>合成鼠标事件；本项目只用它做“点一下原生时钟”，不移动窗口、不拖拽。</summary>
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        /// <summary>找任务栏原生时钟窗口（<c>Shell_TrayWnd</c> 的 <c>TrayClockWClass</c> 子窗口）。</summary>
        public static IntPtr FindNativeClockWindow()
        {
            IntPtr shell = FindWindow("Shell_TrayWnd", null);
            return shell != IntPtr.Zero ? FindDescendantByClass(shell, "TrayClockWClass") : IntPtr.Zero;
        }

        /// <summary>
        /// 在原生时钟中心合成一次真实左键点击，用于打开系统「日历」飞出面板。
        /// 为什么不用消息投递：给 <c>TrayClockWClass</c> 发 <c>WM_LBUTTONDOWN/UP</c> 实测无效，
        /// 该控件只在真实鼠标输入（带命中测试与鼠标捕获）时才展开面板。
        /// 调用方必须先把覆盖窗隐藏（<c>WS_EX_TRANSPARENT</c> 单独设置并不会让点击穿透），
        /// 否则点击会被覆盖窗自己吃掉。隐藏期间会露出系统自己的时间格式，所以这里的等待
        /// 压到最短（约 30 ms），把“格式跳一下”压缩到难以察觉的一两帧。
        /// 找不到时钟（任务栏改版）时静默放弃，系统时钟不受影响。
        /// </summary>
        public static void ClickNativeClock()
        {
            IntPtr clock = FindNativeClockWindow();
            if (clock == IntPtr.Zero)
            {
                return;
            }

            RECT rect;
            if (!GetWindowRect(clock, out rect))
            {
                return;
            }

            // 取时钟矩形中心：点这里等于用户点系统时钟本身。
            SetCursorPos((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
            // 光标先到位再按键：系统按光标位置判定命中的是哪个托盘项。
            Thread.Sleep(12);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(18);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        // ===== 全屏判定 =====

        /// <summary>QUNS 状态：D3D 独占全屏应用运行中，是“真全屏”最可靠的信号。</summary>
        public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

        /// <summary>QUNS 状态：演示模式（投影/演示时系统抑制通知），同样按全屏处理。</summary>
        public const int QUNS_PRESENTATION_MODE = 4;

        /// <summary>
        /// 查询当前用户通知状态（QUNS_*）。返回 0 表示成功，此时 <paramref name="state"/> 有效；
        /// 非 0 表示查询失败，调用方不要把 state 当作有效值。
        /// </summary>
        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        /// <summary>读取窗口标题（Unicode）；返回字符数，0 表示无标题或句柄无效。仅用于诊断日志。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

        /// <summary>窗口是否最大化；只写进 fullscreen.log，用来区分最大化与无边框全屏。</summary>
        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        /// <summary>
        /// 判断当前前台是否处于全屏（独占全屏或铺满整个显示器的无边框全屏）。
        /// 先看 <see cref="SHQueryUserNotificationState"/> 的 QUNS 3/4，它对无边框全屏/浏览器 F11 不可靠，
        /// 再按前台窗口矩形兜底；忽略本进程窗口与桌面/任务栏窗口。1 秒轮询调用一次，
        /// 返回 true 时由控制器隐藏悬浮窗与任务栏覆盖窗（只 Hide，不停计时器）。
        /// 判定依据会写入 fullscreen.log，便于排查“某个应用全屏时没隐藏”。
        /// </summary>
        public static bool IsFullscreenForeground()
        {
            int state = -1;
            // queried 区分“查询失败”与“状态 0（正常桌面）”，日志里 quns 会显示 err。
            bool queried = SHQueryUserNotificationState(out state) == 0;
            bool exclusive = queried
                && (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE);

            // 无论走哪条分支都先取前台窗口，日志需要类名/标题/进程用于事后对照。
            IntPtr foreground = GetForegroundWindow();

            string className = string.Empty;
            string title = string.Empty;
            uint processId = 0;
            // 句柄为空（锁屏、切换到安全桌面）时跳过全部查询，后面按 no-foreground 处理。
            if (foreground != IntPtr.Zero)
            {
                StringBuilder buffer = new StringBuilder(256);
                // 类名与标题都只用于日志，读不到就留空，不影响判定结果。
                if (GetClassName(foreground, buffer, buffer.Capacity) > 0)
                {
                    className = buffer.ToString();
                }

                buffer = new StringBuilder(200);
                // 标题可能很长，200 字符缓冲够用；截断只影响日志观感。
                if (GetWindowTextW(foreground, buffer, buffer.Capacity) > 0)
                {
                    title = buffer.ToString();
                }

                GetWindowThreadProcessId(foreground, out processId);
            }

            bool result = false;
            // reason 记录命中/落选原因，直接进日志；默认 none 表示未进入任何分支。
            string reason = "none";
            RECT frame = new RECT();
            RECT monitorRect = new RECT();
            bool hasFrame = false;
            bool hasMonitor = false;

            // 先看独占全屏信号；不成立再逐级排除非应用窗口，最后按矩形兜底。
            if (exclusive)
            {
                // 独占全屏最可信，直接短路，不再做矩形比较。
                result = true;
                reason = "quns";
            }
            else if (foreground == IntPtr.Zero || !IsWindowVisible(foreground))
            {
                // 没有前台窗口或窗口不可见（最小化/隐藏）时不当作全屏。
                reason = "no-foreground";
            }
            else if (processId == GetCurrentProcessId())
            {
                // 自己的设置/面板/时钟窗口不算全屏应用。
                reason = "self";
            }
            else if (string.Equals(className, "Progman", StringComparison.Ordinal)
                || string.Equals(className, "WorkerW", StringComparison.Ordinal)
                || string.Equals(className, "Shell_TrayWnd", StringComparison.Ordinal)
                || string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.Ordinal))
            {
                // 桌面（Progman/WorkerW）与任务栏本身铺满屏幕，但不是应用全屏，必须排除。
                reason = "shell";
            }
            else
            {
                // MONITOR_DEFAULTTONEAREST = 2：窗口不在任何显示器时也给出最近的显示器矩形。
                IntPtr monitor = MonitorFromWindow(foreground, 2);
                MONITORINFO info = new MONITORINFO();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

                // 拿不到显示器或结构体校验失败时放弃矩形判定，日志里记 no-monitor。
                if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
                {
                    reason = "no-monitor";
                }
                else
                {
                    monitorRect = info.rcMonitor;
                    hasMonitor = true;

                    // 优先用 DWM 扩展边框：最大化窗口的真实客户区是工作区（不含任务栏），
                    // 不会因为不可见边框被误判为全屏。
                    if (DwmGetWindowAttribute(foreground, 9, out frame, Marshal.SizeOf(typeof(RECT))) != 0)
                    {
                        hasFrame = GetWindowRect(foreground, out frame);
                    }
                    else
                    {
                        hasFrame = true;
                    }

                    if (!hasFrame)
                    {
                        reason = "no-rect";
                    }
                    else
                    {
                        // 容忍 2px：个别全屏窗口的边框会略微超出显示器。
                        result = frame.Left <= monitorRect.Left + 2
                            && frame.Top <= monitorRect.Top + 2
                            && frame.Right >= monitorRect.Right - 2
                            && frame.Bottom >= monitorRect.Bottom - 2;
                        reason = result ? "frame" : "smaller";
                    }
                }
            }

            // 签名只含“结果/原因/进程/类名/显示器”：同一程序内切标签页不会重复写日志。
            string signature = (result ? "1" : "0")
                + "|" + reason
                + "|" + processId
                + "|" + className
                + "|" + RectText(monitorRect);

            // 明细压成一行，字段固定顺序，便于直接用文本工具比对两次判定差异。
            StringBuilder detail = new StringBuilder();
            detail.Append(result ? "[FULLSCREEN]" : "[window]");
            detail.Append(" reason=").Append(reason);
            detail.Append(" quns=").Append(queried ? state.ToString() : "err");
            detail.Append(" pid=").Append(processId);
            detail.Append(" cls=").Append(className.Length == 0 ? "?" : className);
            // 只有真正取到矩形才写 frame/monitor，缺失时留空避免误读成 (0,0,0,0)。
            if (hasFrame)
            {
                detail.Append(" frame=").Append(RectText(frame));
            }
            if (hasMonitor)
            {
                detail.Append(" monitor=").Append(RectText(monitorRect));
            }
            detail.Append(" zoomed=").Append(IsZoomed(foreground) ? "1" : "0");
            detail.Append(" title=").Append(title);

            // 只在签名变化时真正落盘，Report 内部做去重；这里不关心写入结果。
            FullscreenProbe.Report(signature, detail.ToString());
            return result;
        }

        /// <summary>把 RECT 拼成 <c>"left,top,right,bottom"</c> 文本，仅供日志与签名使用，不参与判定。</summary>
        private static string RectText(RECT rect)
        {
            return rect.Left + "," + rect.Top + "," + rect.Right + "," + rect.Bottom;
        }

        // ===== 窗口字体 =====

        /// <summary>WM_GETFONT：向窗口询问当前 HFONT，取不到返回零。</summary>
        public const int WM_GETFONT = 0x0031;

        /// <summary>
        /// GDI 逻辑字体描述（LOGFONTW），由 <see cref="GetObjectW"/> 从 HFONT 复制而来。
        /// <c>lfHeight</c> 为负表示字符高度（像素）、为正表示单元格高度，本类取绝对值当字号；
        /// <c>lfFaceName</c> 是定长 32 字符的 Unicode 字体名，读到的就是系统任务栏当前字体。
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct LOGFONT
        {
            // 逻辑单位度量：高度（负 = 字符高度）、宽度、倾斜与基线角度。
            public int lfHeight;
            public int lfWidth;
            public int lfEscapement;
            public int lfOrientation;
            // 字重（400 常规、700 粗体）与三个布尔样式字节。
            public int lfWeight;
            public byte lfItalic;
            public byte lfUnderline;
            public byte lfStrikeOut;
            // 字符集与输出精度：任务栏字体通常为 DEFAULT_CHARSET，这里只读不改。
            public byte lfCharSet;
            public byte lfOutPrecision;
            public byte lfClipPrecision;
            public byte lfQuality;
            public byte lfPitchAndFamily;

            // 字体名，Unicode 定长 32 字符（含结尾 0），必须用 ByValTStr 才能正确封送。
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string lfFaceName;
        }

        /// <summary>把 GDI 对象（这里是 HFONT）内容复制到 LOGFONT；返回值等于结构体大小才算成功。</summary>
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetObjectW")]
        private static extern int GetObjectW(IntPtr gdiObject, int bufferSize, ref LOGFONT logFont);

        /// <summary>
        /// 读取窗口当前字体。任务栏时钟字体随系统设置变化，覆盖窗据此跟随。
        /// 成功时返回 true 并给出字体名与像素高度；句柄为空、窗口无字体或 GetObjectW 失败时返回 false，
        /// 两个输出参数保持 null / 0，调用方据此回退到旧配置的字体。
        /// </summary>
        /// <param name="hWnd">目标窗口句柄（通常是任务栏的 <c>TrayClockWClass</c>），只读不持有。</param>
        /// <param name="faceName">输出的字体名，如 <c>Segoe UI</c>；失败时为 null。</param>
        /// <param name="pixelHeight">输出的字符高度（像素，已取绝对值）；失败时为 0。</param>
        /// <returns>是否成功取到有效字体名与非零字号。</returns>
        public static bool TryGetWindowFont(IntPtr hWnd, out string faceName, out int pixelHeight)
        {
            faceName = null;
            pixelHeight = 0;
            // 任务栏尚未创建时句柄为空，直接按失败返回，避免向零句柄发消息。
            if (hWnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr font = SendMessage(hWnd, WM_GETFONT, IntPtr.Zero, IntPtr.Zero);
            // 窗口没有设置字体（用系统默认字体绘制）时返回零句柄，同样按失败处理。
            if (font == IntPtr.Zero)
            {
                return false;
            }

            LOGFONT logFont = new LOGFONT();
            int size = Marshal.SizeOf(typeof(LOGFONT));
            // GetObject 约定：返回字节数等于请求大小才算完整复制，否则结构体内容不可信。
            if (GetObjectW(font, size, ref logFont) != size)
            {
                return false;
            }

            // lfHeight 为负表示字符高度，取绝对值就是绘制字号（像素）。
            pixelHeight = Math.Abs(logFont.lfHeight);
            faceName = logFont.lfFaceName;
            return pixelHeight > 0 && !string.IsNullOrEmpty(faceName);
        }

        // ===== 深色标题栏 =====

        /// <summary>
        /// 尝试把窗口标题栏切成深色，配合暗色主题；失败时静默放弃，保持系统默认外观。
        /// 先用 DWMWA_USE_IMMERSIVE_DARK_MODE = 20（Windows 10 2004+），
        /// 返回非 0 说明系统不认这个属性，再退回旧编号 19（更早的 1809/1903）。
        /// 捕获 DllNotFoundException / EntryPointNotFoundException：Win7 等没有对应 DWM 入口点时直接跳过。
        /// </summary>
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
                // 非 0 的 HRESULT 表示该属性编号不被支持，换旧编号再试一次。
                if (result != 0)
                {
                    DwmSetWindowAttribute(hWnd, 19, ref enabled, sizeof(int));
                }
            }
            catch (DllNotFoundException)
            {
                // 系统没有 dwmapi.dll（极老系统）时没有深色标题栏可言，保持默认。
            }
            catch (EntryPointNotFoundException)
            {
                // dwmapi 存在但缺少该入口点，同样只影响观感，吞掉即可。
            }
        }
    }
}
