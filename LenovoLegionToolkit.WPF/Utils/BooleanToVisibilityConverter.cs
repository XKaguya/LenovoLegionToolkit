using System.Windows;

namespace LenovoLegionToolkit.WPF.Utils
{
    public static class BooleanToVisibilityConverter
    {
        public static Visibility Convert(bool value)
        {
            return value ? Visibility.Visible : Visibility.Collapsed;
        }

        public static bool ConvertBack(Visibility value)
        {
            return value == Visibility.Visible;
        }
    }
}