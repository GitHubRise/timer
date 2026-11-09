// 设置窗口 · 桌面悬浮窗外观页签。
// 负责桌面悬浮窗总开关、字体/字号/颜色、不透明度、阴影与 3D 立体字，
// 以及穿透、置顶、开机自启、全屏隐藏等窗口行为。
// 对应 AppSettings 的 ShowDesktopOverlay、FontFamilyName、FontSize、ColorHex、
// Opacity、ShowShadow、Text3D、ClickThrough、Topmost、StartWithWindows、HideOnFullscreen。
// 约定：本页灰色说明文字统一由 SettingsUiHints.Hint 创建，保证提示总开关能一并隐藏。

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

    /// <summary>设置窗口的桌面悬浮窗外观页签：构建布局并把控件变化写回编辑副本。</summary>
    internal sealed partial class SettingsWindow
    {

        // ===== 布局构建 =====

        /// <summary>
        /// 构建「桌面悬浮窗」页签：按总开关、字体与颜色、效果、窗口行为四组卡片纵向排列。
        /// 控件只改编辑副本 <c>_working</c>，真正落盘由主窗口的「应用」统一提交。
        /// </summary>
        private UIElement BuildAppearanceTab()
        {
            // 整页用纵向 StackPanel 承载，卡片之间靠外边距自然分隔。
            StackPanel panel = new StackPanel();

            // 第一张卡片：桌面悬浮窗总开关；关闭只影响桌面悬浮窗，任务栏时钟照常显示。
            Border overlayCard = UiTheme.MakeCard();
            StackPanel overlayPanel = new StackPanel();
            overlayPanel.Children.Add(UiTheme.MakeSectionTitle("桌面悬浮窗"));
            overlayPanel.Children.Add(_desktopOverlayCheck);
            // 灰色提示统一走工厂，关掉 SettingsUiHints.ShowHints 时整块折叠，不占布局。
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

            // 第二张卡片：字体、字号与时间颜色，只作用于桌面悬浮窗。
            Border fontCard = UiTheme.MakeCard();
            StackPanel fontPanel = new StackPanel();
            fontPanel.Children.Add(UiTheme.MakeSectionTitle("字体与颜色"));

            TextBlock fontLabel = UiTheme.MakeText("字体", 13, UiTheme.TextPrimary, FontWeights.Normal);
            fontLabel.Margin = new Thickness(0, 0, 0, 6);
            fontPanel.Children.Add(fontLabel);
            fontPanel.Children.Add(_fontCombo);
            // 字号与颜色复用通用构建方法，数值文案和选中描边分别由各自的事件处理器同步。
            fontPanel.Children.Add(BuildSliderBlock("字号", _sizeSlider, _sizeValueText, "文字大小，16 到 160 像素", new Thickness(0, 14, 0, 2)));
            fontPanel.Children.Add(BuildColorRow());
            fontPanel.Children.Add(BuildPalette());
            fontCard.Child = fontPanel;
            panel.Children.Add(fontCard);

            // 格式语言卡片由 Format 分部类构建，外观页只负责把它插在字体与效果之间。
            panel.Children.Add(BuildOverlayFormatSection());

            // 第三张卡片：不透明度、阴影与 3D 立体字，都属于纯视觉选项。
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

            // 第四张卡片：穿透、置顶、开机自启与全屏隐藏，四项互不冲突，可同时开启。
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

            // 返回整页根元素，由主文件挂到页签容器里。
            return panel;
        }

        // ===== 事件处理 =====

        /// <summary>
        /// 字体选择变化：把字体名写回编辑副本并立即刷新预览；回填期间 <c>_updating</c> 为 true，直接忽略。
        /// </summary>
        private void OnFontChanged(object sender, SelectionChangedEventArgs e)
        {
            // 回填字体下拉时会再次触发 SelectionChanged，靠标志位挡住，避免误标未保存。
            if (_updating)
            {
                return;
            }
            FontChoice choice = _fontCombo.SelectedItem as FontChoice;
            // 未选中具体字体时（例如下拉列表刚被清空）保持原值，避免把空字符串写进配置。
            if (choice != null)
            {
                _working.FontFamilyName = choice.Name;
                // 先重画预览，再标记有未保存改动，顺序与界面反馈一致。
                UpdatePreview();
                MarkDirty();
            }
        }

        /// <summary>
        /// 字号滑块变化：写入编辑副本，并用整数像素同步右侧数值文案，避免拖动时出现长小数。
        /// </summary>
        private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 回填控件值时也会触发 ValueChanged，这里靠 _updating 拦住，防止把回填当成用户修改。
            if (_updating)
            {
                return;
            }
            _working.FontSize = e.NewValue;
            // 界面只显示整数像素，实际字体大小仍按滑块的双精度值参与排版。
            _sizeValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + " px";
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 不透明度滑块变化：界面按 20–100 的百分比显示，配置里保存 0–1 的倍数。
        /// </summary>
        private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // 程序回填滑块时忽略本次事件，避免把初始值误标成用户修改。
            if (_updating)
            {
                return;
            }
            _working.Opacity = e.NewValue / 100.0;
            // 展示值四舍五入为整数百分比，和滑块刻度保持一致。
            _opacityValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + "%";
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 阴影/3D 开关变化：两个开关共用一个处理器，统一刷新预览，避免分别维护两套逻辑。
        /// </summary>
        private void OnEffectChanged(object sender, RoutedEventArgs e)
        {
            // 回填复选框时跳过，防止初始化阶段误触发脏标记。
            if (_updating)
            {
                return;
            }
            // 复选框是三态的，只有明确勾选才算开启。
            _working.ShowShadow = _shadowCheck.IsChecked == true;
            _working.Text3D = _threeDCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 窗口行为开关变化：四个开关一次写全，任一开关变动都以当前界面状态为准。
        /// 这些选项要重新应用后才完全生效，所以这里只改副本并标记未保存。
        /// </summary>
        private void OnBehaviorChanged(object sender, RoutedEventArgs e)
        {
            // 回填阶段忽略事件，避免打开设置窗口就出现「有未应用的修改」。
            if (_updating)
            {
                return;
            }
            _working.ClickThrough = _clickThroughCheck.IsChecked == true;
            _working.Topmost = _topmostCheck.IsChecked == true;
            _working.StartWithWindows = _startWithWindowsCheck.IsChecked == true;
            _working.HideOnFullscreen = _hideFullscreenCheck.IsChecked == true;
            MarkDirty();
        }

        /// <summary>
        /// 显示位置开关变化：桌面悬浮窗与任务栏时钟各自独立，可同时关闭；实际创建/销毁窗口由控制器负责。
        /// </summary>
        private void OnDisplaySurfaceChanged(object sender, RoutedEventArgs e)
        {
            // 回填期间直接返回，避免把初始勾选状态当成用户操作。
            if (_updating)
            {
                return;
            }

            _working.ShowDesktopOverlay = _desktopOverlayCheck.IsChecked == true;
            _working.ShowTaskbarClock = _taskbarClockCheck.IsChecked == true;
            MarkDirty();
        }

        /// <summary>
        /// 当前状态变化：工作/娱乐两个单选按钮只有选中「工作」时才写 Work，其余一律视为娱乐。
        /// </summary>
        private void OnCurrentStateChanged(object sender, RoutedEventArgs e)
        {
            // 回填单选按钮时会再次触发 Checked，靠标志位避免覆盖用户刚做的选择。
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
        /// 打开系统颜色对话框选择时间颜色；取消时保持原色不变。
        /// 用 <see cref="SimpleWin32Window"/> 包一层窗口句柄，让对话框以设置窗口为所有者，避免跑到其他窗口后面。
        /// </summary>
        private void OnPickColor(object sender, RoutedEventArgs e)
        {
            // 复用系统调色板，用户可以自由输入 RGB，比自绘取色器更省事。
            using (WinForms.ColorDialog dialog = new WinForms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                // WPF 的 Color 与 WinForms 的 Color 类型不同，这里只搬运 RGB 三个通道。
                Color current = _working.TimeColor;
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WinForms.IWin32Window owner = new SimpleWin32Window(handle);
                // 只有点「确定」才写回，取消或直接关闭都保持原色。
                if (dialog.ShowDialog(owner) == WinForms.DialogResult.OK)
                {
                    SelectColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
                }
            }
        }

        // ===== 回填与读取 =====

        /// <summary>
        /// 选中一个颜色并同步界面：配置里统一存十六进制字符串，色块、色号文本与预览都从这里刷新。
        /// </summary>
        private void SelectColor(Color color)
        {
            _working.ColorHex = ColorUtil.ToHex(color);
            // 先刷新色块/色号，再重画预览，最后标记未保存。
            UpdateColorUi();
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 按编辑副本里的颜色刷新色块、色号文本，并给预设色块加上选中描边。
        /// </summary>
        private void UpdateColorUi()
        {
            Color color = _working.TimeColor;
            _colorSwatch.Background = UiTheme.BrushFrom(color);
            _colorHexText.Text = ColorUtil.ToHex(color);

            // 预设色块可能有几十个，逐个比较十六进制字符串，命中者用粗描边标出。
            for (int i = 0; i < _presets.Count; i++)
            {
                ColorPreset preset = _presets[i];
                // 忽略大小写，避免 "#AABBCC" 与 "#aabbcc" 被当成两种颜色。
                bool selected = string.Equals(
                    ColorUtil.ToHex(preset.Color),
                    _working.ColorHex,
                    StringComparison.OrdinalIgnoreCase);
                preset.Swatch.BorderThickness = new Thickness(selected ? 3 : 1);
                // 选中用主文字色描边，未选中用低对比灰，避免一排色块看花眼。
                preset.Swatch.BorderBrush = selected
                    ? UiTheme.TextPrimary
                    : UiTheme.Frozen(0x55, 0x55, 0x60);
            }
        }
    }
}
