using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

public class OverclockDiscreteGPUAutomationStep(ToggleState state)
    : IAutomationStep<ToggleState>
{
    private readonly GPUOverclockController _controller = IoCContainer.Resolve<GPUOverclockController>();

    public ToggleState State { get; } = state;

    public Task<ToggleState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<ToggleState>());

    public Task<bool> IsSupportedAsync() => _controller.IsSupportedAsync();

    public async Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        if (!await _controller.IsSupportedAsync().ConfigureAwait(false))
            return;

        var (isEnabled, info) = _controller.GetState();

        bool targetState = State switch
        {
            ToggleState.On => true,
            ToggleState.Off => false,
            ToggleState.Toggle => !isEnabled,
            _ => isEnabled
        };

        _controller.SaveState(targetState, info);

        await _controller.ApplyStateAsync(true).ConfigureAwait(false);
    }

    IAutomationStep IAutomationStep.DeepCopy() => new OverclockDiscreteGPUAutomationStep(State);
}
