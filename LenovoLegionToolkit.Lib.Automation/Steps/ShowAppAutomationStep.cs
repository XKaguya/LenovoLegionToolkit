using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

public class ShowAppAutomationStep : IAutomationStep
{
    public bool IsDangerousOnStartup => false;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public IAutomationStep DeepCopy() => new ShowAppAutomationStep();

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        MessagingCenter.Publish(new ShowAppMessage());
        return Task.CompletedTask;
    }
}
