using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

// 日程列表用到的矢量图标：以 24×24 viewBox 的 SVG path 字符串形式内联，由 Create 生成 WPF Path。
// 边界：不读文件、不做资源字典查找；图标数据是编译期常量，改名或改 path 会直接影响界面显示。
// 这样做的原因：项目没有 XAML/资源字典，内联 path 可避免 pack URI 与文件复制路径失效的问题。
// 新增图标时沿用 24×24 坐标系，颜色与尺寸交给调用方，本类不绑定主题色。
namespace GameClock
{
    /// <summary>
    /// 内置 SVG 图标的 path 数据与 Path 工厂；坐标统一为 24×24，填充色与显示尺寸由调用方决定。
    /// </summary>
    internal static class SvgIcons
    {
        /// <summary>电源图标，用于日程的启用/停用操作。</summary>
        public const string Power =
            "M16.56,5.44L15.11,6.89C16.84,7.94 18,9.83 18,12A6,6 0 0,1 12,18A6,6 0 0,1 6,12C6,9.83 7.16,7.94 8.88,6.88L7.44,5.44C5.36,6.88 4,9.28 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12C20,9.28 18.64,6.88 16.56,5.44M11,3V13H13V3H11Z";

        /// <summary>垃圾桶图标，用于删除日程。</summary>
        public const string Trash =
            "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z";

        /// <summary>铅笔图标，用于编辑日程。</summary>
        public const string Pencil =
            "M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z";

        /// <summary>加号图标，用于「添加日程」按钮。</summary>
        public const string Plus =
            "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z";

        /// <summary>放大镜图标，用于日程搜索框。</summary>
        public const string Search =
            "M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z";

        /// <summary>
        /// 按 path 数据创建固定尺寸的图标 Path：等比缩放（Stretch=Uniform）并居中。
        /// </summary>
        /// <param name="pathData">SvgIcons 提供的 path 字符串（24×24 坐标系）。</param>
        /// <param name="fill">填充画刷，一般用主题文字色或强调色。</param>
        /// <param name="size">图标边长（像素），调用方通常传 15–16。</param>
        /// <returns>可放进按钮 Content 或容器 Children 的 Path。</returns>
        public static Path Create(string pathData, Brush fill, double size)
        {
            // 空 path 说明常量丢失或调用方拼错：Geometry.Parse 会抛异常，先留日志线索；
            // 异常仍照常抛出，不在此处吞掉，避免图标静默消失更难排查。
            if (string.IsNullOrEmpty(pathData))
            {
                AppLog.Warn("Icon", "图标 path 数据为空，无法创建图标");
            }

            Path path = new Path();
            path.Data = Geometry.Parse(pathData);
            path.Fill = fill;
            path.Stretch = Stretch.Uniform;
            path.Width = size;
            path.Height = size;
            path.HorizontalAlignment = HorizontalAlignment.Center;
            path.VerticalAlignment = VerticalAlignment.Center;
            path.IsHitTestVisible = false;
            return path;
        }
    }
}
