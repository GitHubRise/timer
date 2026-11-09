// 悬浮窗与托盘共用的暗色右键菜单：负责菜单项文案、勾选状态与自绘外观。
// 本类只发事件、不做业务：设置/穿透/置顶/状态切换/退出都由 AppController 订阅后处理。
// 外观走 ToolStripProfessionalRenderer 派生类，统一暗色底、白色选中文字与手绘对勾。
// 生命周期：AppController 启动时建一份 → 悬停或右键时 ShowAtCursor → 退出时 Dispose。
// 约束：菜单对象同时挂给托盘，Dispose 前必须由使用方先摘掉引用，否则托盘会访问已释放控件。

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GameClock
{

    /// <summary>
    /// 悬浮窗与系统托盘共用的右键菜单。
    /// 只提供菜单结构与勾选同步（<see cref="Sync"/>），所有动作都以事件形式抛给 <see cref="AppController"/>。
    /// 生命周期由控制器掌握：<see cref="ShowAtCursor"/> 负责弹出，<see cref="Dispose"/> 释放自绘字体与菜单句柄。
    /// </summary>
    internal sealed class OverlayMenu : IDisposable
    {

        // ===== 菜单项 =====

        /// <summary>“设置…”项；加粗显示，是菜单里最常用的入口。</summary>
        private readonly ToolStripMenuItem _settingsItem;

        /// <summary>“鼠标穿透”勾选项；勾选后悬浮窗忽略鼠标，只能从托盘菜单恢复。</summary>
        private readonly ToolStripMenuItem _clickThroughItem;

        /// <summary>“始终置顶”勾选项；控制悬浮窗是否压在游戏画面之上。</summary>
        private readonly ToolStripMenuItem _topmostItem;

        /// <summary>“当前状态”父项；本身不带动作，展开后是工作/娱乐两个子项。</summary>
        private readonly ToolStripMenuItem _modeItem;

        /// <summary>“工作”子项；勾选表示当前按工作状态过滤提醒。</summary>
        private readonly ToolStripMenuItem _workModeItem;

        /// <summary>“娱乐”子项；勾选表示当前按娱乐状态过滤提醒。</summary>
        private readonly ToolStripMenuItem _leisureModeItem;

        /// <summary>“重置位置”项；把悬浮窗放回默认角落。</summary>
        private readonly ToolStripMenuItem _resetPositionItem;

        /// <summary>“操作中心”项；任务栏通知按钮被覆盖窗盖住后，用它打开 Windows 通知中心。</summary>
        private readonly ToolStripMenuItem _actionCenterItem;

        /// <summary>“关于”项；打开版本信息窗口。</summary>
        private readonly ToolStripMenuItem _aboutItem;

        /// <summary>“退出”项；结束整个进程。</summary>
        private readonly ToolStripMenuItem _exitItem;

        // ===== 字体与释放状态 =====

        /// <summary>菜单基础字体；显式创建便于在 <see cref="Dispose"/> 里确定性地释放。</summary>
        private readonly Font _menuFont;

        /// <summary>是否已释放；保证重复 Dispose 不会二次释放 GDI 对象。</summary>
        private bool _disposed;

        // ===== 对外事件 =====

        /// <summary>点“设置…”时触发。</summary>
        public event EventHandler SettingsClicked;

        /// <summary>点“鼠标穿透”时触发；勾选状态由控制器回写，本类不自己改。</summary>
        public event EventHandler ClickThroughClicked;

        /// <summary>点“始终置顶”时触发。</summary>
        public event EventHandler TopmostClicked;

        /// <summary>选“工作”子项时触发。</summary>
        public event EventHandler WorkModeClicked;

        /// <summary>选“娱乐”子项时触发。</summary>
        public event EventHandler LeisureModeClicked;

        /// <summary>点“重置位置”时触发。</summary>
        public event EventHandler ResetPositionClicked;

        /// <summary>点“操作中心”时触发；由控制器调用 <c>Win+N</c> 打开通知中心。</summary>
        public event EventHandler ActionCenterClicked;

        /// <summary>点“关于”时触发。</summary>
        public event EventHandler AboutClicked;

        /// <summary>点“退出”时触发。</summary>
        public event EventHandler ExitClicked;

        /// <summary>菜单即将展开；订阅方借此机会刷新勾选状态。</summary>
        public event EventHandler MenuOpening;

        /// <summary>菜单已展开；控制器用它暂停置顶守护。</summary>
        public event EventHandler MenuOpened;

        /// <summary>菜单已关闭；控制器用它恢复置顶守护。</summary>
        public event EventHandler MenuClosed;

        /// <summary>
        /// 构建整个菜单：先统一外观（暗色底、无图标边距、显示勾选与提示），再逐项建条目并接线。
        /// 顺序上先建菜单项再插进 Menu.Items，最后挂 Opening/Opened/Closed 三个生命周期事件。
        /// </summary>
        public OverlayMenu()
        {
            // 显式指定中文字体与字号，避免随系统 DPI 设置变成难以辨认的小字。
            _menuFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

            Menu = new ContextMenuStrip();
            Menu.Font = _menuFont;
            // 本菜单没有图标，关掉左侧图像边距可让文字更靠左、整体更紧凑。
            Menu.ShowImageMargin = false;
            Menu.ShowCheckMargin = true;
            Menu.ShowItemToolTips = true;
            Menu.DropShadowEnabled = true;
            Menu.BackColor = Color.FromArgb(32, 32, 38);
            Menu.ForeColor = Color.FromArgb(238, 238, 242);
            Menu.Padding = new Padding(4, 5, 4, 5);
            Menu.Renderer = new DarkMenuRenderer();

            _settingsItem = CreateItem("设置…", "打开时间悬浮窗设置");
            _clickThroughItem = CreateItem("鼠标穿透", "开启后悬浮窗忽略鼠标；可从托盘菜单恢复");
            _topmostItem = CreateItem("始终置顶", "让时间悬浮窗保持在游戏画面之上");
            _modeItem = new ToolStripMenuItem("当前状态");
            _modeItem.ForeColor = Color.FromArgb(238, 238, 242);
            _modeItem.Padding = new Padding(0, 3, 0, 3);
            _modeItem.ToolTipText = "工作或娱乐状态；提醒会按状态自动过滤";
            _workModeItem = CreateItem("工作", "只触发“全部状态”和“仅工作”的提醒");
            _leisureModeItem = CreateItem("娱乐", "只触发“全部状态”和“仅娱乐”的提醒");
            _resetPositionItem = CreateItem("重置位置", "将时间悬浮窗放回屏幕右上角");
            _actionCenterItem = CreateItem("操作中心", "打开 Windows 通知中心（Win+N）；任务栏通知按钮被时钟覆盖时可从这里进");
            _aboutItem = CreateItem("关于", "查看版本号和编译号");
            _exitItem = CreateItem("退出", "关闭时间悬浮窗");

            // 设置是主入口，加粗做视觉区分；该字体独立于 _menuFont，释放时要单独处理。
            _settingsItem.Font = new Font(_menuFont, FontStyle.Bold);

            // 状态是二级菜单，需单独设置下拉面板的配色，否则会露出系统浅色主题。
            _modeItem.DropDownItems.Add(_workModeItem);
            _modeItem.DropDownItems.Add(_leisureModeItem);
            _modeItem.DropDown.BackColor = Color.FromArgb(32, 32, 38);
            _modeItem.DropDown.ForeColor = Color.FromArgb(238, 238, 242);
            _modeItem.DropDown.Renderer = Menu.Renderer;

            // 每个条目只转发事件，业务判断全部留在控制器，菜单本身不持有设置。
            _settingsItem.Click += delegate { Raise(SettingsClicked); };
            _clickThroughItem.Click += delegate { Raise(ClickThroughClicked); };
            _topmostItem.Click += delegate { Raise(TopmostClicked); };
            _workModeItem.Click += delegate { Raise(WorkModeClicked); };
            _leisureModeItem.Click += delegate { Raise(LeisureModeClicked); };
            _resetPositionItem.Click += delegate { Raise(ResetPositionClicked); };
            _actionCenterItem.Click += delegate { Raise(ActionCenterClicked); };
            _aboutItem.Click += delegate { Raise(AboutClicked); };
            _exitItem.Click += delegate { Raise(ExitClicked); };

            // 分隔线按“常用 → 开关 → 状态 → 辅助”分组，避免十几项挤成一片。
            Menu.Items.Add(_settingsItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(_clickThroughItem);
            Menu.Items.Add(_topmostItem);
            Menu.Items.Add(_modeItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(_resetPositionItem);
            Menu.Items.Add(_actionCenterItem);
            Menu.Items.Add(_aboutItem);
            Menu.Items.Add(_exitItem);

            Menu.Opening += OnMenuOpening;
            Menu.Opened += OnMenuOpened;
            Menu.Closed += OnMenuClosed;
        }

        /// <summary>菜单本体；托盘通过 <c>ContextMenuStrip</c> 直接复用它，因此不额外包装。</summary>
        public ContextMenuStrip Menu { get; private set; }

        /// <summary>菜单当前是否可见；悬停展开逻辑用它避免重复弹出。</summary>
        public bool IsOpen
        {
            get { return Menu.Visible; }
        }

        /// <summary>
        /// 把控制器里的设置回写到菜单：置顶/穿透勾选，以及“当前状态：X”文案与子项勾选。
        /// 状态先经 <c>ReminderModes.Normalize</c> 归一化，非法值不会让两个子项都不勾。
        /// </summary>
        public void Sync(bool topmost, bool clickThrough, string currentMode)
        {
            _clickThroughItem.Checked = clickThrough;
            _topmostItem.Checked = topmost;

            string mode = ReminderModes.Normalize(currentMode);
            _modeItem.Text = "当前状态：" + ReminderModes.DisplayName(mode);
            _workModeItem.Checked = mode == ReminderModes.Work;
            _leisureModeItem.Checked = mode == ReminderModes.Leisure;
        }

        /// <summary>
        /// 在鼠标位置弹出菜单；已展开时直接返回，防止连点或悬停抖动导致重复弹出。
        /// 位置取系统光标而非窗口坐标，保证托盘与悬浮窗两处调用行为一致。
        /// </summary>
        public void ShowAtCursor()
        {
            if (Menu.Visible)
            {
                return;
            }
            Menu.Show(Cursor.Position);
        }

        /// <summary>
        /// 创建菜单项并套用统一外观：文字色、内边距、悬停提示。
        /// <c>CheckOnClick</c> 关掉，勾选状态一律由 <see cref="Sync"/> 从设置回写，避免点击与设置不同步。
        /// </summary>
        private ToolStripMenuItem CreateItem(string text, string toolTip)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ForeColor = Color.FromArgb(238, 238, 242);
            item.Padding = new Padding(0, 3, 0, 3);
            item.ToolTipText = toolTip;
            item.CheckOnClick = false;
            return item;
        }

        /// <summary>转发 Opening：控制器在展开瞬间刷新勾选，保证看到的是最新设置。</summary>
        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (MenuOpening != null)
            {
                MenuOpening(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 菜单展开后把原生窗口设为置顶：任务栏或游戏可能抢 TOPMOST，菜单需要自己再顶一次。
        /// 句柄尚未创建（Handle 为 0）时跳过，避免向无效窗口发消息。
        /// </summary>
        private void OnMenuOpened(object sender, EventArgs e)
        {
            if (Menu.Handle != IntPtr.Zero)
            {
                NativeMethods.SetTopmost(Menu.Handle, true);
            }
            if (MenuOpened != null)
            {
                MenuOpened(this, EventArgs.Empty);
            }
        }

        /// <summary>转发 Closed：控制器据此恢复悬浮窗的置顶守护。</summary>
        private void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            if (MenuClosed != null)
            {
                MenuClosed(this, EventArgs.Empty);
            }
        }

        /// <summary>统一的空安全事件触发；菜单事件不携带附加数据，发送者为 null 不影响订阅方。</summary>
        private static void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 释放菜单与自绘字体；<see cref="_disposed"/> 保证幂等。
        /// 先释放菜单再释放字体，因为菜单项在销毁过程中可能还要读取字体度量。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            Menu.Dispose();
            // 加粗字体是单独 new 出来的，必须单独释放，不能只依赖菜单释放。
            _settingsItem.Font.Dispose();
            _menuFont.Dispose();
        }

        /// <summary>
        /// 暗色菜单渲染器：只重写文字颜色与对勾绘制，其余背景/边框交给配色表。
        /// 用 <c>ToolStripProfessionalRenderer</c> 派生的好处是保留系统的高亮、键盘导航与无障碍行为。
        /// </summary>
        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {

            /// <summary>固定使用暗色配色表，并关闭圆角以贴合悬浮窗的直角风格。</summary>
            public DarkMenuRenderer()
                : base(new DarkMenuColorTable())
            {
                RoundedEdges = false;
            }

            /// <summary>
            /// 自绘文字：禁用项用低对比灰，选中项纯白，其余用正文色。
            /// 必须走 <c>TextRenderer.DrawText</c> 而不是 Graphics.DrawString，才能保持与系统一致的字体渲染。
            /// </summary>
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                Color color;
                // 禁用项与选中项都要单独取色，直接沿用主题色会在暗底上看不清。
                if (!e.Item.Enabled)
                {
                    color = Color.FromArgb(120, 120, 132);
                }
                else
                {
                    color = e.Item.Selected ? Color.White : Color.FromArgb(238, 238, 242);
                }
                TextRenderer.DrawText(
                    e.Graphics,
                    e.Text,
                    e.TextFont,
                    e.TextRectangle,
                    color,
                    e.TextFormat);
            }

            /// <summary>
            /// 手绘选中标记：在勾选区域内画一条两段折线代替系统对勾图标。
            /// 坐标按控件矩形比例计算，因此不同 DPI 与行高下都能保持居中；线宽下限 1.6 防止细到看不见。
            /// </summary>
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle bounds = e.ImageRectangle;
                // 用比例定位起笔点与两个拐点，避免写死像素导致高 DPI 下错位。
                float x = bounds.Left + bounds.Width * 0.26f;
                float y = bounds.Top + bounds.Height * 0.52f;
                using (Pen pen = new Pen(Color.FromArgb(255, 69, 58), Math.Max(1.6f, bounds.Height * 0.09f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    // 折线必须开抗锯齿，否则斜线会出现明显阶梯。
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
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
        /// 暗色菜单配色表：把下拉背景、边框、悬停/按下高亮、分隔线与勾选底色统一成悬浮窗的深灰系。
        /// 只覆盖实际会出现的几个属性，其余沿用基类默认值，减少与系统主题的耦合。
        /// </summary>
        private sealed class DarkMenuColorTable : ProfessionalColorTable
        {

            // ===== 调色板 =====

            /// <summary>菜单底色，与悬浮窗深灰背景一致。</summary>
            private static readonly Color Background = Color.FromArgb(32, 32, 38);

            /// <summary>菜单外边框，比底色略亮一档以便分辨边界。</summary>
            private static readonly Color Border = Color.FromArgb(58, 58, 68);

            /// <summary>悬停/按下高亮底色；只比背景亮一点，避免选中时过于刺眼。</summary>
            private static readonly Color Selected = Color.FromArgb(50, 50, 60);

            /// <summary>分隔线颜色，与边框同属一个亮度层级。</summary>
            private static readonly Color Separator = Color.FromArgb(55, 55, 64);

            // ===== 下拉面板 =====

            /// <summary>下拉面板背景色。</summary>
            public override Color ToolStripDropDownBackground
            {
                get { return Background; }
            }

            /// <summary>图标边距渐变起点；本菜单已关闭图像边距，仍需给值以免出现白条。</summary>
            public override Color ImageMarginGradientBegin
            {
                get { return Background; }
            }

            /// <summary>图标边距渐变中点。</summary>
            public override Color ImageMarginGradientMiddle
            {
                get { return Background; }
            }

            /// <summary>图标边距渐变终点。</summary>
            public override Color ImageMarginGradientEnd
            {
                get { return Background; }
            }

            // ===== 边框与高亮 =====

            /// <summary>菜单外框颜色。</summary>
            public override Color MenuBorder
            {
                get { return Border; }
            }

            /// <summary>菜单项边框；与高亮色相同，使悬停块看起来是一整块实色。</summary>
            public override Color MenuItemBorder
            {
                get { return Selected; }
            }

            /// <summary>鼠标悬停时的菜单项底色。</summary>
            public override Color MenuItemSelected
            {
                get { return Selected; }
            }

            /// <summary>悬停底色渐变的起点。</summary>
            public override Color MenuItemSelectedGradientBegin
            {
                get { return Selected; }
            }

            /// <summary>悬停底色渐变的终点；起止同色即得到纯色块。</summary>
            public override Color MenuItemSelectedGradientEnd
            {
                get { return Selected; }
            }

            /// <summary>按下状态渐变起点。</summary>
            public override Color MenuItemPressedGradientBegin
            {
                get { return Selected; }
            }

            /// <summary>按下状态渐变中点。</summary>
            public override Color MenuItemPressedGradientMiddle
            {
                get { return Selected; }
            }

            /// <summary>按下状态渐变终点。</summary>
            public override Color MenuItemPressedGradientEnd
            {
                get { return Selected; }
            }

            // ===== 分隔线与勾选 =====

            /// <summary>分隔线深色部分。</summary>
            public override Color SeparatorDark
            {
                get { return Separator; }
            }

            /// <summary>分隔线浅色部分；与深色相同，得到一条细实线。</summary>
            public override Color SeparatorLight
            {
                get { return Separator; }
            }

            /// <summary>勾选框背景色；本菜单对勾为手绘，这里只影响底色。</summary>
            public override Color CheckBackground
            {
                get { return Selected; }
            }

            /// <summary>勾选项处于选中态时的底色。</summary>
            public override Color CheckSelectedBackground
            {
                get { return Selected; }
            }

            /// <summary>勾选项处于按下态时的底色。</summary>
            public override Color CheckPressedBackground
            {
                get { return Selected; }
            }
        }
    }
}
