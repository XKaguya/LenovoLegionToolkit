using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System.Management;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class FanMaxSpeedAutomationStep(ToggleState state)
    : IAutomationStep<ToggleState>
{
    public ToggleState State { get; } = state;

    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task<ToggleState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<ToggleState>());

    public IAutomationStep DeepCopy() => new FanMaxSpeedAutomationStep(State);

    public async Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        var controller = await _godModeController.GetControllerAsync().ConfigureAwait(false);

        var typeName = controller.GetType().Name;

        bool? applied = typeName switch
        {
            "GodModeControllerV1" => await HandleLegacyAsync().ConfigureAwait(false),
            "GodModeControllerV2" or "GodModeControllerV3" or "GodModeControllerV4" => await HandleModernAsync().ConfigureAwait(false),
            _ => null
        };

        if (applied.HasValue)
            await TryUpdatePresetAsync(applied.Value).ConfigureAwait(false);
    }

    private async Task<bool> HandleLegacyAsync()
    {
        bool currentSpeed = await WMI.LenovoFanMethod.FanGetFullSpeedAsync().ConfigureAwait(false);
        bool targetState = State switch
        {
            ToggleState.On => true,
            ToggleState.Off => false,
            ToggleState.Toggle => !currentSpeed,
            _ => currentSpeed
        };

        if (currentSpeed != targetState)
            await WMI.LenovoFanMethod.FanSetFullSpeedAsync(targetState ? 1 : 0).ConfigureAwait(false);

        return targetState;
    }

    private async Task<bool> HandleModernAsync()
    {
        var currentValue = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.FanFullSpeed).ConfigureAwait(false);

        var targetValue = State switch
        {
            ToggleState.On => 1,
            ToggleState.Off => 0,
            ToggleState.Toggle => currentValue == 0 ? 1 : 0,
            _ => currentValue
        };

        if (currentValue != targetValue)
            await WMI.LenovoOtherMethod.SetFeatureValueAsync(CapabilityID.FanFullSpeed, targetValue).ConfigureAwait(false);

        return targetValue != 0;
    }

    private async Task TryUpdatePresetAsync(bool targetState)
    {
        var state = await _godModeController.GetStateAsync().ConfigureAwait(false);
        var activePresetId = state.ActivePresetId;
        var preset = state.Presets[activePresetId];

        if (preset.FanFullSpeed is null)
            return;

        var updatedPreset = preset with { FanFullSpeed = targetState };
        var updatedPresets = new Dictionary<Guid, GodModePreset>(state.Presets)
        {
            [activePresetId] = updatedPreset
        };

        await _godModeController.SetStateAsync(new GodModeState
        {
            ActivePresetId = activePresetId,
            Presets = updatedPresets.AsReadOnlyDictionary()
        }).ConfigureAwait(false);
    }
}