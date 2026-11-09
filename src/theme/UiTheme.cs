// 暗色主题的唯一来源：色板画刷、按钮/下拉/单选/页签样式、文本与控件工厂。
// 项目不使用 XAML 资源字典，所有外观都由这里的代码构建，设置窗口与对话框只调用工厂、不自己配颜色。
// 约束：本类不引用 AppSettings，主题与业务配置无关；调用方也不要缓存这里的 Style 实例之外的中间对象。
// 画刷由 Frozen 创建后即只读，可跨线程与跨窗口共享；按钮/下拉/单选样式懒加载缓存并复用同一实例。
// WPF 模板用 FrameworkElementFactory 构建（C# 5 下无法写 XAML 模板），颜色与圆角等常量都集中在本文件。

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GameClock
{

    /// <summary>
    /// 暗色主题：集中提供画刷、控件样式与创建控件的工厂方法。
    /// 全部成员为静态，进程内共享一份；按钮/下拉/单选样式按类型懒加载缓存，页签样式则在每次 <see cref="ApplyDarkTabs"/> 时现建。
    /// 新增外观时优先加画刷或改常量，不要在窗口里散落颜色字面量。
    /// </summary>
    internal static class UiTheme
    {
        // ===== 色板：窗口与卡片 =====

        /// <summary>窗口底色（近黑）；设置窗口、对话框与关于窗的最底层背景。</summary>
        public static readonly SolidColorBrush WindowBackground = Frozen(0x17, 0x17, 0x1B);

        /// <summary>卡片底色；比窗口底色略亮一级，用于分组的圆角容器。</summary>
        public static readonly SolidColorBrush CardBackground = Frozen(0x22, 0x22, 0x29);

        /// <summary>卡片描边色；分隔同色系卡片与底图，1px 即可。</summary>
        public static readonly SolidColorBrush CardBorder = Frozen(0x35, 0x35, 0x3F);

        // ===== 色板：文字 =====

        /// <summary>主文字色（近白）；标题、输入框内容与关键数值。</summary>
        public static readonly SolidColorBrush TextPrimary = Frozen(0xF2, 0xF2, 0xF5);

        /// <summary>次要文字色（浅灰）；字段标签、说明段与未选中页签。</summary>
        public static readonly SolidColorBrush TextSecondary = Frozen(0x9E, 0x9E, 0xAA);

        /// <summary>弱化文字色（中灰）；版权行、功能清单等可略读的辅助信息。</summary>
        public static readonly SolidColorBrush TextMuted = Frozen(0x72, 0x72, 0x7E);

        // ===== 色板：强调与状态 =====

        /// <summary>主题强调色（珊瑚红）；主按钮、选中态、光标与校验提示共用。</summary>
        public static readonly SolidColorBrush Accent = Frozen(0xFF, 0x45, 0x3A);

        /// <summary>强调色悬停态；主按钮鼠标移入时的背景。</summary>
        public static readonly SolidColorBrush AccentHover = Frozen(0xFF, 0x5B, 0x50);

        /// <summary>强调色按下态；主按钮按下与选中反馈，比常态更深。</summary>
        public static readonly SolidColorBrush AccentPressed = Frozen(0xD9, 0x2D, 0x24);

        /// <summary>成功色（薄荷绿）；天气/网络测试通过等正向结果的文字色。</summary>
        public static readonly SolidColorBrush Success = Frozen(0x54, 0xD6, 0x8C);

        // ===== 色板：输入框 =====

        /// <summary>输入框底色；比卡片更深，让可编辑区域向内凹陷。</summary>
        public static readonly SolidColorBrush FieldBackground = Frozen(0x1B, 0x1B, 0x21);

        /// <summary>输入框描边；同时用作次要按钮的描边，保持“可交互边界”一致。</summary>
        public static readonly SolidColorBrush FieldBorder = Frozen(0x40, 0x40, 0x4B);

        // ===== 色板：次要按钮 =====

        /// <summary>次要按钮底色；取消、重置等非破坏性动作的常态填充。</summary>
        public static readonly SolidColorBrush SecondaryButton = Frozen(0x2E, 0x2E, 0x37);

        /// <summary>次要按钮悬停底色；比常态亮一档，也用于下拉项悬停与选中页签。</summary>
        public static readonly SolidColorBrush SecondaryButtonHover = Frozen(0x3A, 0x3A, 0x46);

        /// <summary>次要按钮按下底色；比常态暗一档。</summary>
        public static readonly SolidColorBrush SecondaryButtonPressed = Frozen(0x25, 0x25, 0x2D);

        // ===== 样式缓存：懒加载，进程内只建一次 =====

        /// <summary>主按钮样式缓存；首次访问 <see cref="PrimaryButtonStyle"/> 时创建。</summary>
        private static Style _primaryButtonStyle;

        /// <summary>次要按钮样式缓存；首次访问 <see cref="SecondaryButtonStyle"/> 时创建。</summary>
        private static Style _secondaryButtonStyle;

        /// <summary>幽灵按钮样式缓存；用于标题栏关闭按钮这类无底色的图标按钮。</summary>
        private static Style _ghostButtonStyle;

        /// <summary>下拉框样式缓存；所有下拉共用，避免每建一个 ComboBox 就重建一次模板。</summary>
        private static Style _comboBoxStyle;

        /// <summary>单选框样式缓存；设置页里的互斥选项共用。</summary>
        private static Style _radioButtonStyle;

        // ===== 按钮样式入口 =====

        /// <summary>主按钮：强调色底 + 白字 + 同色描边，用于“保存/应用/添加”等主操作。</summary>
        public static Style PrimaryButtonStyle
        {
            get
            {
                // 懒加载：第一次取用时建样式，之后直接返回缓存实例。
                if (_primaryButtonStyle == null)
                {
                    _primaryButtonStyle = CreateButtonStyle(
                        Accent,
                        Brushes.White,
                        Accent,
                        AccentHover,
                        AccentPressed,
                        1);
                }
                return _primaryButtonStyle;
            }
        }

        /// <summary>次要按钮：深灰底 + 主文字色 + 输入框同色描边，用于“取消/重置”等辅助操作。</summary>
        public static Style SecondaryButtonStyle
        {
            get
            {
                if (_secondaryButtonStyle == null)
                {
                    _secondaryButtonStyle = CreateButtonStyle(
                        SecondaryButton,
                        TextPrimary,
                        FieldBorder,
                        SecondaryButtonHover,
                        SecondaryButtonPressed,
                        1);
                }
                return _secondaryButtonStyle;
            }
        }

        /// <summary>
        /// 幽灵按钮：透明底、无描边，只显示文字或图标；
        /// 悬停时浮出次要按钮底色作为可点击反馈，专供标题栏关闭键使用。
        /// </summary>
        public static Style GhostButtonStyle
        {
            get
            {
                if (_ghostButtonStyle == null)
                {
                    _ghostButtonStyle = CreateButtonStyle(
                        Brushes.Transparent,
                        TextSecondary,
                        Brushes.Transparent,
                        SecondaryButton,
                        SecondaryButtonPressed,
                        1);
                }
                return _ghostButtonStyle;
            }
        }

        // ===== 基础控件工厂 =====

        /// <summary>创建一张圆角深色卡片：1px 描边、10px 圆角、内边距 16×14，默认底部留 14px 间距。</summary>
        public static Border MakeCard()
        {
            Border border = new Border();
            border.Background = CardBackground;
            border.BorderBrush = CardBorder;
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(10);
            border.Padding = new Thickness(16, 14, 16, 14);
            border.Margin = new Thickness(0, 0, 0, 14);
            return border;
        }

        /// <summary>
        /// 创建一段带样式的文字。默认开启自动换行，长文案放进窄列也不会被裁切；
        /// 调用方若要单行显示需自行把 <see cref="TextBlock.TextWrapping"/> 改回 NoWrap。
        /// </summary>
        /// <param name="text">显示内容，可为空串（用于先占位、稍后填错误信息）。</param>
        /// <param name="size">字号（设备无关单位）。</param>
        /// <param name="foreground">文字颜色，通常取本类色板。</param>
        /// <param name="weight">字重，标题用 SemiBold/Bold，正文用 Normal。</param>
        public static TextBlock MakeText(string text, double size, Brush foreground, FontWeight weight)
        {
            TextBlock block = new TextBlock();
            block.Text = text;
            block.FontSize = size;
            block.Foreground = foreground;
            block.FontWeight = weight;
            block.TextWrapping = TextWrapping.Wrap;
            return block;
        }

        /// <summary>创建卡片内的小节标题：12 号次要色半粗体，底部留 10px 与内容分隔。</summary>
        public static TextBlock MakeSectionTitle(string text)
        {
            TextBlock block = MakeText(text, 12, TextSecondary, FontWeights.SemiBold);
            block.Margin = new Thickness(0, 0, 0, 10);
            return block;
        }

        // ===== 输入控件适配 =====

        /// <summary>
        /// 给输入框套上暗色配色：深底、浅字、1px 描边、8×5 内边距。
        /// 顺带把光标与选区也改成主题色，否则默认的黑色光标在深底上几乎看不见。
        /// </summary>
        public static void ApplyTextBoxStyle(TextBox box)
        {
            box.Background = FieldBackground;
            box.Foreground = TextPrimary;
            box.BorderBrush = FieldBorder;
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(8, 5, 8, 5);
            box.FontSize = 13;
            box.CaretBrush = TextPrimary;
            box.SelectionBrush = Accent;
            box.SelectionTextBrush = Brushes.White;
            box.SnapsToDevicePixels = true;
        }

        /// <summary>给下拉框套上共享的暗色样式；样式首次调用时构建，之后直接复用。</summary>
        public static void ApplyComboBoxStyle(ComboBox combo)
        {
            if (_comboBoxStyle == null)
            {
                _comboBoxStyle = CreateComboBoxStyle();
            }
            combo.Style = _comboBoxStyle;
        }

        /// <summary>给单选框套上共享的暗色样式；与下拉框一样按需构建并缓存。</summary>
        public static void ApplyRadioButtonStyle(RadioButton radio)
        {
            if (_radioButtonStyle == null)
            {
                _radioButtonStyle = CreateRadioButtonStyle();
            }
            radio.Style = _radioButtonStyle;
        }

        /// <summary>
        /// 把 <see cref="TabControl"/> 改成扁平暗色页签：去掉边框与背景，
        /// 项容器与整体模板都换成本文件构建的版本，内容区自动拉伸填满可用空间。
        /// </summary>
        public static void ApplyDarkTabs(TabControl tabs)
        {
            tabs.Background = Brushes.Transparent;
            tabs.BorderThickness = new Thickness(0);
            tabs.Padding = new Thickness(0);
            // 内容区拉伸：让页签内的表单能撑满窗口宽度，而不是缩在左侧。
            tabs.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            tabs.VerticalContentAlignment = VerticalAlignment.Stretch;
            tabs.ItemContainerStyle = CreateTabItemStyle();
            tabs.Template = CreateTabControlTemplate();
        }

        // ===== 下拉框样式与模板 =====

        /// <summary>
        /// 构建下拉框样式：统一高度 34、13 号字、深色填充与输入框同款描边，
        /// 下拉最大高度限制为 330 以免超长列表顶到屏幕外；模板与项样式分别由其私有方法提供。
        /// </summary>
        private static Style CreateComboBoxStyle()
        {
            Style style = new Style(typeof(ComboBox));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 34.0));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.BackgroundProperty, FieldBackground));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, FieldBorder));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(ComboBox.MaxDropDownHeightProperty, 330.0));
            style.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty, CreateComboBoxItemStyle()));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateComboBoxTemplate()));
            return style;
        }

        /// <summary>
        /// 构建下拉框控件模板。层次自下而上为：
        /// 圆角描边底 → 覆盖全区的透明 <see cref="ToggleButton"/>（把点击转成开合）→ 选中项文本 → 右侧箭头 → 弹出层。
        /// 关键契约：开合状态双向绑定 <c>TemplatedParent.IsDropDownOpen</c>，弹出层宽度跟随控件实际宽度、最大高度跟随
        /// <see cref="ComboBox.MaxDropDownHeight"/>；文本与箭头都设 <c>IsHitTestVisible=false</c>，点击一律落到 ToggleButton。
        /// </summary>
        private static ControlTemplate CreateComboBoxTemplate()
        {
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            // 底：模板绑定取控件的背景/描边/线宽，样式里的 Setter 因此能作用到圆角边框上。
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);
            root.AppendChild(border);

            // 点击层：铺满整个控件的透明 ToggleButton，自己再套一个空模板去掉默认外观。
            FrameworkElementFactory toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.SetValue(ToggleButton.FocusableProperty, false);
            // Press 模式：按下即开合，符合下拉框的直觉手感（Release 需要等抬起）。
            toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
            ControlTemplate toggleTemplate = new ControlTemplate(typeof(ToggleButton));
            FrameworkElementFactory toggleBorder = new FrameworkElementFactory(typeof(Border));
            toggleBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            toggleTemplate.VisualTree = toggleBorder;
            toggle.SetValue(Control.TemplateProperty, toggleTemplate);

            // 双向绑定：用户点击改动 IsChecked，同时控件代码改 IsDropDownOpen 时按钮状态也跟着同步。
            Binding openBinding = new Binding("IsDropDownOpen");
            openBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            openBinding.Mode = BindingMode.TwoWay;
            toggle.SetValue(ToggleButton.IsCheckedProperty, openBinding);
            root.AppendChild(toggle);

            // 选中项文本：左侧留 11px、右侧留 30px 给箭头，不参与命中测试。
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(11, 0, 30, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(content);

            // 右侧箭头：用 Path 画一条 V 形折线，圆头线帽与整体圆角风格呼应。
            FrameworkElementFactory arrow = new FrameworkElementFactory(typeof(Path));
            arrow.SetValue(Path.DataProperty, Geometry.Parse("M 0 0 L 4.5 4.5 L 9 0"));
            arrow.SetValue(Path.StrokeProperty, TextSecondary);
            arrow.SetValue(Path.StrokeThicknessProperty, 1.6);
            arrow.SetValue(Path.StrokeStartLineCapProperty, PenLineCap.Round);
            arrow.SetValue(Path.StrokeEndLineCapProperty, PenLineCap.Round);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 11, 0));
            arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(arrow);

            // 弹出层：贴在控件下方，允许透明以便圆角外露出背景，点击外部自动收起。
            FrameworkElementFactory popup = new FrameworkElementFactory(typeof(Popup));
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetValue(Popup.StaysOpenProperty, false);

            // 与下拉按钮一样双向绑定 IsDropDownOpen，保证点外部收起时控件状态同步复位。
            Binding popupBinding = new Binding("IsDropDownOpen");
            popupBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBinding.Mode = BindingMode.TwoWay;
            popup.SetValue(Popup.IsOpenProperty, popupBinding);

            // 列表容器：卡片配色 + 8px 圆角，内边距 4 让项与边框之间留一点呼吸。
            FrameworkElementFactory popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, CardBackground);
            popupBorder.SetValue(Border.BorderBrushProperty, CardBorder);
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
            popupBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));

            // 宽度绑定控件实际宽度：下拉列表至少与输入框等宽，读起来不会挤成一列窄条。
            Binding minWidthBinding = new Binding("ActualWidth");
            minWidthBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBorder.SetValue(FrameworkElement.MinWidthProperty, minWidthBinding);

            // 高度绑定 MaxDropDownHeight：项很多时由滚动条接管，而不是整页撑高。
            Binding maxHeightBinding = new Binding("MaxDropDownHeight");
            maxHeightBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBorder.SetValue(FrameworkElement.MaxHeightProperty, maxHeightBinding);

            FrameworkElementFactory scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);

            // 键盘导航限制在列表内部：上下键只在候选项之间移动，不逃出弹出层。
            FrameworkElementFactory items = new FrameworkElementFactory(typeof(ItemsPresenter));
            items.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Contained);
            scroll.AppendChild(items);
            popupBorder.AppendChild(scroll);
            popup.AppendChild(popupBorder);
            root.AppendChild(popup);

            ControlTemplate template = new ControlTemplate(typeof(ComboBox));
            template.VisualTree = root;
            return template;
        }

        /// <summary>
        /// 构建下拉候选项样式：透明底 + 主文字色 + 10×7 内边距，项内是一层 5px 圆角的自绘边框。
        /// 触发器顺序固定为 悬停 在 选中 之前——两者都命中时后加的“选中”背景覆盖悬停色，选中态始终可见。
        /// </summary>
        private static Style CreateComboBoxItemStyle()
        {
            Style style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            // 项内边框：背景走模板绑定，触发器改控件的 Background 就能直接反馈到圆角块上。
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            // 留 1px 外边距，相邻项的高亮块之间不会连成一片。
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(1));

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            border.AppendChild(content);

            ControlTemplate template = new ControlTemplate(typeof(ComboBoxItem));
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            /// <summary>悬停时铺次要按钮底色，提示当前候选位置。</summary>
            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BackgroundProperty, SecondaryButton));
            style.Triggers.Add(hover);

            /// <summary>选中项用更亮一档的底色，与悬停态区分开。</summary>
            Trigger selected = new Trigger();
            selected.Property = Selector.IsSelectedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BackgroundProperty, SecondaryButtonHover));
            style.Triggers.Add(selected);

            return style;
        }

        // ===== 单选框样式与模板 =====

        /// <summary>
        /// 构建单选框样式：主文字色 13.5 号字、手型光标，描边由触发器控制。
        /// 悬停时描边提亮，选中时描边转为强调色，让“当前选择”只靠一道描边就能读出。
        /// </summary>
        private static Style CreateRadioButtonStyle()
        {
            Style style = new Style(typeof(RadioButton));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.5));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, FieldBorder));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateRadioButtonTemplate()));

            /// <summary>悬停：描边从输入框灰提亮到次要文字灰。</summary>
            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, TextSecondary));
            style.Triggers.Add(hover);

            /// <summary>选中：描边换成强调色；内圈圆点另由模板内的可见性绑定负责。</summary>
            Trigger selected = new Trigger();
            selected.Property = ToggleButton.IsCheckedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
            style.Triggers.Add(selected);

            return style;
        }

        /// <summary>
        /// 构建单选框模板：左侧 16×16 圆环 + 内圈 7px 圆点，右侧内容文本。
        /// 结构为 DockPanel（左固定圆环、其余留给文本）；内圈可见性单向绑定 <c>IsChecked</c>，
        /// 经 <see cref="BooleanToVisibilityConverter"/> 转换，因此勾选状态无需额外触发器。
        /// </summary>
        private static ControlTemplate CreateRadioButtonTemplate()
        {
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(DockPanel));
            root.SetValue(DockPanel.LastChildFillProperty, true);
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            // 圆环容器：固定 16×16 并靠左停靠，避免不同字号下圆心错位。
            FrameworkElementFactory circle = new FrameworkElementFactory(typeof(Grid));
            circle.SetValue(FrameworkElement.WidthProperty, 16.0);
            circle.SetValue(FrameworkElement.HeightProperty, 16.0);
            circle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            circle.SetValue(DockPanel.DockProperty, Dock.Left);

            // 外环：描边走模板绑定，样式触发器改 BorderBrush 时这里自动跟随。
            FrameworkElementFactory outer = new FrameworkElementFactory(typeof(Ellipse));
            outer.SetValue(Shape.StrokeProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            outer.SetValue(Shape.StrokeThicknessProperty, 1.4);
            outer.SetValue(Shape.FillProperty, FieldBackground);
            circle.AppendChild(outer);

            // 内点：7px 强调色圆点，仅选中时可见。
            FrameworkElementFactory inner = new FrameworkElementFactory(typeof(Ellipse));
            inner.SetValue(FrameworkElement.WidthProperty, 7.0);
            inner.SetValue(FrameworkElement.HeightProperty, 7.0);
            inner.SetValue(Shape.FillProperty, Accent);
            inner.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            inner.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            // 单向绑定 IsChecked → 可见性：控件本身负责维护勾选，模板只负责显示。
            Binding visibility = new Binding("IsChecked");
            visibility.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            visibility.Converter = new BooleanToVisibilityConverter();
            inner.SetValue(UIElement.VisibilityProperty, visibility);
            circle.AppendChild(inner);

            root.AppendChild(circle);

            // 内容文本：左侧留 9px 与圆环分开，垂直居中跟随单选行高。
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(9, 0, 0, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(content);

            ControlTemplate template = new ControlTemplate(typeof(RadioButton));
            template.VisualTree = root;
            return template;
        }

        // ===== 页签样式与模板 =====

        /// <summary>
        /// 构建页签项样式：13 号半粗体、左右 16px 内边距、项间 6px 间距，手型光标。
        /// 触发器顺序为 悬停 在 选中 之前：悬停先铺浅灰，选中再用卡片底色覆盖，保证选中项视觉最重。
        /// </summary>
        private static Style CreateTabItemStyle()
        {            Style style = new Style(typeof(TabItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextSecondary));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 8, 16, 8)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0)));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));

            // 项底色：7px 圆角块，背景走模板绑定以便触发器改色。
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

            // 标题内容：用 ContentSource="Header" 取 TabItem.Header，而不是内容区的 SelectedContent。
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            ControlTemplate template = new ControlTemplate(typeof(TabItem));
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            /// <summary>悬停：未选中项浮出浅灰底并把文字提亮。</summary>
            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BackgroundProperty, SecondaryButton));
            hover.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Triggers.Add(hover);

            /// <summary>选中：填充卡片底色，与下方内容区同色，形成“页签连着内容”的效果。</summary>
            Trigger selected = new Trigger();
            selected.Property = Selector.IsSelectedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BackgroundProperty, CardBackground));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Triggers.Add(selected);

            return style;
        }

        /// <summary>
        /// 构建页签容器模板：上方横向页签条（<see cref="TabPanel"/>，自身即项宿主），
        /// 下方内容宿主。<c>PART_SelectedContentHost</c> 是 <see cref="TabControl"/> 约定的部件名，
        /// 改名会导致选中内容无处显示；内容宿主双向随选中项切换，并拉伸填满剩余空间。
        /// </summary>
        private static ControlTemplate CreateTabControlTemplate()
        {
            FrameworkElementFactory dock = new FrameworkElementFactory(typeof(DockPanel));
            dock.SetValue(DockPanel.LastChildFillProperty, true);
            dock.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            // 页签条：IsItemsHost 让它直接承载 TabItem；靠上停靠，底部留 12px 与内容区隔开。
            FrameworkElementFactory tabPanel = new FrameworkElementFactory(typeof(TabPanel));
            tabPanel.SetValue(TabPanel.IsItemsHostProperty, true);
            tabPanel.SetValue(DockPanel.DockProperty, Dock.Top);
            tabPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 12));
            dock.AppendChild(tabPanel);

            // 内容宿主：PART_SelectedContentHost 是控件代码按名查找的契约，名称不可改。
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
            presenter.SetValue(FrameworkElement.NameProperty, "PART_SelectedContentHost");
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            dock.AppendChild(presenter);

            ControlTemplate template = new ControlTemplate(typeof(TabControl));
            template.VisualTree = dock;
            return template;
        }

        // ===== 画刷工具 =====

        /// <summary>
        /// 由 RGB 分量创建已冻结的纯色画刷。冻结后不可变，可安全跨线程共享，也省去每次绘制时的属性校验开销。
        /// 色板字段与各窗口里的局部配色都走这里，保证“创建即只读”这一约束。
        /// </summary>
        /// <param name="red">红色分量 0–255。</param>
        /// <param name="green">绿色分量 0–255。</param>
        /// <param name="blue">蓝色分量 0–255。</param>
        public static SolidColorBrush Frozen(byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        /// <summary>由 <see cref="Color"/> 值创建已冻结的纯色画刷；供需要带 alpha 或从别处取色的场景使用。</summary>
        public static SolidColorBrush BrushFrom(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // ===== 按钮样式构建 =====

        /// <summary>
        /// 构建按钮样式的通用骨架：底色/前景/描边/内边距/字号/字重/手型光标一次设好，
        /// 再由三个触发器覆盖交互态——悬停换底色、按下换更深底色、禁用降到 45% 不透明度。
        /// 模板固定为 <see cref="CreateButtonTemplate"/> 的圆角块，所有按钮共享同一外观骨架、只换颜色。
        /// </summary>
        /// <param name="background">常态底色（幽灵按钮传透明）。</param>
        /// <param name="foreground">文字色。</param>
        /// <param name="border">描边色（幽灵按钮传透明）。</param>
        /// <param name="hoverBackground">悬停底色。</param>
        /// <param name="pressedBackground">按下底色。</param>
        /// <param name="borderThickness">描边宽度，当前调用方都传 1。</param>
        private static Style CreateButtonStyle(
            Brush background,
            Brush foreground,
            Brush border,
            Brush hoverBackground,
            Brush pressedBackground,
            double borderThickness)
        {
            Style style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, background));
            style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, border));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(borderThickness)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 8, 16, 8)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateButtonTemplate()));

            /// <summary>悬停：换成调用方给的悬停底色。</summary>
            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BackgroundProperty, hoverBackground));
            style.Triggers.Add(hover);

            /// <summary>按下：换成更深的底色，给出明确的按压反馈。</summary>
            Trigger pressed = new Trigger();
            pressed.Property = ButtonBase.IsPressedProperty;
            pressed.Value = true;
            pressed.Setters.Add(new Setter(Control.BackgroundProperty, pressedBackground));
            style.Triggers.Add(pressed);

            /// <summary>禁用：只降不透明度而不改色，保持与其它按钮同一套配色。</summary>
            Trigger disabled = new Trigger();
            disabled.Property = UIElement.IsEnabledProperty;
            disabled.Value = false;
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            style.Triggers.Add(disabled);

            return style;
        }

        /// <summary>
        /// 构建按钮模板：单个 7px 圆角 <see cref="Border"/> 包一个居中的 <see cref="ContentPresenter"/>。
        /// 背景、描边、内边距全部模板绑定到控件属性，因此同一样式骨架改色即可得到主/次/幽灵三种按钮。
        /// </summary>
        private static ControlTemplate CreateButtonTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            // 内容居中：按钮宽度由 MinWidth/内容决定，文字始终落在外框正中。
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            ControlTemplate template = new ControlTemplate(typeof(Button));
            template.VisualTree = border;
            return template;
        }
    }
}
