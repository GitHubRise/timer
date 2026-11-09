// 关于对话框：展示产品名、版本号、编译号/时间、运行环境与功能清单。
// 由设置窗口底部“关于”入口或托盘菜单创建，模态显示（ShowDialog），关闭即弃、不复用实例。
// 内容全部来自构建期生成的 BuildInfo 与代码内常量，不读 AppSettings、不访问网络。
// 布局与主设置窗一致：无边框 + 自绘标题栏（可拖动）+ 滚动内容区 + 固定按钮栏，样式统一取自 UiTheme。

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
    /// 关于窗口：产品标识、版本信息卡片与功能说明的静态展示页，无输入、无持久化。
    /// 生命周期极短，调用方以 <see cref="Window.ShowDialog"/> 打开，Esc 或“关闭”按钮退出。
    /// 版本号取自构建期生成的 <see cref="BuildInfo"/>，因此界面文案不需要随发版手改。
    /// </summary>
    internal sealed class AboutWindow : Window
    {

        /// <summary>
        /// 构造并一次性搭好界面：窗口固定尺寸、无边框、居中于所有者、不占任务栏。
        /// 构造期只组装可视树，不触发任何 IO；Esc 关闭由 <see cref="Window.KeyDown"/> 兜底。
        /// </summary>
        public AboutWindow()
        {
            // ===== 窗口外观：固定 480×540，尺寸按内容估算，正常缩放下不会出现滚动条 =====
            Title = "关于";
            Width = 480;
            Height = 540;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Topmost = true;
            Background = UiTheme.WindowBackground;
            Foreground = UiTheme.TextPrimary;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            // 可视树在构造期一次建好：本窗没有数据绑定也不刷新内容，之后不再增删元素。
            Content = BuildContent();
            // 键盘关闭兜底：无边框窗口没有系统关闭按钮，Esc 是唯一纯键盘退出口。
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
        /// 构建整窗布局：三行网格 = 标题栏 / 内容区 / 按钮栏。
        /// 内容区不套滚动条——条目数量与文字长度都是固定的，窗口高度已按内容选定。
        /// </summary>
        /// <returns>可直接赋给 <see cref="Window.Content"/> 的根元素。</returns>
        private UIElement BuildContent()
        {
            // 中间行 Star：窗口被系统缩放或字号变化时，多余高度留给内容区而不是压扁按钮栏。
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ===== 标题栏：自绘，整条可拖动 =====
            Border header = new Border();
            header.Background = UiTheme.Frozen(0x17, 0x17, 0x1C);
            header.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.Padding = new Thickness(24, 13, 14, 13);

            /// <summary>在标题栏任意位置按下左键即拖动窗口。</summary>
            header.MouseLeftButtonDown += delegate
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // 鼠标已松开时 DragMove 会抛此异常；拖动未发生也无副作用，可安全吞掉。
                }
            };
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock headerText = UiTheme.MakeText("关于", 14, UiTheme.TextPrimary, FontWeights.SemiBold);
            headerText.VerticalAlignment = VerticalAlignment.Center;
            headerGrid.Children.Add(headerText);

            // 关闭图标按钮：幽灵样式，30×30 方形热区，与日程对话框保持一致。
            Button close = new Button();
            close.Content = "×";
            close.Style = UiTheme.GhostButtonStyle;
            close.Width = 30;
            close.Height = 30;
            close.Padding = new Thickness(0);
            close.FontSize = 18;
            close.ToolTip = "关闭";

            /// <summary>标题栏关闭按钮直接关窗。</summary>
            close.Click += delegate { Close(); };
            AutomationProperties.SetAutomationId(close, "AboutCloseButton");
            headerGrid.Children.Add(close);
            Grid.SetColumn(close, 1);
            header.Child = headerGrid;
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            // ===== 内容区：标识区 + 版本卡片 + 说明文字 =====
            StackPanel content = new StackPanel();
            content.Margin = new Thickness(28, 22, 28, 18);

            // 标识区：左图标（78 宽固定列）+ 右名称与标语。
            Grid identity = new Grid();
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid clockIcon = BuildClockIcon();
            // 顶部对齐，避免右侧文字行数变化时图标上下浮动。
            clockIcon.VerticalAlignment = VerticalAlignment.Top;
            identity.Children.Add(clockIcon);
            Grid.SetColumn(clockIcon, 0);

            StackPanel names = new StackPanel();
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

            // 版本卡片：复用通用圆角卡片，逐行追加标签/值对。
            Border infoCard = UiTheme.MakeCard();
            infoCard.Margin = new Thickness(0, 20, 0, 12);
            StackPanel info = new StackPanel();
            info.Children.Add(UiTheme.MakeSectionTitle("版本信息"));

            // 四行固定顺序：版本号 → 编译号 → 编译时间（可选）→ 运行环境；行高由 AddInfoRow 统一控制。
            AddInfoRow(info, "版本号", BuildInfo.Version);
            // 本地开发构建可能没有编译号，此时显示 dev 而不是空行。
            AddInfoRow(info, "编译号", string.IsNullOrEmpty(BuildInfo.BuildNumber) ? "dev" : BuildInfo.BuildNumber);
            // 编译时间由构建脚本可选填充，缺失就整行省略，不留空白占位。
            if (!string.IsNullOrEmpty(BuildInfo.BuildDate))
            {
                AddInfoRow(info, "编译时间", BuildInfo.BuildDate);
            }
            AddInfoRow(info, "运行环境", ".NET Framework 4.8 · WPF");

            infoCard.Child = info;
            content.Children.Add(infoCard);

            // 功能说明：两段式文案，自动换行并按 20 的行高保证多行可读性。
            TextBlock description = UiTheme.MakeText(
                "支持 24 小时制、全球时区、天气、工作/娱乐提醒与日程管理。"
                + "默认 UTC+08:00，所有提醒和日程均可按当前时区触发。",
                12,
                UiTheme.TextSecondary,
                FontWeights.Normal);
            description.TextWrapping = TextWrapping.Wrap;
            description.LineHeight = 20;
            description.Margin = new Thickness(2, 0, 2, 12);
            content.Children.Add(description);

            // 功能清单：单行枚举，用弱化色避免与正文抢注意力。
            TextBlock features = UiTheme.MakeText(
                "时间显示 · 天气 · 定时提醒 · 日程 · 鼠标穿透 · 开机自启",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            features.TextWrapping = TextWrapping.Wrap;
            features.Margin = new Thickness(2, 0, 2, 0);
            content.Children.Add(features);

            // 版权行：与上方正文拉开 18px，视觉上作为页脚小字。
            TextBlock copyright = UiTheme.MakeText(
                "Copyright © 2026 GameClock. All rights reserved.",
                10.5,
                UiTheme.TextMuted,
                FontWeights.Normal);
            copyright.Margin = new Thickness(2, 18, 2, 0);
            content.Children.Add(copyright);

            root.Children.Add(content);
            Grid.SetRow(content, 1);

            // ===== 按钮栏：右对齐单个主按钮，与对话框的确认键位置一致 =====
            Border footer = new Border();
            footer.Background = UiTheme.Frozen(0x17, 0x17, 0x1C);
            footer.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(24, 12, 24, 12);
            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            Button closeButton = new Button();
            closeButton.Content = "关闭";
            // 本窗只有“关闭”一个动作，用主色按钮给出唯一的视觉焦点。
            closeButton.Style = UiTheme.PrimaryButtonStyle;
            closeButton.MinWidth = 88;
            closeButton.Height = 34;

            /// <summary>确认按钮关闭窗口；本窗无返回值。</summary>
            closeButton.Click += delegate { Close(); };
            AutomationProperties.SetAutomationId(closeButton, "AboutOkButton");
            actions.Children.Add(closeButton);
            footer.Child = actions;
            root.Children.Add(footer);
            Grid.SetRow(footer, 2);

            return root;
        }

        /// <summary>
        /// 用图形原语拼一枚 64×64 的时钟图标：外圈表盘、时针、分针与中心圆点。
        /// 不使用图片资源，避免为单一图标引入额外文件与 DPI 缩放问题；坐标以 32,32 为圆心。
        /// </summary>
        /// <returns>定尺寸的图标容器，可直接放进网格列。</returns>
        private static Grid BuildClockIcon()
        {
            // 定尺寸容器：内部所有坐标都按 64×64 设计，放进任意列宽都不会被拉伸变形。
            // 图标四层按添加顺序叠放：外圈 → 时针 → 分针 → 中心点，后加的画在上面。
            Grid host = new Grid();
            host.Width = 64;
            host.Height = 64;

            // 表盘外圈：浅蓝色描边、3px 粗细，居中于 64 方格内。
            Ellipse ring = new Ellipse();
            ring.Width = 58;
            ring.Height = 58;
            ring.Stroke = UiTheme.Frozen(0x2E, 0xA8, 0xFF);
            ring.StrokeThickness = 3;
            ring.HorizontalAlignment = HorizontalAlignment.Center;
            ring.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(ring);

            // 时针：从圆心指向 12 点方向，与外圈同色。
            Line hour = new Line();
            hour.X1 = 32;
            hour.Y1 = 32;
            hour.X2 = 32;
            hour.Y2 = 16;
            hour.Stroke = UiTheme.Frozen(0x2E, 0xA8, 0xFF);
            hour.StrokeThickness = 3;
            // 圆头线帽让指针末端不显生硬，与整体圆角风格一致。
            hour.StrokeStartLineCap = PenLineCap.Round;
            hour.StrokeEndLineCap = PenLineCap.Round;
            host.Children.Add(hour);

            // 分针：指向右下，用主题强调色让图标有唯一的高亮焦点。
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

            // 中心圆点：盖住两根指针的接头，避免交接处出现豁口。
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
        /// 向信息卡片追加一行“标签 : 值”。标签列固定 88 宽保证多行左对齐，
        /// 值列用等宽字体显示版本号与日期，位数更整齐；值可换行（如运行环境）。
        /// </summary>
        /// <param name="parent">目标竖排容器，通常是版本卡片内的 <see cref="StackPanel"/>。</param>
        /// <param name="label">左侧标签文字。</param>
        /// <param name="value">右侧值文字；为 null 时按空串处理（由调用方保证非空）。</param>
        private static void AddInfoRow(StackPanel parent, string label, string value)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 3, 0, 3);
            // 标签列写死 88：三行标签宽度不同，固定列宽才能让右侧值始终对齐同一竖线。
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock labelText = UiTheme.MakeText(label, 11.5, UiTheme.TextMuted, FontWeights.Normal);
            labelText.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(labelText);

            TextBlock valueText = UiTheme.MakeText(value, 12.5, UiTheme.TextPrimary, FontWeights.SemiBold);
            valueText.FontFamily = new FontFamily("Consolas");
            valueText.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(valueText);
            Grid.SetColumn(valueText, 1);

            parent.Children.Add(row);
        }
    }
}
