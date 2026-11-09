// 设置窗口使用的暗色细滚动条样式。
// 宽度固定 10px、无上下箭头，滑块为圆角半透明矩形，悬停/拖动时变为强调蓝。
// 模板全部用代码构建（FrameworkElementFactory），不依赖 XAML 资源字典；
// ModernTrack 将 IsDirectionReversed 设为 true，使值增大时滑块向下移动，
// 与纵向滚动条的视觉方向一致，避免滚轮滚动与滑块位置相反。

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace GameClock
{

    /// <summary>
    /// 纵向 <see cref="Track"/> 的专用子类：固定不可聚焦、垂直方向，并反转方向，
    /// 让 <see cref="Track.Value"/> 增大时滑块向下移动。滑块使用
    /// <see cref="ModernScrollBar.CreateThumbStyle"/> 生成的圆角样式。
    /// 生命周期：每个滚动条模板创建一个实例，由模板持有。
    /// </summary>
    internal sealed class ModernTrack : Track
    {

        /// <summary>初始化轨道并挂载滑块；方向与焦点行为在此一次确定。</summary>
        public ModernTrack()
        {
            // 滚动条不参与 Tab 焦点链，避免键盘操作跳进滑块。
            Focusable = false;
            Orientation = Orientation.Vertical;
            // 反转后 Value 增大对应屏幕上的向下移动，符合纵向滚动直觉。
            IsDirectionReversed = true;
            // 滑块样式在构造时创建，模板只需负责布局。
            Thumb = new Thumb
            {
                Focusable = false,
                Style = ModernScrollBar.CreateThumbStyle()
            };
        }
    }

    /// <summary>
    /// 暗色细滚动条的样式工厂：为 <see cref="ScrollBar"/> 生成整体样式与模板，
    /// 并为 <see cref="Thumb"/> 生成带悬停/拖动反馈的样式。
    /// 每次调用都新建 Style/ControlTemplate，由调用方持有，工厂本身不缓存。
    /// 样式不引用 <c>AppSettings</c>，颜色取自 <see cref="UiTheme"/> 的固定色值。
    /// </summary>
    internal static class ModernScrollBar
    {

        /// <summary>
        /// 创建滚动条整体样式：固定 10px 宽、透明背景、不可聚焦，并替换为无箭头模板。
        /// </summary>
        public static Style CreateStyle()
        {
            Style style = new Style(typeof(ScrollBar));
            // 10px 是设置窗口内容区可接受的最窄宽度，再窄会难以点中滑块。
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 10.0));
            style.Setters.Add(new Setter(FrameworkElement.FocusableProperty, false));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateTemplate()));
            return style;
        }

        /// <summary>
        /// 创建滑块样式：最小高度 30px，常态深灰，悬停变亮蓝，拖动变深蓝。
        /// 光标使用 <see cref="System.Windows.Input.Cursors.SizeNS"/> 提示可纵向拖动。
        /// </summary>
        public static Style CreateThumbStyle()
        {
            Style style = new Style(typeof(Thumb));
            // 最小高度保证内容很短时滑块仍有足够抓取面积。
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30.0));
            style.Setters.Add(new Setter(Control.BackgroundProperty, UiTheme.Frozen(0x45, 0x4A, 0x59)));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.SizeNS));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateThumbTemplate()));

            // 悬停反馈：用强调蓝提示当前可拖动。
            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BackgroundProperty, UiTheme.Frozen(0x2E, 0x9B, 0xFF)));
            style.Triggers.Add(hover);

            // 拖动反馈：按下期间用更深的蓝色，与悬停状态区分。
            Trigger drag = new Trigger();
            drag.Property = Thumb.IsDraggingProperty;
            drag.Value = true;
            drag.Setters.Add(new Setter(Control.BackgroundProperty, UiTheme.Frozen(0x1E, 0x90, 0xFF)));
            style.Triggers.Add(drag);

            return style;
        }

        /// <summary>
        /// 构建滚动条模板：底层圆角轨道加覆盖其上的 <see cref="ModernTrack"/>，
        /// 轨道的最小值、最大值、视口与当前值通过模板绑定同步到滚动条。
        /// </summary>
        private static ControlTemplate CreateTemplate()
        {
            // Grid 作为模板根，背景透明以接收整条滚动区域的鼠标事件。
            FrameworkElementFactory grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            // 轨道底色比面板略深，圆角 4px 与滑块形成层次。
            FrameworkElementFactory trackBackground = new FrameworkElementFactory(typeof(Border));
            trackBackground.SetValue(Border.BackgroundProperty, UiTheme.Frozen(0x18, 0x18, 0x1F));
            trackBackground.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            trackBackground.SetValue(FrameworkElement.MarginProperty, new Thickness(1, 1, 1, 1));
            grid.AppendChild(trackBackground);

            // 轨道上下各留 1px，避免滑块贴住滚动区域边缘。
            FrameworkElementFactory track = new FrameworkElementFactory(typeof(ModernTrack));
            track.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));
            track.SetValue(Track.MinimumProperty, new TemplateBindingExtension(ScrollBar.MinimumProperty));
            track.SetValue(Track.MaximumProperty, new TemplateBindingExtension(ScrollBar.MaximumProperty));
            track.SetValue(Track.ViewportSizeProperty, new TemplateBindingExtension(ScrollBar.ViewportSizeProperty));

            // 值用双向绑定回写 ScrollBar.Value，拖动滑块才能实际滚动内容。
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
        /// 构建滑块模板：单个圆角 <see cref="Border"/>，背景通过模板绑定取 Thumb.Background，
        /// 内缩 2px 让滑块与轨道之间留出空隙。
        /// </summary>
        private static ControlTemplate CreateThumbTemplate()
        {
            // 背景用 TemplateBinding，悬停/拖动触发器只需改 Background。
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 2, 2, 2));
            // 对齐设备像素，避免圆角在缩放时发虚。
            border.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);

            ControlTemplate template = new ControlTemplate(typeof(Thumb));
            template.VisualTree = border;
            return template;
        }
    }
}
