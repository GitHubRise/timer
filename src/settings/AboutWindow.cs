// 关于窗口（AboutWindow）：产品名、版本/编译号/编译时间（来自构建生成的 BuildInfo）、简介与关闭入口。
// 调用方：设置窗口或托盘菜单打开后以 ShowDialog 显示；本窗口不产生任何持久化副作用。
// 边界：只读 BuildInfo 常量，不读写 AppSettings、不访问网络与注册表。
// 不变量：无边框窗口靠标题栏 MouseLeftButtonDown → DragMove() 拖动；Esc 与「关闭」按钮都走 Close()。
// 已知坑：DragMove 在没有按下左键时调用会抛 InvalidOperationException，必须就地吞掉并写明原因。

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GameClock
{
    /// <summary>
    /// 关于窗口：展示产品名、版本/编译号/编译时间与功能简介，只读不写任何持久化状态。
    /// </summary>
    internal sealed class AboutWindow : Window
    {
        /// <summary>
        /// 构造关于窗口：无边框、居中于 owner、置顶显示；界面在构造期一次性建好，之后不再变化。
        /// </summary>
        public AboutWindow()
        {
            Title = "关于";
            // Title 只给任务栏与辅助技术用；界面上看到的是自绘的 headerText。
            // 480×540 是内容「图标 + 版本卡片 + 简介」在 100% 缩放下的自然高度，改内容需同步核对。
            Width = 480;
            Height = 540;
            ResizeMode = ResizeMode.NoResize;
            // 固定尺寸：内容一次排好就不再变化，允许缩放只会留下大片空白。
            // 自绘标题栏，因此不要系统边框；AllowsTransparency 保持 false，避免子控件走软件渲染。
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            // 不进任务栏：它是设置窗口的附属对话框，单独占一个任务栏位很突兀。
            // 置顶：从设置窗口以 ShowDialog 打开时不要被主窗口遮住。
            Topmost = true;
            Background = UiTheme.WindowBackground;
            // 背景/前景/字体统一从 UiTheme 取，避免与设置窗口出现两种深色基调。
            Foreground = UiTheme.TextPrimary;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            Content = BuildContent();
            // 内容一次性构建：本窗口没有任何动态数据（版本号在编译期确定），不需要重新布局。
            // Esc 关闭挂在窗口级 KeyDown 上，焦点在任意子控件时都能生效。
            KeyDown += delegate(object sender, KeyEventArgs e)

            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
        }

        /// <summary>
        /// 构造窗口内容：标题栏（可拖动 + 关闭）、中部的图标/版本信息卡/简介、底部关闭按钮。
        /// </summary>
        private UIElement BuildContent()
        {
            // 三行布局：标题栏与页脚固定高度，中间内容行吃掉剩余空间；窗口不可缩放（ResizeMode.NoResize）。
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border header = new Border();
            // 标题栏与页脚同色（0x17171C），比内容区深一档，形成「上框 + 内容 + 下框」的结构。
            header.Background = UiTheme.Frozen(0x17, 0x17, 0x1C);
            header.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.Padding = new Thickness(24, 13, 14, 13);
            // 左 24 与内容区左边距对齐；右侧 14 是给 30px 关闭按钮留的视觉留白（不是按钮本身宽度）。
            // 拖动整条标题栏（不只是文字），与设置窗口行为一致。
            header.MouseLeftButtonDown += delegate
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException ex)
                {
                    // 没有按住左键时调 DragMove 会抛异常（键盘触发、或双击第二下时按钮已抬起）；不影响功能，只记录。
                    AppLog.Error("Settings", "关于窗口拖动失败：DragMove 未处于左键按下状态。", ex);
                }
            };
            Grid headerGrid = new Grid();
            // 两列：标题星号吃满、关闭按钮 Auto 贴右。
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock headerText = UiTheme.MakeText("关于", 14, UiTheme.TextPrimary, FontWeights.SemiBold);
            headerText.VerticalAlignment = VerticalAlignment.Center;
            headerGrid.Children.Add(headerText);

            Button close = new Button();
            // 用「×」字符而不是 SVG：18px 字形的视觉重心在 30px 按钮里刚好居中，也省一次图标解析。
            close.Content = "×";
            close.Style = UiTheme.GhostButtonStyle;
            close.Width = 30;
            close.Height = 30;
            close.Padding = new Thickness(0);
            close.FontSize = 18;
            close.ToolTip = "关闭";
            close.Click += delegate { Close(); };
            AutomationProperties.SetAutomationId(close, "AboutCloseButton");
            headerGrid.Children.Add(close);
            Grid.SetColumn(close, 1);
            header.Child = headerGrid;
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            StackPanel content = new StackPanel();
            // 上下留白不对称（22 / 18）：下方还要接页脚，多留一点才不会显得内容贴底。
            content.Margin = new Thickness(28, 22, 28, 18);

            Grid identity = new Grid();
            // 78px 图标列 = 64px 画布 + 14px 间隙；名称列吃满剩余宽度。
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid clockIcon = BuildClockIcon();
            clockIcon.VerticalAlignment = VerticalAlignment.Top;
            identity.Children.Add(clockIcon);
            Grid.SetColumn(clockIcon, 0);

            StackPanel names = new StackPanel();
            // 名称组上/左各偏移 4px：让三行文字的视觉左边界贴近图标圆环切线，而不是画布边缘。
            names.Margin = new Thickness(4, 4, 0, 0);
            TextBlock product = UiTheme.MakeText("桌面时间悬浮窗", 21, UiTheme.TextPrimary, FontWeights.Bold);
            product.Margin = new Thickness(0, 0, 0, 4);
            names.Children.Add(product);
            TextBlock english = UiTheme.MakeText("GameClock", 11.5, UiTheme.TextMuted, FontWeights.Normal);
            english.Margin = new Thickness(0, 0, 0, 5);
            names.Children.Add(english);
            TextBlock tagline = UiTheme.MakeText(
                "面向 Windows 10/11 的桌面时间工具",
                11.5,
                UiTheme.TextSecondary,
                FontWeights.Normal);
            names.Children.Add(tagline);
            identity.Children.Add(names);
            Grid.SetColumn(names, 1);
            content.Children.Add(identity);

            // 版本卡片：值全部来自构建生成的 BuildInfo，本文件不硬编码版本号。
            Border infoCard = UiTheme.MakeCard();
            infoCard.Margin = new Thickness(0, 20, 0, 12);
            StackPanel info = new StackPanel();
            // 卡片内先放小节标题，再逐行追加「标签 + 值」，行数由构建信息决定。
            info.Children.Add(UiTheme.MakeSectionTitle("版本信息"));

            AddInfoRow(info, "版本号", BuildInfo.Version);
            // 未走 build.ps1 的本地手编产物没有编译号，用 dev 占位而不是显示空白。
            AddInfoRow(info, "编译号", string.IsNullOrEmpty(BuildInfo.BuildNumber) ? "dev" : BuildInfo.BuildNumber);
            // 旧构建产物可能没有编译时间，缺失时整行不显示，避免出现「编译时间：（空）」。
            if (!string.IsNullOrEmpty(BuildInfo.BuildDate))
            {
                AddInfoRow(info, "编译时间", BuildInfo.BuildDate);
            }
            AddInfoRow(info, "运行环境", ".NET Framework 4.8 · WPF");
            // 运行环境写死：本程序只以 .NET Framework 4.8 + WPF 形态分发，不存在其它组合。

            infoCard.Child = info;
            content.Children.Add(infoCard);

            TextBlock description = UiTheme.MakeText(
                // 固化两句话：功能范围 + 时区默认值；功能有增减时这里要同步改。
                "支持 24 小时制、全球时区、天气、工作/娱乐提醒与日程管理。"
                + "默认 UTC+08:00，所有提醒和日程均可按当前时区触发。",
                12,
                UiTheme.TextSecondary,
                FontWeights.Normal);
            description.TextWrapping = TextWrapping.Wrap;
            description.LineHeight = 20;
            // 12px 字号配 20px 行高（约 1.67 倍）：中文密排在暗色底上才不会显得拥挤。
            description.Margin = new Thickness(2, 0, 2, 12);
            content.Children.Add(description);

            TextBlock features = UiTheme.MakeText(
                // 功能关键词一行罗列，用户扫一眼就能确认当前版本包含哪些能力。
                "时间显示 · 天气 · 定时提醒 · 日程 · 鼠标穿透 · 开机自启",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            features.TextWrapping = TextWrapping.Wrap;
            features.Margin = new Thickness(2, 0, 2, 0);
            content.Children.Add(features);

            TextBlock copyright = UiTheme.MakeText(
                // 版权行上留 18px，与功能行拉开层次，避免整段文字糊成一片。
                "Copyright © 2026 GameClock. All rights reserved.",
                10.5,
                UiTheme.TextMuted,
                FontWeights.Normal);
            copyright.Margin = new Thickness(2, 18, 2, 0);
            content.Children.Add(copyright);

            root.Children.Add(content);
            Grid.SetRow(content, 1);

            Border footer = new Border();
            // 页脚与标题栏同色同描边，把内容区上下夹住，窗口看起来是闭合的。
            footer.Background = UiTheme.Frozen(0x17, 0x17, 0x1C);
            footer.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(24, 12, 24, 12);
            StackPanel actions = new StackPanel();
            // 单按钮场景下靠右比居中更符合 Windows 对话框惯例。
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            Button closeButton = new Button();
            // 主按钮 88×34：比标题栏的「×」更醒目，是主要的退出入口。
            closeButton.Content = "关闭";
            closeButton.Style = UiTheme.PrimaryButtonStyle;
            closeButton.MinWidth = 88;
            closeButton.Height = 34;
            closeButton.Click += delegate { Close(); };
            AutomationProperties.SetAutomationId(closeButton, "AboutOkButton");
            actions.Children.Add(closeButton);
            footer.Child = actions;
            root.Children.Add(footer);
            Grid.SetRow(footer, 2);

            return root;
        }

        /// <summary>
        /// 用几何图形现画一个 64×64 的时钟图标（表圈、时针、分针、轴点）。
        /// 用 Shape 而不是图片资源：缩放不糊，主题改色时只改这里的画刷。
        /// </summary>
        private static Grid BuildClockIcon()
        {
            // 64×64 画布：表圈直径 58，加上两侧各 3px 描边正好贴边，再大就会被 Grid 裁掉。
            Grid host = new Grid();
            host.Width = 64;
            host.Height = 64;

            // 表圈：只描边不填充，颜色取 0x2EA8FF（品牌蓝），描边 3px 与指针同宽。
            Ellipse ring = new Ellipse();
            ring.Width = 58;
            ring.Height = 58;
            ring.Stroke = UiTheme.Frozen(0x2E, 0xA8, 0xFF);
            ring.StrokeThickness = 3;
            ring.HorizontalAlignment = HorizontalAlignment.Center;
            ring.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(ring);

            // 坐标系原点在画布中心 (32,32)；时针笔直向上 16px，长度约占半径的 55%。
            Line hour = new Line();
            hour.X1 = 32;
            hour.Y1 = 32;
            hour.X2 = 32;
            hour.Y2 = 16;
            hour.Stroke = UiTheme.Frozen(0x2E, 0xA8, 0xFF);
            hour.StrokeThickness = 3;
            // 圆头线帽：短线段用方头显得生硬，圆头更接近真实指针。
            hour.StrokeStartLineCap = PenLineCap.Round;
            hour.StrokeEndLineCap = PenLineCap.Round;
            host.Children.Add(hour);

            // 分针指向右下（约 4 点方向），用强调色与时针区分，端点落在 (44,38)。
            Line minute = new Line();
            minute.X1 = 32;
            minute.Y1 = 32;
            minute.X2 = 44;
            minute.Y2 = 38;
            minute.Stroke = UiTheme.Accent;
            minute.StrokeThickness = 3;
            minute.StrokeStartLineCap = PenLineCap.Round;
            minute.StrokeEndLineCap = PenLineCap.Round;
            host.Children.Add(minute);

            // 轴点：6px 实心圆，压住两根线的接缝，避免视觉上出现断口。
            Ellipse center = new Ellipse();
            center.Width = 6;
            center.Height = 6;
            center.Fill = UiTheme.TextPrimary;
            center.HorizontalAlignment = HorizontalAlignment.Center;
            center.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(center);

            return host;
        }

        /// <summary>
        /// 往版本卡片追加一行「标签 + 值」，标签列宽固定，值列吃掉剩余宽度。
        /// </summary>
        private static void AddInfoRow(StackPanel parent, string label, string value)
        {
            // 上下各 3px 的呼吸间距；行距靠外边距而不是 LineHeight，便于单行独立排版。
            Grid row = new Grid();
            row.Margin = new Thickness(0, 3, 0, 3);
            // 88px 是「标签最长项（运行环境/编译时间）+ 缓冲」的经验宽度，改文案时注意别撑破。
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 标签弱化（TextMuted + 11.5px），让值成为视觉焦点。
            TextBlock labelText = UiTheme.MakeText(label, 11.5, UiTheme.TextMuted, FontWeights.Normal);
            labelText.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(labelText);

            // 值统一 Consolas：版本号/编译号里数字与点号多，等宽字体对齐更整齐。
            TextBlock valueText = UiTheme.MakeText(value, 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            valueText.FontFamily = new FontFamily("Consolas");
            valueText.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(valueText);
            Grid.SetColumn(valueText, 1);

            parent.Children.Add(row);
            // 直接追加到调用方面板，本方法不持有任何状态，可安全重复调用。
        }
    }
}
