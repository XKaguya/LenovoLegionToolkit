using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Features.Hybrid;

public class BiosGPUModeFeature : IFeature<HybridModeState>
{
    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            if (mi.LegionSeries == LegionSeries.ThinkBook)
            {
                var result = await WMI.LenovoBiosSetting.GetBiosSelections("GraphicsDevice").ConfigureAwait(false);
                if (result.Contains("UMA"))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<HybridModeState[]> GetAllStatesAsync()
    {
        var supported = new List<HybridModeState>();

        var selections = await WMI.LenovoBiosSetting.GetBiosSelections("GraphicsDevice").ConfigureAwait(false);
        if (selections.Contains("UMA Graphics"))
            supported.Add(HybridModeState.UMA);
        if (selections.Contains("Discrete Graphics"))
            supported.Add(HybridModeState.Off);
        if (selections.Contains("Switchable Graphics"))
        {
            supported.Add(HybridModeState.On);
            supported.Add(HybridModeState.OnIGPUOnly);
            supported.Add(HybridModeState.OnAuto);
        }

        return supported.ToArray();
    }

    public async Task<HybridModeState> GetStateAsync()
    {
        var biosMode = await WMI.LenovoBiosSetting.GetBiosSetting("GraphicsDevice").ConfigureAwait(false);

        return biosMode switch
        {
            "UMA Graphics" => HybridModeState.UMA,
            "Discrete Graphics" => HybridModeState.Off,
            "Switchable Graphics" => HybridModeState.OnAuto,
            _ => throw new InvalidOperationException($"Unknown BIOS GraphicsDevice: {biosMode}")
        };
    }

    public async Task SetStateAsync(HybridModeState state)
    {
        switch (state)
        {
            case HybridModeState.UMA:
                await WMI.LenovoBiosSetting.SetBiosSetting("GraphicsDevice", "UMA Graphics").ConfigureAwait(false);
                await WMI.LenovoBiosSetting.SaveBiosSetting().ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unsupported state");
        }
    }

}