using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class OsdAutomationStep(OsdState state)
    : IAutomationStep<OsdState>
{
    public OsdState State { get; } = state;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task<OsdState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<OsdState>());

    public IAutomationStep DeepCopy() => new OsdAutomationStep(State);

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        MessagingCenter.Publish(new OsdChangedMessage(State));
        return Task.CompletedTask;
    }
}
