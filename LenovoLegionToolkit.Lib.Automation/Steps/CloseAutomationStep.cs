using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class CloseAutomationStep(Close state)
    : IAutomationStep<Close>
{
    public Close State { get; } = state;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task<Close[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<Close>());

    public IAutomationStep DeepCopy() => new CloseAutomationStep(State);

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        Environment.Exit(0);
        return Task.CompletedTask;
    }
}
