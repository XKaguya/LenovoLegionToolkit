using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using WindowsDisplayAPI;
using WindowsDisplayAPI.Native.DeviceContext;

namespace LenovoLegionToolkit.Lib.Features;

public class RefreshRateFeature : IFeature<RefreshRate>
{
    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public async Task<RefreshRate[]> GetAllStatesAsync()
    {
        Log.Instance.Trace($"Getting all refresh rates...");

        var display = await InternalDisplay.GetAsync().ConfigureAwait(true);
        if (display is null)
        {
            Log.Instance.Trace($"Built in display not found");

            return [];
        }

        Log.Instance.Trace($"Built in display found: {display}");

        var currentSettings = display.CurrentSetting;

        Log.Instance.Trace($"Current built in display settings: {currentSettings.ToExtendedString()}");

        var result = display.GetPossibleSettings()
            .Where(dps => Match(dps, currentSettings))
            .Select(dps => dps.Frequency)
            .Distinct()
            .OrderBy(freq => freq)
            .Select(freq => new RefreshRate(freq))
            .ToArray();

        Log.Instance.Trace($"Possible refresh rates are {string.Join(", ", result)}");

        return result;
    }

    public async Task<RefreshRate> GetStateAsync()
    {
        Log.Instance.Trace($"Getting current refresh rate...");

        var display = await InternalDisplay.GetAsync().ConfigureAwait(true);
        if (display is null)
        {
            Log.Instance.Trace($"Built in display not found");

            return default(RefreshRate);
        }

        var currentSettings = display.CurrentSetting;
        var result = new RefreshRate(currentSettings.Frequency);

        Log.Instance.Trace($"Current refresh rate is {result} [currentSettings={currentSettings.ToExtendedString()}]");

        return result;
    }

    public async Task SetStateAsync(RefreshRate state)
    {
        var display = await InternalDisplay.GetAsync().ConfigureAwait(true);
        if (display is null)
        {
            Log.Instance.Trace($"Built in display not found");
            throw new InvalidOperationException("Built in display not found");
        }

        var currentSettings = display.CurrentSetting;

        Log.Instance.Trace($"Current built in display settings: {currentSettings.ToExtendedString()}");

        if (currentSettings.Frequency == state.Frequency)
        {
            Log.Instance.Trace($"Frequency already set to {state.Frequency}");
            return;
        }

        var possibleSettings = display.GetPossibleSettings();
        var newSettings = possibleSettings
            .Where(dps => Match(dps, currentSettings))
            .Where(dps => dps.Frequency == state.Frequency)
            .Select(dps => new DisplaySetting(dps, currentSettings.Position, currentSettings.Orientation, DisplayFixedOutput.Default))
            .FirstOrDefault();

        if (newSettings is not null)
        {
            Log.Instance.Trace($"Setting display to {newSettings.ToExtendedString()}...");

            display.SetSettingsUsingPathInfo(newSettings);

            Log.Instance.Trace($"Display set to {newSettings.ToExtendedString()}");
        }
        else
        {
            Log.Instance.Trace($"Could not find matching settings for frequency {state}");
        }
    }

    private static bool Match(DisplayPossibleSetting dps, DisplayPossibleSetting ds)
    {
        if (dps.IsTooSmall())
            return false;

        var result = true;
        result &= dps.Resolution == ds.Resolution;
        result &= dps.ColorDepth == ds.ColorDepth;
        result &= dps.IsInterlaced == ds.IsInterlaced;
        return result;
    }
}
