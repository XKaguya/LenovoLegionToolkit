using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

public class GodModeController(GodModeControllerV1 controllerV1, GodModeControllerV2 controllerV2, GodModeControllerV3 controllerV3, GodModeControllerV4 controllerV4)
    : IGodModeController
{
    public IGodModeController? Controller { get; private set; } = null;
    private IGodModeController ControllerV1 => controllerV1;
    private IGodModeController ControllerV2 => controllerV2;
    private IGodModeController ControllerV3 => controllerV3;
    private IGodModeController ControllerV4 => controllerV4;

    public event EventHandler<Guid>? PresetChanged
    {
        add
        {
            ControllerV1.PresetChanged += value;
            ControllerV2.PresetChanged += value;
            ControllerV3.PresetChanged += value;
            ControllerV4.PresetChanged += value;
        }
        remove
        {
            ControllerV1.PresetChanged -= value;
            ControllerV2.PresetChanged -= value;
            ControllerV3.PresetChanged -= value;
            ControllerV4.PresetChanged -= value;
        }
    }

    public async Task<bool> NeedsVantageDisabledAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.NeedsVantageDisabledAsync().ConfigureAwait(false);
    }

    public async Task<bool> NeedsLegionZoneDisabledAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.NeedsLegionZoneDisabledAsync().ConfigureAwait(false);
    }

    public async Task<bool> NeedsLegionSpaceDisabledAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.NeedsLegionZoneDisabledAsync().ConfigureAwait(false);
    }

    public async Task<(Guid, GodModeSettings.GodModeSettingsStore.Preset)> GetActivePresetAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.GetActivePresetAsync().ConfigureAwait(false);
    }

    public async Task<Guid> GetActivePresetIdAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.GetActivePresetIdAsync().ConfigureAwait(false);
    }

    public async Task<string?> GetActivePresetNameAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.GetActivePresetNameAsync().ConfigureAwait(false);
    }

    public async Task<Dictionary<Guid, GodModeSettings.GodModeSettingsStore.Preset>> GetGodModePresetsAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.GetGodModePresetsAsync().ConfigureAwait(false);
    }

    public async Task<GodModeState> GetStateAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.GetStateAsync().ConfigureAwait(false);
    }

    public async Task SetStateAsync(GodModeState state)
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        await controller.SetStateAsync(state).ConfigureAwait(false);
    }

    public async Task ApplyStateAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        await controller.ApplyStateAsync().ConfigureAwait(false);
    }

    public async Task<FanTable> GetDefaultFanTableAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.GetDefaultFanTableAsync().ConfigureAwait(false);
    }

    public async Task<FanTable> GetMinimumFanTableAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.GetMinimumFanTableAsync().ConfigureAwait(false);
    }

    public async Task<Dictionary<PowerModeState, GodModeDefaults>> GetDefaultsInOtherPowerModesAsync()
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        return await controller.GetDefaultsInOtherPowerModesAsync().ConfigureAwait(false);
    }

    public async Task RestoreDefaultsInOtherPowerModeAsync(PowerModeState state)
    {
        var controller = await GetControllerAsync().ConfigureAwait(false);
        await controller.RestoreDefaultsInOtherPowerModeAsync(state).ConfigureAwait(false);
    }

    public async Task<IGodModeController> GetControllerAsync()
    {
        if (Controller != null)
        {
            return Controller;
        }

        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        if (mi.Properties.SupportsGodModeV1)
        {
            Controller = controllerV1;
            return controllerV1;
        }

        if (mi.Properties.SupportsGodModeV2)
        {
            Controller = controllerV2;
            return controllerV2;
        }

        if (mi.Properties.SupportsGodModeV3)
        {
            Controller = controllerV3;
            return controllerV3;
        }

        if (mi.Properties.SupportsGodModeV4)
        {
            Controller = controllerV4;
            return controllerV4;
        }

        throw new InvalidOperationException("No supported version found");
    }
}
