using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class CycleGodModePresetAutomationStep() : IAutomationStep
{
    private readonly PowerModeFeature _feature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly GodModeController _controller = IoCContainer.Resolve<GodModeController>();

    public async Task<bool> IsSupportedAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return mi.Properties.SupportsGodMode;
    }

    public async Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        var state = await _controller.GetStateAsync().ConfigureAwait(false);
        if (state.Presets.Count < 2)
            return;

        var presetIds = state.Presets.Keys.ToList();
        var currentIndex = presetIds.IndexOf(state.ActivePresetId);
        
        if (currentIndex == -1)
            currentIndex = 0;

        var nextIndex = (currentIndex + 1) % presetIds.Count;
        var targetPresetId = presetIds[nextIndex];
        var presetName = state.Presets[targetPresetId].Name;

        var newState = state with { ActivePresetId = targetPresetId };

        await _controller.SetStateAsync(newState).ConfigureAwait(false);

        if (await _feature.GetStateAsync().ConfigureAwait(false) == PowerModeState.GodMode)
            await _controller.ApplyStateAsync().ConfigureAwait(false);

        context.LastRunOutput = presetName;
    }

    public IAutomationStep DeepCopy() => new CycleGodModePresetAutomationStep();
}
