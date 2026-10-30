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
    internal static class UiTheme
    {
        public static readonly SolidColorBrush WindowBackground = Frozen(0x17, 0x17, 0x1B);
        public static readonly SolidColorBrush CardBackground = Frozen(0x22, 0x22, 0x29);
        public static readonly SolidColorBrush CardBorder = Frozen(0x35, 0x35, 0x3F);
        public static readonly SolidColorBrush TextPrimary = Frozen(0xF2, 0xF2, 0xF5);
        public static readonly SolidColorBrush TextSecondary = Frozen(0x9E, 0x9E, 0xAA);
        public static readonly SolidColorBrush TextMuted = Frozen(0x72, 0x72, 0x7E);
        public static readonly SolidColorBrush Accent = Frozen(0xFF, 0x45, 0x3A);
        public static readonly SolidColorBrush AccentHover = Frozen(0xFF, 0x5B, 0x50);
        public static readonly SolidColorBrush AccentPressed = Frozen(0xD9, 0x2D, 0x24);
        public static readonly SolidColorBrush Success = Frozen(0x54, 0xD6, 0x8C);
        public static readonly SolidColorBrush FieldBackground = Frozen(0x1B, 0x1B, 0x21);
        public static readonly SolidColorBrush FieldBorder = Frozen(0x40, 0x40, 0x4B);
        public static readonly SolidColorBrush SecondaryButton = Frozen(0x2E, 0x2E, 0x37);
        public static readonly SolidColorBrush SecondaryButtonHover = Frozen(0x3A, 0x3A, 0x46);
        public static readonly SolidColorBrush SecondaryButtonPressed = Frozen(0x25, 0x25, 0x2D);

        private static Style _primaryButtonStyle;
        private static Style _secondaryButtonStyle;
        private static Style _ghostButtonStyle;
        private static Style _comboBoxStyle;
        private static Style _radioButtonStyle;

        public static Style PrimaryButtonStyle
        {
            get
            {
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

        public static TextBlock MakeSectionTitle(string text)
        {
            TextBlock block = MakeText(text, 12, TextSecondary, FontWeights.SemiBold);
            block.Margin = new Thickness(0, 0, 0, 10);
            return block;
        }

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

        public static void ApplyComboBoxStyle(ComboBox combo)
        {
            if (_comboBoxStyle == null)
            {
                _comboBoxStyle = CreateComboBoxStyle();
            }
            combo.Style = _comboBoxStyle;
        }

        public static void ApplyRadioButtonStyle(RadioButton radio)
        {
            if (_radioButtonStyle == null)
            {
                _radioButtonStyle = CreateRadioButtonStyle();
            }
            radio.Style = _radioButtonStyle;
        }

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

        private static ControlTemplate CreateComboBoxTemplate()
        {
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);
            root.AppendChild(border);

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

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(11, 0, 30, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(content);

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

            FrameworkElementFactory popup = new FrameworkElementFactory(typeof(Popup));
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetValue(Popup.StaysOpenProperty, false);

            Binding popupBinding = new Binding("IsDropDownOpen");
            popupBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBinding.Mode = BindingMode.TwoWay;
            popup.SetValue(Popup.IsOpenProperty, popupBinding);

            FrameworkElementFactory popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, CardBackground);
            popupBorder.SetValue(Border.BorderBrushProperty, CardBorder);
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
            popupBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));

            Binding minWidthBinding = new Binding("ActualWidth");
            minWidthBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBorder.SetValue(FrameworkElement.MinWidthProperty, minWidthBinding);

            Binding maxHeightBinding = new Binding("MaxDropDownHeight");
            maxHeightBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBorder.SetValue(FrameworkElement.MaxHeightProperty, maxHeightBinding);

            FrameworkElementFactory scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);

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

        private static Style CreateRadioButtonStyle()
        {
            Style style = new Style(typeof(RadioButton));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.5));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, FieldBorder));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateRadioButtonTemplate()));

            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, TextSecondary));
            style.Triggers.Add(hover);

            Trigger selected = new Trigger();
            selected.Property = ToggleButton.IsCheckedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
            style.Triggers.Add(selected);

            return style;
        }

        private static ControlTemplate CreateRadioButtonTemplate()
        {
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(DockPanel));
            root.SetValue(DockPanel.LastChildFillProperty, true);
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

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

            Trigger hover = new Trigger();
            hover.Property = UIElement.IsMouseOverProperty;
            hover.Value = true;
            hover.Setters.Add(new Setter(Control.BackgroundProperty, SecondaryButton));
            hover.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Triggers.Add(hover);

            Trigger selected = new Trigger();
            selected.Property = Selector.IsSelectedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BackgroundProperty, CardBackground));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Triggers.Add(selected);

            return style;
        }

        private static ControlTemplate CreateTabControlTemplate()
        {
            FrameworkElementFactory dock = new FrameworkElementFactory(typeof(DockPanel));
            dock.SetValue(DockPanel.LastChildFillProperty, true);
            dock.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            FrameworkElementFactory tabPanel = new FrameworkElementFactory(typeof(TabPanel));
            tabPanel.SetValue(TabPanel.IsItemsHostProperty, true);
            tabPanel.SetValue(DockPanel.DockProperty, Dock.Top);
            tabPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 12));
            dock.AppendChild(tabPanel);

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

        public static SolidColorBrush Frozen(byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        public static SolidColorBrush BrushFrom(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

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

            Trigger disabled = new Trigger();
            disabled.Property = UIElement.IsEnabledProperty;
            disabled.Value = false;
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            style.Triggers.Add(disabled);

            return style;
        }

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
