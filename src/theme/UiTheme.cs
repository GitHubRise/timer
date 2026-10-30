using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

// 暗色主题的唯一色值与控件样式来源（设置窗口、日程窗口、关于窗口共用）。
// 边界：不引用 AppSettings、不读配置；只提供静态画刷与样式工厂，配置值由上层传入。
// 项目只有 .cs 且没有 XAML/资源字典，所以样式与模板全部用 FrameworkElementFactory 在代码里搭。
// 画刷在创建时 Freeze，成为可跨线程安全共享的只读单例，任何地方都不应再改它的属性。
namespace GameClock
{
    /// <summary>
    /// 暗色主题色板与控件样式工厂；颜色取值集中在本类，控件代码不写字面色值。
    /// </summary>
    internal static class UiTheme
    {
        // 色板按「底色 → 边框 → 文字 → 强调 → 状态」排列；改配色只改这一段。

        /// <summary>设置/日程/关于窗口的整体底色（深灰黑，比纯黑柔和）。</summary>
        public static readonly SolidColorBrush WindowBackground = Frozen(0x17, 0x17, 0x1B);

        /// <summary>卡片与下拉面板底色，比窗口底色亮一档以形成层次。</summary>
        public static readonly SolidColorBrush CardBackground = Frozen(0x22, 0x22, 0x29);

        /// <summary>卡片、下拉面板与卡片内分隔线的边框色。</summary>
        public static readonly SolidColorBrush CardBorder = Frozen(0x35, 0x35, 0x3F);

        /// <summary>主文字色（接近白，避免纯白在黑底上过刺眼）。</summary>
        public static readonly SolidColorBrush TextPrimary = Frozen(0xF2, 0xF2, 0xF5);

        /// <summary>次要文字色：分组标题、未选中页签。</summary>
        public static readonly SolidColorBrush TextSecondary = Frozen(0x9E, 0x9E, 0xAA);

        /// <summary>弱化文字色：说明、占位与辅助提示。</summary>
        public static readonly SolidColorBrush TextMuted = Frozen(0x72, 0x72, 0x7E);

        /// <summary>品牌强调色（红），用于主按钮、选中态与警示文字。</summary>
        public static readonly SolidColorBrush Accent = Frozen(0xFF, 0x45, 0x3A);

        /// <summary>强调色的悬停态，比 Accent 略亮。</summary>
        public static readonly SolidColorBrush AccentHover = Frozen(0xFF, 0x5B, 0x50);

        /// <summary>强调色的按下态，比 Accent 略暗。</summary>
        public static readonly SolidColorBrush AccentPressed = Frozen(0xD9, 0x2D, 0x24);

        /// <summary>成功/启用状态色（绿），用于日程启用标记与保存成功提示。</summary>
        public static readonly SolidColorBrush Success = Frozen(0x54, 0xD6, 0x8C);

        /// <summary>输入框、下拉框与单选圆的内部底色，比窗口底色更暗。</summary>
        public static readonly SolidColorBrush FieldBackground = Frozen(0x1B, 0x1B, 0x21);

        /// <summary>输入框边框色，同时用作次要按钮的正常边框。</summary>
        public static readonly SolidColorBrush FieldBorder = Frozen(0x40, 0x40, 0x4B);

        /// <summary>次要按钮的默认底色，也用作页签/下拉项的悬停底色。</summary>
        public static readonly SolidColorBrush SecondaryButton = Frozen(0x2E, 0x2E, 0x37);

        /// <summary>次要按钮悬停底色，也用作下拉项选中底色。</summary>
        public static readonly SolidColorBrush SecondaryButtonHover = Frozen(0x3A, 0x3A, 0x46);

        /// <summary>次要按钮按下底色，比默认底色更深。</summary>
        public static readonly SolidColorBrush SecondaryButtonPressed = Frozen(0x25, 0x25, 0x2D);

        // 样式构建包含 ControlTemplate，成本不低；首次访问时创建并缓存，后续控件共用同一实例。
        // Style 在多个控件间共享时触发器的状态仍按控件各自计算，因此无需逐控件克隆。
        private static Style _primaryButtonStyle;
        private static Style _secondaryButtonStyle;
        private static Style _ghostButtonStyle;
        private static Style _comboBoxStyle;
        private static Style _radioButtonStyle;

