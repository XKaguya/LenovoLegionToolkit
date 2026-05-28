using System.Collections.Generic;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Settings;

public interface INotificationCustomizationStore
{
    Dictionary<NotificationType, int> IconOverrides { get; }
    Dictionary<NotificationType, RGBColor> ColorOverrides { get; }
    Dictionary<NotificationType, RGBColor> TextColorOverrides { get; }
    Dictionary<NotificationType, NotificationPosition> PositionOverrides { get; }
    Dictionary<NotificationType, NotificationDuration> DurationOverrides { get; }

    void SynchronizeStore();
}
