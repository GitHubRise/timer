using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GameClock
{
    /// <summary>
    /// 任务栏时钟展开的“今日”面板：大号时间、农历、天气与当天提醒/日程。
    /// 面板不抢焦点，鼠标移开后由任务栏时钟负责关闭。
    /// </summary>
    internal sealed class TaskbarPanelWindow : Window
    {
        private const double PanelWidth = 340.0;

        private readonly TextBlock _timeText;
        private readonly TextBlock _dateText;
        private readonly TextBlock _lunarText;
        private readonly TextBlock _weatherText;
        private readonly TextBlock _emptyText;
        private readonly StackPanel _agendaPanel;
        private readonly Border _agendaSeparator;
        private readonly TextBlock _modeText;
        private string _lastSignature;
        private string _lastWeatherText;
        private bool _positioned;
        private NativeMethods.RECT _anchorRect;
        private double _anchorDpi = 1.0;
        private bool _hasAnchor;

        public TaskbarPanelWindow()
        {
            Title = "GameClockTaskbarPanel";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = PanelWidth;
            Left = -10000;
            Top = -10000;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            Border card = new Border();
            card.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x22));
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x46));
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(14);
            card.Padding = new Thickness(20, 18, 20, 14);
            card.Margin = new Thickness(12);
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 26;
            shadow.ShadowDepth = 7;
            shadow.Opacity = 0.5;
            shadow.Color = Colors.Black;
            card.Effect = shadow;

            StackPanel root = new StackPanel();

            _timeText = UiTheme.MakeText("00:00:00", 34, UiTheme.TextPrimary, FontWeights.SemiBold);
            _timeText.TextWrapping = TextWrapping.NoWrap;
            root.Children.Add(_timeText);

            _dateText = UiTheme.MakeText("", 13, UiTheme.TextPrimary, FontWeights.Normal);
            _dateText.Margin = new Thickness(0, 2, 0, 0);
            root.Children.Add(_dateText);

            _lunarText = UiTheme.MakeText("", 12, UiTheme.TextSecondary, FontWeights.Normal);
            _lunarText.Margin = new Thickness(0, 3, 0, 0);
            root.Children.Add(_lunarText);

            _weatherText = UiTheme.MakeText("", 12.5, UiTheme.Accent, FontWeights.Normal);
            _weatherText.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(_weatherText);

            _modeText = UiTheme.MakeText("", 11, UiTheme.TextMuted, FontWeights.Normal);
            _modeText.Margin = new Thickness(0, 3, 0, 0);
            root.Children.Add(_modeText);

            _agendaSeparator = new Border();
            _agendaSeparator.Height = 1;
            _agendaSeparator.Background = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x40));
            _agendaSeparator.Margin = new Thickness(0, 14, 0, 12);
            root.Children.Add(_agendaSeparator);

            _agendaPanel = new StackPanel();
            root.Children.Add(_agendaPanel);

            _emptyText = UiTheme.MakeText("今天没有启用的提醒或日程。", 12, UiTheme.TextMuted, FontWeights.Normal);
            _emptyText.Margin = new Thickness(0, 2, 0, 2);

            TextBlock hint = UiTheme.MakeText("左键展开 / 右键菜单", 10.5, UiTheme.TextMuted, FontWeights.Normal);
            hint.VerticalAlignment = VerticalAlignment.Center;

            Button settingsButton = new Button();
            settingsButton.Content = "打开设置";
            settingsButton.Style = UiTheme.GhostButtonStyle;
            settingsButton.FontSize = 11.5;
            settingsButton.Padding = new Thickness(10, 3, 10, 3);
            settingsButton.HorizontalAlignment = HorizontalAlignment.Right;
            settingsButton.VerticalAlignment = VerticalAlignment.Center;
            settingsButton.Click += delegate
            {
                Hide();
                EventHandler handler = SettingsRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            Grid footerRow = new Grid();
            footerRow.Margin = new Thickness(0, 14, 0, 0);
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerRow.Children.Add(hint);
            Grid.SetColumn(hint, 0);
            footerRow.Children.Add(settingsButton);
            Grid.SetColumn(settingsButton, 1);
            root.Children.Add(footerRow);

            card.Child = root;
            Content = card;
        }

        public event EventHandler SettingsRequested;

        /// <summary>
        /// 刷新面板内容。时间每秒更新，提醒/日程列表只在内容变化时重建。
        /// </summary>
        public void SetModel(TaskbarPanelModel model)
        {
            if (model == null)
            {
                return;
            }

            _timeText.Text = model.TimeText;
            _dateText.Text = model.DateText;
            _lunarText.Text = model.LunarText;
            _modeText.Text = model.ModeText;

            string weather = model.WeatherText ?? string.Empty;
            bool weatherChanged = !string.Equals(weather, _lastWeatherText, StringComparison.Ordinal);
            _weatherText.Text = weather;
            _weatherText.Visibility = weather.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            string signature = model.Signature;
            bool agendaChanged = !string.Equals(signature, _lastSignature, StringComparison.Ordinal);
            if (agendaChanged)
            {
                _lastSignature = signature;
                RebuildAgenda(model);
            }

            if ((agendaChanged || weatherChanged || _lastWeatherText == null) && _hasAnchor && IsVisible)
            {
                UpdateLayout();
                Reposition();
            }
            _lastWeatherText = weather;
        }

        /// <summary>
        /// 把面板放到任务栏时钟上方（任务栏在顶部时放到下方）。
        /// </summary>
        public void ShowNear(NativeMethods.RECT clockRect, double dpiScale)
        {
            _anchorRect = clockRect;
            _anchorDpi = dpiScale > 0.1 ? dpiScale : 1.0;
            _hasAnchor = true;

            if (!_positioned)
            {
                Show();
                UpdateLayout();
                _positioned = true;
            }
            else if (!IsVisible)
            {
                Show();
                UpdateLayout();
            }

            Reposition();
            NativeMethods.SetTopmost(new WindowInteropHelper(this).Handle, true);
        }

        private void Reposition()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !_hasAnchor)
            {
                return;
            }

            int width = (int)Math.Round(ActualWidth * _anchorDpi);
            int height = (int)Math.Round(ActualHeight * _anchorDpi);
            int left = _anchorRect.Right - width;
            int top = _anchorRect.Top - height - 8;
            if (top < 8)
            {
                top = _anchorRect.Bottom + 8;
            }
            if (left < 8)
            {
                left = 8;
            }

            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                left,
                top,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        private void RebuildAgenda(TaskbarPanelModel model)
        {
            _agendaPanel.Children.Clear();

            List<TaskbarAgendaLine> lines = model.Agenda;
            if (lines == null || lines.Count == 0)
            {
                _agendaPanel.Children.Add(_emptyText);
                return;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                _agendaPanel.Children.Add(BuildAgendaRow(lines[i]));
            }
        }

        private UIElement BuildAgendaRow(TaskbarAgendaLine line)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 0, 0, 6);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock time = UiTheme.MakeText(line.Time, 12, UiTheme.Accent, FontWeights.SemiBold);
            time.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(time);
            Grid.SetColumn(time, 0);

            TextBlock text = UiTheme.MakeText(line.Text, 12, UiTheme.TextPrimary, FontWeights.Normal);
            text.VerticalAlignment = VerticalAlignment.Top;
            if (line.Dimmed)
            {
                text.Foreground = UiTheme.TextMuted;
                time.Foreground = UiTheme.TextMuted;
            }
            row.Children.Add(text);
            Grid.SetColumn(text, 1);
            return row;
        }
    }

    /// <summary>今日面板的一行提醒/日程。</summary>
    internal sealed class TaskbarAgendaLine
    {
        public TaskbarAgendaLine(string time, string text, bool dimmed)
        {
            Time = time;
            Text = text;
            Dimmed = dimmed;
        }

        public string Time { get; private set; }
        public string Text { get; private set; }
        public bool Dimmed { get; private set; }
    }

    /// <summary>今日面板的全部显示数据。</summary>
    internal sealed class TaskbarPanelModel
    {
        public string TimeText { get; set; }
        public string DateText { get; set; }
        public string LunarText { get; set; }
        public string WeatherText { get; set; }
        public string ModeText { get; set; }
        public string Signature { get; set; }
        public List<TaskbarAgendaLine> Agenda { get; set; }
    }
}
