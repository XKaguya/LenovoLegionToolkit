using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class SpeakerVolumeAutomationStep(int volume) 
    : IAutomationStep
{
    private readonly SpeakerFeature _feature = IoCContainer.Resolve<SpeakerFeature>();
    public int Volume { get; } = volume;

    public Task<bool> IsSupportedAsync() => _feature.IsSupportedAsync();

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        return _feature.SetVolumeAsync(Volume);
    }

    IAutomationStep IAutomationStep.DeepCopy() => new SpeakerVolumeAutomationStep(Volume);
}
