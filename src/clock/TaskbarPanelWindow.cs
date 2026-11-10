// 任务栏时钟的“今日”面板：在原生 TrayClockWClass 上方弹出深色卡片，显示大号时间、
// 公历/农历日期、天气、当前状态与当天提醒/日程。
// 契约：数据只由 TaskbarClockWindow.BuildPanelModel 组装成 TaskbarPanelModel 传入，本窗口不读
// AppSettings、不算时间与日程，只负责排版与定位，因此可随时创建/销毁。
// 定位按“屏幕物理像素”回算：锚点用原生时钟的 RECT，尺寸用本窗口 DIP × DPI，二者不可混用。
// 已知坑：ShowActivated=false + WS_EX_NOACTIVATE 才不抢焦点；面板开合完全由父窗口的悬停计时器决定。
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GameClock
{

    /// <summary>
    /// 任务栏时钟展开的“今日”面板：大号时间、农历、天气与当天提醒/日程。
    /// 面板不抢焦点，鼠标移开后由任务栏时钟负责关闭。
    /// </summary>
    internal sealed class TaskbarPanelWindow : Window
    {

        // ===== 尺寸与控件 =====

        /// <summary>面板宽度固定为 340 DIP；高度由内容决定（SizeToContent.Height）。</summary>
        private const double PanelWidth = 340.0;

        /// <summary>大号时间（主视觉，34px SemiBold）。</summary>
        private readonly TextBlock _timeText;

        /// <summary>公历日期行（含中文星期）。</summary>
        private readonly TextBlock _dateText;

        /// <summary>农历行，由格式引擎的 NN / MD 令牌生成。</summary>
        private readonly TextBlock _lunarText;

        /// <summary>天气行；空串时整体折叠，不占高度。</summary>
        private readonly TextBlock _weatherText;

        /// <summary>无提醒/日程时的灰色占位文案，复用同一实例。</summary>
        private readonly TextBlock _emptyText;

        /// <summary>日程行容器，内容变化时整体清空重建。</summary>
        private readonly StackPanel _agendaPanel;

        /// <summary>分隔线，位于顶部信息与日程列表之间。</summary>
        private readonly Border _agendaSeparator;

        /// <summary>当前状态行（工作/休息…），决定哪些提醒会出现在列表里。</summary>
        private readonly TextBlock _modeText;

        // ===== 内容缓存与去重 =====

        /// <summary>上一帧 Agenda 的签名；相同则不重建控件树。</summary>
        private string _lastSignature;

        /// <summary>上一帧天气文本；用于判断内容高度是否变化、需要重新定位。</summary>
        private string _lastWeatherText;

        // ===== 定位锚点 =====

        /// <summary>是否已经 Show 过：首次显示要 UpdateLayout 才能拿到真实尺寸。</summary>
        private bool _positioned;

        /// <summary>原生时钟矩形（屏幕物理像素），面板贴在它上方。</summary>
        private NativeMethods.RECT _anchorRect;

        /// <summary>锚点所在显示器的 DPI 缩放，用于把 DIP 尺寸换回物理像素。</summary>
        private double _anchorDpi = 1.0;

        /// <summary>是否已收到过锚点；没有锚点时 Reposition 直接返回。</summary>
        private bool _hasAnchor;

        /// <summary>
        /// 构造面板：建立窗口样式（无边框、透明、置顶、不抢焦点）、深色卡片与全部文本控件。
        /// 构造时不显示也不定位，由 TaskbarClockWindow.ShowPanel → ShowNear 决定何时出现。
        /// 控件树一次性搭好，之后 SetModel 只改文字，避免每次展开都重建。
        /// </summary>
        public TaskbarPanelWindow()
        {
            // 窗口样式：无边框 + 允许透明 + 不占任务栏 + 不抢焦点；
            // 配合 ShowActivated=false，点面板不会把焦点从正在用的应用抢走。
            Title = "GameClockTaskbarPanel";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = PanelWidth;
            Left = -10000;
            Top = -10000;
            // 字体固定用微软雅黑 UI：面板是独立浮层，不跟随系统任务栏字体。
            FontFamily = new FontFamily("Microsoft YaHei UI");

            // 卡片：深色圆角底 + 1px 描边，与外层透明窗口一起形成贴在任务栏上的浮层观感。
            Border card = new Border();
            card.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x22));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x46));
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(14);
            card.Padding = new Thickness(20, 18, 20, 14);
            card.Margin = new Thickness(12);
            // 投影半径大于外边距（12），卡片边缘才不会把阴影裁掉。
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 26;
            shadow.ShadowDepth = 7;
            shadow.Opacity = 0.5;
            shadow.Color = Colors.Black;
            card.Effect = shadow;

            // 文本列：时间 → 日期 → 农历 → 天气 → 状态 → 分隔线 → 日程列表，自上而下堆叠。
            StackPanel root = new StackPanel();

            // 时间 34px SemiBold 作为视觉主体；NoWrap 防止窄屏折行，宽度由固定 PanelWidth 兜住。
            _timeText = UiTheme.MakeText("00:00:00", 34, UiTheme.TextPrimary, FontWeights.SemiBold);
            _timeText.TextWrapping = TextWrapping.NoWrap;
            root.Children.Add(_timeText);

            // 日期用主文字色、字号接近正文，紧贴时间下方。
            _dateText = UiTheme.MakeText("", 13, UiTheme.TextPrimary, FontWeights.Normal);
            _dateText.Margin = new Thickness(0, 2, 0, 0);
            root.Children.Add(_dateText);

            // 农历用次要色：信息价值低于公历，视觉上退一层。
            _lunarText = UiTheme.MakeText("", 12, UiTheme.TextSecondary, FontWeights.Normal);
            _lunarText.Margin = new Thickness(0, 3, 0, 0);
            root.Children.Add(_lunarText);

            // 天气用强调色，让“今天天气”在深色卡片里最先被看到。
            _weatherText = UiTheme.MakeText("", 12.5, UiTheme.Accent, FontWeights.Normal);
            _weatherText.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(_weatherText);

            // 状态行最弱（灰色小字）：它是筛选条件的说明，不是主角。
            _modeText = UiTheme.MakeText("", 11, UiTheme.TextMuted, FontWeights.Normal);
            _modeText.Margin = new Thickness(0, 3, 0, 0);
            root.Children.Add(_modeText);

            // 分隔线用固定 1px 深灰而不是主题色：它只是视觉分界，不该抢眼。
            _agendaSeparator = new Border();
            _agendaSeparator.Height = 1;
            _agendaSeparator.Background = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x40));
            _agendaSeparator.Margin = new Thickness(0, 14, 0, 12);
            root.Children.Add(_agendaSeparator);

            // 日程容器：行由 RebuildAgenda 动态增删，构造时为空。
            _agendaPanel = new StackPanel();
            root.Children.Add(_agendaPanel);

            // 空态文案只建一次，RebuildAgenda 在列表为空时把同一实例挂进容器。
            _emptyText = UiTheme.MakeText("今天没有启用的提醒或日程。", 12, UiTheme.TextMuted, FontWeights.Normal);
            _emptyText.Margin = new Thickness(0, 2, 0, 2);

            // 底部工具行：左侧灰色操作提示，右侧「打开设置」按钮，用 Grid 两列对齐。
            // 提示里写明左键是“日历”而不是展开面板，避免用户点了以后找不到面板。
            TextBlock hint = UiTheme.MakeText("悬停展开 · 左键日历 · 右键菜单", 10.5, UiTheme.TextMuted, FontWeights.Normal);
            hint.VerticalAlignment = VerticalAlignment.Center;

            // 按钮只抛事件、不直接开设置窗口：本窗口不持有控制器引用，便于单独销毁。
            Button settingsButton = new Button();
            settingsButton.Content = "打开设置";
            settingsButton.Style = UiTheme.GhostButtonStyle;
            settingsButton.FontSize = 11.5;
            settingsButton.Padding = new Thickness(10, 3, 10, 3);
            settingsButton.HorizontalAlignment = HorizontalAlignment.Right;
            settingsButton.VerticalAlignment = VerticalAlignment.Center;
            // 先 Hide() 再抛事件：设置窗口会抢焦点，留着面板会停在设置窗下方不消失。
            settingsButton.Click += delegate
            {
                Hide();
                // 事件可能尚无订阅者（控制器未接线），判空后再触发。
                EventHandler handler = SettingsRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            // 两列：提示占满剩余宽度（Star），按钮按自身尺寸靠右（Auto）。
            Grid footerRow = new Grid();
            footerRow.Margin = new Thickness(0, 14, 0, 0);
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerRow.Children.Add(hint);
            Grid.SetColumn(hint, 0);
            footerRow.Children.Add(settingsButton);
            Grid.SetColumn(settingsButton, 1);
            root.Children.Add(footerRow);

            // 卡片作为窗口内容；Margin=12 给投影留出落点。
            card.Child = root;
            Content = card;
        }

        /// <summary>
        /// 面板里点击「打开设置」时触发；由 TaskbarClockWindow 转发给控制器。
        /// 订阅者可能不存在，触发前需判空。
        /// </summary>
        public event EventHandler SettingsRequested;

        /// <summary>
        /// 刷新面板内容。时间每秒更新，提醒/日程列表只在内容变化时重建。
        /// </summary>
        public void SetModel(TaskbarPanelModel model)
        {
            // 控制器可能在启动早期传入空模型，直接忽略，保留上一次画面。
            if (model == null)
            {
                return;
            }

            // 时间/日期/农历/状态每帧覆盖，代价极低，不必判等。
            _timeText.Text = model.TimeText;
            _dateText.Text = model.DateText;
            _lunarText.Text = model.LunarText;
            _modeText.Text = model.ModeText;

            // 天气可能为 null（取数失败）：统一成空串，下面按长度决定显隐。
            string weather = model.WeatherText ?? string.Empty;
            bool weatherChanged = !string.Equals(weather, _lastWeatherText, StringComparison.Ordinal);
            _weatherText.Text = weather;
            // 空天气整体折叠，否则会留下一行空白把卡片撑高。
            _weatherText.Visibility = weather.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            // 日程列表重建要清空并重建控件树，用 Signature 判等，只有内容真变才做。
            string signature = model.Signature;
            bool agendaChanged = !string.Equals(signature, _lastSignature, StringComparison.Ordinal);
            if (agendaChanged)
            {
                _lastSignature = signature;
                RebuildAgenda(model);
            }

            // 内容变化会改变卡片高度：已显示且已定位时才重新量尺寸并重排。
            if ((agendaChanged || weatherChanged || _lastWeatherText == null) && _hasAnchor && IsVisible)
            {
                UpdateLayout();
                Reposition();
            }
            // 记录本次天气，作为下一帧“高度是否需要重排”的比较基准。
            _lastWeatherText = weather;
        }

        /// <summary>
        /// 把面板放到任务栏时钟上方（任务栏在顶部时放到下方）。
        /// </summary>
        public void ShowNear(NativeMethods.RECT clockRect, double dpiScale)
        {
            // 记录锚点与 DPI：Reposition 只用这两个原始值换算，不依赖窗口当前状态。
            _anchorRect = clockRect;
            // DPI 传入 0 或负值时按 1.0 兜底，避免除零与文字尺寸异常。
            _anchorDpi = dpiScale > 0.1 ? dpiScale : 1.0;
            _hasAnchor = true;

            // 首次显示才真正 Show；之后只做 Show/Hide，反复开关不重建控件树。
            if (!_positioned)
            {
                // 先 Show 再 UpdateLayout：ActualWidth/Height 要等布局跑完才可信。
                Show();
                UpdateLayout();
                _positioned = true;
            }
            // 已创建但被藏起来（点击外部收起）：重新 Show 即可，不必再布局。
            else if (!IsVisible)
            {
                Show();
                UpdateLayout();
            }

            // 定位走 SetWindowPos 直接写物理像素，绕开 WPF 的 DIP 换算。
            Reposition();
            // 面板可能被任务栏重绘压到下层，显示后立刻抬回 TOPMOST 组首位。
            NativeMethods.SetTopmost(new WindowInteropHelper(this).Handle, true);
        }

        /// <summary>
        /// 按锚点矩形把面板摆到原生时钟正上方；上方空间不足（任务栏在屏幕顶部）时改摆到下方。
        /// 只在 ShowNear 或内容高度变化后调用，依赖 _hasAnchor 与已完成的布局。
        /// </summary>
        private void Reposition()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // 句柄未建立或还没有锚点时无法计算，直接返回，避免把面板丢到 (0,0)。
            if (hwnd == IntPtr.Zero || !_hasAnchor)
            {
                return;
            }

            // ActualWidth/Height 是 DIP，换成物理像素才能与锚点矩形同坐标系。
            int width = (int)Math.Round(ActualWidth * _anchorDpi);
            int height = (int)Math.Round(ActualHeight * _anchorDpi);
            // 右对齐原生时钟：面板右边缘贴住时钟右边缘，视觉上与任务栏按钮对齐。
            int left = _anchorRect.Right - width;
            int top = _anchorRect.Top - height - 8;
            // 上方放不下（top < 8，任务栏在屏幕顶部）就翻到时钟下方。
            if (top < 8)
            {
                top = _anchorRect.Bottom + 8;
            }
            // 左侧越界时贴住屏幕左边距，避免面板被裁掉一半。
            if (left < 8)
            {
                left = 8;
            }

            // 不改尺寸、不激活窗口；SWP_SHOWWINDOW 保证从隐藏状态恢复时立即出现。
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                left,
                top,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        /// <summary>
        /// 重建日程列表：先清空容器，空列表挂占位文案，否则逐行构建。
        /// 由 SetModel 在 Signature 变化时调用；行数已由 TaskbarClockWindow 限到 6 条。
        /// </summary>
        private void RebuildAgenda(TaskbarPanelModel model)
        {
            // 整体清空比逐行比对简单可靠：行是轻量 Grid，重建开销可忽略。
            _agendaPanel.Children.Clear();

            List<TaskbarAgendaLine> lines = model.Agenda;
            // 空列表用同一个 _emptyText 占位，反复重建不会累积控件。
            if (lines == null || lines.Count == 0)
            {
                _agendaPanel.Children.Add(_emptyText);
                return;
            }

            // 顺序即 BuildPanelModel 排好的时间序，此处不再排序。
            for (int i = 0; i < lines.Count; i++)
            {
                _agendaPanel.Children.Add(BuildAgendaRow(lines[i]));
            }
        }

        /// <summary>
        /// 构建一行日程：左列固定 46 DIP 放时间，右列自适应放正文。
        /// </summary>
        /// <param name="line">已排好序的一行数据，可能已过期（Dimmed）。</param>
        /// <returns>可直接加入日程容器的行元素。</returns>
        private UIElement BuildAgendaRow(TaskbarAgendaLine line)
        {
            // 左列固定 46 DIP：HH:mm 加一点余量，保证两行时间串对齐。
            Grid row = new Grid();
            row.Margin = new Thickness(0, 0, 0, 6);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 时间用强调色 + SemiBold，便于快速扫读。
            TextBlock time = UiTheme.MakeText(line.Time, 12, UiTheme.Accent, FontWeights.SemiBold);
            time.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(time);
            Grid.SetColumn(time, 0);

            TextBlock text = UiTheme.MakeText(line.Text, 12, UiTheme.TextPrimary, FontWeights.Normal);
            text.VerticalAlignment = VerticalAlignment.Top;
            // 过期条目整行转灰：时间与正文同色，视觉上不再抢注意力。
            if (line.Dimmed)
            {
                text.Foreground = UiTheme.TextMuted;
                time.Foreground = UiTheme.TextMuted;
            }
            row.Children.Add(text);
            Grid.SetColumn(text, 1);
            return row;
        }
    }

    /// <summary>
    /// 日程列表的一行：时间 + 正文 + 是否过期。
    /// 由 TaskbarClockWindow 组装并排序，面板只负责画，不参与触发判定。
    /// </summary>
    internal sealed class TaskbarAgendaLine
    {

        /// <summary>创建一行日程数据；只在 TaskbarClockWindow.BuildPanelModel 内部使用。</summary>
        /// <param name="time">HH:mm 文本，直接显示，不在此处解析。</param>
        /// <param name="text">正文；日程已带类别前缀。</param>
        /// <param name="dimmed">true = 时间已过，整行置灰。</param>
        public TaskbarAgendaLine(string time, string text, bool dimmed)
        {
            Time = time;
            Text = text;
            Dimmed = dimmed;
        }

        /// <summary>显示用时间文本（HH:mm），不参与解析。</summary>
        public string Time { get; private set; }

        /// <summary>显示用正文。</summary>
        public string Text { get; private set; }

        /// <summary>是否已过期；只影响颜色，不过滤条目。</summary>
        public bool Dimmed { get; private set; }
    }

    /// <summary>
    /// 今日面板一帧的全部显示数据：由 TaskbarClockWindow.BuildPanelModel 组装，SetModel 消费。
    /// 纯数据载体，不持有窗口引用、不计算时间——刷新时整份替换，避免面板自己缓存过期状态。
    /// </summary>
    internal sealed class TaskbarPanelModel
    {

        // ===== 顶部信息（时间/日期/农历/天气/状态） =====

        /// <summary>大号时间文本（HH:mm:ss）。</summary>
        public string TimeText { get; set; }

        /// <summary>公历日期文本，含中文星期。</summary>
        public string DateText { get; set; }

        /// <summary>农历文本；格式串不合法时为空。</summary>
        public string LunarText { get; set; }

        /// <summary>天气文本；空串表示未取到，面板折叠该行。</summary>
        public string WeatherText { get; set; }

        /// <summary>当前状态显示文案（已带“当前状态：”前缀）。</summary>
        public string ModeText { get; set; }

        // ===== 日程列表与去重签名 =====

        /// <summary>与日程内容相关的签名；相同则面板不重建控件树。</summary>
        public string Signature { get; set; }

        /// <summary>当天提醒/日程，已按时间排序并截断到 6 条。</summary>
        public List<TaskbarAgendaLine> Agenda { get; set; }
    }
}
