// 设置窗口「天气」页签：显示开关、字体字号、颜色与阴影、刷新频率、城市与格式。
// 字段对应 AppSettings.WeatherEnabled / WeatherCity / WeatherFontFamilyName /
// WeatherFontSize / WeatherColorHex / WeatherShowShadow / WeatherFontBold /
// WeatherFontItalic / WeatherRefreshMinutes / WeatherFormat，全部写入编辑副本 _working。
// 天气行显示在时间下方，外观独立于时间文字；数据来自 Open-Meteo 公共 API，无需 Key。
// 约定：灰色说明一律经 SettingsUiHints.Hint 创建，状态行改色走 SetStatusColor；
// 任务栏时钟字体与颜色跟随系统任务栏，不受本页签设置影响。

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
        // ===== 页签布局构建 =====

        /// <summary>
        /// 构建天气页签：一张主卡片容纳全部设置，灰提示关闭时再省掉「显示说明」卡片。
        /// 控件本身由主文件构造阶段创建，这里负责排列顺序与页签专属尺寸。
        /// </summary>
        /// <returns>可直接加入页签容器的根面板。</returns>
        private UIElement BuildWeatherTab()
        {
            StackPanel panel = new StackPanel();

            // 主卡片自上而下：开关、字体、粗斜体、字号、颜色、色板、阴影、刷新、城市、格式。
            Border weatherCard = UiTheme.MakeCard();
            StackPanel content = new StackPanel();
            content.Children.Add(UiTheme.MakeSectionTitle("天气预报"));
            content.Children.Add(_weatherEnabledCheck);

            TextBlock fontLabel = UiTheme.MakeText("天气字体", 13, UiTheme.TextPrimary, FontWeights.Normal);
            fontLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(fontLabel);
            content.Children.Add(_weatherFontCombo);

            // 加粗与斜体并排，共用一行，避免把页签纵向拉长。
            StackPanel styleRow = new StackPanel();
            styleRow.Orientation = Orientation.Horizontal;
            styleRow.Margin = new Thickness(0, 4, 0, 0);
            styleRow.Children.Add(_weatherBoldCheck);
            styleRow.Children.Add(_weatherItalicCheck);
            content.Children.Add(styleRow);

            // 字号用滑块加数值文本，范围与提示文案都写明 9–60 像素。
            content.Children.Add(BuildSliderBlock(
                "天气字号",
                _weatherSizeSlider,
                _weatherSizeValueText,
                "天气行单独使用的字号，9 到 60 像素",
                new Thickness(0, 12, 0, 2)));
            content.Children.Add(BuildWeatherColorRow());
            content.Children.Add(BuildWeatherPalette());
            content.Children.Add(_weatherShadowCheck);

            TextBlock refreshLabel = UiTheme.MakeText("自动更新频率", 13, UiTheme.TextPrimary, FontWeights.Normal);
            refreshLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(refreshLabel);
            // 刷新频率直接选档位，具体可选值由 WeatherRefreshOptions 收敛。
            content.Children.Add(_weatherRefreshCombo);

            TextBlock cityLabel = UiTheme.MakeText("城市", 13, UiTheme.TextPrimary, FontWeights.Normal);
            cityLabel.Margin = new Thickness(0, 12, 0, 6);
            content.Children.Add(cityLabel);

            // 城市行：输入框占满剩余宽度，「测试地名」按钮按内容取宽。
            Grid cityRow = new Grid();
            cityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cityRow.Children.Add(_weatherCityBox);
            cityRow.Children.Add(_weatherTestButton);
            Grid.SetColumn(_weatherTestButton, 1);
            content.Children.Add(cityRow);

            // 测试状态行默认是灰色引导语，查询结果由 SetStatusColor 切换到成功色或强调色。
            content.Children.Add(_weatherTestStatus);

            // 城市提示说明支持中英文，并强调保存或应用后会立即刷新。
            TextBlock cityHint = SettingsUiHints.Hint(
                "支持中文或英文城市名，例如：北京、上海、Tokyo。保存或应用后会立即刷新。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            cityHint.Margin = new Thickness(0, 6, 0, 0);
            cityHint.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(cityHint);

            TextBlock formatLabel = UiTheme.MakeText("天气格式（可选）", 13, UiTheme.TextPrimary, FontWeights.Normal);
            formatLabel.Margin = new Thickness(0, 14, 0, 6);
            content.Children.Add(formatLabel);
            content.Children.Add(_weatherFormatBox);
            // 格式状态行既显示校验错误，也显示格式化后的实时预览。
            content.Children.Add(_weatherFormatStatus);

            // 变量说明是灰提示，列出天气专用变量与几个常用示例。
            TextBlock formatHelp = SettingsUiHints.Hint(
                "可用变量：pos 城市  we1~we6 6日天气  tl1~tl6 低温  th1~th6 高温\n" +
                "rtm 实时温度  rhu 湿度  rwd 风向  icn 天气图标  aqi 空气质量  ip 公网 IP\n\n" +
                "示例：pos · we1 · rtm\n" +
                "示例：icn rtm · rhu · rwd\n" +
                "示例：pos · we1 · rtm · 今日 tl1~th1\n" +
                "留空时使用默认格式：城市 · 天气 · 温度 · 今日高低温。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            formatHelp.Margin = new Thickness(0, 8, 0, 0);
            formatHelp.TextWrapping = TextWrapping.Wrap;
            formatHelp.LineHeight = 18;
            content.Children.Add(formatHelp);

            // 数据来源说明：公共 API、默认 30 分钟、无 Key，网络不可用时隐藏天气行。
            TextBlock apiHint = SettingsUiHints.Hint(
                "天气数据来自 Open-Meteo 公共 API，按设置的频率自动更新（默认 30 分钟）；无需 API Key。网络不可用时会自动隐藏天气行。",
                11,
                UiTheme.TextMuted,
                FontWeights.Normal);
            apiHint.Margin = new Thickness(0, 10, 0, 0);
            apiHint.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(apiHint);

            weatherCard.Child = content;
            panel.Children.Add(weatherCard);

            // 「显示说明」只在灰提示打开时创建，关闭时连卡片一起省掉，不占任何高度。
            if (SettingsUiHints.ShowHints)
            {
                Border noteCard = UiTheme.MakeCard();
                StackPanel note = new StackPanel();
                note.Children.Add(UiTheme.MakeSectionTitle("显示说明"));
                TextBlock noteText = UiTheme.MakeText(
                    "天气会显示在时间下方，例如：\n北京 · 晴 · 11°C · 今日 5°~16°\n\n" +
                    "天气的字体、字号、颜色和阴影都可以独立于时间设置。\n" +
                    "只发送城市名称和坐标到公共天气服务，不包含任何个人账号信息。",
                    11.5,
                    UiTheme.TextSecondary,
                    FontWeights.Normal);
                noteText.LineHeight = 19;
                note.Children.Add(noteText);
                noteCard.Child = note;
                panel.Children.Add(noteCard);
            }

            return panel;
        }

        /// <summary>
        /// 构建「天气颜色」一行：左侧色块与十六进制值，右侧「选择…」打开系统取色器。
        /// </summary>
        /// <returns>可直接加入主卡片的行容器。</returns>
        private UIElement BuildWeatherColorRow()
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 12, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左侧色块只做展示，不接收点击，避免与右侧取色按钮职责重叠。
            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            TextBlock label = UiTheme.MakeText("天气颜色", 13, UiTheme.TextPrimary, FontWeights.Normal);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(label);
            left.Children.Add(_weatherColorSwatch);
            left.Children.Add(_weatherColorHexText);
            row.Children.Add(left);
            Grid.SetColumn(left, 0);

            Button pick = new Button();
            pick.Content = "选择…";
            pick.Style = UiTheme.SecondaryButtonStyle;
            pick.Padding = new Thickness(13, 6, 13, 6);
            pick.Height = 30;
            pick.Click += OnPickWeatherColor;
            AutomationProperties.SetAutomationId(pick, "PickWeatherColorButton");
            row.Children.Add(pick);
            Grid.SetColumn(pick, 1);

            return row;
        }

        /// <summary>
        /// 构建预设色板：九个常用颜色排成可换行的圆形色块，点击即选中。
        /// 选中描边由 <see cref="UpdateWeatherColorUi"/> 统一维护。
        /// </summary>
        /// <returns>可直接加入主卡片的色板容器。</returns>
        private UIElement BuildWeatherPalette()
        {
            WrapPanel palette = new WrapPanel();
            palette.Margin = new Thickness(0, 10, 0, 2);
            // 预设色覆盖红橙黄绿青蓝紫黑白，顺序与下面的名称数组一一对应。
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
                // 悬停提示同时给出颜色名与十六进制值，方便用户对照自定义色。
                swatch.ToolTip = names[i] + "  " + ColorUtil.ToHex(presetColor);
                // 用 delegate 而不是 lambda：C# 5 下无需额外语法，且能直接捕获当前循环变量。
                swatch.MouseLeftButtonDown += delegate
                {
                    SelectWeatherColor(presetColor);
                };
                palette.Children.Add(swatch);
                // 登记色块本身，选中状态刷新时要反过来改它的描边。
                _weatherColorPresets.Add(new ColorPreset(presetColor, swatch));
            }
            return palette;
        }

        // ===== 事件处理 =====

        /// <summary>
        /// 天气总开关变化：写回开关值、同步城市输入框可用性，并刷新预览。
        /// </summary>
        private void OnWeatherChanged(object sender, RoutedEventArgs e)
        {
            // 装载阶段 _updating 为 true，此时只回填控件，不应把中间状态写回编辑副本。
            if (_updating)
            {
                return;
            }
            _working.WeatherEnabled = _weatherEnabledCheck.IsChecked == true;
            // 关闭天气后城市框变灰，但保留已有城市名，重新打开无需重填。
            _weatherCityBox.IsEnabled = _working.WeatherEnabled;
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 按分钟数选中刷新频率档位；配置里出现非标准值时回退到默认的 30 分钟（索引 2）。
        /// </summary>
        /// <param name="minutes">要选中的间隔分钟数。</param>
        private void SelectWeatherRefreshOption(int minutes)
        {
            WeatherRefreshOption match = null;
            for (int i = 0; i < _weatherRefreshOptions.Count; i++)
            {
                if (_weatherRefreshOptions[i].Minutes == minutes)
                {
                    match = _weatherRefreshOptions[i];
                    break;
                }
            }
            // 旧配置可能存着已下线的档位，落回默认值而不是让下拉框空着。
            if (match == null)
            {
                match = _weatherRefreshOptions[2];
            }

            // 改选中项会触发 SelectionChanged，用 _updating 屏蔽回写，并在 finally 中复原。
            bool wasUpdating = _updating;
            _updating = true;
            try
            {
                _weatherRefreshCombo.SelectedItem = match;
            }
            finally
            {
                _updating = wasUpdating;
            }
        }

        /// <summary>城市输入变化：去首尾空格后写回，并立即刷新预览。</summary>
        private void OnWeatherCityChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherCity = (_weatherCityBox.Text ?? string.Empty).Trim();
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>天气字体变化：把选中项的字体名写回；空选中说明字体列表尚未就绪，直接忽略。</summary>
        private void OnWeatherFontChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            FontChoice choice = _weatherFontCombo.SelectedItem as FontChoice;
            // 取不到字体时保留旧值，避免下拉空选把已保存的字体清空。
            if (choice != null)
            {
                _working.WeatherFontFamilyName = choice.Name;
                UpdatePreview();
                MarkDirty();
            }
        }

        /// <summary>天气字号滑块变化：写回 double 值，同时把取整后的像素数显示在右侧。</summary>
        private void OnWeatherSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherFontSize = e.NewValue;
            // 显示值取整只是为了让文案稳定，配置仍保留原始 double，拖动不会累积误差。
            _weatherSizeValueText.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.InvariantCulture) + " px";
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>天气阴影开关变化：写回后重绘预览，阴影只作用于天气行，不影响时间文字。</summary>
        private void OnWeatherShadowChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherShowShadow = _weatherShadowCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>天气加粗/斜体变化：两个开关共用一个处理器，一次写回两个字重属性。</summary>
        private void OnWeatherFontStyleChanged(object sender, RoutedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            _working.WeatherFontBold = _weatherBoldCheck.IsChecked == true;
            _working.WeatherFontItalic = _weatherItalicCheck.IsChecked == true;
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 刷新频率变化：只改取数节奏，不触发 <see cref="UpdatePreview"/>，因为预览与间隔无关。
        /// </summary>
        private void OnWeatherRefreshChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }
            WeatherRefreshOption option = _weatherRefreshCombo.SelectedItem as WeatherRefreshOption;
            // 下拉正在重绑数据源时可能短暂为空，此时不写回，防止把档位清成 0。
            if (option != null)
            {
                _working.WeatherRefreshMinutes = option.Minutes;
                MarkDirty();
            }
        }

        // ===== 城市测试 =====

        /// <summary>
        /// 「测试地名」：用当前输入的城市异步查询公共天气 API，只验证地名与网络，不写入设置。
        /// 用自增序号丢弃过期回调，保证界面只反映最后一次查询。
        /// </summary>
        private void OnTestWeatherCity(object sender, RoutedEventArgs e)
        {
            string city = (_weatherCityBox.Text ?? string.Empty).Trim();
            // 空城市直接提示，不发请求，省掉一次必然失败的往返。
            if (city.Length == 0)
            {
                _weatherTestStatus.Text = "请先填写城市名。";
                SettingsUiHints.SetStatusColor(_weatherTestStatus, UiTheme.Accent);
                return;
            }

            // 序号在发请求前自增；回调时对不上就说明已有更新的请求，旧结果直接丢弃。
            int version = ++_weatherTestVersion;
            // 查询期间禁用按钮并改文案，防止连续点击把请求叠起来。
            _weatherTestButton.IsEnabled = false;
            _weatherTestButton.Content = "查询中…";
            _weatherTestStatus.Text = "正在查询 " + city + " 的天气，请稍候…";
            // 「进行中」是引导性状态，用灰色；有结果后再换成功色或强调色。
            SettingsUiHints.SetStatusColor(_weatherTestStatus, UiTheme.TextMuted);

            WeatherService.FetchAsync(city, delegate(WeatherInfo info)
            {
                try
                {
                    // 回调在后台线程，碰控件前必须切回 UI 线程。
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        // 期间用户又测了别的城市，本次结果过期，连按钮都不该再改。
                        if (version != _weatherTestVersion)
                        {
                            return;
                        }
                        // 无论成功失败都恢复按钮，避免一次网络错误把按钮永久锁死。
                        _weatherTestButton.IsEnabled = true;
                        _weatherTestButton.Content = "测试地名";
                        if (info != null)
                        {
                            _weatherTestStatus.Text = "✓ 查询成功：" + info.DisplayText;
                            SettingsUiHints.SetStatusColor(_weatherTestStatus, UiTheme.Success);
                        }
                        else
                        {
                            _weatherTestStatus.Text = "✕ 查询失败：未找到该地名，或网络暂时不可用。";
                            SettingsUiHints.SetStatusColor(_weatherTestStatus, UiTheme.Accent);
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                    // 窗口已关闭、Dispatcher 已停止时 BeginInvoke 会抛此异常；
                    // 此时没有界面可更新，也没有资源需要释放，可以安全吞掉。
                }
            });
        }

        // ===== 颜色选择 =====

        /// <summary>
        /// 打开 WinForms 颜色对话框（WPF 没有自带取色器），以当前窗口为模态宿主。
        /// 确认后统一交给 <see cref="SelectWeatherColor"/> 落值，与预设色走同一条路径。
        /// </summary>
        private void OnPickWeatherColor(object sender, RoutedEventArgs e)
        {
            using (WinForms.ColorDialog dialog = new WinForms.ColorDialog())
            {
                // FullOpen 直接展开调色板，AnyColor 允许自定义任意颜色。
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                // 初始色取当前配置，用户取消时不会产生任何改动。
                Color current = _working.WeatherColor;
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

                // WinForms 模态对话框需要 IWin32Window 宿主，这里用窗口句柄包一层。
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                WinForms.IWin32Window owner = new SimpleWin32Window(handle);
                if (dialog.ShowDialog(owner) == WinForms.DialogResult.OK)
                {
                    SelectWeatherColor(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
                }
            }
        }

        /// <summary>
        /// 选中一个天气颜色：只保存十六进制串，随后刷新色块与预设描边，并重绘预览。
        /// </summary>
        /// <param name="color">要应用的颜色。</param>
        private void SelectWeatherColor(Color color)
        {
            // 配置里存字符串而不是 Color，保证 JSON 可读、可手工修改。
            _working.WeatherColorHex = ColorUtil.ToHex(color);
            // 色块、十六进制文本与预设选中态必须一起更新，否则界面会自相矛盾。
            UpdateWeatherColorUi();
            UpdatePreview();
            MarkDirty();
        }

        /// <summary>
        /// 按当前颜色刷新色块、十六进制文本与预设描边。
        /// 选中判断比较十六进制串并忽略大小写，避免 <c>#ff3b30</c> 与 <c>#FF3B30</c> 误判。
        /// </summary>
        private void UpdateWeatherColorUi()
        {
            Color color = _working.WeatherColor;
            _weatherColorSwatch.Background = UiTheme.BrushFrom(color);
            _weatherColorHexText.Text = ColorUtil.ToHex(color);

            for (int i = 0; i < _weatherColorPresets.Count; i++)
            {
                ColorPreset preset = _weatherColorPresets[i];
                bool selected = string.Equals(
                    ColorUtil.ToHex(preset.Color),
                    _working.WeatherColorHex,
                    StringComparison.OrdinalIgnoreCase);
                // 选中色块加粗描边并换成主文字色，未选中恢复默认细边。
                preset.Swatch.BorderThickness = new Thickness(selected ? 3 : 1);
                preset.Swatch.BorderBrush = selected
                    ? UiTheme.TextPrimary
                    : UiTheme.Frozen(0x55, 0x55, 0x60);
            }
        }

        // ===== 格式校验 =====

        /// <summary>
        /// 天气格式输入变化：留空表示使用默认格式；非空则按简洁变量语法校验后才写回。
        /// 校验失败保留上一个有效格式，只把错误文案写到状态行。
        /// </summary>
        private void OnWeatherFormatTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            string text = (_weatherFormatBox.Text ?? string.Empty).Trim();
            // 空格式是合法状态，明确存空串，渲染时再退回默认格式。
            if (text.Length == 0)
            {
                _working.WeatherFormat = string.Empty;
                UpdateWeatherFormatStatus();
                UpdatePreview();
                MarkDirty();
                return;
            }

            string normalized;
            string error;
            // 天气只占一行，第二个参数传 false 禁止格式里出现换行。
            if (FormatEngine.TryNormalizeTokenFormat(text, false, out normalized, out error))
            {
                _working.WeatherFormat = normalized;
                UpdateWeatherFormatStatus();
                UpdatePreview();
            }
            else
            {
                // 非法输入不覆盖已保存的格式，状态行改用强调色，保证错误始终可见。
                _weatherFormatStatus.Text = error;
                SettingsUiHints.SetStatusColor(_weatherFormatStatus, UiTheme.Accent);
            }
            // 合法与否都算用户编辑过，标记脏才能让「应用」保存这次改动。
            MarkDirty();
        }

        /// <summary>
        /// 刷新天气格式状态行：空格式说明会退回默认；非空则用当前时区时间与示例天气生成预览。
        /// </summary>
        private void UpdateWeatherFormatStatus()
        {
            if (string.IsNullOrWhiteSpace(_working.WeatherFormat))
            {
                _weatherFormatStatus.Text = "留空使用默认格式：城市 · 天气 · 温度 · 今日高低温。";
                SettingsUiHints.SetStatusColor(_weatherFormatStatus, UiTheme.TextMuted);
                return;
            }

            // 预览必须走与真实渲染相同的时区与格式化路径，避免预览和实际显示不一致。
            DateTime now = TimeZoneCatalog.GetNow(TimeZoneCatalog.GetZone(_working.TimeZoneId));
            string example = FormatEngine.Format(now, CreateSampleWeather(), _working.WeatherFormat);
            _weatherFormatStatus.Text = "预览：" + example;
            SettingsUiHints.SetStatusColor(_weatherFormatStatus, UiTheme.TextMuted);
        }

        // ===== 内部类型 =====

        /// <summary>
        /// 把 WPF 窗口句柄包装成 WinForms 的 <see cref="WinForms.IWin32Window"/>，
        /// 供颜色对话框指定模态宿主；只转发句柄，不持有窗口引用。
        /// </summary>
        private sealed class SimpleWin32Window : WinForms.IWin32Window
        {

            /// <summary>原生窗口句柄，构造后不再变化。</summary>
            private readonly IntPtr _handle;

            /// <summary>用给定句柄创建包装对象。</summary>
            /// <param name="handle">WPF 窗口的 HWND。</param>
            public SimpleWin32Window(IntPtr handle)
            {
                _handle = handle;
            }

            /// <summary>WinForms 需要的句柄属性，直接返回构造时保存的值。</summary>
            public IntPtr Handle
            {
                get { return _handle; }
            }
        }
    }
}
