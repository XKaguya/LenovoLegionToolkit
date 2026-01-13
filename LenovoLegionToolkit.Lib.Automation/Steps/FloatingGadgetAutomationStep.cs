using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class FloatingGadgetAutomationStep(FloatingGadgetState state)
    : IAutomationStep<FloatingGadgetState>
{
    public FloatingGadgetState State { get; } = state;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task<FloatingGadgetState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<FloatingGadgetState>());

    public IAutomationStep DeepCopy() => new FloatingGadgetAutomationStep(State);

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        MessagingCenter.Publish(new FloatingGadgetChangedMessage(State));
        return Task.CompletedTask;
    }
}
