// 悬浮窗右键菜单：WinForms ContextMenuStrip 的封装，只负责展示勾选状态并把点击翻译成事件。
// 调用方：ClockWindow（右键/托盘触发）与 AppController（Sync 状态、ShowAtCursor 打开）。
// 边界：菜单不自带业务逻辑，不直接改窗口或设置；所有行为由订阅 AppController 的事件决定。
// 已知坑：菜单窗口也需要置顶，否则在游戏置顶层下方看不见；打开前必须已 Sync 勾选状态。
// 已知坑：字体对象在 Dispose 时必须一并释放，否则每次重建菜单都会泄漏 GDI 句柄。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GameClock
{
    /// <summary>
    /// 悬浮窗右键菜单：拥有菜单项、暗色渲染器与状态同步，向控制器暴露点击事件。
    /// </summary>
    internal sealed class OverlayMenu : IDisposable
    {
        // 菜单项字段：构造时创建并在整个菜单生命周期内复用，Dispose 时随菜单释放。
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _clickThroughItem;
        private readonly ToolStripMenuItem _topmostItem;
        private readonly ToolStripMenuItem _modeItem;
        private readonly ToolStripMenuItem _workModeItem;
        private readonly ToolStripMenuItem _leisureModeItem;
        private readonly ToolStripMenuItem _resetPositionItem;
        private readonly ToolStripMenuItem _aboutItem;
        private readonly ToolStripMenuItem _exitItem;
        private readonly Font _menuFont;
        // 释放标记：Dispose 可能被窗口关闭与进程退出两条路径重复调用，必须幂等。
        private bool _disposed;

        // 用户操作事件：本类只负责抛出，订阅方（AppController）负责真正的状态变更与副作用。
        public event EventHandler SettingsClicked;
        public event EventHandler ClickThroughClicked;
        public event EventHandler TopmostClicked;
        public event EventHandler WorkModeClicked;
        public event EventHandler LeisureModeClicked;
        public event EventHandler ResetPositionClicked;
        public event EventHandler AboutClicked;
        public event EventHandler ExitClicked;
        // 菜单生命周期事件：控制器借此暂停全屏隐藏轮询或刷新勾选状态。
        public event EventHandler MenuOpening;
        public event EventHandler MenuOpened;
        public event EventHandler MenuClosed;

        /// <summary>
        /// 构造菜单并一次性搭好所有菜单项、暗色渲染与事件转发；不访问窗口或设置。
        /// </summary>
        public OverlayMenu()
        {
            // 微软雅黑 9.5pt 与设置窗口字号对齐；该字体对象由 Dispose 单独释放。
            _menuFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

            // 菜单整体外观：暗色底、无图片边距、保留勾选边距、带阴影与自定义渲染器。
            Menu = new ContextMenuStrip();
            Menu.Font = _menuFont;
            Menu.ShowImageMargin = false;
            Menu.ShowCheckMargin = true;
            Menu.ShowItemToolTips = true;
            Menu.DropShadowEnabled = true;
            Menu.BackColor = Color.FromArgb(32, 32, 38);
            Menu.ForeColor = Color.FromArgb(238, 238, 242);
            Menu.Padding = new Padding(4, 5, 4, 5);
            Menu.Renderer = new DarkMenuRenderer();

            // 菜单项文案与提示：提示语说明该项对窗口/提醒的影响，避免用户误以为只是外观开关。
            _settingsItem = CreateItem("设置…", "打开时间悬浮窗设置");
            _clickThroughItem = CreateItem("鼠标穿透", "开启后悬浮窗忽略鼠标；可从托盘菜单恢复");
            _topmostItem = CreateItem("始终置顶", "让时间悬浮窗保持在游戏画面之上");
            // 「当前状态」是父菜单，本身不触发事件，只承载工作/娱乐两个子项。
            _modeItem = new ToolStripMenuItem("当前状态");
            _modeItem.ForeColor = Color.FromArgb(238, 238, 242);
            _modeItem.Padding = new Padding(0, 3, 0, 3);
            _modeItem.ToolTipText = "工作或娱乐状态；提醒会按状态自动过滤";
            _workModeItem = CreateItem("工作", "只触发“全部状态”和“仅工作”的提醒");
            _leisureModeItem = CreateItem("娱乐", "只触发“全部状态”和“仅娱乐”的提醒");
            _resetPositionItem = CreateItem("重置位置", "将时间悬浮窗放回屏幕右上角");
            _aboutItem = CreateItem("关于", "查看版本号和编译号");
            _exitItem = CreateItem("退出", "关闭时间悬浮窗");

            // 设置项加粗，作为最常用的入口在视觉上突出。
            _settingsItem.Font = new Font(_menuFont, FontStyle.Bold);

            // 状态子菜单：内部下拉单独设暗色，否则会沿用 WinForms 默认浅色。
            _modeItem.DropDownItems.Add(_workModeItem);
            _modeItem.DropDownItems.Add(_leisureModeItem);
            _modeItem.DropDown.BackColor = Color.FromArgb(32, 32, 38);
            _modeItem.DropDown.ForeColor = Color.FromArgb(238, 238, 242);
            _modeItem.DropDown.Renderer = Menu.Renderer;

            // 点击转发：统一用匿名委托调用 Raise，避免每个菜单项重复判空。
            _settingsItem.Click += delegate { Raise(SettingsClicked); };
            _clickThroughItem.Click += delegate { Raise(ClickThroughClicked); };
            _topmostItem.Click += delegate { Raise(TopmostClicked); };
            _workModeItem.Click += delegate { Raise(WorkModeClicked); };
            _leisureModeItem.Click += delegate { Raise(LeisureModeClicked); };
            _resetPositionItem.Click += delegate { Raise(ResetPositionClicked); };
            _aboutItem.Click += delegate { Raise(AboutClicked); };
            _exitItem.Click += delegate { Raise(ExitClicked); };

            // 菜单项顺序即界面顺序：设置、穿透/置顶/状态、重置/关于/退出，分隔线用于分组。
            Menu.Items.Add(_settingsItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(_clickThroughItem);
            Menu.Items.Add(_topmostItem);
            Menu.Items.Add(_modeItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(_resetPositionItem);
            Menu.Items.Add(_aboutItem);
            Menu.Items.Add(_exitItem);

            // 生命周期事件挂到本类处理器，再转发给外部订阅方。
            Menu.Opening += OnMenuOpening;
            Menu.Opened += OnMenuOpened;
            Menu.Closed += OnMenuClosed;
        }

        /// <summary>底层菜单控件；ClockWindow 需要句柄做置顶或挂到托盘时使用。</summary>
        public ContextMenuStrip Menu { get; private set; }

        /// <summary>菜单当前是否处于展开状态；用于避免重复弹出或重复置顶。</summary>
        public bool IsOpen
        {
            get { return Menu.Visible; }
        }

        /// <summary>
        /// 把当前置顶/穿透/状态同步到菜单勾选与文字；每次弹出前由控制器调用。
        /// </summary>
        /// <param name="topmost">窗口是否始终置顶。</param>
        /// <param name="clickThrough">窗口是否已开启鼠标穿透。</param>
        /// <param name="currentMode">当前工作/娱乐状态原始值，未知值按默认状态显示。</param>
        public void Sync(bool topmost, bool clickThrough, string currentMode)
        {
            // 勾选状态直接映射两个开关；菜单关闭时改 Checked 不会触发任何业务逻辑。
            _clickThroughItem.Checked = clickThrough;
            _topmostItem.Checked = topmost;

            string mode = ReminderModes.Normalize(currentMode);
            // 父菜单文字带出当前状态，用户不展开子菜单也能看到；子项勾选表示二选一。
            _modeItem.Text = "当前状态：" + ReminderModes.DisplayName(mode);
            _workModeItem.Checked = mode == ReminderModes.Work;
            _leisureModeItem.Checked = mode == ReminderModes.Leisure;
        }

        /// <summary>
        /// 在鼠标当前位置弹出菜单；已展开时忽略，避免重复弹窗叠加。
        /// </summary>
        public void ShowAtCursor()
        {
            // 菜单已可见说明用户还没选完，重复 Show 会让菜单闪烁或错位，直接返回。
            if (Menu.Visible)
            {
                return;
            }
            Menu.Show(Cursor.Position);
        }

        /// <summary>
        /// 按统一暗色样式创建一个菜单项；文案与提示由调用方给出，勾选行为由 Sync 控制。
        /// </summary>
        private ToolStripMenuItem CreateItem(string text, string toolTip)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ForeColor = Color.FromArgb(238, 238, 242);
            item.Padding = new Padding(0, 3, 0, 3);
            item.ToolTipText = toolTip;
            // 勾选完全由 Sync 从真实状态回填，禁止点击自动切换，避免界面与窗口状态不一致。
            item.CheckOnClick = false;
            return item;
        }

        /// <summary>菜单即将展开：转发生命周期事件，供控制器在弹出前同步状态。</summary>
        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 没有订阅方时不做任何事；不取消展开，保持菜单默认行为。
            if (MenuOpening != null)
            {
                MenuOpening(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 菜单展开后把菜单窗口置顶：WinForms 弹出菜单默认不是置顶窗口，
        /// 在游戏全屏/其他置顶窗口上会被遮住。
        /// </summary>
        private void OnMenuOpened(object sender, EventArgs e)
        {
            // 句柄可能尚未创建（首次展开时句柄延迟创建），此时跳过置顶，等系统下次展开再试。
            if (Menu.Handle != IntPtr.Zero)
            {
                NativeMethods.SetTopmost(Menu.Handle, true);
            }
            // 通知外部菜单已完全展开，控制器可据此暂停全屏隐藏轮询。
            if (MenuOpened != null)
            {
                MenuOpened(this, EventArgs.Empty);
            }
        }

        /// <summary>菜单关闭：转发事件，控制器恢复全屏隐藏轮询等状态。</summary>
        private void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            // 关闭原因（点击项/点空白/失焦）不影响处理，一律通知外部。
            if (MenuClosed != null)
            {
                MenuClosed(this, EventArgs.Empty);
            }
        }

        /// <summary>安全触发事件：无订阅方时静默忽略，避免每个菜单项重复判空。</summary>
        private static void Raise(EventHandler handler)
        {
            // sender 传 null 是历史约定，订阅方只关心「被点击了」这一事实。
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 释放菜单、加粗字体与基础字体；重复调用安全，供窗口关闭与进程退出两条路径使用。
        /// </summary>
        public void Dispose()
        {
            // 幂等保护：窗体关闭后进程退出可能再次调用 Dispose。
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            // 菜单持有字体引用与原生窗口句柄，先释放菜单，再释放它引用的两个 Font 对象。
            Menu.Dispose();
            _settingsItem.Font.Dispose();
            _menuFont.Dispose();
            AppLog.Info("App", "释放悬浮窗右键菜单");
        }

        /// <summary>
        /// 菜单暗色渲染器：文字按启用/选中/禁用分色，勾选标记手绘成红色对勾。
        /// </summary>
        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            /// <summary>使用暗色配色表并关闭圆角，保持与悬浮窗一致的直角暗色外观。</summary>
            public DarkMenuRenderer()
                : base(new DarkMenuColorTable())
            {
                RoundedEdges = false;
            }

            /// <summary>
            /// 重绘菜单项文字：禁用项更暗、选中项纯白、其余用标准前景色，
            /// 避免系统默认按浅色主题切换文字颜色导致对比度不足。
            /// </summary>
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                Color color;
                // 禁用项（如当前状态下不可选）用低对比灰，明确表示不能点击。
                if (!e.Item.Enabled)
                {
                    color = Color.FromArgb(120, 120, 132);
                }
                else
                {
                    // 选中项与普通项分开取色，保证暗底上始终可读。
                    color = e.Item.Selected ? Color.White : Color.FromArgb(238, 238, 242);
                }
                // 直接调用 TextRenderer 并原样透传 TextFormat，保留对齐与省略号行为。
                TextRenderer.DrawText(
                    e.Graphics,
                    e.Text,
                    e.TextFont,
                    e.TextRectangle,
                    color,
                    e.TextFormat);
            }

            /// <summary>
            /// 重绘勾选标记：不用系统默认勾，改画红色两折线，与整体暗色主题搭配。
            /// </summary>
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle bounds = e.ImageRectangle;
                // 起点取勾选区域左侧 26%、下方 52%，给笔宽和对勾高度留出边距。
                float x = bounds.Left + bounds.Width * 0.26f;
                float y = bounds.Top + bounds.Height * 0.52f;
                // 线宽随勾选区域高度缩放（至少 1.6px），高 DPI 下不会细到看不见。
                using (Pen pen = new Pen(Color.FromArgb(255, 69, 58), Math.Max(1.6f, bounds.Height * 0.09f)))
                {
                    // 圆头圆角让三折线看起来更像手绘对勾。
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    // 三个点构成对勾：左下起笔 → 底部转折 → 右上收笔。
                    e.Graphics.DrawLines(
                        pen,
                        new PointF[]
                        {
                            new PointF(x, y),
                            new PointF(x + bounds.Width * 0.18f, y + bounds.Height * 0.18f),
                            new PointF(x + bounds.Width * 0.48f, y - bounds.Height * 0.20f)
                        });
                }
            }
        }

        /// <summary>
        /// 暗色菜单配色表：统一背景、边框、选中与分隔线颜色，避免默认 Office 蓝。
        /// </summary>
        private sealed class DarkMenuColorTable : ProfessionalColorTable
        {
            // 四个基础色：面板背景、边框、选中高亮、分隔线；所有 override 属性都从这四色派生。
            private static readonly Color Background = Color.FromArgb(32, 32, 38);
            private static readonly Color Border = Color.FromArgb(58, 58, 68);
            private static readonly Color Selected = Color.FromArgb(50, 50, 60);
            private static readonly Color Separator = Color.FromArgb(55, 55, 64);

            /// <summary>下拉面板背景色。</summary>
            public override Color ToolStripDropDownBackground
            {
                get { return Background; }
            }

            /// <summary>图标边距渐变起始色：与面板同色，消除默认的横向渐变。</summary>
            public override Color ImageMarginGradientBegin
            {
                get { return Background; }
            }

            /// <summary>图标边距渐变中间色：同背景色。</summary>
            public override Color ImageMarginGradientMiddle
            {
                get { return Background; }
            }

            /// <summary>图标边距渐变结束色：同背景色。</summary>
            public override Color ImageMarginGradientEnd
            {
                get { return Background; }
            }

            /// <summary>菜单外边框颜色。</summary>
            public override Color MenuBorder
            {
                get { return Border; }
            }

            /// <summary>菜单项获得焦点时的边框颜色。</summary>
            public override Color MenuItemBorder
            {
                get { return Selected; }
            }

            /// <summary>菜单项选中背景色。</summary>
            public override Color MenuItemSelected
            {
                get { return Selected; }
            }

            /// <summary>选中项渐变起始色：与选中色一致，得到纯色高亮。</summary>
            public override Color MenuItemSelectedGradientBegin
            {
                get { return Selected; }
            }

            /// <summary>选中项渐变结束色：与选中色一致。</summary>
            public override Color MenuItemSelectedGradientEnd
            {
                get { return Selected; }
            }

            /// <summary>按下项渐变起始色。</summary>
            public override Color MenuItemPressedGradientBegin
            {
                get { return Selected; }
            }

            /// <summary>按下项渐变中间色。</summary>
            public override Color MenuItemPressedGradientMiddle
            {
                get { return Selected; }
            }

            /// <summary>按下项渐变结束色。</summary>
            public override Color MenuItemPressedGradientEnd
            {
                get { return Selected; }
            }

            /// <summary>分隔线暗色。</summary>
            public override Color SeparatorDark
            {
                get { return Separator; }
            }

            /// <summary>分隔线亮色：与暗色相同，避免出现双色 3D 分隔线。</summary>
            public override Color SeparatorLight
            {
                get { return Separator; }
            }

            /// <summary>勾选框背景色。</summary>
            public override Color CheckBackground
            {
                get { return Selected; }
            }

            /// <summary>勾选框选中时的背景色。</summary>
            public override Color CheckSelectedBackground
            {
                get { return Selected; }
            }

            /// <summary>勾选框按下时的背景色。</summary>
            public override Color CheckPressedBackground
            {
                get { return Selected; }
            }
        }
    }
}
