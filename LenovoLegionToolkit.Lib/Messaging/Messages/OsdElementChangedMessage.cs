using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public readonly struct OsdElementChangedMessage(List<OsdItem> items) : IMessage
{
    public List<OsdItem> Items { get; } = items;
}
