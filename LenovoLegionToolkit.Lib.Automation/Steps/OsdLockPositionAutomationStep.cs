using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class OsdLockPositionAutomationStep(ToggleState state)
    : IAutomationStep<ToggleState>
{
    private readonly OsdSettings _osdSettings = IoCContainer.Resolve<OsdSettings>();

    public ToggleState State { get; } = state;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task<ToggleState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<ToggleState>());

    public IAutomationStep DeepCopy() => new OsdLockPositionAutomationStep(State);

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        bool currentState = _osdSettings.Store.IsLocked;
        bool newState = State switch
        {
            ToggleState.On => true,
            ToggleState.Off => false,
            ToggleState.Toggle => !currentState,
            _ => currentState
        };

        if (currentState != newState)
        {
            _osdSettings.Store.IsLocked = newState;
            _osdSettings.SynchronizeStore();
            MessagingCenter.Publish(new OsdAppearanceChangedMessage());
        }

        return Task.CompletedTask;
    }
}
