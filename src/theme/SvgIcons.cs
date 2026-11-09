// 日程列表操作按钮使用的内联 SVG 图标路径。
// 全部图标统一按 24×24 viewBox 编写，只保存 path 的 d 数据，不读取磁盘文件或资源字典，
// 因此发布时无需附带额外图标文件，也不会因资源缺失导致界面空白。
// 调用方把常量传给 Create，得到已设置填充色与尺寸的 WPF Path；
// 图标本身不含交互语义，命中测试被关闭，点击事件由外层按钮负责。

using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GameClock
{

    /// <summary>
    /// 图标 path 数据的常量集合。每个常量是一段 24×24 viewBox 的 SVG path，
    /// 由 <see cref="Create"/> 转换为 <see cref="Path"/> 后放入按钮内容。
    /// 生命周期：纯静态数据，不持有 WPF 对象，也不做缓存。
    /// </summary>
    internal static class SvgIcons
    {
        // ===== 图标 path 数据 =====

        /// <summary>电源图标：圆环缺口加一条竖线，用于“开机自启”等开关场景。</summary>
        public const string Power =
            "M16.56,5.44L15.11,6.89C16.84,7.94 18,9.83 18,12A6,6 0 0,1 12,18A6,6 0 0,1 6,12C6,9.83 7.16,7.94 8.88,6.88L7.44,5.44C5.36,6.88 4,9.28 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12C20,9.28 18.64,6.88 16.56,5.44M11,3V13H13V3H11Z";

        /// <summary>垃圾桶图标：桶盖加桶身，用于删除日程。</summary>
        public const string Trash =
            "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z";

        /// <summary>铅笔图标：斜置笔杆，用于编辑日程。</summary>
        public const string Pencil =
            "M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z";

        /// <summary>加号图标：横竖两条等长线，用于新增日程。</summary>
        public const string Plus =
            "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z";

        /// <summary>放大镜图标：圆环加斜柄，用于搜索框。</summary>
        public const string Search =
            "M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z";

        // ===== 图标构造 =====

        /// <summary>
        /// 把一段 SVG path 数据转换成固定尺寸的 <see cref="Path"/>。
        /// 填充色与尺寸由调用方决定，图标在布局中居中且不参与命中测试。
        /// </summary>
        /// <param name="pathData">24×24 viewBox 的 SVG path 数据。</param>
        /// <param name="fill">填充画刷，通常来自主题色。</param>
        /// <param name="size">图标的逻辑像素边长，宽高相同。</param>
        /// <returns>可直接放入按钮或面板的 <see cref="Path"/>。</returns>
        public static Path Create(string pathData, Brush fill, double size)
        {
            Path path = new Path();
            // path 数据是编译期常量，解析失败属于代码错误，不在运行期兜底。
            path.Data = Geometry.Parse(pathData);
            path.Fill = fill;
            // Uniform 保证任意 viewBox 比例都缩放到给定边长内，不拉伸变形。
            path.Stretch = Stretch.Uniform;
            path.Width = size;
            path.Height = size;
            // 居中对齐，避免不同图标的视觉重心偏移。
            path.HorizontalAlignment = HorizontalAlignment.Center;
            path.VerticalAlignment = VerticalAlignment.Center;
            // 让点击穿透到外层按钮，图标只负责显示。
            path.IsHitTestVisible = false;
            return path;
        }
    }
}
