using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class ITSModeAutomationStep(ITSMode state)
    : AbstractFeatureAutomationStep<ITSMode>(state)
{
    public override IAutomationStep DeepCopy() => new ITSModeAutomationStep(State);
}
