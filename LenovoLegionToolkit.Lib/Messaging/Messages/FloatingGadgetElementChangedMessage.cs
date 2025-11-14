using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public readonly struct FloatingGadgetElementChangedMessage(List<FloatingGadgetItem> items) : IMessage
{
    public List<FloatingGadgetItem> Items { get; } = items;
}