        /// <summary>主按钮样式：强调色底 + 白字，用于「保存」等主操作。</summary>
        public static Style PrimaryButtonStyle
        {
            get
            {
                // 首次访问才构建样式：窗口可能只用到其中一两种，没必要在启动时全部建好。
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

        /// <summary>次要按钮样式：深灰底 + 浅色边框，用于「应用」「取消」等次级操作。</summary>
        public static Style SecondaryButtonStyle
        {
            get
            {
                // 首次访问才构建样式，之后复用同一实例，避免每开一个窗口重建模板。
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

        /// <summary>幽灵按钮样式：透明底无边框，只有文字与悬停底色，用于关闭/关于等弱操作。</summary>
        public static Style GhostButtonStyle
        {
            get
            {
                // 首次访问才构建样式；幽灵按钮只有标题栏关闭与「关于」用，构建成本同样摊薄。
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

        /// <summary>
        /// 创建一个卡片容器：圆角 10、内边距 16/14、下外边距 14（卡片之间的垂直间隔）。
        /// 尺寸写死是有意的：设置页各卡片需要等宽同高节奏，不做自适应。
        /// </summary>
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
        /// 创建自动换行的文本块；字号与颜色由调用方给出，便于同一套排版复用到不同位置。
        /// 默认 TextWrapping=Wrap，避免长说明文字把设置窗口撑出横向滚动。
        /// </summary>
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

        /// <summary>创建卡片内的分组标题：12px 次要色半粗体，下边距 10 与内容拉开。</summary>
        public static TextBlock MakeSectionTitle(string text)
        {
            TextBlock block = MakeText(text, 12, TextSecondary, FontWeights.SemiBold);
            block.Margin = new Thickness(0, 0, 0, 10);
            return block;
        }

        /// <summary>
        /// 给 TextBox 套深色输入框外观；逐属性赋值而不设 Style，因为输入框用法差异大（单行/多行）。
        /// 13px 是设置页正文的标准字号；SelectionBrush 用强调色，选中文字用白色保证对比度。
        /// </summary>
        public static void ApplyTextBoxStyle(TextBox box)
        {
            // 只改外观属性、不换 Template：保留系统 TextBox 的撤销、IME 与文本选择行为。
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

        /// <summary>
        /// 给 ComboBox 套深色样式（含下拉面板模板）；样式按进程缓存一份，所有下拉框共用。
        /// 只赋值 Style、不碰控件其余属性，避免覆盖调用方设置的数据绑定。
        /// </summary>
        public static void ApplyComboBoxStyle(ComboBox combo)
        {
            // 下拉模板较重（含 Popup 与 ItemsPresenter），按进程只建一次。
            if (_comboBoxStyle == null)
            {
                _comboBoxStyle = CreateComboBoxStyle();
            }
            combo.Style = _comboBoxStyle;
        }

        /// <summary>给 RadioButton 套深色圆形单选样式；同样只设置 Style，不干扰选中状态绑定。</summary>
        public static void ApplyRadioButtonStyle(RadioButton radio)
        {
            // 单选样式同样懒加载，由第一个 RadioButton 触发构建。
            if (_radioButtonStyle == null)
            {
                _radioButtonStyle = CreateRadioButtonStyle();
            }
            radio.Style = _radioButtonStyle;
        }

        /// <summary>
        /// 把 TabControl 换成暗色外观：透明底 + 自绘页签 + 只保留「页签栏 + 内容宿主」的模板。
        /// 必须显式设置 Template/ItemContainerStyle：项目没有 App.xaml 资源字典，隐式样式不存在，
        /// 漏设会让 WPF 回落到系统 Aero 主题，页签在深色背景上会露出浅色底。
        /// </summary>
        public static void ApplyDarkTabs(TabControl tabs)
        {
            tabs.Background = Brushes.Transparent;
            tabs.BorderThickness = new Thickness(0);
            tabs.Padding = new Thickness(0);
            tabs.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            tabs.VerticalContentAlignment = VerticalAlignment.Stretch;
            tabs.ItemContainerStyle = CreateTabItemStyle();
            tabs.Template = CreateTabControlTemplate();
        }

        /// <summary>
        /// 下拉框样式：固定 34px 高、13px 字号，与输入框和按钮的视觉高度对齐。
        /// MaxDropDownHeight 330 约等于 10 行下拉项，再高会顶到屏幕底部。
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
        /// 构造下拉框模板：底层 Border 画底与边框，上面铺透明的 ToggleButton 负责开合，
        /// ContentPresenter 显示选中项，Path 画箭头，Popup 承载下拉列表。
        /// 模板内的 Padding/Margin 是为对齐箭头与输入框内边距服务的，调整时需一起改。
        /// </summary>
        private static ControlTemplate CreateComboBoxTemplate()
        {
            // 根容器透明，全部视觉由子元素负责；底与边框用模板绑定，改色只需改控件属性。
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);
            root.AppendChild(border);

            // 透明 ToggleButton 铺满整个控件：ClickMode=Press 让按下即开合（默认 Release 要等抬起），
            // Focusable=false 避免抢走输入焦点，IsChecked 双向绑定到 IsDropDownOpen。
            FrameworkElementFactory toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.SetValue(ToggleButton.FocusableProperty, false);
            toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
            ControlTemplate toggleTemplate = new ControlTemplate(typeof(ToggleButton));
            FrameworkElementFactory toggleBorder = new FrameworkElementFactory(typeof(Border));
            toggleBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            toggleTemplate.VisualTree = toggleBorder;
            toggle.SetValue(Control.TemplateProperty, toggleTemplate);

            Binding openBinding = new Binding("IsDropDownOpen");
            openBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            openBinding.Mode = BindingMode.TwoWay;
            toggle.SetValue(ToggleButton.IsCheckedProperty, openBinding);
            root.AppendChild(toggle);

            // 选中项展示区：左留 11 与输入框内边距对齐，右留 30 给箭头让位；
            // IsHitTestVisible=false 让点击穿透到下面的 ToggleButton 上。
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(11, 0, 30, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(content);

            // 箭头：9×4.5 的下折线，描边取次要文字色并做圆头，同样不接收点击。
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

            // 下拉面板：AllowsTransparency 才能显示圆角（Popup 默认矩形裁剪）；
            // StaysOpen=false 让点击面板外自动收起，与系统下拉框行为一致。
            FrameworkElementFactory popup = new FrameworkElementFactory(typeof(Popup));
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetValue(Popup.StaysOpenProperty, false);

            Binding popupBinding = new Binding("IsDropDownOpen");
            popupBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBinding.Mode = BindingMode.TwoWay;
            popup.SetValue(Popup.IsOpenProperty, popupBinding);

            // 面板外观：卡片底色 + 1px 边框 + 圆角 8 + 4px 内衬，和卡片同一套视觉语言。
            FrameworkElementFactory popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, CardBackground);
            popupBorder.SetValue(Border.BorderBrushProperty, CardBorder);
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
            popupBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));

            // 面板最小宽度跟随控件实际宽度，避免短选项比控件窄而显得错位；最大高度跟随 MaxDropDownHeight。
            Binding minWidthBinding = new Binding("ActualWidth");
            minWidthBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBorder.SetValue(FrameworkElement.MinWidthProperty, minWidthBinding);

            Binding maxHeightBinding = new Binding("MaxDropDownHeight");
            maxHeightBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBorder.SetValue(FrameworkElement.MaxHeightProperty, maxHeightBinding);

            // 只允许竖向滚动；CanContentScroll=true 让滚动按项而非像素，长列表滚动更顺。
            FrameworkElementFactory scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);

            // 方向键限制在下拉内部循环，避免焦点跑出 Popup 落到设置窗口其他控件上。
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
        /// 构造下拉项样式：12.5px（比选中值小半号以区分层级），圆角 5 的悬停/选中底色。
        /// </summary>
        private static Style CreateComboBoxItemStyle()
        {
            Style style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(1));

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            border.AppendChild(content);

            ControlTemplate template = new ControlTemplate(typeof(ComboBoxItem));
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            // 悬停与选中都是「后声明者生效」：选中触发器声明在悬停之后，
            // 因此鼠标停在选中项上时显示选中底色而不是悬停底色。
            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BackgroundProperty, SecondaryButton));
            style.Triggers.Add(hover);

            Trigger selected = new Trigger();
            selected.Property = Selector.IsSelectedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BackgroundProperty, SecondaryButtonHover));
            style.Triggers.Add(selected);

