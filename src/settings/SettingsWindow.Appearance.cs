// 设置窗口「外观」页签分部（partial SettingsWindow）：桌面悬浮窗开关、字体/字号/颜色、效果与窗口行为。
// 调用方：SettingsWindow.Shell 的 BuildWindowContent 调 BuildAppearanceTab 构建本页，AttachEvents 把控件接到本文件的 On* 处理器。
// 边界：只构造控件并把用户选择写回 _working 编辑副本；不落盘、不创建/销毁窗口、不读写注册表。
// 预览由 Shell 的 UpdatePreview 用 _working 统一渲染；应用/保存链路的校验与日志由 SettingsWindow.Shell.CommitAll 负责。
// 已知坑：所有处理器必须先判 _updating，否则 LoadControlsFromWorking 的回填会被当成用户修改并标脏。

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
    // 本分部文件：外观页签的控件构造与事件处理；字段与公共骨架在 SettingsWindow.cs，应用/保存链路在 SettingsWindow.Shell.cs。
    internal sealed partial class SettingsWindow
    {
        /// <summary>
        /// 构造「外观」页签根面板：显示位置 → 字体与颜色 → 悬浮窗格式 → 效果 → 窗口行为。
        /// </summary>
        private UIElement BuildAppearanceTab()
        {
            // 纵向 StackPanel 的添加顺序即页面从上到下的卡片顺序；滚动由外层页签容器负责，这里不要再套 ScrollViewer。
            StackPanel panel = new StackPanel();

            // 卡片一：显示位置（桌面悬浮窗）+ 外观参数；关闭悬浮窗不影响任务栏时钟。
            // 本页所有控件都只改「未应用」的编辑副本，点「应用」后才由 Shell 提交并落盘。
            // 字体/字号/颜色写的是悬浮窗字段，任务栏用各自的 Taskbar* 字段，两处互不影响。
            Border overlayCard = UiTheme.MakeCard();
            StackPanel overlayPanel = new StackPanel();
            overlayPanel.Children.Add(UiTheme.MakeSectionTitle("桌面悬浮窗"));
            // 该复选框与任务栏页的「任务栏时钟」复选框同属显示位置开关，两者互相独立，可同时关闭。
            overlayPanel.Children.Add(_desktopOverlayCheck);
            TextBlock overlayHint = SettingsUiHints.Hint(
                "可拖动并支持右键菜单；关闭后桌面不再显示悬浮窗，任务栏时钟不受影响。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            overlayHint.TextWrapping = TextWrapping.Wrap;
            overlayHint.Margin = new Thickness(24, 3, 0, 0);
            overlayPanel.Children.Add(overlayHint);
            overlayCard.Child = overlayPanel;
            panel.Children.Add(overlayCard);

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

            panel.Children.Add(BuildOverlayFormatSection());

            Border effectCard = UiTheme.MakeCard();
            StackPanel effectPanel = new StackPanel();
            effectPanel.Children.Add(UiTheme.MakeSectionTitle("效果"));
            effectPanel.Children.Add(BuildSliderBlock("不透明度", _opacitySlider, _opacityValueText, "整个悬浮窗的不透明度，20% 到 100%", new Thickness(0, 0, 0, 4)));
            effectPanel.Children.Add(_shadowCheck);
            effectPanel.Children.Add(_threeDCheck);
            TextBlock effectHint = SettingsUiHints.Hint(
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
            windowPanel.Children.Add(_hideFullscreenCheck);
            TextBlock windowHint = SettingsUiHints.Hint(
                "穿透开启后，时钟上的右键菜单不可用，请右键系统托盘图标恢复；全屏隐藏会在检测到全屏游戏或视频时暂时隐藏悬浮窗。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            windowHint.Margin = new Thickness(24, 3, 0, 0);
            windowPanel.Children.Add(windowHint);
            windowCard.Child = windowPanel;
            panel.Children.Add(windowCard);

            return panel;
        }

        /// <summary>
        /// 字体下拉框变化：把选中的字体名写回编辑副本并刷新预览。
        /// </summary>
        private void OnFontChanged(object sender, SelectionChangedEventArgs e)
        {
            // LoadControlsFromWorking 回填也会触发 SelectionChanged；_updating 期间必须忽略，
            // 否则会把回填值当成用户修改并标脏。
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

        /// <summary>
        /// 字号滑块变化：滑块值本身就是像素，直接写入 FontSize（DPI 缩放在渲染层处理）。
        /// </summary>
        private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            _working.FontSize = e.NewValue;
            // 显示值取整：滑块步长为 1，但 double 值可能带小数尾巴。
            _sizeValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + " px";
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 不透明度滑块变化：界面是 20–100 的百分数，AppSettings.Opacity 存 0.2–1.0，此处负责换算。
        /// </summary>
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

        /// <summary>
        /// 阴影/3D 复选框变化：两个开关一并写回（同一个处理器同时服务两个 CheckBox，无法判断是哪一个）。
        /// </summary>
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

        /// <summary>
        /// 窗口行为复选框变化：穿透/置顶/开机自启/全屏隐藏一次性写回编辑副本。
        /// </summary>
        private void OnBehaviorChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.ClickThrough = _clickThroughCheck.IsChecked == true;
            _working.Topmost = _topmostCheck.IsChecked == true;
            _working.StartWithWindows = _startWithWindowsCheck.IsChecked == true;
            _working.HideOnFullscreen = _hideFullscreenCheck.IsChecked == true;
            // 这四项都要点「应用」后才落地（开机自启还会写注册表），所以此处只记录用户的选择结果，不记副作用。
            AppLog.Info(
                "Settings",
                "窗口行为选择：穿透=" + (_working.ClickThrough ? "开" : "关")
                    + "，置顶=" + (_working.Topmost ? "开" : "关")
                    + "，开机自启=" + (_working.StartWithWindows ? "开" : "关")
                    + "，全屏隐藏=" + (_working.HideOnFullscreen ? "开" : "关"));
            MarkDirty();
        }

        /// <summary>
        /// 显示位置复选框变化：桌面悬浮窗与任务栏时钟各自独立，允许同时关闭（此时两个位置都不显示）。
        /// </summary>
        private void OnDisplaySurfaceChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            _working.ShowDesktopOverlay = _desktopOverlayCheck.IsChecked == true;
            _working.ShowTaskbarClock = _taskbarClockCheck.IsChecked == true;
            // 窗口的创建/销毁统一由 AppController.ApplyDisplaySurfaces 在应用后执行，这里不改窗口，只记选择。
            AppLog.Info(
                "Settings",
                "显示位置选择：桌面悬浮窗=" + (_working.ShowDesktopOverlay ? "开" : "关")
                    + "，任务栏时钟=" + (_working.ShowTaskbarClock ? "开" : "关"));
            MarkDirty();
        }

        /// <summary>
        /// 当前工作/娱乐状态切换：只影响提醒的过滤，不影响时间显示与日程。
        /// </summary>
        private void OnCurrentStateChanged(object sender, RoutedEventArgs e)
        {
            // 「当前状态」是提醒/日程的分组维度，不属于外观参数，因此不在这里改任何显示字段。
            // 两个单选由 Shell 接到同一个处理器，切换后只刷新预览与脏标记，不触发取数。
            if (_updating)
            {
                return;
            }
            _working.CurrentMode = _workModeRadio.IsChecked == true
                ? ReminderModes.Work
                : ReminderModes.Leisure;
            MarkDirty();
        }

        /// <summary>
        /// 打开系统取色对话框选择时间颜色；确认后交给 <see cref="SelectColor"/> 落到编辑副本。
        /// </summary>
        private void OnPickColor(object sender, RoutedEventArgs e)
        {
            // WinForms 的 ColorDialog 不是 WPF 控件，必须显式传 owner 句柄，否则在置顶的无边框窗口下可能被压到后面。
            using (WinForms.ColorDialog dialog = new WinForms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                Color current = _working.TimeColor;
                // System.Drawing.Color 与 WPF Color 是两套类型，只能按 RGB 三元组互相转换，不能直接赋值。
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

                // 窗口未显示时该句柄是 IntPtr.Zero，ColorDialog 会退化成无 owner 的模态框，属于可接受的降级。
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WinForms.IWin32Window owner = new SimpleWin32Window(handle);
                // 取消不改任何字段：避免把「点了取消」记成一次颜色变更并标脏。
                if (dialog.ShowDialog(owner) == WinForms.DialogResult.OK)
                {
                    SelectColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
                }
            }
        }

        /// <summary>
        /// 把颜色写进编辑副本，并同步色块、预览与脏标记；取色盘与系统对话框共用这一个落点。
        /// </summary>
        private void SelectColor(Color color)
        {
            _working.ColorHex = ColorUtil.ToHex(color);
            // 颜色是用户可见决策，记一条即可；此处已是最终落点，取色盘与 ColorDialog 不会重复记录。
            AppLog.Info("Settings", "悬浮窗时间颜色改为 " + _working.ColorHex + "。");
            UpdateColorUi();
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 把当前颜色刷到色块与十六进制文本，并在取色盘上高亮选中的预设。
        /// </summary>
        private void UpdateColorUi()
        {
            // 选中态靠边框粗细区分（3px 主文字色 / 1px #555560），不改变色块本身的颜色。
            Color color = _working.TimeColor;
            _colorSwatch.Background = UiTheme.BrushFrom(color);
            _colorHexText.Text = ColorUtil.ToHex(color);

            // 逐个比对十六进制串而不是 Color 结构：预设色与用户手选色只要文本一致就算同一色，忽略大小写差异。
            for (int i = 0; i < _presets.Count; i++)
            {
                ColorPreset preset = _presets[i];
                bool selected = string.Equals(
                    ColorUtil.ToHex(preset.Color),
                    _working.ColorHex,
                    StringComparison.OrdinalIgnoreCase);
                // 选中用 3px 亮边框，未选中 1px 暗边框（#555560），靠粗细与亮度双重区分，避免色盲用户只靠颜色判断。
                preset.Swatch.BorderThickness = new Thickness(selected ? 3 : 1);
                preset.Swatch.BorderBrush = selected
                    ? UiTheme.TextPrimary
                    : UiTheme.Frozen(0x55, 0x55, 0x60);
            }
        }
    }
}
