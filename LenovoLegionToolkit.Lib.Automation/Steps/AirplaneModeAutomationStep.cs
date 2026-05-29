using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LenovoLegionToolkit.Lib.System;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class AirplaneModeAutomationStep(ToggleState state) : IAutomationStep<ToggleState>
{
    public ToggleState State { get; } = state;

    public bool IsDangerousOnStartup => true;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        switch (State)
        {
            case ToggleState.On:
                AirplaneMode.TurnOn();
                break;
            case ToggleState.Off:
                AirplaneMode.TurnOff();
                break;
            case ToggleState.Toggle:
                AirplaneMode.Toggle();
                break;
        }
        
        return Task.CompletedTask;
    }

    public Task<ToggleState[]> GetAllStatesAsync() => Task.FromResult(new[] { ToggleState.Off, ToggleState.On, ToggleState.Toggle });

    public IAutomationStep DeepCopy() => new AirplaneModeAutomationStep(State);
}