            return style;
        }

        /// <summary>
        /// 构造单选样式：13.5px 字号（与正文行高匹配）、手型光标，靠边框色表达悬停与选中。
        /// </summary>
        private static Style CreateRadioButtonStyle()
        {
            Style style = new Style(typeof(RadioButton));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.5));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, FieldBorder));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateRadioButtonTemplate()));

            // 悬停只把边框调亮，保持「未选中」的语义，不用填充色抢注意力。
            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, TextSecondary));
            style.Triggers.Add(hover);

            // 选中态用强调色描边；触发器声明在悬停之后，选中项悬停时仍是强调色。
            Trigger selected = new Trigger();
            selected.Property = ToggleButton.IsCheckedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
            style.Triggers.Add(selected);

            return style;
        }

        /// <summary>
        /// 构造单选的视觉树：16px 外圈 + 7px 内点，内点可见性绑定 IsChecked。
        /// 内点用 BooleanToVisibilityConverter 在代码里绑定，因为项目没有 XAML 资源可声明转换器。
        /// </summary>
        private static ControlTemplate CreateRadioButtonTemplate()
        {
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(DockPanel));
            root.SetValue(DockPanel.LastChildFillProperty, true);
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            // 外圈固定 16px 并用 Dock.Left 定宽，保证同一列的单选圆点左对齐、内容文字起点一致。
            FrameworkElementFactory circle = new FrameworkElementFactory(typeof(Grid));
            circle.SetValue(FrameworkElement.WidthProperty, 16.0);
            circle.SetValue(FrameworkElement.HeightProperty, 16.0);
            circle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            circle.SetValue(DockPanel.DockProperty, Dock.Left);

            FrameworkElementFactory outer = new FrameworkElementFactory(typeof(Ellipse));
            outer.SetValue(Shape.StrokeProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            outer.SetValue(Shape.StrokeThicknessProperty, 1.4);
            outer.SetValue(Shape.FillProperty, FieldBackground);
            circle.AppendChild(outer);

            FrameworkElementFactory inner = new FrameworkElementFactory(typeof(Ellipse));
            inner.SetValue(FrameworkElement.WidthProperty, 7.0);
            inner.SetValue(FrameworkElement.HeightProperty, 7.0);
            inner.SetValue(Shape.FillProperty, Accent);
            inner.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            inner.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            // 内点可见性跟随 IsChecked；RadioButton 在模板化父级上暴露 IsChecked 供绑定。
            Binding visibility = new Binding("IsChecked");
            visibility.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            visibility.Converter = new BooleanToVisibilityConverter();
            inner.SetValue(UIElement.VisibilityProperty, visibility);
            circle.AppendChild(inner);

            root.AppendChild(circle);

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

        /// <summary>
        /// 构造页签样式：圆角 7 的胶囊、16/8 内边距、右间距 6；悬停变亮、选中用卡片底色。
        /// 页签用 13px 半粗体，比正文略强调又不至于抢过卡片标题。
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

            // 页签的命中区域是整块 Border；Header 通过 ContentSource 绑定，不额外包内容属性。
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            ControlTemplate template = new ControlTemplate(typeof(TabItem));
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            // 悬停：底色变亮并把文字提为主文字色，明确提示可点击。
            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BackgroundProperty, SecondaryButton));
            hover.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Triggers.Add(hover);

            // 选中：用卡片底色与下方内容区衔接，文字用主文字色；后声明保证覆盖悬停。
            Trigger selected = new Trigger();
            selected.Property = Selector.IsSelectedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BackgroundProperty, CardBackground));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Triggers.Add(selected);

            return style;
        }

        /// <summary>
        /// 构造 TabControl 模板：顶部页签栏（TabPanel）+ 铺满剩余空间的内容宿主。
        /// PART_SelectedContentHost 是 TabControl 的模板契约名，改名会导致选中页内容无处承载。
        /// </summary>
        private static ControlTemplate CreateTabControlTemplate()
        {
            FrameworkElementFactory dock = new FrameworkElementFactory(typeof(DockPanel));
            dock.SetValue(DockPanel.LastChildFillProperty, true);
            dock.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            // 页签栏固定在顶部，下边距 12 与内容区拉开；IsItemsHost 告诉 TabPanel 承载页签项。
            FrameworkElementFactory tabPanel = new FrameworkElementFactory(typeof(TabPanel));
            tabPanel.SetValue(TabPanel.IsItemsHostProperty, true);
            tabPanel.SetValue(DockPanel.DockProperty, Dock.Top);
            tabPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 12));
            dock.AppendChild(tabPanel);

            // 内容宿主铺满剩余空间；ContentSource=SelectedContent 由 TabControl 自动切换页内容。
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

        /// <summary>
        /// 由 RGB 分量创建已冻结的纯色画刷；Freeze 后可跨线程共享，也省去每次渲染的变更通知开销。
        /// </summary>
        public static SolidColorBrush Frozen(byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 由 Color 创建已冻结的画刷；时钟文字等按用户配置色动态换色时用，同样可安全共享。
        /// </summary>
        public static SolidColorBrush BrushFrom(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 构造按钮样式：底色/文字/边框/悬停/按下五组颜色由调用方给，模板统一用圆角 7 的 Border。
        /// borderThickness 一律传 1（幽灵按钮用透明边框）：边框参与布局，统一厚度三种按钮才能等高。
        /// </summary>
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

            // 悬停与按下各换一档底色；按下声明在后，两者同时命中时以按下底色为准。
            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BackgroundProperty, hoverBackground));
            style.Triggers.Add(hover);

            Trigger pressed = new Trigger();
            pressed.Property = ButtonBase.IsPressedProperty;
            pressed.Value = true;
            pressed.Setters.Add(new Setter(Control.BackgroundProperty, pressedBackground));
            style.Triggers.Add(pressed);

            // 禁用态只降透明度、不换底色：三种按钮共用同一套触发器，换色会破坏各自配色。
            Trigger disabled = new Trigger();
            disabled.Property = UIElement.IsEnabledProperty;
            disabled.Value = false;
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            style.Triggers.Add(disabled);

            return style;
        }

        /// <summary>
        /// 构造按钮模板：圆角 7 的 Border 承载内容并模板绑定背景/边框/内边距。
        /// SnapsToDevicePixels 让 1px 边框落在整数像素上，避免高 DPI 下边框发虚。
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
