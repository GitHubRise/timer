using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace GameClock
{
    /// <summary>
    /// A slim, modern vertical scroll bar used by the settings window.
    /// It deliberately has no arrow buttons and uses a rounded translucent thumb.
    /// </summary>
    internal sealed class ModernTrack : Track
    {
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

    internal static class ModernScrollBar
    {
        public static Style CreateStyle()
        {
            Style style = new Style(typeof(ScrollBar));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 10.0));
            style.Setters.Add(new Setter(FrameworkElement.FocusableProperty, false));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateTemplate()));
            return style;
        }

        public static Style CreateThumbStyle()
        {
            Style style = new Style(typeof(Thumb));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30.0));
            style.Setters.Add(new Setter(Control.BackgroundProperty, UiTheme.Frozen(0x45, 0x4A, 0x59)));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.SizeNS));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateThumbTemplate()));

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

        private static ControlTemplate CreateTemplate()
        {
            FrameworkElementFactory grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            FrameworkElementFactory trackBackground = new FrameworkElementFactory(typeof(Border));
            trackBackground.SetValue(Border.BackgroundProperty, UiTheme.Frozen(0x18, 0x18, 0x1F));
            trackBackground.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            trackBackground.SetValue(FrameworkElement.MarginProperty, new Thickness(1, 1, 1, 1));
            grid.AppendChild(trackBackground);

            FrameworkElementFactory track = new FrameworkElementFactory(typeof(ModernTrack));
            track.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));
            track.SetValue(Track.MinimumProperty, new TemplateBindingExtension(ScrollBar.MinimumProperty));
            track.SetValue(Track.MaximumProperty, new TemplateBindingExtension(ScrollBar.MaximumProperty));
            track.SetValue(Track.ViewportSizeProperty, new TemplateBindingExtension(ScrollBar.ViewportSizeProperty));

            Binding valueBinding = new Binding("Value");
            valueBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            valueBinding.Mode = BindingMode.TwoWay;
            track.SetValue(Track.ValueProperty, valueBinding);

            grid.AppendChild(track);

            ControlTemplate template = new ControlTemplate(typeof(ScrollBar));
            template.VisualTree = grid;
            return template;
        }

        private static ControlTemplate CreateThumbTemplate()
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
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
