using System.Windows.Media;
using LenovoLegionToolkit.Lib;

namespace LenovoLegionToolkit.WPF.Extensions;

public static class ITSModeStateExtensions
{
    public static SolidColorBrush GetSolidColorBrush(this ITSMode itsMode) => new(itsMode switch
    {
        ITSMode.ItsAuto => Color.FromRgb(53, 123, 242),
        ITSMode.MmcCool => Colors.White,
        ITSMode.MmcPerformance => Color.FromRgb(212, 51, 51),
        ITSMode.MmcGeek => Color.FromRgb(99, 52, 227),
        _ => Colors.Transparent,
    });
}
