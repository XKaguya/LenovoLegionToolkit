namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public readonly struct OsdChangedMessage(OsdState state) : IMessage
{
    public OsdState State { get; } = state;
}
