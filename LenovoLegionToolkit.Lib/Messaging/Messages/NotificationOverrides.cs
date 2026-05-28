namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public class NotificationOverrides
{
    public int? IconOverride { get; set; }
    public RGBColor? ColorOverride { get; set; }
    public RGBColor? TextColorOverride { get; set; }
    public NotificationPosition? PositionOverride { get; set; }
    public NotificationDuration? DurationOverride { get; set; }
}
