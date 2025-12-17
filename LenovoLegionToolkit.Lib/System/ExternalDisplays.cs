using System.Linq;
using System.Threading.Tasks;
using WindowsDisplayAPI;

namespace LenovoLegionToolkit.Lib.System;

public static class ExternalDisplays
{
    public static async Task<Display[]> GetAsync()
    {
        var internalDisplay = await InternalDisplay.GetAsync().ConfigureAwait(false);

        var allDisplays = await Task.Run(Display.GetDisplays).ConfigureAwait(false);

        return allDisplays.Where(d => d.DevicePath != internalDisplay?.DevicePath).ToArray();
    }
}
