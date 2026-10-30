using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GameClock
{
    internal sealed class ScheduleEditorWindow : Window
    {
        private readonly ScheduleItem _existing;
        private readonly List<ScheduleCategoryOption> _types;
        private readonly List<ScheduleAdvanceOption> _advances;
        private readonly ComboBox _typeCombo;
        private readonly TextBox _titleBox;
        private readonly TextBox _dateBox;
        private readonly TextBox _timeBox;
        private readonly ComboBox _advanceCombo;
        private readonly CheckBox _annualCheck;
        private readonly TextBox _noteBox;
        private readonly TextBlock _status;
        private readonly TextBlock _headerTitle;
        private bool _updating;

        public ScheduleEditorWindow(ScheduleItem existing)
        {
            _existing = existing != null ? existing.Clone() : null;
            _types = ScheduleCategories.Create();
            _advances = ScheduleAdvanceOptions.Create();

            Title = existing == null ? "添加日程" : "编辑日程";
            Width = 540;
            Height = 620;
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
            Resources.Add(typeof(ScrollBar), ModernScrollBar.CreateStyle());

            _typeCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_typeCombo);
            _typeCombo.ItemsSource = _types;
            _typeCombo.DisplayMemberPath = "Label";
            _typeCombo.SelectedValuePath = "Value";
            _typeCombo.SelectedValue = ScheduleCategories.Meeting;
            _typeCombo.Height = 32;
            AutomationProperties.SetAutomationId(_typeCombo, "ScheduleDialogType");
            _typeCombo.SelectionChanged += OnTypeChanged;

            _titleBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_titleBox);
            _titleBox.MaxLength = 80;
            _titleBox.Height = 32;
            AutomationProperties.SetAutomationId(_titleBox, "ScheduleDialogTitle");

            _dateBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_dateBox);
            _dateBox.Text = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _dateBox.MaxLength = 10;
            _dateBox.Height = 32;
            _dateBox.FontFamily = new FontFamily("Consolas");
            AutomationProperties.SetAutomationId(_dateBox, "ScheduleDialogDate");

            _timeBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_timeBox);
            _timeBox.Text = "09:00";
            _timeBox.MaxLength = 5;
            _timeBox.Height = 32;
            _timeBox.FontFamily = new FontFamily("Consolas");
            AutomationProperties.SetAutomationId(_timeBox, "ScheduleDialogTime");

            _advanceCombo = new ComboBox();
            UiTheme.ApplyComboBoxStyle(_advanceCombo);
            _advanceCombo.ItemsSource = _advances;
            _advanceCombo.DisplayMemberPath = "Label";
            _advanceCombo.SelectedValuePath = "Minutes";
            _advanceCombo.SelectedValue = 15;
            _advanceCombo.Height = 32;
            AutomationProperties.SetAutomationId(_advanceCombo, "ScheduleDialogAdvance");

            _annualCheck = new CheckBox();
            _annualCheck.Content = "每年重复（生日、纪念日）";
            _annualCheck.Foreground = UiTheme.TextPrimary;
            _annualCheck.FontSize = 12.5;
            _annualCheck.VerticalContentAlignment = VerticalAlignment.Center;
            _annualCheck.Margin = new Thickness(0, 10, 0, 0);
            AutomationProperties.SetAutomationId(_annualCheck, "ScheduleDialogAnnual");

            _noteBox = new TextBox();
            UiTheme.ApplyTextBoxStyle(_noteBox);
            _noteBox.MaxLength = 240;
            _noteBox.Height = 76;
            _noteBox.AcceptsReturn = true;
            _noteBox.TextWrapping = TextWrapping.Wrap;
            _noteBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            AutomationProperties.SetAutomationId(_noteBox, "ScheduleDialogNote");

            _status = UiTheme.MakeText("", 11, UiTheme.Accent, FontWeights.Normal);
            _status.Margin = new Thickness(0, 8, 0, 0);
            _status.TextWrapping = TextWrapping.Wrap;

            _headerTitle = UiTheme.MakeText(
                existing == null ? "添加日程" : "编辑日程",
                15,
                UiTheme.TextPrimary,
                FontWeights.SemiBold);
            _headerTitle.VerticalAlignment = VerticalAlignment.Center;

            Content = BuildContent();

            _updating = true;
            try
            {
                if (_existing != null)
                {
                    _typeCombo.SelectedValue = ScheduleCategories.Normalize(_existing.Type);
                    _titleBox.Text = _existing.Title;
                    DateTime date;
                    _dateBox.Text = ScheduleService.TryParseDate(_existing.Date, out date)
                        ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    _timeBox.Text = _existing.Time;
                    _advanceCombo.SelectedValue = ScheduleAdvanceOptions.Normalize(_existing.AdvanceMinutes);
                    _annualCheck.IsChecked = _existing.Annual;
                    _noteBox.Text = _existing.Note;
                }
                else
                {
                    _annualCheck.IsChecked = false;
                }
            }
            finally
            {
                _updating = false;
            }

            Loaded += delegate
            {
                Activate();
                _titleBox.Focus();
                Keyboard.Focus(_titleBox);
                _titleBox.CaretIndex = _titleBox.Text.Length;
            };
            KeyDown += OnWindowKeyDown;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        public ScheduleItem Result { get; private set; }

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
            header.Padding = new Thickness(22, 12, 14, 12);
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
            headerGrid.Children.Add(_headerTitle);

            Button close = new Button();
            close.Content = "×";
            close.Style = UiTheme.GhostButtonStyle;
            close.Width = 30;
            close.Height = 30;
            close.Padding = new Thickness(0);
            close.FontSize = 18;
            close.ToolTip = "关闭";
            close.Click += delegate { DialogResult = false; };
            AutomationProperties.SetAutomationId(close, "ScheduleDialogClose");
            headerGrid.Children.Add(close);
            Grid.SetColumn(close, 1);
            header.Child = headerGrid;
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Padding = new Thickness(22, 18, 22, 12);

            Border card = UiTheme.MakeCard();
            card.Margin = new Thickness(0);
            StackPanel form = new StackPanel();
            form.Children.Add(UiTheme.MakeSectionTitle("日程信息"));

            Grid rowOne = new Grid();
            rowOne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(122) });
            rowOne.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowOne.Children.Add(BuildLabeledField("类型", _typeCombo, new Thickness(0)));
            StackPanel titleField = BuildLabeledField("标题", _titleBox, new Thickness(10, 0, 0, 0));
            rowOne.Children.Add(titleField);
            Grid.SetColumn(titleField, 1);
            form.Children.Add(rowOne);

            Grid rowTwo = new Grid();
            rowTwo.Margin = new Thickness(0, 12, 0, 0);
            rowTwo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(152) });
            rowTwo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            rowTwo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowTwo.Children.Add(BuildLabeledField("日期 yyyy-MM-dd", _dateBox, new Thickness(0)));
            StackPanel timeField = BuildLabeledField("时间 HH:mm", _timeBox, new Thickness(10, 0, 0, 0));
            rowTwo.Children.Add(timeField);
            Grid.SetColumn(timeField, 1);
            StackPanel advanceField = BuildLabeledField("提前提醒", _advanceCombo, new Thickness(10, 0, 0, 0));
            rowTwo.Children.Add(advanceField);
            Grid.SetColumn(advanceField, 2);
            form.Children.Add(rowTwo);

            form.Children.Add(_annualCheck);

            StackPanel noteField = BuildLabeledField("备注（可选）", _noteBox, new Thickness(0, 12, 0, 0));
            form.Children.Add(noteField);
            form.Children.Add(_status);

            card.Child = form;
            scroll.Content = card;
            root.Children.Add(scroll);
            Grid.SetRow(scroll, 1);

            Border footer = new Border();
            footer.Background = UiTheme.Frozen(0x17, 0x17, 0x1C);
            footer.BorderBrush = UiTheme.Frozen(0x2C, 0x2C, 0x34);
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(22, 12, 22, 12);
            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;

            Button cancel = new Button();
            cancel.Content = "取消";
            cancel.Style = UiTheme.SecondaryButtonStyle;
            cancel.Height = 34;
            cancel.MinWidth = 82;
            cancel.Click += delegate { DialogResult = false; };
            AutomationProperties.SetAutomationId(cancel, "ScheduleDialogCancel");
            buttons.Children.Add(cancel);

            Button save = new Button();
            save.Content = _existing == null ? "添加" : "保存";
            save.Style = UiTheme.PrimaryButtonStyle;
            save.Height = 34;
            save.MinWidth = 90;
            save.Margin = new Thickness(8, 0, 0, 0);
            save.IsDefault = true;
            save.Click += OnSave;
            AutomationProperties.SetAutomationId(save, "ScheduleDialogSave");
            buttons.Children.Add(save);

            footer.Child = buttons;
            root.Children.Add(footer);
            Grid.SetRow(footer, 2);

            return root;
        }

        private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            ScheduleCategoryOption option = _typeCombo.SelectedItem as ScheduleCategoryOption;
            if (option != null)
            {
                _annualCheck.IsChecked = ScheduleCategories.IsAnnual(option.Value);
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            string title = (_titleBox.Text ?? string.Empty).Trim();
            if (title.Length == 0)
            {
                _status.Text = "请填写日程标题。";
                _titleBox.Focus();
                return;
            }

            DateTime date;
            if (!ScheduleService.TryParseDate(_dateBox.Text, out date))
            {
                _status.Text = "日期格式应为 yyyy-MM-dd，例如 2026-10-30。";
                _dateBox.Focus();
                return;
            }

            string time;
            if (!TimeFormatter.TryNormalizeTime(_timeBox.Text, out time))
            {
                _status.Text = "时间格式应为 HH:mm，例如 09:30。";
                _timeBox.Focus();
                return;
            }

            ScheduleCategoryOption type = _typeCombo.SelectedItem as ScheduleCategoryOption;
            ScheduleAdvanceOption advance = _advanceCombo.SelectedItem as ScheduleAdvanceOption;
            if (type == null || advance == null)
            {
                _status.Text = "请选择日程类型和提前提醒时间。";
                return;
            }

            ScheduleItem result = _existing != null ? _existing.Clone() : new ScheduleItem();
            result.Type = type.Value;
            result.Title = title;
            result.Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            result.Time = time;
            result.AdvanceMinutes = advance.Minutes;
            result.Annual = _annualCheck.IsChecked == true;
            result.Note = (_noteBox.Text ?? string.Empty).Trim();
            result.Enabled = _existing != null ? _existing.Enabled : true;
            result.LastTriggeredKey = "";
            if (!result.Normalize())
            {
                _status.Text = "日程内容不完整，请检查后重试。";
                return;
            }

            Result = result;
            DialogResult = true;
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                OnSave(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }
            if (Keyboard.FocusedElement == _noteBox)
            {
                return;
            }
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
            {
                return;
            }

            OnSave(sender, new RoutedEventArgs());
            e.Handled = true;
        }

        private static StackPanel BuildLabeledField(string labelText, FrameworkElement control, Thickness margin)
        {
            StackPanel panel = new StackPanel();
            panel.Margin = margin;
            TextBlock label = UiTheme.MakeText(labelText, 10.5, UiTheme.TextSecondary, FontWeights.Normal);
            label.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(label);
            panel.Children.Add(control);
            return panel;
        }
    }
}
