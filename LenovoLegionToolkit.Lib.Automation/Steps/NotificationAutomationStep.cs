using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class NotificationAutomationStep(string? text)
    : IAutomationStep
{
    public string? Text { get; } = text;

    public int? IconOverride { get; set; }
    public RGBColor? ColorOverride { get; set; }
    public RGBColor? TextColorOverride { get; set; }
    public NotificationPosition? PositionOverride { get; set; }
    public NotificationDuration? DurationOverride { get; set; }

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(Text))
        {
            var text = Text.Replace("$RUN_OUTPUT$", context.LastRunOutput);
            var overrides = new NotificationOverrides
            {
                IconOverride = IconOverride,
                ColorOverride = ColorOverride,
                TextColorOverride = TextColorOverride,
                PositionOverride = PositionOverride,
                DurationOverride = DurationOverride
            };
            MessagingCenter.Publish(new NotificationMessage(NotificationType.AutomationNotification, text) { Overrides = overrides });
        }

        return Task.CompletedTask;
    }

    IAutomationStep IAutomationStep.DeepCopy() => new NotificationAutomationStep(Text)
    {
        IconOverride = IconOverride,
        ColorOverride = ColorOverride,
        TextColorOverride = TextColorOverride,
        PositionOverride = PositionOverride,
        DurationOverride = DurationOverride
    };
}
