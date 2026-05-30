namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public readonly struct OsdChangedMessage(ToggleState state) : IMessage
{
    public ToggleState State { get; } = state;
}
