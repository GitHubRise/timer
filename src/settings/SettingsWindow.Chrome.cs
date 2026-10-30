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
    internal sealed partial class SettingsWindow
    {
        private Border BuildPreviewCard()
        {
            Border card = new Border();
            card.Background = UiTheme.FieldBackground;
            card.BorderBrush = UiTheme.CardBorder;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(10);
            card.Height = 116;
            card.Padding = new Thickness(16, 12, 16, 12);

            Grid grid = new Grid();
            TextBlock label = UiTheme.MakeText("实时预览", 10.5, UiTheme.TextMuted, FontWeights.SemiBold);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(label);

            Viewbox viewbox = new Viewbox();
            viewbox.Stretch = Stretch.Uniform;
            viewbox.StretchDirection = StretchDirection.DownOnly;
            viewbox.HorizontalAlignment = HorizontalAlignment.Center;
            viewbox.VerticalAlignment = VerticalAlignment.Center;
            viewbox.Margin = new Thickness(0, 16, 0, 0);

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

        private TabItem CreateTab(string header, UIElement content)
        {
            TabItem item = new TabItem();
            item.Header = header;

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Margin = new Thickness(0, 2, 2, 0);

            StackPanel host = new StackPanel();
            host.Margin = new Thickness(2, 4, 10, 14);
            host.Children.Add(content);
            scroll.Content = host;
            item.Content = scroll;
            return item;
        }

        private Border BuildFooter()
        {
            Border footer = new Border();
            footer.Background = UiTheme.Frozen(0x1C, 0x1C, 0x22);
            footer.BorderBrush = UiTheme.CardBorder;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(24, 12, 24, 12);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel leftActions = new StackPanel();
            leftActions.Orientation = Orientation.Horizontal;
            leftActions.VerticalAlignment = VerticalAlignment.Center;

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

            grid.Children.Add(_footerStatus);
            Grid.SetColumn(_footerStatus, 1);

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

        private UIElement BuildColorRow()
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 14, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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
            pick.Padding = new Thickness(13, 6, 13, 6);
            pick.Height = 30;
            pick.Click += OnPickColor;
            AutomationProperties.SetAutomationId(pick, "PickColorButton");
            row.Children.Add(pick);
            Grid.SetColumn(pick, 1);

            return row;
        }

        private UIElement BuildPalette()
        {
            WrapPanel palette = new WrapPanel();
            palette.Margin = new Thickness(0, 12, 0, 0);
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
                swatch.ToolTip = names[i] + "  " + ColorUtil.ToHex(presetColor);
                swatch.MouseLeftButtonDown += delegate
                {
                    SelectColor(presetColor);
                };
                palette.Children.Add(swatch);
                _presets.Add(new ColorPreset(presetColor, swatch));
            }
            return palette;
        }

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

        private static Slider CreateSlider(double min, double max, double value, string automationId)
        {
            Slider slider = new Slider();
            slider.Minimum = min;
            slider.Maximum = max;
            slider.Value = value;
            slider.SmallChange = 1;
            slider.LargeChange = 5;
            slider.IsSnapToTickEnabled = true;
            slider.TickFrequency = 1;
            slider.Focusable = true;
            AutomationProperties.SetAutomationId(slider, automationId);
            return slider;
        }

        private static CheckBox CreateCheckBox(string text, bool isChecked, string automationId, string toolTip)
        {
            CheckBox check = new CheckBox();
            check.Content = text;
            check.IsChecked = isChecked;
            check.Foreground = UiTheme.TextPrimary;
            check.FontSize = 13;
            check.Margin = new Thickness(0, 4, 0, 5);
            check.VerticalContentAlignment = VerticalAlignment.Center;
            check.ToolTip = toolTip;
            AutomationProperties.SetAutomationId(check, automationId);
            return check;
        }

        private static DataTemplate CreateFontItemTemplate()
        {
            FrameworkElementFactory text = new FrameworkElementFactory(typeof(TextBlock));
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
