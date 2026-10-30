using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GameClock
{
    // 文件职责：悬浮窗与任务栏时钟共用的文字渲染控件——主层文字 + 可选柔和投影 + 可选立体
    // （挤出）效果。布局只由主层文字决定，其余层仅靠 TranslateTransform 偏移，不参与测量。
    // 边界：纯视觉控件，不读 AppSettings、不发事件、不写日志；每次 ApplyStyle 都是一次
    // 重绘，属于高频路径（滚轮缩放时每次变化都会调用），因此这里禁止任何日志输出。
    // 调用方：ClockWindow 与 TaskbarClockWindow，样式值由调用方算好后传入。

    /// <summary>
    /// 文字渲染控件：主层负责布局与前景色，可选投影提升暗背景可读性，
    /// 可选立体层按深度做对角线挤出；所有层共享同一份文字。
    /// </summary>
    internal sealed class LayeredText : Grid
    {
        // 主层：唯一参与布局测量的 TextBlock，字号/字体/颜色都以它为准。
        private readonly TextBlock _main;
        // 挤出层集合：Depth 由深到浅（3→1），越深偏移越大，叠出立体边缘。
        private readonly List<ExtrusionLayer> _layers;

        /// <summary>
        /// 构造：先按 3→1 的深度把挤出层加入视觉树（初始折叠），最后加主层，
        /// 保证主层在最上方显示。此时尚未套用任何样式，文字为空。
        /// </summary>
        public LayeredText()
        {
            // 控件整体居中：外层容器（StackPanel/Border）负责定位，这里只管文字对齐。
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            _layers = new List<ExtrusionLayer>();

            // 深度 3→1 建层：先加深层，后加的浅层自然盖在上面，形成连续的边缘过渡。
            for (int depth = 3; depth >= 1; depth--)
            {
                TextBlock layer = CreateTextBlock();
                // 初始折叠：未开启立体效果时不参与渲染，也不占额外开销。
                layer.Visibility = Visibility.Collapsed;
                // 先给一个零位移的变换，ApplyStyle 时直接替换，避免运行期出现 null 判断。
                layer.RenderTransform = new TranslateTransform(0, 0);
                Children.Add(layer);
                _layers.Add(new ExtrusionLayer(layer, depth));
            }

            // 主层最后加入：视觉树中位于挤出层之上，文字主体不会被边缘色盖住。
            _main = CreateTextBlock();
            Children.Add(_main);
        }

        /// <summary>
        /// 显示文字。赋值时同步到主层与全部挤出层；null 统一归一为空串，
        /// 避免 WPF 对 null Text 抛异常，也避免出现空引用判断分支。
        /// </summary>
        public string Text
        {
            get { return _main.Text; }
            set
            {
                string safe = value ?? string.Empty;
                _main.Text = safe;
                // 逐层同步：挤出层必须与主层文字完全一致，否则偏移后会出现重影。
                for (int i = 0; i < _layers.Count; i++)
                {
                    _layers[i].Text.Text = safe;
                }
            }
        }

        /// <summary>
        /// 套用字体样式：字体族/字号/字重/前景色 + 可选投影 + 可选立体挤出。
        /// 调用方在字号或配色变化时调用（滚轮缩放、设置应用后重建），会触发一次重绘。
        /// </summary>
        public void ApplyStyle(
            FontFamily fontFamily,
            double fontSize,
            FontWeight fontWeight,
            Color color,
            bool showShadow,
            bool show3D)
        {
            // 字体族为空时退回等宽字体：等宽数字在时钟上更稳定，不会因数字宽度跳动。
            FontFamily family = fontFamily ?? new FontFamily("Consolas");

            SolidColorBrush mainBrush = UiTheme.BrushFrom(color);
            _main.FontFamily = family;
            _main.FontSize = fontSize;
            _main.FontWeight = fontWeight;
            _main.Foreground = mainBrush;
            // 关闭时显式置 null：DropShadowEffect 会额外占用一层离屏渲染，不需要就别挂。
            _main.Effect = showShadow ? CreateShadow(color, fontSize) : null;

            // 立体边缘色由前景色向白/黑混合得到：深色文字提亮边缘，浅色文字压暗边缘。
            Color edgeColor = CreateEdgeColor(color);
            SolidColorBrush edgeBrush = UiTheme.BrushFrom(edgeColor);
            // 每像素偏移量：随字号增大而变粗，但夹在 0.9–2.6 px 之间，
            // 小字号不至于糊成一团，大字号也不至于拉开成重影。
            double step = Math.Max(0.9, Math.Min(2.6, fontSize * 0.034));

            // 逐层套用样式并偏移：偏移量 = 单位步长 × 深度，方向固定右下 45°。
            for (int i = 0; i < _layers.Count; i++)
            {
                ExtrusionLayer layer = _layers[i];
                layer.Text.FontFamily = family;
                layer.Text.FontSize = fontSize;
                layer.Text.FontWeight = fontWeight;
                layer.Text.Foreground = edgeBrush;
                // 立体开关：关掉时整体折叠，避免三层透明文字仍参与命中与绘制。
                layer.Text.Visibility = show3D ? Visibility.Visible : Visibility.Collapsed;
                double offset = step * layer.Depth;
                layer.Text.RenderTransform = new TranslateTransform(offset, offset);
            }
        }

        /// <summary>
        /// 创建一个统一的 TextBlock：居中、不拦截鼠标。
        /// 不拦截命中是关键——文字不参与点击/拖动，窗口级手势才能正常落到根元素上。
        /// </summary>
        private static TextBlock CreateTextBlock()
        {
            TextBlock block = new TextBlock();
            block.TextAlignment = TextAlignment.Center;
            block.HorizontalAlignment = HorizontalAlignment.Center;
            block.VerticalAlignment = VerticalAlignment.Center;
            // 文字不参与命中测试：点击穿透由窗口样式决定，控件本身不应吞事件。
            block.IsHitTestVisible = false;
            return block;
        }

        /// <summary>
        /// 生成柔和投影：深色文字用白影、浅色文字用黑影（否则暗底上看不见），
        /// 模糊半径与深度随字号线性放大，保证大字号下阴影不会显得过于锐利。
        /// </summary>
        private static DropShadowEffect CreateShadow(Color color, double fontSize)
        {
            // 亮度阈值 0.22：低于此值视为深色文字，需要亮色阴影衬托。
            bool darkText = ColorUtil.Luminance(color) < 0.22;
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.Color = darkText ? Colors.White : Colors.Black;
            // 315° 即左上方向偏移，光从右下来，符合常见 UI 光照约定。
            shadow.Direction = 315;
            shadow.ShadowDepth = Math.Max(1.0, fontSize * 0.045);
            shadow.BlurRadius = Math.Max(2.0, fontSize * 0.075);
            // 深色文字的白影要更淡（0.55）才不脏；浅色文字的黑影要更实（0.88）才够清晰。
            shadow.Opacity = darkText ? 0.55 : 0.88;
            return shadow;
        }

        /// <summary>
        /// 计算立体边缘色：深色文字向白色混合 48%，浅色文字向黑色混合 50%，
        /// 混合比例刻意接近对半，使边缘既可见又不会抢过主文字。
        /// </summary>
        private static Color CreateEdgeColor(Color color)
        {
            // 深色文字：提亮边缘，形成高光方向感。
            if (ColorUtil.Luminance(color) < 0.22)
            {
                return Blend(color, Colors.White, 0.48);
            }
            // 浅色文字：压暗边缘，避免在亮背景上立体层“消失”。
            return Blend(color, Colors.Black, 0.50);
        }

        /// <summary>
        /// 线性 RGB 混合：amount=0 取 from，=1 取 to；保留原 alpha，不改整体不透明度。
        /// 不用 ScRGB/伽马校正，颜色差异在文字边缘尺度上肉眼不可辨，保持实现简单。
        /// </summary>
        private static Color Blend(Color from, Color to, double amount)
        {
            double inverse = 1.0 - amount;
            byte r = (byte)Math.Round(from.R * inverse + to.R * amount);
            byte g = (byte)Math.Round(from.G * inverse + to.G * amount);
            byte b = (byte)Math.Round(from.B * inverse + to.B * amount);
            return Color.FromArgb(from.A, r, g, b);
        }

        /// <summary>
        /// 单层挤出记录：TextBlock 引用 + 深度（1 最浅、3 最深），
        /// 偏移量在 ApplyStyle 时按深度计算，这里只存不变的身份信息。
        /// </summary>
        private sealed class ExtrusionLayer
        {
            /// <summary>绑定该深度的 TextBlock 与深度值；深度只读，构造后不再变化。</summary>
            public ExtrusionLayer(TextBlock text, int depth)
            {
                Text = text;
                Depth = depth;
            }

            /// <summary>该层的文本控件，样式与位移都由 ApplyStyle 统一设置。</summary>
            public TextBlock Text { get; private set; }
            /// <summary>挤出深度（1–3），决定偏移倍率。</summary>
            public int Depth { get; private set; }
        }
    }
}
