namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public readonly struct FloatingGadgetChangedMessage(FloatingGadgetState state) : IMessage
{
    public FloatingGadgetState State { get; } = state;
}
