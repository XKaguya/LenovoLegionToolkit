using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public readonly struct DashboardElementChangedMessage(SensorItem[] items) : IMessage
{
    public SensorItem[] Items { get; } = items;
}
