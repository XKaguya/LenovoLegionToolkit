using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;

namespace LenovoLegionToolkit.Lib.Utils;

public static class SpecialKeyLedHelper
{
    private static MachineInformation? _cachedMachineInformation;

    public static async Task SetLedAsync(SpecialKeyLedState state)
    {
        try
        {
            _cachedMachineInformation ??= await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            if (_cachedMachineInformation.Value.LegionSeries > LegionSeries.Legion_Legacy)
            {
                await WMI.LenovoUtilityData.SetFeatureAsync(state).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"LED sync failed [state={state}]", ex);
        }
    }
}
