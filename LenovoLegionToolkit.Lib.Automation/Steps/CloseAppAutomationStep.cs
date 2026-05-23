using System;
using System.Threading;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

public class CloseAppAutomationStep : IAutomationStep
{
    public bool IsDangerousOnStartup => true;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public IAutomationStep DeepCopy() => new CloseAppAutomationStep();

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        Environment.Exit(0);
        return Task.CompletedTask;
    }
}
