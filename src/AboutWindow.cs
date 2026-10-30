using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GameClock
{
    internal sealed class AboutWindow : Window
    {
        public AboutWindow()
        {
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

            Content = BuildContent();
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
        }

        private UIElement BuildContent()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border header = new Border();
            header.Background = UiTheme.Frozen(0x17, 0x17, 0x1C);
            header.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.Padding = new Thickness(24, 13, 14, 13);
            header.MouseLeftButtonDown += delegate
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            };
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock headerText = UiTheme.MakeText("关于", 14, UiTheme.TextPrimary, FontWeights.SemiBold);
            headerText.VerticalAlignment = VerticalAlignment.Center;
            headerGrid.Children.Add(headerText);

            Button close = new Button();
            close.Content = "×";
            close.Style = UiTheme.GhostButtonStyle;
            close.Width = 30;
            close.Height = 30;
            close.Padding = new Thickness(0);
            close.FontSize = 18;
            close.ToolTip = "关闭";
            close.Click += delegate { Close(); };
            AutomationProperties.SetAutomationId(close, "AboutCloseButton");
            headerGrid.Children.Add(close);
            Grid.SetColumn(close, 1);
            header.Child = headerGrid;
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            StackPanel content = new StackPanel();
            content.Margin = new Thickness(28, 22, 28, 18);

            Grid identity = new Grid();
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid clockIcon = BuildClockIcon();
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

            Border infoCard = UiTheme.MakeCard();
            infoCard.Margin = new Thickness(0, 20, 0, 12);
            StackPanel info = new StackPanel();
            info.Children.Add(UiTheme.MakeSectionTitle("版本信息"));

            AddInfoRow(info, "版本号", BuildInfo.Version);
            AddInfoRow(info, "编译号", string.IsNullOrEmpty(BuildInfo.BuildNumber) ? "dev" : BuildInfo.BuildNumber);
            if (!string.IsNullOrEmpty(BuildInfo.BuildDate))
            {
                AddInfoRow(info, "编译时间", BuildInfo.BuildDate);
            }
            AddInfoRow(info, "运行环境", ".NET Framework 4.8 · WPF");

            infoCard.Child = info;
            content.Children.Add(infoCard);

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

            TextBlock features = UiTheme.MakeText(
                "时间显示 · 天气 · 定时提醒 · 日程 · 鼠标穿透 · 开机自启",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            features.TextWrapping = TextWrapping.Wrap;
            features.Margin = new Thickness(2, 0, 2, 0);
            content.Children.Add(features);

            TextBlock copyright = UiTheme.MakeText(
                "Copyright © 2026 GameClock. All rights reserved.",
                10.5,
                UiTheme.TextMuted,
                FontWeights.Normal);
            copyright.Margin = new Thickness(2, 18, 2, 0);
            content.Children.Add(copyright);

            root.Children.Add(content);
            Grid.SetRow(content, 1);

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
            closeButton.Style = UiTheme.PrimaryButtonStyle;
            closeButton.MinWidth = 88;
            closeButton.Height = 34;
            closeButton.Click += delegate { Close(); };
            AutomationProperties.SetAutomationId(closeButton, "AboutOkButton");
            actions.Children.Add(closeButton);
            footer.Child = actions;
            root.Children.Add(footer);
            Grid.SetRow(footer, 2);

            return root;
        }

        private static Grid BuildClockIcon()
        {
            Grid host = new Grid();
            host.Width = 64;
            host.Height = 64;

            Ellipse ring = new Ellipse();
            ring.Width = 58;
            ring.Height = 58;
            ring.Stroke = UiTheme.Frozen(0x2E, 0xA8, 0xFF);
            ring.StrokeThickness = 3;
            ring.HorizontalAlignment = HorizontalAlignment.Center;
            ring.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(ring);

            Line hour = new Line();
            hour.X1 = 32;
            hour.Y1 = 32;
            hour.X2 = 32;
            hour.Y2 = 16;
            hour.Stroke = UiTheme.Frozen(0x2E, 0xA8, 0xFF);
            hour.StrokeThickness = 3;
            hour.StrokeStartLineCap = PenLineCap.Round;
            hour.StrokeEndLineCap = PenLineCap.Round;
            host.Children.Add(hour);

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

            Ellipse center = new Ellipse();
            center.Width = 6;
            center.Height = 6;
            center.Fill = UiTheme.TextPrimary;
            center.HorizontalAlignment = HorizontalAlignment.Center;
            center.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(center);

            return host;
        }

        private static void AddInfoRow(StackPanel parent, string label, string value)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 3, 0, 3);
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
