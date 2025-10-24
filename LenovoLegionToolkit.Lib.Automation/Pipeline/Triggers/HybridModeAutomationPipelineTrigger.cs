using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

[method: JsonConstructor]
public class HybridModeAutomationPipelineTrigger(HybridModeState hybridModeState) : IHybridModeAutomationPipelineTrigger
{
    public string DisplayName => Resource.HybridModeAutomationPipelineTrigger_DisplayName;

    public HybridModeState HybridModeState { get; } = hybridModeState;

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is not HybridModeAutomationEvent e)
            return Task.FromResult(false);

        var result = e.HybridModeState == HybridModeState;
        return Task.FromResult(result);
    }

    public async Task<bool> IsMatchingState()
    {
        var feature = IoCContainer.Resolve<HybridModeFeature>();
        return await feature.GetStateAsync().ConfigureAwait(false) == HybridModeState;
    }

    public void UpdateEnvironment(AutomationEnvironment environment) => environment.HybridMode = HybridModeState;

    public IAutomationPipelineTrigger DeepCopy() => new HybridModeAutomationPipelineTrigger(HybridModeState);

    public IHybridModeAutomationPipelineTrigger DeepCopy(HybridModeState hybridModeState) => new HybridModeAutomationPipelineTrigger(hybridModeState);

    public override bool Equals(object? obj)
    {
        return obj is HybridModeAutomationPipelineTrigger t && HybridModeState == t.HybridModeState;
    }

    public override int GetHashCode() => HashCode.Combine(HybridModeState);

    public override string ToString() => $"{nameof(HybridModeState)}: {HybridModeState}";
}
