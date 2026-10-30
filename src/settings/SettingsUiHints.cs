// 设置界面的灰色提示文字总开关。
// 设置窗口各页签里的灰色说明文字都从 SettingsUiHints.Hint 创建，改 ShowHints 一处
// 即可整体关闭，无需逐页删代码；关闭时文字块直接折叠，不占布局空间。
// 约束：开关只在创建控件时读取，运行期改值不会重排已建好的界面；重新打开窗口才生效。
// 状态行（格式校验、天气测试等）用 SetStatusColor 改色，灰色视为“无内容引导语”，
// 只有变成强调色/成功色/失败色时才显示，避免关掉提示后连错误也看不见。

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameClock
{
    /// <summary>设置界面提示文字的集中开关与工厂。</summary>
    internal static class SettingsUiHints
    {
        /// <summary>灰色提示总开关；false = 全部关闭（默认）。</summary>
        public static bool ShowHints = false;

        /// <summary>
        /// 创建一条灰色提示文字。关闭时返回已折叠的文字块，调用方仍可照常设置边距、
        /// 换行与文本，只是不会出现在界面上、也不占据高度。
        /// </summary>
        public static TextBlock Hint(string text, double fontSize, Brush color, FontWeight weight)
        {
            TextBlock block = UiTheme.MakeText(text, fontSize, color, weight);
            // 关闭提示时把文字块折叠起来，保留对象以免调用方后续设置属性时抛空引用。
            if (!ShowHints)
            {
                block.Visibility = Visibility.Collapsed;
            }

            return block;
        }

        /// <summary>
        /// 状态行改色并同步显隐：灰色是引导语，总开关关闭时保持折叠；
        /// 强调色/成功色/失败色代表真实结果，始终显示，保证校验与测试结果不被误关。
        /// </summary>
        public static void SetStatusColor(TextBlock block, Brush color)
        {
            // 允许控件尚未初始化时直接跳过，避免分部类构造顺序不同导致空引用。
            if (block == null)
            {
                return;
            }

            block.Foreground = color;
            bool isGuideText = ReferenceEquals(color, UiTheme.TextMuted);
            block.Visibility = (ShowHints || !isGuideText) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
