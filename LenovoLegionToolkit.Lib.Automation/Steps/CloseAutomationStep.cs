using System;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

public class CloseAutomationStep : IAutomationStep
{
    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public IAutomationStep DeepCopy() => new CloseAutomationStep();

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        Environment.Exit(0);
        return Task.CompletedTask;
    }
}
