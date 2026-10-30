using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GameClock
{
    internal static class SvgIcons
    {
        // SVG path data (24x24 viewBox) for the action icons used by the schedule list.
        public const string Power =
            "M16.56,5.44L15.11,6.89C16.84,7.94 18,9.83 18,12A6,6 0 0,1 12,18A6,6 0 0,1 6,12C6,9.83 7.16,7.94 8.88,6.88L7.44,5.44C5.36,6.88 4,9.28 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12C20,9.28 18.64,6.88 16.56,5.44M11,3V13H13V3H11Z";

        public const string Trash =
            "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z";

        public const string Pencil =
            "M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z";

        public const string Plus =
            "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z";

        public const string Search =
            "M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z";

        public static Path Create(string pathData, Brush fill, double size)
        {
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
