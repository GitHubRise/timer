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
        private UIElement BuildAppearanceTab()
        {
            StackPanel panel = new StackPanel();

            Border displayCard = UiTheme.MakeCard();
            StackPanel displayPanel = new StackPanel();
            displayPanel.Children.Add(UiTheme.MakeSectionTitle("显示位置"));
            displayPanel.Children.Add(_desktopOverlayCheck);
            displayPanel.Children.Add(_taskbarClockCheck);
            TextBlock displayHint = UiTheme.MakeText(
                "桌面悬浮窗可拖动并支持右键菜单；任务栏时钟固定显示在任务栏时间区，开启时会暂时隐藏系统自带时间。两者至少保留一个。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            displayHint.TextWrapping = TextWrapping.Wrap;
            displayHint.Margin = new Thickness(24, 3, 0, 0);
            displayPanel.Children.Add(displayHint);
            displayCard.Child = displayPanel;
            panel.Children.Add(displayCard);

            Border fontCard = UiTheme.MakeCard();
            StackPanel fontPanel = new StackPanel();
            fontPanel.Children.Add(UiTheme.MakeSectionTitle("字体与颜色"));

            TextBlock fontLabel = UiTheme.MakeText("字体", 13, UiTheme.TextPrimary, FontWeights.Normal);
            fontLabel.Margin = new Thickness(0, 0, 0, 6);
            fontPanel.Children.Add(fontLabel);
            fontPanel.Children.Add(_fontCombo);
            fontPanel.Children.Add(BuildSliderBlock("字号", _sizeSlider, _sizeValueText, "文字大小，16 到 160 像素", new Thickness(0, 14, 0, 2)));
            fontPanel.Children.Add(BuildColorRow());
            fontPanel.Children.Add(BuildPalette());
            fontCard.Child = fontPanel;
            panel.Children.Add(fontCard);

            Border effectCard = UiTheme.MakeCard();
            StackPanel effectPanel = new StackPanel();
            effectPanel.Children.Add(UiTheme.MakeSectionTitle("效果"));
            effectPanel.Children.Add(BuildSliderBlock("不透明度", _opacitySlider, _opacityValueText, "整个悬浮窗的不透明度，20% 到 100%", new Thickness(0, 0, 0, 4)));
            effectPanel.Children.Add(_shadowCheck);
            effectPanel.Children.Add(_threeDCheck);
            TextBlock effectHint = UiTheme.MakeText(
                "阴影提升可读性；3D 立体字会为文字增加向下偏移的厚度。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            effectHint.Margin = new Thickness(24, 2, 0, 0);
            effectPanel.Children.Add(effectHint);
            effectCard.Child = effectPanel;
            panel.Children.Add(effectCard);

            Border windowCard = UiTheme.MakeCard();
            StackPanel windowPanel = new StackPanel();
            windowPanel.Children.Add(UiTheme.MakeSectionTitle("窗口行为"));
            windowPanel.Children.Add(_clickThroughCheck);
            windowPanel.Children.Add(_topmostCheck);
            windowPanel.Children.Add(_startWithWindowsCheck);
            TextBlock windowHint = UiTheme.MakeText(
                "穿透开启后，时钟上的右键菜单不可用，请右键系统托盘图标恢复。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            windowHint.Margin = new Thickness(24, 3, 0, 0);
            windowPanel.Children.Add(windowHint);
            windowCard.Child = windowPanel;
            panel.Children.Add(windowCard);

            return panel;
        }

        private void OnFontChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            FontChoice choice = _fontCombo.SelectedItem as FontChoice;
            if (choice != null)
            {
                _working.FontFamilyName = choice.Name;
                UpdatePreview();
                MarkDirty();
            }
        }

        private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            _working.FontSize = e.NewValue;
            _sizeValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + " px";
            UpdatePreview();
            MarkDirty();
        }

        private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            _working.Opacity = e.NewValue / 100.0;
            _opacityValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + "%";
            UpdatePreview();
            MarkDirty();
        }

        private void OnEffectChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.ShowShadow = _shadowCheck.IsChecked == true;
            _working.Text3D = _threeDCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        private void OnBehaviorChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.ClickThrough = _clickThroughCheck.IsChecked == true;
            _working.Topmost = _topmostCheck.IsChecked == true;
            _working.StartWithWindows = _startWithWindowsCheck.IsChecked == true;
            MarkDirty();
        }

        private void OnDisplaySurfaceChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            bool desktop = _desktopOverlayCheck.IsChecked == true;
            bool taskbar = _taskbarClockCheck.IsChecked == true;
            if (!desktop && !taskbar)
            {
                // Keep at least one surface visible; the second event writes the flags.
                _desktopOverlayCheck.IsChecked = true;
                return;
            }

            _working.ShowDesktopOverlay = desktop;
            _working.ShowTaskbarClock = taskbar;
            MarkDirty();
        }

        private void OnCurrentStateChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.CurrentMode = _workModeRadio.IsChecked == true
                ? ReminderModes.Work
                : ReminderModes.Leisure;
            MarkDirty();
        }

        private void OnPickColor(object sender, RoutedEventArgs e)
        {
            using (WinForms.ColorDialog dialog = new WinForms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                Color current = _working.TimeColor;
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WinForms.IWin32Window owner = new SimpleWin32Window(handle);
                if (dialog.ShowDialog(owner) == WinForms.DialogResult.OK)
                {
                    SelectColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
                }
            }
        }

        private void SelectColor(Color color)
        {
            _working.ColorHex = ColorUtil.ToHex(color);
            UpdateColorUi();
            UpdatePreview();
            MarkDirty();
        }

        private void UpdateColorUi()
        {
            Color color = _working.TimeColor;
            _colorSwatch.Background = UiTheme.BrushFrom(color);
            _colorHexText.Text = ColorUtil.ToHex(color);

            for (int i = 0; i < _presets.Count; i++)
            {
                ColorPreset preset = _presets[i];
                bool selected = string.Equals(
                    ColorUtil.ToHex(preset.Color),
                    _working.ColorHex,
                    StringComparison.OrdinalIgnoreCase);
                preset.Swatch.BorderThickness = new Thickness(selected ? 3 : 1);
                preset.Swatch.BorderBrush = selected
                    ? UiTheme.TextPrimary
                    : UiTheme.Frozen(0x55, 0x55, 0x60);
            }
        }
    }
}
