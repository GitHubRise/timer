using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace GameClock
{
    // 本文件是设置窗口的 UI 工厂：预览卡、页签容器、页脚、调色板、滑块/复选框与字体模板。
    // 只负责“搭控件 + 挂事件”，不读写 _working、不落盘；事件处理器在 SettingsWindow.Shell.cs 与各页签分部类。
    // 这些方法在构造期各调用一次，属于纯 UI 构造，按约定不记日志（无外部副作用、无可恢复失败）。
    internal sealed partial class SettingsWindow
    {
        /// <summary>
        /// 预览卡：固定 116 高，内部用 Viewbox 等比缩小真实排版结果。
        /// 只缩不放（StretchDirection.DownOnly），保证字号调大时预览不会撑破卡片。
        /// </summary>
        private Border BuildPreviewCard()
        {
            // 卡片自身画边框与内边距；高度固定，避免不同格式串让整窗高度跳动。
            Border card = new Border();
            card.Background = UiTheme.FieldBackground;
            card.BorderBrush = UiTheme.CardBorder;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(10);
            card.Height = 116;
            card.Padding = new Thickness(16, 12, 16, 12);

            Grid grid = new Grid();
            // “实时预览”标签贴左上，与居中的预览内容分开，避免两者重叠。
            // 字号 10.5 比正文小一档，作为说明性文字不抢内容的视觉重心。
            TextBlock label = UiTheme.MakeText("实时预览", 10.5, UiTheme.TextMuted, FontWeights.SemiBold);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(label);

            // Viewbox 负责把 StackPanel 的真实排版整体缩放；上边距 16 为标签让位。
            Viewbox viewbox = new Viewbox();
            viewbox.Stretch = Stretch.Uniform;
            viewbox.StretchDirection = StretchDirection.DownOnly;
            viewbox.HorizontalAlignment = HorizontalAlignment.Center;
            viewbox.VerticalAlignment = VerticalAlignment.Center;
            viewbox.Margin = new Thickness(0, 16, 0, 0);

            // 时间行与天气行放进同一个 StackPanel，缩放比例才会一致（两行相对大小不变）。
            StackPanel previewStack = new StackPanel();
            previewStack.HorizontalAlignment = HorizontalAlignment.Center;
            previewStack.VerticalAlignment = VerticalAlignment.Center;
            previewStack.Children.Add(_previewText);
            previewStack.Children.Add(_previewWeatherText);
            viewbox.Child = previewStack;
            grid.Children.Add(viewbox);

            card.Child = grid;
            return card;
        }

        /// <summary>
        /// 统一的页签容器：TabItem 内含一层纵向 ScrollViewer，内容超长时页签内部滚动，
        /// 而不是把整个窗口撑高。页签内容由各 Build*Tab 方法提供。
        /// </summary>
        private TabItem CreateTab(string header, UIElement content)
        {
            TabItem item = new TabItem();
            item.Header = header;

            // 横向禁用滚动、纵向 Auto：设置项都是单列布局，横向滚动没有意义。
            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Margin = new Thickness(0, 2, 2, 0);

            // 右边留 10、底部留 14 是给滚动条与最后一项的呼吸空间。
            StackPanel host = new StackPanel();
            host.Margin = new Thickness(2, 4, 10, 14);
            host.Children.Add(content);
            scroll.Content = host;
            item.Content = scroll;
            return item;
        }

        /// <summary>
        /// 页脚：左“恢复默认/关于”，中间状态文字，右“应用/保存并退出”。
        /// 三列布局（Auto/Star/Auto），中间列吸收多余宽度，保证按钮始终贴右。
        /// </summary>
        private Border BuildFooter()
        {
            // 页脚底色比窗口略深（0x1C1C22），用上边框与页签区分层次。
            // Frozen 缓存画刷：页脚是常驻控件，避免每次布局重新分配 Brush 对象。
            Border footer = new Border();
            footer.Background = UiTheme.Frozen(0x1C, 0x1C, 0x22);
            footer.BorderBrush = UiTheme.CardBorder;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            // 左右 24 与标题区的 26 接近但不完全相等：页脚按钮比标题文字内缩 2px 视觉更齐。
            footer.Padding = new Thickness(24, 12, 24, 12);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧次要操作：恢复默认、关于。两者都只改编辑副本或不改设置。
            // 高度统一 34、圆角走 GhostButtonStyle，与其他次要按钮保持同一视觉规格。
            StackPanel leftActions = new StackPanel();
            leftActions.Orientation = Orientation.Horizontal;
            leftActions.VerticalAlignment = VerticalAlignment.Center;

            // MinWidth 88 保证“恢复默认”四字加内边距不换行；“关于”两字用 62 足够。
            Button reset = new Button();
            reset.Content = "恢复默认";
            reset.Style = UiTheme.GhostButtonStyle;
            reset.MinWidth = 88;
            reset.Height = 34;
            reset.Click += OnResetDefaults;
            AutomationProperties.SetAutomationId(reset, "ResetButton");
            leftActions.Children.Add(reset);

            Button about = new Button();
            about.Content = "关于";
            about.Style = UiTheme.GhostButtonStyle;
            about.MinWidth = 62;
            about.Height = 34;
            about.Margin = new Thickness(2, 0, 0, 0);
            about.Click += OnAbout;
            AutomationProperties.SetAutomationId(about, "AboutButton");
            leftActions.Children.Add(about);

            grid.Children.Add(leftActions);
            Grid.SetColumn(leftActions, 0);

            // 中间列是唯一操作反馈位（“有未应用的修改/已应用/校验错误”都写这里）。
            grid.Children.Add(_footerStatus);
            Grid.SetColumn(_footerStatus, 1);

            // 右侧主操作：应用（不关闭）与保存并退出（IsDefault，回车即触发）。
            // “应用”置于“保存并退出”左侧：主按钮贴右，符合 Windows 对话框的按钮顺序习惯。
            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.VerticalAlignment = VerticalAlignment.Center;

            Button apply = new Button();
            apply.Content = "应用";
            apply.Style = UiTheme.SecondaryButtonStyle;
            apply.MinWidth = 82;
            apply.Height = 34;
            apply.Margin = new Thickness(0, 0, 9, 0);
            apply.Click += OnApply;
            apply.ToolTip = "保存设置但不关闭窗口";
            AutomationProperties.SetAutomationId(apply, "ApplyButton");
            actions.Children.Add(apply);

            // IsDefault=true：在任意输入框里按回车等同点“保存并退出”，是设置窗口的常用习惯。
            Button save = new Button();
            save.Content = "保存并退出";
            save.Style = UiTheme.PrimaryButtonStyle;
            save.MinWidth = 108;
            save.Height = 34;
            save.IsDefault = true;
            save.Click += OnSaveAndExit;
            save.ToolTip = "保存设置并关闭设置窗口";
            AutomationProperties.SetAutomationId(save, "SaveButton");
            actions.Children.Add(save);

            grid.Children.Add(actions);
            Grid.SetColumn(actions, 2);
            footer.Child = grid;
            return footer;
        }

        /// <summary>外观页的“时间颜色”行：色块 + 十六进制文本 + 选择按钮（真正取色走系统对话框）。</summary>
        private UIElement BuildColorRow()
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 14, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧“标签 + 色块 + 十六进制值”整体垂直居中，与右侧按钮对齐。
            // 标签右边距 12：色块紧跟在文字后，不留过大空隙，便于一眼对应。
            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            TextBlock label = UiTheme.MakeText("时间颜色", 13, UiTheme.TextPrimary, FontWeights.Normal);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(label);
            left.Children.Add(_colorSwatch);
            left.Children.Add(_colorHexText);
            row.Children.Add(left);
            Grid.SetColumn(left, 0);

            Button pick = new Button();
            pick.Content = "选择…";
            pick.Style = UiTheme.SecondaryButtonStyle;
            // 内边距 13/6、高 30：比页脚按钮矮一档，作为行内按钮不喧宾夺主。
            pick.Padding = new Thickness(13, 6, 13, 6);
            pick.Height = 30;
            pick.Click += OnPickColor;
            AutomationProperties.SetAutomationId(pick, "PickColorButton");
            row.Children.Add(pick);
            Grid.SetColumn(pick, 1);

            return row;
        }

        /// <summary>
        /// 九色快捷调色板。颜色与名称按下标一一对应，新增颜色时必须同时补 names，
        /// 否则 ToolTip 会数组越界；色块本身只触发 SelectColor，不直接写配置。
        /// </summary>
        private UIElement BuildPalette()
        {
            WrapPanel palette = new WrapPanel();
            palette.Margin = new Thickness(0, 12, 0, 0);
            // 9 个预设色（Material 风格）+ 黑/白；顺序即界面顺序，不要随意重排。
            Color[] presetColors = new Color[]
            {
                Color.FromRgb(0xFF, 0x3B, 0x30),
                Color.FromRgb(0xFF, 0x95, 0x00),
                Color.FromRgb(0xFF, 0xD6, 0x0A),
                Color.FromRgb(0x34, 0xC7, 0x59),
                Color.FromRgb(0x00, 0xC7, 0xBE),
                Color.FromRgb(0x0A, 0x84, 0xFF),
                Color.FromRgb(0xBF, 0x5A, 0xF2),
                Color.FromRgb(0x00, 0x00, 0x00),
                Color.FromRgb(0xF2, 0xF2, 0xF7)
            };
            // 名称数组长度必须与 presetColors 一致（9 个），仅用于 ToolTip。
            string[] names = new string[]
            {
                "红色（默认）", "橙色", "黄色", "绿色", "青色", "蓝色", "紫色", "黑色", "白色"
            };

            for (int i = 0; i < presetColors.Length; i++)
            {
                // 每轮取局部副本：匿名委托捕获的是 presetColor 变量，循环里必须每轮新建，
                // 否则所有色块会共享最后一次迭代的值（C# 5 的经典闭包坑）。
                Color presetColor = presetColors[i];
                // 24×24 圆角 12 = 正圆；右边距 9 让 9 个色块在 640 宽窗口里恰好不折行（换行也允许）。
                Border swatch = new Border();
                swatch.Width = 24;
                swatch.Height = 24;
                swatch.CornerRadius = new CornerRadius(12);
                swatch.Background = UiTheme.BrushFrom(presetColor);
                swatch.BorderThickness = new Thickness(1);
                swatch.BorderBrush = UiTheme.Frozen(0x55, 0x55, 0x60);
                swatch.Margin = new Thickness(0, 0, 9, 0);
                swatch.Cursor = Cursors.Hand;
                swatch.ToolTip = names[i] + "  " + ColorUtil.ToHex(presetColor);
                swatch.MouseLeftButtonDown += delegate
                {
                    SelectColor(presetColor);
                };
                palette.Children.Add(swatch);
                // 记录色块引用，UpdateColorUi 据此高亮“当前选中色”。
                _presets.Add(new ColorPreset(presetColor, swatch));
            }
            return palette;
        }

        /// <summary>
        /// 滑块行通用骨架：标题 + 右侧实时数值 + 滑块本体 + 提示文字。
        /// 数值文本由各页签自己的事件更新，这里只负责布局。
        /// </summary>
        private UIElement BuildSliderBlock(string labelText, Slider slider, TextBlock valueText, string helpText, Thickness margin)
        {
            StackPanel panel = new StackPanel();
            panel.Margin = margin;

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock label = UiTheme.MakeText(labelText, 13, UiTheme.TextPrimary, FontWeights.Normal);
            header.Children.Add(label);
            header.Children.Add(valueText);
            Grid.SetColumn(valueText, 1);
            panel.Children.Add(header);

            slider.Margin = new Thickness(0, 3, 0, 0);
            slider.ToolTip = helpText;
            panel.Children.Add(slider);
            return panel;
        }

        /// <summary>
        /// 创建整数步进滑块。min/max 由调用方传入，必须与 AppSettings.Normalize 的夹取区间一致，
        /// 否则界面能选到保存时被静默改掉的数值。TickFrequency=1 + SnapToTick 保证只出整数。
        /// </summary>
        private static Slider CreateSlider(double min, double max, double value, string automationId)
        {
            Slider slider = new Slider();
            slider.Minimum = min;
            slider.Maximum = max;
            slider.Value = value;
            // SmallChange=1（方向键）/LargeChange=5（PageUp/PageDown、点击轨道）：
            // 数值型设置项都是整数像素或百分比，5 的粗调步长足够，不必更大。
            slider.SmallChange = 1;
            slider.LargeChange = 5;
            // SnapToTick + TickFrequency=1：保证 Value 始终是整数，避免 52.0000001 这类脏值落盘。
            slider.IsSnapToTickEnabled = true;
            slider.TickFrequency = 1;
            slider.Focusable = true;
            AutomationProperties.SetAutomationId(slider, automationId);
            return slider;
        }

        /// <summary>创建统一样式的复选框（字号 13、上下留白 4/5、带 ToolTip）。</summary>
        private static CheckBox CreateCheckBox(string text, bool isChecked, string automationId, string toolTip)
        {
            CheckBox check = new CheckBox();
            check.Content = text;
            check.IsChecked = isChecked;
            check.Foreground = UiTheme.TextPrimary;
            check.FontSize = 13;
            // 上下留白 4/5：连续多个复选框靠这个间距分隔，不额外加 StackPanel 间隔。
            check.Margin = new Thickness(0, 4, 0, 5);
            check.VerticalContentAlignment = VerticalAlignment.Center;
            check.ToolTip = toolTip;
            AutomationProperties.SetAutomationId(check, automationId);
            return check;
        }

        /// <summary>
        /// 字体下拉项的模板：每一行用该字体自身渲染字体名，用户能直接看出字形差异。
        /// 项目无 XAML 文件，只能用 FrameworkElementFactory 手搓 DataTemplate（C# 5 不支持内联 XAML）。
        /// </summary>
        private static DataTemplate CreateFontItemTemplate()
        {
            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            // 关键：FontFamily 绑定到选项自身的 Family，而不是用界面默认字体。
            text.SetBinding(TextBlock.FontFamilyProperty, new Binding("Family"));
            text.SetValue(TextBlock.FontSizeProperty, 14.0);
            text.SetValue(TextBlock.ForegroundProperty, UiTheme.TextPrimary);

            DataTemplate template = new DataTemplate();
            template.VisualTree = text;
            return template;
        }
    }
}
