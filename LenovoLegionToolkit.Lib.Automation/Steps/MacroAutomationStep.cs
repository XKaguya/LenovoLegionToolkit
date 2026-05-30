using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Macro;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

public class MacroAutomationStep(ToggleState state) : IAutomationStep<ToggleState>
{
    private readonly MacroController _controller = IoCContainer.Resolve<MacroController>();

    public ToggleState State { get; } = state;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task<ToggleState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<ToggleState>());

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        bool targetState = State switch
        {
            ToggleState.On => true,
            ToggleState.Off => false,
            ToggleState.Toggle => !_controller.IsEnabled,
            _ => throw new ArgumentOutOfRangeException(nameof(State), State, null)
        };

        _controller.SetEnabled(targetState);
        return Task.CompletedTask;
    }

    public IAutomationStep DeepCopy() => new MacroAutomationStep(State);

}
