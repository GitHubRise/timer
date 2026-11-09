// 设置窗口的控件构建分部类（SettingsWindow.Chrome.cs）。
// 职责：提供外壳与各页签共用的“纯构建”助手——实时预览卡、带滚动的页签容器、底部动作栏、
// 时间颜色行与预设调色板，以及滑杆块、滑杆/复选框/字体下拉模板等控件工厂。
// 边界：这些方法只 new 控件并设属性，不读不写 _working，因此可在构造期安全调用、也可在重建行时复用。
// 窗口契约由主文件与 Shell 分部落地：WindowStyle=None + ResizeMode=CanResize、最小 640×560、
// 四边缩放靠 WindowChrome 的 ResizeBorderThickness=6、拖动只走标题 Grid 的
// MouseLeftButtonDown → DragMove()（光标 Cursors.SizeAll）；本文件不得改动这些前提。

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

    /// <summary>
    /// 设置窗口的控件构建分部：只做“把控件拼出来”这件事，不含业务判断。
    /// 与外壳分部（Shell）的分工是——Shell 决定窗口骨架里放什么，本分部提供可复用的构件与工厂；
    /// 构建期一次性创建控件，运行期只由各页签方法替换内容，不保存静态状态。
    /// </summary>
    internal sealed partial class SettingsWindow
    {
        // ===== 实时预览卡 =====

        /// <summary>
        /// 构建标题下方的“实时预览”卡：固定高度 116，内部用 <see cref="Viewbox"/> 包住时间与天气两行，
        /// 内容再长也只会在卡片内等比缩小，不会撑高骨架、把页签往下挤。
        /// </summary>
        /// <returns>可直接放进根 Grid 第 1 行的预览卡 Border。</returns>
        private Border BuildPreviewCard()
        {
            // 卡片用输入框底色 + 圆角，与页签内部的字段视觉统一；高度固定，切换页签时骨架不跳动。
            Border card = new Border();
            card.Background = UiTheme.FieldBackground;
            card.BorderBrush = UiTheme.CardBorder;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(10);
            card.Height = 116;
            card.Padding = new Thickness(16, 12, 16, 12);

            Grid grid = new Grid();
            // “实时预览”角标固定在左上，不参与居中，明确这是预览而不是真实悬浮窗。
            TextBlock label = UiTheme.MakeText("实时预览", 10.5, UiTheme.TextMuted, FontWeights.SemiBold);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(label);

            // StretchDirection=DownOnly 保证只缩不放：短文本不会被放大糊掉，长文本也不会溢出卡片。
            Viewbox viewbox = new Viewbox();
            viewbox.Stretch = Stretch.Uniform;
            viewbox.StretchDirection = StretchDirection.DownOnly;
            viewbox.HorizontalAlignment = HorizontalAlignment.Center;
            viewbox.VerticalAlignment = VerticalAlignment.Center;
            viewbox.Margin = new Thickness(0, 16, 0, 0);

            // 时间与天气两行统一放进 StackPanel：两者可见性都由 UpdatePreview 控制，
            // 这里只负责排版，天气行折叠时 StackPanel 会自动收掉它的高度。
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

        // ===== 页签容器 =====

        /// <summary>
        /// 把一个页签内容包成统一外观的 <see cref="TabItem"/>：外层 ScrollViewer 负责纵向滚动，
        /// 横向滚动条强制禁用，避免长文本把整页撑宽、破坏窗口的固定布局假设。
        /// </summary>
        /// <param name="header">页签标题文本。</param>
        /// <param name="content">页签内容根元素（由对应 Build*Tab 方法生成）。</param>
        /// <returns>可直接加入 <c>_tabs.Items</c> 的页签项。</returns>
        private TabItem CreateTab(string header, UIElement content)
        {
            TabItem item = new TabItem();
            item.Header = header;

            // 纵向滚动条按需出现；右边留 2px 让它不贴边，也避免出现滚动条时内容被裁掉。
            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Margin = new Thickness(0, 2, 2, 0);

            // 里层再包一层 StackPanel，页签内容只算一项，后续要追加说明文字也不必改结构。
            StackPanel host = new StackPanel();
            host.Margin = new Thickness(2, 4, 10, 14);
            host.Children.Add(content);
            scroll.Content = host;
            item.Content = scroll;
            return item;
        }

        // ===== 底部动作栏 =====

        /// <summary>
        /// 构建窗口底部动作栏：三列——左侧“恢复默认 / 关于”，中间状态文字（星号列吸收剩余宽度），
        /// 右侧“应用 / 保存并退出”。四个按钮高度统一 34，保证底栏在缩放时高度稳定。
        /// </summary>
        /// <returns>带顶部 1px 分隔线的页脚 Border。</returns>
        private Border BuildFooter()
        {
            // 页脚比窗体底色略深一档，并用顶部 1px 线分隔内容区，视觉上把“动作”和“设置项”分开。
            Border footer = new Border();
            footer.Background = UiTheme.Frozen(0x1C, 0x1C, 0x22);
            footer.BorderBrush = UiTheme.CardBorder;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(24, 12, 24, 12);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧放低频动作：恢复默认与关于，用幽灵样式降低存在感，避免误点。
            StackPanel leftActions = new StackPanel();
            leftActions.Orientation = Orientation.Horizontal;
            leftActions.VerticalAlignment = VerticalAlignment.Center;

            Button reset = new Button();
            reset.Content = "恢复默认";
            reset.Style = UiTheme.GhostButtonStyle;
            reset.MinWidth = 88;
            reset.Height = 34;
            reset.Click += OnResetDefaults;
            // 自动化测试依赖稳定 Id；与 Shell 里的关闭按钮保持同一套命名习惯。
            AutomationProperties.SetAutomationId(reset, "ResetButton");
            leftActions.Children.Add(reset);

            Button about = new Button();
            about.Content = "关于";
            about.Style = UiTheme.GhostButtonStyle;
            about.MinWidth = 62;
            about.Height = 34;
            // 2px 间距让两个幽灵按钮贴在一起也不至于连成一条。
            about.Margin = new Thickness(2, 0, 0, 0);
            about.Click += OnAbout;
            AutomationProperties.SetAutomationId(about, "AboutButton");
            leftActions.Children.Add(about);

            grid.Children.Add(leftActions);
            Grid.SetColumn(leftActions, 0);

            // 状态文字放中间列（星号），左右动作组固定宽度，窗口拉宽时只有它变长。
            // 文本内容与颜色由 MarkDirty / OnApply / OnResetDefaults 等动作方法直接改写。
            grid.Children.Add(_footerStatus);
            Grid.SetColumn(_footerStatus, 1);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.VerticalAlignment = VerticalAlignment.Center;

            // “应用”用次要样式：保存但不关窗，适合边调边看效果。
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

            // “保存并退出”用主色并设为默认按钮：Enter 直接落到它上面，符合表单提交习惯。
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

        // ===== 时间颜色选择 =====

        /// <summary>
        /// 构建“时间颜色”行：左侧是标签 + 当前色块 + 十六进制色值，右侧是“选择…”按钮。
        /// 色块与色值这两个控件属于窗口字段，由 <c>UpdateColorUi</c> 负责刷新显示。
        /// </summary>
        /// <returns>可直接放进外观页签的一行 Grid。</returns>
        private UIElement BuildColorRow()
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 14, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧标签、色块、色值三者垂直居中排成一行，用户不必来回对照就能看出当前颜色。
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

            // “选择…”打开系统取色面板，覆盖预设色之外的任意颜色需求。
            Button pick = new Button();
            pick.Content = "选择…";
            pick.Style = UiTheme.SecondaryButtonStyle;
            pick.Padding = new Thickness(13, 6, 13, 6);
            pick.Height = 30;
            pick.Click += OnPickColor;
            AutomationProperties.SetAutomationId(pick, "PickColorButton");
            row.Children.Add(pick);
            Grid.SetColumn(pick, 1);

            return row;
        }

        /// <summary>
        /// 构建预设色调色板：九个常用颜色排成一行，点击色块立即写入编辑副本并刷新选中态。
        /// 每个色块同时登记到 <c>_presets</c>，否则之后无法把选中描边改回去。
        /// </summary>
        /// <returns>装好九个圆形色块的 <see cref="WrapPanel"/>。</returns>
        private UIElement BuildPalette()
        {
            WrapPanel palette = new WrapPanel();
            palette.Margin = new Thickness(0, 12, 0, 0);
            // 颜色与名称两个数组按下标严格对应，顺序即界面顺序；改颜色时名称必须一起改。
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
            string[] names = new string[]
            {
                "红色（默认）", "橙色", "黄色", "绿色", "青色", "蓝色", "紫色", "黑色", "白色"
            };

            for (int i = 0; i < presetColors.Length; i++)
            {
                // 24×24、圆角 12 得到正圆；外描边 0x55 灰让浅色块在深色背景上也有轮廓。
                Color presetColor = presetColors[i];
                Border swatch = new Border();
                swatch.Width = 24;
                swatch.Height = 24;
                swatch.CornerRadius = new CornerRadius(12);
                swatch.Background = UiTheme.BrushFrom(presetColor);
                swatch.BorderThickness = new Thickness(1);
                swatch.BorderBrush = UiTheme.Frozen(0x55, 0x55, 0x60);
                swatch.Margin = new Thickness(0, 0, 9, 0);
                swatch.Cursor = Cursors.Hand;
                // ToolTip 同时给出中文名与十六进制，方便用户把颜色抄到别处。
                swatch.ToolTip = names[i] + "  " + ColorUtil.ToHex(presetColor);
                // 用匿名方法而不是事件处理器，是为了把当前循环的 presetColor 直接捕获成局部值；
                // 点击只做一件事：把颜色写进编辑副本并刷新界面。
                swatch.MouseLeftButtonDown += delegate
                {
                    SelectColor(presetColor);
                };
                palette.Children.Add(swatch);
                // 登记色块，供 SelectColor 之后统一刷新选中描边。
                _presets.Add(new ColorPreset(presetColor, swatch));
            }
            return palette;
        }

        // ===== 通用控件工厂 =====

        /// <summary>
        /// 构建“标签 + 右侧数值 + 滑杆”的通用块。数值文本由调用方传入，并在滑杆值变化时
        /// 由调用方自己的处理器更新（这里只搭骨架，不挂事件，避免各页签行为被写死）。
        /// </summary>
        /// <param name="labelText">左侧标签文本。</param>
        /// <param name="slider">已创建好的滑杆，通常来自 <see cref="CreateSlider"/>。</param>
        /// <param name="valueText">右侧实时数值文本。</param>
        /// <param name="helpText">滑杆的 ToolTip 说明。</param>
        /// <param name="margin">整个块的外边距，用于对齐相邻设置项。</param>
        /// <returns>可直接放进页签的 StackPanel。</returns>
        private UIElement BuildSliderBlock(string labelText, Slider slider, TextBlock valueText, string helpText, Thickness margin)
        {
            StackPanel panel = new StackPanel();
            panel.Margin = margin;

            // 首行两列：标签吸收剩余宽度，数值靠右对齐，视觉上像“设置项 + 当前值”。
            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock label = UiTheme.MakeText(labelText, 13, UiTheme.TextPrimary, FontWeights.Normal);
            header.Children.Add(label);
            header.Children.Add(valueText);
            Grid.SetColumn(valueText, 1);
            panel.Children.Add(header);

            // 滑杆紧跟在标题下方 3px；ToolTip 承担解释成本，避免页面上堆说明文字。
            slider.Margin = new Thickness(0, 3, 0, 0);
            slider.ToolTip = helpText;
            panel.Children.Add(slider);
            return panel;
        }

        /// <summary>
        /// 创建统一规格的整数滑杆：小步长 1、大步长 5、吸附到整数刻度，
        /// 保证字号/透明度/闪烁次数永远不会出现小数。Focusable 让键盘也能调节。
        /// </summary>
        /// <param name="min">最小值。</param>
        /// <param name="max">最大值。</param>
        /// <param name="value">初始值。</param>
        /// <param name="automationId">UI 自动化 Id。</param>
        /// <returns>配置好的 <see cref="Slider"/>。</returns>
        private static Slider CreateSlider(double min, double max, double value, string automationId)
        {
            Slider slider = new Slider();
            slider.Minimum = min;
            slider.Maximum = max;
            slider.Value = value;
            slider.SmallChange = 1;
            slider.LargeChange = 5;
            // 吸附到刻度 + 刻度间隔 1：拖动时天然落在整数上，省去各处再取整。
            slider.IsSnapToTickEnabled = true;
            slider.TickFrequency = 1;
            slider.Focusable = true;
            AutomationProperties.SetAutomationId(slider, automationId);
            return slider;
        }

        /// <summary>
        /// 创建统一外观的复选框：内容、初始勾选、自动化 Id 与 ToolTip 一次设好，
        /// 垂直内容居中让勾选框与多行文本也能对齐。
        /// </summary>
        /// <param name="text">显示文本。</param>
        /// <param name="isChecked">初始勾选状态。</param>
        /// <param name="automationId">UI 自动化 Id。</param>
        /// <param name="toolTip">说明文字，写清开启后的副作用。</param>
        /// <returns>配置好的 <see cref="CheckBox"/>。</returns>
        private static CheckBox CreateCheckBox(string text, bool isChecked, string automationId, string toolTip)
        {
            CheckBox check = new CheckBox();
            check.Content = text;
            check.IsChecked = isChecked;
            check.Foreground = UiTheme.TextPrimary;
            check.FontSize = 13;
            // 上下各留一点外边距，多个复选框纵向排列时不会挤成一团。
            check.Margin = new Thickness(0, 4, 0, 5);
            check.VerticalContentAlignment = VerticalAlignment.Center;
            check.ToolTip = toolTip;
            AutomationProperties.SetAutomationId(check, automationId);
            return check;
        }

        /// <summary>
        /// 创建字体下拉项的模板：文字既显示字体名，也用该字体本身渲染
        /// （<c>FontFamily</c> 绑定到选项的 Family），用户可以直接看到字形而非只读名字。
        /// 项目无 XAML 资源，故用 <see cref="FrameworkElementFactory"/> 在代码里搭模板。
        /// </summary>
        /// <returns>供字体下拉框使用的 <see cref="DataTemplate"/>。</returns>
        private static DataTemplate CreateFontItemTemplate()
        {
            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
            // Name 用于显示，Family 用于“用自己的字体显示自己”，两者都来自 FontChoice。
            text.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            text.SetBinding(TextBlock.FontFamilyProperty, new Binding("Family"));
            text.SetValue(TextBlock.FontSizeProperty, 14.0);
            text.SetValue(TextBlock.ForegroundProperty, UiTheme.TextPrimary);

            DataTemplate template = new DataTemplate();
            template.VisualTree = text;
            return template;
        }
    }
}
