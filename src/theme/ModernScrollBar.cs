using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

// 设置窗口用的细滚动条：10px 宽、无上下箭头按钮、圆角拇指，悬停/拖拽时才变蓝。
// 边界：只产出 Style / ControlTemplate，不引用任何窗口或 AppSettings；颜色一律取自 UiTheme。
// 为什么自绘：系统 Aero 滚动条在深色底上会露出浅色槽与箭头，且系统默认宽度（约 17px）对设置页过宽。
// 使用方式：窗口构造时把 CreateStyle() 注册到 Resources[typeof(ScrollBar)]，即对整个窗口隐式生效。
namespace GameClock
{
    /// <summary>
    /// 自绘滚动条的轨道：继承 Track 只为在构造时固定垂直方向、反向映射与拇指样式，
    /// 省去在每个模板里重复设置这些属性。IsDirectionReversed=true 表示「拇指向下 = 值增大」。
    /// </summary>
    internal sealed class ModernTrack : Track
    {
        /// <summary>创建轨道并挂上 ModernScrollBar 统一提供的拇指。</summary>
        public ModernTrack()
        {
            Focusable = false;
            Orientation = Orientation.Vertical;
            IsDirectionReversed = true;
            Thumb = new Thumb
            {
                Focusable = false,
                Style = ModernScrollBar.CreateThumbStyle()
            };
        }
    }

    /// <summary>
    /// 细滚动条样式工厂：提供 ScrollBar 样式与 Thumb 样式，供各窗口注册为隐式样式。
    /// </summary>
    internal static class ModernScrollBar
    {
        /// <summary>
        /// 创建 ScrollBar 样式：宽 10px、透明底、不参与 Tab 焦点（滚动条不该拦截键盘导航）。
        /// </summary>
        public static Style CreateStyle()
        {
            Style style = new Style(typeof(ScrollBar));
            // 透明底 + 10px 固定宽：深色内容区无需另画背景，也比系统默认约 17px 少挤占文本。
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 10.0));
            style.Setters.Add(new Setter(FrameworkElement.FocusableProperty, false));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateTemplate()));
            return style;
        }

        /// <summary>
        /// 创建拇指样式：最小高度 30px（长列表下也点得中），默认深灰、悬停亮蓝、拖拽深蓝。
        /// 三档颜色直接写在触发器里，不走 UiTheme 色板——蓝色只用于滚动条，避免污染主题色。
        /// </summary>
        public static Style CreateThumbStyle()
        {
            Style style = new Style(typeof(Thumb));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30.0));
            style.Setters.Add(new Setter(Control.BackgroundProperty, UiTheme.Frozen(0x45, 0x4A, 0x59)));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.SizeNS));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateThumbTemplate()));

            // 悬停变亮蓝、拖拽用更深蓝；拖拽触发器声明在后，两者同时命中时以深蓝为准。
            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BackgroundProperty, UiTheme.Frozen(0x2E, 0x9B, 0xFF)));
            style.Triggers.Add(hover);

            Trigger drag = new Trigger();
            drag.Property = Thumb.IsDraggingProperty;
            drag.Value = true;
            drag.Setters.Add(new Setter(Control.BackgroundProperty, UiTheme.Frozen(0x1E, 0x90, 0xFF)));
            style.Triggers.Add(drag);

            return style;
        }

        /// <summary>
        /// 构造滚动条模板：底层是圆角 4 的槽，上层叠 ModernTrack。
        /// Track 的 Value 必须双向绑定到 ScrollBar.Value，否则拖拽拇指不会回写滚动位置；
        /// Minimum/Maximum/ViewportSize 用模板绑定，拇指长度才能随内容比例变化。
        /// </summary>
        private static ControlTemplate CreateTemplate()
        {
            FrameworkElementFactory grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            // 1px 内缩让槽与 10px 宽度之间留出细边，避免槽紧贴控件边缘显得发闷。
            FrameworkElementFactory trackBackground = new FrameworkElementFactory(typeof(Border));
            trackBackground.SetValue(Border.BackgroundProperty, UiTheme.Frozen(0x18, 0x18, 0x1F));
            trackBackground.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            trackBackground.SetValue(FrameworkElement.MarginProperty, new Thickness(1, 1, 1, 1));
            grid.AppendChild(trackBackground);

            // 上下各留 1px，与槽的内缩对齐，拇指拖到两端时不会压住圆角。
            FrameworkElementFactory track = new FrameworkElementFactory(typeof(ModernTrack));
            track.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));
            track.SetValue(Track.MinimumProperty, new TemplateBindingExtension(ScrollBar.MinimumProperty));
            track.SetValue(Track.MaximumProperty, new TemplateBindingExtension(ScrollBar.MaximumProperty));
            track.SetValue(Track.ViewportSizeProperty, new TemplateBindingExtension(ScrollBar.ViewportSizeProperty));

            // 双向绑定：拖拽/点击轨道改的是 Track.Value，必须回写到 ScrollBar.Value 才会真正滚动内容。
            Binding valueBinding = new Binding("Value");
            valueBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            valueBinding.Mode = BindingMode.TwoWay;
            track.SetValue(Track.ValueProperty, valueBinding);

            grid.AppendChild(track);

            ControlTemplate template = new ControlTemplate(typeof(ScrollBar));
            template.VisualTree = grid;
            return template;
        }

        /// <summary>
        /// 构造拇指模板：圆角 3、四周内缩 2px 的 Border，内缩让拇指与 10px 宽的槽之间留出空隙。
        /// </summary>
        private static ControlTemplate CreateThumbTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            // 背景模板绑定到 Thumb.Background，三档颜色才能由 CreateThumbStyle 的触发器统一切换。
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 2, 2, 2));
            border.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

            ControlTemplate template = new ControlTemplate(typeof(Thumb));
            template.VisualTree = border;
            return template;
        }
    }
}
