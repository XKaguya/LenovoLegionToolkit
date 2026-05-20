using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class ITSModeAutomationStep(ITSMode state)
    : AbstractFeatureAutomationStep<ITSMode>(state)
{
    public override IAutomationStep DeepCopy() => new ITSModeAutomationStep(State);

    public override async Task RunAsync(AutomationContext context, AutomationEnvironment environment,
        CancellationToken token)
    {
        var feature = IoCContainer.Resolve<ITSModeFeature>();
        var currentState = await feature.GetStateAsync().ConfigureAwait(false);
        if (!State.Equals(currentState))
            await feature.SetStateAsync(State).ConfigureAwait(false);

        var listener = IoCContainer.Resolve<ITSModeListener>();
        await listener.OnChangedAsync(State).ConfigureAwait(false);
    }
}
