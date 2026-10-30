using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GameClock
{
    internal sealed class OverlayMenu : IDisposable
    {
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
        private bool _disposed;

        public event EventHandler SettingsClicked;
        public event EventHandler ClickThroughClicked;
        public event EventHandler TopmostClicked;
        public event EventHandler WorkModeClicked;
        public event EventHandler LeisureModeClicked;
        public event EventHandler ResetPositionClicked;
        public event EventHandler AboutClicked;
        public event EventHandler ExitClicked;
        public event EventHandler MenuOpening;
        public event EventHandler MenuOpened;
        public event EventHandler MenuClosed;

        public OverlayMenu()
        {
            _menuFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

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
            _aboutItem = CreateItem("关于", "查看版本号和编译号");
            _exitItem = CreateItem("退出", "关闭时间悬浮窗");

            _settingsItem.Font = new Font(_menuFont, FontStyle.Bold);

            _modeItem.DropDownItems.Add(_workModeItem);
            _modeItem.DropDownItems.Add(_leisureModeItem);
            _modeItem.DropDown.BackColor = Color.FromArgb(32, 32, 38);
            _modeItem.DropDown.ForeColor = Color.FromArgb(238, 238, 242);
            _modeItem.DropDown.Renderer = Menu.Renderer;

            _settingsItem.Click += delegate { Raise(SettingsClicked); };
            _clickThroughItem.Click += delegate { Raise(ClickThroughClicked); };
            _topmostItem.Click += delegate { Raise(TopmostClicked); };
            _workModeItem.Click += delegate { Raise(WorkModeClicked); };
            _leisureModeItem.Click += delegate { Raise(LeisureModeClicked); };
            _resetPositionItem.Click += delegate { Raise(ResetPositionClicked); };
            _aboutItem.Click += delegate { Raise(AboutClicked); };
            _exitItem.Click += delegate { Raise(ExitClicked); };

            Menu.Items.Add(_settingsItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(_clickThroughItem);
            Menu.Items.Add(_topmostItem);
            Menu.Items.Add(_modeItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(_resetPositionItem);
            Menu.Items.Add(_aboutItem);
            Menu.Items.Add(_exitItem);

            Menu.Opening += OnMenuOpening;
            Menu.Opened += OnMenuOpened;
            Menu.Closed += OnMenuClosed;
        }

        public ContextMenuStrip Menu { get; private set; }

        public bool IsOpen
        {
            get { return Menu.Visible; }
        }

        public void Sync(bool topmost, bool clickThrough, string currentMode)
        {
            _clickThroughItem.Checked = clickThrough;
            _topmostItem.Checked = topmost;

            string mode = ReminderModes.Normalize(currentMode);
            _modeItem.Text = "当前状态：" + ReminderModes.DisplayName(mode);
            _workModeItem.Checked = mode == ReminderModes.Work;
            _leisureModeItem.Checked = mode == ReminderModes.Leisure;
        }

        public void ShowAtCursor()
        {
            if (Menu.Visible)
            {
                return;
            }
            Menu.Show(Cursor.Position);
        }

        private ToolStripMenuItem CreateItem(string text, string toolTip)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ForeColor = Color.FromArgb(238, 238, 242);
            item.Padding = new Padding(0, 3, 0, 3);
            item.ToolTipText = toolTip;
            item.CheckOnClick = false;
            return item;
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (MenuOpening != null)
            {
                MenuOpening(this, EventArgs.Empty);
            }
        }

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

        private void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            if (MenuClosed != null)
            {
                MenuClosed(this, EventArgs.Empty);
            }
        }

        private static void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            Menu.Dispose();
            _settingsItem.Font.Dispose();
            _menuFont.Dispose();
        }

        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer()
                : base(new DarkMenuColorTable())
            {
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                Color color;
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

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle bounds = e.ImageRectangle;
                float x = bounds.Left + bounds.Width * 0.26f;
                float y = bounds.Top + bounds.Height * 0.52f;
                using (Pen pen = new Pen(Color.FromArgb(255, 69, 58), Math.Max(1.6f, bounds.Height * 0.09f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
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

        private sealed class DarkMenuColorTable : ProfessionalColorTable
        {
            private static readonly Color Background = Color.FromArgb(32, 32, 38);
            private static readonly Color Border = Color.FromArgb(58, 58, 68);
            private static readonly Color Selected = Color.FromArgb(50, 50, 60);
            private static readonly Color Separator = Color.FromArgb(55, 55, 64);

            public override Color ToolStripDropDownBackground
            {
                get { return Background; }
            }

            public override Color ImageMarginGradientBegin
            {
                get { return Background; }
            }

            public override Color ImageMarginGradientMiddle
            {
                get { return Background; }
            }

            public override Color ImageMarginGradientEnd
            {
                get { return Background; }
            }

            public override Color MenuBorder
            {
                get { return Border; }
            }

            public override Color MenuItemBorder
            {
                get { return Selected; }
            }

            public override Color MenuItemSelected
            {
                get { return Selected; }
            }

            public override Color MenuItemSelectedGradientBegin
            {
                get { return Selected; }
            }

            public override Color MenuItemSelectedGradientEnd
            {
                get { return Selected; }
            }

            public override Color MenuItemPressedGradientBegin
            {
                get { return Selected; }
            }

            public override Color MenuItemPressedGradientMiddle
            {
                get { return Selected; }
            }

            public override Color MenuItemPressedGradientEnd
            {
                get { return Selected; }
            }

            public override Color SeparatorDark
            {
                get { return Separator; }
            }

            public override Color SeparatorLight
            {
                get { return Separator; }
            }

            public override Color CheckBackground
            {
                get { return Selected; }
            }

            public override Color CheckSelectedBackground
            {
                get { return Selected; }
            }

            public override Color CheckPressedBackground
            {
                get { return Selected; }
            }
        }
    }
}
